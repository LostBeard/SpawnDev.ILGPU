// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    Copyright (c) 2024-2026 SpawnDev / LostBeard
//
// File: WasmIntrinsics.cs
//
// Provides throw-free wrapper stubs for .NET Math methods that contain
// argument-validation throws. ILGPU inlines Math.Round/Truncate/Sign/Clamp
// bodies which emit Throw instructions the Wasm transpiler cannot handle.
// These wrappers are registered via RegisterRedirect so the compiler uses
// them instead of the throwing .NET implementations.
// ---------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// Wasm-specific math intrinsic wrappers.
    /// All methods are marked NoInlining so ILGPU treats them as opaque call targets
    /// whose bodies are transpiled by the Wasm code generator (no throw instructions).
    /// </summary>
    public static class WasmIntrinsics
    {
        // ── Unary float ──

        /// <summary>Returns the absolute value of a float.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Abs(float val) => val < 0 ? -val : val;

        /// <summary>Returns the sign of a float (-1, 0, or 1).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Sign(float val) => val > 0 ? 1 : val < 0 ? -1 : 0;

        /// <summary>Rounds a float to the nearest integer value.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        // Kernel calls to this wrapper are emitted as the native f32.nearest (round half to even)
        // by WasmCodeGenerator (IsNativeMathWrapper) - this body only runs on the host. It was
        // Floor(val + 0.5), which rounds halves UP (2.5 -> 3) instead of to even like MathF.Round.
        public static float Round(float val) => MathF.Round(val);

        /// <summary>Truncates a float toward zero.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Truncate(float val) => MathF.Truncate(val);

        // ── Binary float ──

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

        // ── Ternary float ──

        /// <summary>Clamps a float value to the specified range.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

        /// <summary>Computes (x * y) + z with a single rounding.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static float FusedMultiplyAdd(float x, float y, float z) => x * y + z;

        // ── Unary int ──

        /// <summary>Returns the absolute value of an int.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Abs(int val) => val < 0 ? -val : val;

        /// <summary>Returns the sign of an int (-1, 0, or 1).</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Sign(int val) => val > 0 ? 1 : val < 0 ? -1 : 0;

        // ── Binary int ──

        /// <summary>Returns the larger of two int values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Max(int val1, int val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the smaller of two int values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Min(int val1, int val2) => val1 < val2 ? val1 : val2;

        /// <summary>Clamps an int value to the specified range.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        // ── XMath compatibility ──

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

        // ── Additional integer types for IntrinsicMath compatibility ──

        /// <summary>Returns the absolute value of a long.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static long Abs(long val) => val < 0 ? -val : val;

        /// <summary>Returns the absolute value of a double.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Abs(double val) => val < 0 ? -val : val;

        /// <summary>Returns the smaller of two long values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static long Min(long val1, long val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the smaller of two double values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Min(double val1, double val2) => val1 < val2 ? val1 : val2;

        /// <summary>Returns the larger of two long values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static long Max(long val1, long val2) => val1 > val2 ? val1 : val2;

        /// <summary>Returns the larger of two double values.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static double Max(double val1, double val2) => val1 > val2 ? val1 : val2;
    }
}
