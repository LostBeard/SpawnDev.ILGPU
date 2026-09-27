using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// A helper that takes views (a global input view plus function-local <see cref="LocalMemory"/> scratch) called
/// from enough call sites that the Inliner's cumulative IL budget runs out and later calls stay calls.
///
/// Found 2026-09-27 by Tuvok in SpawnScene's GpuEpipolarRansac (per-thread 8-point solve with LocalMemory
/// scratch): the WGSL fn-def generator cannot marshal view parameters - it typed every one
/// <c>ptr&lt;storage, array&lt;f32&gt;&gt;</c>, declared a LocalMemory alias as plain <c>f32</c>, and Tint rejected the
/// shader ("cannot assign 'ptr&lt;function, array&lt;f32, 72&gt;, read_write&gt;' to 'f32'") at pipeline creation.
/// The CPU accelerator ran the same kernel correctly. Fix: <c>InlineAddressParameterCalls</c> in the WebGPU and
/// WebGL kernel transformers - an address-taking helper is always inlined.
///
/// The work is real: each thread solves 8 dense 6x6 systems with a known solution by Gaussian elimination with
/// complete pivoting (row AND column swaps through LocalMemory, dynamic indices) and writes one checksum.
/// </summary>
public abstract partial class BackendTestBase
{
    const int AddrHelperN = 6;
    const int AddrHelperSystems = 8;
    const int AddrHelperStride = AddrHelperN * (AddrHelperN + 1);   // [A | b], row-major

    /// <summary>
    /// Solve system <paramref name="system"/> of the thread's block into <paramref name="x"/>. Returns false when
    /// singular. Large on purpose (copy, complete-pivot search, row and column swaps, elimination, back
    /// substitution, un-permute): with 8 call sites it exhausts the kernel's inlining budget.
    /// </summary>
    static bool AddrHelperSolve(ArrayView<float> input, int baseOffset, int system, int n,
        ArrayView<float> a, ArrayView<int> perm, ArrayView<float> x)
    {
        // n is a RUNTIME value on purpose: with a constant n, LoopUnrolling expands every loop (x 8 call sites)
        // into ~14K lines of GLSL and WebGL's FXC compile exceeds the test timeout - a compile-cost trait, not
        // the address-parameter bug this test is about. Runtime n also makes every LocalMemory index dynamic.
        int w = n + 1;
        int src = baseOffset + system * n * w;
        for (int i = 0; i < n * w; i++) a[i] = input[src + i];
        for (int c = 0; c < n; c++) perm[c] = c;
        for (int r = 0; r < n; r++)
        {
            int pr = r, pc = r;
            float pv = 0f;
            for (int i = r; i < n; i++)
                for (int c = r; c < n; c++)
                {
                    float v = XMath.Abs(a[i * w + c]);
                    if (v > pv) { pv = v; pr = i; pc = c; }
                }
            if (pv < 1e-12f) return false;
            if (pr != r)
                for (int c = 0; c < w; c++) { float t = a[r * w + c]; a[r * w + c] = a[pr * w + c]; a[pr * w + c] = t; }
            if (pc != r)
            {
                for (int i = 0; i < n; i++) { float t = a[i * w + r]; a[i * w + r] = a[i * w + pc]; a[i * w + pc] = t; }
                int tp = perm[r]; perm[r] = perm[pc]; perm[pc] = tp;
            }
            float inv = 1f / a[r * w + r];
            for (int i = r + 1; i < n; i++)
            {
                float f = a[i * w + r] * inv;
                for (int c = r; c < w; c++) a[i * w + c] -= f * a[r * w + c];
            }
        }
        for (int r = n - 1; r >= 0; r--)
        {
            float acc = a[r * w + n];
            for (int c = r + 1; c < n; c++) acc -= a[r * w + c] * a[c * w + n];   // solved y_c parked in column n
            a[r * w + n] = acc / a[r * w + r];
        }
        for (int c = 0; c < n; c++) x[perm[c]] = a[c * w + n];
        return true;
    }

    static float AddrHelperChecksum(ArrayView<float> x, int system, int n)
    {
        float s = 0f;
        for (int i = 0; i < n; i++) s += x[i] * (i + 1) * (system + 1);
        return s;
    }

