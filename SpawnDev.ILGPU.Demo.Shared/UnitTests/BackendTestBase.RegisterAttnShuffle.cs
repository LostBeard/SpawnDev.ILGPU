using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression guard for the WebGPU register per-query attention codegen (Tuvok 2026-06-23,
    // tuvok-to-geordi-WEBGPU-register-attention-wgsl-invalid-pipeline). That kernel produced an
    // INVALID WebGPU ComputePipeline. This reproduces its exact codegen shape in the library so
    // the suite catches it on real Dawn without the ML consumer:
    //   - a const-16 `new float[16]` per-lane REGISTER accumulator (unrolls -> scalar-replace),
    //   - a `Warp.ShuffleXor` butterfly reduce of a partial dot across nLanes cooperating lanes,
    //   - inside a RUNTIME kv loop whose bound comes from a storage buffer.
    //
    // Two bugs this locks down:
    //  (1) WGSL dropped ShuffleOperation.Kind -> Warp.ShuffleXor emitted plain subgroupShuffle
    //      (absolute lane) instead of subgroupShuffleXor (id ^ mask) = wrong butterfly. Fixed
    //      2026-06-23 (WGSLCodeGenerator SubgroupShuffleBuiltin + per-kind emulation source lane).
    //  (2) the pipeline validity itself on Dawn (if subgroup uniformity ever regresses, the
    //      WebGPU lane fails here at pipeline creation with Dawn's actual validation error).
    public abstract partial class BackendTestBase
    {
        private const int RAS_WARP = 32;   // one warp = block
        private const int RAS_NLANES = 4;  // lanes cooperating per query (D/16, power of two)
        private const int RAS_T = 16;      // per-lane const register tile
        private const int RAS_ITERS = 3;   // runtime kv-loop bound (from a storage buffer)

        // One 32-lane warp. Each lane owns a const-16 register slice; the per-iteration partial
        // is butterfly-reduced across the aligned group of RAS_NLANES lanes via Warp.ShuffleXor.
        private static void RegisterAttnShuffleKernel(
            Index1D _, ArrayView<float> input, ArrayView<float> output, ArrayView<int> p)
        {
            int lane = Group.IdxX;          // 0..31
            int nLanes = p[0];              // runtime (storage buffer) -> exercises uniformity
            int iters = p[1];

            var acc = new float[RAS_T];     // const-16 register accumulator
            for (int d = 0; d < RAS_T; d++) acc[d] = 0f;

            for (int j = 0; j < iters; j++)
            {
                float pd = 0f;
                for (int d = 0; d < RAS_T; d++) pd += input[lane * RAS_T + d] * (float)(j + 1);
                for (int off = nLanes / 2; off > 0; off >>= 1) pd += Warp.ShuffleXor(pd, off);
                for (int d = 0; d < RAS_T; d++) acc[d] += pd;
            }

            for (int d = 0; d < RAS_T; d++) output[lane * RAS_T + d] = acc[d];
        }

        [TestMethod]
        public async Task RegisterAttnShuffleXor_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            RequireFeature(accelerator, "subgroup_shuffle",
                "Register-attention butterfly (Warp.ShuffleXor) requires subgroup/warp shuffle.");

            // WebGL cannot do cross-lane warp shuffle - no shared memory / barriers in the
            // Transform-Feedback vertex model (same structural limit as in-kernel group
            // Scan/Reduce). Its WarpShuffle codegen is a no-op fallback (returns the lane's own
            // value), so this kernel is genuinely unsupported there. (Capability genuinely
            // impossible on the backend = the one allowed skip. The silent no-op fallback ought
            // to fail loud like Scan/Reduce - tracked separately.)
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException(
                    "WebGL cannot do cross-lane Warp.ShuffleXor (no shared memory/barriers in Transform Feedback).");

            int total = RAS_WARP * RAS_T; // 512
            var input = new float[total];
            for (int i = 0; i < total; i++) input[i] = (i % 13) * 0.25f - 1.5f;

            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<float>(total);
            using var pBuf = accelerator.Allocate1D(new int[] { RAS_NLANES, RAS_ITERS });

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>>(
                RegisterAttnShuffleKernel);
            kernel(new KernelConfig(1, RAS_WARP), (Index1D)RAS_WARP, inBuf.View, outBuf.View, pBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<float>();

            // CPU oracle: per-lane base sum; ShuffleXor over a power-of-two nLanes reduces the
            // aligned group {lane & ~(nLanes-1) .. +nLanes-1}; each lane in the group gets the
            // group sum; acc accumulates sum_j (j+1)*groupSum.
            var laneBase = new float[RAS_WARP];
            for (int lane = 0; lane < RAS_WARP; lane++)
            {
                float s = 0f;
                for (int d = 0; d < RAS_T; d++) s += input[lane * RAS_T + d];
                laneBase[lane] = s;
            }
            for (int lane = 0; lane < RAS_WARP; lane++)
            {
                int gbase = lane & ~(RAS_NLANES - 1);
                float groupSum = 0f;
                for (int l = gbase; l < gbase + RAS_NLANES; l++) groupSum += laneBase[l];
                float expected = 0f;
                for (int j = 0; j < RAS_ITERS; j++) expected += (j + 1) * groupSum;
                for (int d = 0; d < RAS_T; d++)
                {
                    float got = result[lane * RAS_T + d];
                    if (Math.Abs(got - expected) > 1e-2)
                        throw new Exception(
                            $"RegisterAttnShuffleXor mismatch at lane {lane}, d {d}: expected {expected:F4}, got {got:F4}. " +
                            $"(ShuffleXor butterfly + const-16 register accumulator.)");
                }
            }
        });
    }
}
