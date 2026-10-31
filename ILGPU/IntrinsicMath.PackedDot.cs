// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: IntrinsicMath.PackedDot.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Intrinsics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    partial class IntrinsicMath
    {
        /// <summary>
        /// The dot product of four packed signed 8-bit integers plus an accumulator:
        /// <c>acc + a0*b0 + a1*b1 + a2*b2 + a3*b3</c>, where byte 0 is the lowest byte
        /// of each operand. The sum wraps on 32-bit overflow, exactly like CUDA's
        /// <c>dp4a.s32.s32</c> and WGSL's <c>acc + dot4I8Packed(a, b)</c>.
        /// </summary>
        /// <remarks>
        /// CUDA sm_61+ runs this as one <c>dp4a</c> instruction and WebGPU as
        /// <c>dot4I8Packed</c> when the browser has the
        /// <c>packed_4x8_integer_dot_product</c> WGSL language feature
        /// (<see cref="Runtime.CapabilityContext.PackedInt8DotProduct"/> tells which);
        /// every other backend runs <see cref="Dot4I8PackedSoftware"/>, which gives the
        /// same bits. Every backend that compiles kernels maps this intrinsic.
        /// </remarks>
        /// <param name="a">Four signed bytes.</param>
        /// <param name="b">Four signed bytes.</param>
        /// <param name="acc">The accumulator.</param>
        /// <returns>The accumulator plus the dot product.</returns>
        [IntrinsicImplementation]
        public static int Dot4I8Packed(uint a, uint b, int acc) =>
            Dot4I8PackedSoftware(a, b, acc);

        /// <summary>
        /// The dot product of four packed unsigned 8-bit integers plus an accumulator:
        /// <c>acc + a0*b0 + a1*b1 + a2*b2 + a3*b3</c>, where byte 0 is the lowest byte
        /// of each operand. The sum wraps on 32-bit overflow, exactly like CUDA's
        /// <c>dp4a.u32.u32</c> and WGSL's <c>acc + dot4U8Packed(a, b)</c>.
        /// </summary>
        /// <remarks>
        /// Native on the same backends as <see cref="Dot4I8Packed(uint, uint, int)"/>;
        /// <see cref="Dot4U8PackedSoftware"/> elsewhere.
        /// </remarks>
        /// <param name="a">Four unsigned bytes.</param>
        /// <param name="b">Four unsigned bytes.</param>
        /// <param name="acc">The accumulator.</param>
        /// <returns>The accumulator plus the dot product.</returns>
        [IntrinsicImplementation]
        public static uint Dot4U8Packed(uint a, uint b, uint acc) =>
            Dot4U8PackedSoftware(a, b, acc);

        /// <summary>
        /// <see cref="Dot4I8Packed(uint, uint, int)"/> as four multiply-adds: the backends
        /// without a native packed dot product redirect the intrinsic here.
        /// </summary>
        /// <remarks>
        /// Each byte is sign-extended as <c>((byte ^ 0x80) - 0x80)</c>, not by shifting it
        /// to the top and back: a left shift into the sign bit is undefined behavior in
        /// GLSL ES 3.0 (ANGLE returned 0 for <c>0x8000 &lt;&lt; 16</c>), and no
        /// sub-word conversions are involved.
        /// </remarks>
        /// <param name="a">Four signed bytes.</param>
        /// <param name="b">Four signed bytes.</param>
        /// <param name="acc">The accumulator.</param>
        /// <returns>The accumulator plus the dot product.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Dot4I8PackedSoftware(uint a, uint b, int acc)
        {
            int ai = (int)a, bi = (int)b;
            return acc
                + (((ai & 0xFF) ^ 0x80) - 0x80) * (((bi & 0xFF) ^ 0x80) - 0x80)
                + (((ai >> 8 & 0xFF) ^ 0x80) - 0x80) * (((bi >> 8 & 0xFF) ^ 0x80) - 0x80)
                + (((ai >> 16 & 0xFF) ^ 0x80) - 0x80) * (((bi >> 16 & 0xFF) ^ 0x80) - 0x80)
                + (((ai >> 24 & 0xFF) ^ 0x80) - 0x80) * (((bi >> 24 & 0xFF) ^ 0x80) - 0x80);
        }

        /// <summary>
        /// <see cref="Dot4U8Packed(uint, uint, uint)"/> as four multiply-adds: the backends
        /// without a native packed dot product redirect the intrinsic here.
        /// </summary>
        /// <param name="a">Four unsigned bytes.</param>
        /// <param name="b">Four unsigned bytes.</param>
        /// <param name="acc">The accumulator.</param>
        /// <returns>The accumulator plus the dot product.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint Dot4U8PackedSoftware(uint a, uint b, uint acc) =>
            acc
            + (a & 0xFFu) * (b & 0xFFu)
            + (a >> 8 & 0xFFu) * (b >> 8 & 0xFFu)
            + (a >> 16 & 0xFFu) * (b >> 16 & 0xFFu)
            + (a >> 24) * (b >> 24);

        /// <summary>
        /// Registers redirects of <see cref="Dot4I8Packed(uint, uint, int)"/> and
        /// <see cref="Dot4U8Packed(uint, uint, uint)"/> to their four-term forms, for a
        /// backend without a native packed dot product.
        /// </summary>
        /// <param name="manager">The intrinsic manager.</param>
        /// <param name="createRedirect">
        /// Creates the backend's redirect implementation for a target method.
        /// </param>
        internal static void RegisterPackedDotSoftware(
            IntrinsicImplementationManager manager,
            System.Func<System.Reflection.MethodInfo, IntrinsicImplementation> createRedirect)
        {
            manager.RegisterMethod(
                typeof(IntrinsicMath).GetMethod(nameof(Dot4I8Packed))!,
                createRedirect(typeof(IntrinsicMath).GetMethod(nameof(Dot4I8PackedSoftware))!));
            manager.RegisterMethod(
                typeof(IntrinsicMath).GetMethod(nameof(Dot4U8Packed))!,
                createRedirect(typeof(IntrinsicMath).GetMethod(nameof(Dot4U8PackedSoftware))!));
        }
    }
}
