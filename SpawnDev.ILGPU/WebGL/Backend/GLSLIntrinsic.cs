using System.Diagnostics.CodeAnalysis;
// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGL
//                        Copyright (c) 2024 SpawnDev Project
//
// File: GLSLIntrinsic.cs
//
// GLSL intrinsic handler delegate for WebGL2 backend code generation.
// ---------------------------------------------------------------------------------------

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Contains intrinsic handler definitions for GLSL code generation.
    /// </summary>
    public static class GLSLIntrinsic
    {
        /// <summary>
        /// Represents a handler for GLSL intrinsic code generation.
        /// </summary>
        /// <param name="backend">The parent WebGL backend.</param>
        /// <param name="codeGenerator">The code generator to use.</param>
        /// <param name="value">The value to generate code for.</param>
        public delegate void Handler(
            WebGLBackend backend,
            GLSLCodeGenerator codeGenerator,
            global::ILGPU.IR.Value value);
    }

    /// <summary>
    /// Represents a specific handler for user defined code-generation functionality
    /// that is compatible with the <see cref="WebGLBackend"/>.
    /// </summary>
    public sealed class WebGLIntrinsic : global::ILGPU.IR.Intrinsics.IntrinsicImplementation
    {
        /// <summary>
        /// Constructs a new WebGL intrinsic.
        /// </summary>
        /// <param name="targetMethod">The associated target method.</param>
        /// <param name="mode">The code-generation mode.</param>
        public WebGLIntrinsic(System.Reflection.MethodInfo targetMethod, global::ILGPU.IR.Intrinsics.IntrinsicImplementationMode mode)
            : base(
                  WebGLBackend.BackendTypeWebGL,
                  targetMethod,
                  mode)
        { }

        /// <summary>
        /// Constructs a new WebGL intrinsic.
        /// </summary>
        /// <param name="handlerType">The associated target handler type.</param>
        /// <param name="methodName">The target method name (or null).</param>
        /// <param name="mode">The code-generator mode.</param>
        public WebGLIntrinsic([DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.HandlerMethods)] System.Type handlerType, string methodName, global::ILGPU.IR.Intrinsics.IntrinsicImplementationMode mode)
            : base(
                  WebGLBackend.BackendTypeWebGL,
                  handlerType,
                  methodName,
                  mode)
        { }

        /// <summary cref="global::ILGPU.IR.Intrinsics.IntrinsicImplementation.CanHandleBackend(global::ILGPU.Backends.Backend)"/>
        protected override bool CanHandleBackend(global::ILGPU.Backends.Backend backend) =>
            backend is WebGLBackend;
    }
}