    static void AddrHelperKernel(Index1D t, ArrayView<float> input, ArrayView<float> output, int threads, int n)
    {
        if (t >= threads) return;
        var a = LocalMemory.Allocate<float>(AddrHelperStride);
        var perm = LocalMemory.Allocate<int>(AddrHelperN);
        var x = LocalMemory.Allocate<float>(AddrHelperN);
        int b = t * AddrHelperSystems * n * (n + 1);
        float sum = 0f;
        // Eight separate call sites (NOT a loop): a loop body is one call site, inlined once.
        if (AddrHelperSolve(input, b, 0, n, a, perm, x)) sum += AddrHelperChecksum(x, 0, n); else sum += 1e6f;
        if (AddrHelperSolve(input, b, 1, n, a, perm, x)) sum += AddrHelperChecksum(x, 1, n); else sum += 1e6f;
        if (AddrHelperSolve(input, b, 2, n, a, perm, x)) sum += AddrHelperChecksum(x, 2, n); else sum += 1e6f;
        if (AddrHelperSolve(input, b, 3, n, a, perm, x)) sum += AddrHelperChecksum(x, 3, n); else sum += 1e6f;
        if (AddrHelperSolve(input, b, 4, n, a, perm, x)) sum += AddrHelperChecksum(x, 4, n); else sum += 1e6f;
        if (AddrHelperSolve(input, b, 5, n, a, perm, x)) sum += AddrHelperChecksum(x, 5, n); else sum += 1e6f;
        if (AddrHelperSolve(input, b, 6, n, a, perm, x)) sum += AddrHelperChecksum(x, 6, n); else sum += 1e6f;
        if (AddrHelperSolve(input, b, 7, n, a, perm, x)) sum += AddrHelperChecksum(x, 7, n); else sum += 1e6f;
        output[t] = sum;
    }

    [TestMethod]
    public async Task HelperCodegen_AddressParams_LocalMemoryViews_ManyCallSites() => await RunTest(async accelerator =>
    {
        const int threads = 64;
        var rng = new Random(20260927);
        var input = new float[threads * AddrHelperSystems * AddrHelperStride];
        var expected = new double[threads];
        for (int t = 0; t < threads; t++)
            for (int s = 0; s < AddrHelperSystems; s++)
            {
                // Well-conditioned but NOT diagonally ordered, so complete pivoting really swaps rows and columns.
                var m = new double[AddrHelperN, AddrHelperN];
                var xs = new double[AddrHelperN];
                for (int i = 0; i < AddrHelperN; i++)
                {
                    xs[i] = rng.NextDouble() * 4 - 2;
                    for (int c = 0; c < AddrHelperN; c++) m[i, c] = rng.NextDouble() * 2 - 1;
                    m[i, (i + s + 1) % AddrHelperN] += 4 * (rng.Next(2) * 2 - 1);
                }
                int o = (t * AddrHelperSystems + s) * AddrHelperStride;
                for (int i = 0; i < AddrHelperN; i++)
                {
                    double bi = 0;
                    for (int c = 0; c < AddrHelperN; c++) { input[o + i * (AddrHelperN + 1) + c] = (float)m[i, c]; bi += (float)m[i, c] * xs[c]; }
                    input[o + i * (AddrHelperN + 1) + AddrHelperN] = (float)bi;
                }
                for (int i = 0; i < AddrHelperN; i++) expected[t] += xs[i] * (i + 1) * (s + 1);
            }

        using var inBuf = accelerator.Allocate1D(input);
        using var outBuf = accelerator.Allocate1D<float>(threads);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int>(AddrHelperKernel);
        kernel((Index1D)threads, inBuf.View, outBuf.View, threads, AddrHelperN);
        await accelerator.SynchronizeAsync();
        var actual = await outBuf.CopyToHostAsync<float>();
        for (int t = 0; t < threads; t++)
        {
            double tol = 1e-3 * Math.Max(1.0, Math.Abs(expected[t]));
            if (!(Math.Abs(actual[t] - expected[t]) <= tol))
                throw new Exception($"thread {t}: checksum {actual[t]} expected {expected[t]:G9} (tolerance {tol:G3})");
        }
    });

    /// <summary>
    /// Minimal shape of the WebGL failure the test above exposed once its helper was inlined: a helper that
    /// returns from INSIDE a nested loop (a multi-exit loop), inlined at ONE call site, with work after it.
    /// Returns the index of the first row whose running sum passes a bar, or -1.
    /// </summary>
    static int EarlyExitFindRow(ArrayView<float> m, int rowBase, int rows, int cols, float bar)
    {
        for (int r = 0; r < rows; r++)
        {
            float acc = 0f;
            for (int c = 0; c < cols; c++)
            {
                acc += m[rowBase + r * cols + c];
                if (acc > bar) return r;
            }
        }
        return -1;
    }

    static void EarlyExitKernel(Index1D t, ArrayView<float> m, ArrayView<float> output, int threads, int rows, int cols)
    {
        if (t >= threads) return;
        int found = EarlyExitFindRow(m, t * rows * cols, rows, cols, 3.5f);
        float after = 0f;
        for (int c = 0; c < cols; c++) after += m[t * rows * cols + c];   // work AFTER the inlined early exit
        output[t] = found * 1000f + after;
    }

