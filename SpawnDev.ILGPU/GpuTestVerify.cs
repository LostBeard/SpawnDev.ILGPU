using ILGPU;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

/// <summary>
/// GPU-based test verification utilities. Runs verification kernels on the GPU
/// so data never transfers to CPU — only small violation counts are read back.
///
/// Usage:
///   await GpuTestVerify.VerifyDescendingSort(accelerator, keysBuf, valuesBuf, n, "MyTest");
///   var (mean, max) = await GpuTestVerify.CompareBuffers(accelerator, actual, expected);
/// </summary>
public static class GpuTestVerify
{
    // ═══════════════════════════════════════════════════════════
    //  Integer / Sort Verification Kernels
    // ═══════════════════════════════════════════════════════════

    static void VerifyDescendingOrderKernel(
        Index1D index, ArrayView<int> keys, ArrayView<int> violations, int n)
    {
        if (index > 0 && index < n && keys[index] > keys[index - 1])
            Atomic.Add(ref violations[0], 1);
    }

    static void VerifyAscendingOrderKernel(
        Index1D index, ArrayView<int> keys, ArrayView<int> violations, int n)
    {
        if (index > 0 && index < n && keys[index] < keys[index - 1])
            Atomic.Add(ref violations[0], 1);
    }

    static void VerifyIndexIntegrityKernel(
        Index1D index, ArrayView<int> values, ArrayView<int> seen,
        ArrayView<int> violations, int n)
    {
        int v = values[index];
        if (v < 0 || v >= n)
        {
            Atomic.Add(ref violations[0], 1); // out of range
        }
        else
        {
            int prev = Atomic.Exchange(ref seen[v], 1);
            if (prev != 0)
                Atomic.Add(ref violations[1], 1); // duplicate
        }
    }

