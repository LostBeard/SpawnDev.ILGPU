// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: PrecisionConvert.cs
//
// Transpilable GENERIC float<->T value conversion for kernels written against
// `where T : INumber<T>` (Half / BFloat16 / Float8E4M3 / Float8E5M2 / float / ...).
//
// The problem it solves: inside a generic-math kernel there is no C# way to write `(float)tValue`
// or `(T)floatValue` (no cast constraint exists), so callers reach for `float.CreateChecked(t)` /
// `T.CreateChecked(f)`. Those lower to System.Numerics' generic range/identity checks which touch
// `System.Type` - the kernel transpiler rejects that ("Class type 'System.Type' is not supported")
// on every GPU backend (CUDA/OpenCL/WebGPU/WebGL/Wasm), even though the CONCRETE `(float)Half` cast
// transpiles fine.
//
// These two methods are tagged [ConvertIntrinisc], so the frontend lowers a call to the SAME
// ConvertValue IR node the concrete cast emits - resolving T per instantiation (ConvertToSingle<T>
// -> Convert(value -> f32); ConvertFromSingle<T> -> Convert(f32 -> T)). No System.Type, transpiles
// on all 6 backends. The managed bodies are the host fallback (pure CPU-side calls outside kernels).
//
// This lets every precision-aware op (Conv / GroupNorm / SiLU / MatMul / ...) that reads a low-
// precision input, accumulates in float, and writes low-precision output be ONE generic kernel for
// float/Half/bf16/fp8 instead of N per-type variants - the Rule-4 zero-fp32-temp-buffer path.
// ---------------------------------------------------------------------------------------

using ILGPU.Frontend.Intrinsic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    /// <summary>
    /// Transpilable generic conversions between <see cref="float"/> and an
    /// <see cref="INumber{T}"/> numeric type (Half / BFloat16 / Float8E4M3 / Float8E5M2 / ...),
    /// usable inside a generic kernel where a plain <c>(float)value</c> / <c>(T)value</c> cast
    /// is not expressible. Lower to the same convert the concrete casts emit (no System.Type).
    /// </summary>
    public static class PrecisionConvert
    {
        /// <summary>
        /// Converts the given value of an <see cref="INumber{T}"/> type to a 32-bit float.
        /// Inside a kernel this lowers to a native convert (the generic equivalent of
        /// <c>(float)value</c>); the managed body is the host-side fallback.
        /// </summary>
        /// <typeparam name="T">The numeric source type.</typeparam>
        /// <param name="value">The value to convert.</param>
        /// <returns>The value as a 32-bit float.</returns>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ConvertToSingle<T>(T value)
            where T : unmanaged, INumber<T> =>
            float.CreateTruncating(value);

        /// <summary>
        /// Converts the given 32-bit float to a value of an <see cref="INumber{T}"/> type.
        /// Inside a kernel this lowers to a native convert (the generic equivalent of
        /// <c>(T)value</c>); the managed body is the host-side fallback.
        /// </summary>
        /// <typeparam name="T">The numeric target type.</typeparam>
        /// <param name="value">The float value to convert.</param>
        /// <returns>The value as <typeparamref name="T"/>.</returns>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T ConvertFromSingle<T>(float value)
            where T : unmanaged, INumber<T> =>
            T.CreateTruncating(value);
    }
}
