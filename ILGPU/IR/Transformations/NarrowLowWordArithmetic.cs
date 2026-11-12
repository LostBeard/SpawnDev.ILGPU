// ---------------------------------------------------------------------------------------
//                                    SpawnDev.ILGPU
//
// File: NarrowLowWordArithmetic.cs
//
// This file is part of the SpawnDev ILGPU fork and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Types;
using ILGPU.IR.Values;
using System;
using System.Collections.Generic;

namespace ILGPU.IR.Transformations
{
    /// <summary>
    /// Computes 64-bit integer arithmetic in 32 bits where only its low 32 bits are read.
    /// </summary>
    /// <remarks>
    /// For backends that EMULATE 64-bit integers (WebGPU: <c>vec2&lt;u32&gt;</c> and a library function per operation).
    /// The low 32 bits of an add, subtract, multiply, and / or / xor, negate, not and a left shift by a constant depend
    /// only on the low 32 bits of their operands, so <c>(int)(a64 * b64 + c64)</c> is exactly
    /// <c>(int)a64 * (int)b64 + (int)c64</c> in wrapping 32-bit arithmetic. The readers of low words are an explicit
    /// conversion to a 32-bit or narrower integer and, with <c>narrowViewIndices</c>, the element index of a view access
    /// and the offset of a sub-view (those backends index with 32 bits: their generators already truncate a 64-bit index).
    /// MEASURED 2026-11-12 (SpawnDev.Codecs VP9 encode kernel, 27,667 lines of WGSL): 1,456 <c>i64_add</c>, 422
    /// <c>i64_mul</c> (63 lines each once the shader compiler has inlined it) and 1,437 <c>i64_to_i32</c> calls, nearly all
    /// of them <c>view[(long)i * stride + offset]</c> index arithmetic: about 47,000 of the 75,000 lines the D3D shader
    /// compiler sees after inlining.
    /// A value is narrowed only when EVERY reader of it reads the low word (through other narrowed values, phis and
    /// selects included): it gets a 32-bit twin right after it, its readers use the twin, and dead code elimination
    /// removes the 64-bit original. A value with a full-width reader (a comparison, a right shift, a division, a store)
    /// is left alone, and so is any other 64-bit value (a parameter, a load, a call result, ...): a narrowed reader takes
    /// its low word through one conversion to 32 bits.
    /// </remarks>
    public sealed class NarrowLowWordArithmetic : UnorderedTransformation
    {
        private readonly bool narrowViewIndices;
        private readonly Func<bool>? enabled;

        /// <summary>
        /// <paramref name="narrowViewIndices"/>: also narrow the element index of a view access and the offset of a
        /// sub-view. Only for a backend that indexes views with 32 bits.
        /// </summary>
        /// <param name="narrowViewIndices">True to narrow view element indices and sub-view offsets.</param>
        /// <param name="enabled">Asked at every method: false leaves the method as it is (a backend's off switch).</param>
        public NarrowLowWordArithmetic(bool narrowViewIndices, Func<bool>? enabled = null)
        {
            this.narrowViewIndices = narrowViewIndices;
            this.enabled = enabled;
        }

        private static bool IsInt64(Value value) =>
            value.Type is PrimitiveType && value.BasicValueType == BasicValueType.Int64;

        private static bool IsInt32OrNarrowerInt(BasicValueType type) =>
            type == BasicValueType.Int8 || type == BasicValueType.Int16 || type == BasicValueType.Int32;

        /// <summary>An operation whose low 32 result bits are a function of the low 32 bits of its operands.</summary>
        private static bool IsLowWordOperation(Value value) =>
            value switch
            {
                BinaryArithmeticValue binary => binary.Kind switch
                {
                    BinaryArithmeticKind.Add or BinaryArithmeticKind.Sub or BinaryArithmeticKind.Mul or
                    BinaryArithmeticKind.And or BinaryArithmeticKind.Or or BinaryArithmeticKind.Xor => true,
                    // a run-time shift count can reach 32..63, where the 64-bit low word is 0 and a 32-bit shift is not
                    BinaryArithmeticKind.Shl => binary.Right.Resolve() is PrimitiveValue,
                    _ => false,
                },
                UnaryArithmeticValue unary =>
                    unary.Kind == UnaryArithmeticKind.Neg || unary.Kind == UnaryArithmeticKind.Not,
                _ => false,
            };

        /// <summary>A 64-bit value whose 32-bit twin can be computed from the twins of its operands.</summary>
        private static bool IsCandidate(Value value) =>
            IsInt64(value) && (IsLowWordOperation(value) || value is PhiValue || value is Predicate);

