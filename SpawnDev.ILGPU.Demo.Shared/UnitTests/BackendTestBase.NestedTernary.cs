using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-22 (SpawnDev.Codecs VP8 loop filter, found by Geordi): `int h = level >= 40 ? 2 : (level >= 15 ? 1 : 0);`
    // returned 1 for level 40..63 on CUDA and OpenCL (CPU correct). The if-chain and the sum of two ternaries were right.
    public abstract partial class BackendTestBase
    {
        private static void NestedTernaryKernel(Index1D i, ArrayView<int> levels, ArrayView<int> outNested, ArrayView<int> outIf, ArrayView<int> outSum)
        {
            int level = levels[i];
            int nested = level >= 40 ? 2 : (level >= 15 ? 1 : 0);
            int viaIf = 0;
            if (level >= 15) viaIf = 1;
            if (level >= 40) viaIf = 2;
            int sum = (level >= 40 ? 1 : 0) + (level >= 15 ? 1 : 0);
            outNested[i] = nested;
            outIf[i] = viaIf;
            outSum[i] = sum;
        }

        [TestMethod]
        public async Task NestedTernary_ThresholdChain_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // WebGL writes one output element per thread at its own slot; this kernel stores to three buffers (one each): fine.
            const int N = 64;
            var levels = new int[N];
            for (int k = 0; k < N; k++) levels[k] = k;

            using var lvBuf = accelerator.Allocate1D(levels);
            using var nestedBuf = accelerator.Allocate1D(new int[N]);
            using var ifBuf = accelerator.Allocate1D(new int[N]);
            using var sumBuf = accelerator.Allocate1D(new int[N]);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                NestedTernaryKernel);
            kernel(N, lvBuf.View, nestedBuf.View, ifBuf.View, sumBuf.View);
            await accelerator.SynchronizeAsync();
            var nested = await nestedBuf.CopyToHostAsync<int>();
            var viaIf = await ifBuf.CopyToHostAsync<int>();
            var sum = await sumBuf.CopyToHostAsync<int>();

            for (int k = 0; k < N; k++)
            {
                int want = k >= 40 ? 2 : (k >= 15 ? 1 : 0);
                if (nested[k] != want)
                    throw new Exception($"NestedTernary: nested ternary wrong at level {k}: expected {want}, got {nested[k]} (if-chain gave {viaIf[k]}, sum gave {sum[k]}).");
                if (viaIf[k] != want)
                    throw new Exception($"NestedTernary: if-chain wrong at level {k}: expected {want}, got {viaIf[k]}.");
                if (sum[k] != want)
                    throw new Exception($"NestedTernary: sum of ternaries wrong at level {k}: expected {want}, got {sum[k]}.");
            }
        });
    }
}
