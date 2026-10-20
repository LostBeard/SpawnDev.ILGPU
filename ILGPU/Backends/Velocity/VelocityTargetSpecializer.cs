// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2023-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityTargetSpecializer.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using ILGPU.Backends.IL;
using ILGPU.IR.Values;
using ILGPU.Runtime.Velocity;
using ILGPU.Util;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace ILGPU.Backends.Velocity
{
    /// <summary>
    /// An abstract target specializer used to generated vectorized instructions.
    /// </summary>
    abstract class VelocityTargetSpecializer
    {
        #region Static

        internal static MethodInfo GetMethod<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicMethods)] T>(
            string name) =>
            typeof(T)
                .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
                .AsNotNull();

        private static readonly MethodInfo MemoryBarrierMethod =
            GetMethod<VelocityTargetSpecializer>(nameof(MemoryBarrier));
        private static readonly MethodInfo GroupBarrierMethod =
            GetMethod<VelocityTargetSpecializer>(nameof(GroupBarrierImpl));
        private static readonly MethodInfo GroupSumMethod =
            GetMethod<VelocityTargetSpecializer>(nameof(GroupSumImpl));

        private static readonly MethodInfo GetDynamicSharedMemoryMethod =
            GetMethod<VelocityTargetSpecializer>(
                nameof(GetDynamicSharedMemoryImpl));
        private static readonly MethodInfo GetDynamicSharedMemoryLengthInBytesMethod =
            GetMethod<VelocityTargetSpecializer>(
                nameof(GetDynamicSharedMemoryLengthInBytesImpl));
        private static readonly MethodInfo GetSharedMemoryFromPoolMethod =
            GetMethod<VelocityTargetSpecializer>(nameof(GetSharedMemoryFromPoolImpl));
        private static readonly MethodInfo GetLocalMemoryFromPoolMethod =
            GetMethod<VelocityTargetSpecializer>(nameof(GetLocalMemoryFromPoolImpl));
        private static readonly MethodInfo DebuggerBreakMethod =
            GetMethod<VelocityTargetSpecializer>(nameof(DebuggerBreakImpl));

        /// <summary>
        /// Wrapper around an Interlocked memory barrier.
        /// </summary>
        internal static void MemoryBarrier() => Interlocked.MemoryBarrier();

        /// <summary>
        /// A group barrier: the warps of the group meet.
        /// </summary>
        internal static void GroupBarrierImpl(VelocityGroupExecutionContext context) =>
            context.Barrier();

        /// <summary>
        /// Sums a value over the warps of the group.
        /// </summary>
        internal static int GroupSumImpl(
            int value,
            VelocityGroupExecutionContext context) =>
            context.GroupSum(value);

        /// <summary>
        /// Wrapper around a group extension context.
        /// </summary>
        internal static int GetDynamicSharedMemoryLengthInBytesImpl<T>(
            VelocityGroupExecutionContext context)
            where T : unmanaged
        {
            int elementSize = Interop.SizeOf<T>();
            return context.DynamicSharedMemory.IntLength / elementSize;
        }

        /// <summary>
        /// Wrapper around a group extension context.
        /// </summary>
        internal static long GetDynamicSharedMemoryImpl(
            VelocityGroupExecutionContext context) =>
            context.DynamicSharedMemory.LoadEffectiveAddressAsPtr().ToInt64();

        /// <summary>
        /// Wrapper around a group extension context.
        /// </summary>
        internal static long GetSharedMemoryFromPoolImpl<T>(
            VelocityGroupExecutionContext context,
            int length)
            where T : unmanaged =>
            context.GetSharedMemoryFromPool<T>(length)
                .LoadEffectiveAddressAsPtr()
                .ToInt64();

        /// <summary>
        /// Wrapper around a group extension context.
        /// </summary>
        internal static long GetLocalMemoryFromPoolImpl(
            VelocityGroupExecutionContext context,
            int lengthInBytes) =>
            context.GetLocalMemoryFromPool(lengthInBytes)
                .LoadEffectiveAddressAsPtr()
                .ToInt64();

        /// <summary>
        /// Wrapper around a debugger command.
        /// </summary>
        internal static void DebuggerBreakImpl() => Debugger.Break();

        #endregion

        #region Instance

        protected VelocityTargetSpecializer(
            int warpSize,
            Type warpType32,
            Type warpType64)
        {
            WarpSize = warpSize;
            WarpType32 = warpType32;
            WarpType64 = warpType64;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the warp size associated with this target specializer.
        /// </summary>
        public int WarpSize { get; }

        /// <summary>
        /// Returns the type representing a current warp value instance operating on 32
        /// bit values.
        /// </summary>
        public Type WarpType32 { get; }

        /// <summary>
        /// Returns the type representing a current warp value instance operating on 64
        /// bit values.
        /// </summary>
        public Type WarpType64 { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new type generator using the runtime system provided.
        /// </summary>
        /// <param name="capabilityContext">
        /// The parent capabilities system to use.
        /// </param>
        /// <param name="runtimeSystem">The parent runtime system to use.</param>
        public abstract VelocityTypeGenerator CreateTypeGenerator(
            VelocityCapabilityContext capabilityContext,
            RuntimeSystem runtimeSystem);

        #endregion

        #region General

        public abstract void LoadLaneIndexVector32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void LoadLaneIndexVector64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void LoadWarpSizeVector32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void LoadWarpSizeVector64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region Masks

        public abstract void PushAllLanesMask32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void PushNoLanesMask32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConvertMask32To64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConvertMask64To32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void IntersectMask32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void IntersectMask64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void UnifyMask32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void UnifyMask64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void NegateMask32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void NegateMask64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConditionalSelect32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConditionalSelect64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void CheckForAnyActiveLaneMask<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void CheckForNoActiveLaneMask<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void CheckForEqualMasks<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void GetNumberOfActiveLanes<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region Scalar Values

        public abstract void LoadWarpSize32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void LoadWarpSize64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConvertBoolScalar<TILEmitter>(TILEmitter emitter, bool value)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConvertScalarTo32<TILEmitter>(
            TILEmitter emitter,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConvertScalarTo64<TILEmitter>(
            TILEmitter emitter,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region Comparisons

        public abstract void Compare32<TILEmitter>(
            TILEmitter emitter,
            CompareKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void Compare64<TILEmitter>(
            TILEmitter emitter,
            CompareKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region Conversions

        public abstract void ConvertSoftware32<TILEmitter>(
            TILEmitter emitter,
            ArithmeticBasicValueType sourceType,
            ArithmeticBasicValueType targetType)
            where TILEmitter : struct, IILEmitter;

        public abstract void ConvertSoftware64<TILEmitter>(
            TILEmitter emitter,
            ArithmeticBasicValueType sourceType,
            ArithmeticBasicValueType targetType)
            where TILEmitter : struct, IILEmitter;

        public abstract void Convert32<TILEmitter>(
            TILEmitter emitter,
            VelocityWarpOperationMode source,
            VelocityWarpOperationMode target)
            where TILEmitter : struct, IILEmitter;

        public abstract void Convert64<TILEmitter>(
            TILEmitter emitter,
            VelocityWarpOperationMode source,
            VelocityWarpOperationMode target)
            where TILEmitter : struct, IILEmitter;

        public abstract void Convert32To64<TILEmitter>(
            TILEmitter emitter,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void Convert64To32<TILEmitter>(
            TILEmitter emitter,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region Arithmetics

        public abstract void UnaryOperation32<TILEmitter>(
            TILEmitter emitter,
            UnaryArithmeticKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void UnaryOperation64<TILEmitter>(
            TILEmitter emitter,
            UnaryArithmeticKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void BinaryOperation32<TILEmitter>(
            TILEmitter emitter,
            BinaryArithmeticKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void BinaryOperation64<TILEmitter>(
            TILEmitter emitter,
            BinaryArithmeticKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void TernaryOperation32<TILEmitter>(
            TILEmitter emitter,
            TernaryArithmeticKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void TernaryOperation64<TILEmitter>(
            TILEmitter emitter,
            TernaryArithmeticKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region Atomics

        public abstract void AtomicCompareExchange32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void AtomicCompareExchange64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Atomic32<TILEmitter>(
            TILEmitter emitter,
            AtomicKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        public abstract void Atomic64<TILEmitter>(
            TILEmitter emitter,
            AtomicKind kind,
            VelocityWarpOperationMode mode)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region Threads

        /// <summary>
        /// Emits a group barrier: the warps of the thread group meet (the execution
        /// context is the first argument of every generated method).
        /// </summary>
        public virtual void Barrier<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter
        {
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.EmitCall(GroupBarrierMethod);
        }

        /// <summary>
        /// Emits the sum over the warps of the thread group of the int on the stack.
        /// </summary>
        public void GroupSum<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter
        {
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.EmitCall(GroupSumMethod);
        }

        public abstract void BarrierPopCount32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void BarrierAnd32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void BarrierOr32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Shuffle32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ShuffleUp32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void SubShuffleUp32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ShuffleDown32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void SubShuffleDown32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void ShuffleXor32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void SubShuffleXor32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        #endregion

        #region IO

        public abstract void Load8<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Load16<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Load32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Load64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Converts the bits of a low precision float (Half, BFloat16, FP8, FP4) on the
        /// stack into the float lanes it is computed with.
        /// </summary>
        public abstract void DecodeNarrow<TILEmitter>(
            TILEmitter emitter,
            BasicValueType basicValueType)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Converts the float lanes of a low precision float (Half, BFloat16, FP8, FP4)
        /// on the stack into its bits.
        /// </summary>
        public abstract void EncodeNarrow<TILEmitter>(
            TILEmitter emitter,
            BasicValueType basicValueType)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Rounds the float lanes on the stack to the given low precision float.
        /// </summary>
        public void RoundNarrow<TILEmitter>(
            TILEmitter emitter,
            BasicValueType basicValueType)
            where TILEmitter : struct, IILEmitter
        {
            EncodeNarrow(emitter, basicValueType);
            DecodeNarrow(emitter, basicValueType);
        }

        /// <summary>
        /// Sign extends the raw nibbles (0 to 15) on the stack.
        /// </summary>
        public abstract void SignExtendNibble<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Reduces the integers on the stack to their raw nibble (0 to 15).
        /// </summary>
        public abstract void MaskNibble<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Loads the raw nibbles (mask and nibble addresses on the stack).
        /// </summary>
        public abstract void LoadNibble<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Stores the raw nibbles (mask, nibble addresses and values on the stack).
        /// </summary>
        public abstract void StoreNibble<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Loads a value of the given type.
        /// </summary>
        /// <param name="emitter">The emitter.</param>
        /// <param name="basicValueType">The type to load.</param>
        /// <param name="nibbleAddress">
        /// True if the address of a packed 4bit type counts nibbles (the elements of
        /// views), false if it is the address of a byte holding the nibble (the field of
        /// a structure).
        /// </param>
        public void Load<TILEmitter>(
            TILEmitter emitter,
            BasicValueType basicValueType,
            bool nibbleAddress = true)
            where TILEmitter : struct, IILEmitter
        {
            switch (basicValueType)
            {
                case BasicValueType.Int1:
                case BasicValueType.Int8:
                    Load8(emitter);
                    break;
                case BasicValueType.Int16:
                    Load16(emitter);
                    break;
                case BasicValueType.Float16:
                case BasicValueType.BFloat16:
                    Load16(emitter);
                    DecodeNarrow(emitter, basicValueType);
                    break;
                case BasicValueType.Float8E4M3:
                case BasicValueType.Float8E5M2:
                    Load8(emitter);
                    DecodeNarrow(emitter, basicValueType);
                    break;
                case BasicValueType.QInt4:
                    if (nibbleAddress)
                    {
                        LoadNibble(emitter);
                    }
                    else
                    {
                        Load8(emitter);
                        MaskNibble(emitter);
                    }
                    break;
                case BasicValueType.Float4E2M1:
                    if (nibbleAddress)
                    {
                        LoadNibble(emitter);
                    }
                    else
                    {
                        Load8(emitter);
                        MaskNibble(emitter);
                    }
                    DecodeNarrow(emitter, basicValueType);
                    break;
                case BasicValueType.Int32:
                case BasicValueType.Float32:
                    Load32(emitter);
                    break;
                case BasicValueType.Int64:
                case BasicValueType.Float64:
                    Load64(emitter);
                    break;
            }
        }

        public abstract void Store8<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Store16<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Store32<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void Store64<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        /// <summary>
        /// Stores a value of the given type (see <see cref="Load{TILEmitter}"/> for the
        /// addresses of packed 4bit types).
        /// </summary>
        public void Store<TILEmitter>(
            TILEmitter emitter,
            BasicValueType basicValueType,
            bool nibbleAddress = true)
            where TILEmitter : struct, IILEmitter
        {
            switch (basicValueType)
            {
                case BasicValueType.Int1:
                case BasicValueType.Int8:
                    Store8(emitter);
                    break;
                case BasicValueType.Int16:
                    Store16(emitter);
                    break;
                case BasicValueType.Float16:
                case BasicValueType.BFloat16:
                    EncodeNarrow(emitter, basicValueType);
                    Store16(emitter);
                    break;
                case BasicValueType.Float8E4M3:
                case BasicValueType.Float8E5M2:
                    EncodeNarrow(emitter, basicValueType);
                    Store8(emitter);
                    break;
                case BasicValueType.QInt4:
                    if (nibbleAddress)
                        StoreNibble(emitter);
                    else
                        Store8(emitter);
                    break;
                case BasicValueType.Float4E2M1:
                    EncodeNarrow(emitter, basicValueType);
                    if (nibbleAddress)
                        StoreNibble(emitter);
                    else
                        Store8(emitter);
                    break;
                case BasicValueType.Int32:
                case BasicValueType.Float32:
                    Store32(emitter);
                    break;
                case BasicValueType.Int64:
                case BasicValueType.Float64:
                    Store64(emitter);
                    break;
            }
        }

        #endregion

        #region Misc

        public static void MemoryBarrier<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter =>
            emitter.EmitCall(MemoryBarrierMethod);

        public void GetDynamicSharedMemory<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter
        {
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.EmitCall(GetDynamicSharedMemoryMethod);

            // Convert the scalar version into a warp-wide value
            ConvertScalarTo64(emitter, VelocityWarpOperationMode.U);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = global::ILGPU.Util.TrimmingAnnotations.UnconstrainedGeneric)]
        public void GetDynamicSharedMemoryLength<TILEmitter>(
            TILEmitter emitter,
            Type type)
            where TILEmitter : struct, IILEmitter
        {
            // Get the base pointer from the shared pool
            var method = GetDynamicSharedMemoryLengthInBytesMethod
                .MakeGenericMethod(type);
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.EmitCall(method);

            // Convert the scalar version into a warp-wide value
            ConvertScalarTo32(emitter, VelocityWarpOperationMode.U);
        }

        [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = global::ILGPU.Util.TrimmingAnnotations.UnconstrainedGeneric)]
        public void GetSharedMemoryFromPool<TILEmitter>(
            TILEmitter emitter,
            Type type,
            int length)
            where TILEmitter : struct, IILEmitter
        {
            // Get the base pointer from the shared pool
            var method = GetSharedMemoryFromPoolMethod.MakeGenericMethod(type);
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.LoadIntegerConstant(length);
            emitter.EmitCall(method);

            // Convert the scalar version into a warp-wide value
            ConvertScalarTo64(emitter, VelocityWarpOperationMode.U);
        }

        public void GetUnifiedLocalMemoryFromPool<TILEmitter>(
            TILEmitter emitter,
            int lengthInBytes)
            where TILEmitter : struct, IILEmitter
        {
            // Get the base pointer from the local pool
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.LoadIntegerConstant(lengthInBytes);
            emitter.EmitCall(GetLocalMemoryFromPoolMethod);

            // Convert the scalar version into a warp-wide value
            ConvertScalarTo64(emitter, VelocityWarpOperationMode.U);
        }

        public static void DebuggerBreak<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter =>
            emitter.EmitCall(DebuggerBreakMethod);

        public abstract void DebugAssertFailed<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void WriteToOutput<TILEmitter>(TILEmitter emitter)
            where TILEmitter : struct, IILEmitter;

        public abstract void DumpWarp32<TILEmitter>(
            TILEmitter emitter,
            string? label = null)
            where TILEmitter : struct, IILEmitter;

        public abstract void DumpWarp64<TILEmitter>(
            TILEmitter emitter,
            string? label = null)
            where TILEmitter : struct, IILEmitter;

        public void DupDumpAndBreak32<TILEmitter>(
            TILEmitter emitter,
            string? label = null)
            where TILEmitter : struct, IILEmitter
        {
            emitter.Emit(OpCodes.Dup);
            DumpWarp32(emitter, label);
            DebuggerBreak(emitter);
        }

        public void DupDumpAndBreak64<TILEmitter>(
            TILEmitter emitter,
            string? label = null)
            where TILEmitter : struct, IILEmitter
        {
            emitter.Emit(OpCodes.Dup);
            DumpWarp64(emitter, label);
            DebuggerBreak(emitter);
        }

        #endregion
    }
}
