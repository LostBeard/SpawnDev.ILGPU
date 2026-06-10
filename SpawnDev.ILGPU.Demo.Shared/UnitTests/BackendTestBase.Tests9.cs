using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 9: Large-scale RadixSort stress tests + diagnostic scan tests.
    //
    // These tests reproduce the exact usage patterns from SpawnScene's Gaussian
    // splat renderer, which sorts 1.4M+ splat indices by quantized camera depth
    // using DescendingInt32 RadixSortPairs every frame. The renderer exhibits
    // holes and random splat movement at >4M elements, pointing to data
    // corruption in the radix sort at scale.
    //
    // DIAGNOSTIC: The global inclusive scan (accelerator.CreateScan with
    // ScanKind.Inclusive) switches from single-group to multi-pass when the
    // input exceeds MaxNumThreadsPerGroup elements. RadixSort's counter array
    // scan crosses this threshold when data > ~16K elements. Tests below
    // isolate the scan to determine if it's the root cause.
    //
    // Test categories:
    //   0. Diagnostic: Global inclusive scan at various sizes
    //   1. Diagnostic: RadixSort at boundary sizes (16K vs 20K)
    //   2. Real-world scale (1.4M elements, SpawnScene's exact count)
    //   3. Sentinel value handling (int.MinValue for culled splats)
    //   4. Repeated re-sorts (simulating camera movement)
    //   5. Large scale stress (2M, 4M elements)
    //   6. Narrow key range / heavy duplicates (quantized depth bunching)
    public abstract partial class BackendTestBase
    {
        // ═══════════════════════════════════════════════════════════
        //  Diagnostic: Global Inclusive Scan tests
        //  These isolate the multi-pass scan used internally by RadixSort
        //  on the counter array. Single-group scan (≤256 elements) should
        //  pass; multi-pass scan (>256 elements) may fail.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Global inclusive scan on 256 elements (all 1s). This fits in a single
        /// workgroup, so no multi-pass scan is needed. Expected: [1, 2, 3, ..., 256].
        /// </summary>
        [TestMethod]
        public async Task GlobalInclusiveScan256Test() => await RunTest(async accelerator =>
        {
            int n = 256;
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = 1;

            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(n);
            var tempSize = accelerator.ComputeScanTempStorageSize<int>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(
                ScanKind.Inclusive);
            scan(accelerator.DefaultStream, inputBuf.View, outputBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            int errors = 0;
            for (int i = 0; i < n; i++)
            {
                if (result[i] != i + 1)
                {
                    if (errors < 5)
                        Console.WriteLine($"  Scan[{i}] = {result[i]}, expected {i + 1}");
                    errors++;
                }
            }
            if (errors > 0)
                throw new Exception($"GlobalInclusiveScan256: {errors}/{n} wrong values");
        });

        /// <summary>
        /// Global inclusive scan on 320 elements (exceeds single workgroup of 256).
        /// This triggers the multi-pass scan path. If the multi-pass scan has a bug,
        /// this will fail while the 256-element test passes.
        /// </summary>
        [TestMethod]
        public async Task GlobalInclusiveScan320Test() => await RunTest(async accelerator =>
        {
            int n = 320;
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = 1;

            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(n);
            var tempSize = accelerator.ComputeScanTempStorageSize<int>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(
                ScanKind.Inclusive);
            scan(accelerator.DefaultStream, inputBuf.View, outputBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            int errors = 0;
            for (int i = 0; i < n; i++)
            {
                if (result[i] != i + 1)
                {
                    if (errors < 10)
                        Console.WriteLine($"  Scan[{i}] = {result[i]}, expected {i + 1}");
                    errors++;
                }
            }
            if (errors > 0)
                throw new Exception($"GlobalInclusiveScan320: {errors}/{n} wrong values. " +
                    $"Multi-pass scan threshold exceeded (>256 elements).");
        });

        /// <summary>
        /// Global inclusive scan on 8000 elements — the approximate counter array
        /// size for a 500K-element radix sort. If this fails, the multi-pass scan
        /// is the root cause of radix sort failures at scale.
        /// </summary>
        [TestMethod]
        public async Task GlobalInclusiveScan8000Test() => await RunTest(async accelerator =>
        {
            int n = 8000;
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = 1;

            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(n);
            var tempSize = accelerator.ComputeScanTempStorageSize<int>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(
                ScanKind.Inclusive);
            scan(accelerator.DefaultStream, inputBuf.View, outputBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            int errors = 0;
            int firstErrorIdx = -1;
            for (int i = 0; i < n; i++)
            {
                if (result[i] != i + 1)
                {
                    if (errors == 0) firstErrorIdx = i;
                    if (errors < 10)
                        Console.WriteLine($"  Scan[{i}] = {result[i]}, expected {i + 1}");
                    errors++;
                }
            }
            if (errors > 0)
                throw new Exception($"GlobalInclusiveScan8000: {errors}/{n} wrong values. " +
                    $"First error at [{firstErrorIdx}]: got {result[firstErrorIdx]}, expected {firstErrorIdx + 1}. " +
                    $"This confirms the multi-pass scan is broken.");
        });

        /// <summary>
        /// HIGH-TRIAL inclusive scan stress (2026-06-09, Geordi). Runs a large multi-tile scan
        /// MANY times with REUSED buffers + scan delegate (the real RadixSort reuse pattern), to
        /// catch the ~1.6% residual race in the SCAN dispatch specifically. The isolated scan kernel
        /// is CLEAN in a pure-Node repro (1024 tiles / 48 workers / ~1M yields, 0 fails) — so if THIS
        /// fails under real contention (TJ's Fallout76), the difference is the REAL worker pool / V8 /
        /// memory, isolating the bug to the scan-in-context. If this stays clean while the RadixSort
        /// canaries fail under the same contention, the scan is NOT the culprit (=> kernel1/kernel2/
        /// handoff). Run under contention: PMT_FILTER=GlobalInclusiveScanHighTrial.
        /// </summary>
        [TestMethod(Timeout = 600000)]
        public async Task GlobalInclusiveScanHighTrialTest() => await RunTest(async accelerator =>
        {
            const int n = 16384;       // 64 tiles at groupDim=256 — many cross-tile boundary carries
            const int iterations = 300;
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = 1; // all-1s => output[i] == i+1 (trivial CPU oracle)

            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(n);
            var tempSize = accelerator.ComputeScanTempStorageSize<int>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);
            var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(
                ScanKind.Inclusive);

            int badRuns = 0, totalMismatch = 0, firstBadIter = -1, firstBadIdx = -1, firstBadGot = 0;
            for (int it = 0; it < iterations; it++)
            {
                scan(accelerator.DefaultStream, inputBuf.View, outputBuf.View, tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();
                var result = await outputBuf.CopyToHostAsync<int>();
                int mism = 0;
                for (int i = 0; i < n; i++)
                {
                    if (result[i] != i + 1)
                    {
                        if (mism == 0 && badRuns == 0) { firstBadIter = it; firstBadIdx = i; firstBadGot = result[i]; }
                        mism++;
                    }
                }
                if (mism > 0) { badRuns++; totalMismatch += mism; }
            }
            Console.WriteLine($"===SCANHIGHTRIAL=== n={n} iters={iterations} badRuns={badRuns} totalMismatch={totalMismatch}");
            if (badRuns > 0)
                throw new Exception($"GlobalInclusiveScanHighTrial: {badRuns}/{iterations} runs corrupt, " +
                    $"{totalMismatch} total mismatches. First @iter {firstBadIter} idx {firstBadIdx}: got {firstBadGot}, expected {firstBadIdx + 1}. " +
                    $"=> the SCAN dispatch corrupts under contention (scan-in-context is the culprit).");
        });

        /// <summary>
        /// Inclusive scan at the EXACT counter array threshold: 4096 (single-tile) vs 4160 (multi-tile).
        /// Counter array = numVirtualGroups * 4. With gridDim=16, groupDim=256:
        ///   n=262144 → numVG=1024 → counter=4096 → scan numIterPerGroup=1 (PASS)
        ///   n=262145 → numVG=1040 → counter=4160 → scan numIterPerGroup=2 (FAIL?)
        /// Uses histogram-like data (small ints, many zeros) to mimic real counter arrays.
        /// </summary>
        [TestMethod]
        public async Task GlobalInclusiveScan4160Test() => await RunTest(async accelerator =>
        {
            // Use distinguishable per-group values: group g gets all (g+1)s.
            // Group 0: all 1s (sum=256), Group 1: all 2s (sum=512), etc.
            // Expected at group boundary g*256: sum of groups 0..g-1 + first element
            int n = 4096;
            var input = new int[n];
            for (int i = 0; i < n; i++)
                input[i] = (i / 256) + 1; // group 0=1, group 1=2, ...

            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(n);
            var tempSize = accelerator.ComputeScanTempStorageSize<int>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(
                ScanKind.Inclusive);
            scan(accelerator.DefaultStream, inputBuf.View, outputBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();

            // Print values at each group boundary (every 256 elements)
            string details = "";
            int runningSum = 0;
            int errors = 0;
            int firstErrorIdx = -1;
            for (int i = 0; i < n; i++)
            {
                runningSum += input[i];
                bool isGroupStart = (i % 256) == 0;
                if (isGroupStart)
                {
                    int grp = i / 256;
                    details += $"  Group {grp} start [{i}]: got={result[i]}, expected={runningSum}, diff={result[i] - runningSum}\n";
                }
                if (result[i] != runningSum)
                {
                    if (errors == 0) firstErrorIdx = i;
                    errors++;
                }
            }

            if (errors > 0)
                throw new Exception($"GlobalInclusiveScan n={n}: {errors}/{n} wrong values. " +
                    $"First error at [{firstErrorIdx}].\n" +
                    $"Group boundary values:\n{details}");
        });

        // ═══════════════════════════════════════════════════════════
        //  Diagnostic: RadixSort boundary tests
        //  16384 elements should use single-group counter scan (pass).
        //  20000 elements should use multi-pass counter scan (fail if scan is broken).
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// RadixSort with exactly 16384 elements. The counter array has 256 entries,
        /// which fits in a single workgroup. This should pass.
        /// </summary>
        [TestMethod]
        public async Task RadixSortBoundary16KTest() => await RunTest(async accelerator =>
        {
            int n = 16384;
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(42);
            for (int i = 0; i < n; i++) { keys[i] = rng.Next(0, 100000); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n, "RadixSortBoundary16K");
        });

        /// <summary>
        /// RadixSort with 20000 elements. The counter array has ~320 entries,
        /// exceeding the single-workgroup threshold. This triggers the multi-pass
        /// counter scan. If it fails while the 16K test passes, the multi-pass
        /// scan is confirmed as the root cause.
        /// </summary>
        [TestMethod]
        public async Task RadixSortBoundary20KTest() => await RunTest(async accelerator =>
        {
            int n = 20000;
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(42);
            for (int i = 0; i < n; i++) { keys[i] = rng.Next(0, 100000); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n, "RadixSortBoundary20K");
        });

        // ═══════════════════════════════════════════════════════════
        //  Diagnostic: Find exact radix sort failure threshold
        //  Tests sizes from 32K to 500K to pinpoint where it breaks.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Probes multiple radix sort sizes to find the exact threshold where
        /// the sort starts failing. Reports pass/fail for each size.
        /// Sizes: 32K, 50K, 65K, 100K, 128K, 200K, 256K, 500K.
        /// </summary>
        [TestMethod]
        public async Task RadixSortThresholdProbeTest() => await RunTest(async accelerator =>
        {
            // Alignment diagnostic: gridStride = 16 * 256 = 4096.
            // Sizes that are exact multiples of 4096 have paddedLength == numVirtualGroups * groupDim.
            // Non-multiples have paddedLength < numVirtualGroups * groupDim (some virtual groups idle).
            int[] sizes = { 262144, 262145, 266240, 266241, 270000 };
            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();

            string results = "";
            string firstFailure = null;

            foreach (int n in sizes)
            {
                var keys = new int[n];
                var values = new int[n];
                var rng = new Random(42);
                for (int i = 0; i < n; i++)
                {
                    keys[i] = rng.Next(0, 10_000_000);
                    values[i] = i;
                }

                using var keysBuf = accelerator.Allocate1D(keys);
                using var valuesBuf = accelerator.Allocate1D(values);
                var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
                using var tempBuf = accelerator.Allocate1D<int>(tempSize);

                radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();

                // GPU-accelerated verification — no CopyToHostAsync
                var (orderViolations, duplicates, outOfRange, _) =
                    await GpuTestVerify.VerifyDescendingSort(accelerator, keysBuf, valuesBuf, n);

                int violations = orderViolations;
                string status = violations == 0 ? "PASS" : $"FAIL({violations} order, {duplicates} dup, {outOfRange} OOB)";
                results += $"  n={n}: {status}\n";

                if (violations > 0 && firstFailure == null)
                    firstFailure = $"First failure at n={n}: {violations} order violations out of {n} elements";
            }

            if (firstFailure != null)
                throw new Exception($"RadixSortThresholdProbe: {firstFailure}\n\nFull results:\n{results}");
        });

        // ═══════════════════════════════════════════════════════════
        //  Helper: Verify RadixSortPairs result integrity
        // ═══════════════════════════════════════════════════════════

        private static void VerifyDescendingSortIntegrity(
            int[] sortedKeys, int[] sortedValues, int n, string testName)
        {
            // Check 1: keys are in descending order
            int orderViolations = 0;
            int firstViolationIdx = -1;
            for (int i = 1; i < n; i++)
            {
                if (sortedKeys[i] > sortedKeys[i - 1])
                {
                    if (orderViolations == 0) firstViolationIdx = i;
                    orderViolations++;
                }
            }
            if (orderViolations > 0)
                throw new Exception(
                    $"{testName}: Descending order violated {orderViolations} times out of {n} elements. " +
                    $"First violation at [{firstViolationIdx}]: {sortedKeys[firstViolationIdx - 1]} < {sortedKeys[firstViolationIdx]}");

            // Check 2: every original index appears exactly once
            var seen = new HashSet<int>(n);
            int dupes = 0, outOfRange = 0;
            foreach (var v in sortedValues)
            {
                if (v < 0 || v >= n) outOfRange++;
                else if (!seen.Add(v)) dupes++;
            }
            int missing = n - seen.Count;

            if (dupes > 0 || missing > 0 || outOfRange > 0)
                throw new Exception(
                    $"{testName}: Index integrity failure — {dupes} duplicates, {missing} missing, " +
                    $"{outOfRange} out-of-range out of {n} elements.");
        }

        // ═══════════════════════════════════════════════════════════
        //  Test 1: 1.4M elements — SpawnScene's exact splat count
        //  Descending sort with realistic quantized depth keys.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts 1,393,167 elements (SpawnScene's Gaussian splat count from a 5K
        /// source image) using DescendingInt32 RadixSortPairs. Keys simulate
        /// quantized camera-to-splat depth values in [0..10000000].
        /// Verifies descending order and full index integrity.
        /// </summary>
        [TestMethod(Timeout = 240000)]
        public async Task RadixSortDescending1_4MTest() => await RunTest(async accelerator =>
        {
            int n = 1_393_167; // SpawnScene exact count
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(42);

            // Simulate quantized depth: dist * 10000, range [0..10M]
            for (int i = 0; i < n; i++)
            {
                keys[i] = rng.Next(0, 10_000_000);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var originalKeysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n,
                "RadixSortDescending1.4M", originalKeysBuf: originalKeysBuf);
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 2: SpawnScene sentinel pattern — visible + culled splats
        //  Visible splats get depth >= 0, culled get int.MinValue.
        //  After descending sort: all visible (highest depth first),
        //  then all culled (int.MinValue) at the end.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Mimics SpawnScene's CullAndDistanceKernel output: ~70% of splats are
        /// visible with quantized depth in [0..65534], ~30% are culled with
        /// int.MinValue sentinel. After DescendingInt32 sort, all visible splats
        /// should appear first (highest depth to lowest), followed by all culled
        /// splats (int.MinValue). Verifies the boundary and index integrity.
        /// </summary>
        [TestMethod]
        public async Task RadixSortDescendingWithSentinelsTest() => await RunTest(async accelerator =>
        {
            int n = 1_393_167;
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(99);
            int cullCount = 0;

            // ~30% culled (int.MinValue), ~70% visible with depth in [0..65534]
            for (int i = 0; i < n; i++)
            {
                bool culled = rng.NextDouble() < 0.3;
                if (culled)
                {
                    keys[i] = int.MinValue;
                    cullCount++;
                }
                else
                {
                    // Quantized depth: scale=500, max=65534 (SpawnScene 16-bit mode)
                    keys[i] = rng.Next(0, 65535);
                }
                values[i] = i;
            }

            int visibleCount = n - cullCount;

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            // DIAGNOSTIC: Binary search for culprit pass
            // NumBits must be multiple of 4. Test: 4, 8, 12, ..., 32 (2, 4, 6, ..., 16 passes)
            string passResults = "";
            for (int testBits = 4; testBits <= 32; testBits += 4)
            {
                keysBuf.CopyFromCPU(keys);
                valuesBuf.CopyFromCPU(values);

                DescendingInt32Partial.ConfiguredNumBits = testBits;
                var partialSort = accelerator.CreateRadixSortPairs<
                    int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32Partial>();
                partialSort(
                    accelerator.DefaultStream,
                    keysBuf.View,
                    valuesBuf.View,
                    tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();

                // Verify partial sort: output should be sorted by lower testBits (descending)
                var gpuResult = await keysBuf.CopyToHostAsync();
                int partialViolations = 0;
                for (int pi = 1; pi < n; pi++)
                {
                    // Extract the sort key (lower testBits, descending = inverted)
                    int keyA = 0, keyB = 0;
                    for (int s = 0; s < testBits; s += 2)
                    {
                        keyA |= ((~((gpuResult[pi-1] ^ (1 << 31)) >> s)) & 3) << s;
                        keyB |= ((~((gpuResult[pi] ^ (1 << 31)) >> s)) & 3) << s;
                    }
                    if (keyB > keyA) partialViolations++;
                }
                passResults += $"\n  NumBits={testBits} ({testBits/2} passes): {partialViolations} partial-sort violations";

                if (partialViolations > 0) break;
            }

            // Run full sort for final verification
            keysBuf.CopyFromCPU(keys);
            valuesBuf.CopyFromCPU(values);
            var fullSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            fullSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n,
                "RadixSortSentinels (per-pass:" + passResults + ")");

            // GPU sentinel boundary check — no CopyToHostAsync
            int actualVisible = await GpuTestVerify.CountNonSentinel(accelerator, keysBuf, n);

            if (actualVisible != visibleCount)
                throw new Exception(
                    $"RadixSortSentinels: Visible/culled boundary wrong. " +
                    $"Expected {visibleCount} visible then {cullCount} culled, " +
                    $"but GPU counted {actualVisible} non-sentinel elements.");
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 3: Repeated re-sorts (camera movement simulation)
        //  SpawnScene re-sorts every frame as camera moves. Each frame
        //  generates new depth keys but reuses the same buffers.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Simulates 5 consecutive frames of camera movement: each frame writes
        /// new random depth keys into the key buffer and re-initializes the value
        /// buffer to identity, then re-sorts. Catches bugs where residual data
        /// from a previous sort corrupts the next (e.g., temp buffer state leak).
        /// Uses 500K elements for reasonable test time across 5 iterations.
        /// </summary>
        [TestMethod]
        public async Task RadixSortRepeatedResortTest() => await RunTest(async accelerator =>
        {
            int n = 500_000;
            var keys = new int[n];
            var values = new int[n];

            using var keysBuf = accelerator.Allocate1D<int>(n);
            using var valuesBuf = accelerator.Allocate1D<int>(n);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();

            // Simulate 5 "frames" with different camera positions
            for (int frame = 0; frame < 5; frame++)
            {
                var rng = new Random(frame * 1000 + 7);

                // Each frame: new depth keys, reset identity values
                for (int i = 0; i < n; i++)
                {
                    keys[i] = rng.Next(0, 10_000_000);
                    values[i] = i;
                }

                keysBuf.CopyFromCPU(keys);
                valuesBuf.CopyFromCPU(values);

                radixSort(
                    accelerator.DefaultStream,
                    keysBuf.View,
                    valuesBuf.View,
                    tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();

                await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n,
                    $"RadixSortResort_Frame{frame}");
            }
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 4: 2M elements — medium stress test
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts 2,097,152 (2M) elements descending. Tests the radix sort at a
        /// scale larger than SpawnScene's typical count but below the 4M failure
        /// threshold. Catches buffer sizing and workgroup dispatch issues.
        /// </summary>
        [TestMethod(Timeout = 240000)]
        public async Task RadixSortDescending2MTest() => await RunTest(async accelerator =>
        {
            int n = 2_097_152; // 2^21
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(314);

            for (int i = 0; i < n; i++)
            {
                keys[i] = rng.Next(0, 10_000_000);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n, "RadixSortDescending2M");
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 5: 4M elements — the failure boundary
        //  SpawnScene reports holes and random movement at this scale.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts 4,194,304 (4M) elements descending. This is the approximate scale
        /// where SpawnScene's Gaussian splat renderer shows holes and random splat
        /// movement when the camera moves. If this test fails, the radix sort has
        /// a data corruption bug at this buffer size (likely workgroup dispatch
        /// overflow, temp buffer underallocation, or histogram aliasing).
        /// </summary>
        [TestMethod(Timeout = 240000)]
        public async Task RadixSortDescending4MTest() => await RunTest(async accelerator =>
        {
            int n = 4_194_304; // 2^22
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(271);

            for (int i = 0; i < n; i++)
            {
                keys[i] = rng.Next(0, 10_000_000);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var originalKeysBuf = accelerator.Allocate1D(keys); // preserve for tracking check
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            // GPU verification — data stays on GPU, CPU reads back only violation counts
            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n,
                "RadixSortDescending4M", originalKeysBuf: originalKeysBuf);
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 6: Heavy duplicate keys (quantized depth bunching)
        //  SpawnScene's depth quantization produces many ties when
        //  splats are at similar distances from the camera.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts 1M elements where keys are drawn from a narrow range [0..1000],
        /// creating ~1000 duplicates per key on average. This mimics the worst case
        /// in SpawnScene where many splats share the same quantized depth (e.g.,
        /// a flat wall viewed head-on). Sort must not lose or duplicate any index.
        /// </summary>
        [TestMethod]
        public async Task RadixSortHeavyDuplicateKeysTest() => await RunTest(async accelerator =>
        {
            int n = 1_000_000;
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(137);

            // Only 1000 distinct key values — extreme duplication
            for (int i = 0; i < n; i++)
            {
                keys[i] = rng.Next(0, 1000);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n, "RadixSortHeavyDuplicates");
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 7: Non-power-of-2 large count (odd element count)
        //  Catches off-by-one bugs in workgroup tail handling.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts 1,500,001 elements (odd, non-power-of-2) descending. Radix sort
        /// kernels process data in workgroup-sized chunks; the last workgroup often
        /// has fewer elements than the group size. This test catches off-by-one
        /// errors in tail handling that could corrupt the last few elements.
        /// </summary>
        [TestMethod]
        public async Task RadixSortDescendingOddCountTest() => await RunTest(async accelerator =>
        {
            int n = 1_500_001; // Prime-ish, non-power-of-2
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(577);

            for (int i = 0; i < n; i++)
            {
                keys[i] = rng.Next(0, 10_000_000);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n, "RadixSortDescendingOddCount");
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 8: Ascending 1.4M (control test)
        //  Same scale as Test 1 but ascending, to isolate descending-
        //  specific bugs from general large-scale issues.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Sorts 1,393,167 elements ascending. If this passes but the descending
        /// test fails, the bug is in the DescendingInt32 bit-flip logic rather
        /// than in the general radix sort infrastructure.
        /// </summary>
        [TestMethod(Timeout = 240000)]
        public async Task RadixSortAscending1_4MTest() => await RunTest(async accelerator =>
        {
            int n = 1_393_167;
            var keys = new int[n];
            var values = new int[n];
            var rng = new Random(42);

            for (int i = 0; i < n; i++)
            {
                keys[i] = rng.Next(0, 10_000_000);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, AscendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, AscendingInt32>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            // GPU-accelerated verification — no CopyToHostAsync
            var (orderViolations, dupes, outOfRange, _) =
                await GpuTestVerify.VerifyAscendingSort(accelerator, keysBuf, valuesBuf, n);

            if (orderViolations > 0)
                throw new Exception(
                    $"RadixSortAscending1.4M: Ascending order violated {orderViolations} times out of {n}");
            if (dupes > 0 || outOfRange > 0)
                throw new Exception(
                    $"RadixSortAscending1.4M: {dupes} duplicates, {outOfRange} out-of-range");
        });

        // ═══════════════════════════════════════════════════════════
        //  Test 9: Full SpawnScene simulation — cull + sort + verify
        //  Combines sentinel pattern with repeated re-sorts and
        //  varying cull ratios (camera panning changes visibility).
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Full SpawnScene frame simulation: 1.4M splats, 3 frames where each
        /// frame has a different cull ratio (simulating camera pan from looking
        /// at a wall to looking at open sky). Each frame re-writes keys with
        /// new depth values and sentinel patterns, then re-sorts. Verifies order,
        /// index integrity, and correct sentinel boundary after each frame.
        /// </summary>
        [TestMethod(Timeout = 300000)]
        public async Task RadixSortSpawnSceneSimulationTest() => await RunTest(async accelerator =>
        {
            int n = 1_393_167;
            var keys = new int[n];
            var values = new int[n];

            using var keysBuf = accelerator.Allocate1D<int>(n);
            using var valuesBuf = accelerator.Allocate1D<int>(n);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, DescendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<
                int, Stride1D.Dense, int, Stride1D.Dense, DescendingInt32>();

            // Frame 0: 10% culled (most splats visible — looking at dense scene)
            // Frame 1: 50% culled (half visible — camera turned sideways)
            // Frame 2: 90% culled (camera looking at sky, few splats in view)
            double[] cullRatios = { 0.10, 0.50, 0.90 };

            for (int frame = 0; frame < cullRatios.Length; frame++)
            {
                var rng = new Random(frame * 7919 + 13);
                double cullRatio = cullRatios[frame];
                int expectedCulled = 0;

                for (int i = 0; i < n; i++)
                {
                    if (rng.NextDouble() < cullRatio)
                    {
                        keys[i] = int.MinValue;
                        expectedCulled++;
                    }
                    else
                    {
                        keys[i] = rng.Next(0, 10_000_000);
                    }
                    values[i] = i;
                }

                int expectedVisible = n - expectedCulled;

                keysBuf.CopyFromCPU(keys);
                valuesBuf.CopyFromCPU(values);

                radixSort(
                    accelerator.DefaultStream,
                    keysBuf.View,
                    valuesBuf.View,
                    tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();

                // GPU verification for sort order + index integrity
                await VerifyDescendingSortOnGpu(accelerator, keysBuf, valuesBuf, n,
                    $"SpawnSceneSim_Frame{frame}(cull={cullRatio:P0})");

                // GPU sentinel boundary check — no CopyToHostAsync
                int actualVisible = await GpuTestVerify.CountNonSentinel(accelerator, keysBuf, n);

                if (actualVisible != expectedVisible)
                    throw new Exception(
                        $"SpawnSceneSim_Frame{frame}: Sentinel boundary mismatch. " +
                        $"Expected {expectedVisible} visible, GPU counted {actualVisible}.");
            }
        });
        // ═══════════════════════════════════════════════════════════
        //  Diagnostic: Isolate pass 2 of multi-pass scan
        //  Tests inclusive scan + Group.Broadcast with known boundary
        //  values to determine if the bug is in pass 1 or pass 2.
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Directly tests the logic of MultiPassScanKernel2's boundary scan:
        /// loads known right boundary values, does group inclusive scan, then
        /// Group.Broadcast from Grid.IdxX. If this test fails, the bug is in
        /// the group scan or broadcast. If it passes, the bug is in pass 1.
        /// Uses 16 workgroups of 256 threads with 17 boundary values.
        /// </summary>
        [TestMethod]
        public async Task ScanBroadcastIsolationTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(256, accelerator.MaxNumThreadsPerGroup);
            int numGroups = 16;
            int numBoundaries = numGroups + 1; // 17
            var boundaries = new int[numBoundaries];
            for (int g = 0; g < numGroups; g++)
                boundaries[g + 1] = groupSize * (g + 1);

            using var boundaryBuf = accelerator.Allocate1D(boundaries);
            using var outputBuf = accelerator.Allocate1D<int>(numGroups);

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(
                ScanBroadcastIsolationKernel);
            kernel(new KernelConfig(numGroups, groupSize), boundaryBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();

            // Expected left boundaries: inclusive scan of boundaries, picked at Grid.IdxX
            // scan[0]=0, scan[1]=256, scan[2]=768, scan[3]=1536, scan[4]=2560, ...
            int[] expectedScan = new int[numBoundaries];
            for (int i = 1; i < numBoundaries; i++)
                expectedScan[i] = expectedScan[i - 1] + boundaries[i];

            string details = "";
            int errors = 0;
            for (int g = 0; g < numGroups; g++)
            {
                int expected = expectedScan[g];
                int actual = result[g];
                if (actual != expected)
                {
                    details += $"  Group {g}: got={actual}, expected={expected}, diff={actual - expected}\n";
                    errors++;
                }
            }

            if (errors > 0)
                throw new Exception(
                    $"ScanBroadcastIsolation: {errors}/{numGroups} wrong left boundaries.\n" +
                    $"Bug is in pass 2 (group scan + broadcast).\n{details}");

        });

        /// <summary>
        /// Tests AllReduce per group: 16 groups each reduce 256 elements.
        /// Input[i] = (i/256)+1 (group g has all g+1 values).
        /// Expected AllReduce result for group g = 256*(g+1).
        /// If this fails, the bug is in AllReduce (pass 1's right boundary computation).
        /// </summary>
        [TestMethod]
        public async Task AllReducePerGroupDiagTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(256, accelerator.MaxNumThreadsPerGroup);
            int numGroups = 16;
            int n = groupSize * numGroups;
            var input = new int[n];
            for (int i = 0; i < n; i++)
                input[i] = (i / groupSize) + 1;

            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(numGroups);

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, int>(
                AllReducePerGroupDiagKernel);
            kernel(new KernelConfig(numGroups, groupSize), inputBuf.View, outputBuf.View, n);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();

            string details = "";
            int errors = 0;
            for (int g = 0; g < numGroups; g++)
            {
                int expected = groupSize * (g + 1);
                int actual = result[g];
                if (actual != expected)
                {
                    details += $"  Group {g}: AllReduce={actual}, expected={expected}, diff={actual - expected}\n";
                    errors++;
                }
            }

            if (errors > 0)
                throw new Exception(
                    $"AllReducePerGroupDiag: {errors}/{numGroups} wrong reductions.\n" +
                    $"Bug is in AllReduce (pass 1).\n{details}");
        });

        /// <summary>
        /// Tests bare Group.Broadcast: each of 16 workgroups broadcasts from
        /// thread Grid.IdxX. Verifies correct value selection.
        /// </summary>
        [TestMethod]
        public async Task GroupBroadcastDiagTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(256, accelerator.MaxNumThreadsPerGroup);
            int numGroups = 16;
            using var outputBuf = accelerator.Allocate1D<int>(numGroups);

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>>(
                GroupBroadcastDiagKernel);
            kernel(new KernelConfig(numGroups, groupSize), outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();

            string details = "";
            int errors = 0;
            for (int g = 0; g < numGroups; g++)
            {
                // Each thread has value Group.IdxX * 100 + 7.
                // Broadcast from thread Grid.IdxX = g.
                // Expected: g * 100 + 7.
                int expected = g * 100 + 7;
                int actual = result[g];
                if (actual != expected)
                {
                    details += $"  Group {g}: got={actual}, expected={expected}\n";
                    errors++;
                }
            }

            if (errors > 0)
                throw new Exception(
                    $"GroupBroadcastDiag: {errors}/{numGroups} wrong broadcasts.\n{details}");
        });

        /// <summary>
        /// Full pass 2 simulation: boundary scan + broadcast + tile scan.
        /// This kernel calls InclusiveScan TWICE (like MultiPassScanKernel2 does):
        /// once for the boundary scan, once for the tile scan. If this fails while
        /// ScanBroadcastIsolationTest passes, the bug is in having two InclusiveScan
        /// calls in the same kernel (shared memory aliasing or phi value conflict).
        /// </summary>
        [TestMethod]
        public async Task DualScanKernelTest() => await RunTest(async accelerator =>
        {
            int numGroups = 16;
            int groupSize = Math.Min(256, accelerator.MaxNumThreadsPerGroup);
            int n = numGroups * groupSize;

            int numBoundaries = numGroups + 1;
            var boundaries = new int[numBoundaries];
            for (int g = 0; g < numGroups; g++)
                boundaries[g + 1] = groupSize * (g + 1);

            var input = new int[n];
            for (int i = 0; i < n; i++)
                input[i] = (i / groupSize) + 1;

            using var boundaryBuf = accelerator.Allocate1D(boundaries);
            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(n);

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                DualScanKernel);
            kernel(new KernelConfig(numGroups, groupSize), boundaryBuf.View, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();

            // Verify output: should be the inclusive scan of input with boundary offsets
            string details = "";
            int errors = 0;
            int runningSum = 0;
            for (int i = 0; i < n; i++)
            {
                runningSum += input[i];
                if (result[i] != runningSum)
                {
                    if (errors < 20)
                    {
                        int grp = i / groupSize;
                        details += $"  [{i}] (group {grp}): got={result[i]}, expected={runningSum}, diff={result[i] - runningSum}\n";
                    }
                    errors++;
                }
            }

            // Print group boundary summary
            string boundaryDetails = "";
            int rSum = 0;
            for (int i = 0; i < n; i++)
            {
                rSum += input[i];
                if (i % groupSize == 0)
                {
                    int grp = i / groupSize;
                    boundaryDetails += $"  Group {grp} [{i}]: got={result[i]}, expected={rSum}, diff={result[i] - rSum}\n";
                }
            }
            if (errors > 0)
                throw new Exception(
                    $"DualScanKernel: {errors}/{n} wrong values.\n" +
                    $"Two InclusiveScan calls in same kernel produce wrong results.\n" +
                    $"Group boundaries:\n{boundaryDetails}\n" +
                    $"First errors:\n{details}");
        });

        /// <summary>
        /// Same as DualScanKernelTest but tests the fence pathway:
        /// uses two SEPARATE kernel dispatches (like the actual multi-pass scan)
        /// instead of a single kernel. If this passes while DualScanKernelTest fails,
        /// the bug is confirmed as being in the single kernel with two InclusiveScan calls.
        /// If this also fails, the bug is in the fence or buffer handling.
        /// </summary>
        [TestMethod]
        public async Task TwoPassScanSimulationTest() => await RunTest(async accelerator =>
        {
            int numGroups = 16;
            int groupSize = Math.Min(256, accelerator.MaxNumThreadsPerGroup);
            int n = numGroups * groupSize;

            var input = new int[n];
            for (int i = 0; i < n; i++)
                input[i] = (i / groupSize) + 1;

            using var inputBuf = accelerator.Allocate1D(input);
            using var outputBuf = accelerator.Allocate1D<int>(n);

            // Manually do what CreateMultiPassScan does:
            // 1. Allocate temp buffer for boundaries
            int numBoundaries = numGroups + 1; // 17
            using var tempBuf = accelerator.Allocate1D<int>(numBoundaries);

            // Initialize tempBuf to 0
            var zeros = new int[numBoundaries];
            tempBuf.CopyFromCPU(zeros);

            // Pass 1: compute right boundaries
            var pass1Kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, int>(
                AllReducePerGroupDiagKernel);
            // Write to tempBuf[1..16] (offset by 1 for inclusive scan)
            pass1Kernel(new KernelConfig(numGroups, groupSize),
                inputBuf.View, tempBuf.View.SubView(1, numGroups), n);
            await accelerator.SynchronizeAsync();

            // Fence: copy + sync (same as actual multi-pass scan)
            using var fenceBuf = accelerator.Allocate1D<int>(numBoundaries);
            fenceBuf.View.CopyFrom(accelerator.DefaultStream, tempBuf.View);
            accelerator.Synchronize();

            // Pass 2: scan boundaries + apply to tile scan
            var pass2Kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                DualScanKernel);
            pass2Kernel(new KernelConfig(numGroups, groupSize),
                tempBuf.View, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();

            // Verify
            string details = "";
            int errors = 0;
            int runningSum = 0;
            for (int i = 0; i < n; i++)
            {
                runningSum += input[i];
                if (result[i] != runningSum)
                {
                    if (errors < 20)
                    {
                        int grp = i / groupSize;
                        details += $"  [{i}] (group {grp}): got={result[i]}, expected={runningSum}, diff={result[i] - runningSum}\n";
                    }
                    errors++;
                }
            }

            string boundaryDetails = "";
            int rSum = 0;
            for (int i = 0; i < n; i++)
            {
                rSum += input[i];
                if (i % groupSize == 0)
                {
                    int grp = i / groupSize;
                    boundaryDetails += $"  Group {grp} [{i}]: got={result[i]}, expected={rSum}, diff={result[i] - rSum}\n";
                }
            }
            if (errors > 0)
                throw new Exception(
                    $"TwoPassScanSimulation: {errors}/{n} wrong values.\n" +
                    $"Group boundaries:\n{boundaryDetails}\n" +
                    $"First errors:\n{details}");
        });

        /// <summary>
        /// HIGH-TRIAL cross-group shared-reuse detector for the residual Wasm large-sort race.
        /// The residual only reproduces under full-sweep accumulation, and the 16-group
        /// diagnostics above fire at most once per sweep (16 cross-group-reuse trials/run).
        /// This runs the SAME production scan + broadcast path RadixSort uses, but at
        /// 4M-scale group count (16384 groups) so a single dispatch exercises thousands of
        /// cross-group shared-memory REUSES — the exact pressure the failing 4M sort applies.
        ///
        /// Per group g, every thread feeds value v=(g%251)+1 into an inclusive scan; thread i
        /// must observe scanned == v*(i+1). A stale cross-worker read of another worker's
        /// shared write (the scan's unprotected `sharedMemory[i]` / `scanResults[..]` loads)
        /// yields a wrong partial → counted in errorCount[0]. The boundary value is read via
        /// Group.Broadcast (tag-hardened in 4.9.10) → counted separately in errorCount[1], so
        /// scanReadErrors>0 with broadcastErrors==0 localizes the residual to the UNPROTECTED
        /// scan read, not the broadcast. Self-verifying (no CPU reference array); passes
        /// silently when clean so it is safe to leave in the suite as a regression guard.
        /// </summary>
        [TestMethod]
        public async Task CrossGroupScanReuseDetectorTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(256, accelerator.MaxNumThreadsPerGroup);
            int numGroups = 16384; // 4M-scale cross-group reuse pressure (matches failing 4M sort)

            // [0] = scan-read corruptions, [1] = Group.Broadcast corruptions,
            // [2] = direct shared-boundary-read corruptions (radix line-747 pattern)
            using var errorBuf = accelerator.Allocate1D(new int[3]);

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>>(
                CrossGroupScanReuseDetectorKernel);
            kernel(new KernelConfig(numGroups, groupSize), errorBuf.View);
            await accelerator.SynchronizeAsync();

            var errors = await errorBuf.CopyToHostAsync<int>();
            if (errors[0] > 0 || errors[1] > 0 || errors[2] > 0)
                throw new Exception(
                    $"CrossGroupScanReuseDetector: scanReadErrors={errors[0]}, broadcastErrors={errors[1]}, " +
                    $"directBoundaryErrors={errors[2]} across {numGroups} groups of {groupSize}.\n" +
                    $"scanReadErrors>0 => stale unprotected scan cross-worker read.\n" +
                    $"directBoundaryErrors>0 => stale direct shared boundary read (the radix line-747 pattern, " +
                    $"NOT covered by the Group.Broadcast tag handshake).\n" +
                    $"broadcastErrors>0 => the tag-hardened broadcast itself is still racing (deeper V8 issue).");
        });

        /// <summary>
        /// Companion to CrossGroupScanReuseDetectorTest covering the OTHER live residual
        /// hypothesis: a loop-carried local being lost across a phase-barrier yield INSIDE a
        /// loop (the radix presort runs its scan + 4 barriers inside a grid-stride loop —
        /// RadixSortExtensions.cs:700-757). A scan barrier is a yield point in phase mode; if
        /// EmitSaveAllLocals drops a loop-carried value across the yield, the accumulator
        /// corrupts even when every individual read is fresh. This kernel loops the production
        /// InclusiveScan ITERS times (=> ITERS*2 in-loop yields) carrying an accumulator that
        /// must survive them, and self-verifies BOTH per-iteration scan freshness [0] and the
        /// loop-carried accumulator [1]. Runs at 4M-group scale for cross-group reuse pressure.
        /// </summary>
        [TestMethod]
        public async Task GridStrideScanStateDetectorTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(256, accelerator.MaxNumThreadsPerGroup);
            // Hypothesis B needs ITERATIONS (in-loop yields), not group count. Keep groups modest
            // so the 64-iteration in-kernel loop (128 in-loop barrier syncs/group) stays well under
            // the 30s PMT cap even under FO76 contention — 16384 groups timed out and cascaded.
            int numGroups = 256;
            using var errorBuf = accelerator.Allocate1D(new int[2]);

            var kernel = accelerator.LoadStreamKernel<ArrayView<int>>(
                GridStrideScanStateDetectorKernel);
            kernel(new KernelConfig(numGroups, groupSize), errorBuf.View);
            await accelerator.SynchronizeAsync();

            var errors = await errorBuf.CopyToHostAsync<int>();
            if (errors[0] > 0 || errors[1] > 0)
                throw new Exception(
                    $"GridStrideScanStateDetector: scanFreshnessErrors={errors[0]}, " +
                    $"loopCarriedStateErrors={errors[1]} across {numGroups} groups of {groupSize}.\n" +
                    $"scanFreshnessErrors>0 => a per-iteration scan cross-worker read was stale.\n" +
                    $"loopCarriedStateErrors>0 => an accumulator was corrupted across an in-loop barrier yield " +
                    $"(EmitSaveAllLocals state save/restore gap — distinct from a stale read).");
        });

        #region Tests9 Diagnostic Kernel Methods

        /// <summary>
        /// Kernel that mimics MultiPassScanKernel2's boundary scan + broadcast.
        /// Loads rightBoundaries[Group.IdxX], does inclusive scan, broadcasts from Grid.IdxX.
        /// </summary>
        /// <summary>
        /// Per-group self-verifying scan + broadcast. value v=(g%251)+1 (distinct between
        /// adjacent groups, nonzero, no int overflow at *256). Inclusive scan: thread i must
        /// see v*(i+1). Boundary broadcast from last thread must be v*DimX. Any mismatch is a
        /// stale cross-worker read of reused shared memory; counted atomically per error class.
        /// </summary>
        static void CrossGroupScanReuseDetectorKernel(ArrayView<int> errorCount)
        {
            int g = Grid.IdxX;
            int v = (g % 251) + 1;
            int scanned = GroupExtensions.InclusiveScan<int, AddInt32>(v);
            int expectedScan = v * (Group.IdxX + 1);
            if (scanned != expectedScan)
                Atomic.Add(ref errorCount[0], 1); // scan cross-worker read stale

            // [1] Boundary via Group.Broadcast (tag-hardened in 4.9.10).
            int boundary = Group.Broadcast(scanned, Group.DimX - 1);
            int expectedBoundary = v * Group.DimX;
            if (boundary != expectedBoundary)
                Atomic.Add(ref errorCount[1], 1);

            // [2] Boundary via a DIRECT cross-worker shared read — the UNPROTECTED pattern
            // RadixSortExtensions.cs:747 uses (`scanMemory[groupSize*j - 1]`), which is NOT a
            // Group.Broadcast and is NOT covered by the tag handshake. Size 300 (not 256) to
            // avoid same-size alloca dedup with the scan's internal 256-slot workspace.
            var sh = SharedMemory.Allocate<int>(300);
            sh[Group.IdxX] = scanned;
            Group.Barrier();
            int directBoundary = sh[Group.DimX - 1]; // last thread's value, read by all (cross-worker)
            if (directBoundary != expectedBoundary)
                Atomic.Add(ref errorCount[2], 1);
            Group.Barrier(); // guard sh against reuse before all threads have read
        }

        /// <summary>
        /// Loops the production InclusiveScan ITERS times carrying an accumulator across the
        /// scan's in-loop barrier yields. Per iteration k (uniform across the group) thread i
        /// must see scanned == k*(i+1); acc must end at (i+1)*ITERS*(ITERS+1)/2. A dropped
        /// loop-carried local across a yield corrupts acc without any stale read.
        /// </summary>
        static void GridStrideScanStateDetectorKernel(ArrayView<int> errorCount)
        {
            const int iters = 64; // ITERS*2 in-loop yields; max k*(IdxX+1)=64*256, acc<=532480 (no overflow)
            int acc = 0;
            for (int k = 1; k <= iters; k++)
            {
                int scanned = GroupExtensions.InclusiveScan<int, AddInt32>(k);
                if (scanned != k * (Group.IdxX + 1))
                    Atomic.Add(ref errorCount[0], 1);
                acc += scanned;
            }
            int expectedAcc = (Group.IdxX + 1) * (iters * (iters + 1) / 2);
            if (acc != expectedAcc)
                Atomic.Add(ref errorCount[1], 1);
        }

        static void ScanBroadcastIsolationKernel(
            ArrayView<int> rightBoundaries,
            ArrayView<int> output)
        {
            int localRB = Group.IdxX < rightBoundaries.Length
                ? rightBoundaries[Group.IdxX]
                : 0;

            int scanned = GroupExtensions.InclusiveScan<int, AddInt32>(localRB);
            int leftBoundary = Group.Broadcast(scanned, Grid.IdxX);

            if (Group.IsFirstThread)
                output[Grid.IdxX] = leftBoundary;
        }

        /// <summary>
        /// Kernel that computes AllReduce per group, mimicking pass 1 of multi-pass scan.
        /// </summary>
        static void AllReducePerGroupDiagKernel(
            ArrayView<int> input,
            ArrayView<int> output,
            int n)
        {
            int tid = Grid.IdxX * Group.DimX + Group.IdxX;
            int value = tid < n ? input[tid] : 0;
            int reduced = GroupExtensions.AllReduce<int, AddInt32>(value);

            if (Group.IsFirstThread)
                output[Grid.IdxX] = reduced;
        }

        /// <summary>
        /// Kernel that tests Group.Broadcast in isolation.
        /// Each thread has value (Group.IdxX * 100 + 7).
        /// Each workgroup broadcasts from thread Grid.IdxX.
        /// </summary>
        static void GroupBroadcastDiagKernel(
            ArrayView<int> output)
        {
            int myValue = Group.IdxX * 100 + 7;
            int broadcastValue = Group.Broadcast(myValue, Grid.IdxX);

            if (Group.IsFirstThread)
                output[Grid.IdxX] = broadcastValue;
        }

        /// <summary>
        /// Kernel that calls InclusiveScan TWICE in the same kernel — mimicking
        /// MultiPassScanKernel2's behavior:
        /// 1. Scan boundary values, broadcast to get leftBoundary
        /// 2. Scan tile data from input, add leftBoundary to output
        /// If two InclusiveScan calls in the same kernel cause shared memory or
        /// phi value conflicts, this will produce wrong results.
        /// </summary>
        static void DualScanKernel(
            ArrayView<int> rightBoundaries,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            // Step 1: Boundary scan (same as ScanBroadcastIsolationKernel)
            int localRB = Group.IdxX < rightBoundaries.Length
                ? rightBoundaries[Group.IdxX]
                : 0;

            int scanned = GroupExtensions.InclusiveScan<int, AddInt32>(localRB);
            int leftBoundary = Group.Broadcast(scanned, Grid.IdxX);

            // Step 2: Tile scan (InclusiveScan called SECOND time in same kernel)
            int tileStart = Grid.IdxX * Group.DimX;
            int tid = tileStart + Group.IdxX;
            int inputValue = tid < input.Length ? input[tid] : 0;

            int tileScan = GroupExtensions.InclusiveScan<int, AddInt32>(inputValue);

            // Output = leftBoundary + tileScan
            if (tid < output.Length)
                output[tid] = leftBoundary + tileScan;
        }

        #endregion
    }
}
