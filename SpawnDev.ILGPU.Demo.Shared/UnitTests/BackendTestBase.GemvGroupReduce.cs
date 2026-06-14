using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression guard for the WebGPU GEMV grid-stride emitter bug (plan #4).
    // Shape that triggered it: an explicitly-grouped kernel (LoadStreamKernel + KernelConfig(N, G))
    // where Grid.IdxX is the output column, Group.IdxX is the lane, and an inner strided K-loop
    // `for (k = tid; k < K; k += G)` accumulates a partial, then a shared-mem tree reduces to output[n].
    // The K-loop is BARRIER-FREE (the reduction's barriers are AFTER it), so the WGSL uniform-break
    // transform must NOT fire on it — pre-fix it conflated the inner K-loop break with a synthetic group
    // grid-stride counter -> ~K/G x too small (partial accumulation), WebGPU-only. The two-pass
    // uniform-break (decide from the EMITTED body's barriers) keeps the natural `k < K` break here while
    // barrier-containing grid-stride loops (scan/radix) still get the uniform break. CPU-referenced,
    // all 6 backends. This is the same plain-float GEMV as FusedDequantMatMul's M==1 path, minus decode.
    public abstract partial class BackendTestBase
    {
        private const int GemvReproGroupSize = 64;

        // output[n] = sum_k input[k] * matrix[n*K + k]   (row-major weight, one group per output column n)
        private static void GemvGroupReduceKernel(
            ArrayView<float> input,    // [K]
            ArrayView<float> matrix,   // [N*K]
            ArrayView<float> output,   // [N]
            ArrayView<int> p)          // [K, N]
        {
            int K = p[0], N = p[1];
            int n = Grid.IdxX;        // one group per output column
            int tid = Group.IdxX;     // 0..GemvReproGroupSize-1

            var sh = SharedMemory.Allocate<float>(GemvReproGroupSize);
            float partial = 0f;
            if (n < N)
            {
                int rowBase = n * K;
                for (int k = tid; k < K; k += GemvReproGroupSize)
                    partial += input[k] * matrix[rowBase + k];
            }
            sh[tid] = partial;
            Group.Barrier();

            for (int stride = GemvReproGroupSize / 2; stride > 0; stride >>= 1)
            {
                if (tid < stride) sh[tid] += sh[tid + stride];
                Group.Barrier();
            }
            if (tid == 0 && n < N) output[n] = sh[0];
        }

        [TestMethod]
        public async Task GemvGroupReduce_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // The kernel uses SharedMemory.Allocate + Group.Barrier (a cooperative tree reduction),
            // which WebGL (Transform-Feedback, no shared memory / no barriers) cannot do — the real ML
            // M==1 GEMV is excluded on WebGL for the same reason (per-element fallback). Structural skip.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("In-kernel shared-memory reduction is structurally unsupported on WebGL.");

            const int K = 512;   // 8 K-tiles of 64 — a short (conflated) loop reads ~1 of 8 tiles
            const int N = 96;    // > 1 column so the per-column grid-stride bug bites n>=1

            var input = new float[K];
            var matrix = new float[N * K];
            for (int k = 0; k < K; k++) input[k] = (k % 7) * 0.5f - 1.0f;
            for (int i = 0; i < N * K; i++) matrix[i] = ((i % 13) - 6) * 0.25f;

            using var inBuf = accelerator.Allocate1D(input);
            using var matBuf = accelerator.Allocate1D(matrix);
            using var outBuf = accelerator.Allocate1D<float>(N);
            using var pBuf = accelerator.Allocate1D(new int[] { K, N });

            var kernel = accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<int>>(
                GemvGroupReduceKernel);
            kernel(new KernelConfig(N, GemvReproGroupSize), inBuf.View, matBuf.View, outBuf.View, pBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<float>();

            for (int n = 0; n < N; n++)
            {
                double expected = 0;
                for (int k = 0; k < K; k++) expected += (double)input[k] * matrix[n * K + k];
                if (Math.Abs(result[n] - expected) > 1e-2)
                    throw new Exception(
                        $"GEMV mismatch at column {n}: expected {expected:F4}, got {result[n]:F4} " +
                        $"(ratio {(expected != 0 ? result[n] / expected : 0):F3} — a short K-loop reads ~1 of {K / GemvReproGroupSize} tiles).");
            }
        });
    }
}
