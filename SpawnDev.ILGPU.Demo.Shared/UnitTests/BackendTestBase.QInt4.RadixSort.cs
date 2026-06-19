using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using QInt4 = ILGPU.QInt4;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Packed 4-bit QInt4 radix-sort coverage. Exercises the QInt4 nibble LOAD + the Ascending/
    // DescendingQInt4 key transform (sign-bit flip, NO ones-complement - signed two's complement is
    // already magnitude-monotonic per sign). Radix historically surfaces data-type-handling gaps the
    // convert/load/store tests miss (e.g. the FP4 PTX struct-field-IO all-zero-keys bug). The
    // ExtractBits test is LOAD-only so it runs on all 6 backends; the keys-only SORT scatters into
    // packed storage (atomic-RMW nibble store), which is unsupported on CPU (managed ref indexer) and
    // WebGL (no atomics / whole-texel scatter), so it is gated to the packed-store backends.
    public abstract partial class BackendTestBase
    {
        // -8..7 each appears twice (32 elements): every nibble value at both byte positions.
        static int[] QInt4SignedValues()
        {
            var v = new int[32];
            for (int i = 0; i < v.Length; i++) v[i] = (i % 16) - 8;
            return v;
        }

        static byte[] PackQInt4(int[] vals)
        {
            var packed = new byte[(vals.Length + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((vals[2 * k] & 0xF) | ((vals[2 * k + 1] & 0xF) << 4));
            return packed;
        }

        static void QInt4ExtractKernel<TOp>(
            Index1D i, ArrayView<QInt4> keys, ArrayView<int> flags, int shift, int bitMask)
            where TOp : struct, IRadixSortOperation<QInt4>
        {
            TOp op = default;
            flags[i.X] = op.ExtractRadixBits(keys[i.X], shift, bitMask);
        }

        // ExtractRadixBits GPU-vs-CPU bucket compare for every pass. A device bucket that diverges
        // from the CPU oracle pinpoints a backend QInt4-load or key-transform miscompile.
        async Task QInt4RadixExtractBitsImpl<TOp>(Accelerator accelerator, string tag)
            where TOp : struct, IRadixSortOperation<QInt4>
        {
            var vals = QInt4SignedValues();
            var packed = PackQInt4(vals);
            int n = vals.Length;

            using var keysBuf = accelerator.Allocate1D<QInt4>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var flagsBuf = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<QInt4>, ArrayView<int>, int, int>(QInt4ExtractKernel<TOp>);

            TOp op = default;
            for (int shift = 0; shift < op.NumBits; shift++)
            {
                kernel(n, keysBuf.View, flagsBuf.View, shift, 1);
                await accelerator.SynchronizeAsync();
                var gpu = await flagsBuf.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                {
                    int cpu = op.ExtractRadixBits((QInt4)vals[i], shift, 1);
                    if (gpu[i] != cpu)
                        throw new Exception(
                            $"{tag} QInt4 ExtractRadixBits mismatch at value={vals[i]} shift={shift}: " +
                            $"GPU={gpu[i]} CPU={cpu}");
                }
            }
        }

        // Keys-only ascending sort over the packed buffer; verifies the sorted nibbles are ascending.
        // The scatter writes packed nibbles (atomic word RMW), so this needs a packed-store backend.
        async Task QInt4RadixKeysAscendingImpl(Accelerator accelerator)
        {
            var vals = QInt4SignedValues();
            int n = vals.Length;
            var packed = PackQInt4(vals);

            using var keysBuf = accelerator.Allocate1D<QInt4>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortTempStorageSize<QInt4, AscendingQInt4>(n));
            accelerator.CreateRadixSort<QInt4, Stride1D.Dense, AscendingQInt4>()(
                accelerator.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedPacked = new byte[packed.Length];
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyToCPU(sortedPacked);
            // unpack + sign-extend
            var got = new int[n];
            for (int i = 0; i < n; i++)
            {
                int nib = (sortedPacked[i >> 1] >> ((i & 1) * 4)) & 0xF;
                got[i] = ((nib ^ 0x8) - 0x8);
            }
            var expected = (int[])vals.Clone();
            Array.Sort(expected); // ascending
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"QInt4 keys-asc mismatch at [{i}]: expected={expected[i]} got={got[i]}");
        }

        // Pairs (QInt4 key + int value) ascending: DISTINCT keys -8..7 in descending input order with
        // value=index, so sorted keys must be ascending and each value follows its key's permutation.
        // Exercises the key-bundle struct-field path (the FP4 PTX EmitIO all-zero-keys bug class).
        async Task QInt4RadixPairsAscendingImpl(Accelerator accelerator)
        {
            int n = 16;
            var keysInt = new int[n];
            var values = new int[n];
            for (int i = 0; i < n; i++)
            {
                keysInt[i] = 7 - i;   // descending input -8..7 -> keysInt = 7,6,...,-8
                values[i] = i;
            }
            var packed = PackQInt4(keysInt);

            using var keysBuf = accelerator.Allocate1D<QInt4>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var valsBuf = accelerator.Allocate1D(values);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortPairsTempStorageSize<QInt4, int, AscendingQInt4>(n));
            accelerator.CreateRadixSortPairs<QInt4, Stride1D.Dense, int, Stride1D.Dense, AscendingQInt4>()(
                accelerator.DefaultStream, keysBuf.View, valsBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedPacked = new byte[packed.Length];
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyToCPU(sortedPacked);
            var sv = await valsBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int nib = (sortedPacked[i >> 1] >> ((i & 1) * 4)) & 0xF;
                int gotKey = ((nib ^ 0x8) - 0x8);
                int expectedKey = -8 + i; // ascending
                if (gotKey != expectedKey)
                    throw new Exception($"QInt4 pairs key mismatch at [{i}]: expected={expectedKey} got={gotKey}");
                // key -8+i was input index (7-(-8+i))=(15-i) -> value 15-i.
                if (sv[i] != 15 - i)
                    throw new Exception($"QInt4 pairs value mismatch at [{i}]: expected={15 - i} got={sv[i]}");
            }
        }

        [TestMethod]
        public async Task QInt4Radix_ExtractBits_GpuMatchesCpu() => await RunTest(async acc =>
            await QInt4RadixExtractBitsImpl<AscendingQInt4>(acc, "asc"));

        [TestMethod]
        public async Task QInt4Radix_PairsAscending() => await RunTest(async acc =>
        {
            var t = acc.AcceleratorType;
            if (t == AcceleratorType.CPU || t == AcceleratorType.WebGL)
                throw new UnsupportedTestException(
                    $"QInt4 radix scatter writes packed nibbles (atomic-RMW store), unsupported on {t}.");
            await QInt4RadixPairsAscendingImpl(acc);
        });

        [TestMethod]
        public async Task QInt4Radix_KeysAscending() => await RunTest(async acc =>
        {
            var t = acc.AcceleratorType;
            if (t == AcceleratorType.CPU || t == AcceleratorType.WebGL)
                throw new UnsupportedTestException(
                    $"QInt4 radix scatter writes packed nibbles (atomic-RMW store), unsupported on {t}.");
            await QInt4RadixKeysAscendingImpl(acc);
        });
    }
}
