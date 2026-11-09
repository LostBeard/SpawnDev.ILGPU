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
            int maxThreads = VelocityAccelerator.HostLaunchMaxThreads;
            return maxThreads > 0
                && hostEligible
                && hostKernelState >= 0
                && config.Size <= maxThreads;
        }

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
