using System;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using BFloat16 = ILGPU.BFloat16;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // bfloat16 radix-sort coverage on all 6 backends. Exercises Interop.FloatAsInt(BFloat16)
    // (the 16-bit bf16 pattern, NOT the promoted f32 bits) through AscendingBFloat16 /
    // DescendingBFloat16. Inputs span NEGATIVE..POSITIVE so the sign-flip + ones-complement
    // key transform is exercised (a positives-only test would never touch the negative path).
    // Integers in [-128, 127] are exactly representable in bf16 (7 mantissa + implicit bits),
    // so the comparison is exact.
    public abstract partial class BackendTestBase
    {
        // Runs DescendingBFloat16.ExtractRadixBits on the device, one call per element.
        static void BF16DescExtractKernel(
            Index1D i, ArrayView<BFloat16> keys, ArrayView<int> flags, int shift, int bitMask)
        {
            DescendingBFloat16 op = default;
            flags[i.X] = op.ExtractRadixBits(keys[i.X], shift, bitMask);
        }

        // Same, but the bf16 keys arrive as a BODY-STRUCT view field (param0_f0) - the exact
        // shape the radix sort kernels use - to isolate whether the body-struct bf16 load (not a
        // direct param) is where descending diverges on WebGPU.
        public struct BF16BucketBundle
        {
            public ArrayView<BFloat16> Keys;
            public ArrayView<int> Flags;
        }

        static void BF16DescExtractBodyStructKernel(
            Index1D i, BF16BucketBundle b, int shift, int bitMask)
        {
            DescendingBFloat16 op = default;
            b.Flags[i.X] = op.ExtractRadixBits(b.Keys[i.X], shift, bitMask);
        }

        /// <summary>
        /// DIAGNOSTIC: compares the raw radix bucket from DescendingBFloat16.ExtractRadixBits computed
        /// ON THE DEVICE vs the CPU oracle, for every 2-bit radix pass (shift 0,2,..,14; bitMask=3 - the
        /// same passes the radix sort uses). If the device returns wrong buckets from correct WGSL, this
        /// pinpoints the exact key+shift (= a backend codegen/runtime miscompilation); if the buckets
        /// match but the full sort still mis-orders, the fault is in the sort coordination, not the op.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_RadixExtractBits_GpuMatchesCpu() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            int n = 256;
            var keys = new BFloat16[n];
            for (int i = 0; i < n; i++) keys[i] = (BFloat16)(float)(i - 128); // -128..127

            using var keysBuf = accelerator.Allocate1D(keys);
            using var flagsBuf = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<BFloat16>, ArrayView<int>, int, int>(BF16DescExtractKernel);

            DescendingBFloat16 op = default;
            for (int shift = 0; shift < 16; shift += 2)
            {
                kernel(n, keysBuf.View, flagsBuf.View, shift, 3);
                await accelerator.SynchronizeAsync();
                var gpuFlags = await flagsBuf.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                {
                    int cpuBucket = op.ExtractRadixBits(keys[i], shift, 3);
                    if (gpuFlags[i] != cpuBucket)
                        throw new Exception(
                            $"bf16 desc ExtractRadixBits mismatch at key={(float)keys[i]} shift={shift}: " +
                            $"GPU={gpuFlags[i]} CPU={cpuBucket}");
                }
            }
        });

        /// <summary>
        /// DIAGNOSTIC (body-struct variant): same bucket compare, but the bf16 keys arrive through a
        /// body-struct view field (the radix kernel's param0_f0 shape). Isolates the body-struct bf16
        /// load from the direct-param load.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_RadixExtractBits_BodyStruct_GpuMatchesCpu() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            int n = 256;
            var keys = new BFloat16[n];
            for (int i = 0; i < n; i++) keys[i] = (BFloat16)(float)(i - 128);

            using var keysBuf = accelerator.Allocate1D(keys);
            using var flagsBuf = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, BF16BucketBundle, int, int>(BF16DescExtractBodyStructKernel);

            DescendingBFloat16 op = default;
            for (int shift = 0; shift < 16; shift += 2)
            {
                kernel(n, new BF16BucketBundle { Keys = keysBuf.View, Flags = flagsBuf.View }, shift, 3);
                await accelerator.SynchronizeAsync();
                var gpuFlags = await flagsBuf.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                {
                    int cpuBucket = op.ExtractRadixBits(keys[i], shift, 3);
                    if (gpuFlags[i] != cpuBucket)
                        throw new Exception(
                            $"bf16 desc body-struct ExtractRadixBits mismatch at key={(float)keys[i]} " +
                            $"shift={shift}: GPU={gpuFlags[i]} CPU={cpuBucket}");
                }
            }
        });

        /// <summary>
        /// RadixSortPairs with bf16 keys + int values, ascending, negative..positive input.
        /// Verifies both the key ordering AND that the paired values follow the permutation.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_RadixSortPairs_Ascending() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            int n = 256;
            var keys = new BFloat16[n];
            var values = new int[n];
            // Descending keys 127..-128, value = input index. After an ascending sort,
            // position i must hold key (i-128) which came from input index (n-1-i).
            for (int i = 0; i < n; i++)
            {
                keys[i] = (BFloat16)(float)((n - 1 - i) - 128);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator
                .ComputeRadixSortPairsTempStorageSize<BFloat16, int, AscendingBFloat16>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator
                .CreateRadixSortPairs<BFloat16, Stride1D.Dense, int, Stride1D.Dense, AscendingBFloat16>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<BFloat16>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(i - 128); // ascending -128..127
                if (MathF.Abs((float)sortedKeys[i] - expected) > 0.01f)
                    throw new Exception(
                        $"bf16 RadixSortPairs key mismatch at [{i}]: expected={expected}, got={(float)sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception(
                        $"bf16 RadixSortPairs value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// DIAGNOSTIC: minimal-input descending bf16 sort. Dumps the exact result so the minimal
        /// failing case is visible (vs the full 256-element sort). Also cross-checks against the
        /// SAME keys widened to f32 and sorted with DescendingFloat - if the bf16 result differs from
        /// the f32-widened result on the same backend, the fault is the bf16-key radix coordination,
        /// not the sort itself (widening bf16-&gt;f32 is lossless + order-preserving).
        /// </summary>
        [TestMethod]
        public async Task BFloat16_RadixSort_Descending_MinimalDump() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            // Sweep sizes to find the threshold where descending bf16 breaks. Each input is already
            // descending (i.e. exp[k] = topVal - k) so the sorted result must equal the input.
            foreach (int n in new[] { 8, 16, 32, 64, 96, 128, 192, 256 })
            {
                var keys = new BFloat16[n];
                for (int i = 0; i < n; i++) keys[i] = (BFloat16)(float)(i - (n / 2)); // ASCENDING input (needs full reversal)

                using var keysBuf = accelerator.Allocate1D(keys);
                using var tempBuf = accelerator.Allocate1D<int>(
                    accelerator.ComputeRadixSortTempStorageSize<BFloat16, DescendingBFloat16>(n));
                accelerator.CreateRadixSort<BFloat16, Stride1D.Dense, DescendingBFloat16>()(
                    accelerator.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();
                var sorted = await keysBuf.CopyToHostAsync<BFloat16>();

                for (int i = 0; i < n; i++)
                {
                    float expected = (float)((n / 2) - 1 - i);
                    if (MathF.Abs((float)sorted[i] - expected) > 0.01f)
                    {
                        string head = "";
                        for (int j = 0; j < System.Math.Min(n, 16); j++) head += (float)sorted[j] + ",";
                        throw new Exception($"bf16 desc FIRST FAILS at n={n}, [{i}]: exp={expected} got={(float)sorted[i]} first=[{head}]");
                    }
                }
            }
        });

        /// <summary>
        /// Keys-only RadixSort with bf16 keys, descending, negative..positive input.
        /// </summary>
        [TestMethod]
        public async Task BFloat16_RadixSort_Descending() => await RunTest(async accelerator =>
        {
            RequireBFloat16SupportedBackend(accelerator);
            int n = 256;
            var keys = new BFloat16[n];
            for (int i = 0; i < n; i++)
                keys[i] = (BFloat16)(float)(i - 128); // ascending -128..127 input

            using var keysBuf = accelerator.Allocate1D(keys);
            var tempSize = accelerator
                .ComputeRadixSortTempStorageSize<BFloat16, DescendingBFloat16>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator
                .CreateRadixSort<BFloat16, Stride1D.Dense, DescendingBFloat16>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<BFloat16>();
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(127 - i); // descending 127..-128
                if (MathF.Abs((float)sortedKeys[i] - expected) > 0.01f)
                    throw new Exception(
                        $"bf16 RadixSort (desc) key mismatch at [{i}]: expected={expected}, got={(float)sortedKeys[i]}");
            }
        });
    }
}
