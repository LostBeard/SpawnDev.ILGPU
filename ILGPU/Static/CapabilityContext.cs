// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: CapabilityContext.tt/CapabilityContext.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeInformation.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: CapabilitiesImporter.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using ILGPU.Resources;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Represents general capabilities available to all accelerators.
    /// </summary>
    public abstract class CapabilityContext
    {
        #region Properties

        /// <summary>
        /// Supports Float16 (Half) data type.
        /// </summary>
        public bool Float16 { get; protected set; }

        /// <summary>
        /// True if <see cref="IntrinsicMath.Dot4I8Packed(uint, uint, int)"/> and
        /// <see cref="IntrinsicMath.Dot4U8Packed(uint, uint, uint)"/> run as one native
        /// instruction on this accelerator (CUDA sm_61+ <c>dp4a</c>, WebGPU with the
        /// <c>packed_4x8_integer_dot_product</c> WGSL language feature). If false, they
        /// still work and give the same bits, as a four-term multiply-add.
        /// </summary>
        public bool PackedInt8DotProduct { get; protected set; }

        #endregion

        #region Methods

        /// <summary>
        /// Creates exception for 'Float16'.
        /// </summary>
        public static Exception GetNotSupportedFloat16Exception() =>
            new CapabilityNotSupportedException(
                string.Format(
                    ErrorMessages.CapabilityNotSupported,
                    "Float16"));

        #endregion
    }
}

namespace ILGPU.Runtime.CPU
{
    /// <summary>
    /// Represents capabilities available to the CPU accelerator.
    /// </summary>
    public sealed class CPUCapabilityContext : CapabilityContext
    {
        #region Instance

        internal CPUCapabilityContext()
        {
            Float16 = true;
        }

        #endregion
    }
}

namespace ILGPU.Runtime.Velocity
{
    /// <summary>
    /// Represents capabilities available to the Velocity accelerator.
    /// </summary>
    public sealed class VelocityCapabilityContext : CapabilityContext
    {
        #region Instance

        internal VelocityCapabilityContext()
        {
            Float16 = true;
        }

        #endregion

        #region Properties

        #endregion

        #region Methods

        #endregion
    }
}

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Represents capabilities available to Cuda accelerators.
    /// </summary>
    public sealed class CudaCapabilityContext : CapabilityContext
    {
        #region Instance

        /// <summary>
        /// Create a new capability context of Cuda accelerators.
        /// </summary>
        public CudaCapabilityContext(CudaArchitecture arch)
        {
            // Below sm_53 the PTX backend emulates f16 arithmetic through f32 (see
            // PTXFloat16Emulation): Float16 stays true, Float16Native tells the paths apart.
            Float16 = true;
            Float16Native = arch >= CudaArchitecture.SM_53;
            Float16_Min = !Float16Native || arch >= CudaArchitecture.SM_75;
            Float16_Max = !Float16Native || arch >= CudaArchitecture.SM_80;
            Float16_TanH = arch >= CudaArchitecture.SM_80;
            Float32_TanH = arch >= CudaArchitecture.SM_75;
            // dp4a (PTXIntrinsics.Dot4I8PackedPtx / Dot4U8PackedPtx)
            PackedInt8DotProduct = arch >= CudaArchitecture.SM_61;
        }

        #endregion

        #region Properties

        /// <summary>
        /// True if the device has native f16 arithmetic (sm_53+). If false, Float16 is
        /// still supported, but its arithmetic is emulated through f32.
        /// </summary>
        public bool Float16Native { get; internal set; }

        /// <summary>
        /// Supports Float16 intrinsic Min.
        /// </summary>
        public bool Float16_Min { get; internal set; }

        /// <summary>
        /// Supports Float16 intrinsic Max.
        /// </summary>
        public bool Float16_Max { get; internal set; }

        /// <summary>
        /// Supports Float16 intrinsic TanH.
        /// </summary>
        public bool Float16_TanH { get; internal set; }

        /// <summary>
        /// Supports Float32 intrinsic TanH.
        /// </summary>
        public bool Float32_TanH { get; internal set; }

        #endregion

        #region Methods

        /// <summary>
        /// Creates exception for 'Float16_Min'.
        /// </summary>
        public static Exception GetNotSupportedFloat16_MinException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupportedCuda,
                    "Intrinsic 'Min' for Float16",
                    CudaArchitecture.SM_75));

        /// <summary>
        /// Creates exception for 'Float16_Max'.
        /// </summary>
        public static Exception GetNotSupportedFloat16_MaxException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupportedCuda,
                    "Intrinsic 'Max' for Float16",
                    CudaArchitecture.SM_80));

        /// <summary>
        /// Creates exception for 'Float16_TanH'.
        /// </summary>
        public static Exception GetNotSupportedFloat16_TanHException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupportedCuda,
                    "Intrinsic 'TanH' for Float16",
                    CudaArchitecture.SM_80));

        /// <summary>
        /// Creates exception for 'Float32_TanH'.
        /// </summary>
        public static Exception GetNotSupportedFloat32_TanHException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupportedCuda,
                    "Intrinsic 'TanH' for Float32",
                    CudaArchitecture.SM_75));

        #endregion
    }
}

