// ---------------------------------------------------------------------------------------
//                                    ILGPU
//                        Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: FloatRemainder.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

namespace ILGPU
{
    /// <summary>
    /// The EXACT truncated floating-point remainder C# defines for <c>x % y</c> (IEEE fmod: sign of the
    /// dividend, |result| &lt; |y|, no rounding at all - the true remainder is always representable), written
    /// with integer operations only so every backend can compile it.
    /// </summary>
    /// <remarks>
    /// Backends without an exact native fmod used <c>x - y * trunc(x / y)</c> or
    /// <c>frac(|x * rcp(y)|) * |y|</c>: -7f % 3f came out -1.0000005 on CUDA without libdevice,
    /// 1e30f % 3.3f came out 0, and 0.3f % 0.1f came out 0 (WebGPU, WebGL, Wasm). Found through
    /// SpawnDev.ILGPU.ML's ONNX Mod (2026-10-01). This is the shift-subtract long division of the
    /// significands (musl's fmod), restructured to a single exit with no early returns inside the loops:
    /// the result is the bit-exact C# value for every input, including subnormals, signed zeros, NaN and
    /// infinities. The loop runs at most (exponent difference + 1) times.
    /// </remarks>
    public static class FloatRemainder
    {
        /// <summary>The exact <c>x % y</c> for <see cref="float"/>.</summary>
        public static float Rem(float x, float y)
        {
            uint ux = Interop.FloatAsInt(x);
            uint uy = Interop.FloatAsInt(y);
            int ex = (int)((ux >> 23) & 0xFFu);
            int ey = (int)((uy >> 23) & 0xFFu);
            uint sx = ux & 0x80000000u;
            uint ax = ux & 0x7FFFFFFFu;
            uint ay = uy & 0x7FFFFFFFu;

            uint resultBits;
            if (ay == 0u || ay > 0x7F800000u || ex == 0xFF)
            {
                // y = +-0, y = NaN, or x = +-Inf/NaN: NaN.
                resultBits = 0x7FC00000u;
            }
            else if (ax < ay)
            {
                // |x| < |y| (also covers y = +-Inf with finite x): x itself.
                resultBits = ux;
            }
            else if (ax == ay)
            {
                resultBits = sx;   // +-0 with the sign of x
            }
            else
            {
                // Significands with the implicit bit; subnormals normalized by shifting.
                uint mx, my;
                if (ex == 0)
                {
                    uint i = ux << 9;
                    while ((int)i >= 0) { ex--; i <<= 1; }
                    mx = ux << (1 - ex);
                }
                else
                {
                    mx = (ux & 0x007FFFFFu) | 0x00800000u;
                }
                if (ey == 0)
                {
                    uint i = uy << 9;
                    while ((int)i >= 0) { ey--; i <<= 1; }
                    my = uy << (1 - ey);
                }
                else
                {
                    my = (uy & 0x007FFFFFu) | 0x00800000u;
                }

                // Long division: mx < 2*my holds throughout.
                while (ex > ey)
                {
                    uint d = mx - my;
                    if ((int)d >= 0) mx = d;
                    mx <<= 1;
                    ex--;
                }
                uint last = mx - my;
                if ((int)last >= 0) mx = last;

                if (mx == 0u)
                {
                    resultBits = sx;
                }
                else
                {
                    while ((mx >> 23) == 0u) { mx <<= 1; ex--; }
                    if (ex > 0)
                        resultBits = ((mx - 0x00800000u) | ((uint)ex << 23)) | sx;
                    else
                        resultBits = (mx >> (1 - ex)) | sx;
                }
            }
            return Interop.IntAsFloat(resultBits);
        }

        /// <summary>The exact <c>x % y</c> for <see cref="double"/> (native-f64 backends).</summary>
        public static double Rem(double x, double y)
        {
            ulong ux = Interop.FloatAsInt(x);
            ulong uy = Interop.FloatAsInt(y);
            int ex = (int)((ux >> 52) & 0x7FFul);
            int ey = (int)((uy >> 52) & 0x7FFul);
            ulong sx = ux & 0x8000000000000000ul;
            ulong ax = ux & 0x7FFFFFFFFFFFFFFFul;
            ulong ay = uy & 0x7FFFFFFFFFFFFFFFul;

            ulong resultBits;
            if (ay == 0ul || ay > 0x7FF0000000000000ul || ex == 0x7FF)
            {
                resultBits = 0x7FF8000000000000ul;
            }
            else if (ax < ay)
            {
                resultBits = ux;
            }
            else if (ax == ay)
            {
                resultBits = sx;
            }
            else
            {
                ulong mx, my;
                if (ex == 0)
                {
                    ulong i = ux << 12;
                    while ((long)i >= 0) { ex--; i <<= 1; }
                    mx = ux << (1 - ex);
                }
                else
                {
                    mx = (ux & 0x000FFFFFFFFFFFFFul) | 0x0010000000000000ul;
                }
                if (ey == 0)
                {
                    ulong i = uy << 12;
                    while ((long)i >= 0) { ey--; i <<= 1; }
                    my = uy << (1 - ey);
                }
                else
                {
                    my = (uy & 0x000FFFFFFFFFFFFFul) | 0x0010000000000000ul;
                }

                while (ex > ey)
                {
                    ulong d = mx - my;
                    if ((long)d >= 0) mx = d;
                    mx <<= 1;
                    ex--;
                }
                ulong last = mx - my;
                if ((long)last >= 0) mx = last;

                if (mx == 0ul)
                {
                    resultBits = sx;
                }
                else
                {
                    while ((mx >> 52) == 0ul) { mx <<= 1; ex--; }
                    if (ex > 0)
                        resultBits = ((mx - 0x0010000000000000ul) | ((ulong)ex << 52)) | sx;
                    else
                        resultBits = (mx >> (1 - ex)) | sx;
                }
            }
            return Interop.IntAsFloat(resultBits);
        }
    }
}
