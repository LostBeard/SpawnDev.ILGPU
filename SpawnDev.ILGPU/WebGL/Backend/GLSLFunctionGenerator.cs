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
using System.Text;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Generates GLSL ES 3.0 code for helper (non-kernel) functions.
    /// These are called by the kernel entry point or by other helpers.
    /// </summary>
    internal sealed class GLSLFunctionGenerator : GLSLCodeGenerator
    {
        public GLSLFunctionGenerator(in GeneratorArgs args, Method method, Allocas allocas)
            : base(args, method, allocas)
        {
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
