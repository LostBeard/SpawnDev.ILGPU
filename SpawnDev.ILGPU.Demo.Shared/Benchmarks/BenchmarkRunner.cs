using System.Diagnostics;
using global::ILGPU;
using global::ILGPU.Runtime;

namespace SpawnDev.ILGPU.Demo.Shared.Benchmarks;

/// <summary>
/// Shared benchmark runner used by both WPF and Blazor demos.
/// Provides identical kernels and timing logic so results are directly comparable.
/// </summary>
public static class BenchmarkRunner
{
    /// <summary>Number of warmup iterations before measurement.</summary>
    public const int WarmUpIterations = 5;

    /// <summary>Number of measured iterations.</summary>
    public const int MeasuredIterations = 20;

    /// <summary>Fraction of outliers to trim from each end (10% = drop 2 fastest + 2 slowest from 20).</summary>
    public const double TrimFraction = 0.10;

    // ====================================================================
    //  KERNELS — identical across all platforms
    // ====================================================================

    public static void VectorAddKernel(Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c)
    {
        c[index] = a[index] + b[index];
    }

    public static void VectorScaleKernel(Index1D index, ArrayView<float> a, float scalar)
    {
        a[index] = a[index] * scalar;
    }

    public static void SaxpyKernel(Index1D index, ArrayView<float> y, ArrayView<float> x, float alpha)
    {
        y[index] = alpha * x[index] + y[index];
    }

    public static void MandelbrotKernel(Index1D index, ArrayView<int> output, int width, int height, int maxIter)
    {
        int px = index % width;
        int py = index / width;
        float x0 = (px / (float)width) * 3.5f - 2.5f;
        float y0 = (py / (float)height) * 2.0f - 1.0f;
        float x = 0, y = 0;
        int iter = 0;
        while (x * x + y * y <= 4.0f && iter < maxIter)
        {
            float xtemp = x * x - y * y + x0;
            y = 2.0f * x * y + y0;
            x = xtemp;
            iter++;
        }
        output[index] = iter;
    }

    public static void PrimeCountKernel(Index1D index, ArrayView<int> results, int offset)
    {
        int n = offset + index;
        if (n < 2) { results[index] = 0; return; }
        if (n < 4) { results[index] = 1; return; }
        if (n % 2 == 0) { results[index] = 0; return; }
        for (int d = 3; d * d <= n; d += 2)
        {
            if (n % d == 0) { results[index] = 0; return; }
        }
        results[index] = 1;
    }

    // --- Wasm SIMD128 showcase kernel ---------------------------------------------------
    //
    // FmaHeavy is the ALU-DENSE elementwise kernel used by the "Wasm SIMD128 — with vs
    // without" comparison. Two properties make the Wasm v128 (f32x4) win actually VISIBLE:
    //
    //  1. It is a SINGLE straight-line basic block (no branches, no loop) of f32 unit-stride
    //     load → R dependent mul/add steps → store. That is exactly the Stage-3a vectorizable
    //     class, so the Wasm backend emits the additive `kernel_simd` (4 lanes/call) for it.
    //     A `for` loop would split into multiple IR blocks and fall back to scalar — hence the
    //     hand-unroll. R == FmaHeavyDepth steps; keep the two in sync (the PMT numerical gate
    //     Wasm_Simd128_FmaHeavyMatchesScalarAndReference fails loud if they drift).
    //  2. It is ALU-BOUND (R mul/adds per 1 load + 1 store), so the ~3.5x pure-ALU f32x4 win
    //     measured at the Phase-2 gate dominates the fixed dispatch/memory floor. Memory-bound
    //     elementwise kernels (Vector Add, SAXPY) gain little — that is the honest contrast the
    //     comparison card shows: SIMD multiplies ALU throughput, not memory bandwidth.
    //
    // Cross-mode determinism: f32 mul-then-add with NO fused FMA, so the v128 path is BIT-exact
    // to the scalar path (wasm core SIMD has no f32x4 FMA either). 0.999*v + 0.001 stays finite.
    public const int FmaHeavyDepth = 256;
    public const float FmaMul = 0.999f;
    public const float FmaAdd = 0.001f;

    public static void FmaHeavyKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
    {
        float v = input[index];
        // FmaHeavyDepth (256) hand-unrolled mul-add steps — straight-line, single IR block (so it stays in
        // the Stage-3a vectorizable class). 8 steps/line × 32 lines. ALU density chosen so the f32x4 win is
        // a clear, honest fraction of the f32x4 ceiling above the fixed dispatch/memory floor.
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd; v = v * FmaMul + FmaAdd;
        output[index] = v;
    }