        /// <summary>
        /// True when <paramref name="use"/> reads only the low 32 bits of the value it uses: a conversion to a 32-bit or
        /// narrower integer, a view index or sub-view offset (with <c>narrowViewIndices</c>), or an operand of a value
        /// that is narrowed itself.
        /// </summary>
        private bool ReadsLowWordOnly(Use use, HashSet<Value> narrowed)
        {
            var target = use.Target;
            switch (target)
            {
                case ConvertValue convert:
                    return convert.Type is PrimitiveType && IsInt32OrNarrowerInt(convert.BasicValueType);
                case LoadElementAddress:
                case SubViewValue:
                    // operand 1 is the element index / the offset (a sub-view's length is operand 2)
                    return narrowViewIndices && use.Index == 1;
                case BinaryArithmeticValue binary:
                    // a shift COUNT is not a low-word read
                    return narrowed.Contains(target) && (binary.Kind != BinaryArithmeticKind.Shl || use.Index == 0);
                case Predicate:
                    return narrowed.Contains(target) && use.Index > 0;
                default:
                    return narrowed.Contains(target);
            }
        }

        /// <summary>
        /// The 64-bit values EVERY reader of which reads the low word only (the largest such set: a loop-carried value
        /// read only by itself and by low-word readers is in it). Such a value dies once its readers use the twin. A value
        /// with a full-width reader (a comparison, a right shift, a division, a store, a call, ...) stays as it is and
        /// its low word is read from it: a twin would only repeat the work (measured on SpawnDev.Codecs, 2026-11-12:
        /// twins for those made 18 of 568 kernels larger).
        /// </summary>
        private HashSet<Value> FindNarrowed(Method.Builder builder)
        {
            // the values of the method as it is now: a use by a value that was removed earlier does not count
            var live = new HashSet<Value>();
            var narrowed = new HashSet<Value>();
            foreach (var block in builder.SourceBlocks)
            {
                foreach (var entry in block)
                {
                    Value value = entry.Value;
                    live.Add(value);
                    if (IsCandidate(value))
                        narrowed.Add(value);
                }
                if (block.Terminator != null)
                    live.Add(block.Terminator);
            }
            if (narrowed.Count == 0)
                return narrowed;

            var work = new Stack<Value>(narrowed);
            while (work.Count > 0)
            {
                var value = work.Pop();
                if (!narrowed.Contains(value))
                    continue;
                bool lowWordOnly = true;
                bool read = false;
                foreach (var use in value.Uses)
                {
                    if (!live.Contains(use.Target))
                        continue;
                    read = true;
                    if (!ReadsLowWordOnly(use, narrowed))
                    {
                        lowWordOnly = false;
                        break;
                    }
                }
                if (lowWordOnly && read)
                    continue;
                // not narrowed: its operands now have a full-width reader
                narrowed.Remove(value);
                foreach (var node in value.Nodes)
                {
                    var operand = node.Resolve();
                    if (narrowed.Contains(operand))
                        work.Push(operand);
                }
            }
            return narrowed;
        }

        /// <summary>True when <paramref name="value"/> reads the low word of a narrowed value (or of a constant index).</summary>
        private bool IsRoot(Value value, HashSet<Value> narrowed)
        {
            switch (value)
            {
                case ConvertValue convert:
                    return convert.Type is PrimitiveType && IsInt32OrNarrowerInt(convert.BasicValueType)
                        && narrowed.Contains(convert.Value.Resolve());
                case LoadElementAddress lea when narrowViewIndices:
                    return IsInt64(lea.Offset) && IsNarrowedIndex(lea.Offset.Resolve(), narrowed);
                case SubViewValue subView when narrowViewIndices:
                    return IsInt64(subView.Offset) && IsNarrowedIndex(subView.Offset.Resolve(), narrowed);
                default:
                    return false;
            }
        }

        private static bool IsNarrowedIndex(Value index, HashSet<Value> narrowed) =>
            index is PrimitiveValue || narrowed.Contains(index);

