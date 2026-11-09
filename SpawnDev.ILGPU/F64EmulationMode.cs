namespace SpawnDev.ILGPU
{
    /// <summary>
    /// How a backend without native 64-bit floats (WebGPU, WebGL) represents <c>double</c>.
    /// </summary>
    public enum F64EmulationMode
    {
        /// <summary>
        /// Dekker double-float technique using two f32 values.
        /// ~48-53 bits of mantissa, 8 bytes per value. Good balance of precision and performance.
        /// </summary>
        Dekker,

        /// <summary>
        /// Quad-float (Hida, Li and Bailey's QD library) using four f32 values, 16 bytes per value: about 96 bits of
        /// significand between operations. A value is rounded to binary64 only when it is stored to memory or converted,
        /// so a chain of operations kept in registers (<c>r -= a * b</c>) does NOT give .NET's result, where every
        /// operation rounds to binary64. Use <see cref="Ieee"/> for results bit-identical to .NET.
        /// </summary>
        Ozaki,

        /// <summary>
        /// No emulation — double is promoted to float (f32). Maximum performance, loses precision.
        /// </summary>
        Disabled,

        /// <summary>
        /// IEEE-754 binary64 in integer arithmetic on the bit pattern (8 bytes per value): every operation rounds to
        /// nearest even at 53 bits with subnormals, exactly like .NET's <c>double</c>. Addition, subtraction,
        /// multiplication, division, Sqrt, <c>%</c>, Math.IEEERemainder, Math.FusedMultiplyAdd (one rounding), Floor /
        /// Ceiling / Truncate / Round, conversions and comparisons are bit-identical to .NET. Transcendental functions
        /// (Sin, Exp, Log, Pow, ...) are evaluated in f32, as in the other modes. The slowest mode: use it where results
        /// must match the CPU / CUDA / OpenCL bit for bit (bit-exact codec ports, reproducible encoders).
        /// </summary>
        Ieee,
    }
}
