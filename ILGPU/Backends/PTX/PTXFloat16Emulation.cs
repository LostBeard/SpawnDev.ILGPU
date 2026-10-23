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

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ILGPU.Backends.PTX
{
    /// <summary>
    /// Emulates the f16 arithmetic and compare instructions on devices below sm_53, which
    /// only offer f16 loads, stores, moves and conversions to/from f32.
    /// </summary>
    /// <remarks>
    /// Half values stay in 16-bit registers exactly as on native devices. Each f16
    /// arithmetic/compare instruction in the generated PTX is rewritten into a scoped block
    /// that widens its operands with cvt.f32.f16 (exact), runs the f32 instruction and, for
    /// value results, rounds back with cvt.rn.f16.f32. add, sub, mul, min, max and the
    /// compares therefore give the same bits as the native instruction (an f32 operation on
    /// two f16 values followed by one rounding is correctly rounded). fma goes through an f32
    /// fma and may differ from the native single rounding in rare double-rounding cases, and
    /// the .ftz variants do not flush f16 subnormals.
    /// </remarks>
    internal static class PTXFloat16Emulation
    {
        private static readonly Regex InstructionRegex = new Regex(
            @"^(?<indent>[ \t]*)(?<pred>@!?%\w+[ \t]+)?(?<op>[a-z][a-z0-9_.]*)[ \t]+" +
            @"(?<args>[^;\r\n]*);",
            RegexOptions.CultureInvariant);

        private static readonly Regex BinaryRegex = new Regex(
            @"^(?<base>add|sub|mul|min|max)(\.ftz)?\.f16$",
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

        /// <summary>
        /// Rewrites all f16 arithmetic and compare instructions in the given PTX.
        /// </summary>
        /// <param name="ptx">The PTX assembly.</param>
        /// <returns>The PTX assembly without native f16 arithmetic.</returns>
        public static string Rewrite(string ptx)
        {
            if (!ptx.Contains(".f16", System.StringComparison.Ordinal))
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
                string? replacement = match.Success
                    ? TryRewrite(
                        match.Groups["indent"].Value,
                        match.Groups["pred"].Value,
                        match.Groups["op"].Value,
                        match.Groups["args"].Value.Split(','))
                    : null;
                result.Append(replacement ?? line);
            }
            return result.ToString();
        }

        /// <summary>
        /// Emits the statements of one rewritten instruction.
        /// </summary>
        private sealed class Block
        {
            private readonly StringBuilder statements = new StringBuilder();
            private readonly string indent;
            private readonly string predicate;
            private int numFloats;
            private int numHalves;

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

            /// <summary>
            /// Widens an f16 operand into a fresh f32 register and returns its name.
            /// </summary>
            public string Widen(string operand)
            {
                operand = operand.Trim();
                string half = operand;
                if (!operand.StartsWith('%'))
                {
                    // An immediate cannot be converted directly: move it into a register.
                    half = "%eh" + numHalves++.ToString(CultureInfo.InvariantCulture);
                    Emit($"mov.b16 {half}, {operand};");
                }
                string widened = NewFloat();
                Emit($"cvt.f32.f16 {widened}, {half};");
                return widened;
            }

            public string NewFloat() =>
                "%ef" + numFloats++.ToString(CultureInfo.InvariantCulture);

            public override string ToString()
            {
                var header = new StringBuilder();
                header.Append(indent).Append("{\n");
                if (numFloats > 0)
                {
                    header.Append(indent).Append("\t.reg .f32 ");
                    for (int i = 0; i < numFloats; ++i)
                    {
                        if (i > 0)
                            header.Append(", ");
                        header.Append("%ef").Append(i.ToString(CultureInfo.InvariantCulture));
                    }
                    header.Append(";\n");
                }
                if (numHalves > 0)
                {
                    header.Append(indent).Append("\t.reg .b16 ");
                    for (int i = 0; i < numHalves; ++i)
                    {
                        if (i > 0)
                            header.Append(", ");
                        header.Append("%eh").Append(i.ToString(CultureInfo.InvariantCulture));
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
                // Explicit .rn keeps ptxas from contracting the f32 op with its neighbours.
                string op32 = kind is "min" or "max" ? kind + ".f32" : kind + ".rn.f32";
                string left = block.Widen(args[1]);
                string right = block.Widen(args[2]);
                string value = block.NewFloat();
                block.Emit($"{op32} {value}, {left}, {right};");
                block.Emit($"cvt.rn.f16.f32 {args[0]}, {value};");
                return block.ToString();
            }

            if (op == "fma.rn.f16" && args.Length == 4)
            {
                string a = block.Widen(args[1]);
                string b = block.Widen(args[2]);
                string c = block.Widen(args[3]);
                string value = block.NewFloat();
                block.Emit($"fma.rn.f32 {value}, {a}, {b}, {c};");
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
