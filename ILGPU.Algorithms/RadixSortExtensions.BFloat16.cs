// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: RadixSortExtensions.BFloat16.cs
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
    // WebGL bf16-key radix sort. bf16 is a 2-byte sub-word key; the WebGL render-to-texture
    // scatter writes WHOLE 32-bit texels and cannot move a sub-texel value, so - exactly like
    // Half (RadixSortExtensions.cs) - bf16 sorts via an UNPACKED f32 working representation:
    // copy-in widens each bf16 to f32 (lossless: bf16 is a strict subset of f32), the radix bit
    // is derived by narrowing back to BFloat16 and calling the canonical ExtractRadixBits, and
    // copy-out narrows the sorted f32 back to bf16 (exact round-trip for any value that began as
    // a bf16). Mirrors the Half path one-for-one.
    static partial class RadixSortExtensions
    {
        private static void WebGLScatterRadixCopyInBFloat16<TStride>(
            Index1D index, ArrayView1D<BFloat16, TStride> input,
            ArrayView1D<float, Stride1D.Dense> output)
            where TStride : struct, IStride1D =>
            output[index.X] = (float)input[index.X];

        private static void WebGLScatterRadixCopyOutBFloat16<TStride>(
            Index1D index, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<BFloat16, TStride> output)
            where TStride : struct, IStride1D =>
            output[index.X] = (BFloat16)input[index.X];

        private static void WebGLScatterRadixExtractBitBFloat16<TRadixSortOperation>(
            Index1D index, ArrayView1D<float, Stride1D.Dense> keys,
            ArrayView1D<int, Stride1D.Dense> flags, int bit)
            where TRadixSortOperation : struct, IRadixSortOperation<BFloat16>
        {
            TRadixSortOperation op = default;
            flags[index.X] = op.ExtractRadixBits((BFloat16)keys[index.X], bit, 1);
        }

        // Keys-only bf16 sort. Invoked by reflection from CreateRadixSort (the outer method is
        // generic on T; the compiler can't see T == BFloat16 to bind the
        // IRadixSortOperation<BFloat16> constraint statically). Called once per handler.
        private static RadixSort<BFloat16, TStride> CreateWebGLScatterRadixSortBFloat16<
            TStride, TRadixSortOperation>(Accelerator accelerator, IScatterProvider scatter)
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<BFloat16>
        {
            var copyIn = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<BFloat16, TStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInBFloat16<TStride>);
            var copyOut = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<BFloat16, TStride>>(
                WebGLScatterRadixCopyOutBFloat16<TStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBitBFloat16<TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits; // 16

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

        // ===== WebGPU f32-widen radix sort for bf16 keys =====
        // naga/Dawn mis-compiles the bf16 DESCENDING transform inside the specialized WebGPU radix
        // kernel (drops the float ordering -> sorts as raw int16); ILGPU's bf16 codegen is proven
        // correct (the standalone ExtractRadixBits bucket-compare matches the CPU oracle on WebGPU).
        // bf16 widens to f32 LOSSLESSLY + order-preservingly, so sort an f32 working copy with the
        // (correct) f32 radix sort and narrow back. WebGPU is NOT an IScatterProvider, so unlike the
        // WebGL path this uses the generic f32 radix sort directly (no render-to-texture scatter).

        // Maps the bf16 sort operation to its f32 counterpart (the only bf16 ops are
        // Ascending/DescendingBFloat16; widening preserves order so the f32 op gives the same order).
        private static bool IsAscendingBFloat16<TRadixSortOperation>() =>
            typeof(TRadixSortOperation) == typeof(AscendingBFloat16);

        // Keys-only. Invoked by reflection from CreateRadixSort.
        private static RadixSort<BFloat16, TStride> CreateWebGPUWidenRadixSortBFloat16<
            TStride, TRadixSortOperation>(Accelerator accelerator)
            where TStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<BFloat16>
        {
            var copyIn = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<BFloat16, TStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInBFloat16<TStride>);
            var copyOut = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<BFloat16, TStride>>(
                WebGLScatterRadixCopyOutBFloat16<TStride>);
            bool ascending = IsAscendingBFloat16<TRadixSortOperation>();
            var floatSortAsc = ascending
                ? accelerator.CreateRadixSort<float, Stride1D.Dense, AscendingFloat>() : null;
            var floatSortDesc = ascending
                ? null : accelerator.CreateRadixSort<float, Stride1D.Dense, DescendingFloat>();

            // Working buffers are reused (allocate-once / re-size on length change) and captured by the
            // returned delegate - NOT disposed per-dispatch. On WebGPU the radix kernels are batched and
            // submitted on the caller's Flush/Synchronize AFTER this handler returns, so a per-dispatch
            // `using` Dispose would destroy buffers still referenced by queued commands ("buffer used in
            // submit while destroyed"). Reuse-and-grow is also the standard EnsureBuffer pattern.
            MemoryBuffer1D<float, Stride1D.Dense>? f32Buf = null;
            MemoryBuffer1D<int, Stride1D.Dense>? fTempBuf = null;

            return (stream, view, temp) =>
            {
                int n = (int)view.Length;
                if (n <= 1)
                    return;
                int fTempSize = ascending
                    ? accelerator.ComputeRadixSortTempStorageSize<float, AscendingFloat>(n)
                    : accelerator.ComputeRadixSortTempStorageSize<float, DescendingFloat>(n);
                if (f32Buf == null || f32Buf.Length != n)
                { f32Buf?.Dispose(); f32Buf = accelerator.Allocate1D<float>(n); }
                if (fTempBuf == null || fTempBuf.Length != fTempSize)
                { fTempBuf?.Dispose(); fTempBuf = accelerator.Allocate1D<int>(fTempSize); }
                copyIn(stream, n, view, f32Buf.View);
                if (ascending)
                    floatSortAsc!(stream, f32Buf.View, fTempBuf.View.AsContiguous());
                else
                    floatSortDesc!(stream, f32Buf.View, fTempBuf.View.AsContiguous());
                copyOut(stream, n, f32Buf.View, view);
            };
        }

        // Pairs (bf16 KEY + any non-bf16 value). Invoked by reflection from CreateRadixSortPairs.
        private static RadixSortPairs<BFloat16, TKeyStride, TValue, TValueStride>
            CreateWebGPUWidenRadixSortPairsBFloat16Key<
                TKeyStride, TValue, TValueStride, TRadixSortOperation>(Accelerator accelerator)
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<BFloat16>
        {
            var copyInKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<BFloat16, TKeyStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInBFloat16<TKeyStride>);
            var copyOutKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<BFloat16, TKeyStride>>(
                WebGLScatterRadixCopyOutBFloat16<TKeyStride>);
            bool ascending = IsAscendingBFloat16<TRadixSortOperation>();
            var floatPairsAsc = ascending
                ? accelerator.CreateRadixSortPairs<
                    float, Stride1D.Dense, TValue, TValueStride, AscendingFloat>() : null;
            var floatPairsDesc = ascending
                ? null : accelerator.CreateRadixSortPairs<
                    float, Stride1D.Dense, TValue, TValueStride, DescendingFloat>();

            // Reused (not per-dispatch-disposed) working buffers - see CreateWebGPUWidenRadixSortBFloat16
            // for why (WebGPU batches + submits after this handler returns).
            MemoryBuffer1D<float, Stride1D.Dense>? f32Buf = null;
            MemoryBuffer1D<int, Stride1D.Dense>? fTempBuf = null;

            return (stream, keys, values, tempView) =>
            {
                int n = (int)keys.Length;
                if (n <= 1)
                    return;
                int fTempSize = ascending
                    ? accelerator.ComputeRadixSortPairsTempStorageSize<float, TValue, AscendingFloat>(n)
                    : accelerator.ComputeRadixSortPairsTempStorageSize<float, TValue, DescendingFloat>(n);
                if (f32Buf == null || f32Buf.Length != n)
                { f32Buf?.Dispose(); f32Buf = accelerator.Allocate1D<float>(n); }
                if (fTempBuf == null || fTempBuf.Length != fTempSize)
                { fTempBuf?.Dispose(); fTempBuf = accelerator.Allocate1D<int>(fTempSize); }
                copyInKeys(stream, n, keys, f32Buf.View);
                if (ascending)
                    floatPairsAsc!(stream, f32Buf.View, values, fTempBuf.View.AsContiguous());
                else
                    floatPairsDesc!(stream, f32Buf.View, values, fTempBuf.View.AsContiguous());
                copyOutKeys(stream, n, f32Buf.View, keys);
            };
        }

        // bf16-KEY pairs sort (bf16 key + any 4/8-byte non-bf16 value). Keys use the unpacked f32
        // working representation; values use the same int/float/uint scatter program as the
        // generic pairs path. Invoked by reflection from CreateRadixSortPairs.
        private static RadixSortPairs<BFloat16, TKeyStride, TValue, TValueStride>
            CreateWebGLScatterRadixSortPairsBFloat16Key<
                TKeyStride, TValue, TValueStride, TRadixSortOperation>(
            Accelerator accelerator, IScatterProvider scatter)
            where TKeyStride : struct, IStride1D
            where TValue : unmanaged
            where TValueStride : struct, IStride1D
            where TRadixSortOperation : struct, IRadixSortOperation<BFloat16>
        {
            var copyInKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<BFloat16, TKeyStride>, ArrayView1D<float, Stride1D.Dense>>(
                WebGLScatterRadixCopyInBFloat16<TKeyStride>);
            var copyOutKeys = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<BFloat16, TKeyStride>>(
                WebGLScatterRadixCopyOutBFloat16<TKeyStride>);
            var copyInVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, TValueStride>, ArrayView1D<TValue, Stride1D.Dense>>(
                WebGLScatterRadixCopyIn<TValue, TValueStride>);
            var copyOutVals = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<TValue, Stride1D.Dense>, ArrayView1D<TValue, TValueStride>>(
                WebGLScatterRadixCopyOut<TValue, TValueStride>);
            var extractBit = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, int>(
                WebGLScatterRadixExtractBitBFloat16<TRadixSortOperation>);
            var computeDest = accelerator.LoadAutoGroupedKernel<
                Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                ArrayView1D<int, Stride1D.Dense>, int>(WebGLScatterRadixComputeDest);
            var exclusiveScan = accelerator.CreateScan<
                int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Exclusive);

            int numBits = default(TRadixSortOperation).NumBits; // 16
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
