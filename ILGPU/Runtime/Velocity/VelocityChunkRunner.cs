// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityChunkRunner.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace ILGPU.Runtime.Velocity
{
    /// <summary>
    /// Runs the chunks of a kernel launch (groups without barriers) on worker threads
    /// with a large stack. A Velocity kernel is one JIT-compiled method whose frame holds
    /// a warp-wide local for every value of the kernel: a big, fully inlined kernel
    /// needs more than the 1 MB of a default thread (SpawnDev.Codecs
    /// Vp9FrameSequentialEncodeKernel on Vector128 needs 1 to 2 MB and wider warps need
    /// more), and the launch ran inline on the calling thread or on thread pool threads
    /// through Parallel.For: a stack overflow killed the process (0xC00000FD). The
    /// worker threads live as long as the accelerator.
    /// </summary>
    sealed class VelocityChunkRunner : DisposeBase
    {
        /// <summary>
        /// The stack size of the Velocity worker threads: address space the system
        /// reserves, committed only as the kernels use it.
        /// </summary>
        internal const int WorkerStackSize = 64 << 20;

        #region Nested types

        private sealed class Worker
        {
            public Thread Thread = null!;
            public readonly SemaphoreSlim Start = new(0);
            public VelocityGroupExecutionContext? Context;
        }

        #endregion

        private readonly VelocityAccelerator accelerator;
        private readonly List<Worker> workers = new();
        private volatile bool shutdown;

        private Action<int, VelocityGroupExecutionContext>? body;
        private int nextChunk;
        private int numChunks;
        private CountdownEvent? done;
        private int failed;
        private Exception? error;

        public VelocityChunkRunner(VelocityAccelerator accelerator)
        {
            this.accelerator = accelerator;
        }

        /// <summary>
        /// Runs <paramref name="chunkBody"/> for every chunk index in
        /// [0, <paramref name="chunks"/>) on at most <paramref name="maxWorkers"/>
        /// worker threads and waits for all of them; the first exception is rethrown.
        /// The caller makes sure that only one launch runs at a time.
        /// </summary>
        public void Run(
            int chunks,
            int maxWorkers,
            Action<int, VelocityGroupExecutionContext> chunkBody)
        {
            int count = Math.Max(1, Math.Min(chunks, maxWorkers));
            EnsureWorkers(count);

            body = chunkBody;
            nextChunk = -1;
            numChunks = chunks;
            failed = 0;
            error = null;
            var finished = done = new CountdownEvent(count);
            Thread.MemoryBarrier();

            for (int i = 0; i < count; ++i)
                workers[i].Start.Release();
            finished.Wait();
            finished.Dispose();
            done = null;
            body = null;

            if (error is Exception exception)
                ExceptionDispatchInfo.Capture(exception).Throw();
        }

        private void EnsureWorkers(int count)
        {
            while (workers.Count < count)
            {
                var worker = new Worker();
                worker.Thread = new Thread(() => WorkerLoop(worker), WorkerStackSize)
                {
                    IsBackground = true,
                    Name = $"Velocity worker {workers.Count}",
                };
                workers.Add(worker);
                worker.Thread.Start();
            }
        }

        private void WorkerLoop(Worker worker)
        {
            while (true)
            {
                worker.Start.Wait();
                if (shutdown)
                    return;
                var finished = done.AsNotNull();
                try
                {
                    var context = worker.Context ??= new VelocityGroupExecutionContext(
                        accelerator);
                    var chunkBody = body.AsNotNull();
                    int chunk;
                    while (Volatile.Read(ref failed) == 0 &&
                        (chunk = Interlocked.Increment(ref nextChunk)) < numChunks)
                    {
                        chunkBody(chunk, context);
                    }
                }
                catch (Exception exception)
                {
                    if (Interlocked.Exchange(ref failed, 1) == 0)
                        error = exception;
                }
                finally
                {
                    finished.Signal();
                }
            }
        }

        /// <summary>
        /// Stops the worker threads.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shutdown = true;
                foreach (var worker in workers)
                    worker.Start.Release();
                foreach (var worker in workers)
                    worker.Thread.Join();
                foreach (var worker in workers)
                {
                    worker.Context?.Dispose();
                    worker.Start.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
