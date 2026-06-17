// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortExtensions.Float8E4M3.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using System.Runtime.CompilerServices;

namespace ILGPU.Algorithms
{
    // WebGL Float8E4M3-key radix sort. FP8 is a 1-byte sub-word key; the WebGL
    // render-to-texture scatter writes WHOLE 32-bit texels and cannot move a sub-texel
    // value, so - exactly like Half and BFloat16 (RadixSortExtensions.cs /
    // RadixSortExtensions.BFloat16.cs) - FP8 sorts via an UNPACKED f32 working
    // representation: copy-in widens each Float8E4M3 to f32 (lossless: every FP8 value is a
    // strict subset of f32), the radix bit is derived by narrowing back to Float8E4M3 and
    // calling the canonical ExtractRadixBits, and copy-out narrows the sorted f32 back to
    // Float8E4M3 (exact round-trip for any value that began as a Float8E4M3). Mirrors the
    // BFloat16 path one-for-one.
    static partial class RadixSortExtensions
    {
        private static void WebGLScatterRadixCopyInFloat8E4M3<TStride>(
            Index1D index, ArrayView1D<Float8E4M3, TStride> input,
            ArrayView1D<float, Stride1D.Dense> output)
            where TStride : struct, IStride1D =>
            output[index.X] = (float)input[index.X];

        private static void WebGLScatterRadixCopyOutFloat8E4M3<TStride>(
            Index1D index, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<Float8E4M3, TStride> output)
            where TStride : struct, IStride1D =>
            output[index.X] = (Float8E4M3)input[index.X];

        private static void WebGLScatterRadixExtractBitFloat8E4M3<TRadixSortOperation>(
            Index1D index, ArrayView1D<float, Stride1D.Dense> keys,
            ArrayView1D<int, Stride1D.Dense> flags, int bit)
            where TRadixSortOperation : struct, IRadixSortOperation<Float8E4M3>
        {
            TRadixSortOperation op = default;
            flags[index.X] = op.ExtractRadixBits((Float8E4M3)keys[index.X], bit, 1);
        }

        // Keys-only Float8E4M3 sort. Invoked by reflection from CreateRadixSort (the outer
        // method is generic on T; the compiler can't see T == Float8E4M3 to bind the
        // IRadixSortOperation<Float8E4M3> constraint statically). Called once per handler.
        private static RadixSort<Float8E4M3, TStride> CreateWebGLScatterRadixSortFloat8E4M3<
            TStride, TRadixSortOperation>(Accelerator accelerator, IScatterProvider scatter)
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<Float8E4M3>
        {
            var copyIn = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<Float8E4M3, TStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInFloat8E4M3<TStride>);
            var copyOut = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<Float8E4M3, TStride>>(
                WebGLScatterRadixCopyOutFloat8E4M3<TStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBitFloat8E4M3<TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits; // 8

            return (stream, view, temp) =>
            {
                int n = (int)view.Length;
                if (n <= 1)
                    return;

                using var keysA = accelerator.Allocate1D<float>(n);
                using var keysB = accelerator.Allocate1D<float>(n);
                using var flags = accelerator.Allocate1D<int>(n);
                using var onePrefix = accelerator.Allocate1D<int>(n);
                using var dest = accelerator.Allocate1D<int>(n);
                using var scanTemp = accelerator.Allocate1D<int>(1);

                copyIn(stream, n, view, keysA.View);

                var src = keysA;
                var dst = keysB;
                for (int bit = 0; bit < numBits; bit++)
                {
                    extractBit(stream, n, src.View, flags.View, bit);
                    exclusiveScan(stream, flags.View, onePrefix.View, scanTemp.View);
                    computeDest(stream, n, flags.View, onePrefix.View, dest.View, n);
                    scatter.Scatter(dst.View, src.View, dest.View, n, "float");
                    var tmp = src; src = dst; dst = tmp;
                }

                copyOut(stream, n, src.View, view);
            };
        }


        // Float8E4M3-KEY pairs sort (FP8 key + any 4/8-byte non-FP8 value). Keys use the
        // unpacked f32 working representation; values use the same int/float/uint scatter
        // program as the generic pairs path. Invoked by reflection from CreateRadixSortPairs.
        private static RadixSortPairs<Float8E4M3, TKeyStride, TValue, TValueStride>
            CreateWebGLScatterRadixSortPairsFloat8E4M3Key<
                TKeyStride, TValue, TValueStride, TRadixSortOperation>(
            Accelerator accelerator, IScatterProvider scatter)
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<Float8E4M3>
        {
            var copyInKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<Float8E4M3, TKeyStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInFloat8E4M3<TKeyStride>);
            var copyOutKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<Float8E4M3, TKeyStride>>(
                WebGLScatterRadixCopyOutFloat8E4M3<TKeyStride>);
            var copyInVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, TValueStride>, ArrayView1D<TValue, Stride1D.Dense>>(
                WebGLScatterRadixCopyIn<TValue, TValueStride>);
            var copyOutVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, Stride1D.Dense>, ArrayView1D<TValue, TValueStride>>(
                WebGLScatterRadixCopyOut<TValue, TValueStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBitFloat8E4M3<TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits; // 8
            string valType = WebGLScatterValueType<TValue>();
            int valCpe = WebGLScatterCpe<TValue>();

            return (stream, keys, values, tempView) =>
            {
                int n = (int)keys.Length;
                if (n <= 1)
                    return;

                using var keysA = accelerator.Allocate1D<float>(n);
                using var keysB = accelerator.Allocate1D<float>(n);
                using var valsA = accelerator.Allocate1D<TValue>(n);
                using var valsB = accelerator.Allocate1D<TValue>(n);
                using var flags = accelerator.Allocate1D<int>(n);
                using var onePrefix = accelerator.Allocate1D<int>(n);
                using var dest = accelerator.Allocate1D<int>(n);
                using var scanTemp = accelerator.Allocate1D<int>(1);

                copyInKeys(stream, n, keys, keysA.View);
                copyInVals(stream, n, values, valsA.View);

                var kSrc = keysA;
                var kDst = keysB;
                var vSrc = valsA;
                var vDst = valsB;
                for (int bit = 0; bit < numBits; bit++)
                {
                    extractBit(stream, n, kSrc.View, flags.View, bit);
                    exclusiveScan(stream, flags.View, onePrefix.View, scanTemp.View);
                    computeDest(stream, n, flags.View, onePrefix.View, dest.View, n);
                    scatter.Scatter(kDst.View, kSrc.View, dest.View, n, "float", 1);
                    scatter.Scatter(vDst.View, vSrc.View, dest.View, n, valType, valCpe);
                    var kt = kSrc; kSrc = kDst; kDst = kt;
                    var vt = vSrc; vSrc = vDst; vDst = vt;
                }

                copyOutKeys(stream, n, kSrc.View, keys);
                copyOutVals(stream, n, vSrc.View, values);
            };
        }
    }
}
