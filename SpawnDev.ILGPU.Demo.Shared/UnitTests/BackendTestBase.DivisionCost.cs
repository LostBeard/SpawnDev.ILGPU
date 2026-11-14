using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // What a correctly rounded f32 division costs. 5.4.32 made `/` exact on WebGPU / WebGL (an integer routine:
    // WGSLEmulationLibrary.F32CorrectlyRoundedFunctions) and OpenCL (-cl-fp32-correctly-rounded-divide-sqrt). 262,144
    // threads (65,536 x 256 measured only the launch overhead: multiply = divide on WebGPU, 2026-11-14), each a loop of
    // 4,096 dependent divisions by a varying divisor, against the same loop with a multiply (the
    // loop's own cost); ns per operation as a [report] line, and the result bit-exact against .NET (every backend now
    // divides as .NET does). WebGPUTests.DivisionCost_DefaultAndFastMath_Report adds MathMode.Fast (hardware `/`).
    public abstract partial class BackendTestBase
    {
        public const int DivisionCostThreads = 262144;
        public const int DivisionCostIterations = 4096;

        public static void DivisionCostKernel(Index1D i, ArrayView<float> io, int iterations)
        {
            float x = io[i];
            for (int k = 0; k < iterations; k++)
                x = (x + 3.0f) / (1.5f + (k & 3) * 0.25f);
            io[i] = x;
        }

        public static void MultiplyCostKernel(Index1D i, ArrayView<float> io, int iterations)
        {
            float x = io[i];
            for (int k = 0; k < iterations; k++)
                x = (x + 3.0f) * (0.5f + (k & 3) * 0.0625f);
            io[i] = x;
        }

        static float DivisionCostHost(float x, int iterations)
        {
            for (int k = 0; k < iterations; k++)
                x = (x + 3.0f) / (1.5f + (k & 3) * 0.25f);
            return x;
        }

        /// <summary>Times both kernels (best of 3 after a warm-up); checks the division against the host when asked.</summary>
        public static async Task<(double DivNs, double MulNs, int Mismatches)> DivisionCostAsync(Accelerator acc, bool checkExact)
        {
            var input = new float[DivisionCostThreads];
            var rng = new Random(7);
            for (int t = 0; t < input.Length; t++) input[t] = (float)(rng.NextDouble() * 100);
            using var io = acc.Allocate1D<float>(DivisionCostThreads);
            async Task<(double, float[])> Time(Action<Index1D, ArrayView<float>, int> method)
            {
                var k = acc.LoadAutoGroupedStreamKernel(method);
                double best = double.MaxValue;
                float[] result = Array.Empty<float>();
                for (int r = 0; r < 4; r++)
                {
                    io.View.CopyFromCPU(input);
                    await acc.SynchronizeAsync();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    k((Index1D)DivisionCostThreads, io.View, DivisionCostIterations);
                    await acc.SynchronizeAsync();
                    sw.Stop();
                    if (r > 0) best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                    if (r == 3) result = await io.CopyToHostAsync<float>();
                }
                return (best * 1e6 / ((double)DivisionCostThreads * DivisionCostIterations), result);
            }
            var (divNs, divOut) = await Time(DivisionCostKernel);
            var (mulNs, _) = await Time(MultiplyCostKernel);
            int bad = 0;
            if (checkExact)
                for (int t = 0; t < input.Length; t++)
                    if (BitConverter.SingleToInt32Bits(divOut[t]) != BitConverter.SingleToInt32Bits(DivisionCostHost(input[t], DivisionCostIterations))) bad++;
            return (divNs, mulNs, bad);
        }

        [TestMethod]
        public async Task DivisionCost_CorrectlyRounded_Report() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.CPU)
                throw new UnsupportedTestException("CPU accelerator: the debugging backend runs one OS thread per GPU thread; the probe is for the compiled backends");
            var (div, mul, bad) = await DivisionCostAsync(accelerator, checkExact: true);
            string F(double v) => v.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);
            Console.WriteLine($"[report] {accelerator.AcceleratorType} division cost, ns per op ({DivisionCostThreads} threads x {DivisionCostIterations}): divide {F(div)}, multiply {F(mul)}");
            if (bad > 0)
                throw new Exception($"{accelerator.AcceleratorType}: {bad} of {DivisionCostThreads} division chains differ from .NET");
        });
    }
}
