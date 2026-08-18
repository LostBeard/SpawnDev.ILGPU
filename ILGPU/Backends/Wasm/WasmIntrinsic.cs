// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2024-2026 SpawnDev / LostBeard
//
// File: WasmIntrinsic.cs
//
// Wasm-specific intrinsic implementation handler.
// Mirrors WebGPUIntrinsic.cs but targets BackendType.Wasm.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Intrinsics;
using ILGPU.Util;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ILGPU.Backends.Wasm
{
    /// <summary>
    /// Represents a specific handler for user defined code-generation functionality
    /// that is compatible with the Wasm backend.
    /// </summary>
    public sealed class WasmIntrinsic : IntrinsicImplementation
    {
        #region Instance

        /// <summary>
        /// Constructs a new Wasm intrinsic that can handle all architectures.
        /// </summary>
        /// <param name="targetMethod">The associated target method.</param>
        /// <param name="mode">The code-generation mode.</param>
        public WasmIntrinsic(MethodInfo targetMethod, IntrinsicImplementationMode mode)
            : base(
                  BackendType.Wasm,
                  targetMethod,
                  mode)
        { }

        /// <summary>
        /// Constructs a new Wasm intrinsic that can handle all architectures.
        /// </summary>
        /// <param name="handlerType">The associated target handler type.</param>
        /// <param name="mode">The code-generation mode.</param>
        public WasmIntrinsic(
            [DynamicallyAccessedMembers(TrimmingAnnotations.HandlerMethods)]
            Type handlerType,
            IntrinsicImplementationMode mode)
            : base(
                  BackendType.Wasm,
                  handlerType,
                  null,
                  mode)
        { }

        /// <summary>
        /// Constructs a new Wasm intrinsic that can handle all architectures.
        /// </summary>
        /// <param name="handlerType">The associated target handler type.</param>
        /// <param name="methodName">The target method name (or null).</param>
        /// <param name="mode">The code-generator mode.</param>
        public WasmIntrinsic(
            [DynamicallyAccessedMembers(TrimmingAnnotations.HandlerMethods)]
            Type handlerType,
            string methodName,
            IntrinsicImplementationMode mode)
            : base(
                  BackendType.Wasm,
                  handlerType,
                  methodName,
                  mode)
        { }

        #endregion

        #region Methods

        /// <summary>
        /// Returns true if this intrinsic can handle the given backend.
        /// </summary>
        protected internal override bool CanHandleBackend(Backend backend) =>
            backend.BackendType == BackendType.Wasm;

        #endregion
    }
}