    // ====================================================================
    //  BENCHMARK ORCHESTRATION
    // ====================================================================

    /// <summary>
    /// Runs a single benchmark with warmup, measured iterations, statistical trimming, and GC between suites.
    /// </summary>
    public static async Task<BenchmarkResult> RunAsync(Accelerator acc, string benchId, BackendProfile profile)
    {
        try
        {
            int elementCount = benchId switch
            {
                "mandelbrot" => profile.MandelbrotSize * profile.MandelbrotSize,
                "primes" => profile.PrimesN,
                _ => profile.DefaultN,
            };

            var (warmupMs, times) = benchId switch
            {
                "vecadd" => await RunVectorAdd(acc, profile),
                "vecscale" => await RunVectorScale(acc, profile),
                "saxpy" => await RunSaxpy(acc, profile),
                "fmaheavy" => await RunFmaHeavy(acc, profile),
                "mandelbrot" => await RunMandelbrot(acc, profile),
                "primes" => await RunPrimes(acc, profile),
                _ => throw new ArgumentException($"Unknown benchmark: {benchId}"),
            };

            double trimmedMedian = TrimmedMedian(times);
            double throughputEps = elementCount / (trimmedMedian / 1000.0);
            string unit = benchId == "mandelbrot" ? "px" : "elem";

            return new BenchmarkResult
            {
                BackendName = profile.Name,
                BenchmarkName = benchId,
                MedianMs = Math.Round(trimmedMedian, 2),
                Throughput = FormatThroughput(throughputEps, unit),
                ThroughputElemPerSec = throughputEps,
                ElementCount = elementCount,
                WarmupMs = Math.Round(warmupMs, 1),
                AllTimesMs = times,
            };
        }
        catch (Exception ex)
        {
            return new BenchmarkResult
            {
                BackendName = profile.Name,
                BenchmarkName = benchId,
                Failed = true,
                Error = ex.Message,
            };
        }
    }

    // ====================================================================
    //  INDIVIDUAL BENCHMARKS
    // ====================================================================

    private static async Task<(double warmupMs, double[] times)> RunVectorAdd(Accelerator acc, BackendProfile profile)
    {
        int n = profile.DefaultN;
        var a = Enumerable.Range(0, n).Select(i => (float)i).ToArray();
        var b = Enumerable.Range(0, n).Select(i => (float)(i * 2)).ToArray();
        var c = new float[n];

        using var bufA = acc.Allocate1D(a);
        using var bufB = acc.Allocate1D(b);
        using var bufC = acc.Allocate1D(c);

        var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAddKernel);

        var warmupMs = await RunWarmup(() =>
        {
            kernel((Index1D)n, bufA.View, bufB.View, bufC.View);
            return acc.SynchronizeAsync();
        });

        var times = await RunMeasured(() =>
        {
            kernel((Index1D)n, bufA.View, bufB.View, bufC.View);
            return acc.SynchronizeAsync();
        });