    [TestMethod]
    public async Task HelperCodegen_ReturnFromNestedLoop_ThenMoreWork() => await RunTest(async accelerator =>
    {
        const int threads = 64, rows = 5, cols = 4;
        var rng = new Random(7);
        var m = new float[threads * rows * cols];
        for (int i = 0; i < m.Length; i++) m[i] = (float)rng.NextDouble();
        var expected = new float[threads];
        for (int t = 0; t < threads; t++)
        {
            int found = -1;
            for (int r = 0; r < rows && found < 0; r++)
            {
                float acc = 0f;
                for (int c = 0; c < cols; c++) { acc += m[t * rows * cols + r * cols + c]; if (acc > 3.5f) { found = r; break; } }
            }
            float after = 0f;
            for (int c = 0; c < cols; c++) after += m[t * rows * cols + c];
            expected[t] = found * 1000f + after;
        }
        using var mBuf = accelerator.Allocate1D(m);
        using var outBuf = accelerator.Allocate1D<float>(threads);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int, int>(EarlyExitKernel);
        kernel((Index1D)threads, mBuf.View, outBuf.View, threads, rows, cols);
        await accelerator.SynchronizeAsync();
        var actual = await outBuf.CopyToHostAsync<float>();
        for (int t = 0; t < threads; t++)
            if (!(Math.Abs(actual[t] - expected[t]) <= 1e-3f))
                throw new Exception($"thread {t}: got {actual[t]} expected {expected[t]}");
    });

    /// <summary>
    /// The solver's control-flow shape without its math: an early <c>return false</c> from inside a nested loop
    /// that SKIPS a later loop (which runs before <c>return true</c>), and a call site that branches on the result.
    /// </summary>
    static bool EarlyExitThenLoop(ArrayView<float> m, int rowBase, int rows, int cols, float bar, ArrayView<float> scratch)
    {
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
            {
                float v = m[rowBase + r * cols + c];
                if (v > bar) return false;
                scratch[r * cols + c] = v * 2f;
            }
        for (int r = rows - 1; r >= 0; r--)
            scratch[r * cols] += scratch[r * cols + cols - 1];
        return true;
    }

    static void EarlyExitThenLoopKernel(Index1D t, ArrayView<float> m, ArrayView<float> output, int threads, int rows, int cols, float bar)
    {
        if (t >= threads) return;
        var scratch = LocalMemory.Allocate<float>(64);
        float sum;
        if (EarlyExitThenLoop(m, t * rows * cols, rows, cols, bar, scratch))
        {
            sum = 0f;
            for (int r = 0; r < rows; r++) sum += scratch[r * cols];
        }
        else sum = -1f;
        output[t] = sum;
    }

    [TestMethod]
    public async Task HelperCodegen_EarlyReturnSkipsLaterLoop_CallerBranches() => await RunTest(async accelerator =>
    {
        const int threads = 64, rows = 5, cols = 4;
        const float bar = 0.97f;   // some threads exit early, most do not
        var rng = new Random(11);
        var m = new float[threads * rows * cols];
        for (int i = 0; i < m.Length; i++) m[i] = (float)rng.NextDouble();
        var expected = new float[threads];
        int early = 0;
        for (int t = 0; t < threads; t++)
        {
            var sc = new float[rows * cols];
            bool ok = true;
            for (int r = 0; r < rows && ok; r++)
                for (int c = 0; c < cols; c++)
                {
                    float v = m[t * rows * cols + r * cols + c];
                    if (v > bar) { ok = false; break; }
                    sc[r * cols + c] = v * 2f;
                }
            if (ok)
            {
                for (int r = rows - 1; r >= 0; r--) sc[r * cols] += sc[r * cols + cols - 1];
                float s2 = 0f;
                for (int r = 0; r < rows; r++) s2 += sc[r * cols];
                expected[t] = s2;
            }
            else { expected[t] = -1f; early++; }
        }
        if (early == 0 || early == threads) throw new Exception($"test data must mix early and normal exits ({early}/{threads} early)");
        using var mBuf = accelerator.Allocate1D(m);
        using var outBuf = accelerator.Allocate1D<float>(threads);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int, int, float>(EarlyExitThenLoopKernel);
        kernel((Index1D)threads, mBuf.View, outBuf.View, threads, rows, cols, bar);
        await accelerator.SynchronizeAsync();
        var actual = await outBuf.CopyToHostAsync<float>();
        for (int t = 0; t < threads; t++)
            if (!(Math.Abs(actual[t] - expected[t]) <= 1e-4f))
                throw new Exception($"thread {t}: got {actual[t]} expected {expected[t]} ({early}/{threads} exit early)");
    });

