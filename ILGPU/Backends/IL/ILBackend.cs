// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: ILBackend.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.IL.Transformations;
using ILGPU.IR;
using ILGPU.IR.Transformations;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Resources;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Util;
using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU.Backends.IL
{
    /// <summary>
    /// The basic MSIL backend for the CPU runtime.
    /// </summary>
    public abstract class ILBackend : Backend<ILBackend.Handler>
    {
        #region Nested Types

        /// <summary>
        /// Represents the handler delegate type of custom code-generation handlers.
        /// </summary>
        /// <param name="backend">The current backend.</param>
        /// <param name="emitter">The current emitter.</param>
        /// <param name="value">The value to generate code for.</param>
        public delegate void Handler(
            ILBackend backend,
            in ILEmitter emitter,
            Value value);

        #endregion

        #region Static

        /// <summary>
        /// A reference to the static <see cref="Reconstruct2DIndex(Index2D, int)"/>
        /// method.
        /// </summary>
        private static readonly MethodInfo Reconstruct2DIndexMethod =
            typeof(ILBackend).GetMethod(
                nameof(Reconstruct2DIndex),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// A reference to the static <see cref="Reconstruct3DIndex(Index3D, int)"/>
        /// method.
        /// </summary>
        private static readonly MethodInfo Reconstruct3DIndexMethod =
            typeof(ILBackend).GetMethod(
                nameof(Reconstruct3DIndex),
                BindingFlags.NonPublic | BindingFlags.Static)
            .ThrowIfNull();

        /// <summary>
        /// Helper method to reconstruct 2D indices.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Index2D Reconstruct2DIndex(Index2D totalDim, int linearIndex) =>
            Stride2D.DenseX.ReconstructFromElementIndex(linearIndex, totalDim);

        /// <summary>
        /// Helper method to reconstruct 3D indices.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Index3D Reconstruct3DIndex(Index3D totalDim, int linearIndex) =>
            Stride3D.DenseXY.ReconstructFromElementIndex(linearIndex, totalDim);

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new IL backend.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="capabilities">The supported capabilities.</param>
        /// <param name="warpSize">The current warp size.</param>
        /// <param name="argumentMapper">The argument mapper to use.</param>
        internal ILBackend(
            Context context,
            CapabilityContext capabilities,
            int warpSize,
            ArgumentMapper argumentMapper)
            : base(
                  context,
                  capabilities,
                  BackendType.IL,
                  argumentMapper)
        {
            WarpSize = warpSize;

            InitIntrinsicProvider();
            InitializeKernelTransformers(builder =>
            {
                var transformerBuilder = Transformer.CreateBuilder(
                    TransformerConfiguration.Empty);
                transformerBuilder.AddBackendOptimizations<CodePlacement.GroupOperands>(
                    new ILAcceleratorSpecializer(
                        PointerType,
                        warpSize,
                        Context.Properties.EnableAssertions,
                        Context.Properties.EnableIOOperations),
                    context.Properties.InliningMode,
                    context.Properties.OptimizationLevel);
                builder.Add(transformerBuilder.ToTransformer());
            });
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated warp size.
        /// </summary>
        public int WarpSize { get; }

        /// <summary>
        /// Returns the associated <see cref="Backend.ArgumentMapper"/>.
        /// </summary>
        public new ILArgumentMapper ArgumentMapper =>
            base.ArgumentMapper.AsNotNullCast<ILArgumentMapper>();

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new <see cref="ILCompiledKernel"/> instance.
        /// </summary>
        protected sealed override CompiledKernel Compile(
            EntryPoint entryPoint,
            in BackendContext backendContext,
            in KernelSpecialization specialization)
        {
            // The CPU (IL) backend executes the original managed kernel method directly, so an
            // in-kernel `packedView[i] = value` runs the managed ArrayView indexer, whose ref model
            // cannot address a single nibble of a packed sub-byte buffer (QInt4/QUInt4). A nibble
            // store would silently write to a per-thread scratch and lose the result. Fail loud at
            // compile time instead. (Reading packed views IS supported - the indexer decodes the
            // nibble by value; only in-kernel writes are unsupported on this backend.)
            VerifyNoPackedSubByteStores(backendContext);

            // Build the custom strongly type task type and define the kernel method
            var taskType = GenerateAcceleratorTask(
                entryPoint.Parameters,
                out ConstructorInfo taskConstructor,
                out ImmutableArray<FieldInfo> taskArgumentMapping);

            MethodInfo kernelMethod;
            using (RuntimeSystem.DefineRuntimeMethod(
                typeof(void),
                CPUAcceleratorTask.ExecuteParameterTypes,
                out var methodEmitter))
            {
                var emitter = new ILEmitter(methodEmitter.ILGenerator);

                // Generate CPU runtime startup code and initialize all locals
                GenerateStartupCode(
                    entryPoint,
                    emitter,
                    taskType,
                    out var taskLocal,
                    out var indexLocal);
                var locals = GenerateLocals(
                    emitter,
                    taskArgumentMapping,
                    taskLocal);

                // Generate the actual kernel code
                GenerateCode(
                    entryPoint,
                    backendContext,
                    emitter,
                    taskLocal,
                    indexLocal,
                    locals);

                // Finish building
                emitter.Emit(OpCodes.Ret);
                emitter.Finish();
                kernelMethod = methodEmitter.Finish();
            }

            return new ILCompiledKernel(
                Context,
                entryPoint,
                kernelMethod,
                taskType,
                taskConstructor,
                taskArgumentMapping,
                backendContext.SharedAllocations.Length +
                    backendContext.DynamicSharedAllocations.Length,
                backendContext.SharedMemorySpecification.StaticSize);
        }

        /// <summary>
        /// Throws if the kernel stores into a packed sub-byte view (e.g. QInt4/QUInt4) - the CPU (IL)
        /// backend runs the managed array-view indexer, which returns a ref and cannot write a single
        /// nibble in place, so such a store would be silently lost. Reading packed views is supported.
        /// </summary>
        private static void VerifyNoPackedSubByteStores(in BackendContext backendContext)
        {
            // The kernel method itself (the enumerator below yields only the OTHER, non-kernel
            // methods) plus every callee.
            ScanMethodForPackedStores(backendContext.KernelMethod);
            foreach (var (method, _) in backendContext)
                ScanMethodForPackedStores(method);
        }

        /// <summary>
        /// Throws <see cref="NotSupportedException"/> if the given IR method stores into a packed
        /// sub-byte (QInt4/QUInt4) view - unsupported on the CPU backend (see
        /// <see cref="VerifyNoPackedSubByteStores"/>).
        /// </summary>
        private static void ScanMethodForPackedStores(Method method)
        {
            foreach (var block in method.Blocks)
            {
                foreach (var valueEntry in block)
                {
                    if (valueEntry.Value is Store store &&
                        store.Target.Type is PointerType pt &&
                        (pt.ElementType.BasicValueType == BasicValueType.QInt4
                         || pt.ElementType.BasicValueType == BasicValueType.Float4E2M1))
                    {
                        throw new NotSupportedException(
                            "Packed sub-byte views (QInt4/QUInt4/Float4E2M1) do not support in-kernel " +
                            "element stores on the CPU backend: the managed array-view indexer " +
                            "cannot address a single nibble in place. Use a GPU backend for " +
                            "packed in-kernel writes, or build the packed buffer via an " +
                            "ArrayView<byte>/<uint> with explicit nibble packing. Reading a " +
                            "packed view (e.g. (float)packed[i]) IS supported on the CPU backend.");
                    }
                }
            }
        }

        /// <summary>
        /// Generates the actual kernel code.
        /// </summary>
        /// <typeparam name="TEmitter">The emitter type.</typeparam>
        /// <param name="entryPoint">The desired entry point.</param>
        /// <param name="backendContext">The current backend context.</param>
        /// <param name="emitter">The current code generator.</param>
        /// <param name="task">The strongly typed task local.</param>
        /// <param name="index">The index dimension local (for implicit kernels).</param>
        /// <param name="locals">
        /// The array of all local variables loaded from the task kernel implementation.
        /// </param>
        protected abstract void GenerateCode<TEmitter>(
            EntryPoint entryPoint,
            in BackendContext backendContext,
            TEmitter emitter,
            in ILLocal task,
            in ILLocal index,
            ImmutableArray<ILLocal> locals)
            where TEmitter : struct, IILEmitter;

        #endregion

        #region Kernel Functionality

        /// <summary>
        /// Generates code that caches all task fields in local variables.
        /// </summary>
        /// <param name="emitter">The current code generator.</param>
        /// <param name="taskArgumentMapping">
        /// The created task-argument mapping that maps parameter indices of uniforms
        /// and dynamically-sized shared-memory-variable-length specifications to fields
        /// in the task class.
        /// </param>
        /// <param name="task">The strongly typed task local.</param>
        private static ImmutableArray<ILLocal> GenerateLocals<TEmitter>(
            TEmitter emitter,
            ImmutableArray<FieldInfo> taskArgumentMapping,
            ILLocal task)
            where TEmitter : struct, IILEmitter
        {
            // Cache all fields in local variables
            var taskArgumentLocals = ImmutableArray.CreateBuilder<ILLocal>(
                taskArgumentMapping.Length);

            for (int i = 0, e = taskArgumentMapping.Length; i < e; ++i)
            {
                var taskArgument = taskArgumentMapping[i];
                var taskArgumentType = taskArgument.FieldType;

                // Load instance field i
                emitter.Emit(LocalOperation.Load, task);
                emitter.Emit(OpCodes.Ldfld, taskArgumentMapping[i]);

                // Declare local
                taskArgumentLocals.Add(emitter.DeclareLocal(taskArgumentType));

                // Cache field value in local variable
                emitter.Emit(LocalOperation.Store, taskArgumentLocals[i]);
            }

            return taskArgumentLocals.MoveToImmutable();
        }

        /// <summary>
        /// Generates specialized task classes for kernel execution.
        /// </summary>codeEmitter
        /// <param name="parameters">The parameter collection.</param>
        /// <param name="taskConstructor">The created task constructor.</param>
        /// <param name="taskArgumentMapping">
        /// The created task-argument mapping that maps parameter indices of uniforms
        /// and dynamically-sized shared-memory-variable-length specifications to fields
        /// in the task class.
        /// </param>
        [UnconditionalSuppressMessage("Trimming", "IL2111",
            Justification = TrimmingAnnotations.SelfRegistrar)]
        [UnconditionalSuppressMessage("Trimming", "IL2070",
            Justification = TrimmingAnnotations.EmittedType)]
        private Type GenerateAcceleratorTask(
            in ParameterCollection parameters,
            out ConstructorInfo taskConstructor,
            out ImmutableArray<FieldInfo> taskArgumentMapping)
        {
            const string ArgumentFormat = "Arg{0}";

            var acceleratorTaskType = typeof(CPUAcceleratorTask);
            var argFieldBuilders = new FieldInfo[parameters.Count];

            Type taskType;
            {
                using var scopedLock = RuntimeSystem.DefineRuntimeClass(
                    acceleratorTaskType,
                    out var taskBuilder);

                var ctor = taskBuilder.DefineConstructor(
                    MethodAttributes.Public,
                    CallingConventions.HasThis,
                    CPUAcceleratorTask.ConstructorParameterTypes);

                // Build constructor
                {
                    var constructorILGenerator = ctor.GetILGenerator();
                    constructorILGenerator.Emit(OpCodes.Ldarg_0);
                    for (
                        int i = 0,
                        e = CPUAcceleratorTask.ConstructorParameterTypes.Length;
                        i < e;
                        ++i)
                    {
                        constructorILGenerator.Emit(OpCodes.Ldarg, i + 1);
                    }
                    constructorILGenerator.Emit(
                        OpCodes.Call,
                        CPUAcceleratorTask.GetTaskConstructor(acceleratorTaskType));
                    constructorILGenerator.Emit(OpCodes.Ret);
                }

                // Define all fields
                for (int i = 0, e = argFieldBuilders.Length; i < e; ++i)
                {
                    taskBuilder.DefineField(
                        string.Format(ArgumentFormat, i),
                        parameters[i],
                        FieldAttributes.Public);
                }

                // Create the actual type
                taskType = taskBuilder.CreateType();

                // Get all fields
                for (int i = 0, e = argFieldBuilders.Length; i < e; ++i)
                {
                    argFieldBuilders[i] = taskBuilder.GetField(
                        string.Format(ArgumentFormat, i)).AsNotNull();
                }
            }
            taskConstructor = taskType.GetConstructor(
                CPUAcceleratorTask.ConstructorParameterTypes).AsNotNull();

            // Map the final fields
            var resultMapping = ImmutableArray.CreateBuilder<FieldInfo>(
                parameters.Count);
            for (int i = 0, e = parameters.Count; i < e; ++i)
            {
                resultMapping.Add(
                    taskType.GetField(argFieldBuilders[i].Name).AsNotNull());
            }
            taskArgumentMapping = resultMapping.MoveToImmutable();

            return taskType;
        }

        /// <summary>
        /// Generates kernel startup code.
        /// </summary>
        /// <param name="entryPoint">The entry point.</param>
        /// <param name="emitter">The current code generator.</param>
        /// <param name="taskType">The created task.</param>
        /// <param name="task">The created strongly typed task local.</param>
        /// <param name="index">The index dimension local (for implicit kernels).</param>
        [UnconditionalSuppressMessage("Trimming", "IL2067",
            Justification = TrimmingAnnotations.EmittedType)]
        private static void GenerateStartupCode<TEmitter>(
            EntryPoint entryPoint,
            TEmitter emitter,
            Type taskType,
            out ILLocal task,
            out ILLocal index)
            where TEmitter : struct, IILEmitter
        {
            // Cast generic task type to actual task type
            task = emitter.DeclareLocal(taskType);
            emitter.Emit(OpCodes.Ldarg_0);
            emitter.Emit(OpCodes.Castclass, taskType);
            emitter.Emit(LocalOperation.Store, task);

            // Construct launch index from linear index
            index = emitter.DeclareLocal(entryPoint.KernelIndexType);
            emitter.Emit(LocalOperation.LoadAddress, index);
            emitter.Emit(OpCodes.Initobj, index.VariableType);

            if (entryPoint.IsExplicitlyGrouped)
                return;

            // Convert to the appropriate index type
            emitter.Emit(LocalOperation.Load, task);
            switch (entryPoint.IndexType)
            {
                case IndexType.Index1D:
                    // Ignore the task local and construct a new 1D instance
                    emitter.Emit(OpCodes.Pop);
                    emitter.Emit(ArgumentOperation.Load, CPUAcceleratorTask.LinearIndex);
                    emitter.EmitNewObject(Index1D.MainConstructor);
                    break;
                case IndexType.Index2D:
                    // Convert to 2D index
                    emitter.EmitCall(
                        CPUAcceleratorTask.GetTotalUserDimXYGetter(taskType));
                    emitter.Emit(ArgumentOperation.Load, CPUAcceleratorTask.LinearIndex);
                    emitter.EmitCall(Reconstruct2DIndexMethod);
                    break;
                case IndexType.Index3D:
                    // Convert to 3D index
                    emitter.EmitCall(
                        CPUAcceleratorTask.GetTotalUserDimGetter(taskType));
                    emitter.Emit(ArgumentOperation.Load, CPUAcceleratorTask.LinearIndex);
                    emitter.EmitCall(Reconstruct3DIndexMethod);
                    break;
                default:
                    throw new NotSupportedException(
                        RuntimeErrorMessages.NotSupportedIndexType);

            }
            // Store the index operation
            emitter.Emit(LocalOperation.Store, index);
        }

        #endregion
    }
}
