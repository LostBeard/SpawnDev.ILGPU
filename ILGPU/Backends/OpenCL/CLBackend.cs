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