    /// <summary>
    /// Scalar-only helper (no address parameters, so it is NOT force-inlined at IR level) with the early-return
    /// shape: from inside nested loops it returns past a later loop. Large enough, at 8 call sites, that the
    /// Inliner budget leaves later calls as calls - which the WGSL generator inlines at EMISSION time through
    /// its own structurizer (InlineStructuredBlock).
    /// </summary>
    static int ScalarEarlyExit(int seed, int rows, int cols, int bar)
    {
        int acc = seed;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                acc = (acc * 1103515245 + 12345) & 0x7fffffff;
                int v = (acc >> 8) % 1000;
                if (v > bar) return -1 - r;          // early exit: skips the loop below
                acc ^= v * (r + 1) * (c + 3);
                acc = acc & 0x7fffffff;
                // Bulk (distinct statements, not a loop) so 8 call sites exhaust the Inliner's IL budget.
                int m0 = (acc ^ (acc >> 3)) & 0xffff, m1 = (acc ^ (acc >> 5)) & 0xffff, m2 = (acc ^ (acc >> 7)) & 0xffff;
                int m3 = (m0 * 31 + m1 * 17 + m2 * 13) & 0xffff, m4 = (m1 * 29 + m2 * 11 + m0 * 7) & 0xffff;
                int m5 = (m3 ^ m4) + (m0 & m2) - (m1 | 3), m6 = (m5 * 5 + m3 * 3 + m4) & 0xffff;
                int m7 = (m6 ^ (m6 << 2)) & 0xffff, m8 = (m7 + m5 * 9 + m2 * 21) & 0xffff;
                int m9 = (m8 ^ m7 ^ m6 ^ m5) & 0xffff, m10 = (m9 * 7 + m4 * 3 + m1) & 0xffff;
                int m11 = (m10 + (m9 >> 1) + (m8 >> 2) + (m7 >> 3)) & 0xffff;
                acc = (acc ^ (m11 << 7) ^ m10) & 0x7fffffff;
            }
        }
        int tail = 0;
        for (int k = rows - 1; k >= 0; k--)
        {
            tail += (acc >> k) & 15;
            tail = tail * 3 + k;
            tail &= 0xffff;
        }
        return tail;
    }

    static void ScalarEarlyExitKernel(Index1D t, ArrayView<int> output, int threads, int rows, int cols, int bar)
    {
        if (t >= threads) return;
        int s = 0;
        s += ScalarEarlyExit(t * 8 + 0, rows, cols, bar);
        s += ScalarEarlyExit(t * 8 + 1, rows, cols, bar) * 3;
        s += ScalarEarlyExit(t * 8 + 2, rows, cols, bar) * 5;
        s += ScalarEarlyExit(t * 8 + 3, rows, cols, bar) * 7;
        s += ScalarEarlyExit(t * 8 + 4, rows, cols, bar) * 11;
        s += ScalarEarlyExit(t * 8 + 5, rows, cols, bar) * 13;
        s += ScalarEarlyExit(t * 8 + 6, rows, cols, bar) * 17;
        s += ScalarEarlyExit(t * 8 + 7, rows, cols, bar) * 19;
        output[t] = s;
    }

    [TestMethod]
    public async Task HelperCodegen_ScalarHelper_EarlyReturnSkipsLoop_ManyCallSites() => await RunTest(async accelerator =>
    {
        const int threads = 64, rows = 4, cols = 4, bar = 985;
        var expected = new int[threads];
        int early = 0, total = 0;
        int[] w = { 1, 3, 5, 7, 11, 13, 17, 19 };
        for (int t = 0; t < threads; t++)
            for (int k = 0; k < 8; k++)
            {
                int v = ScalarEarlyExit(t * 8 + k, rows, cols, bar);
                if (v < 0) early++;
                total++;
                expected[t] += v * w[k];
            }
        if (early == 0 || early == total) throw new Exception($"test data must mix early and normal exits ({early}/{total})");
        using var outBuf = accelerator.Allocate1D<int>(threads);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int, int, int>(ScalarEarlyExitKernel);
        kernel((Index1D)threads, outBuf.View, threads, rows, cols, bar);
        await accelerator.SynchronizeAsync();
        var actual = await outBuf.CopyToHostAsync<int>();
        for (int t = 0; t < threads; t++)
            if (actual[t] != expected[t])
                throw new Exception($"thread {t}: got {actual[t]} expected {expected[t]} ({early}/{total} calls exit early)");
    });
}
