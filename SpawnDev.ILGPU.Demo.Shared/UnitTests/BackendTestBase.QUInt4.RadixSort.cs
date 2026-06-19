using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using QUInt4 = ILGPU.QUInt4;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Packed 4-bit QUInt4 (UNSIGNED 0..15) radix-sort coverage - the unsigned companion to the QInt4
    // radix tests. Exercises the QUInt4 nibble LOAD (zero-extend) + the Ascending/DescendingQUInt4 key
    // transform (NO sign-flip - unsigned is already monotonic). ExtractBits is load-only (all 6
    // backends); the keys/pairs SORT scatters into packed storage (atomic-RMW nibble store), so it is
    // gated to the packed-store backends (CPU + WebGL skip), exactly like QInt4.
    public abstract partial class BackendTestBase
    {
        // 0..15 each appears twice (32 elements): every nibble value at both byte positions.
        static int[] QUInt4UnsignedValues()
        {
            var v = new int[32];
            for (int i = 0; i < v.Length; i++) v[i] = i % 16;
            return v;
        }

        static byte[] PackQUInt4(int[] vals)
        {
            var packed = new byte[(vals.Length + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((vals[2 * k] & 0xF) | ((vals[2 * k + 1] & 0xF) << 4));
            return packed;
        }

        static void QUInt4ExtractKernel<TOp>(
            Index1D i, ArrayView<QUInt4> keys, ArrayView<int> flags, int shift, int bitMask)
            where TOp : struct, IRadixSortOperation<QUInt4>
        {
            TOp op = default;
            flags[i.X] = op.ExtractRadixBits(keys[i.X], shift, bitMask);
        }

        // ExtractRadixBits GPU-vs-CPU bucket compare for every pass - pinpoints a QUInt4-load or
        // key-transform miscompile on any backend.
        async Task QUInt4RadixExtractBitsImpl<TOp>(Accelerator accelerator, string tag)
            where TOp : struct, IRadixSortOperation<QUInt4>
        {
            var vals = QUInt4UnsignedValues();
            var packed = PackQUInt4(vals);
            int n = vals.Length;

            using var keysBuf = accelerator.Allocate1D<QUInt4>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var flagsBuf = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<QUInt4>, ArrayView<int>, int, int>(QUInt4ExtractKernel<TOp>);

            TOp op = default;
            for (int shift = 0; shift < op.NumBits; shift++)
            {
                kernel(n, keysBuf.View, flagsBuf.View, shift, 1);
                await accelerator.SynchronizeAsync();
                var gpu = await flagsBuf.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                {
                    int cpu = op.ExtractRadixBits((QUInt4)vals[i], shift, 1);
                    if (gpu[i] != cpu)
                        throw new Exception(
                            $"{tag} QUInt4 ExtractRadixBits mismatch at value={vals[i]} shift={shift}: " +
                            $"GPU={gpu[i]} CPU={cpu}");
                }
            }
        }

        // Keys-only ascending sort over the packed buffer; verifies the sorted nibbles are ascending 0..15.
        async Task QUInt4RadixKeysAscendingImpl(Accelerator accelerator)
        {
            var vals = QUInt4UnsignedValues();
            int n = vals.Length;
            var packed = PackQUInt4(vals);

            using var keysBuf = accelerator.Allocate1D<QUInt4>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortTempStorageSize<QUInt4, AscendingQUInt4>(n));
            accelerator.CreateRadixSort<QUInt4, Stride1D.Dense, AscendingQUInt4>()(
                accelerator.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());

            using var decBuf = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QUInt4>, ArrayView<int>>(QUInt4LoadKernel)(
                n, keysBuf.View, decBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await decBuf.CopyToHostAsync<int>();
            var expected = (int[])vals.Clone();
            Array.Sort(expected); // ascending 0..15
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"QUInt4 keys-asc mismatch at [{i}]: expected={expected[i]} got={got[i]}");
        }

        // Pairs (QUInt4 key + int value) ascending: DISTINCT keys 0..15 in descending input order with
        // value=index, so sorted keys must be ascending 0..15 and each value follows its key's permutation.
        async Task QUInt4RadixPairsAscendingImpl(Accelerator accelerator)
        {
            int n = 16;
            var keysInt = new int[n];
            var values = new int[n];
            for (int i = 0; i < n; i++)
            {
                keysInt[i] = 15 - i;  // descending input 15,14,...,0
                values[i] = i;
            }
            var packed = PackQUInt4(keysInt);

            using var keysBuf = accelerator.Allocate1D<QUInt4>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var valsBuf = accelerator.Allocate1D(values);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortPairsTempStorageSize<QUInt4, int, AscendingQUInt4>(n));
            accelerator.CreateRadixSortPairs<QUInt4, Stride1D.Dense, int, Stride1D.Dense, AscendingQUInt4>()(
                accelerator.DefaultStream, keysBuf.View, valsBuf.View, tempBuf.View.AsContiguous());

            using var decBuf = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QUInt4>, ArrayView<int>>(QUInt4LoadKernel)(
                n, keysBuf.View, decBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await decBuf.CopyToHostAsync<int>();
            var sv = await valsBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int expectedKey = i; // ascending 0..15
                if (got[i] != expectedKey)
                    throw new Exception($"QUInt4 pairs key mismatch at [{i}]: expected={expectedKey} got={got[i]}");
                // key i was input index (15-i) -> value 15-i.
                if (sv[i] != 15 - i)
                    throw new Exception($"QUInt4 pairs value mismatch at [{i}]: expected={15 - i} got={sv[i]}");
            }
        }

        static void GateQUInt4Radix(Accelerator acc)
        {
            var t = acc.AcceleratorType;
            if (t == AcceleratorType.CPU || t == AcceleratorType.WebGL)
                throw new UnsupportedTestException(
                    $"QUInt4 radix scatter writes packed nibbles (atomic-RMW store), unsupported on {t}.");
        }

        [TestMethod]
        public async Task QUInt4Radix_ExtractBits_GpuMatchesCpu() => await RunTest(async acc =>
            await QUInt4RadixExtractBitsImpl<AscendingQUInt4>(acc, "asc"));

        [TestMethod]
        public async Task QUInt4Radix_KeysAscending() => await RunTest(async acc =>
        {
            GateQUInt4Radix(acc);
            await QUInt4RadixKeysAscendingImpl(acc);
        });

        [TestMethod]
        public async Task QUInt4Radix_PairsAscending() => await RunTest(async acc =>
        {
            GateQUInt4Radix(acc);
            await QUInt4RadixPairsAscendingImpl(acc);
        });
    }
}
