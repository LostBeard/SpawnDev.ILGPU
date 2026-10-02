using System.Diagnostics.CodeAnalysis;
// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: WGSLIntrinsic.cs
//
// WGSL intrinsic handler delegate for WebGPU backend code generation.
// ---------------------------------------------------------------------------------------

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Contains intrinsic handler definitions for WGSL code generation.
    /// </summary>
    public static class WGSLIntrinsic
    {
        /// <summary>
        /// Represents a handler for WGSL intrinsic code generation.
        /// </summary>
        /// <param name="backend">The parent WebGPU backend.</param>
        /// <param name="codeGenerator">The code generator to use.</param>
        /// <param name="value">The value to generate code for.</param>
        public delegate void Handler(
            WebGPUBackend backend,
            WGSLCodeGenerator codeGenerator,
            global::ILGPU.IR.Value value);
    }

    /// <summary>
    /// Represents a specific handler for user defined code-generation functionality
    /// that is compatible with the <see cref="WebGPUBackend"/>.
    /// </summary>
    public sealed class WebGPUIntrinsic : global::ILGPU.IR.Intrinsics.IntrinsicImplementation
    {
        /// <summary>
        /// Constructs a new WebGPU intrinsic.
        /// </summary>
        /// <param name="targetMethod">The associated target method.</param>
        /// <param name="mode">The code-generation mode.</param>
        public WebGPUIntrinsic(System.Reflection.MethodInfo targetMethod, global::ILGPU.IR.Intrinsics.IntrinsicImplementationMode mode)
            : base(
                  WebGPUBackend.BackendTypeWebGPU,
                  targetMethod,
                  mode)
        { }

        /// <summary>
        /// Constructs a new WebGPU intrinsic.
        /// </summary>
        /// <param name="handlerType">The associated target handler type.</param>
        /// <param name="methodName">The target method name (or null).</param>
        /// <param name="mode">The code-generator mode.</param>
        public WebGPUIntrinsic([DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.HandlerMethods)] System.Type handlerType, string methodName, global::ILGPU.IR.Intrinsics.IntrinsicImplementationMode mode)
            : base(
                  WebGPUBackend.BackendTypeWebGPU,
                  handlerType,
                  methodName,
                  mode)
        { }

        /// <summary cref="global::ILGPU.IR.Intrinsics.IntrinsicImplementation.CanHandleBackend(global::ILGPU.Backends.Backend)"/>
        protected override bool CanHandleBackend(global::ILGPU.Backends.Backend backend) =>
            backend is WebGPUBackend;
    }
}
