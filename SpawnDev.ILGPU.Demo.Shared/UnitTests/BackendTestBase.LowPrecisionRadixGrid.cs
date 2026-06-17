using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Half = ILGPU.Half;
using BFloat16 = ILGPU.BFloat16;
using Float8E4M3 = ILGPU.Float8E4M3;
using Float8E5M2 = ILGPU.Float8E5M2;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Completes the radix-sort TEST grid for the four low-precision float types so EVERY
    // type x {keys-only, key/value pairs} x {ascending, descending} combination is covered on all 6
    // backends, plus a body-struct ExtractBits diagnostic for each (Geordi 2026-06-17, data-type 100%
    // sweep). Production radix code was already at full parity (Ascending/Descending ops +
    // Interop.FloatAsInt(T) + per-backend FloatAsIntCast); this fills the empty cells the audit found:
    // keys-only-ascending (all 4), pairs-descending (all 4), Half keys-only sort, body-struct
    // ExtractBits (Half/E4M3/E5M2 - bf16 already had it). Reuses Fp8ExactValues (-16..16), which is
    // EXACTLY representable in Half/bf16/E4M3/E5M2 alike, so the sign-flip + ones-complement key
    // transform is fully exercised across negative/zero/positive and the comparison is exact.
    public abstract partial class BackendTestBase
    {
        // ---- generic helpers for the directions Float8.RadixSort.cs didn't already cover ----

        // Keys-only ASCENDING, tiled to a multi-group size (full multi-pass/multi-group coordination).
        async Task RadixKeysAscendingImpl<T, TAsc>(
            Accelerator acc, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TAsc : struct, IRadixSortOperation<T>
        {
            const int reps = 12;
            int n = Fp8ExactValues.Length * reps;
            var inF = new float[n];
            for (int i = 0; i < n; i++) inF[i] = Fp8ExactValues[i % Fp8ExactValues.Length];
            var keys = new T[n];
            for (int i = 0; i < n; i++) keys[i] = fromF(inF[i]);

            var expected = (float[])inF.Clone();
            Array.Sort(expected); // ascending

            using var keysBuf = acc.Allocate1D(keys);
            using var tempBuf = acc.Allocate1D<int>(acc.ComputeRadixSortTempStorageSize<T, TAsc>(n));
            acc.CreateRadixSort<T, Stride1D.Dense, TAsc>()(
                acc.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());
            await acc.SynchronizeAsync();

            var sorted = await keysBuf.CopyToHostAsync<T>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs(toF(sorted[i]) - expected[i]) > 0.001f)
                    throw new Exception($"{tag} keys-only ASC mismatch at [{i}]: expected={expected[i]} got={toF(sorted[i])}");
        }

        // Keys-only DESCENDING (so Half - which had no keys-only sort test - gets one too).
        async Task RadixKeysDescendingImpl<T, TDesc>(
            Accelerator acc, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TDesc : struct, IRadixSortOperation<T>
        {
            const int reps = 12;
            int n = Fp8ExactValues.Length * reps;
            var inF = new float[n];
            for (int i = 0; i < n; i++) inF[i] = Fp8ExactValues[i % Fp8ExactValues.Length];
            var keys = new T[n];
            for (int i = 0; i < n; i++) keys[i] = fromF(inF[i]);

            var expected = (float[])inF.Clone();
            Array.Sort(expected); Array.Reverse(expected); // descending

            using var keysBuf = acc.Allocate1D(keys);
            using var tempBuf = acc.Allocate1D<int>(acc.ComputeRadixSortTempStorageSize<T, TDesc>(n));
            acc.CreateRadixSort<T, Stride1D.Dense, TDesc>()(
                acc.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());
            await acc.SynchronizeAsync();

            var sorted = await keysBuf.CopyToHostAsync<T>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs(toF(sorted[i]) - expected[i]) > 0.001f)
                    throw new Exception($"{tag} keys-only DESC mismatch at [{i}]: expected={expected[i]} got={toF(sorted[i])}");
        }

        // Pairs (key + int value) DESCENDING: ascending input with value=index; expect keys descending
        // and each value to follow its key's permutation.
        async Task RadixPairsDescendingImpl<T, TDesc>(
            Accelerator acc, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TDesc : struct, IRadixSortOperation<T>
        {
            int n = Fp8ExactValues.Length;
            var keys = new T[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = fromF(Fp8ExactValues[i]); values[i] = i; } // ascending input

            using var keysBuf = acc.Allocate1D(keys);
            using var valsBuf = acc.Allocate1D(values);
            using var tempBuf = acc.Allocate1D<int>(acc.ComputeRadixSortPairsTempStorageSize<T, int, TDesc>(n));
            acc.CreateRadixSortPairs<T, Stride1D.Dense, int, Stride1D.Dense, TDesc>()(
                acc.DefaultStream, keysBuf.View, valsBuf.View, tempBuf.View.AsContiguous());
            await acc.SynchronizeAsync();

            var sk = await keysBuf.CopyToHostAsync<T>();
            var sv = await valsBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                float expected = Fp8ExactValues[n - 1 - i]; // descending
                if (MathF.Abs(toF(sk[i]) - expected) > 0.001f)
                    throw new Exception($"{tag} pairs DESC key mismatch at [{i}]: expected={expected} got={toF(sk[i])}");
                // pos i holds Fp8ExactValues[n-1-i], which was input index (n-1-i) -> value n-1-i.
                if (sv[i] != n - 1 - i)
                    throw new Exception($"{tag} pairs DESC value mismatch at [{i}]: expected={n - 1 - i} got={sv[i]}");
            }
        }

        // Body-struct ExtractBits GPU-vs-CPU: keys arrive through a view FIELD of a struct (the shape
        // the real radix kernels use), isolating the body-struct sub-word load per backend.
        public struct LowpBucketBundle<T> where T : unmanaged
        {
            public ArrayView<T> Keys;
            public ArrayView<int> Flags;
        }

        static void LowpBodyStructExtractKernel<T, TOp>(Index1D i, LowpBucketBundle<T> b, int shift, int bitMask)
            where T : unmanaged
            where TOp : struct, IRadixSortOperation<T>
        {
            TOp op = default;
            b.Flags[i.X] = op.ExtractRadixBits(b.Keys[i.X], shift, bitMask);
        }

        async Task RadixBodyStructExtractImpl<T, TOp>(
            Accelerator acc, Func<float, T> fromF, Func<T, float> toF, string tag)
            where T : unmanaged
            where TOp : struct, IRadixSortOperation<T>
        {
            var keys = new T[Fp8ExactValues.Length];
            for (int i = 0; i < keys.Length; i++) keys[i] = fromF(Fp8ExactValues[i]);

            using var keysBuf = acc.Allocate1D(keys);
            using var flagsBuf = acc.Allocate1D<int>(keys.Length);
            var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, LowpBucketBundle<T>, int, int>(
                LowpBodyStructExtractKernel<T, TOp>);

            TOp op = default;
            for (int shift = 0; shift < op.NumBits; shift++)
            {
                kernel(keys.Length, new LowpBucketBundle<T> { Keys = keysBuf.View, Flags = flagsBuf.View }, shift, 1);
                await acc.SynchronizeAsync();
                var gpu = await flagsBuf.CopyToHostAsync<int>();
                for (int i = 0; i < keys.Length; i++)
                {
                    int cpu = op.ExtractRadixBits(keys[i], shift, 1);
                    if (gpu[i] != cpu)
                        throw new Exception($"{tag} body-struct ExtractRadixBits mismatch at key={toF(keys[i])} shift={shift}: GPU={gpu[i]} CPU={cpu}");
                }
            }
        }

        // ============================ test grid (the missing cells) ============================

        // ---- keys-only ASCENDING (was missing for ALL four types) ----
        [TestMethod] public async Task RadixGrid_Half_KeysAscending() => await RunTest(async a =>
            await RadixKeysAscendingImpl<Half, AscendingHalf>(a, f => (Half)f, x => (float)x, "Half"));
        [TestMethod] public async Task RadixGrid_BFloat16_KeysAscending() => await RunTest(async a =>
            await RadixKeysAscendingImpl<BFloat16, AscendingBFloat16>(a, f => (BFloat16)f, x => (float)x, "bf16"));
        [TestMethod] public async Task RadixGrid_E4M3_KeysAscending() => await RunTest(async a =>
            await RadixKeysAscendingImpl<Float8E4M3, AscendingFloat8E4M3>(a, f => (Float8E4M3)f, x => (float)x, "E4M3"));
        [TestMethod] public async Task RadixGrid_E5M2_KeysAscending() => await RunTest(async a =>
            await RadixKeysAscendingImpl<Float8E5M2, AscendingFloat8E5M2>(a, f => (Float8E5M2)f, x => (float)x, "E5M2"));

        // ---- keys-only DESCENDING (Half had none; the others already have one in their own files) ----
        [TestMethod] public async Task RadixGrid_Half_KeysDescending() => await RunTest(async a =>
            await RadixKeysDescendingImpl<Half, DescendingHalf>(a, f => (Half)f, x => (float)x, "Half"));

        // ---- pairs DESCENDING (was missing for ALL four types) ----
        [TestMethod] public async Task RadixGrid_Half_PairsDescending() => await RunTest(async a =>
            await RadixPairsDescendingImpl<Half, DescendingHalf>(a, f => (Half)f, x => (float)x, "Half"));
        [TestMethod] public async Task RadixGrid_BFloat16_PairsDescending() => await RunTest(async a =>
            await RadixPairsDescendingImpl<BFloat16, DescendingBFloat16>(a, f => (BFloat16)f, x => (float)x, "bf16"));
        [TestMethod] public async Task RadixGrid_E4M3_PairsDescending() => await RunTest(async a =>
            await RadixPairsDescendingImpl<Float8E4M3, DescendingFloat8E4M3>(a, f => (Float8E4M3)f, x => (float)x, "E4M3"));
        [TestMethod] public async Task RadixGrid_E5M2_PairsDescending() => await RunTest(async a =>
            await RadixPairsDescendingImpl<Float8E5M2, DescendingFloat8E5M2>(a, f => (Float8E5M2)f, x => (float)x, "E5M2"));

        // ---- body-struct ExtractBits (bf16 already had it; add Half/E4M3/E5M2) ----
        [TestMethod] public async Task RadixGrid_Half_BodyStructExtractBits() => await RunTest(async a =>
            await RadixBodyStructExtractImpl<Half, DescendingHalf>(a, f => (Half)f, x => (float)x, "Half"));
        [TestMethod] public async Task RadixGrid_E4M3_BodyStructExtractBits() => await RunTest(async a =>
            await RadixBodyStructExtractImpl<Float8E4M3, DescendingFloat8E4M3>(a, f => (Float8E4M3)f, x => (float)x, "E4M3"));
        [TestMethod] public async Task RadixGrid_E5M2_BodyStructExtractBits() => await RunTest(async a =>
            await RadixBodyStructExtractImpl<Float8E5M2, DescendingFloat8E5M2>(a, f => (Float8E5M2)f, x => (float)x, "E5M2"));
    }
}
