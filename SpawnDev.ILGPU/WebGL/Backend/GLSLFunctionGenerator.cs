// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGL
//                        Copyright (c) 2024 SpawnDev Project
//
// File: GLSLFunctionGenerator.cs
//
// Generates GLSL ES 3.0 code for helper device functions (non-kernel methods).
// ---------------------------------------------------------------------------------------

using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;
using System.Collections.Generic;
using System.Text;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Generates GLSL ES 3.0 code for helper (non-kernel) functions.
    /// These are called by the kernel entry point or by other helpers.
    /// </summary>
    internal sealed class GLSLFunctionGenerator : GLSLCodeGenerator
    {
        private readonly GeneratorArgs _generatorArgs;

        // Packed-4-bit (FP4/QInt4/QUInt4) view params passed into THIS helper. Keyed by the helper
        // param Index; the value is the sampler binding name (p_{Index}) + the packed decode info.
        // GLSL ES 3.0 has no pointers, so a packed-4-bit ArrayView fn-param is passed as a sampler
        // TRIPLE (isampler2D p_N, int p_N_tileW, int p_N_offset). The kernel generator records which
        // params are packed views via GeneratorArgs.HelperPackedViewParams (it alone knows QUInt4
        // signedness); this generator emits the triple signature + the 8-nibbles/word texelFetch load.
        private readonly Dictionary<int, PackedViewParamInfo> _packedViewParams = new();
        // Maps a LEA target variable name -> the packed-view helper param Index, so the Load override
        // can emit the nibble texelFetch. Mirrors the kernel-gen's _subWordLEAVars, scoped to the helper.
        private readonly Dictionary<string, int> _packedViewLEAVars = new();

        public GLSLFunctionGenerator(in GeneratorArgs args, Method method, Allocas allocas)
            : base(args, method, allocas)
        {
            _generatorArgs = args;
            foreach (var param in method.Parameters)
            {
                if (_generatorArgs.HelperPackedViewParams.TryGetValue((method.Id, param.Index), out var info))
                    _packedViewParams[param.Index] = info;
            }
        }

        public override void GenerateHeader(StringBuilder builder) { }

        public override void GenerateCode()
        {
            // Emulation library forward declarations.
            //
            // CodeGeneratorBackend merges helpers BEFORE the kernel's content (reverse
            // merge order). The emulation library lives at the top of the kernel's
            // Builder, so in the final GLSL, helper fn defs appear BEFORE the i64/f64
            // emulation function definitions. GLSL ES 3.0 requires fns to be declared
            // before use — without forward decls, helpers calling `i64_shr` /
            // `i64_shl` / etc. fail with "no matching overloaded function found".
            //
            // Forward decls + later defs is legal GLSL (and conventional). Emitting
            // the full set up-front (cheap — they're prototypes only) is simpler than
            // tracking which specific helpers each helper actually uses.
            // Closes Tests23_I64Shift_InHelper_NoCodegenError on WebGL after the
            // local.13+ i64 shift dispatch fix routed `>>` on uvec2 through `i64_shr`.
            Builder.AppendLine("// === Emulation library forward declarations ===");
            Builder.AppendLine("uvec2 i64_from_i32(int v);");
            Builder.AppendLine("uvec2 u64_from_u32(uint v);");
            Builder.AppendLine("int i64_to_i32(uvec2 v);");
            Builder.AppendLine("uint u64_to_u32(uvec2 v);");
            Builder.AppendLine("uvec2 i64_add(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_sub(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_mul(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 u64_mul(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_neg(uvec2 a);");
            Builder.AppendLine("uvec2 i64_abs(uvec2 a);");
            Builder.AppendLine("uvec2 i64_and(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_or(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_xor(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_not(uvec2 a);");
            Builder.AppendLine("uvec2 i64_shl(uvec2 a, uint shift);");
            Builder.AppendLine("uvec2 i64_shr(uvec2 a, uint shift);");
            Builder.AppendLine("uvec2 u64_shr(uvec2 a, uint shift);");
            Builder.AppendLine("bool i64_lt(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool i64_le(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool i64_gt(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool i64_ge(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool i64_eq(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool i64_ne(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool u64_lt(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool u64_le(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool u64_gt(uvec2 a, uvec2 b);");
            Builder.AppendLine("bool u64_ge(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_min(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 i64_max(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 u64_min(uvec2 a, uvec2 b);");
            Builder.AppendLine("uvec2 u64_max(uvec2 a, uvec2 b);");
            Builder.AppendLine("vec2 f64_from_ieee754_bits(uint lo, uint hi);");
            Builder.AppendLine("uvec2 f64_to_ieee754_bits(vec2 v);");
            Builder.AppendLine("float _f16_to_f32(uint bits);");
            Builder.AppendLine("uint _f32_to_f16(float v);");
            Builder.AppendLine("float _bf16_to_f32(uint bits);");
            Builder.AppendLine("uint _f32_to_bf16(float v);");
            // FP4 (E2M1) nibble decode - a packed FP4 view loaded inside this helper calls it.
            Builder.AppendLine("float _e2m1_to_f32(uint raw);");
            Builder.AppendLine();

            GenerateHeaderStub(Builder);
            Builder.AppendLine(" {");
            IndentLevel = 1;

            GenerateCodeInternal();

            // GLSL ES 3.0 requires all code paths to return a value.
            // Add a fallback return for non-void functions in case the IR
            // ends with a throw (which we translate to a comment/noop)
            // or other unreachable terminator. For struct return types we
            // must emit a constructor with one zero-arg per field; the
            // single-arg form `struct_N(0)` is rejected by GLSL ES with
            // "Number of constructor parameters does not match the number
            // of structure fields" (rc.16 fn-def codegen Bug D, 2026-05-05).
            string returnType = TypeGenerator[Method.ReturnType];
            if (returnType != "void")
            {
                string init;
                if (Method.ReturnType is global::ILGPU.IR.Types.StructureType structType
                    && returnType.StartsWith("struct_"))
                {
                    init = GetStructDefaultInitializer(structType);
                }
                else
                {
                    init = GetDefaultValue(returnType);
                }
                Builder.Append("    ");
                Builder.AppendLine($"return {init};");
            }

            IndentLevel = 0;
            Builder.AppendLine("}");
        }

        private void GenerateHeaderStub(StringBuilder builder)
        {
            string returnType = TypeGenerator[Method.ReturnType];

            builder.Append(returnType);
            builder.Append(" ");
            builder.Append(GetMethodName(Method));
            builder.Append("(");

            bool first = true;
            foreach (var param in Method.Parameters)
            {
                if (!first) builder.Append(", ");
                first = false;

                // Packed-4-bit (FP4/QInt4/QUInt4) view param: GLSL ES 3.0 has no pointer types, so
                // pass it as a sampler TRIPLE - the R32I integer texel buffer (isampler2D) plus its
                // tile width and SubView element offset. The matching call site
                // (GLSLKernelFunctionGenerator.TryEmitPackedViewHelperCall) expands each packed-view
                // arg into `u_param{N}, u_param{N}_tileW, u_param{N}_offset`. The load override below
                // does the 8-nibbles/word texelFetch + decode against `p_{Index}`/_tileW/_offset.
                if (_packedViewParams.ContainsKey(param.Index))
                {
                    builder.Append($"highp isampler2D p_{param.Index}, highp int p_{param.Index}_tileW, highp int p_{param.Index}_offset");
                    // Bind the view param to a sentinel variable; LEA uses the param Index, not this.
                    Bind(param, new Variable($"p_{param.Index}", "int"));
                    continue;
                }

                // GLSL has no pointer types: ILGPU IR `Pointer<T>` and
                // `AddressSpaceType` (the lowered shape of `ref T` / `out T`
                // params) both map to the element type via TypeGenerator.
                // For pass-by-value this means the helper would receive a
                // copy and writes wouldn't propagate back. Mark these params
                // with `inout` so the GLSL compiler treats them as
                // bidirectional reference semantics, matching C# `ref`/`out`.
                bool isRefParam = param.ParameterType is global::ILGPU.IR.Types.AddressSpaceType
                               || param.ParameterType is global::ILGPU.IR.Types.PointerType;

                var paramType = TypeGenerator[param.ParameterType];
                if (isRefParam) builder.Append("inout ");
                builder.Append(paramType);
                builder.Append(" ");
                builder.Append($"p_{param.Index}");

                // Bind the parameter's value to a variable
                var variable = new Variable($"p_{param.Index}", paramType);
                Bind(param, variable);
            }

            builder.Append(")");
        }

        /// <summary>
        /// LEA on a packed-4-bit view helper param: record the (target var -> helper param Index)
        /// mapping so the Load override can emit the nibble texelFetch. Falls back to the base LEA
        /// for all other sources.
        /// </summary>
        public override void GenerateCode(LoadElementAddress value)
        {
            if (ResolveToParam(value.Source) is global::ILGPU.IR.Values.Parameter p
                && _packedViewParams.ContainsKey(p.Index))
            {
                var target = Load(value);
                var offset = Load(value.Offset);
                // The LEA "pointer" is just the integer element index; the Load override turns it into
                // a texelFetch. Declare it so downstream refs resolve, but its value is the index.
                Declare(target);
                AppendLine($"int {target.Name}_idx = int({offset}); // packed-4-bit LEA into p_{p.Index}");
                _packedViewLEAVars[target.Name] = p.Index;
                return;
            }
            base.GenerateCode(value);
        }

        /// <summary>
        /// Load through a packed-4-bit view LEA: 8 nibbles per R32I texel, mirroring
        /// GLSLKernelFunctionGenerator's in-kernel packed load. FP4 decodes via _e2m1_to_f32;
        /// QInt4 sign-extends (-8..7); QUInt4 zero-extends (0..15).
        /// </summary>
        public override void GenerateCode(global::ILGPU.IR.Values.Load loadVal)
        {
            var sourceVar = Load(loadVal.Source);
            if (_packedViewLEAVars.TryGetValue(sourceVar.Name, out int paramIdx)
                && _packedViewParams.TryGetValue(paramIdx, out var info))
            {
                var target = Load(loadVal);
                Declare(target);
                string idx = $"{sourceVar.Name}_idx";
                string bn = $"p_{paramIdx}";
                // texel = idx>>3, shift = (idx&7)*4, mask 0xF (8 nibbles / 32-bit texel).
                string texelIdx = $"(({idx}) / 8 + {bn}_offset)";
                string shift = $"(({idx}) % 8) * 4";
                string fetch = $"texelFetch({bn}, ivec2({texelIdx} % {bn}_tileW, {texelIdx} / {bn}_tileW), 0).r";
                string rawNib = $"(({fetch}) >> ({shift})) & 0xF";
                string extractExpr;
                if (info.IsFloat4)
                    extractExpr = $"_e2m1_to_f32(uint({rawNib}))";
                else if (info.IsUnsigned)
                    extractExpr = $"({rawNib})";                                       // QUInt4: zero-extend
                else
                    extractExpr = $"(({rawNib}) >= 8 ? ({rawNib}) - 16 : ({rawNib}))"; // QInt4: sign-extend
                AppendLine($"{target} = {extractExpr};");
                return;
            }
            base.GenerateCode(loadVal);
        }

        /// <summary>Trace a value back to a helper Parameter through view/cast wrappers.</summary>
        private static global::ILGPU.IR.Values.Parameter? ResolveToParam(Value value)
        {
            var v = value.Resolve();
            for (int i = 0; i < 16 && v != null; i++)
            {
                switch (v)
                {
                    case global::ILGPU.IR.Values.Parameter p: return p;
                    case NewView nv: v = nv.Pointer.Resolve(); break;
                    case AddressSpaceCast asc: v = asc.Value.Resolve(); break;
                    case SubViewValue sv: v = sv.Source.Resolve(); break;
                    case global::ILGPU.IR.Values.Load ld: v = ld.Source.Resolve(); break;
                    default: return null;
                }
            }
            return null;
        }

        public static string GetMethodName(Method method)
        {
            // Generate a unique, valid GLSL function name
            string baseName = method.Name ?? "func";
            // Clean name: replace invalid chars
            baseName = baseName.Replace(".", "_").Replace("<", "_").Replace(">", "_")
                               .Replace(",", "_").Replace(" ", "_").Replace("`", "_");
            return $"fn_{baseName}_{method.Id}";
        }
    }
}
