using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // MathF.Sin / MathF.Cos of large arguments (rotary positions: a few hundred to a few thousand radians). WebGPU / WebGL
    // emitted the native sin / cos, which D3D evaluates accurately only near [-pi, pi]: SpawnDev.ILGPU.ML's rope tables were
    // 3.8e-5 to 4.1e-5 off at a few hundred radians on WebGPU / WebGL, 2e-5 everywhere else (Tuvok, 2026-11-15).
    public abstract partial class BackendTestBase
    {
        public static void TrigSinKernel(Index1D i, ArrayView<float> x, ArrayView<float> y) => y[i] = MathF.Sin(x[i]);
        public static void TrigCosKernel(Index1D i, ArrayView<float> x, ArrayView<float> y) => y[i] = MathF.Cos(x[i]);

        [TestMethod]
        public async Task Trig_LargeArguments_MatchHost() => await RunTest(async accelerator =>
        {
            var rnd = new Random(20261115);
            const int n = 1 << 16;
            var small = new float[n];
            var large = new float[n];
            for (int k = 0; k < n; k++)
            {
                small[k] = (float)((rnd.NextDouble() * 2 - 1) * Math.PI);
                large[k] = (float)((rnd.NextDouble() * 2 - 1) * 2000.0);
            }
            var report = "";
            bool failed = false;
            // CUDA / OpenCL / Wasm: 5.96e-8 at most (the double result rounded to float); 2.5e-7 leaves room for ~2 ulp
            foreach (var (name, kernelMethod, host) in new (string, Action<Index1D, ArrayView<float>, ArrayView<float>>, Func<float, float>)[]
                { ("sin", TrigSinKernel, MathF.Sin), ("cos", TrigCosKernel, MathF.Cos) })
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel(kernelMethod);
                foreach (var (range, input) in new[] { ("|x|<=pi", small), ("|x|<=2000", large) })
                {
                    using var dX = accelerator.Allocate1D(input);
                    using var dY = accelerator.Allocate1D<float>(n);
                    kernel(n, dX.View, dY.View);
                    var got = await dY.CopyToHostAsync<float>();
                    double max = 0; float at = 0;
                    for (int k = 0; k < n; k++)
                    {
                        double e = Math.Abs((double)got[k] - host(input[k]));
                        if (e > max) { max = e; at = input[k]; }
                    }
                    report += $" {name} {range}: max abs error {max:E2} at x={at:R};";
                    failed |= max > 2.5e-7;
                }
            }
            if (failed)
                throw new Exception($"{accelerator.AcceleratorType} sin / cos off by more than 2.5e-7:{report}");
        });
    }
}
