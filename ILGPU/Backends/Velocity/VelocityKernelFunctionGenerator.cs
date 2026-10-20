// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityKernelFunctionGenerator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.IL;
using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Values;
using ILGPU.Util;
using System;
using System.Reflection;
using System.Reflection.Emit;

namespace ILGPU.Backends.Velocity
{
    /// <summary>
    /// A generator for primary Velocity kernels.
    /// </summary>
    /// <typeparam name="TILEmitter">The IL emitter type.</typeparam>
    sealed class VelocityKernelFunctionGenerator<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TILEmitter> :
        VelocityCodeGenerator<TILEmitter>
        where TILEmitter : struct, IILEmitter
    {
        #region Constants

        public const int GlobalGroupDimIndex = 1;
        public const int GlobalGridDimIndex = 2;
        public const int GlobalStartIndex = 3;
        public const int GlobalEndIndex = 4;
        public const int GlobalParametersIndex = 5;

        #endregion

        private readonly ILLabel localExitMarker;
        private readonly ILLocal targetMaskCount;
        private readonly ILLocal globalIndex;

        /// <summary>
        /// Creates a new Velocity kernel generator.
        /// </summary>
        /// <param name="args">The generator args to use.</param>
        /// <param name="method">The current method to generate code for.</param>
        /// <param name="allocas">All allocations of the current method.</param>
        public VelocityKernelFunctionGenerator(
            in GeneratorArgs args,
            Method method,
            Allocas allocas)
            : base(args, method, allocas)
        {
            EntryPoint = args.EntryPoint;
            ParametersType = args.Module.ParametersType;

            // Generate an exit marker to jump to when the kernel function returns
            localExitMarker = Emitter.DeclareLabel();

            // We use this counter to remember the number of active threads that entered
            // the kernel successfully
            targetMaskCount = Emitter.DeclareLocal(typeof(int));

            // Declare our global thread index local
            globalIndex = Emitter.DeclareLocal(typeof(int));
        }

        /// <summary>
        /// Returns the current entry point.
        /// </summary>
        private static readonly MethodInfo MathMinInt32 = typeof(Math).GetMethod(
            nameof(Math.Min),
            new Type[] { typeof(int), typeof(int) }).AsNotNull();

        public EntryPoint EntryPoint { get; }

        /// <summary>
        /// Returns the current parameters type.
        /// </summary>
        public Type ParametersType { get; }

        /// <summary>
        /// Loads the current global index.
        /// </summary>
        protected override void LoadGlobalIndexScalar() =>
            Emitter.Emit(LocalOperation.Load, globalIndex);

        /// <summary>
        /// Loads the current group dimension.
        /// </summary>
        protected override void LoadGroupDimScalar() =>
            Emitter.Emit(ArgumentOperation.Load, GlobalGroupDimIndex);

        /// <summary>
        /// Loads the current grid dimension.
        /// </summary>
        protected override void LoadGridDimScalar() =>
            Emitter.Emit(ArgumentOperation.Load, GlobalGridDimIndex);

        /// <summary>
        /// Generates Velocity code for this kernel.
        /// </summary>
        public override void GenerateCode()
        {
            // Extract all arguments of the actual parameters object
            var parametersLocal = Emitter.DeclareLocal(ParametersType);
            Emitter.Emit(ArgumentOperation.Load, GlobalParametersIndex);
            Emitter.Emit(OpCodes.Castclass, ParametersType);
            Emitter.Emit(LocalOperation.Store, parametersLocal);

            // Load all parameters by mapping them to local variables
            for (
                int i = EntryPoint.KernelIndexParameterOffset;
                i < Method.NumParameters;
                ++i)
            {
                var parameterType = Method.Parameters[i].ParameterType;
                var parameterLocal = DeclareVectorizedTemporary(parameterType);

                Emitter.Emit(LocalOperation.Load, parametersLocal);
                Emitter.LoadField(
                    ParametersType,
                    i - EntryPoint.KernelIndexParameterOffset);
                Emitter.Emit(LocalOperation.Store, parameterLocal);

                Alias(Method.Parameters[i], parameterLocal);
            }

            // Declare a local index counter and initialize it with the start index
            Emitter.Emit(ArgumentOperation.Load, GlobalStartIndex);
            Emitter.Emit(LocalOperation.Store, globalIndex);

            // The main kernel mask of an iteration depends on how many threads of the
            // current group it covers (see below)
            var baseGroupMask = Emitter.DeclareLocal(Specializer.WarpType32);
            var iterationStep = Emitter.DeclareLocal(typeof(int));

            // Build our execution loop header
            var iterationHeader = Emitter.DeclareLabel();
            Emitter.MarkLabel(iterationHeader);

            // Compare our global index against the exclusive end index
            var exitMarker = Emitter.DeclareLabel();
            Emitter.Emit(LocalOperation.Load, globalIndex);
            Emitter.Emit(ArgumentOperation.Load, GlobalEndIndex);
            Emitter.Emit(OpCodes.Bge, exitMarker);

            // An iteration runs one warp: the next WarpSize threads of the current group,
            // or fewer when the group ends sooner (a group is smaller than a warp, or its
            // size is not a multiple of the warp size). A group of more threads than a
            // warp is processed by consecutive warps; a chunk always starts at the
            // beginning of a group, so (global index % group size) is the number of
            // threads of this group that earlier iterations covered.
            // step = min(WarpSize, groupDim - globalIndex % groupDim)
            Emitter.Emit(ArgumentOperation.Load, GlobalGroupDimIndex);
            Emitter.Emit(LocalOperation.Load, globalIndex);
            Emitter.Emit(ArgumentOperation.Load, GlobalGroupDimIndex);
            Emitter.Emit(OpCodes.Rem);
            Emitter.Emit(OpCodes.Sub);
            Emitter.EmitConstant(Specializer.WarpSize);
            Emitter.EmitCall(MathMinInt32);
            Emitter.Emit(LocalOperation.Store, iterationStep);

            // The lanes of the warp that belong to this group
            Specializer.LoadLaneIndexVector32(Emitter);
            Emitter.Emit(LocalOperation.Load, iterationStep);
            Specializer.ConvertScalarTo32(Emitter, VelocityWarpOperationMode.I);
            Specializer.Compare32(
                Emitter,
                CompareKind.LessThan,
                VelocityWarpOperationMode.I);
            Emitter.Emit(LocalOperation.Store, baseGroupMask);

            // Build our execution body
            {
                // Bind the current implicitly grouped kernel index (if any): the linear
                // thread number, or a structure with the index in every dimension
                var offsetVector = Emitter.DeclareLocal(Specializer.WarpType32);
                ILLocal? indexLocal = null;
                int numIndexDimensions = EntryPoint.NumFlattendedIndexParameters;
                if (EntryPoint.IsImplicitlyGrouped)
                {
                    if (numIndexDimensions < 2)
                    {
                        Alias(Method.Parameters[0], offsetVector);
                    }
                    else
                    {
                        indexLocal = DeclareVectorizedTemporary(
                            Method.Parameters[0].ParameterType);
                        Alias(Method.Parameters[0], indexLocal.Value);
                    }
                }

                // Compute the current global index
                Emitter.Emit(LocalOperation.Load, globalIndex);
                Specializer.ConvertScalarTo32(Emitter, VelocityWarpOperationMode.I);
                Specializer.LoadLaneIndexVector32(Emitter);
                Specializer.BinaryOperation32(
                    Emitter,
                    BinaryArithmeticKind.Add,
                    VelocityWarpOperationMode.I);
                Emitter.Emit(LocalOperation.Store, offsetVector);

                // Adjust the current main kernel mask based on the user grid size
                Emitter.Emit(LocalOperation.Load, offsetVector);
                Emitter.Emit(ArgumentOperation.Load, GlobalEndIndex);
                Specializer.ConvertScalarTo32(Emitter, VelocityWarpOperationMode.I);
                Specializer.Compare32(
                    Emitter,
                    CompareKind.LessThan,
                    VelocityWarpOperationMode.I);
                Emitter.Emit(LocalOperation.Load, baseGroupMask);
                Specializer.IntersectMask32(Emitter);

                // The index of several dimensions: its coordinates are the group index
                // times the group size plus the thread index, and the last group of
                // a row can cover more threads than the extent
                if (indexLocal.HasValue)
                {
                    var indexType = indexLocal.Value.VariableType;
                    for (int i = 0; i < numIndexDimensions; ++i)
                    {
                        var dimension = (DeviceConstantDimension3D)i;
                        Emitter.Emit(LocalOperation.LoadAddress, indexLocal.Value);
                        EmitGridIndex(dimension);
                        LoadContextField(GetGroupDimField(dimension));
                        Specializer.ConvertScalarTo32(
                            Emitter,
                            VelocityWarpOperationMode.I);
                        EmitGroupIndex(dimension);
                        Specializer.TernaryOperation32(
                            Emitter,
                            TernaryArithmeticKind.MultiplyAdd,
                            VelocityWarpOperationMode.I);
                        Emitter.StoreField(indexType, i);
                    }

                    // The first coordinate is the one that can pass the extent
                    Emitter.Emit(LocalOperation.LoadAddress, indexLocal.Value);
                    Emitter.LoadField(indexType, 0);
                    LoadExtentVector(0);
                    Specializer.Compare32(
                        Emitter,
                        CompareKind.LessThan,
                        VelocityWarpOperationMode.I);
                    Specializer.IntersectMask32(Emitter);
                }

                var entryPointMask = GetBlockMask(Method.EntryBlock);
                Emitter.Emit(OpCodes.Dup);
                Emitter.Emit(LocalOperation.Store, entryPointMask);

                // Determine the target mask count
                Specializer.GetNumberOfActiveLanes(Emitter);
                Emitter.Emit(LocalOperation.Store, targetMaskCount);

                // Emit the actual kernel code
                GenerateCodeInternal();

                // Emit the exit marker
                Emitter.MarkLabel(localExitMarker);
            }

            // Increase the processing index by the number of threads this iteration covered
            Emitter.Emit(LocalOperation.Load, globalIndex);
            Emitter.Emit(LocalOperation.Load, iterationStep);
            Emitter.Emit(OpCodes.Add);
            Emitter.Emit(LocalOperation.Store, globalIndex);

            // Branch to the loop header
            Emitter.Emit(OpCodes.Br, iterationHeader);

            // Return
            Emitter.MarkLabel(exitMarker);
            Emitter.Emit(OpCodes.Ret);
        }

        /// <inheritdoc />
        public override void GenerateCode(ReturnTerminator returnTerminator)
        {
            returnTerminator.Assert(returnTerminator.IsVoidReturn);

            // Jump to the exit block if all lanes are active
            Emitter.Emit(
                LocalOperation.Load,
                GetBlockMask(returnTerminator.BasicBlock));
            Specializer.GetNumberOfActiveLanes(Emitter);
            Emitter.Emit(LocalOperation.Load, targetMaskCount);
            Emitter.Emit(OpCodes.Beq, localExitMarker);
        }
    }
}
