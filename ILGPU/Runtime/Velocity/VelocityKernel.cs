// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2022-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityKernel.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.Velocity;
using ILGPU.Util;
using System;
using System.Diagnostics;
using System.Reflection;

namespace ILGPU.Runtime.Velocity
{
    /// <summary>
    /// Represents a single Velocity kernel.
    /// </summary>
    public sealed class VelocityKernel : Kernel
    {
        #region Static

        /// <summary>
        /// Represents the <see cref="VelocityAccelerator"/> property getter.
        /// </summary>
        internal static readonly MethodInfo GetVelocityAccelerator =
            typeof(VelocityKernel).GetProperty(
                    nameof(VelocityAccelerator),
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                .AsNotNull()
                .GetGetMethod(true)
                .AsNotNull();

        /// <summary>
        /// Represents the <see cref="KernelEntryPoint"/> property getter.
        /// </summary>
        internal static readonly MethodInfo GetKernelExecutionDelegate =
            typeof(VelocityKernel).GetProperty(
                nameof(KernelEntryPoint),
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
            .AsNotNull()
            .GetGetMethod(true)
            .AsNotNull();

        /// <summary>
        /// Represents the <see cref="ShouldLaunchOnHost"/> method.
        /// </summary>
        internal static readonly MethodInfo ShouldLaunchOnHostMethod =
            typeof(VelocityKernel).GetMethod(
                nameof(ShouldLaunchOnHost),
                BindingFlags.NonPublic | BindingFlags.Instance)
            .AsNotNull();

        /// <summary>
        /// Represents the <see cref="LaunchOnHost"/> method.
        /// </summary>
        internal static readonly MethodInfo LaunchOnHostMethod =
            typeof(VelocityKernel).GetMethod(
                nameof(LaunchOnHost),
                BindingFlags.NonPublic | BindingFlags.Instance)
            .AsNotNull();

        #endregion

        #region Instance

        /// <summary>
        /// True when this kernel may run through the host path at all: no group synchronization and no shared memory
        /// (the host accelerator runs every launch of it on its own threads, group after group).
        /// </summary>
        private readonly bool hostEligible;

        /// <summary>
        /// The same kernel loaded on <see cref="VelocityAccelerator"/>'s host (IL backend) accelerator, once a small
        /// launch needed it; null before that or when it could not be loaded.
        /// </summary>
        private Kernel? hostKernel;

        /// <summary>
        /// 0 = not tried, 1 = loaded, -1 = could not be loaded (the Velocity path is used).
        /// </summary>
        private int hostKernelState;

        /// <summary>
        /// Loads a compiled kernel into the given Cuda context as kernel program.
        /// </summary>
        /// <param name="accelerator">The associated accelerator.</param>
        /// <param name="kernel">The source kernel.</param>
        /// <param name="launcher">The launcher method for the given kernel.</param>
        internal VelocityKernel(
            VelocityAccelerator accelerator,
            VelocityCompiledKernel kernel,
            MethodInfo launcher)
            : base(accelerator, kernel, launcher)
        {
            KernelEntryPoint = kernel.CreateKernelEntryPoint();
            hostEligible = !kernel.UsesGroupSynchronization && !kernel.EntryPoint.SharedMemory.HasSharedMemory;
            CustomGroupSize = 0;
        }

        /// <summary>
        /// The custom group size the kernel was loaded with (0 = automatic or explicitly grouped).
        /// </summary>
        internal int CustomGroupSize { get; set; }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated Velocity runtime.
        /// </summary>
        public VelocityAccelerator VelocityAccelerator =>
            Accelerator.AsNotNullCast<VelocityAccelerator>();

        /// <summary>
        /// The main kernel entry point function to be called from each velocity
        /// multiprocessor during execution.
        /// </summary>
        internal VelocityEntryPointHandler KernelEntryPoint { get; }

        #endregion

        #region Host launch

        /// <summary>
        /// True when a launch of <paramref name="config"/> threads runs through the host path: Velocity executes each IR
        /// operation as a call into a warp-type helper with lane masks, which for a launch of a few threads running long
        /// serial loops costs ~60x the IL backend's direct execution of the same kernel (measured 2026-11-09: 402.6 vs
        /// 6.7 ns per iteration of a one-thread bool-decoder loop; SpawnDev.Codecs' VP9 parse 1,105 vs 3.4 ms per frame).
        /// </summary>
        internal bool ShouldLaunchOnHost(KernelConfig config)
        {
            pendingStart = 0;
            int maxThreads = VelocityAccelerator.HostLaunchMaxThreads;
            if (maxThreads <= 0 || !hostEligible || hostKernelState < 0)
                return false;
            if (config.Size <= maxThreads)
                return true;
            if (!VelocityAccelerator.AdaptiveHostLaunches)
                return false;
            return ChooseWidePath(config.Size);
        }

        #region Adaptive path choice for wide launches

        // A wide launch of a kernel without group synchronization or shared memory can run either way: Velocity's vectorized
        // code (right for uniform, arithmetic-heavy kernels) or the kernel's own IL on the host accelerator (right for
        // branchy, serial per-thread code: SpawnDev.Codecs' VP9 reconstruction, one thread per 4x4 unit, 51.2 vs 7.6 ms per
        // frame on Vector256, 2026-11-10). Neither wins everywhere, so each kernel measures both: after one warm-up launch per
        // path (compilation, JIT, the host kernel's load), it alternates SamplesPerPath timed launches per path, normalized
        // per thread, and runs on the host only when the host's median is HostMargin times faster. It measures again after
        // RecheckAfter wide launches. (A first attempt with ONE sample per path picked the wrong path on noise.)

        private const int SamplesPerPath = 3;
        private const double HostMargin = 1.25;
        private const int RecheckAfter = 1024;

        /// <summary>0 = measuring, 1 = wide launches on the host, 2 = wide launches on Velocity.</summary>
        private int wideDecision;
        private int wideLaunchesSinceDecision;
        private int measureStep;
        private readonly double[] hostSamples = new double[SamplesPerPath];
        private readonly double[] velocitySamples = new double[SamplesPerPath];
        private long pendingStart;
        private bool pendingOnHost;
        private long pendingSize;

        /// <summary>
        /// Where wide launches of this kernel run: null while measuring, true on the host, false on Velocity (a
        /// diagnostic; see <see cref="VelocityAccelerator.AdaptiveHostLaunches"/>).
        /// </summary>
        public bool? WideLaunchesOnHost => wideDecision switch { 1 => true, 2 => false, _ => null };

        private bool ChooseWidePath(long size)
        {
            lock (hostSamples)
            {
                if (wideDecision != 0)
                {
                    if (++wideLaunchesSinceDecision < RecheckAfter)
                        return wideDecision == 1;
                    // measure again: the data (and so the better path) can change over a kernel's life
                    wideDecision = 0;
                    measureStep = 2; // both paths are warm
                }
                // steps 0 and 1: one untimed warm-up launch per path; then pairs of timed launches
                bool onHost = (measureStep & 1) == 1;
                if (measureStep >= 2)
                {
                    pendingStart = Stopwatch.GetTimestamp();
                    pendingOnHost = onHost;
                    pendingSize = size;
                }
                measureStep++;
                return onHost;
            }
        }

        /// <summary>Ends the timing of a measured launch (called after the launch has completed).</summary>
        private void EndMeasuredLaunch(bool onHost)
        {
            long start = pendingStart;
            if (start == 0)
                return;
            long elapsed = Stopwatch.GetTimestamp() - start;
            lock (hostSamples)
            {
                pendingStart = 0;
                if (wideDecision != 0 || pendingOnHost != onHost)
                    return;
                int pair = (measureStep - 1 - 2) / 2;
                if (pair < 0 || pair >= SamplesPerPath)
                    return;
                double perThread = (double)elapsed / Math.Max(1L, pendingSize);
                (onHost ? hostSamples : velocitySamples)[pair] = perThread;
                if (measureStep >= 2 + 2 * SamplesPerPath && onHost)
                {
                    double host = Median(hostSamples), velocity = Median(velocitySamples);
                    wideDecision = host * HostMargin < velocity ? 1 : 2;
                    wideLaunchesSinceDecision = 0;
                    Trace.WriteLine($"Velocity: kernel '{CompiledKernel.Name}' wide launches on " +
                        $"{(wideDecision == 1 ? "the host" : "Velocity")} (median per thread: host {host:F1}, Velocity {velocity:F1} ticks)");
                }
            }
        }

        private static double Median(double[] values)
        {
            var sorted = (double[])values.Clone();
            Array.Sort(sorted);
            return sorted[sorted.Length / 2];
        }

        /// <summary>Called by the launcher after a launch on the Velocity path.</summary>
        internal void EndVelocityLaunch() => EndMeasuredLaunch(onHost: false);

        /// <summary>
        /// Represents the <see cref="EndVelocityLaunch"/> method.
        /// </summary>
        internal static readonly MethodInfo EndVelocityLaunchMethod =
            typeof(VelocityKernel).GetMethod(
                nameof(EndVelocityLaunch),
                BindingFlags.NonPublic | BindingFlags.Instance)
            .AsNotNull();

        #endregion

        /// <summary>
        /// Runs the launch on the host accelerator (synchronously, like a Velocity launch). Returns false when the kernel
        /// could not be loaded there, so the launcher continues with the Velocity path.
        /// </summary>
        internal bool LaunchOnHost(object dimension, object[] args)
        {
            var host = hostKernel;
            if (host is null)
            {
                host = VelocityAccelerator.TryLoadHostKernel(
                    CompiledKernel.AsNotNullCast<VelocityCompiledKernel>(),
                    CustomGroupSize);
                if (host is null)
                {
                    hostKernelState = -1;
                    // a measured launch that falls back to Velocity is timed as a Velocity launch; the choice settles on
                    // Velocity because the host is no longer eligible (ShouldLaunchOnHost)
                    pendingOnHost = false;
                    return false;
                }
                hostKernel = host;
                hostKernelState = 1;
            }
            var reflectionArgs = new object[KernelParameterOffset + args.Length];
            reflectionArgs[KernelInstanceParamIdx] = host;
            reflectionArgs[KernelStreamParamIdx] = host.Accelerator.DefaultStream;
            reflectionArgs[KernelParamDimensionIdx] = dimension;
            args.CopyTo(reflectionArgs, KernelParameterOffset);
            host.Launcher.AsNotNull().Invoke(null, reflectionArgs);
            VelocityAccelerator.CountHostLaunch();
            EndMeasuredLaunch(onHost: true);
            return true;
        }

        #endregion

        #region IDisposable

        /// <summary>
        /// Does not perform any operation.
        /// </summary>
        protected override void DisposeAcceleratorObject(bool disposing) { }

        #endregion
    }
}