        return (warmupMs, times);
    }

    private static async Task<(double warmupMs, double[] times)> RunVectorScale(Accelerator acc, BackendProfile profile)
    {
        int n = profile.DefaultN;
        var a = Enumerable.Range(0, n).Select(i => (float)i).ToArray();

        using var bufA = acc.Allocate1D(a);
        var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(VectorScaleKernel);

        var warmupMs = await RunWarmup(() =>
        {
            kernel((Index1D)n, bufA.View, 2.0f);
            return acc.SynchronizeAsync();
        });

        var times = await RunMeasured(() =>
        {
            kernel((Index1D)n, bufA.View, 2.0f);
            return acc.SynchronizeAsync();
        });

        return (warmupMs, times);
    }

    private static async Task<(double warmupMs, double[] times)> RunSaxpy(Accelerator acc, BackendProfile profile)
    {
        int n = profile.DefaultN;
        var x = Enumerable.Range(0, n).Select(i => (float)i).ToArray();
        var y = Enumerable.Range(0, n).Select(i => (float)(i * 0.5)).ToArray();

        using var bufX = acc.Allocate1D(x);
        using var bufY = acc.Allocate1D(y);
        var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float>(SaxpyKernel);

        var warmupMs = await RunWarmup(() =>
        {
            kernel((Index1D)n, bufY.View, bufX.View, 2.5f);
            return acc.SynchronizeAsync();
        });

        var times = await RunMeasured(() =>
        {
            kernel((Index1D)n, bufY.View, bufX.View, 2.5f);
            return acc.SynchronizeAsync();
        });

        return (warmupMs, times);
    }

    private static async Task<(double warmupMs, double[] times)> RunFmaHeavy(Accelerator acc, BackendProfile profile)
    {
        int n = profile.DefaultN;
        var input = Enumerable.Range(0, n).Select(i => (float)((i % 1000) * 0.001f)).ToArray();

        using var bufIn = acc.Allocate1D(input);
        using var bufOut = acc.Allocate1D<float>(n);
        var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(FmaHeavyKernel);

        var warmupMs = await RunWarmup(() =>
        {
            kernel((Index1D)n, bufIn.View, bufOut.View);
            return acc.SynchronizeAsync();
        });

        var times = await RunMeasured(() =>
        {
            kernel((Index1D)n, bufIn.View, bufOut.View);
            return acc.SynchronizeAsync();
        });

        return (warmupMs, times);
    }

    private static async Task<(double warmupMs, double[] times)> RunMandelbrot(Accelerator acc, BackendProfile profile)
    {
        int size = profile.MandelbrotSize;
        int totalPixels = size * size;
        var output = new int[totalPixels];

        using var buf = acc.Allocate1D(output);
        var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int, int>(MandelbrotKernel);

        var warmupMs = await RunWarmup(() =>
        {
            kernel((Index1D)totalPixels, buf.View, size, size, 200);
            return acc.SynchronizeAsync();
        });

        var times = await RunMeasured(() =>
        {
            kernel((Index1D)totalPixels, buf.View, size, size, 200);
            return acc.SynchronizeAsync();
        });

        return (warmupMs, times);
    }

    private static async Task<(double warmupMs, double[] times)> RunPrimes(Accelerator acc, BackendProfile profile)
    {
        int n = profile.PrimesN;
        var results = new int[n];

        using var buf = acc.Allocate1D(results);
        var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(PrimeCountKernel);

        var warmupMs = await RunWarmup(() =>
        {
            kernel((Index1D)n, buf.View, 2);
            return acc.SynchronizeAsync();
        });

        var times = await RunMeasured(() =>
        {
            kernel((Index1D)n, buf.View, 2);
            return acc.SynchronizeAsync();
        });

        return (warmupMs, times);
    }

    // ====================================================================
    //  TIMING HELPERS
    // ====================================================================

    private static async Task<double> RunWarmup(Func<Task> action)
    {
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < WarmUpIterations; i++)
        {
            await action();
        }
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }

    private static async Task<double[]> RunMeasured(Func<Task> action)
    {
        // GC before measurement to reduce jitter from memory pressure
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var times = new double[MeasuredIterations];
        for (int i = 0; i < MeasuredIterations; i++)
        {
            var sw = Stopwatch.StartNew();
            await action();
            sw.Stop();
            times[i] = sw.Elapsed.TotalMilliseconds;
        }
        return times;
    }

    // ====================================================================
    //  STATISTICS
    // ====================================================================

    /// <summary>
    /// Computes the median after trimming the fastest and slowest TrimFraction from each end.
    /// For 20 runs at 10%: drops 2 fastest + 2 slowest, takes median of remaining 16.
    /// </summary>
    public static double TrimmedMedian(double[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int trimCount = Math.Max(1, (int)(sorted.Length * TrimFraction));
        var trimmed = sorted.Skip(trimCount).Take(sorted.Length - 2 * trimCount).ToArray();

        if (trimmed.Length == 0) return Median(sorted); // fallback
        return Median(trimmed);
    }

    /// <summary>Simple median of an array.</summary>
    public static double Median(double[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        return sorted.Length % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }

    // ====================================================================
    //  FORMATTING
    // ====================================================================

    public static string FormatThroughput(double elemPerSec, string unit)
    {
        if (elemPerSec >= 1_000_000_000) return $"{elemPerSec / 1e9:F2} G{unit}/s";
        if (elemPerSec >= 1_000_000) return $"{elemPerSec / 1e6:F2} M{unit}/s";
        if (elemPerSec >= 1_000) return $"{elemPerSec / 1e3:F2} K{unit}/s";
        return $"{elemPerSec:F0} {unit}/s";
    }

    public static string FormatShortThroughput(double v)
    {
        if (v >= 1e9) return $"{v / 1e9:F1}G";
        if (v >= 1e6) return $"{v / 1e6:F1}M";
        if (v >= 1e3) return $"{v / 1e3:F1}K";
        return $"{v:F0}";
    }
}
