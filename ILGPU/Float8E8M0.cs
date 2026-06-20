// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: Float8E8M0.cs
//
// The OCP "E8M0" 8-bit type: 8 exponent bits, NO sign, NO mantissa, bias 127. It is the
// shared SCALE format for every OCP microscaling block (MXFP4, MXFP8, MXINT8, NVFP4) - a
// pure power-of-two scale 2^(e-127). It completes the OCP Float8 family alongside the two
// element formats Float8E4M3 and Float8E5M2.
//
// Semantics (ml_dtypes float8_e8m0fnu): stored byte e in 0..254 decodes to 2^(e-127);
// e == 0xFF is the ONLY special and decodes to NaN. There is no sign, no zero, no Inf - the
// smallest value is 2^-127 (e == 0). Because E8M0 and IEEE-754 binary32 share exponent bias
// 127, the decode is exactly the f32 whose biased exponent field is e and whose mantissa is
// zero, i.e. bit-pattern (e << 23) - except e == 0 (which would give +0, not 2^-127) and
// e == 0xFF (which would give +Inf, not NaN). RawBitsToFloat handles both.
//
// Unlike Float8E4M3/E5M2 this is NOT a kernel-arithmetic value type (you never add two scales
// in a kernel - you decode a scale to f32 and multiply the dequantized elements by it), so it
// is intentionally minimal: a host struct + the kernel-safe RawBitsToFloat decode. Decode an
// MX block's scale byte in-register with Float8E8M0Extensions.RawBitsToFloat - no struct ctor,
// transpiles on every backend, exactly like RawBitsToFloat for FP4/FP8/bf16.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    /// <summary>
    /// The OCP E8M0 8-bit scale type (8 exponent bits, no sign/mantissa, bias 127): a pure
    /// power-of-two scale 2^(e-127), the shared scale format for OCP microscaling blocks
    /// (MXFP4/MXFP8/MXINT8/NVFP4). e == 0xFF is NaN (the only special); smallest is 2^-127.
    /// Bit-exact to <c>ml_dtypes.float8_e8m0fnu</c>.
    /// </summary>
    [Serializable]
    public readonly struct Float8E8M0 : IEquatable<Float8E8M0>, IComparable<Float8E8M0>
    {
        /// <summary>The NaN value (the only special; raw byte 0xFF).</summary>
        public static readonly Float8E8M0 NaN = new Float8E8M0((byte)0xFF);

        private Float8E8M0(byte rawValue) => RawValue = rawValue;

        /// <summary>
        /// Builds a value from its raw 8-bit code (the exponent byte). HOST-side factory for
        /// round-tripping packed scale storage; to decode raw scale bits INSIDE a kernel use
        /// <see cref="Float8E8M0Extensions.RawBitsToFloat(int)"/> (pure bit-math, transpiles).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Float8E8M0 FromRawBits(byte rawBits) => new Float8E8M0(rawBits);

        /// <summary>The raw 8-bit exponent code. Round-trips with <see cref="FromRawBits"/>.</summary>
        public byte RawValue { get; }

        /// <summary>Round a positive f32 scale to its nearest E8M0 power-of-two code.</summary>
        /// <remarks>
        /// Round-to-nearest-even on the exponent (a log2 quantization). NaN, zero, negatives and
        /// infinities map to 0xFF (NaN) per the <c>fnu</c> convention. Host-side: scales normally
        /// arrive pre-quantized in the model file, so decode is the hot path - this is for parity.
        /// </remarks>
        public static Float8E8M0 FromSingle(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                return NaN;
            uint bits = BitConverter.SingleToUInt32Bits(value);
            uint exp = (bits >> 23) & 0xFFu;
            uint mant = bits & 0x7FFFFFu;
            if (exp == 0u)
                return new Float8E8M0(0); // subnormal -> the smallest representable scale 2^-127
            // Geometric midpoint between 2^k and 2^(k+1) is sqrt(2)*2^k: round up when the f32
            // mantissa fraction reaches sqrt(2)-1 (= 0.41421356) of the way, i.e. mant >= round((sqrt(2)-1)*2^23).
            uint e = (mant > 3474675u) ? exp + 1u : exp;
            if (e > 254u) e = 254u;
            return new Float8E8M0((byte)e);
        }

        /// <summary>Decodes this scale to f32 (2^(e-127), or NaN for e == 0xFF).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToSingle() => Float8E8M0Extensions.RawBitsToFloat(RawValue);

        /// <summary>True if this value is the E8M0 NaN (raw byte 0xFF).</summary>
        public bool IsNaNValue => RawValue == 0xFF;

        /// <summary>Decodes an E8M0 scale to f32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator float(Float8E8M0 value) => value.ToSingle();

        /// <summary>Encodes an f32 scale to the nearest E8M0 code.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static explicit operator Float8E8M0(float value) => FromSingle(value);

        /// <inheritdoc/>
        public bool Equals(Float8E8M0 other) => RawValue == other.RawValue;

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Float8E8M0 other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => RawValue;

        /// <inheritdoc/>
        public int CompareTo(Float8E8M0 other) => ((float)this).CompareTo((float)other);

        /// <inheritdoc/>
        public override string ToString() => IsNaNValue ? "NaN" : ((float)this).ToString();
    }

    /// <summary>
    /// Kernel-safe and host helpers for <see cref="Float8E8M0"/>.
    /// </summary>
    public static class Float8E8M0Extensions
    {
        /// <summary>
        /// Decodes a raw E8M0 byte (the exponent code, low 8 bits of <paramref name="rawBits"/>) to
        /// its f32 value: 2^(e-127), or NaN for e == 0xFF. Pure integer/bit-math (no struct ctor), so
        /// it transpiles on every backend - decode a packed MX block's scale byte IN-KERNEL while the
        /// block stays packed, exactly like the FP4/FP8/bf16 <c>RawBitsToFloat</c> decoders.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float RawBitsToFloat(int rawBits)
        {
            // SINGLE-EXIT / branchless on purpose. Early returns inline as control flow, and the WebGL/GLSL
            // structurizer DUPLICATES a following loop's continuation into every exit arm of an inlined
            // multi-exit function - so a multi-exit decode placed before a loop (e.g. an MX block scale
            // decoded before the FP4 nibble loop, which itself has a multi-exit decode) explodes the GLSL
            // combinatorially and blows WebGL's shader-compile limits. Selects are expressions, not branches,
            // so there is nothing to duplicate. Value-identical to the old early-return form.
            uint e = (uint)(rawBits & 0xFF);
            // e in 1..254: E8M0 and f32 share bias 127, so 2^(e-127) is the normal f32 with biased exp e,
            // zero mantissa. e == 0 -> 2^-127 (f32 subnormal 0x00400000). e == 0xFF -> NaN (e<<23 = +Inf).
            uint bits = e << 23;
            bits = (e == 0u) ? 0x00400000u : bits;
            bits = (e == 0xFFu) ? 0x7FC00000u : bits;
            return Interop.IntAsFloat(bits);
        }

        /// <summary>Decodes a <see cref="Float8E8M0"/> scale to f32.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ConvertFloat8E8M0ToFloat(Float8E8M0 value) => RawBitsToFloat(value.RawValue);
    }
}
