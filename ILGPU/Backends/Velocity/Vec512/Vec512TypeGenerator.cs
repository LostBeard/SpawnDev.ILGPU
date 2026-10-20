// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: Vec512TypeGenerator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.IL;
using ILGPU.Runtime.Velocity;
using System;
using System.Numerics;

#if NET7_0_OR_GREATER

namespace ILGPU.Backends.Velocity.Vec512
{
    /// <summary>
    /// A vector type generator of 128bit vectors to be used with the Velocity backend.
    /// </summary>
    sealed class Vec512TypeGenerator : VelocityTypeGenerator
    {
        #region Static

        /// <summary>
        /// Maps basic types to vectorized basic types.
        /// </summary>
        private static readonly Type[] VectorizedBasicTypeMapping = new Type[]
        {
            Vec512Operations.WarpType32, // None/Unknown

            Vec512Operations.WarpType32, // Int1
            Vec512Operations.WarpType32, // Int8
            Vec512Operations.WarpType32, // Int16
            Vec512Operations.WarpType32, // Int32
            Vec512Operations.WarpType64, // Int64

            Vec512Operations.WarpType32, // Float16
            Vec512Operations.WarpType32, // Float32
            Vec512Operations.WarpType64, // Float64

            Vec512Operations.WarpType32, // BFloat16 (ordinal 9) - placeholder (Phase 3)
            Vec512Operations.WarpType32, // Float8E4M3 (ordinal 10) - placeholder
            Vec512Operations.WarpType32, // Float8E5M2 (ordinal 11) - placeholder
            Vec512Operations.WarpType32, // Float4E2M1 (ordinal 12) - placeholder
            Vec512Operations.WarpType32, // QInt4 (ordinal 13), a raw nibble
        };

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new IL vector type generator.
        /// </summary>
        /// <param name="capabilityContext">The parent capability context.</param>
        /// <param name="runtimeSystem">The parent runtime system.</param>
        public Vec512TypeGenerator(
            VelocityCapabilityContext capabilityContext,
            RuntimeSystem runtimeSystem)
            : base(capabilityContext, runtimeSystem, Vec512Operations.WarpSize)
        { }

        #endregion

        #region Type System

        public override Type GetVectorizedBasicType(BasicValueType basicValueType)
        {
            return VectorizedBasicTypeMapping[(int)basicValueType];
        }

        #endregion
    }
}

#endif
