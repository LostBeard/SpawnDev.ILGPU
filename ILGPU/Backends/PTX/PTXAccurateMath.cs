// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXAccurateMath.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Runtime.Cuda;
using System;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.PTX
{
    /// <summary>
    /// Single-precision math for the PTX backend when the CUDA toolkit's LibDevice is not
    /// loaded (PTX only: the functions emit PTX instructions).
    /// </summary>
    /// <remarks>
    /// With LibDevice loaded, <see cref="MathF.Exp(float)"/> is LibDevice's
    /// <c>__nv_expf</c> (measured on an RTX 4070 over every float input: 1.93 ulp at most).
    /// Without it, ILGPU.Algorithms computed <c>ex2.approx(x / ln 2)</c>, whose product
    /// rounds before the exponential, so the error grew with |x| to 63.8 ulp. Most machines
    /// that run CUDA kernels have the driver but not the toolkit.
    /// </remarks>
    public static class PTXAccurateMath
    {
        /// <summary>
        /// e raised to <paramref name="x"/>: within 2.07 ulp for every float input (RTX 4070,
        /// exhaustive), with overflow to +inf, underflow through the subnormals to 0, and
        /// NaN, +inf, -inf exact. PTX only.
        /// </summary>
        /// <param name="x">The exponent.</param>
        /// <returns>e^x.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Exp(float x)
        {
            // n = x / ln 2 rounded to the nearest integer; f = x - n ln 2 in two fused
            // steps (ln2Hi has 16 significant bits, so n ln2Hi is exact); 2^(f / ln 2) with
            // |f / ln 2| <= 0.5 comes from the hardware ex2, accurate on that range
            CudaAsm.Emit("cvt.rni.f32.f32 %0, %1;", out float n, x * 1.44269502f);
            float f = MathF.FusedMultiplyAdd(n, -0.693145752f, x);
            f = MathF.FusedMultiplyAdd(n, -1.42860677e-6f, f);
            CudaAsm.Emit("ex2.approx.ftz.f32 %0, %1;", out float e, f * 1.44269502f);

            // e * 2^n by adding n to the exponent field: e is in [0.707, 1.415] and n in
            // [-126, 126] here, so the result is a normal float
            if (MathF.Abs(x) <= 87.3f)
                return Interop.IntAsFloat(Interop.FloatAsInt(e) + ((uint)(int)n << 23));
            return ExpOutsideNormalRange(x, n, e);
        }

        /// <summary>
        /// The rare part of <see cref="Exp(float)"/>: NaN, the infinities, overflow and
        /// results in the subnormal range (two exact power-of-two scalings, so the result
        /// rounds once).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float ExpOutsideNormalRange(float x, float n, float e)
        {
            if (float.IsNaN(x))
                return x;
            if (x > 88.7228394f)
                return float.PositiveInfinity;
            if (x < -103.972084f)
                return 0f;
            int j = (int)n;
            int j1 = j >> 1;
            int j2 = j - j1;
            return e
                * Interop.IntAsFloat((uint)((j1 + 127) << 23))
                * Interop.IntAsFloat((uint)((j2 + 127) << 23));
        }
    }
}
