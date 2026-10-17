// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2021-2022 ILGPU Project
//                                    www.ilgpu.net
//
// File: CudaAsm.Generated.tt/CudaAsm.Generated.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Frontend.Intrinsic;
using System;

namespace ILGPU.Runtime.Cuda
{
    /// <summary>
    /// Provides library calls for inline PTX assembly instructions.
    /// </summary>
    partial class CudaAsm
    {
        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0>(
            string asm,
            T0 arg0)
            where T0 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0>(
            string asm,
            out T0 arg0)
            where T0 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1>(
            string asm,
            T0 arg0, T1 arg1)
            where T0 : struct
            where T1 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1>(
            string asm,
            out T0 arg0, T1 arg1)
            where T0 : struct
            where T1 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2)
            where T0 : struct
            where T1 : struct
            where T2 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2)
            where T0 : struct
            where T1 : struct
            where T2 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2, T3 arg3)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2, T3 arg3)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            string asm,
            T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            string asm,
            out T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1>(
            string asm,
            out T0 arg0, out T1 arg1)
            where T0 : struct
            where T1 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2)
            where T0 : struct
            where T1 : struct
            where T2 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct
            where T14 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
            string asm,
            out T0 arg0, out T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct
            where T14 : struct
            where T15 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2)
            where T0 : struct
            where T1 : struct
            where T2 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct
            where T14 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct
            where T14 : struct
            where T15 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct
            where T14 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel (several output operands).
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction (output).</param>
        /// <param name="arg1">Argument %1 of the PTX instruction (output).</param>
        /// <param name="arg2">Argument %2 of the PTX instruction (output).</param>
        /// <param name="arg3">Argument %3 of the PTX instruction (output).</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitPTX)]
        public static void Emit<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
            string asm,
            out T0 arg0, out T1 arg1, out T2 arg2, out T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14, T15 arg15)
            where T0 : struct
            where T1 : struct
            where T2 : struct
            where T3 : struct
            where T4 : struct
            where T5 : struct
            where T6 : struct
            where T7 : struct
            where T8 : struct
            where T9 : struct
            where T10 : struct
            where T11 : struct
            where T12 : struct
            where T13 : struct
            where T14 : struct
            where T15 : struct =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0>(
            string asm,
            ref T0 arg0)
            where T0 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1>(
            string asm,
            ref T0 arg0, ref T1 arg1)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        /// <param name="arg37">Argument %37 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36, ref T37 arg37)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter
            where T37 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        /// <param name="arg37">Argument %37 of the PTX instruction.</param>
        /// <param name="arg38">Argument %38 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37, T38>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36, ref T37 arg37, ref T38 arg38)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter
            where T37 : struct, ICudaAsmEmitParameter
            where T38 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        /// <param name="arg37">Argument %37 of the PTX instruction.</param>
        /// <param name="arg38">Argument %38 of the PTX instruction.</param>
        /// <param name="arg39">Argument %39 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37, T38, T39>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36, ref T37 arg37, ref T38 arg38, ref T39 arg39)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter
            where T37 : struct, ICudaAsmEmitParameter
            where T38 : struct, ICudaAsmEmitParameter
            where T39 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        /// <param name="arg37">Argument %37 of the PTX instruction.</param>
        /// <param name="arg38">Argument %38 of the PTX instruction.</param>
        /// <param name="arg39">Argument %39 of the PTX instruction.</param>
        /// <param name="arg40">Argument %40 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37, T38, T39, T40>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36, ref T37 arg37, ref T38 arg38, ref T39 arg39, ref T40 arg40)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter
            where T37 : struct, ICudaAsmEmitParameter
            where T38 : struct, ICudaAsmEmitParameter
            where T39 : struct, ICudaAsmEmitParameter
            where T40 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        /// <param name="arg37">Argument %37 of the PTX instruction.</param>
        /// <param name="arg38">Argument %38 of the PTX instruction.</param>
        /// <param name="arg39">Argument %39 of the PTX instruction.</param>
        /// <param name="arg40">Argument %40 of the PTX instruction.</param>
        /// <param name="arg41">Argument %41 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37, T38, T39, T40, T41>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36, ref T37 arg37, ref T38 arg38, ref T39 arg39, ref T40 arg40, ref T41 arg41)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter
            where T37 : struct, ICudaAsmEmitParameter
            where T38 : struct, ICudaAsmEmitParameter
            where T39 : struct, ICudaAsmEmitParameter
            where T40 : struct, ICudaAsmEmitParameter
            where T41 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        /// <param name="arg37">Argument %37 of the PTX instruction.</param>
        /// <param name="arg38">Argument %38 of the PTX instruction.</param>
        /// <param name="arg39">Argument %39 of the PTX instruction.</param>
        /// <param name="arg40">Argument %40 of the PTX instruction.</param>
        /// <param name="arg41">Argument %41 of the PTX instruction.</param>
        /// <param name="arg42">Argument %42 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37, T38, T39, T40, T41, T42>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36, ref T37 arg37, ref T38 arg38, ref T39 arg39, ref T40 arg40, ref T41 arg41, ref T42 arg42)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter
            where T37 : struct, ICudaAsmEmitParameter
            where T38 : struct, ICudaAsmEmitParameter
            where T39 : struct, ICudaAsmEmitParameter
            where T40 : struct, ICudaAsmEmitParameter
            where T41 : struct, ICudaAsmEmitParameter
            where T42 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

        /// <summary>
        /// Writes the inline PTX assembly instructions into the kernel.
        /// </summary>
        /// <param name="asm">The PTX assembly instruction string.</param>
        /// <param name="arg0">Argument %0 of the PTX instruction.</param>
        /// <param name="arg1">Argument %1 of the PTX instruction.</param>
        /// <param name="arg2">Argument %2 of the PTX instruction.</param>
        /// <param name="arg3">Argument %3 of the PTX instruction.</param>
        /// <param name="arg4">Argument %4 of the PTX instruction.</param>
        /// <param name="arg5">Argument %5 of the PTX instruction.</param>
        /// <param name="arg6">Argument %6 of the PTX instruction.</param>
        /// <param name="arg7">Argument %7 of the PTX instruction.</param>
        /// <param name="arg8">Argument %8 of the PTX instruction.</param>
        /// <param name="arg9">Argument %9 of the PTX instruction.</param>
        /// <param name="arg10">Argument %10 of the PTX instruction.</param>
        /// <param name="arg11">Argument %11 of the PTX instruction.</param>
        /// <param name="arg12">Argument %12 of the PTX instruction.</param>
        /// <param name="arg13">Argument %13 of the PTX instruction.</param>
        /// <param name="arg14">Argument %14 of the PTX instruction.</param>
        /// <param name="arg15">Argument %15 of the PTX instruction.</param>
        /// <param name="arg16">Argument %16 of the PTX instruction.</param>
        /// <param name="arg17">Argument %17 of the PTX instruction.</param>
        /// <param name="arg18">Argument %18 of the PTX instruction.</param>
        /// <param name="arg19">Argument %19 of the PTX instruction.</param>
        /// <param name="arg20">Argument %20 of the PTX instruction.</param>
        /// <param name="arg21">Argument %21 of the PTX instruction.</param>
        /// <param name="arg22">Argument %22 of the PTX instruction.</param>
        /// <param name="arg23">Argument %23 of the PTX instruction.</param>
        /// <param name="arg24">Argument %24 of the PTX instruction.</param>
        /// <param name="arg25">Argument %25 of the PTX instruction.</param>
        /// <param name="arg26">Argument %26 of the PTX instruction.</param>
        /// <param name="arg27">Argument %27 of the PTX instruction.</param>
        /// <param name="arg28">Argument %28 of the PTX instruction.</param>
        /// <param name="arg29">Argument %29 of the PTX instruction.</param>
        /// <param name="arg30">Argument %30 of the PTX instruction.</param>
        /// <param name="arg31">Argument %31 of the PTX instruction.</param>
        /// <param name="arg32">Argument %32 of the PTX instruction.</param>
        /// <param name="arg33">Argument %33 of the PTX instruction.</param>
        /// <param name="arg34">Argument %34 of the PTX instruction.</param>
        /// <param name="arg35">Argument %35 of the PTX instruction.</param>
        /// <param name="arg36">Argument %36 of the PTX instruction.</param>
        /// <param name="arg37">Argument %37 of the PTX instruction.</param>
        /// <param name="arg38">Argument %38 of the PTX instruction.</param>
        /// <param name="arg39">Argument %39 of the PTX instruction.</param>
        /// <param name="arg40">Argument %40 of the PTX instruction.</param>
        /// <param name="arg41">Argument %41 of the PTX instruction.</param>
        /// <param name="arg42">Argument %42 of the PTX instruction.</param>
        /// <param name="arg43">Argument %43 of the PTX instruction.</param>
        [LanguageIntrinsic(LanguageIntrinsicKind.EmitRefPTX)]
        public static void EmitRef<T0, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14, T15, T16, T17, T18, T19, T20, T21, T22, T23, T24, T25, T26, T27, T28, T29, T30, T31, T32, T33, T34, T35, T36, T37, T38, T39, T40, T41, T42, T43>(
            string asm,
            ref T0 arg0, ref T1 arg1, ref T2 arg2, ref T3 arg3, ref T4 arg4, ref T5 arg5, ref T6 arg6, ref T7 arg7, ref T8 arg8, ref T9 arg9, ref T10 arg10, ref T11 arg11, ref T12 arg12, ref T13 arg13, ref T14 arg14, ref T15 arg15, ref T16 arg16, ref T17 arg17, ref T18 arg18, ref T19 arg19, ref T20 arg20, ref T21 arg21, ref T22 arg22, ref T23 arg23, ref T24 arg24, ref T25 arg25, ref T26 arg26, ref T27 arg27, ref T28 arg28, ref T29 arg29, ref T30 arg30, ref T31 arg31, ref T32 arg32, ref T33 arg33, ref T34 arg34, ref T35 arg35, ref T36 arg36, ref T37 arg37, ref T38 arg38, ref T39 arg39, ref T40 arg40, ref T41 arg41, ref T42 arg42, ref T43 arg43)
            where T0 : struct, ICudaAsmEmitParameter
            where T1 : struct, ICudaAsmEmitParameter
            where T2 : struct, ICudaAsmEmitParameter
            where T3 : struct, ICudaAsmEmitParameter
            where T4 : struct, ICudaAsmEmitParameter
            where T5 : struct, ICudaAsmEmitParameter
            where T6 : struct, ICudaAsmEmitParameter
            where T7 : struct, ICudaAsmEmitParameter
            where T8 : struct, ICudaAsmEmitParameter
            where T9 : struct, ICudaAsmEmitParameter
            where T10 : struct, ICudaAsmEmitParameter
            where T11 : struct, ICudaAsmEmitParameter
            where T12 : struct, ICudaAsmEmitParameter
            where T13 : struct, ICudaAsmEmitParameter
            where T14 : struct, ICudaAsmEmitParameter
            where T15 : struct, ICudaAsmEmitParameter
            where T16 : struct, ICudaAsmEmitParameter
            where T17 : struct, ICudaAsmEmitParameter
            where T18 : struct, ICudaAsmEmitParameter
            where T19 : struct, ICudaAsmEmitParameter
            where T20 : struct, ICudaAsmEmitParameter
            where T21 : struct, ICudaAsmEmitParameter
            where T22 : struct, ICudaAsmEmitParameter
            where T23 : struct, ICudaAsmEmitParameter
            where T24 : struct, ICudaAsmEmitParameter
            where T25 : struct, ICudaAsmEmitParameter
            where T26 : struct, ICudaAsmEmitParameter
            where T27 : struct, ICudaAsmEmitParameter
            where T28 : struct, ICudaAsmEmitParameter
            where T29 : struct, ICudaAsmEmitParameter
            where T30 : struct, ICudaAsmEmitParameter
            where T31 : struct, ICudaAsmEmitParameter
            where T32 : struct, ICudaAsmEmitParameter
            where T33 : struct, ICudaAsmEmitParameter
            where T34 : struct, ICudaAsmEmitParameter
            where T35 : struct, ICudaAsmEmitParameter
            where T36 : struct, ICudaAsmEmitParameter
            where T37 : struct, ICudaAsmEmitParameter
            where T38 : struct, ICudaAsmEmitParameter
            where T39 : struct, ICudaAsmEmitParameter
            where T40 : struct, ICudaAsmEmitParameter
            where T41 : struct, ICudaAsmEmitParameter
            where T42 : struct, ICudaAsmEmitParameter
            where T43 : struct, ICudaAsmEmitParameter =>
            throw new NotImplementedException();

    }
}