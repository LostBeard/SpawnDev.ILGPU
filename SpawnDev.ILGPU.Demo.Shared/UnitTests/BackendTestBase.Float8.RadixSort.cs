using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Float8E4M3 = ILGPU.Float8E4M3;
using Float8E5M2 = ILGPU.Float8E5M2;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // FP8 (Float8E4M3 + Float8E5M2) radix-sort coverage on all 6 backends. Exercises
    // Interop.FloatAsInt(Float8E*) (the 8-bit FP8 pattern, NOT the promoted f32 bits) through
    // Ascending/DescendingFloat8E4M3/E5M2 (NumBits=8). Inputs span NEGATIVE..POSITIVE incl 0 and
    // fractions so the sign-flip + ones-complement key transform is fully exercised. Every value
    // used is EXACTLY representable in BOTH formats (small integers / halves / powers of two in
    // 2^-1..2^4), so (float)((T)v) == v and the comparison is exact. On WebGL the keys route
    // through the unpacked-f32 working representation (the whole-texel scatter can't move a 1-byte
    // sub-word value); on the other 5 backends they sort as native 1-byte keys.
    public abstract partial class BackendTestBase
    {
        // Distinct, exactly-representable FP8 values, ascending. Exact in both E4M3 and E5M2.
        static readonly float[] Fp8ExactValues =
        {
            -16f, -12f, -8f, -6f, -4f, -3f, -2f, -1.5f, -1f, -0.5f, 0f,
            0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f, 12f, 16f,
        };

        // Generic device-side ExtractRadixBits kernel (one call per element). Instantiated per
        // concrete (T, TOp), so Interop.FloatAsInt(T) binds the concrete FP8 overload -> the
        // FloatAsIntCast lowering this test validates on every backend.
        static void Fp8ExtractKernel<T, TOp>(
            Index1D i, ArrayView<T> keys, ArrayView<int> flags, int shift, int bitMask)
            where T : unmanaged
            where TOp : struct, IRadixSortOperation<T>
        {
            TOp op = default;
            flags[i.X] = op.ExtractRadixBits(keys[i.X], shift, bitMask);
        }

        // ExtractRadixBits GPU-vs-CPU bucket compare for every 1-bit pass (shift 0..7). A device
        // bucket that diverges from the CPU oracle pinpoints a backend FloatAsInt(FP8) miscompile.
        async Task Fp8RadixExtractBitsImpl<T, TDesc>(
            Accelerator accelerator, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TDesc : struct, IRadixSortOperation<T>
        {
            var keys = new T[Fp8ExactValues.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = fromF(Fp8ExactValues[i]);

            using var keysBuf = accelerator.Allocate1D(keys);
            using var flagsBuf = accelerator.Allocate1D<int>(keys.Length);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<T>, ArrayView<int>, int, int>(Fp8ExtractKernel<T, TDesc>);

            TDesc op = default;
            int numBits = op.NumBits; // 8
            for (int shift = 0; shift < numBits; shift++)
            {
                kernel(keys.Length, keysBuf.View, flagsBuf.View, shift, 1);
                await accelerator.SynchronizeAsync();
                var gpu = await flagsBuf.CopyToHostAsync<int>();
                for (int i = 0; i < keys.Length; i++)
                {
                    int cpu = op.ExtractRadixBits(keys[i], shift, 1);
                    if (gpu[i] != cpu)
                        throw new Exception(
                            $"{tag} ExtractRadixBits mismatch at key={toF(keys[i])} shift={shift}: " +
                            $"GPU={gpu[i]} CPU={cpu}");
                }
            }
        }

        // Keys-only descending sort of the distinct set TILED to a multi-group size (exercises the
        // full multi-pass / multi-group radix coordination). Expected = the same inputs sorted
        // descending on the host (handles the tiled duplicates by value comparison).
        async Task Fp8RadixKeysDescendingImpl<T, TDesc>(
            Accelerator accelerator, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TDesc : struct, IRadixSortOperation<T>
        {
            const int reps = 12;
            int n = Fp8ExactValues.Length * reps; // 252
            var inF = new float[n];
            for (int i = 0; i < n; i++) inF[i] = Fp8ExactValues[i % Fp8ExactValues.Length];
            var keys = new T[n];
            for (int i = 0; i < n; i++) keys[i] = fromF(inF[i]);

            var expected = (float[])inF.Clone();
            Array.Sort(expected);
            Array.Reverse(expected); // descending

            using var keysBuf = accelerator.Allocate1D(keys);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortTempStorageSize<T, TDesc>(n));
            accelerator.CreateRadixSort<T, Stride1D.Dense, TDesc>()(
                accelerator.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sorted = await keysBuf.CopyToHostAsync<T>();
            for (int i = 0; i < n; i++)
            {
                float got = toF(sorted[i]);
                if (MathF.Abs(got - expected[i]) > 0.001f)
                    throw new Exception(
                        $"{tag} keys-only desc mismatch at [{i}]: expected={expected[i]} got={got}");
            }
        }

        // Pairs (FP8 key + int value) ascending, DISTINCT keys (descending input) with value=index,
        // so the sorted keys must be ascending and each value must follow its key's permutation.
        async Task Fp8RadixPairsAscendingImpl<T, TAsc>(
            Accelerator accelerator, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TAsc : struct, IRadixSortOperation<T>
        {
            int n = Fp8ExactValues.Length;
            var keys = new T[n];
            var values = new int[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = fromF(Fp8ExactValues[n - 1 - i]); // descending input
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valsBuf = accelerator.Allocate1D(values);
            using var tempBuf = accelerator.Allocate1D<int>(
                accelerator.ComputeRadixSortPairsTempStorageSize<T, int, TAsc>(n));
            accelerator.CreateRadixSortPairs<T, Stride1D.Dense, int, Stride1D.Dense, TAsc>()(
                accelerator.DefaultStream, keysBuf.View, valsBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sk = await keysBuf.CopyToHostAsync<T>();
            var sv = await valsBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                float expected = Fp8ExactValues[i]; // ascending
                if (MathF.Abs(toF(sk[i]) - expected) > 0.001f)
                    throw new Exception(
                        $"{tag} pairs key mismatch at [{i}]: expected={expected} got={toF(sk[i])}");
                // pos i holds Fp8ExactValues[i], which was input index (n-1-i) -> value n-1-i.
                if (sv[i] != n - 1 - i)
                    throw new Exception(
                        $"{tag} pairs value mismatch at [{i}]: expected={n - 1 - i} got={sv[i]}");
            }
        }

        [TestMethod]
        public async Task Fp8Radix_E4M3_ExtractBits_GpuMatchesCpu() => await RunTest(async acc =>
            await Fp8RadixExtractBitsImpl<Float8E4M3, DescendingFloat8E4M3>(
                acc, f => (Float8E4M3)f, x => (float)x, "E4M3 desc"));

        [TestMethod]
        public async Task Fp8Radix_E5M2_ExtractBits_GpuMatchesCpu() => await RunTest(async acc =>
            await Fp8RadixExtractBitsImpl<Float8E5M2, DescendingFloat8E5M2>(
                acc, f => (Float8E5M2)f, x => (float)x, "E5M2 desc"));

        [TestMethod]
        public async Task Fp8Radix_E4M3_KeysDescending() => await RunTest(async acc =>
            await Fp8RadixKeysDescendingImpl<Float8E4M3, DescendingFloat8E4M3>(
                acc, f => (Float8E4M3)f, x => (float)x, "E4M3"));

        [TestMethod]
        public async Task Fp8Radix_E5M2_KeysDescending() => await RunTest(async acc =>
            await Fp8RadixKeysDescendingImpl<Float8E5M2, DescendingFloat8E5M2>(
                acc, f => (Float8E5M2)f, x => (float)x, "E5M2"));

        [TestMethod]
        public async Task Fp8Radix_E4M3_PairsAscending() => await RunTest(async acc =>
            await Fp8RadixPairsAscendingImpl<Float8E4M3, AscendingFloat8E4M3>(
                acc, f => (Float8E4M3)f, x => (float)x, "E4M3"));

        [TestMethod]
        public async Task Fp8Radix_E5M2_PairsAscending() => await RunTest(async acc =>
            await Fp8RadixPairsAscendingImpl<Float8E5M2, AscendingFloat8E5M2>(
                acc, f => (Float8E5M2)f, x => (float)x, "E5M2"));
    }
}