namespace ILGPU.Runtime.OpenCL
{
    /// <summary>
    /// Represents capabilities available to OpenCL accelerators.
    /// </summary>
    public sealed class CLCapabilityContext : CapabilityContext
    {
        #region Static

        /// <summary>
        /// Extensions for Float16.
        /// </summary>
        internal static readonly ImmutableArray<string> Float16Extensions =
            ImmutableArray.Create("cl_khr_fp16");

        /// <summary>
        /// Extensions for Float64.
        /// </summary>
        internal static readonly ImmutableArray<string> Float64Extensions =
            ImmutableArray.Create("cl_khr_fp64");

        /// <summary>
        /// Extensions for Int64_Atomics.
        /// </summary>
        internal static readonly ImmutableArray<string> Int64_AtomicsExtensions =
            ImmutableArray.Create("cl_khr_int64_base_atomics", "cl_khr_int64_extended_atomics");

        /// <summary>
        /// Extensions for Khronos subgroup shuffle (sub_group_shuffle, sub_group_shuffle_up/down/xor).
        /// </summary>
        internal static readonly ImmutableArray<string> SubGroupShuffleExtensions =
            ImmutableArray.Create(
                "cl_khr_subgroup_shuffle",
                "cl_khr_subgroup_shuffle_relative");

        #endregion

        #region Instance

        /// <summary>
        /// Create a new capability context of OpenCL accelerators.
        /// </summary>
        public CLCapabilityContext(
            bool float16,
            bool float64,
            bool genericAddressSpace,
            bool int64_Atomics,
            bool subGroups,
            bool subGroupShuffle
            )
        {
            var extensions = ImmutableArray.CreateBuilder<string>();
            Float16 = true;
            Float16Native = float16;
            if (Float16Native)
                extensions.AddRange(Float16Extensions);
            Float64 = float64;
            if (Float64)
                extensions.AddRange(Float64Extensions);
            GenericAddressSpace = genericAddressSpace;
            Int64_Atomics = int64_Atomics;
            if (Int64_Atomics)
                extensions.AddRange(Int64_AtomicsExtensions);
            SubGroups = subGroups;
            SubGroupShuffle = subGroupShuffle;
            Extensions = extensions.ToImmutable();
        }

        internal CLCapabilityContext(CLDevice device)
        {
            var extensions = ImmutableArray.CreateBuilder<string>();
            // f16-emulation-plan Phase 3: see note in the public ctor.
            Float16Native = device.HasAllExtensions(Float16Extensions);
            Float16 = true;
            if (Float16Native)
                extensions.AddRange(Float16Extensions);
            Float64 = device.HasAllExtensions(Float64Extensions);
            if (Float64)
                extensions.AddRange(Float64Extensions);
            GenericAddressSpace = false;
            Int64_Atomics = device.HasAllExtensions(Int64_AtomicsExtensions);
            if (Int64_Atomics)
                extensions.AddRange(Int64_AtomicsExtensions);
            SubGroups = false;
            SubGroupShuffle = false;
            Extensions = extensions.ToImmutable();
        }

