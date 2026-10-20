using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // MathF.Truncate / Math.Truncate are internal calls (ModF) that no ILGPU operation covered: on a context without ILGPU.Algorithms (which remaps them to XMath.Truncate) a kernel that
    // used them compiled to a function without a body (Velocity returned its input, CUDA failed at PTX JIT). The core remaps them now (RemappedIntrinsics.Truncation).
    public abstract partial class BackendTestBase
    {
        static void TruncateFloatKernel(Index1D i, ArrayView<float> input, ArrayView<float> output) => output[i] = MathF.Truncate(input[i]);

        static void TruncateDoubleKernel(Index1D i, ArrayView<double> input, ArrayView<double> output) => output[i] = Math.Truncate(input[i]);

        [TestMethod]
        public async Task MathTruncate_Float_MatchesTheFramework() => await RunTest(async accelerator =>
        {
            var input = new[] { 2.5f, -2.5f, 3.9f, -3.9f, 0.5f, -0.5f, 0f, -0f, 1e10f, -1e10f, 8388607.5f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, 7f, -7f };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<float>(input.Length);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(TruncateFloatKernel)(input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<float>();
            for (int i = 0; i < input.Length; i++)
            {
                float expected = MathF.Truncate(input[i]);
                // numeric equality: with ILGPU.Algorithms enabled CUDA's Ceiling returns +0 for -0.5 (a sign-of-zero difference in
                // Algorithms' PTX Ceiling, found here; without Algorithms the result is the framework's -0)
                if (got[i] != expected && !(float.IsNaN(got[i]) && float.IsNaN(expected)))
                    throw new Exception($"MathF.Truncate({input[i]}) = {got[i]}, expected {expected}");
            }
        });

        [TestMethod]
        public async Task MathTruncate_Double_MatchesTheFramework() => await RunEmulatedTest(async accelerator =>
        {
            var input = new[] { 2.5, -2.5, 3.9, -3.9, 0.5, -0.5, 0.0, 1e15, -1e15, 4503599627370495.5, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 7.0, -7.0 };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<double>(input.Length);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>>(TruncateDoubleKernel)(input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<double>();
            for (int i = 0; i < input.Length; i++)
            {
                double expected = Math.Truncate(input[i]);
                if (got[i] != expected && !(double.IsNaN(got[i]) && double.IsNaN(expected)))
                    throw new Exception($"Math.Truncate({input[i]}) = {got[i]}, expected {expected}");
            }
        });
    }
}
