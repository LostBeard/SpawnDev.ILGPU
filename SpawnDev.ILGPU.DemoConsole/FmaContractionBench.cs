using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

/// <summary>
/// 5.4.18 cost of the strict float default (no implicit mul+add contraction) on CUDA and OpenCL, measured, not guessed:
/// each kernel under the default context and under Context.Builder.EnableFloatingPointContraction(), plus the ALU loop
/// written with float.MultiplyAddEstimate under the default. Run from a Release build:
///   dotnet run -c Release --project SpawnDev.ILGPU.DemoConsole -- fma-ab
/// </summary>
internal static class FmaContractionBench
{
    const int AluIters = 4096;
    const int GemvK = 4096;

    // ALU-bound: a dependent multiply-add chain in registers.
    static void AluMulAdd(Index1D i, ArrayView<float> m, ArrayView<float> o)
    {
        float acc = i * 1e-7f, mm = m[0], c = m[1];
        for (int k = 0; k < AluIters; k++) acc = acc * mm + c;
        o[i] = acc;
    }

    static void AluEstimate(Index1D i, ArrayView<float> m, ArrayView<float> o)
    {
        float acc = i * 1e-7f, mm = m[0], c = m[1];
        for (int k = 0; k < AluIters; k++) acc = float.MultiplyAddEstimate(acc, mm, c);
        o[i] = acc;
    }

    // Memory-bound: one row of a GEMV per thread.
    static void Gemv(Index1D row, ArrayView<float> a, ArrayView<float> x, ArrayView<float> o)
    {
        float sum = 0f;
        long baseIdx = (long)(int)row * GemvK;
        for (int k = 0; k < GemvK; k++) sum += a[baseIdx + k] * x[k];
        o[row] = sum;
    }

    static double MedianMs(Accelerator acc, Action launch)
    {
        for (int w = 0; w < 3; w++) launch();
        acc.Synchronize();
        var times = new List<double>();
        for (int r = 0; r < 20; r++)
        {
            var sw = Stopwatch.StartNew();
            launch();
            acc.Synchronize();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        times.Sort();
        return times[times.Count / 2];
    }

    static (double alu, double est, double gemv) Measure(Accelerator acc)
    {
        const int aluThreads = 1 << 20, rows = 4096;
        using var m = acc.Allocate1D(new float[] { 0.999f, 0.001f });
        using var aluOut = acc.Allocate1D<float>(aluThreads);
        var rng = new Random(1);
        var aHost = new float[rows * GemvK];
        for (int i = 0; i < aHost.Length; i++) aHost[i] = (float)rng.NextDouble();
        var xHost = new float[GemvK];
        for (int i = 0; i < xHost.Length; i++) xHost[i] = (float)rng.NextDouble();
        using var a = acc.Allocate1D(aHost);
        using var x = acc.Allocate1D(xHost);
        using var gemvOut = acc.Allocate1D<float>(rows);
        var alu = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(AluMulAdd);
        var est = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(AluEstimate);
        var gemv = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(Gemv);
        return (MedianMs(acc, () => alu(aluThreads, m.View, aluOut.View)),
                MedianMs(acc, () => est(aluThreads, m.View, aluOut.View)),
                MedianMs(acc, () => gemv(rows, a.View, x.View, gemvOut.View)));
    }

    public static Task<int> Run(string[] args)
    {
        foreach (var kind in new[] { "CUDA", "OpenCL" })
        {
            foreach (bool contract in new[] { false, true })
            {
                using var ctx = Context.Create(b =>
                {
                    if (kind == "CUDA") b.Cuda(); else b.OpenCL();
                    if (contract) b.EnableFloatingPointContraction();
                });
                var dev = ctx.Devices.FirstOrDefault(d => d.AcceleratorType == (kind == "CUDA" ? AcceleratorType.Cuda : AcceleratorType.OpenCL));
                if (dev == null) { Console.WriteLine($"{kind}: no device"); continue; }
                using var acc = dev.CreateAccelerator(ctx);
                var (alu, est, gemv) = Measure(acc);
                Console.WriteLine($"{kind,-6} {(contract ? "contraction ON " : "default (strict)")}  {acc.Name}: " +
                    $"ALU a*b+c {alu:F3} ms | ALU MultiplyAddEstimate {est:F3} ms | GEMV 4096x4096 {gemv:F3} ms");
            }
        }
        return Task.FromResult(0);
    }
}
