// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: WebGPUIntrinsic.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Intrinsics;
using ILGPU.Util;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace ILGPU.Backends.WebGPU
{
    /// <summary>
    /// Represents a specific handler for user defined code-generation functionality
    /// that is compatible with the WebGPU backend.
    /// </summary>
    public sealed class WebGPUIntrinsic : IntrinsicImplementation
    {
        #region Nested Types

        /// <summary>
        /// Represents the handler delegate type of custom code-generation handlers.
        /// Backend and CodeGenerator types are left as object since WGSLCodeGenerator
        /// is in a different assembly.
        /// </summary>
        /// <param name="backend">The current backend.</param>
        /// <param name="codeGenerator">The code generator.</param>
        /// <param name="value">The value to generate code for.</param>
        public delegate void Handler(
            object backend,
            object codeGenerator,
            Value value);

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new WebGPU intrinsic that can handle all architectures.
        /// </summary>
        /// <param name="targetMethod">The associated target method.</param>
        /// <param name="mode">The code-generation mode.</param>
        public WebGPUIntrinsic(MethodInfo targetMethod, IntrinsicImplementationMode mode)
            : base(
                  BackendType.WebGPU,
                  targetMethod,
                  mode)
        { }

        /// <summary>
        /// Constructs a new WebGPU intrinsic that can handle all architectures.
        /// </summary>
        /// <param name="handlerType">The associated target handler type.</param>
        /// <param name="mode">The code-generation mode.</param>
        public WebGPUIntrinsic(
            [DynamicallyAccessedMembers(TrimmingAnnotations.HandlerMethods)]
            Type handlerType,
            IntrinsicImplementationMode mode)
            : base(
                  BackendType.WebGPU,
                  handlerType,
                  null,
                  mode)
        { }

        /// <summary>
        /// Constructs a new WebGPU intrinsic that can handle all architectures.
        /// </summary>
        /// <param name="handlerType">The associated target handler type.</param>
        /// <param name="methodName">The target method name (or null).</param>
        /// <param name="mode">The code-generator mode.</param>
        public WebGPUIntrinsic(
            [DynamicallyAccessedMembers(TrimmingAnnotations.HandlerMethods)]
            Type handlerType,
            string methodName,
            IntrinsicImplementationMode mode)
            : base(
                  BackendType.WebGPU,
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
            backend.BackendType == BackendType.WebGPU;

        #endregion
    }
}
