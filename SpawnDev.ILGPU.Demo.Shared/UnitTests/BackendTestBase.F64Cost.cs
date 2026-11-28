using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // What an emulated double costs, and that it stays a double. 262,144 threads, each a chain of 1,024 dependent
    // multiply-adds in double (x = x * 0.75 + c, c from a small table: bounded, never exact in f32). Reports the first
    // load + launch (shader generation and the browser / driver compile) and ns per multiply-add (best of 3 after a
    // warm-up) as a [report] line, and checks every result against .NET to 1e-10 relative: an f32-only result (a lost low
    // half, ~1e-7) fails. 5.4.56 routes the WebGPU Dekker / Ozaki error-free transforms through an opaque zero (Mesa's
    // NIR reassociated their error terms away); this is the before / after cost of that, on hardware where nothing broke.
    public abstract partial class BackendTestBase
    {
        public const int F64CostThreads = 262144;
        public const int F64CostIterations = 1024;

        public static void F64CostKernel(Index1D i, ArrayView<double> io, int iterations)
        {
            double x = io[i];
            for (int k = 0; k < iterations; k++)
                x = x * 0.75 + (0.1 + (k & 7) * 0.0625);
            io[i] = x;
        }

        static double F64CostHost(double x, int iterations)
        {
            for (int k = 0; k < iterations; k++)
                x = x * 0.75 + (0.1 + (k & 7) * 0.0625);
            return x;
        }

        [TestMethod]
        public async Task F64Cost_MultiplyAddChain_Report() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.CPU)
                throw new UnsupportedTestException("CPU accelerator: the debugging backend runs one OS thread per GPU thread; the probe is for the compiled backends");
            var input = new double[F64CostThreads];
            var rng = new Random(11);
            for (int t = 0; t < input.Length; t++) input[t] = rng.NextDouble() * 10.0;
            using var io = accelerator.Allocate1D<double>(F64CostThreads);

            io.View.CopyFromCPU(input);
            await accelerator.SynchronizeAsync();
            var compile = System.Diagnostics.Stopwatch.StartNew();
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, int>(F64CostKernel);
            kernel((Index1D)F64CostThreads, io.View, 1);
            await accelerator.SynchronizeAsync();
            compile.Stop();

            double best = double.MaxValue;
            double[] result = Array.Empty<double>();
            for (int r = 0; r < 4; r++)
            {
                io.View.CopyFromCPU(input);
                await accelerator.SynchronizeAsync();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                kernel((Index1D)F64CostThreads, io.View, F64CostIterations);
                await accelerator.SynchronizeAsync();
                sw.Stop();
                if (r > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                if (r == 3) result = await io.CopyToHostAsync<double>();
            }
            double ns = best * 1e6 / ((double)F64CostThreads * F64CostIterations);
            string F(double v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            Console.WriteLine($"[report] {accelerator.AcceleratorType} f64 multiply-add chain ({F64CostThreads} threads x {F64CostIterations}): first load + launch {compile.Elapsed.TotalMilliseconds:F0} ms, {F(ns)} ns per multiply-add");

            int bad = 0;
            string first = "";
            for (int t = 0; t < result.Length; t++)
            {
                double want = F64CostHost(input[t], F64CostIterations);
                if (!(Math.Abs(result[t] - want) <= Math.Abs(want) * 1e-10))
                {
                    if (bad++ == 0) first = $"[{t}] = {result[t]:R}, expected {want:R}";
                }
            }
            if (bad > 0)
                throw new Exception($"{accelerator.AcceleratorType}: {bad} of {F64CostThreads} double chains off by more than 1e-10 relative (first {first})");
        });
    }
}
