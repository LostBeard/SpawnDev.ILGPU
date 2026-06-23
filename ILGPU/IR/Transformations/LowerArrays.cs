// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: LowerArrays.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Construction;
using ILGPU.IR.Rewriting;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using ILGPU.Resources;
using ILGPU.Util;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU.IR.Transformations
{
    /// <summary>
    /// Converts array values into structure values and allocations.
    /// </summary>
    public sealed class LowerArrays : LowerTypes<ArrayType>
    {
        #region Constants

        /// <summary>
        /// Arrays with this many elements or fewer use unrolled stores for
        /// zero-initialization. Larger arrays emit a loop in the IR instead,
        /// avoiding O(N) IR nodes that cause extreme compilation times
        /// (upstream #1479).
        /// </summary>
        private const int MaxUnrolledArrayInitSize = 32;

        #endregion

        #region Nested Types

        private sealed class ArrayTypeLowering : TypeLowering<ArrayType>
        {
            /// <summary>
            /// Constructs a new array type lowering.
            /// </summary>
            /// <param name="builder">The parent builder.</param>
            /// <param name="targetAddressSpace">The target address spacce.</param>
            public ArrayTypeLowering(
                Method.Builder builder,
                MemoryAddressSpace targetAddressSpace)
                : base(builder)
            {
                TargetAddressSpace = targetAddressSpace;
            }

            /// <summary>
            /// Returns the target address space for all array values.
            /// </summary>
            public MemoryAddressSpace TargetAddressSpace { get; }

            /// <summary>
            /// Returns true if the given type depends on an array type.
            /// </summary>
            public override bool IsTypeDependent(TypeNode type) =>
                type.HasFlags(TypeFlags.ArrayDependent);

            /// <summary>
            /// Converts the array type into a structure of dimensions + 1 elements.
            /// </summary>
            protected override TypeNode ConvertType<TTypeContext>(
                TTypeContext typeContext,
                ArrayType type)
            {
                // Create the structure type builder and initialize the base view.
                var builder = typeContext.CreateStructureType(GetNumFields(type));
                builder.Add(typeContext.CreateViewType(
                    type.ElementType,
                    TargetAddressSpace));

                // Initialize all 32bit dimensions.
                var lengthType = typeContext.GetPrimitiveType(BasicValueType.Int32);
                for (int i = 0, e = type.NumDimensions; i < e; ++i)
                    builder.Add(lengthType);
                return builder.Seal();
            }

            /// <summary>
            /// Returns the number of dimensions of the given array type + 1.
            /// </summary>
            protected override int GetNumFields(ArrayType type) =>
                type.NumDimensions + 1;
        }

        #endregion

        #region Rewriter Helper Methods

        /// <summary>
        /// Gets the view from the given array value.
        /// </summary>
        /// <returns>A reference to the array view.</returns>
        private static Value GetViewFromArray(
            IRBuilder builder,
            Location location,
            Value array) =>
            builder.CreateGetField(
                location,
                array,
                new FieldSpan(0, 1));

        #endregion

        #region Rewriter Methods

        /// <summary>
        /// Lowers new array values into static allocation instances and structures.
        /// </summary>
        private static void Lower(
            RewriterContext context,
            TypeLowering<ArrayType> typeLowering,
            NewArray value)
        {
            var builder = context.Builder;
            var location = value.Location;
            var targetAddressSpace = (typeLowering as ArrayTypeLowering)
                .AsNotNull()
                .TargetAddressSpace;

            // Compute array length
            Value totalLength = builder.CreatePrimitiveValue(location, 1);
            foreach (Value length in value)
            {
                totalLength = builder.CreateArithmetic(
                    location,
                    totalLength,
                    length,
                    BinaryArithmeticKind.Mul);
            }

            // Check for a compile-time known constant
            if (!(totalLength is PrimitiveValue primitiveLength) ||
                primitiveLength.Int32Value < 0)
            {
                throw location.GetNotSupportedException(
                    ErrorMessages.NotSupportNonConstArrayDimension,
                    totalLength.ToString());
            }

            // Create array structure.
            var arrayType = typeLowering
                .ConvertType(value.Type)
                .As<StructureType>(location);
            var result = builder.CreateStructure(location, arrayType);
            Value view;

            // Check for empty array allocations
            if (primitiveLength.HasIntValue(0))
            {
                // Create an empty view.
                view = builder.CreateNewView(
                    location,
                    builder.CreateNull(
                        location,
                        builder.CreatePointerType(
                            value.ElementType,
                            targetAddressSpace)),
                    primitiveLength);
                result.Add(view);

                // Insert a dimension length of 0 in each dimension
                for (int i = 0, e = value.Count; i < e; ++i)
                    result.Add(primitiveLength);
            }
            else
            {
                // Create the allocation and the base view
                var allocation = builder.CreateStaticAllocaArray(
                    location,
                    totalLength,
                    value.ElementType,
                    targetAddressSpace);
                view = builder.CreateNewView(location, allocation, totalLength);
                result.Add(view);

                // Insert dimension information for each array rank
                foreach (Value length in value)
                    result.Add(length);
            }

            // Clear all array elements to their default value. For small arrays
            // we unroll the stores directly; for large arrays we emit a proper
            // loop to avoid generating O(N) IR nodes (fixes upstream #1479).
            var defaultElement = builder.CreateNull(location, value.Type.ElementType);
            int arraySize = primitiveLength.Int32Value;

            if (arraySize <= MaxUnrolledArrayInitSize)
            {
                // Small array: unroll the zero-initialization stores
                for (int i = 0; i < arraySize; ++i)
                {
                    var address = builder.CreateLoadElementAddress(
                        location,
                        view,
                        builder.CreatePrimitiveValue(location, i));
                    builder.CreateStore(location, address, defaultElement);
                }
            }
            else
            {
                // Large array: emit a loop in the IR to avoid O(N) nodes.
                // Structure: currentBlock → loopHeader → loopBody → loopHeader
                //                                      ↘ exitBlock
                var methodBuilder = builder.MethodBuilder;
                var int32Type = builder.CreateType(typeof(int));

                // Split the current block at the NewArray value. This moves
                // all values that come after it (including the terminator) to
                // a new "exit" block, leaving the current block open for us
                // to redirect into the loop.
                var exitBlock = builder.SplitBlock(value);

                // Create the loop header and body blocks
                var loopHeader = methodBuilder.CreateBasicBlock(location);
                var loopBody = methodBuilder.CreateBasicBlock(location);

                // --- Current block: branch to loop header ---
                // SplitBlock already set a branch to exitBlock; replace it
                // with a branch to our loop header instead.
                builder.Terminator = null;
                // The loop-counter phi's initial value (0) is the argument for the
                // current-block -> loopHeader edge, so it MUST be defined in this
                // predecessor block. Creating it in loopHeader (the edge's successor)
                // breaks SSA dominance: the PTX phi-copy is emitted at the end of this
                // predecessor and would find no register for a constant defined in the
                // header it has not entered yet ("No register allocated for
                // PrimitiveValue 0"). Only surfaces when the array stays in memory
                // (dynamic indexing) - static indexing scalar-replaces the array and
                // this init loop never reaches the register allocator.
                var zeroVal = builder.CreatePrimitiveValue(location, 0);
                builder.CreateBranch(location, loopHeader);

                // --- Loop header: phi(i), compare, conditional branch ---
                var phiBuilder = loopHeader.CreatePhi(
                    location,
                    int32Type,
                    2);
                // Add the initial value (0 from the current block)
                phiBuilder.AddArgument(builder.BasicBlock, zeroVal);
                // Note: the back-edge argument from loopBody is added below

                var lengthVal = loopHeader.CreatePrimitiveValue(
                    location, arraySize);
                // We need the phi value for the comparison, seal it after
                // adding the back-edge argument. For now, get a reference:
                // We'll add the loopBody argument and seal after creating it.

                // --- Loop body: store, increment, branch back ---
                // We need to create the increment value in the body first so
                // we can add it to the phi before sealing.
                var phiRef = phiBuilder.PhiValue;
                var bodyAddress = loopBody.CreateLoadElementAddress(
                    location,
                    view,
                    phiRef);
                loopBody.CreateStore(location, bodyAddress, defaultElement);

                var oneVal = loopBody.CreatePrimitiveValue(location, 1);
                var nextI = loopBody.CreateArithmetic(
                    location,
                    phiRef,
                    oneVal,
                    BinaryArithmeticKind.Add);

                // Add the back-edge argument to the phi and seal it
                phiBuilder.AddArgument(loopBody, nextI);
                var loopCounter = phiBuilder.Seal();

                // Now create the comparison in the header
                var cmp = loopHeader.CreateCompare(
                    location,
                    loopCounter,
                    lengthVal,
                    CompareKind.LessThan);
                loopHeader.CreateIfBranch(
                    location,
                    cmp,
                    loopBody,      // true: continue loop
                    exitBlock);    // false: done

                // Loop body branches back to header
                loopBody.CreateBranch(location, loopHeader);
            }

            // Replace the current value with a new structure instance
            context.ReplaceAndRemove(value, result.Seal());
        }

        /// <summary>
        /// Lowers leae nodes to linear <see cref="LoadElementAddress"/> values.
        /// </summary>
        private static void Lower(
            RewriterContext context,
            TypeLowering<ArrayType> typeLowering,
            LoadArrayElementAddress value)
        {
            var builder = context.Builder;
            var location = value.Location;
            Value array = value.ArrayValue;

            // Get and validate the array type
            var structureType = array.Type.As<StructureType>(location);

            // Compute linear address based on the .Net array layouts
            Value elementIndex = builder.CreatePrimitiveValue(location, 0L);

            // (((0 * Width + x) * Height) + y) * Depth + z...
            for (int i = 0, e = structureType.NumFields - 1; i < e; ++i)
            {
                Value length = builder.CreateGetField(
                    location,
                    array,
                    new FieldAccess(1 + i));

                // Create a debug assertion to check for out-of-bounds accesses
                builder.CreateDebugAssert(
                    location,
                    builder.CreateCompare(
                        location,
                        value.Dimensions[i],
                        length,
                        CompareKind.LessThan),
                    builder.CreatePrimitiveValue(
                        location,
                        $"{i}-th array index out of range"));

                // Update index computation
                elementIndex = builder.CreateArithmetic(
                    location,
                    elementIndex,
                    length,
                    BinaryArithmeticKind.Mul);
                elementIndex = builder.CreateArithmetic(
                    location,
                    elementIndex,
                    value.Dimensions[i],
                    BinaryArithmeticKind.Add);
            }

            // Extract the actual view field from the structure and compute the
            // appropriate target element address.
            var view = GetViewFromArray(builder, location, array);
            var lea = builder.CreateLoadElementAddress(
                location,
                view,
                elementIndex);
            context.ReplaceAndRemove(value, lea);
        }


        /// <summary>
        /// Lower array length values to <see cref="GetViewLength"/> values.
        /// </summary>
        [SuppressMessage(
            "Maintainability",
            "CA1508:Avoid dead conditional code",
            Justification = "The dimension check is not dead")]
        private static void Lower(
            RewriterContext context,
            TypeLowering<ArrayType> typeLowering,
            GetArrayLength value)
        {
            var builder = context.Builder;
            var location = value.Location;

            Value length;
            if (value.IsFullLength)
            {
                // Extract the view from the underlying array-implementation structure
                var view = GetViewFromArray(builder, location, value.ArrayValue);
                length = builder.CreateGetViewLength(location, view);
            }
            else
            {
                // Check whether the dimension is a compile-time known constant
                Value dimension = value.Dimension;
                if (!(dimension is PrimitiveValue primitiveValue))
                {
                    throw location.GetNotSupportedException(
                        ErrorMessages.NotSupportNonConstArrayDimension,
                        dimension.ToString());
                }

                // Check whether the field index is out of range
                var structType = value.ArrayValue.Type.As<StructureType>(location);
                int index = primitiveValue.Int32Value;
                if (index < 0 || index >= structType.NumFields - 1)
                {
                    throw location.GetNotSupportedException(
                        ErrorMessages.NotSupportNonConstArrayDimension,
                        index.ToString());
                }

                // BaseView + dimension offset
                length = builder.CreateGetField(
                    location,
                    value.ArrayValue,
                    new FieldAccess(1 + index));
            }

            // Replace the actual length information
            context.ReplaceAndRemove(value, length);
        }

        /// <summary>
        /// Lower array to view casts to direct references to the underyling view.
        /// </summary>
        private static void Lower(
            RewriterContext context,
            TypeLowering<ArrayType> typeLowering,
            ArrayToViewCast value)
        {
            var builder = context.Builder;
            var location = value.Location;

            // Get the view from the array implementation structure
            Value array = value.Value;
            var view = GetViewFromArray(builder, location, array);

            // Cast this view into the generic address space to match all invariants
            view = builder.CreateAddressSpaceCast(
                location,
                view,
                MemoryAddressSpace.Generic);

            // Replace the current cast with the actual view pointing to the elements
            context.ReplaceAndRemove(value, view);
        }

        #endregion

        #region Rewriter

        /// <summary>
        /// The internal rewriter.
        /// </summary>
        private static readonly Rewriter<TypeLowering<ArrayType>> Rewriter =
            new Rewriter<TypeLowering<ArrayType>>();

        /// <summary>
        /// Initializes all rewriter patterns.
        /// </summary>
        static LowerArrays()
        {
            AddRewriters(Rewriter);

            Rewriter.Add<NewArray>(Register, Lower);
            Rewriter.Add<LoadArrayElementAddress>(Register, Lower);
            Rewriter.Add<GetArrayLength>(Register, Lower);
            Rewriter.Add<ArrayToViewCast>(Register, Lower);
        }

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new array type lowering transformation.
        /// </summary>
        /// <param name="targetAddressSpace">
        /// The target address space for all array values.
        /// </param>
        public LowerArrays(MemoryAddressSpace targetAddressSpace)
        {
            TargetAddressSpace = targetAddressSpace;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the target address space for all array values.
        /// </summary>
        public MemoryAddressSpace TargetAddressSpace { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new <see cref="ArrayTypeLowering"/> type converter to convert
        /// internal array types to low-level structure values.
        /// </summary>
        protected override TypeLowering<ArrayType> CreateLoweringConverter(
            Method.Builder builder) =>
            new ArrayTypeLowering(builder, TargetAddressSpace);

        /// <summary>
        /// Applies the array type lowering transformation.
        /// </summary>
        protected override bool PerformTransformation(Method.Builder builder) =>
            PerformTransformation(builder, Rewriter);

        #endregion
    }
}