        /// <summary>Applies the transformation.</summary>
        protected override bool PerformTransformation(Method.Builder builder)
        {
            if (enabled != null && !enabled())
                return false;
            var narrowed = FindNarrowed(builder);
            var roots = new List<Value>();
            foreach (var block in builder.SourceBlocks)
                foreach (var entry in block)
                {
                    Value value = entry.Value;
                    if (IsRoot(value, narrowed))
                        roots.Add(value);
                }
            if (roots.Count == 0)
                return false;

            // Every twin first (nothing is replaced while positions are looked up), then the readers
            var narrower = new Narrower(builder, narrowed);
            foreach (var root in roots)
            {
                var operand = root switch
                {
                    ConvertValue convert => convert.Value.Resolve(),
                    LoadElementAddress lea => lea.Offset.Resolve(),
                    _ => ((SubViewValue)root).Offset.Resolve(),
                };
                if (operand is not PrimitiveValue)
                    narrower.CreateTwin(operand);
            }
            narrower.CompletePhis();

            foreach (var root in roots)
            {
                var blockBuilder = builder[root.BasicBlock];
                blockBuilder.SetupInsertPosition(root);
                Value replacement;
                switch (root)
                {
                    case ConvertValue convert:
                        var twin = narrower.Operand(convert.Value, blockBuilder);
                        replacement = convert.BasicValueType == BasicValueType.Int32
                            ? twin
                            : blockBuilder.CreateConvert(convert.Location, twin, convert.Type, convert.Flags);
                        break;
                    case LoadElementAddress lea:
                        replacement = blockBuilder.CreateLoadElementAddress(
                            lea.Location,
                            lea.Source,
                            narrower.Operand(lea.Offset, blockBuilder));
                        break;
                    default:
                        var subView = (SubViewValue)root;
                        replacement = blockBuilder.CreateSubViewValue(
                            subView.Location,
                            subView.Source,
                            narrower.Operand(subView.Offset, blockBuilder),
                            subView.Length);
                        break;
                }
                if (replacement == root)
                    continue;
                root.Replace(replacement);
                blockBuilder.Remove(root);
            }
            return true;
        }

        /// <summary>Builds the 32-bit twins. Iterative: kernels are compiled in the browser, on a small stack.</summary>
        private sealed class Narrower
        {
            private readonly Method.Builder builder;
            private readonly Dictionary<Value, Value> twins = new Dictionary<Value, Value>();
            private readonly Stack<(PhiValue.Builder Twin, PhiValue Source)> pendingPhis =
                new Stack<(PhiValue.Builder, PhiValue)>();
            private readonly Stack<Value> work = new Stack<Value>();

            private readonly HashSet<Value> narrowed;

            public Narrower(Method.Builder builder, HashSet<Value> narrowed)
            {
                this.builder = builder;
                this.narrowed = narrowed;
            }

            /// <summary>
            /// The 32-bit value of <paramref name="operand"/>: a constant is created at the insert position of
            /// <paramref name="at"/> (a constant has no place of its own to dominate every reader from), anything else
            /// is its twin (<see cref="CreateTwin"/> first).
            /// </summary>
            public Value Operand(Value operand, BasicBlock.Builder at)
            {
                operand = operand.Resolve();
                return operand is PrimitiveValue constant
                    ? at.CreatePrimitiveValue(constant.Location, constant.Int32Value)
                    : twins[operand];
            }

            /// <summary>Creates the twin of the 64-bit <paramref name="root"/> and of everything it is computed from.</summary>
            public void CreateTwin(Value root)
            {
                root = root.Resolve();
                if (twins.ContainsKey(root))
                    return;
                work.Push(root);
                while (work.Count > 0)
                {
                    var value = work.Peek();
                    if (twins.ContainsKey(value))
                    {
                        work.Pop();
                        continue;
                    }
                    if (!narrowed.Contains(value))
                    {
                        twins[value] = CreateLowWord(value);
                        work.Pop();
                        continue;
                    }
                    if (value is PhiValue phi)
                    {
                        // registered before its operands exist: a loop-carried value reaches itself
                        var twin = builder[phi.BasicBlock].CreatePhi(
                            phi.Location,
                            builder[phi.BasicBlock].GetPrimitiveType(BasicValueType.Int32));
                        twins[value] = twin.PhiValue;
                        pendingPhis.Push((twin, phi));
                        work.Pop();
                        continue;
                    }

                    bool waits = false;
                    switch (value)
                    {
                        case BinaryArithmeticValue binary:
                            waits |= Require(binary.Left);
                            if (binary.Kind != BinaryArithmeticKind.Shl)
                                waits |= Require(binary.Right);
                            break;
                        case UnaryArithmeticValue unary:
                            waits |= Require(unary.Value);
                            break;
                        case Predicate predicate:
                            waits |= Require(predicate.TrueValue);
                            waits |= Require(predicate.FalseValue);
                            break;
                    }
                    if (waits)
                        continue;
                    twins[value] = CreateOperation(value);
                    work.Pop();
                }
            }