        #endregion

        #region Properties

        /// <summary>
        /// List of OpenCL extensions.
        /// </summary>
        public ImmutableArray<string> Extensions { get; internal set; }

        /// <summary>
        /// Supports Float64 (double) data type.
        /// </summary>
        public bool Float64 { get; internal set; }

        /// <summary>
        /// Supports generic address space.
        /// </summary>
        public bool GenericAddressSpace { get; internal set; }

        /// <summary>
        /// Supports 64-bit Atomics.
        /// </summary>
        public bool Int64_Atomics { get; internal set; }

        /// <summary>
        /// Supports SubGroups.
        /// </summary>
        public bool SubGroups { get; internal set; }

        /// <summary>
        /// Supports subgroup shuffle (Warp.Shuffle). Requires cl_intel_subgroups or cl_khr_subgroup_shuffle + cl_khr_subgroup_shuffle_relative.
        /// </summary>
        public bool SubGroupShuffle { get; internal set; }

        /// <summary>
        /// True when the OpenCL device has the <c>cl_khr_fp16</c> extension (native
        /// <c>half</c> type support in kernel code). False means Float16 operations
        /// are emulated via <c>vload_half</c> / <c>vstore_half</c> plus <c>float</c>
        /// arithmetic. In both cases <see cref="Float16"/> remains true;
        /// <c>Float16Native</c> distinguishes which path the codegen will take.
        /// Added by f16-emulation-plan Phase 3.
        /// </summary>
        public bool Float16Native { get; internal set; }

        #endregion

        #region Methods

        /// <summary>
        /// Creates exception for 'Float64'.
        /// </summary>
        public static Exception GetNotSupportedFloat64Exception() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupported,
                    "Float64 (double) type"));

        /// <summary>
        /// Creates exception for 'GenericAddressSpace'.
        /// </summary>
        public static Exception GetNotSupportedGenericAddressSpaceException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupported,
                    "GenericAddressSpace feature"));

        /// <summary>
        /// Creates exception for 'Int64_Atomics'.
        /// </summary>
        public static Exception GetNotSupportedInt64_AtomicsException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupported,
                    "64-bit atomics extension"));

        /// <summary>
        /// Creates exception for 'SubGroups'.
        /// </summary>
        public static Exception GetNotSupportedSubGroupsException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupported,
                    "SubGroups extension"));

        /// <summary>
        /// Creates exception for 'SubGroupShuffle'.
        /// </summary>
        public static Exception GetNotSupportedSubGroupShuffleException() =>
            new CapabilityNotSupportedException(
                string.Format(ErrorMessages.CapabilityNotSupported,
                    "SubGroupShuffle extension (cl_intel_subgroups or cl_khr_subgroup_shuffle)"));

        /// <summary>
        /// Appends subgroup-related extension pragmas to Extensions. Call from InitSubGroupSupport
        /// when SubGroups and/or SubGroupShuffle are enabled.
        /// </summary>
        /// <param name="subgroupExts">Extensions for base subgroups (e.g. cl_khr_subgroups).</param>
        /// <param name="shuffleExts">Extensions for shuffle (cl_khr_subgroup_shuffle, etc.). Empty for Intel.</param>
        internal void AddSubGroupExtensions(
            ImmutableArray<string> subgroupExts,
            ImmutableArray<string> shuffleExts)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            builder.AddRange(Extensions);
            builder.AddRange(subgroupExts);
            builder.AddRange(shuffleExts);
            Extensions = builder.ToImmutable();
        }

        #endregion
    }
}