    static void VerifyKeyValueTrackingKernel(
        Index1D index, ArrayView<int> sortedKeys, ArrayView<int> sortedValues,
        ArrayView<int> originalKeys, ArrayView<int> violations, int n)
    {
        if (index < n)
        {
            int origIdx = sortedValues[index];
            if (origIdx >= 0 && origIdx < n)
            {
                if (sortedKeys[index] != originalKeys[origIdx])
                    Atomic.Add(ref violations[0], 1);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Float Comparison Kernel (from Riker's ML pattern)
    // ═══════════════════════════════════════════════════════════

    static void CompareFloatKernel(
        Index1D index, ArrayView<float> actual, ArrayView<float> expected,
        ArrayView<float> results, int n)
    {
        if (index < n)
        {
            float diff = actual[index] - expected[index];
            float absDiff = diff < 0f ? -diff : diff;
            Atomic.Add(ref results[0], absDiff);
            Atomic.Max(ref results[1], absDiff);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  Public API
    // ═══════════════════════════════════════════════════════════

    // WebGL has no atomics, so the GPU verify kernels (Atomic.Add/Exchange) can't run there.
    // Compute the same (orderViolations, duplicates, outOfRange, trackingErrors) tuple on the CPU.
    // The sort under test still runs entirely on the GPU; only verification reads back.
    static async Task<(int orderViolations, int duplicates, int outOfRange, int trackingErrors)>
        VerifySortOnCpuAsync(
            MemoryBuffer1D<int, Stride1D.Dense> keysBuf,
            MemoryBuffer1D<int, Stride1D.Dense> valuesBuf,
            int n,
            MemoryBuffer1D<int, Stride1D.Dense>? originalKeysBuf,
            bool descending)
    {
        var keys = await keysBuf.CopyToHostAsync<int>();
        var values = await valuesBuf.CopyToHostAsync<int>();
        int order = 0, dup = 0, oor = 0, track = 0;
        for (int i = 1; i < n; i++)
            if (descending ? keys[i] > keys[i - 1] : keys[i] < keys[i - 1]) order++;
        var seen = new bool[n];
        for (int i = 0; i < n; i++)
        {
            int v = values[i];
            if (v < 0 || v >= n) oor++;
            else { if (seen[v]) dup++; seen[v] = true; }
        }
        if (originalKeysBuf != null)
        {
            var orig = await originalKeysBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int oi = values[i];
                if (oi >= 0 && oi < n && keys[i] != orig[oi]) track++;
            }
        }
        return (order, dup, oor, track);
    }

    /// <summary>
    /// Verify descending sort order and index integrity on GPU.
    /// Returns (orderViolations, duplicates, outOfRange, trackingErrors).
    /// </summary>
    public static async Task<(int orderViolations, int duplicates, int outOfRange, int trackingErrors)>
        VerifyDescendingSort(
            Accelerator accelerator,
            MemoryBuffer1D<int, Stride1D.Dense> keysBuf,
            MemoryBuffer1D<int, Stride1D.Dense> valuesBuf,
            int n,
            MemoryBuffer1D<int, Stride1D.Dense>? originalKeysBuf = null)
    {
        if (accelerator.AcceleratorType == AcceleratorType.WebGL)
            return await VerifySortOnCpuAsync(keysBuf, valuesBuf, n, originalKeysBuf, descending: true);

        using var orderViolations = accelerator.Allocate1D(new int[] { 0 });
        using var integrityViolations = accelerator.Allocate1D(new int[] { 0, 0 });
        using var seenBuf = accelerator.Allocate1D<int>(n);
        seenBuf.MemSetToZero();

        var orderKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<int>, ArrayView<int>, int>(VerifyDescendingOrderKernel);
        orderKernel((Index1D)n, keysBuf.View, orderViolations.View, n);

        var integrityKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, int>(VerifyIndexIntegrityKernel);
        integrityKernel((Index1D)n, valuesBuf.View, seenBuf.View, integrityViolations.View, n);

        await accelerator.SynchronizeAsync();

        var orderResult = await orderViolations.CopyToHostAsync<int>();
        var integrityResult = await integrityViolations.CopyToHostAsync<int>();

        int trackingErrors = 0;
        if (originalKeysBuf != null)
        {
            using var trackingViolations = accelerator.Allocate1D(new int[] { 0 });
            var trackingKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>, int>(
                VerifyKeyValueTrackingKernel);
            trackingKernel((Index1D)n, keysBuf.View, valuesBuf.View,
                originalKeysBuf.View, trackingViolations.View, n);
            await accelerator.SynchronizeAsync();
            var trackResult = await trackingViolations.CopyToHostAsync<int>();
            trackingErrors = trackResult[0];
        }

        return (orderResult[0], integrityResult[1], integrityResult[0], trackingErrors);
    }

    /// <summary>
    /// Verify ascending sort order and index integrity on GPU.
    /// Returns (orderViolations, duplicates, outOfRange, trackingErrors).
    /// </summary>
    public static async Task<(int orderViolations, int duplicates, int outOfRange, int trackingErrors)>
        VerifyAscendingSort(
            Accelerator accelerator,
            MemoryBuffer1D<int, Stride1D.Dense> keysBuf,
            MemoryBuffer1D<int, Stride1D.Dense> valuesBuf,
            int n,
            MemoryBuffer1D<int, Stride1D.Dense>? originalKeysBuf = null)
    {
        if (accelerator.AcceleratorType == AcceleratorType.WebGL)
            return await VerifySortOnCpuAsync(keysBuf, valuesBuf, n, originalKeysBuf, descending: false);

        using var orderViolations = accelerator.Allocate1D(new int[] { 0 });
        using var integrityViolations = accelerator.Allocate1D(new int[] { 0, 0 });
        using var seenBuf = accelerator.Allocate1D<int>(n);
        seenBuf.MemSetToZero();

        var orderKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<int>, ArrayView<int>, int>(VerifyAscendingOrderKernel);
        orderKernel((Index1D)n, keysBuf.View, orderViolations.View, n);

        var integrityKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, int>(VerifyIndexIntegrityKernel);
        integrityKernel((Index1D)n, valuesBuf.View, seenBuf.View, integrityViolations.View, n);

        await accelerator.SynchronizeAsync();

        var orderResult = await orderViolations.CopyToHostAsync<int>();
        var integrityResult = await integrityViolations.CopyToHostAsync<int>();

        int trackingErrors = 0;
        if (originalKeysBuf != null)
        {
            using var trackingViolations = accelerator.Allocate1D(new int[] { 0 });
            var trackingKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>, int>(
                VerifyKeyValueTrackingKernel);
            trackingKernel((Index1D)n, keysBuf.View, valuesBuf.View,
                originalKeysBuf.View, trackingViolations.View, n);
            await accelerator.SynchronizeAsync();
            var trackResult = await trackingViolations.CopyToHostAsync<int>();
            trackingErrors = trackResult[0];
        }

        return (orderResult[0], integrityResult[1], integrityResult[0], trackingErrors);
    }

    /// <summary>
    /// Compare two float buffers on GPU. Returns (meanAbsError, maxAbsError).
    /// No large data transfer — reads back only 2 floats.
    /// </summary>
    public static async Task<(float meanError, float maxError)> CompareBuffers(
        Accelerator accelerator,
        MemoryBuffer1D<float, Stride1D.Dense> actual,
        MemoryBuffer1D<float, Stride1D.Dense> expected,
        int n)
    {
        using var results = accelerator.Allocate1D(new float[] { 0f, 0f });

        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int>(CompareFloatKernel);
        kernel((Index1D)n, actual.View, expected.View, results.View, n);
        await accelerator.SynchronizeAsync();

        var result = await results.CopyToHostAsync<float>();
        return (result[0] / n, result[1]);
    }

    // ═══════════════════════════════════════════════════════════
    //  Sentinel Counting (for cull/sort boundary verification)
    // ═══════════════════════════════════════════════════════════

    static void CountNonSentinelKernel(
        Index1D index, ArrayView<int> data, ArrayView<int> count, int n, int sentinel)
    {
        if (index < n && data[index] != sentinel)
            Atomic.Add(ref count[0], 1);
    }

    /// <summary>
    /// Count elements that are NOT equal to the sentinel value. GPU-side, returns single int.
    /// Replaces downloading entire sorted arrays to CPU just to find sentinel boundaries.
    /// </summary>
    public static async Task<int> CountNonSentinel(
        Accelerator accelerator,
        MemoryBuffer1D<int, Stride1D.Dense> buffer,
        int n,
        int sentinel = int.MinValue)
    {
        if (accelerator.AcceleratorType == AcceleratorType.WebGL)
        {
            var data = await buffer.CopyToHostAsync<int>();
            int c = 0;
            for (int i = 0; i < n; i++) if (data[i] != sentinel) c++;
            return c;
        }

        using var count = accelerator.Allocate1D(new int[] { 0 });

        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<int>, ArrayView<int>, int, int>(CountNonSentinelKernel);
        kernel((Index1D)n, buffer.View, count.View, n, sentinel);
        await accelerator.SynchronizeAsync();

        var result = await count.CopyToHostAsync<int>();
        return result[0];
    }
}
