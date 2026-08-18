// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: KernelLauncherBuilder.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.IL;
using ILGPU.Resources;
using ILGPU.Util;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Builder methods for kernel launchers.
    /// </summary>
    static class KernelLauncherBuilder
    {
        #region Methods

        /// <summary>
        /// Emits code to load a 3D dimension of a grid or a group index.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="indexType">
        /// The index type (can be Index1D, Index2D or Index3D).
        /// </param>
        /// <param name="emitter">The target IL emitter.</param>
        /// <param name="loadIdx">
        /// A callback to load the referenced index value onto the stack.
        /// </param>
        /// <param name="manipulateIdx">
        /// A callback to manipulate the loaded index of a given dimension.
        /// </param>
        [UnconditionalSuppressMessage("Trimming", "IL2070",
            Justification = TrimmingAnnotations.EmittedType)]
        private static void EmitLoadDimensions<TEmitter>(
            Type indexType,
            in TEmitter emitter,
            Action loadIdx,
            Action<int> manipulateIdx)
            where TEmitter : struct, IILEmitter
        {
            var indexFieldGetter = new MethodInfo?[]
            {
                indexType.GetProperty(
                    nameof(Index3D.X),
                    BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(false),
                indexType.GetProperty(
                    nameof(Index3D.Y),
                    BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(false),
                indexType.GetProperty(
                    nameof(Index3D.Z),
                    BindingFlags.Public | BindingFlags.Instance)?.GetGetMethod(false),
            };

            // Load field indices
            int offset = 0;
            for (int e = indexFieldGetter.Length; offset < e; ++offset)
            {
                var fieldGetter = indexFieldGetter[offset];
                if (fieldGetter == null)
                    break;
                loadIdx();
                emitter.EmitCall(fieldGetter);
                manipulateIdx(offset);
            }

            // Fill empty zeros
            for (; offset < indexFieldGetter.Length; ++offset)
                emitter.Emit(OpCodes.Ldc_I4_1);
        }

        /// <summary>
        /// Emits code for loading a <see cref="SharedMemorySpecification"/> instance.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="entryPoint">The entry point for code generation.</param>
        /// <param name="emitter">The target IL emitter.</param>
        public static void EmitSharedMemorySpeficiation<TEmitter>(
            EntryPoint entryPoint,
            in TEmitter emitter)
            where TEmitter : struct, IILEmitter
        {
            emitter.EmitConstant(entryPoint.SharedMemory.StaticSize);
            emitter.Emit(
                entryPoint.SharedMemory.HasDynamicMemory ?
                OpCodes.Ldc_I4_1 :
                OpCodes.Ldc_I4_0);
            emitter.EmitNewObject(SharedMemorySpecification.Constructor);
        }

        /// <summary>
        /// Emits a kernel-dimension configuration. In the case of an ungrouped index
        /// type, all arguments will be transformed into a <see cref="KernelConfig"/>
        /// instance. Otherwise, the passed kernel configuration will be used without
        /// any modifications.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="entryPoint">The entry point.</param>
        /// <param name="emitter">The target IL emitter.</param>
        /// <param name="dimensionIdx">
        /// The argument index of the provided launch-dimension index.
        /// </param>
        /// <param name="maxGridSize">The max grid dimensions.</param>
        /// <param name="maxGroupSize">The max group dimensions.</param>
        /// <param name="customGroupSize">
        /// The custom group size used for automatic blocking.
        /// </param>
        public static void EmitLoadKernelConfig<TEmitter>(
            EntryPoint entryPoint,
            TEmitter emitter,
            int dimensionIdx,
            in Index3D maxGridSize,
            in Index3D maxGroupSize,
            int customGroupSize = 0)
            where TEmitter : struct, IILEmitter
        {
            if (entryPoint.IsImplicitlyGrouped)
            {
                Debug.Assert(customGroupSize >= 0, "Invalid custom group size");

                // Note that the custom group size is currently limited
                // to the X dimension of the scheduled grid.
                // This is no limitation of the current PTX backend.

                // Declare a local for the runtime-computed effective group size.
                // For customGroupSize >= 1: effective = min(extent.X, customGroupSize).
                // This clamps the group size when the launch extent is smaller than
                // the auto-grouped block size — the extra threads are wasted hardware
                // (only one thread does work per work item), and on register-heavy
                // kernels the wasted threads still consume register budget per-thread,
                // pushing CUDA past `cuFuncGetAttribute(MaxThreadsPerBlock)` and
                // triggering CUDA_ERROR_LAUNCH_OUT_OF_RESOURCES at launch time.
                // Tuvok's SilkDecodeCoreGpu (15-ArrayView body struct + LPC unroll)
                // surfaced this 2026-05-04 with Index1D=1 dispatch picking groupSize=64.
                // For customGroupSize < 1 (legacy path): emit constant 1 directly.
                ILLocal? effectiveGroupSizeLocal = customGroupSize >= 1
                    ? emitter.DeclareLocal(typeof(int))
                    : (ILLocal?)null;
                var mathMinIntInt = typeof(Math).GetMethod(
                    nameof(Math.Min),
                    new[] { typeof(int), typeof(int) }).AsNotNull();

                EmitLoadDimensions(
                    entryPoint.KernelIndexType,
                    emitter,
                    () => emitter.Emit(ArgumentOperation.LoadAddress, dimensionIdx),
                    dimIdx =>
                    {
                        if (dimIdx != 0 || customGroupSize < 1)
                            return;
                        // Stack: [extent.X]
                        // Compute effective = min(extent.X, customGroupSize) and store
                        // to local for re-use as groupDim.X below.
                        emitter.Emit(OpCodes.Dup);
                        emitter.EmitConstant(customGroupSize);
                        emitter.EmitCall(mathMinIntInt);
                        emitter.Emit(LocalOperation.Store, effectiveGroupSizeLocal!.Value);
                        // gridDim.X = (extent.X + effective - 1) / effective.
                        emitter.Emit(LocalOperation.Load, effectiveGroupSizeLocal!.Value);
                        emitter.Emit(OpCodes.Add);
                        emitter.EmitConstant(1);
                        emitter.Emit(OpCodes.Sub);
                        emitter.Emit(LocalOperation.Load, effectiveGroupSizeLocal!.Value);
                        emitter.Emit(OpCodes.Div);
                    });

                // groupDim.X = effective (clamped) when customGroupSize >= 1; else 1.
                if (customGroupSize >= 1)
                    emitter.Emit(LocalOperation.Load, effectiveGroupSizeLocal!.Value);
                else
                    emitter.EmitConstant(1);
                emitter.Emit(OpCodes.Ldc_I4_1);
                emitter.Emit(OpCodes.Ldc_I4_1);

                // The IL stack contains 6 ints to be used as parameters to the
                // KernelConfig constructor. Convert these into Index3 instances.
                var groupDimLocal = emitter.DeclareLocal(typeof(Index3D));
                var gridDimLocal = emitter.DeclareLocal(typeof(Index3D));
                emitter.EmitNewObject(Index3D.MainConstructor);
                emitter.Emit(LocalOperation.Store, groupDimLocal);
                emitter.EmitNewObject(Index3D.MainConstructor);
                emitter.Emit(LocalOperation.Store, gridDimLocal);

                // Verify the grid and group dimensions.
                emitter.Emit(LocalOperation.Load, gridDimLocal);
                emitter.Emit(LocalOperation.Load, groupDimLocal);
                EmitVerifyKernelLaunchBounds(emitter, maxGridSize, maxGroupSize);

                // Create the KernelConfig.
                emitter.Emit(LocalOperation.Load, gridDimLocal);
                emitter.Emit(LocalOperation.Load, groupDimLocal);
                emitter.EmitNewObject(KernelConfig.ImplicitlyGroupedKernelConstructor);
            }
            else
            {
                Debug.Assert(customGroupSize == 0, "Invalid custom group size");

                emitter.Emit(ArgumentOperation.Load, dimensionIdx);

                // The KernelConfig has already been created by the caller, so verify the
                // grid and group dimensions using the values from the KernelConfig.
                var kernelCfgLocal = emitter.DeclareLocal(typeof(KernelConfig));
                emitter.Emit(OpCodes.Dup);
                emitter.Emit(LocalOperation.Store, kernelCfgLocal);
                emitter.Emit(LocalOperation.LoadAddress, kernelCfgLocal);
                emitter.EmitCall(
                    typeof(KernelConfig)
                    .GetProperty(
                        nameof(KernelConfig.GridDim),
                        BindingFlags.Public | BindingFlags.Instance)
                    .AsNotNull()
                    .GetGetMethod()
                    .AsNotNull());
                emitter.Emit(LocalOperation.LoadAddress, kernelCfgLocal);
                emitter.EmitCall(
                    typeof(KernelConfig)
                    .GetProperty(
                        nameof(KernelConfig.GroupDim),
                        BindingFlags.Public | BindingFlags.Instance)
                    .AsNotNull()
                    .GetGetMethod()
                    .AsNotNull());
                EmitVerifyKernelLaunchBounds(emitter, maxGridSize, maxGroupSize);
            }
        }

        /// <summary>
        /// Emits IL instructions to verify the kernel launch bounds.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="emitter">The target IL emitter.</param>
        /// <param name="maxGridSize">The max grid dimensions.</param>
        /// <param name="maxGroupSize">The max group dimensions.</param>
        private static void EmitVerifyKernelLaunchBounds<TEmitter>(
            TEmitter emitter,
            in Index3D maxGridSize,
            in Index3D maxGroupSize)
            where TEmitter : struct, IILEmitter
        {
            // NOTE: Requires that the top two elements of the IL stack contain the
            // grid and group dimensions (as Index3) to be tested.
            emitter.EmitConstant(maxGridSize.X);
            emitter.EmitConstant(maxGridSize.Y);
            emitter.EmitConstant(maxGridSize.Z);
            emitter.EmitNewObject(Index3D.MainConstructor);
            emitter.EmitConstant(maxGroupSize.X);
            emitter.EmitConstant(maxGroupSize.Y);
            emitter.EmitConstant(maxGroupSize.Z);
            emitter.EmitNewObject(Index3D.MainConstructor);
            emitter.EmitCall(
                typeof(KernelLauncherBuilder).GetMethod(
                    nameof(VerifyKernelLaunchBounds),
                    BindingFlags.NonPublic | BindingFlags.Static)
                .AsNotNull());
        }

        /// <summary>
        /// Helper function used to verify the kernel launch dimensions.
        /// </summary>
        /// <param name="gridDim">Kernel launch grid dimensions.</param>
        /// <param name="groupDim">Kernel launch group dimensions.</param>
        /// <param name="maxGridSize">Accelerator max grid dimensions.</param>
        /// <param name="maxGroupSize">Accelerator max group dimensions.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void VerifyKernelLaunchBounds(
            Index3D gridDim,
            Index3D groupDim,
            Index3D maxGridSize,
            Index3D maxGroupSize)
        {
            if (!gridDim.InBoundsInclusive(maxGridSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(gridDim),
                    string.Format(
                        RuntimeErrorMessages.InvalidKernelLaunchGridDimension,
                        gridDim,
                        maxGridSize));
            }
            if (!groupDim.InBoundsInclusive(maxGroupSize))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(groupDim),
                    string.Format(
                        RuntimeErrorMessages.InvalidKernelLaunchGroupDimension,
                        groupDim,
                        maxGroupSize));
            }
        }

        /// <summary>
        /// Emits a new runtime kernel configuration.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="entryPoint">The entry point.</param>
        /// <param name="emitter">The target IL emitter.</param>
        /// <param name="dimensionIdx">
        /// The argument index of the provided launch-dimension index.
        /// </param>
        /// <param name="maxGridSize">The max grid dimensions.</param>
        /// <param name="maxGroupSize">The max group dimensions.</param>
        /// <param name="customGroupSize">
        /// The custom group size used for automatic blocking.
        /// </param>
        public static void EmitLoadRuntimeKernelConfig<TEmitter>(
            EntryPoint entryPoint,
            TEmitter emitter,
            int dimensionIdx,
            in Index3D maxGridSize,
            in Index3D maxGroupSize,
            int customGroupSize = 0)
            where TEmitter : struct, IILEmitter
        {
            EmitLoadKernelConfig(
                entryPoint,
                emitter,
                dimensionIdx,
                maxGridSize,
                maxGroupSize,
                customGroupSize);
            EmitSharedMemorySpeficiation(entryPoint, emitter);

            emitter.EmitNewObject(RuntimeKernelConfig.Constructor);
        }

        /// <summary>
        /// Emits code for loading a typed kernel from a generic kernel instance.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <typeparam name="T">The kernel type.</typeparam>
        /// <param name="kernelArgumentIndex">
        /// The index of the launcher parameter.
        /// </param>
        /// <param name="emitter">The target IL emitter.</param>
        public static void EmitLoadKernelArgument<T, TEmitter>(
            int kernelArgumentIndex,
            in TEmitter emitter)
            where T : Kernel
            where TEmitter : struct, IILEmitter
        {
            Debug.Assert(kernelArgumentIndex >= 0);

            emitter.Emit(ArgumentOperation.Load, kernelArgumentIndex);
            emitter.Emit(OpCodes.Castclass, typeof(T));
        }

        /// <summary>
        /// Emits code for loading a typed accelerator stream from a generic
        /// accelerator-stream instance.
        /// </summary>
        /// <typeparam name="T">The kernel type.</typeparam>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="streamArgumentIndex">The index of the stream parameter.</param>
        /// <param name="emitter">The target IL emitter.</param>
        public static void EmitLoadAcceleratorStream<T, TEmitter>(
            int streamArgumentIndex,
            in TEmitter emitter)
            where T : AcceleratorStream
            where TEmitter : struct, IILEmitter
        {
            Debug.Assert(streamArgumentIndex >= 0);

            emitter.Emit(ArgumentOperation.Load, streamArgumentIndex);
            emitter.Emit(OpCodes.Castclass, typeof(T));
        }

        #endregion
    }
}
