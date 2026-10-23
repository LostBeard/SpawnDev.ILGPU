// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXFloat16Emulation.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime.Cuda;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ILGPU.Backends.PTX
{
    /// <summary>
    /// Emulates the f16 arithmetic and compare instructions on devices below sm_53, which
    /// only offer f16 loads, stores, moves and conversions to/from f32 and f64.
    /// </summary>
    /// <remarks>
    /// Half values stay in 16-bit registers exactly as on native devices. Each f16
    /// instruction in the generated PTX is rewritten into a scoped block that widens its
    /// operands with cvt.f32.f16 (exact), computes in f32 or f64 and rounds back to f16
    /// once. The rewrite covers the whole module, so .func helpers are included.
    /// <list type="bullet">
    /// <item>add, sub, mul, min, max, compares: f32 op on widened operands, one rounding;
    /// identical to the native instruction.</item>
    /// <item>fma: the product of two f16 values is exact in f64; the sum with the addend is
    /// rounded to odd in f64 (add.rm / add.rp pick the odd neighbour when inexact) and then
    /// rounded to nearest-even f16 once, which equals the single rounding of the native fma.
    /// </item>
    /// <item>.ftz forms: f16 subnormal inputs and the result are flushed to a signed zero
    /// like the native instruction does.</item>
    /// <item>neg, abs: sign-bit operations. ex2.approx: ex2.approx.f32 on the widened
    /// operand (an approximation by contract, at least as accurate as the f16 one).</item>
    /// <item>cvt between f16 and integers goes through f32 (exact for the f16 range).</item>
    /// </list>
    /// Any other f16 instruction would make ptxas fail with "requires .target sm_53";
    /// instead the rewrite throws a <see cref="CapabilityNotSupportedException"/> naming the
    /// instruction and the architecture.
    /// </remarks>
    internal static class PTXFloat16Emulation
    {
        private static readonly Regex InstructionRegex = new Regex(
            @"^(?<indent>[ \t]*)(?<pred>@!?%\w+[ \t]+)?(?<op>[a-z][a-z0-9_.]*)[ \t]+" +
            @"(?<args>[^;\r\n]*);",
            RegexOptions.CultureInvariant);

        private static readonly Regex BinaryRegex = new Regex(
            @"^(?<base>add|sub|mul|min|max)(?<ftz>\.ftz)?\.f16$",
            RegexOptions.CultureInvariant);

        private static readonly Regex CompareRegex = new Regex(
            @"^setp\.(?<cmp>[a-z]+)\.f16$",
            RegexOptions.CultureInvariant);

        private static readonly Regex IntToHalfRegex = new Regex(
            @"^cvt\.rn\.f16\.(?<int>[su](8|16|32|64))$",
            RegexOptions.CultureInvariant);

        private static readonly Regex HalfToIntRegex = new Regex(
            @"^cvt\.rzi\.(?<int>[su](8|16|32|64))\.f16$",
            RegexOptions.CultureInvariant);

        /// <summary>Instructions that mention f16 and work on every architecture.</summary>
        private static readonly Regex NativeRegex = new Regex(
            @"^cvt\.(f32|f64)\.f16$|^cvt\.rn\.f16\.f32$",
            RegexOptions.CultureInvariant);

        /// <summary>Any instruction type suffix naming f16 (also f16x2).</summary>
        private static readonly Regex AnyHalfRegex = new Regex(
            @"\.f16(x2)?(\.|$)",
            RegexOptions.CultureInvariant);

        /// <summary>
        /// Rewrites all f16 arithmetic and compare instructions in the given PTX.
        /// </summary>
        /// <param name="ptx">The PTX assembly.</param>
        /// <param name="architecture">The target architecture (for error messages).</param>
        /// <returns>The PTX assembly without native f16 arithmetic.</returns>
        public static string Rewrite(string ptx, CudaArchitecture architecture)
        {
            if (!ptx.Contains(".f16", StringComparison.Ordinal))
                return ptx;

            var result = new StringBuilder(ptx.Length + ptx.Length / 8);
            int position = 0;
            while (position < ptx.Length)
            {
                int end = ptx.IndexOf('\n', position);
                int next = end < 0 ? ptx.Length : end + 1;
                var line = ptx.Substring(position, next - position);
                position = next;

                var match = InstructionRegex.Match(line);
                string? replacement = null;
                if (match.Success)
                {
                    var op = match.Groups["op"].Value;
                    replacement = TryRewrite(
                        match.Groups["indent"].Value,
                        match.Groups["pred"].Value,
                        op,
                        match.Groups["args"].Value.Split(','));
                    if (replacement == null &&
                        AnyHalfRegex.IsMatch(op) &&
                        !NativeRegex.IsMatch(op))
                    {
                        throw new CapabilityNotSupportedException(
                            $"The Float16 instruction '{op}' is not available on {architecture} " +
                            "(native f16 arithmetic needs sm_53) and is not emulated.");
                    }
                }
                result.Append(replacement ?? line);
            }
            return result.ToString();
        }

        /// <summary>
        /// Emits the statements of one rewritten instruction.
        /// </summary>
        private sealed class Block
        {
            private static readonly (string Prefix, string Type)[] RegisterKinds =
            {
                ("ef", ".f32"), ("ed", ".f64"), ("eh", ".b16"), ("er", ".b32"),
                ("eq", ".b64"), ("ep", ".pred"),
            };

            private readonly StringBuilder statements = new StringBuilder();
            private readonly Dictionary<string, int> counts = new Dictionary<string, int>();
            private readonly string indent;
            private readonly string predicate;

            public Block(string indent, string predicate)
            {
                this.indent = indent;
                this.predicate = predicate;
            }

            public void Emit(string statement)
            {
                statements.Append(indent).Append('\t').Append(predicate)
                    .Append(statement).Append('\n');
            }

            private string New(string prefix)
            {
                counts.TryGetValue(prefix, out int count);
                counts[prefix] = count + 1;
                return "%" + prefix + count.ToString(CultureInfo.InvariantCulture);
            }

            public string NewFloat() => New("ef");
            public string NewDouble() => New("ed");
            public string NewHalf() => New("eh");
            public string NewBits() => New("eq");
            public string NewBits32() => New("er");
            public string NewPredicate() => New("ep");

            /// <summary>
            /// Moves an immediate into a b16 register; registers are returned unchanged.
            /// </summary>
            public string AsRegister(string operand)
            {
                operand = operand.Trim();
                if (operand.StartsWith('%'))
                    return operand;
                string half = NewHalf();
                Emit($"mov.b16 {half}, {operand};");
                return half;
            }

            /// <summary>
            /// Flushes an f16 subnormal in place to a zero of the same sign, like .ftz.
            /// </summary>
            public void FlushInPlace(string half)
            {
                string exponent = NewHalf();
                string sign = NewHalf();
                string isZero = NewPredicate();
                Emit($"and.b16 {exponent}, {half}, 31744;");
                Emit($"setp.eq.b16 {isZero}, {exponent}, 0;");
                Emit($"and.b16 {sign}, {half}, 32768;");
                Emit($"selp.b16 {half}, {sign}, {half}, {isZero};");
            }

            /// <summary>
            /// Widens an f16 operand into a fresh f32 register and returns its name.
            /// </summary>
            public string Widen(string operand, bool flush = false)
            {
                string half = AsRegister(operand);
                if (flush)
                {
                    string copy = NewHalf();
                    Emit($"mov.b16 {copy}, {half};");
                    FlushInPlace(copy);
                    half = copy;
                }
                string widened = NewFloat();
                Emit($"cvt.f32.f16 {widened}, {half};");
                return widened;
            }

            /// <summary>
            /// Rounds an f64 register to f16 exactly once. cvt.rn.f16.f64 cannot be used: sm_50
            /// hardware goes through f32 and double-rounds near ties. Instead the value is
            /// rounded to odd at f32 precision (truncate, then set the low bit if inexact),
            /// which keeps all the sticky information, and cvt.rn.f16.f32 rounds that once.
            /// </summary>
            public void RoundDoubleToHalf(string value, string destination)
            {
                string truncated = NewFloat();
                string back = NewDouble();
                string inexact = NewPredicate();
                string bits = NewBits32();
                string oddBits = NewBits32();
                string odd = NewFloat();
                string rounded = NewFloat();
                Emit($"cvt.rz.f32.f64 {truncated}, {value};");
                Emit($"cvt.f64.f32 {back}, {truncated};");
                Emit($"setp.neu.f64 {inexact}, {back}, {value};");
                Emit($"mov.b32 {bits}, {truncated};");
                Emit($"or.b32 {oddBits}, {bits}, 1;");
                Emit($"mov.b32 {odd}, {oddBits};");
                Emit($"selp.f32 {rounded}, {odd}, {truncated}, {inexact};");
                Emit($"cvt.rn.f16.f32 {destination}, {rounded};");
            }

            /// <summary>
            /// Widens an f16 operand into a fresh f64 register and returns its name.
            /// </summary>
            public string WidenToDouble(string operand)
            {
                string widened = NewDouble();
                Emit($"cvt.f64.f16 {widened}, {AsRegister(operand)};");
                return widened;
            }

            public override string ToString()
            {
                var header = new StringBuilder();
                header.Append(indent).Append("{\n");
                foreach (var (prefix, type) in RegisterKinds)
                {
                    if (!counts.TryGetValue(prefix, out int count) || count == 0)
                        continue;
                    header.Append(indent).Append("\t.reg ").Append(type).Append(' ');
                    for (int i = 0; i < count; ++i)
                    {
                        if (i > 0)
                            header.Append(", ");
                        header.Append('%').Append(prefix)
                            .Append(i.ToString(CultureInfo.InvariantCulture));
                    }
                    header.Append(";\n");
                }
                return header.Append(statements).Append(indent).Append("}\n").ToString();
            }
        }

        private static string? TryRewrite(
            string indent,
            string predicate,
            string op,
            string[] args)
        {
            for (int i = 0; i < args.Length; ++i)
                args[i] = args[i].Trim();
            var block = new Block(indent, predicate);

            var binary = BinaryRegex.Match(op);
            if (binary.Success && args.Length == 3)
            {
                string kind = binary.Groups["base"].Value;
                bool ftz = binary.Groups["ftz"].Success;
                // Explicit .rn keeps ptxas from contracting the f32 op with its neighbours.
                string op32 = kind is "min" or "max" ? kind + ".f32" : kind + ".rn.f32";
                string left = block.Widen(args[1], ftz);
                string right = block.Widen(args[2], ftz);
                string value = block.NewFloat();
                block.Emit($"{op32} {value}, {left}, {right};");
                block.Emit($"cvt.rn.f16.f32 {args[0]}, {value};");
                if (ftz)
                    block.FlushInPlace(args[0]);
                return block.ToString();
            }

            if (op == "fma.rn.f16" && args.Length == 4)
            {
                // a * b is exact in f64 (22 bits). Round a*b+c to odd in f64, then to f16.
                string a = block.WidenToDouble(args[1]);
                string b = block.WidenToDouble(args[2]);
                string c = block.WidenToDouble(args[3]);
                string product = block.NewDouble();
                string down = block.NewDouble();
                string up = block.NewDouble();
                string nearest = block.NewDouble();
                string odd = block.NewDouble();
                string rounded = block.NewDouble();
                string bits = block.NewBits();
                string lowBit = block.NewBits();
                string inexact = block.NewPredicate();
                string downIsOdd = block.NewPredicate();
                block.Emit($"mul.rn.f64 {product}, {a}, {b};");
                block.Emit($"add.rm.f64 {down}, {product}, {c};");
                block.Emit($"add.rp.f64 {up}, {product}, {c};");
                block.Emit($"add.rn.f64 {nearest}, {product}, {c};");
                block.Emit($"setp.neu.f64 {inexact}, {down}, {up};");
                block.Emit($"mov.b64 {bits}, {down};");
                block.Emit($"and.b64 {lowBit}, {bits}, 1;");
                block.Emit($"setp.ne.b64 {downIsOdd}, {lowBit}, 0;");
                block.Emit($"selp.f64 {odd}, {down}, {up}, {downIsOdd};");
                block.Emit($"selp.f64 {rounded}, {odd}, {nearest}, {inexact};");
                block.RoundDoubleToHalf(rounded, args[0]);
                return block.ToString();
            }

            if (op == "cvt.rn.f16.f64" && args.Length == 2)
            {
                block.RoundDoubleToHalf(args[1], args[0]);
                return block.ToString();
            }

            if (op == "ex2.approx.f16" && args.Length == 2)
            {
                string source = block.Widen(args[1]);
                string value = block.NewFloat();
                block.Emit($"ex2.approx.f32 {value}, {source};");
                block.Emit($"cvt.rn.f16.f32 {args[0]}, {value};");
                return block.ToString();
            }

            // Sign manipulation needs no arithmetic at all.
            if (op == "neg.f16" && args.Length == 2)
                return $"{indent}{predicate}xor.b16\t{args[0]}, {args[1]}, 32768;\n";
            if (op == "abs.f16" && args.Length == 2)
                return $"{indent}{predicate}and.b16\t{args[0]}, {args[1]}, 32767;\n";

            var compare = CompareRegex.Match(op);
            if (compare.Success && args.Length == 3)
            {
                string left = block.Widen(args[1]);
                string right = block.Widen(args[2]);
                block.Emit(
                    $"setp.{compare.Groups["cmp"].Value}.f32 {args[0]}, {left}, {right};");
                return block.ToString();
            }

            var intToHalf = IntToHalfRegex.Match(op);
            if (intToHalf.Success && args.Length == 2)
            {
                string value = block.NewFloat();
                block.Emit(
                    $"cvt.rn.f32.{intToHalf.Groups["int"].Value} {value}, {args[1]};");
                block.Emit($"cvt.rn.f16.f32 {args[0]}, {value};");
                return block.ToString();
            }

            var halfToInt = HalfToIntRegex.Match(op);
            if (halfToInt.Success && args.Length == 2)
            {
                string value = block.Widen(args[1]);
                block.Emit(
                    $"cvt.rzi.{halfToInt.Groups["int"].Value}.f32 {args[0]}, {value};");
                return block.ToString();
            }

            return null;
        }
    }
}
