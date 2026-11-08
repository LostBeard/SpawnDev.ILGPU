// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXIntrinsics.Generated.tt/PTXIntrinsics.Generated.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeInformation.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Intrinsics;
using ILGPU.IR.Values;
using ILGPU.Runtime.Cuda;
using System.Runtime.CompilerServices;

namespace ILGPU.Backends.PTX
{
    partial class PTXIntrinsics
    {
        #region Warp Shuffles

        /// <summary>
        /// Registers all PTX warp intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterWarpShuffles(IntrinsicImplementationManager manager)
        {
            manager.RegisterWarpShuffle(
                ShuffleKind.Generic,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterWarpShuffle(
                ShuffleKind.Generic,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterSubWarpShuffle(
                ShuffleKind.Generic,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Generic,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterWarpShuffle(
                ShuffleKind.Generic,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleHalf),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Generic,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleHalf),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterWarpShuffle(
                ShuffleKind.Down,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleDownInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterWarpShuffle(
                ShuffleKind.Down,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleDownFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterSubWarpShuffle(
                ShuffleKind.Down,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleDownInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Down,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleDownFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterWarpShuffle(
                ShuffleKind.Down,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleDownHalf),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Down,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleDownHalf),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterWarpShuffle(
                ShuffleKind.Up,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleUpInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterWarpShuffle(
                ShuffleKind.Up,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleUpFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterSubWarpShuffle(
                ShuffleKind.Up,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleUpInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Up,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleUpFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterWarpShuffle(
                ShuffleKind.Up,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleUpHalf),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Up,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleUpHalf),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterWarpShuffle(
                ShuffleKind.Xor,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleXorInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterWarpShuffle(
                ShuffleKind.Xor,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleXorFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterSubWarpShuffle(
                ShuffleKind.Xor,
                BasicValueType.Int64,
                CreateIntrinsic(
                    nameof(WarpShuffleXorInt64),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Xor,
                BasicValueType.Float64,
                CreateIntrinsic(
                    nameof(WarpShuffleXorFloat64),
                    IntrinsicImplementationMode.Redirect));

            manager.RegisterWarpShuffle(
                ShuffleKind.Xor,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleXorHalf),
                    IntrinsicImplementationMode.Redirect));
            manager.RegisterSubWarpShuffle(
                ShuffleKind.Xor,
                BasicValueType.Float16,
                CreateIntrinsic(
                    nameof(WarpShuffleXorHalf),
                    IntrinsicImplementationMode.Redirect));

        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong WarpShuffleInt64(ulong value, int idx)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.Shuffle(parts.Lower, idx);
            parts.Upper = Warp.Shuffle(parts.Upper, idx);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double WarpShuffleFloat64(double value, int idx)
        {
            var shuffled = WarpShuffleInt64(Interop.FloatAsInt(value), idx);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SubWarpShuffleInt64(ulong value, int idx, int width)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.Shuffle(parts.Lower, idx, width);
            parts.Upper = Warp.Shuffle(parts.Upper, idx, width);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SubWarpShuffleFloat64(
            double value,
            int idx,
            int width)
        {
            var shuffled = SubWarpShuffleInt64(
                Interop.FloatAsInt(value),
                idx,
                width);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// PTX has no shfl.sync.*.b16; all shuffle instructions operate on 32-bit values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half WarpShuffleHalf(Half value, int idx)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.Shuffle(widened, idx);
            return Interop.IntAsFloat((ushort)shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half SubWarpShuffleHalf(
            Half value,
            int idx,
            int width)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.Shuffle(widened, idx, width);
            return Interop.IntAsFloat((ushort)shuffled);
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong WarpShuffleDownInt64(ulong value, int idx)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.ShuffleDown(parts.Lower, idx);
            parts.Upper = Warp.ShuffleDown(parts.Upper, idx);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double WarpShuffleDownFloat64(double value, int idx)
        {
            var shuffled = WarpShuffleDownInt64(Interop.FloatAsInt(value), idx);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SubWarpShuffleDownInt64(ulong value, int idx, int width)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.ShuffleDown(parts.Lower, idx, width);
            parts.Upper = Warp.ShuffleDown(parts.Upper, idx, width);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SubWarpShuffleDownFloat64(
            double value,
            int idx,
            int width)
        {
            var shuffled = SubWarpShuffleDownInt64(
                Interop.FloatAsInt(value),
                idx,
                width);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// PTX has no shfl.sync.*.b16; all shuffle instructions operate on 32-bit values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half WarpShuffleDownHalf(Half value, int idx)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.ShuffleDown(widened, idx);
            return Interop.IntAsFloat((ushort)shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half SubWarpShuffleDownHalf(
            Half value,
            int idx,
            int width)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.ShuffleDown(widened, idx, width);
            return Interop.IntAsFloat((ushort)shuffled);
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong WarpShuffleUpInt64(ulong value, int idx)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.ShuffleUp(parts.Lower, idx);
            parts.Upper = Warp.ShuffleUp(parts.Upper, idx);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double WarpShuffleUpFloat64(double value, int idx)
        {
            var shuffled = WarpShuffleUpInt64(Interop.FloatAsInt(value), idx);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SubWarpShuffleUpInt64(ulong value, int idx, int width)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.ShuffleUp(parts.Lower, idx, width);
            parts.Upper = Warp.ShuffleUp(parts.Upper, idx, width);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SubWarpShuffleUpFloat64(
            double value,
            int idx,
            int width)
        {
            var shuffled = SubWarpShuffleUpInt64(
                Interop.FloatAsInt(value),
                idx,
                width);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// PTX has no shfl.sync.*.b16; all shuffle instructions operate on 32-bit values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half WarpShuffleUpHalf(Half value, int idx)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.ShuffleUp(widened, idx);
            return Interop.IntAsFloat((ushort)shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half SubWarpShuffleUpHalf(
            Half value,
            int idx,
            int width)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.ShuffleUp(widened, idx, width);
            return Interop.IntAsFloat((ushort)shuffled);
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong WarpShuffleXorInt64(ulong value, int idx)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.ShuffleXor(parts.Lower, idx);
            parts.Upper = Warp.ShuffleXor(parts.Upper, idx);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double WarpShuffleXorFloat64(double value, int idx)
        {
            var shuffled = WarpShuffleXorInt64(Interop.FloatAsInt(value), idx);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SubWarpShuffleXorInt64(ulong value, int idx, int width)
        {
            var parts = IntrinsicMath.Decompose(value);
            parts.Lower = Warp.ShuffleXor(parts.Lower, idx, width);
            parts.Upper = Warp.ShuffleXor(parts.Upper, idx, width);
            return parts.ToULong();
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static double SubWarpShuffleXorFloat64(
            double value,
            int idx,
            int width)
        {
            var shuffled = SubWarpShuffleXorInt64(
                Interop.FloatAsInt(value),
                idx,
                width);
            return Interop.IntAsFloat(shuffled);
        }

        /// <summary>
        /// Wraps a single warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// PTX has no shfl.sync.*.b16; all shuffle instructions operate on 32-bit values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half WarpShuffleXorHalf(Half value, int idx)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.ShuffleXor(widened, idx);
            return Interop.IntAsFloat((ushort)shuffled);
        }

        /// <summary>
        /// Wraps a single sub-warp-shuffle operation for Half (f16).
        /// Widens to uint before the b32 shuffle, then narrows back to f16.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Half SubWarpShuffleXorHalf(
            Half value,
            int idx,
            int width)
        {
            uint widened = (uint)Interop.FloatAsInt(value);
            uint shuffled = Warp.ShuffleXor(widened, idx, width);
            return Interop.IntAsFloat((ushort)shuffled);
        }


        #endregion

        #region FP16

        /// <summary>
        /// Registers all FP16 intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterFP16(IntrinsicImplementationManager manager)
        {
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Neg,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.Neg),
                    CudaArchitecture.SM_53));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Abs,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.Abs),
                    CudaArchitecture.SM_53));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.IsInfF,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.IsInfinity),
                    null));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.IsNaNF,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.IsNaN),
                    null));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Exp2F,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.Exp2FP32),
                    CudaArchitecture.SM_75));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanhF,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.TanhFP32),
                    CudaArchitecture.SM_75));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Add,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.AddFP32),
                    CudaArchitecture.SM_53));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Sub,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.SubFP32),
                    CudaArchitecture.SM_53));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Mul,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.MulFP32),
                    CudaArchitecture.SM_53));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Div,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.DivFP32),
                    null));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Min,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.MinFP32),
                    CudaArchitecture.SM_80));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Max,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.MaxFP32),
                    CudaArchitecture.SM_80));
            manager.RegisterTernaryArithmetic(
                TernaryArithmeticKind.MultiplyAdd,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.FmaFP32),
                    CudaArchitecture.SM_53));
            manager.RegisterTernaryArithmetic(
                TernaryArithmeticKind.MultiplyAddEstimate,
                BasicValueType.Float16,
                CreateFP16Intrinsic(
                    nameof(HalfExtensions.FmaFP32),
                    CudaArchitecture.SM_53));
        }

        #endregion

        #region Math

        /// <summary>
        /// Registers all Math intrinsics with the given manager.
        /// </summary>
        /// <param name="manager">The target implementation manager.</param>
        private static void RegisterMathFunctions(IntrinsicImplementationManager manager)
        {
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AcosF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Acos),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AsinF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Asin),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AtanF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Atan),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CeilingF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Ceil),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CosF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Cos),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CoshF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Cosh),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.ExpF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Exp),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Exp2F,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Exp2),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.FloorF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Floor),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.LogF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Log),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log2F,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Log2),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log10F,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Log10),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.RsqrtF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Rsqrt),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Sin),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinhF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Sinh),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SqrtF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Sqrt),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Tan),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanhF,
                BasicValueType.Float64,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Tanh),
                    typeof(double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AcosF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Acos),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AsinF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Asin),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AtanF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Atan),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CeilingF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Ceil),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CosF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Cos),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CoshF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Cosh),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.ExpF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Exp),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Exp2F,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Exp2),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.FloorF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Floor),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.LogF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Log),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log2F,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Log2),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log10F,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Log10),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.RsqrtF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Rsqrt),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Sin),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinhF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Sinh),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SqrtF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Sqrt),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Tan),
                    typeof(float)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanhF,
                BasicValueType.Float32,
                CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Tanh),
                    typeof(float)));

            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Atan2F,
                BasicValueType.Float64,
                    CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Atan),
                    typeof(double),
                    typeof(double)));
                manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.BinaryLogF,
                BasicValueType.Float64,
                    CreateMathIntrinsic(
                    typeof(IntrinsicMath.BinaryLog),
                    nameof(IntrinsicMath.BinaryLog.Log),
                    typeof(double),
                    typeof(double)));
                manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.PowF,
                BasicValueType.Float64,
                    CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Pow),
                    typeof(double),
                    typeof(double)));
                manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Rem,
                BasicValueType.Float64,
                    CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Fmod),
                    typeof(double),
                    typeof(double)));
                manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Atan2F,
                BasicValueType.Float32,
                    CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Atan),
                    typeof(float),
                    typeof(float)));
                manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.BinaryLogF,
                BasicValueType.Float32,
                    CreateMathIntrinsic(
                    typeof(IntrinsicMath.BinaryLog),
                    nameof(IntrinsicMath.BinaryLog.Log),
                    typeof(float),
                    typeof(float)));
                manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.PowF,
                BasicValueType.Float32,
                    CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Pow),
                    typeof(float),
                    typeof(float)));
                manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Rem,
                BasicValueType.Float32,
                    CreateLibDeviceMathIntrinsic(
                    nameof(LibDevice.Fmod),
                    typeof(float),
                    typeof(float)));
            }

        #endregion
    }
}