            /// <summary>Schedules the twin of <paramref name="operand"/>; false when it exists or is a constant.</summary>
            private bool Require(Value operand)
            {
                operand = operand.Resolve();
                if (operand is PrimitiveValue || twins.ContainsKey(operand))
                    return false;
                work.Push(operand);
                return true;
            }

            /// <summary>Gives every twin phi its operands (which can create further twins and phis).</summary>
            public void CompletePhis()
            {
                while (pendingPhis.Count > 0)
                {
                    var (twin, source) = pendingPhis.Pop();
                    var sources = source.Sources;
                    for (int i = 0, e = source.Nodes.Length; i < e; ++i)
                    {
                        var operand = source.Nodes[i].Resolve();
                        Value narrowed;
                        if (operand is PrimitiveValue)
                        {
                            // at the end of the predecessor the value arrives from
                            var predecessor = builder[sources[i]];
                            predecessor.SetupInsertPositionToEnd();
                            narrowed = Operand(operand, predecessor);
                        }
                        else
                        {
                            CreateTwin(operand);
                            narrowed = twins[operand];
                        }
                        twin.AddArgument(sources[i], narrowed);
                    }
                    twin.Seal();
                }
            }

            /// <summary>The block builder positioned right after <paramref name="value"/> (after the phis for a phi).</summary>
            private BasicBlock.Builder PositionAfter(Value value)
            {
                if (value is Parameter || value.BasicBlock is null)
                {
                    var entry = builder[builder.EntryBlock];
                    entry.InsertPosition = FirstNonPhi(entry);
                    return entry;
                }
                var blockBuilder = builder[value.BasicBlock];
                if (value is PhiValue)
                {
                    blockBuilder.InsertPosition = FirstNonPhi(blockBuilder);
                }
                else
                {
                    blockBuilder.SetupInsertPosition(value);
                    blockBuilder.InsertPosition = blockBuilder.InsertPosition + 1;
                }
                return blockBuilder;
            }

            private static int FirstNonPhi(BasicBlock.Builder blockBuilder)
            {
                foreach (var entry in blockBuilder.BasicBlock)
                    if (entry.DirectTarget is not PhiValue)
                        return entry.Index;
                return blockBuilder.Count;
            }

            /// <summary>The low 32 bits of a 64-bit value that is not narrowed itself: one conversion, right after it.</summary>
            private Value CreateLowWord(Value value)
            {
                var blockBuilder = PositionAfter(value);
                if (value is UndefinedValue)
                    return blockBuilder.CreatePrimitiveValue(value.Location, 0);
                return blockBuilder.CreateConvert(
                    value.Location,
                    value,
                    blockBuilder.GetPrimitiveType(BasicValueType.Int32));
            }

            /// <summary>The 32-bit twin of an arithmetic operation or a select, right after it.</summary>
            private Value CreateOperation(Value value)
            {
                var blockBuilder = PositionAfter(value);
                var location = value.Location;
                switch (value)
                {
                    case BinaryArithmeticValue binary when binary.Kind == BinaryArithmeticKind.Shl:
                        // C# masks a 64-bit shift count with 63; from 32 on, no source bit is left in the low word
                        long count = ((PrimitiveValue)binary.Right.Resolve()).RawValue & 63;
                        if (count >= 32)
                            return blockBuilder.CreatePrimitiveValue(location, 0);
                        var shifted = Operand(binary.Left, blockBuilder);
                        return blockBuilder.CreateArithmetic(
                            location,
                            shifted,
                            blockBuilder.CreatePrimitiveValue(location, (int)count),
                            BinaryArithmeticKind.Shl,
                            binary.Flags);
                    case BinaryArithmeticValue binary:
                        var left = Operand(binary.Left, blockBuilder);
                        var right = Operand(binary.Right, blockBuilder);
                        return blockBuilder.CreateArithmetic(location, left, right, binary.Kind, binary.Flags);
                    case UnaryArithmeticValue unary:
                        return blockBuilder.CreateArithmetic(
                            location,
                            Operand(unary.Value, blockBuilder),
                            unary.Kind,
                            unary.Flags);
                    default:
                        var predicate = (Predicate)value;
                        var trueValue = Operand(predicate.TrueValue, blockBuilder);
                        var falseValue = Operand(predicate.FalseValue, blockBuilder);
                        return blockBuilder.CreatePredicate(location, predicate.Condition, trueValue, falseValue);
                }
            }
        }
    }
}
