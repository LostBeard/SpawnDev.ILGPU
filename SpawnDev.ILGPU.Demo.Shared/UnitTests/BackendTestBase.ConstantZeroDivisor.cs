using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // An integer division and remainder by a constant zero in a branch the kernel never takes. GLSL leaves integer
    // division by zero undefined, and on WebGL (ANGLE, D3D) the compiler acted on it at compile time: the shader stored
    // nothing, every element kept its old value and no error was raised (SpawnDev.Codecs VorbisResidueAccumulateGpu: its
    // lookup-type-1 branch divides by a quantvals of 0 the caller passes as a literal). The GLSL generator now emits WGSL's
    // defined results for a constant zero divisor. Every backend, against a CPU oracle; the output starts nonzero.
    public abstract partial class BackendTestBase
    {
        static void ConstantZeroDivisor_Kernel(Index1D idx, ArrayView<int> input, ArrayView<int> output, int mode)
        {
            int zero = 0;
            int x = input[idx];
            if (mode == 1)
                output[idx] = x / zero + x % zero;
            else
                output[idx] = x * 3 + 1;
        }

        [TestMethod]
        public async Task IntDivision_ByConstantZeroInUntakenBranch_StoresStillLand() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 64;
            var input = new int[n];
            var initial = new int[n];
            for (int i = 0; i < n; i++) { input[i] = i * 7 - 100; initial[i] = -5000 - i; }
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D(initial);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(ConstantZeroDivisor_Kernel)(
                (Index1D)n, dIn.View, dOut.View, 2);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != input[i] * 3 + 1) throw new Exception($"output[{i}] = {got[i]}, expected {input[i] * 3 + 1}");
        });
    }
}
