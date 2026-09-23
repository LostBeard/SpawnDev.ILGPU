using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A loop that runs far past 100000 iterations. WebGL emitted every structured loop as
    // `for (int _loopN = 0; _loopN < 100000; _loopN++)` (a guard against ANGLE's while(true)), which
    // silently ended any longer loop early - wrong results, no error. The guard is now the
    // u_loopLimit uniform (int.MaxValue); being a uniform also keeps D3D's FXC from running its
    // superlinear trip-count analysis (see WebGLBackend.CreateKernelBuilder).
    public abstract partial class BackendTestBase
    {
        const int LongLoopIterations = 250_000;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint LongLoopMix(uint seed, int iterations)
        {
            uint acc = seed;
            for (int k = 0; k < iterations; k++)
                acc = acc * 1664525u + (uint)k;
            return acc;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static uint LongLoopMixHelper(uint seed, int iterations) => LongLoopMix(seed, iterations);

        // The trip count is a kernel argument so LoopUnrolling cannot resolve it.
        static void LongLoopKernel(Index1D i, ArrayView<uint> inKernel, ArrayView<uint> inHelper, int iterations)
        {
            inKernel[i] = LongLoopMix((uint)i * 2654435761u, iterations);
            inHelper[i] = LongLoopMixHelper((uint)i * 2654435761u, iterations);
        }

        [TestMethod]
        public async Task Loop_RunsPast100000Iterations_KernelAndNoInliningHelper() => await RunTest(async accelerator =>
        {
            const int n = 64;
            using var a = accelerator.Allocate1D<uint>(n);
            using var b = accelerator.Allocate1D<uint>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>, int>(LongLoopKernel);
            k(n, a.View, b.View, LongLoopIterations);
            await accelerator.SynchronizeAsync();
            var ra = await a.CopyToHostAsync();
            var rb = await b.CopyToHostAsync();
            for (int i = 0; i < n; i++)
            {
                uint want = LongLoopMix((uint)i * 2654435761u, LongLoopIterations);
                if (ra[i] != want || rb[i] != want)
                    throw new Exception(
                        $"{LongLoopIterations}-iteration loop on {BackendName}, thread {i}: kernel 0x{ra[i]:x8}, " +
                        $"helper 0x{rb[i]:x8}, expected 0x{want:x8}");
            }
            Console.WriteLine($"[Loop] {LongLoopIterations}-iteration loop on {BackendName}: kernel and helper ✓");
        });
    }
}
