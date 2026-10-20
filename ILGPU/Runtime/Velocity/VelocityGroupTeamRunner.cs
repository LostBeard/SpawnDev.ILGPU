// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityGroupTeamRunner.cs
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
    /// Runs kernels whose thread groups have more than one warp AND need the threads
    /// of a group to meet (barriers): the warps of a group run at the same time, one
    /// per worker thread, as a <see cref="VelocityGroupTeam"/>. The worker threads
    /// live as long as the accelerator; several teams run different groups in
    /// parallel when there are processors for them. (Groups without barriers need no
    /// threads: their warps are processed one after the other by the kernel's own
    /// loop.)
    /// </summary>
    sealed class VelocityGroupTeamRunner : DisposeBase
    {
        #region Nested types

        private sealed class Launch
        {
            public VelocityEntryPointHandler Handler = null!;
            public VelocityParameters Parameters = null!;
            public int GroupDim;
            public int GridDim;
            public int GridSize;
            public int UserSize;
            public int WarpSize;
            public int NumWarps;
            public int Teams;
            public int DynamicSharedMemory;
            public VelocityGroupTeam[] TeamObjects = null!;
            public CountdownEvent Done = null!;

            private int failed;
            private Exception? error;

            public bool Failed => Volatile.Read(ref failed) != 0;

            /// <summary>Remembers the first failure.</summary>
            public void Fail(Exception exception)
            {
                if (Interlocked.Exchange(ref failed, 1) == 0)
                    error = exception;
            }

            public Exception? Error => error;
        }

        private sealed class Worker
        {
            public int Index;
            public Thread Thread = null!;
            public readonly SemaphoreSlim Start = new(0);
            public Launch? Launch;
            public VelocityGroupExecutionContext? Context;
        }

        #endregion

        private readonly VelocityAccelerator accelerator;
        private readonly List<Worker> workers = new();
        private readonly Dictionary<int, List<VelocityGroupTeam>> teamCache = new();
        private volatile bool shutdown;

        public VelocityGroupTeamRunner(VelocityAccelerator accelerator)
        {
            this.accelerator = accelerator;
        }

        /// <summary>
        /// Runs a kernel: every group of the grid, each by a team of warps. The caller
        /// makes sure that only one launch runs at a time.
        /// </summary>
        public void Run(
            VelocityEntryPointHandler handler,
            VelocityParameters parameters,
            int groupDim,
            int gridDim,
            int gridSize,
            int userSize,
            int dynamicSharedMemory)
        {
            int warpSize = accelerator.WarpSize;
            int numWarps = IntrinsicMath.DivRoundUp(groupDim, warpSize);
            int teams = Math.Max(
                1,
                Math.Min(gridSize, accelerator.NumMultiprocessors / numWarps));
            int workersNeeded = teams * numWarps;
            EnsureWorkers(workersNeeded);

            // The teams of this launch (kept for the next launches of the same size)
            if (!teamCache.TryGetValue(numWarps, out var cached))
                teamCache.Add(numWarps, cached = new List<VelocityGroupTeam>());
            while (cached.Count < teams)
                cached.Add(new VelocityGroupTeam(accelerator, numWarps));
            var teamObjects = new VelocityGroupTeam[teams];
            bool oversubscribed = workersNeeded > accelerator.NumMultiprocessors;
            for (int i = 0; i < teams; ++i)
            {
                teamObjects[i] = cached[i];
                teamObjects[i].KernelBarrier.Oversubscribed = oversubscribed;
                teamObjects[i].GroupEnd.Oversubscribed = oversubscribed;
                teamObjects[i].KernelBarrier.Reset();    // a failed launch left them aborted
                teamObjects[i].GroupEnd.Reset();
            }

            var launch = new Launch
            {
                Handler = handler,
                Parameters = parameters,
                GroupDim = groupDim,
                GridDim = gridDim,
                GridSize = gridSize,
                UserSize = userSize,
                WarpSize = warpSize,
                NumWarps = numWarps,
                Teams = teams,
                DynamicSharedMemory = dynamicSharedMemory,
                TeamObjects = teamObjects,
                Done = new CountdownEvent(workersNeeded),
            };

            for (int i = 0; i < workersNeeded; ++i)
            {
                workers[i].Launch = launch;
                workers[i].Start.Release();
            }
            launch.Done.Wait();
            launch.Done.Dispose();

            if (launch.Error is Exception error)
                ExceptionDispatchInfo.Capture(error).Throw();
        }

        private void EnsureWorkers(int count)
        {
            while (workers.Count < count)
            {
                var worker = new Worker { Index = workers.Count };
                worker.Thread = new Thread(() => WorkerLoop(worker))
                {
                    IsBackground = true,
                    Name = $"Velocity warp {worker.Index}",
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
                var launch = worker.Launch.AsNotNull();
                worker.Launch = null;
                try
                {
                    RunWarp(launch, worker);
                }
                catch (Exception exception)
                {
                    launch.Fail(exception);
                }
                finally
                {
                    launch.Done.Signal();
                }
            }
        }

        /// <summary>
        /// One worker thread: warp (index % warps) of team (index / warps): it executes
        /// its part of the groups of the team one after the other.
        /// </summary>
        private void RunWarp(Launch launch, Worker worker)
        {
            int teamIndex = worker.Index / launch.NumWarps;
            int warp = worker.Index % launch.NumWarps;
            var team = launch.TeamObjects[teamIndex];
            var context = worker.Context ??= new VelocityGroupExecutionContext(
                accelerator);
            context.AttachTeam(team, warp);
            try
            {
                for (
                    int group = teamIndex;
                    group < launch.GridSize && !launch.Failed;
                    group += launch.Teams)
                {
                    int groupStart = group * launch.GroupDim;
                    int start = groupStart + warp * launch.WarpSize;
                    int end = Math.Min(
                        Math.Min(start + launch.WarpSize, groupStart + launch.GroupDim),
                        launch.UserSize);

                    context.SetupThreadGrid(launch.DynamicSharedMemory);
                    if (start < end)
                    {
                        launch.Handler.Invoke(
                            context,
                            launch.GroupDim,
                            launch.GridDim,
                            start,
                            end,
                            launch.Parameters);
                    }

                    // This warp has returned: the others do not wait for it at
                    // barriers any more. All warps are done with the group when they
                    // meet here, and only then may the next group use the shared
                    // memory
                    team.WarpFinished(warp);
                    team.GroupEnd.SignalAndWait();
                }
            }
            catch (OperationCanceledException) when (launch.Failed)
            {
                // Another warp failed and released the barriers
            }
            catch (Exception exception)
            {
                launch.Fail(exception);
                team.Abort();
            }
            finally
            {
                context.AttachTeam(null, 0);
            }
        }

        /// <summary>
        /// Stops the worker threads and frees the teams.
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
                foreach (var teams in teamCache.Values)
                    foreach (var team in teams)
                        team.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
