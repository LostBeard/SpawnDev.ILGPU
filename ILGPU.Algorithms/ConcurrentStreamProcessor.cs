// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: ConcurrentStreamProcessor.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime;
using ILGPU.Util;
using System;
using System.Threading.Tasks;

namespace ILGPU.Algorithms
{
    /// <summary>
    /// Processes actions in parallel on multiple asynchronous accelerator streams.
    /// </summary>
    public class ConcurrentStreamProcessor : DisposeBase
    {
        #region Instance

        private readonly AcceleratorStream[] streams;

        /// <summary>
        /// Constructs a new concurrent stream processor.
        /// </summary>
        /// <param name="accelerator">The parent accelerator.</param>
        /// <param name="maxNumConcurrentStreams">
        /// The maximum number of concurrent streams to use (if any).
        /// </param>
        /// <param name="streamProvider">
        /// A custom stream provider function to construct specialized streams.
        /// </param>
        public ConcurrentStreamProcessor(
            Accelerator accelerator,
            int maxNumConcurrentStreams = 0,
            Func<Accelerator, AcceleratorStream>? streamProvider = null)
        {
            maxNumConcurrentStreams = Math.Max(
                Math.Max(maxNumConcurrentStreams, 1),
                Environment.ProcessorCount);

            streams = new AcceleratorStream[maxNumConcurrentStreams];
            if (maxNumConcurrentStreams > 1)
            {
                streamProvider ??= accel => accel.CreateStream();
                for (int i = 0; i < streams.Length; ++i)
                    streams[i] = streamProvider(accelerator);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the maximum number of concurrent streams supported by this processor.
        /// </summary>
        public int MaxNumConcurrentStreams => streams.Length;

        #endregion

        #region Methods

        /// <summary>
        /// Processes the given action concurrently by using the underlying accelerator
        /// streams concurrently to submit different jobs in parallel.
        /// </summary>
        /// <remarks>
        /// Note that this method assumes that all previous jobs have been synchronized
        /// with the current processing thread.
        /// </remarks>
        /// <param name="numActions">The number of actions to submit.</param>
        /// <param name="action">
        /// The action to invoke on each stream to submit work.
        /// </param>
        public void ProcessConcurrently(
            int numActions,
            Action<AcceleratorStream, int> action) =>
            ProcessConcurrently(null, numActions, action);

        /// <summary>
        /// Processes the given action concurrently by using the underlying accelerator
        /// streams concurrently to submit different jobs in parallel.
        /// </summary>
        /// <param name="stream">The current accelerator stream.</param>
        /// <param name="numActions">The number of actions to submit.</param>
        /// <param name="action">
        /// The action to invoke on each stream to submit work.
        /// </param>
        public void ProcessConcurrently(
            AcceleratorStream? stream,
            int numActions,
            Action<AcceleratorStream, int> action)
        {
            if (numActions < 0)
                throw new ArgumentOutOfRangeException(nameof(numActions));
            if (numActions == 0)
                return;
            if (action == null)
                throw new ArgumentOutOfRangeException(nameof(action));

            if (stream != null && numActions == 1 && MaxNumConcurrentStreams == 1)
            {
                // Use the given stream to process the action request
                action(stream, 0);
            }
            else
            {
                // Wait for the current stream to finish processing
                stream?.Synchronize();

                // Perform work on all streams
                int numActionsPerStream = XMath.DivRoundUp(
                    numActions,
                    MaxNumConcurrentStreams);
                var actionStride = new Stride2D.DenseY(numActionsPerStream);
                Parallel.For(0, MaxNumConcurrentStreams, i =>
                {
                    var currentStream = streams[i];
                    using var binding = currentStream.BindScoped();

                    for (int j = 0; j < numActionsPerStream; ++j)
                    {
                        int actionIndex = actionStride.ComputeElementIndex((i, j));
                        if (actionIndex >= numActions)
                            break;
                        action(currentStream, actionIndex);
                    }

                    // Wait for the result
                    currentStream.Synchronize();
                });
            }
        }

        /// <summary>
        /// Browser-safe async counterpart of
        /// <see cref="ProcessConcurrently(int, Action{AcceleratorStream, int})"/>.
        /// </summary>
        /// <param name="numActions">The number of actions to submit.</param>
        /// <param name="action">The action to invoke on each stream to submit work.</param>
        public Task ProcessConcurrentlyAsync(
            int numActions,
            Action<AcceleratorStream, int> action) =>
            ProcessConcurrentlyAsync(null, numActions, action);

        /// <summary>
        /// Browser-safe async counterpart of
        /// <see cref="ProcessConcurrently(AcceleratorStream?, int,
        /// Action{AcceleratorStream, int})"/>. The synchronous overload ends each stream with
        /// <c>Synchronize()</c>, which is a no-op on Wasm/WebGL and only flushes on WebGPU, so
        /// it returns before the submitted work has actually completed on the browser
        /// backends. This version submits the work and then awaits every stream's real async
        /// drain (<c>SynchronizeAsync</c>), so completion is guaranteed on every backend; on
        /// desktop the per-stream drains still overlap.
        /// </summary>
        /// <param name="stream">The current accelerator stream.</param>
        /// <param name="numActions">The number of actions to submit.</param>
        /// <param name="action">The action to invoke on each stream to submit work.</param>
        public async Task ProcessConcurrentlyAsync(
            AcceleratorStream? stream,
            int numActions,
            Action<AcceleratorStream, int> action)
        {
            if (numActions < 0)
                throw new ArgumentOutOfRangeException(nameof(numActions));
            if (numActions == 0)
                return;
            if (action == null)
                throw new ArgumentOutOfRangeException(nameof(action));

            if (stream != null && numActions == 1 && MaxNumConcurrentStreams == 1)
            {
                // Use the given stream to process the single action request
                action(stream, 0);
                await stream.SynchronizeAsync().ConfigureAwait(false);
                return;
            }

            // Drain the incoming stream before fanning out
            if (stream != null)
                await stream.SynchronizeAsync().ConfigureAwait(false);

            int numActionsPerStream = XMath.DivRoundUp(
                numActions,
                MaxNumConcurrentStreams);
            var actionStride = new Stride2D.DenseY(numActionsPerStream);

            // Submit work on every stream (submission is cheap), then await all real drains.
            var drains = new Task[MaxNumConcurrentStreams];
            for (int i = 0; i < MaxNumConcurrentStreams; ++i)
            {
                var currentStream = streams[i];
                using (currentStream.BindScoped())
                {
                    for (int j = 0; j < numActionsPerStream; ++j)
                    {
                        int actionIndex = actionStride.ComputeElementIndex((i, j));
                        if (actionIndex >= numActions)
                            break;
                        action(currentStream, actionIndex);
                    }
                }
                drains[i] = currentStream.SynchronizeAsync();
            }
            await Task.WhenAll(drains).ConfigureAwait(false);
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Frees all internal streams.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Free all internal streams
                foreach (var stream in streams)
                    stream?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
