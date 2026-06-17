using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Float4E2M1 = ILGPU.Float4E2M1;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Float4E2M1 (E2M1FN, the NVFP4/MXFP4 element format) radix-sort coverage on all 6 backends.
    // Exercises Interop.FloatAsInt(Float4E2M1) (the 4-bit FP4 pattern in the low nibble, NOT the
    // promoted f32 bits) through Ascending/DescendingFloat4E2M1 (NumBits=4). Inputs span the full
    // NEGATIVE..POSITIVE set incl 0 so the sign-flip + ones-complement key transform is fully
    // exercised. Every value used is EXACTLY representable in E2M1 ({0,.5,1,1.5,2,3,4,6} and the
    // negatives), so (float)((Float4E2M1)v) == v and the comparison is exact. On WebGL the keys route
    // through the unpacked-f32 working representation (the whole-texel scatter can't move a 1-byte
    // sub-word value); on the other 5 backends they sort as native 1-byte (4-bit) keys.
    public abstract partial class BackendTestBase
    {
        // Distinct, exactly-representable FP4 (E2M1) values, ascending.
        static readonly float[] Fp4ExactValues =
        {
            -6f, -4f, -3f, -2f, -1.5f, -1f, -0.5f, 0f,
            0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f,
        };

        // Generic device-side ExtractRadixBits kernel (one call per element). Instantiated per
        // concrete (T, TOp), so Interop.FloatAsInt(T) binds the concrete FP4 overload -> the
        // FloatAsIntCast lowering this test validates on every backend.
        static void Fp4ExtractKernel<T, TOp>(
            Index1D i, ArrayView<T> keys, ArrayView<int> flags, int shift, int bitMask)
            where T : unmanaged
            where TOp : struct, IRadixSortOperation<T>
        {
            TOp op = default;
            flags[i.X] = op.ExtractRadixBits(keys[i.X], shift, bitMask);
        }

        // ExtractRadixBits GPU-vs-CPU bucket compare for every 1-bit pass (shift 0..3). A device
        // bucket that diverges from the CPU oracle pinpoints a backend FloatAsInt(FP4) miscompile.
        async Task Fp4RadixExtractBitsImpl<T, TDesc>(
            Accelerator accelerator, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TDesc : struct, IRadixSortOperation<T>
        {
            var keys = new T[Fp4ExactValues.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = fromF(Fp4ExactValues[i]);

            using var keysBuf = accelerator.Allocate1D(keys);
            using var flagsBuf = accelerator.Allocate1D<int>(keys.Length);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<T>, ArrayView<int>, int, int>(Fp4ExtractKernel<T, TDesc>);

            TDesc op = default;
            int numBits = op.NumBits; // 4
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
        async Task Fp4RadixKeysDescendingImpl<T, TDesc>(
            Accelerator accelerator, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TDesc : struct, IRadixSortOperation<T>
        {
            const int reps = 12;
            int n = Fp4ExactValues.Length * reps; // 180
            var inF = new float[n];
            for (int i = 0; i < n; i++) inF[i] = Fp4ExactValues[i % Fp4ExactValues.Length];
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

        // Pairs (FP4 key + int value) ascending, DISTINCT keys (descending input) with value=index,
        // so the sorted keys must be ascending and each value must follow its key's permutation.
        async Task Fp4RadixPairsAscendingImpl<T, TAsc>(
            Accelerator accelerator, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TAsc : struct, IRadixSortOperation<T>
        {
            int n = Fp4ExactValues.Length;
            var keys = new T[n];
            var values = new int[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = fromF(Fp4ExactValues[n - 1 - i]); // descending input
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
                float expected = Fp4ExactValues[i]; // ascending
                if (MathF.Abs(toF(sk[i]) - expected) > 0.001f)
                    throw new Exception(
                        $"{tag} pairs key mismatch at [{i}]: expected={expected} got={toF(sk[i])}");
                // pos i holds Fp4ExactValues[i], which was input index (n-1-i) -> value n-1-i.
                if (sv[i] != n - 1 - i)
                    throw new Exception(
                        $"{tag} pairs value mismatch at [{i}]: expected={n - 1 - i} got={sv[i]}");
            }
        }

        [TestMethod]
        public async Task Fp4Radix_E2M1_ExtractBits_GpuMatchesCpu() => await RunTest(async acc =>
            await Fp4RadixExtractBitsImpl<Float4E2M1, DescendingFloat4E2M1>(
                acc, f => (Float4E2M1)f, x => (float)x, "E2M1 desc"));

        [TestMethod]
        public async Task Fp4Radix_E2M1_KeysDescending() => await RunTest(async acc =>
            await Fp4RadixKeysDescendingImpl<Float4E2M1, DescendingFloat4E2M1>(
                acc, f => (Float4E2M1)f, x => (float)x, "E2M1"));

        [TestMethod]
        public async Task Fp4Radix_E2M1_PairsAscending() => await RunTest(async acc =>
            await Fp4RadixPairsAscendingImpl<Float4E2M1, AscendingFloat4E2M1>(
                acc, f => (Float4E2M1)f, x => (float)x, "E2M1"));
    }
}
