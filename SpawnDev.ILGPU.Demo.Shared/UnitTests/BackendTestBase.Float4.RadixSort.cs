using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Float4E2M1 = ILGPU.Float4E2M1;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Float4E2M1 (E2M1FN, the NVFP4/MXFP4 element format) radix-sort coverage. FP4 is now TRUE packed
    // 4-bit storage ([PackedBits(4)], 8 nibbles/word, ceil(N/2) bytes), like QInt4 - so the keys upload
    // as RAW PACKED nibble bytes (no transparent host pack/unpack) and the sorted keys decode back via a
    // kernel (sync raw-byte CopyToCPU throws on the browser backends). Exercises Interop.FloatAsInt(FP4)
    // (the 4-bit code in the low nibble) through Ascending/DescendingFloat4E2M1 (NumBits=4). Inputs span
    // NEGATIVE..POSITIVE incl 0; every value is EXACTLY representable in E2M1, so the comparison is exact.
    // ExtractBits is load-only (all 6 backends); the keys/pairs SORT scatters packed nibbles (atomic-RMW
    // store), so it gates to the packed-store backends (CPU + WebGL skip), exactly like the QInt4 radix.
    public abstract partial class BackendTestBase
    {
        // Distinct, exactly-representable FP4 (E2M1) values, ascending.
        static readonly float[] Fp4ExactValues =
        {
            -6f, -4f, -3f, -2f, -1.5f, -1f, -0.5f, 0f,
            0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f,
        };

        // The raw 4-bit E2M1 code (low nibble) for a float (oracle-proven managed convert).
        static byte Fp4Code(float f) => ((Float4E2M1)f).RawValue;

        static byte[] PackFp4(float[] vals)
        {
            var packed = new byte[(vals.Length + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
            {
                int lo = Fp4Code(vals[2 * k]) & 0xF;
                int hi = (2 * k + 1 < vals.Length) ? (Fp4Code(vals[2 * k + 1]) & 0xF) : 0;
                packed[k] = (byte)(lo | (hi << 4));
            }
            return packed;
        }

        static void Fp4ExtractKernel(
            Index1D i, ArrayView<Float4E2M1> keys, ArrayView<int> flags, int shift, int bitMask)
        {
            DescendingFloat4E2M1 op = default;
            flags[i.X] = op.ExtractRadixBits(keys[i.X], shift, bitMask);
        }

        // ExtractRadixBits GPU-vs-CPU bucket compare for every pass - pinpoints a FloatAsInt(FP4) /
        // packed-load miscompile on any backend.
        [TestMethod]
        public async Task Fp4Radix_E2M1_ExtractBits_GpuMatchesCpu() => await RunTest(async accelerator =>
        {
            var vals = Fp4ExactValues;
            int n = vals.Length;
            var packed = PackFp4(vals);

            using var keysBuf = accelerator.Allocate1D<Float4E2M1>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var flagsBuf = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<Float4E2M1>, ArrayView<int>, int, int>(Fp4ExtractKernel);

            DescendingFloat4E2M1 op = default;
            for (int shift = 0; shift < op.NumBits; shift++)
            {
                kernel(n, keysBuf.View, flagsBuf.View, shift, 1);
                await accelerator.SynchronizeAsync();
                var gpu = await flagsBuf.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                {
                    int cpu = op.ExtractRadixBits((Float4E2M1)vals[i], shift, 1);
                    if (gpu[i] != cpu)
                        throw new Exception(
                            $"E2M1 desc ExtractRadixBits mismatch at key={vals[i]} shift={shift}: GPU={gpu[i]} CPU={cpu}");
                }
            }
        });

        // FP4 radix scatter writes packed nibbles (atomic-RMW store): packed-store backends only.
        static void GateFp4Radix(Accelerator acc)
        {
            var t = acc.AcceleratorType;
            if (t == AcceleratorType.CPU || t == AcceleratorType.WebGL)
                throw new UnsupportedTestException(
                    $"FP4 radix scatter writes packed nibbles (atomic-RMW store), unsupported on {t}.");
        }

        [TestMethod]
        public async Task Fp4Radix_E2M1_KeysDescending() => await RunTest(async accelerator =>
        {
            GateFp4Radix(accelerator);
            const int reps = 12;
            int n = Fp4ExactValues.Length * reps; // 180
            var inF = new float[n];
            for (int i = 0; i < n; i++) inF[i] = Fp4ExactValues[i % Fp4ExactValues.Length];
            var packed = PackFp4(inF);

            var expected = (float[])inF.Clone();
            Array.Sort(expected);
            Array.Reverse(expected); // descending

            using var keysBuf = accelerator.Allocate1D<Float4E2M1>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortTempStorageSize<Float4E2M1, DescendingFloat4E2M1>(n));
            accelerator.CreateRadixSort<Float4E2M1, Stride1D.Dense, DescendingFloat4E2M1>()(
                accelerator.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());

            using var decBuf = accelerator.Allocate1D<float>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Float4E2M1>, ArrayView<float>>(Float4LoadKernel)(
                n, keysBuf.View, decBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await decBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs(got[i] - expected[i]) > 0.001f)
                    throw new Exception($"E2M1 keys-only desc mismatch at [{i}]: expected={expected[i]} got={got[i]}");
        });

        [TestMethod]
        public async Task Fp4Radix_E2M1_PairsAscending() => await RunTest(async accelerator =>
        {
            GateFp4Radix(accelerator);
            int n = Fp4ExactValues.Length;
            var inF = new float[n];
            var values = new int[n];
            for (int i = 0; i < n; i++)
            {
                inF[i] = Fp4ExactValues[n - 1 - i]; // descending input
                values[i] = i;
            }
            var packed = PackFp4(inF);

            using var keysBuf = accelerator.Allocate1D<Float4E2M1>(n);
            ((IContiguousArrayView)keysBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var valsBuf = accelerator.Allocate1D(values);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortPairsTempStorageSize<Float4E2M1, int, AscendingFloat4E2M1>(n));
            accelerator.CreateRadixSortPairs<Float4E2M1, Stride1D.Dense, int, Stride1D.Dense, AscendingFloat4E2M1>()(
                accelerator.DefaultStream, keysBuf.View, valsBuf.View, tempBuf.View.AsContiguous());

            using var decBuf = accelerator.Allocate1D<float>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Float4E2M1>, ArrayView<float>>(Float4LoadKernel)(
                n, keysBuf.View, decBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await decBuf.CopyToHostAsync<float>();
            var sv = await valsBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                float expected = Fp4ExactValues[i]; // ascending
                if (MathF.Abs(got[i] - expected) > 0.001f)
                    throw new Exception($"E2M1 pairs key mismatch at [{i}]: expected={expected} got={got[i]}");
                if (sv[i] != n - 1 - i)
                    throw new Exception($"E2M1 pairs value mismatch at [{i}]: expected={n - 1 - i} got={sv[i]}");
            }
        });
    }
}
