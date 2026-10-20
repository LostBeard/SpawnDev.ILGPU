// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2022-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityDevice.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using ILGPU.Backends.Velocity;
using ILGPU.Backends.Velocity.Scalar;
#if NET7_0_OR_GREATER
using ILGPU.Backends.Velocity.Vec128;
using ILGPU.Backends.Velocity.Vec256;
using ILGPU.Backends.Velocity.Vec512;
#endif
using ILGPU.Util;
using System;

namespace ILGPU.Runtime.Velocity
{
    /// <summary>
    /// The device type of a Velocity device.
    /// </summary>
    public enum VelocityDeviceType
    {
        /// <summary>
        /// Scalar operations to simulate two lanes per warp.
        /// </summary>
        Scalar2,

#if NET7_0_OR_GREATER
        /// <summary>
        /// 128bit vector operations to simulate four lanes per warp using hardware
        /// acceleration via AdvSimd and SSE/AVX.
        /// </summary>
        Vector128,

        /// <summary>
        /// 256bit vector operations to simulate eight lanes per warp using hardware
        /// acceleration via AVX2.
        /// </summary>
        Vector256,

        /// <summary>
        /// 512bit vector operations to simulate sixteen lanes per warp using hardware
        /// acceleration via AVX-512.
        /// </summary>
        Vector512,

#endif
    }

    /// <summary>
    /// Represents a software-emulated velocity device for high-performance execution of
    /// tasks on the CPU using vectorization.
    /// </summary>
    [DeviceType(AcceleratorType.Velocity)]
    public sealed class VelocityDevice : Device
    {
        #region Static

        /// <summary>
        /// The most threads a group can be launched with. A group of more than a warp
        /// that has barriers runs one thread per warp (256 threads = 64 warps of 4
        /// lanes), so this is not raised to the 1024 of a GPU.
        /// </summary>
        internal const int MaxGroupThreads = 256;

        /// <summary>
        /// The group size that algorithms and kernels pick (they read
        /// <see cref="Device.MaxNumThreadsPerGroup"/>): 16 threads, 4 warps of 4 lanes.
        /// Measured (radix sort of 1.4 M keys, 12 processors): 16 threads 8.7 s, 32
        /// threads 10.5 s, 64 threads 40 s, 256 threads 48 s: the warps of a group with
        /// barriers are threads, and a barrier is only cheap while all of them are
        /// running at the same time (0.7 us with up to 12 warps on 12 processors, 15 to
        /// 60 us with 16 to 64). 16 is the least the radix sort accepts (16 counters,
        /// one per thread) and leaves room for several groups at once (teams).
        /// </summary>
        private const int PreferredGroupThreads = 16;

        private static readonly Func<VelocityTargetSpecializer>[] VelocitySpecializers =
            new Func<VelocityTargetSpecializer>[]
        {
            () => new Scalar(),
#if NET7_0_OR_GREATER
            () => new Vec128(),
            () => new Vec256(),
            () => new Vec512(),
#endif
        };

        #endregion

        #region Instance

        /// <summary>
        /// Creates a new velocity device using the given device type.
        /// </summary>
        /// <param name="deviceType">The Velocity device type to use.</param>
        public VelocityDevice(VelocityDeviceType deviceType)
        {
            switch (deviceType)
            {
                case VelocityDeviceType.Scalar2:
                    // Scalar is always supported
                    break;
#if NET7_0_OR_GREATER
                case VelocityDeviceType.Vector128:
                case VelocityDeviceType.Vector256:
                case VelocityDeviceType.Vector512:
                    // Vectors always run using software in the worst case
                    break;
#endif
                default:
                    throw new ArgumentOutOfRangeException(nameof(deviceType));
            }

            Name = $"{nameof(VelocityAccelerator)}_{deviceType}";
            DeviceType = deviceType;
            TargetSpecializer = VelocitySpecializers[(int)deviceType]();
            WarpSize = TargetSpecializer.WarpSize;
            // A group may have more threads than a warp: the warps of a group without
            // barriers run one after the other on one thread (see the kernel iteration
            // loop), those of a group with barriers run at the same time, one thread
            // per warp (VelocityGroupTeamRunner). Algorithms divide the threads of a
            // multiprocessor by the group size, so a multiprocessor holds a full group.
            MaxNumThreadsPerGroup = MaxNumThreadsPerMultiprocessor =
                Math.Max(WarpSize, PreferredGroupThreads);
#if DEBUG
            NumMultiprocessors = 1;
#else
            NumMultiprocessors = Environment.ProcessorCount;
#endif
            // Every dimension may use up to the maximum, the number of threads of the
            // whole group is limited by it as well (see VelocityAccelerator.Run)
            int maxGroupDim = Math.Max(MaxNumThreadsPerGroup, MaxGroupThreads);
            MaxGroupSize = new Index3D(maxGroupDim, maxGroupDim, maxGroupDim);

            MemorySize = long.MaxValue;
            MaxGridSize = new Index3D(int.MaxValue, ushort.MaxValue, ushort.MaxValue);
            MaxConstantMemory = int.MaxValue;
            NumThreads = MaxNumThreads;

            // Get the endian type from the global BitConverter class
            IsLittleEndian = BitConverter.IsLittleEndian;

            // Allocate a sufficient amount of local memory per thread equal to
            // the maximum number of shared memory per group in bytes (2 MB)
            MaxSharedMemoryPerGroup = 2 << 20;

            // Setup default Velocity capabilities
            Capabilities = new VelocityCapabilityContext();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the current device type.
        /// </summary>
        public VelocityDeviceType DeviceType { get; }

        /// <summary>
        /// Returns the internally used target specializer.
        /// </summary>
        internal VelocityTargetSpecializer TargetSpecializer { get; }

        /// <summary>
        /// Returns the number of threads.
        /// </summary>
        public int NumThreads { get; }

        /// <summary>
        /// Returns true if this device operates in little endian mode.
        /// </summary>
        public bool IsLittleEndian { get; }

        #endregion

        #region Methods

        /// <inheritdoc/>
        public override Accelerator CreateAccelerator(Context context) =>
            CreateVelocityAccelerator(context);

        /// <summary>
        /// Creates a new performance CPU accelerator using and the default thread
        /// priority.
        /// </summary>
        /// <param name="context">The ILGPU context.</param>
        /// <returns>The created CPU accelerator.</returns>
        public VelocityAccelerator CreateVelocityAccelerator(Context context) =>
            new VelocityAccelerator(context, this);

        #endregion

        #region Object

        /// <inheritdoc/>
        public override bool Equals(object? obj) =>
            obj is VelocityDevice device &&
            device.DeviceType == DeviceType &&
            base.Equals(obj);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(base.GetHashCode(), DeviceType);

        #endregion
    }
}
