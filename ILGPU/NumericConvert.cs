// ---------------------------------------------------------------------------------------
//                                        ILGPU
//
// File: NumericConvert.cs
//
// Transpilable generic numeric conversions for GPU kernels.
//
// C# 11 generic-math converts (float.CreateTruncating<T>(x), float.CreateChecked<T>(x), ...) inspect
// typeof(T) internally to dispatch, which the ILGPU frontend cannot lower (System.Type is not a kernel
// type) — they throw NotSupportedException("Class type 'System.Type' is not supported") on ALL backends
// (incl. the float specialization, CPU + CUDA included; it is a frontend/IR rejection, not a transpiler
// quirk). That blocks a generic kernel (e.g. MatMul<TW> where TW : INumber<TW>, TW = float | Half) from
// widening its generic weight to float for fp32 accumulation.
//
// These helpers are frontend convert-intrinsics ([ConvertIntrinisc]): the frontend intercepts each call
// and emits a ConvertValue(arg, Float32/Float64) IR node for the CONCRETE T at the call site — exactly
// the per-type cast ILGPU already lowers ((float)Half via the existing [ConvertIntrinisc] on Half's
// operator, identity for float, (float)int, ...). No typeof, so it transpiles on all 6 backends, and a
// generic K<Half> monomorphizes to the same shader/machine code as a hand-written half kernel. The body
// is only the host fallback (the intrinsic intercepts before the body is ever transpiled).
// ---------------------------------------------------------------------------------------

using ILGPU.Frontend.Intrinsic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace ILGPU
{
    /// <summary>
    /// Generic numeric conversions that transpile inside GPU kernels (unlike the C# generic-math
    /// converts, which inspect <see cref="System.Type"/> and fail to lower). Each call monomorphizes to
    /// the concrete per-type GPU convert.
    /// </summary>
    public static class NumericConvert
    {
        /// <summary>
        /// Converts a generic numeric value to <see cref="float"/>. Transpilable in kernels: the call is
        /// lowered to the concrete <c>(float)T</c> convert for the instantiated <typeparamref name="T"/>
        /// (e.g. <c>(float)Half</c>, identity for <c>float</c>, <c>(float)int</c>).
        /// </summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToFloat32<T>(T value)
            where T : INumberBase<T>
            => float.CreateTruncating(value);

        /// <summary>
        /// Converts a generic numeric value to <see cref="double"/>. Transpilable in kernels: the call is
        /// lowered to the concrete <c>(double)T</c> convert for the instantiated <typeparamref name="T"/>.
        /// </summary>
        [ConvertIntrinisc]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToFloat64<T>(T value)
            where T : INumberBase<T>
            => double.CreateTruncating(value);
    }
}
