using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Provides WebGL-specific mathematical intrinsic implementations.
    /// These methods are used as fallbacks during ILGPU-to-GLSL transpilation
    /// when the standard math operations require special handling.
    /// </summary>
    public static class WebGLIntrinsics
    {
        // Unary float operations

        /// <summary>Returns the absolute value of a float.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Abs(float val) => val < 0 ? -val : val;

        /// <summary>Returns the sign of a float (-1, 0, or 1).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Sign(float val) => val > 0 ? 1 : val < 0 ? -1 : 0;

        /// <summary>Rounds a float to the nearest integer value.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Round(float val) => MathF.Round(val, MidpointRounding.AwayFromZero);

        /// <summary>Truncates a float toward zero.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Truncate(float val) => MathF.Truncate(val);

        // Binary float operations

        /// <summary>Returns the angle whose tangent is the quotient of y and x.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);

        /// <summary>Returns the larger of two float values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Max(float val1, float val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the smaller of two float values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Min(float val1, float val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns x raised to the power of y.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Pow(float x, float y) => MathF.Pow(x, y);

        // Ternary float operations

        /// <summary>Clamps a float value to the specified range.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

        /// <summary>Computes (x * y) + z with a single rounding.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float FusedMultiplyAdd(float x, float y, float z) => x * y + z;

        // Unary int operations

        /// <summary>Returns the absolute value of an int.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Abs(int val) => val < 0 ? -val : val;

        /// <summary>Returns the sign of an int (-1, 0, or 1).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Sign(int val) => val > 0 ? 1 : val < 0 ? -1 : 0;

        // Binary int operations

        /// <summary>Returns the larger of two int values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Max(int val1, int val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the smaller of two int values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Min(int val1, int val2) => val1 < val2 ? val1 : val2;

        /// <summary>Clamps an int value to the specified range.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        // Rsqrt and Rcp for XMath compatibility

        /// <summary>Returns the reciprocal square root (1/sqrt(x)) of a float.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Rsqrt(float val) => 1.0f / MathF.Sqrt(val);

        /// <summary>Returns the reciprocal square root (1/sqrt(x)) of a double.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Rsqrt(double val) => 1.0 / Math.Sqrt(val);

        /// <summary>Returns the reciprocal (1/x) of a float.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Rcp(float val) => 1.0f / val;

        /// <summary>Returns the reciprocal (1/x) of a double.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Rcp(double val) => 1.0 / val;

        // Additional integer types for IntrinsicMath compatibility

        /// <summary>Returns the absolute value of an sbyte.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static sbyte Abs(sbyte val) => val < 0 ? (sbyte)(-val) : val;

        /// <summary>Returns the absolute value of a short.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static short Abs(short val) => val < 0 ? (short)(-val) : val;

        /// <summary>Returns the absolute value of a long.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static long Abs(long val) => val < 0 ? -val : val;

        /// <summary>Returns the absolute value of a double.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Abs(double val) => val < 0 ? -val : val;

        /// <summary>Returns the smaller of two sbyte values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static sbyte Min(sbyte val1, sbyte val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the smaller of two short values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static short Min(short val1, short val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the smaller of two long values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static long Min(long val1, long val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the smaller of two double values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Min(double val1, double val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the larger of two sbyte values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static sbyte Max(sbyte val1, sbyte val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the larger of two short values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static short Max(short val1, short val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the larger of two long values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static long Max(long val1, long val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the larger of two double values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Max(double val1, double val2) => val1 > val2 ? val1 : val2;

        // --- Missing unsigned sub-word Min/Max ---

        /// <summary>Returns the smaller of two byte values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static byte Min(byte val1, byte val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the smaller of two ushort values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ushort Min(ushort val1, ushort val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the larger of two byte values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static byte Max(byte val1, byte val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the larger of two ushort values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ushort Max(ushort val1, ushort val2) => val1 > val2 ? val1 : val2;

        // --- Clamp for all sub-word + standard types ---

        /// <summary>Clamps an sbyte value to the inclusive range [min, max].</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static sbyte Clamp(sbyte value, sbyte min, sbyte max) => value < min ? min : value > max ? max : value;

        /// <summary>Clamps a short value to the inclusive range [min, max].</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static short Clamp(short value, short min, short max) => value < min ? min : value > max ? max : value;

        /// <summary>Clamps a byte value to the inclusive range [min, max].</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static byte Clamp(byte value, byte min, byte max) => value < min ? min : value > max ? max : value;

        /// <summary>Clamps a ushort value to the inclusive range [min, max].</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ushort Clamp(ushort value, ushort min, ushort max) => value < min ? min : value > max ? max : value;

    }
}
