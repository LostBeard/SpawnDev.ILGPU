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
using System.Diagnostics;
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
            public Index3D GroupDims;
            public Index3D GridDims;
            public Index3D Extent;
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
            int dynamicSharedMemory,
            Index3D gridDims,
            Index3D groupDims,
            Index3D extent)
        {
            int warpSize = accelerator.WarpSize;
            int numWarps = IntrinsicMath.DivRoundUp(groupDim, warpSize);
            // Teams that fit on the processors (floor), or one more that oversubscribes them slightly (ceil): with 8 warps
            // a group on 12 processors, floor runs ONE team and leaves 4 processors idle. Which is faster depends on the
            // kernel's work between barriers (SpawnDev.Codecs' VP9 loop filter, Vector256: 84.1 -> 64.3 ms per frame with
            // ceil; a barrier-bound microbenchmark at the same shape: 2.25x slower), so each kernel measures both.
            int floorTeams = Math.Max(1, Math.Min(gridSize, accelerator.NumMultiprocessors / numWarps));
            int ceilTeams = Math.Max(1, Math.Min(gridSize,
                IntrinsicMath.DivRoundUp(accelerator.NumMultiprocessors, numWarps)));
            TeamChoice? choice = null;
            bool useCeil = false, timed = false;
            int sample = -1;
            if (ceilTeams != floorTeams && accelerator.AdaptiveTeamCount)
            {
                // per launch shape: a kernel launched with different group counts (VP9's loop filter: the luma plane and
                // the smaller chroma planes in turn) has different work per group, so one shared choice mixed them
                var key = (handler, numWarps, gridSize);
                if (!teamChoices.TryGetValue(key, out choice))
                    teamChoices.Add(key, choice = new TeamChoice());
                if (choice.Decision != 0)
                {
                    useCeil = choice.Decision == 2;
                    if (++choice.Since >= TeamChoice.RecheckAfter)
                    {
                        choice.Previous = choice.Decision;
                        choice.Decision = 0;
                        choice.Step = 2; // both counts are warm: measure again from the timed launches
                    }
                }
                else
                {
                    // steps 0 and 1: one untimed warm-up per count (ceil's first launch creates its worker threads);
                    // then alternating timed launches
                    useCeil = (choice.Step & 1) == 1;
                    if (choice.Step >= 2)
                    {
                        timed = true;
                        sample = (choice.Step - 2) / 2;
                    }
                    choice.Step++;
                }
            }
            int teams = useCeil ? ceilTeams : floorTeams;
            long started = timed ? Stopwatch.GetTimestamp() : 0;
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
                GroupDims = groupDims,
                GridDims = gridDims,
                Extent = extent,
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

            if (timed && choice != null && sample >= 0 && sample < TeamChoice.SamplesPerCount)
            {
                double perGroup = (double)(Stopwatch.GetTimestamp() - started) / Math.Max(1, gridSize);
                (useCeil ? choice.Ceil : choice.Floor)[sample] = perGroup;
                if (useCeil && choice.Step >= 2 + 2 * TeamChoice.SamplesPerCount)
                {
                    double floor = TeamChoice.Median(choice.Floor), ceil = TeamChoice.Median(choice.Ceil);
                    // switch only on a clear margin either way; within it keep the previous choice (the work per launch
                    // varies, so a re-check can come out close: VP9's loop filter once flipped to floor that way)
                    choice.Decision = ceil * TeamChoice.CeilMargin < floor ? 2
                        : floor * TeamChoice.CeilMargin < ceil ? 1
                        : choice.Previous != 0 ? choice.Previous : 1;
                    choice.Since = 0;
                    accelerator.CountTeamCountDecision();
                    Trace.WriteLine($"Velocity: groups of {numWarps} warps run as {(choice.Decision == 2 ? ceilTeams : floorTeams)} " +
                        $"team(s) (median per group: {floorTeams} teams {floor:F0}, {ceilTeams} teams {ceil:F0} ticks)");
                }
            }
        }

        /// <summary>
        /// The measured team count of one kernel at one group shape (see <see cref="Run"/>): floor (the teams that fit on
        /// the processors) or ceil (one more), each timed over <see cref="SamplesPerCount"/> launches after a warm-up.
        /// </summary>
        private sealed class TeamChoice
        {
            public const int SamplesPerCount = 3;
            public const double CeilMargin = 1.1;
            public const int RecheckAfter = 1024;
            /// <summary>0 = measuring, 1 = floor, 2 = ceil.</summary>
            public int Decision;
            /// <summary>The decision before a re-check (kept when the re-check is within the margin).</summary>
            public int Previous;
            public int Step;
            public int Since;
            public readonly double[] Floor = new double[SamplesPerCount];
            public readonly double[] Ceil = new double[SamplesPerCount];

            public static double Median(double[] values)
            {
                var sorted = (double[])values.Clone();
                Array.Sort(sorted);
                return sorted[sorted.Length / 2];
            }
        }

        /// <summary>The team count each kernel measured, per group shape; launches run one at a time, so no lock.</summary>
        private readonly Dictionary<(VelocityEntryPointHandler, int, int), TeamChoice> teamChoices = new();

        private void EnsureWorkers(int count)
        {
            while (workers.Count < count)
            {
                var worker = new Worker { Index = workers.Count };
                worker.Thread = new Thread(
                    () => WorkerLoop(worker),
                    VelocityChunkRunner.WorkerStackSize)
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

                    context.SetLaunchDimensions(
                        launch.GridDims,
                        launch.GroupDims,
                        launch.Extent);
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
