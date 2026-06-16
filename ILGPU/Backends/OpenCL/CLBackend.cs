// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: CLBackend.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.OpenCL.Transformations;
using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Transformations;
using ILGPU.Runtime;
using ILGPU.Runtime.OpenCL;
using ILGPU.Util;
using System.Text;

namespace ILGPU.Backends.OpenCL
{
    /// <summary>
    /// Represents an OpenCL source backend.
    /// </summary>
    public sealed class CLBackend :
        CodeGeneratorBackend<
            CLIntrinsic.Handler,
            CLCodeGenerator.GeneratorArgs,
            CLCodeGenerator,
            StringBuilder>
    {
        #region Static

        /// <summary>
        /// Represents the minimum OpenCL C version that is required.
        /// </summary>
        public static readonly CLCVersion MinimumVersion = CLCVersion.CL20;

        #endregion

        #region Instance

        /// <summary>
        /// Returns the list of enabled OpenCL extensions.
        /// </summary>
        private readonly string extensions;

        /// <summary>
        /// Constructs a new OpenCL source backend.
        /// </summary>
        /// <param name="context">The context to use.</param>
        /// <param name="capabilities">The supported capabilities.</param>
        /// <param name="vendor">The associated major vendor.</param>
        /// <param name="clStdVersion">The OpenCL C version passed to -cl-std.</param>
        public CLBackend(
            Context context,
            CLCapabilityContext capabilities,
            CLDeviceVendor vendor,
            CLCVersion clStdVersion)
            : base(
                  context,
                  capabilities,
                  BackendType.OpenCL,
                  new CLArgumentMapper(context))
        {
            Vendor = vendor;
            CLStdVersion = clStdVersion;

            InitIntrinsicProvider();
            InitializeKernelTransformers(builder =>
            {
                var transformerBuilder = Transformer.CreateBuilder(
                    TransformerConfiguration.Empty);
                transformerBuilder.AddBackendOptimizations<CodePlacement.GroupOperands>(
                    new CLAcceleratorSpecializer(
                        PointerType,
                        Context.Properties.EnableIOOperations),
                    context.Properties.InliningMode,
                    context.Properties.OptimizationLevel);
                builder.Add(transformerBuilder.ToTransformer());
            });

            // Build a list of extensions to enable for each OpenCL kernel.
            var extensionBuilder = new StringBuilder();
            foreach (var extensionName in Capabilities.Extensions)
            {
                extensionBuilder.Append("#pragma OPENCL EXTENSION ");
                extensionBuilder.Append(extensionName);
                extensionBuilder.AppendLine(" : enable");
            }

            // Emit Float16 emulation helpers when cl_khr_fp16 is unavailable. CLTypeGenerator
            // promotes Half values to float for compute, so Interop.FloatAsInt(Half) needs to
            // convert the f32 VALUE back to the 16-bit Half BIT PATTERN (not the f32 bits),
            // which AscendingHalf / DescendingHalf radix-sort encodings depend on. The
            // hardware path uses `as_short(half)` directly when shader-fp16 is on; the
            // emulated path calls these helpers instead. They are tiny, no-op when unused,
            // and let the OpenCL compiler optimize out the call when inlined. Mirrors WGSL's
            // _f32_to_f16 / _f16_to_f32 byte-for-byte (denormals flush to signed zero,
            // overflow clamps exp to 31 with mantissa preserved so NaN stays NaN).
            if (!Capabilities.Float16Native)
            {
                extensionBuilder.AppendLine();
                extensionBuilder.AppendLine("// Float16 bit-conversion helpers (cl_khr_fp16 unavailable - Half emulated as float).");
                extensionBuilder.AppendLine("static inline short _f32_to_half_bits(float f) {");
                extensionBuilder.AppendLine("    int bits = as_int(f);");
                extensionBuilder.AppendLine("    int sign = (bits >> 31) & 1;");
                extensionBuilder.AppendLine("    int exp_raw = (bits >> 23) & 0xFF;");
                extensionBuilder.AppendLine("    int exp_adj = exp_raw - 112;");
                extensionBuilder.AppendLine("    int mant = (bits >> 13) & 0x3FF;");
                extensionBuilder.AppendLine("    if (exp_adj < 0) { exp_adj = 0; mant = 0; }");
                extensionBuilder.AppendLine("    if (exp_adj > 31) { exp_adj = 31; }");
                extensionBuilder.AppendLine("    return (short)((sign << 15) | (exp_adj << 10) | mant);");
                extensionBuilder.AppendLine("}");
                extensionBuilder.AppendLine("static inline float _half_bits_to_f32(short h) {");
                extensionBuilder.AppendLine("    int sign = (h >> 15) & 1;");
                extensionBuilder.AppendLine("    int exp_raw = (h >> 10) & 0x1F;");
                extensionBuilder.AppendLine("    int mant = h & 0x3FF;");
                extensionBuilder.AppendLine("    int out_bits;");
                extensionBuilder.AppendLine("    if (exp_raw == 0) { out_bits = sign << 31; }");
                extensionBuilder.AppendLine("    else if (exp_raw == 0x1F) { out_bits = (sign << 31) | (0xFF << 23) | (mant << 13); }");
                extensionBuilder.AppendLine("    else { out_bits = (sign << 31) | ((exp_raw + 112) << 23) | (mant << 13); }");
                extensionBuilder.AppendLine("    return as_float(out_bits);");
                extensionBuilder.AppendLine("}");
                extensionBuilder.AppendLine();
            }

            // BFloat16 bit-conversion helpers - bf16 is ALWAYS emulated on OpenCL (no native bf16 type;
            // bf16 is the top 16 bits of an fp32, so conversion is pure shifting, not the f16 rebias above).
            // Matches the managed / WGSL / GLSL / Wasm bf16 paths byte-for-byte. NaN preserved (force a
            // mantissa bit). static inline -> the OpenCL compiler strips these if the kernel uses no bf16.
            extensionBuilder.AppendLine();
            extensionBuilder.AppendLine("// BFloat16 bit-conversion helpers (bf16 always emulated as float).");
            extensionBuilder.AppendLine("static inline float _bf16_bits_to_f32(ushort h) {");
            extensionBuilder.AppendLine("    return as_float((uint)h << 16);");
            extensionBuilder.AppendLine("}");
            extensionBuilder.AppendLine("static inline ushort _f32_to_bf16_bits(float f) {");
            extensionBuilder.AppendLine("    uint bits = as_uint(f);");
            extensionBuilder.AppendLine("    if ((bits & 0x7FFFFFFFu) > 0x7F800000u) { return (ushort)((bits >> 16) | 0x0040u); }");
            extensionBuilder.AppendLine("    uint lsb = (bits >> 16) & 1u;");
            extensionBuilder.AppendLine("    return (ushort)((bits + 0x7FFFu + lsb) >> 16);");
            extensionBuilder.AppendLine("}");
            extensionBuilder.AppendLine();

            // FP8 bit-conversion helpers - both FP8 formats are ALWAYS emulated (no native OpenCL fp8
            // type; even Hopper-class cvt is unavailable through OpenCL C). Direct ports of the managed
            // ConvertFloat8E*M*ToFloat / ConvertFloatToFloat8E*M* (CPU-verified idempotence 0/256), so
            // every representable value round-trips bit-identically. E5M2 (1/5/2 bias 15) is IEEE-style
            // (Inf+NaN); E4M3 (1/4/3 bias 7, "E4M3FN") has NO Inf, single NaN 0x7F, saturates to +-448.
            // static inline -> the OpenCL compiler strips these when the kernel uses no fp8.
            extensionBuilder.AppendLine("// FP8 E5M2 bit-conversion helpers (always emulated as float).");
            extensionBuilder.AppendLine("static inline float _e5m2_bits_to_f32(uchar raw) {");
            extensionBuilder.AppendLine("    uint bits = raw;");
            extensionBuilder.AppendLine("    uint sign = (bits & 0x80u) << 24;");
            extensionBuilder.AppendLine("    uint expo = (bits >> 2) & 0x1Fu;");
            extensionBuilder.AppendLine("    uint mant = bits & 0x03u;");
            extensionBuilder.AppendLine("    if (expo == 0u) {");
            extensionBuilder.AppendLine("        if (mant == 0u) return as_float(sign);");
            extensionBuilder.AppendLine("        uint e = 127u - 15u + 1u; uint m = mant;");
            extensionBuilder.AppendLine("        while ((m & 0x04u) == 0u) { m <<= 1; e -= 1u; }");
            extensionBuilder.AppendLine("        m &= 0x03u; return as_float(sign | (e << 23) | (m << 21));");
            extensionBuilder.AppendLine("    }");
            extensionBuilder.AppendLine("    if (expo == 0x1Fu) return as_float(sign | (0xFFu << 23) | (mant << 21));");
            extensionBuilder.AppendLine("    uint f32Exp = expo - 15u + 127u;");
            extensionBuilder.AppendLine("    return as_float(sign | (f32Exp << 23) | (mant << 21));");
            extensionBuilder.AppendLine("}");
            extensionBuilder.AppendLine("static inline uchar _f32_to_e5m2_bits(float f) {");
            extensionBuilder.AppendLine("    uint bits = as_uint(f);");
            extensionBuilder.AppendLine("    uint sign = (bits >> 24) & 0x80u;");
            extensionBuilder.AppendLine("    uint rest = bits & 0x7FFFFFFFu;");
            extensionBuilder.AppendLine("    if (rest > 0x7F800000u) return (uchar)(sign | 0x7Fu);");
            extensionBuilder.AppendLine("    if (rest == 0x7F800000u) return (uchar)(sign | 0x7Cu);");
            extensionBuilder.AppendLine("    int f32Exp = (int)((rest >> 23) & 0xFFu);");
            extensionBuilder.AppendLine("    uint f32Mant = rest & 0x7FFFFFu;");
            extensionBuilder.AppendLine("    int e = f32Exp - 127;");
            extensionBuilder.AppendLine("    if (e > 15) return (uchar)(sign | 0x7Cu);");
            extensionBuilder.AppendLine("    if (e < -14) {");
            extensionBuilder.AppendLine("        if (f32Exp == 0) return (uchar)sign;");
            extensionBuilder.AppendLine("        uint signif = f32Mant | 0x800000u;");
            extensionBuilder.AppendLine("        int shift = (-14 - e) + 21;");
            extensionBuilder.AppendLine("        if (shift > 31) return (uchar)sign;");
            extensionBuilder.AppendLine("        uint m = signif >> shift;");
            extensionBuilder.AppendLine("        uint roundBit = (signif >> (shift - 1)) & 1u;");
            extensionBuilder.AppendLine("        uint sticky = (signif & ((1u << (shift - 1)) - 1u)) != 0u ? 1u : 0u;");
            extensionBuilder.AppendLine("        if (roundBit == 1u && (sticky == 1u || (m & 1u) == 1u)) m += 1u;");
            extensionBuilder.AppendLine("        return (uchar)(sign | (m & 0x03u) | ((m >> 2) << 2));");
            extensionBuilder.AppendLine("    }");
            extensionBuilder.AppendLine("    uint mant2 = f32Mant >> 21;");
            extensionBuilder.AppendLine("    uint round = (f32Mant >> 20) & 1u;");
            extensionBuilder.AppendLine("    uint stick = (f32Mant & 0xFFFFFu) != 0u ? 1u : 0u;");
            extensionBuilder.AppendLine("    uint eField = (uint)(e + 15);");
            extensionBuilder.AppendLine("    uint outBits = (eField << 2) | mant2;");
            extensionBuilder.AppendLine("    if (round == 1u && (stick == 1u || (mant2 & 1u) == 1u)) outBits += 1u;");
            extensionBuilder.AppendLine("    return (uchar)(sign | (outBits & 0x7Fu));");
            extensionBuilder.AppendLine("}");
            extensionBuilder.AppendLine();
            extensionBuilder.AppendLine("// FP8 E4M3 (E4M3FN) bit-conversion helpers (always emulated as float; no Inf, saturate to 448).");
            extensionBuilder.AppendLine("static inline float _e4m3_bits_to_f32(uchar raw) {");
            extensionBuilder.AppendLine("    uint bits = raw;");
            extensionBuilder.AppendLine("    uint sign = (bits & 0x80u) << 24;");
            extensionBuilder.AppendLine("    uint expo = (bits >> 3) & 0x0Fu;");
            extensionBuilder.AppendLine("    uint mant = bits & 0x07u;");
            extensionBuilder.AppendLine("    if ((bits & 0x7Fu) == 0x7Fu) return as_float(sign | 0x7FC00000u);");
            extensionBuilder.AppendLine("    if (expo == 0u) {");
            extensionBuilder.AppendLine("        if (mant == 0u) return as_float(sign);");
            extensionBuilder.AppendLine("        uint e = 127u - 7u + 1u; uint m = mant;");
            extensionBuilder.AppendLine("        while ((m & 0x08u) == 0u) { m <<= 1; e -= 1u; }");
            extensionBuilder.AppendLine("        m &= 0x07u; return as_float(sign | (e << 23) | (m << 20));");
            extensionBuilder.AppendLine("    }");
            extensionBuilder.AppendLine("    uint f32Exp = expo - 7u + 127u;");
            extensionBuilder.AppendLine("    return as_float(sign | (f32Exp << 23) | (mant << 20));");
            extensionBuilder.AppendLine("}");
            extensionBuilder.AppendLine("static inline uchar _f32_to_e4m3_bits(float f) {");
            extensionBuilder.AppendLine("    uint bits = as_uint(f);");
            extensionBuilder.AppendLine("    uint sign = (bits >> 24) & 0x80u;");
            extensionBuilder.AppendLine("    uint rest = bits & 0x7FFFFFFFu;");
            extensionBuilder.AppendLine("    if (rest >= 0x7F800000u) return (uchar)(sign | 0x7Fu);");
            extensionBuilder.AppendLine("    int f32Exp = (int)((rest >> 23) & 0xFFu);");
            extensionBuilder.AppendLine("    uint f32Mant = rest & 0x7FFFFFu;");
            extensionBuilder.AppendLine("    int e = f32Exp - 127;");
            extensionBuilder.AppendLine("    if (e > 8 || (e == 8 && f32Mant > 0x600000u)) return (uchar)(sign | 0x7Eu);");
            extensionBuilder.AppendLine("    if (e < -6) {");
            extensionBuilder.AppendLine("        if (f32Exp == 0) return (uchar)sign;");
            extensionBuilder.AppendLine("        uint signif = f32Mant | 0x800000u;");
            extensionBuilder.AppendLine("        int shift = (-6 - e) + 20;");
            extensionBuilder.AppendLine("        if (shift > 31) return (uchar)sign;");
            extensionBuilder.AppendLine("        uint m = signif >> shift;");
            extensionBuilder.AppendLine("        uint roundBit = (signif >> (shift - 1)) & 1u;");
            extensionBuilder.AppendLine("        uint sticky = (signif & ((1u << (shift - 1)) - 1u)) != 0u ? 1u : 0u;");
            extensionBuilder.AppendLine("        if (roundBit == 1u && (sticky == 1u || (m & 1u) == 1u)) m += 1u;");
            extensionBuilder.AppendLine("        return (uchar)(sign | (m & 0x7Fu));");
            extensionBuilder.AppendLine("    }");
            extensionBuilder.AppendLine("    uint mant3 = f32Mant >> 20;");
            extensionBuilder.AppendLine("    uint round = (f32Mant >> 19) & 1u;");
            extensionBuilder.AppendLine("    uint stick = (f32Mant & 0x7FFFFu) != 0u ? 1u : 0u;");
            extensionBuilder.AppendLine("    uint eField = (uint)(e + 7);");
            extensionBuilder.AppendLine("    uint outBits = (eField << 3) | mant3;");
            extensionBuilder.AppendLine("    if (round == 1u && (stick == 1u || (mant3 & 1u) == 1u)) outBits += 1u;");
            extensionBuilder.AppendLine("    if ((outBits & 0x7Fu) >= 0x7Fu) outBits = 0x7Eu;");
            extensionBuilder.AppendLine("    return (uchar)(sign | (outBits & 0x7Fu));");
            extensionBuilder.AppendLine("}");
            extensionBuilder.AppendLine();

            extensions = extensionBuilder.ToString();
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the associated major device vendor.
        /// </summary>
        public CLDeviceVendor Vendor { get; }

        /// <summary>
        /// Returns the associated OpenCL C version.
        /// </summary>
        public CLCVersion CLStdVersion { get; }

        /// <summary>
        /// Returns the associated <see cref="Backend.ArgumentMapper"/>.
        /// </summary>
        public new CLArgumentMapper ArgumentMapper =>
            base.ArgumentMapper.AsNotNullCast<CLArgumentMapper>();

        /// <summary>
        /// Returns the capabilities of this accelerator.
        /// </summary>
        public new CLCapabilityContext Capabilities =>
            base.Capabilities.AsNotNullCast<CLCapabilityContext>();

        #endregion

        #region Methods

        /// <summary>
        /// Creates a new <see cref="SeparateViewEntryPoint"/> instance.
        /// </summary>
        protected override EntryPoint CreateEntryPoint(
            in EntryPointDescription entry,
            in BackendContext backendContext,
            in KernelSpecialization specialization) =>
            new SeparateViewEntryPoint(
                entry,
                backendContext.SharedMemorySpecification,
                specialization,
                Context.TypeContext,
                2);

        /// <summary>
        /// Creates a new <see cref="StringBuilder"/> and configures a
        /// <see cref="CLCodeGenerator.GeneratorArgs"/> instance.
        /// </summary>
        protected override StringBuilder CreateKernelBuilder(
            EntryPoint entryPoint,
            in BackendContext backendContext,
            in KernelSpecialization specialization,
            out CLCodeGenerator.GeneratorArgs data)
        {
            // Ensure that all intrinsics can be generated
            backendContext.EnsureIntrinsicImplementations(IntrinsicProvider);

            var builder = new StringBuilder();

            builder.AppendLine("//");
            builder.Append("// Generated by ILGPU v");
            builder.AppendLine(Context.Version);
            builder.AppendLine("//");
            builder.AppendLine(extensions);

            var typeGenerator = new CLTypeGenerator(Context.TypeContext, Capabilities);

            data = new CLCodeGenerator.GeneratorArgs(
                this,
                typeGenerator,
                entryPoint.AsNotNullCast<SeparateViewEntryPoint>(),
                backendContext.SharedAllocations,
                backendContext.DynamicSharedAllocations);
            return builder;
        }

        /// <summary>
        /// Creates a new <see cref="CLFunctionGenerator"/>.
        /// </summary>
        protected override CLCodeGenerator CreateFunctionCodeGenerator(
            Method method,
            Allocas allocas,
            CLCodeGenerator.GeneratorArgs data) =>
            new CLFunctionGenerator(data, method, allocas);

        /// <summary>
        /// Generates a new <see cref="CLKernelFunctionGenerator"/>.
        /// </summary>
        protected override CLCodeGenerator CreateKernelCodeGenerator(
            in AllocaKindInformation sharedAllocations,
            Method method,
            Allocas allocas,
            CLCodeGenerator.GeneratorArgs data) =>
            new CLKernelFunctionGenerator(data, method, allocas);

        /// <summary>
        /// Creates a new <see cref="CLCompiledKernel"/>.
        /// </summary>
        protected override CompiledKernel CreateKernel(
            EntryPoint entryPoint,
            CompiledKernel.KernelInfo? kernelInfo,
            StringBuilder builder,
            CLCodeGenerator.GeneratorArgs data)
        {
            var typeBuilder = new StringBuilder();
            data.TypeGenerator.GenerateTypeDeclarations(typeBuilder);
            data.KernelTypeGenerator.GenerateTypeDeclarations(typeBuilder);

            data.TypeGenerator.GenerateTypeDefinitions(typeBuilder);
            data.KernelTypeGenerator.GenerateTypeDefinitions(typeBuilder);

            builder.Insert(0, typeBuilder.ToString());

            var clSource = builder.ToString();
            return new CLCompiledKernel(
                Context,
                entryPoint.AsNotNullCast<SeparateViewEntryPoint>(),
                kernelInfo,
                clSource,
                CLStdVersion);
        }

        #endregion
    }
}
