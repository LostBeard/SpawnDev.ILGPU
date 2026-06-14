using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Repro for Tuvok finding #2: float.PositiveInfinity reported as silently mis-evaluated on the
    // Wasm backend (a +inf sentinel behaved as if +inf ~= 0). Offline disassembly showed the
    // f32.const +inf literal + the f32 comparison opcodes are emitted CORRECTLY, so this test
    // verifies RUNTIME behavior across all 6 backends and ALSO exercises a nested-conditional +
    // sentinel shape (the interpreted-Wasm if/else-if fragility that the TopK kernel actually hit).
    // CPU-oracle checked. If a probe fails ONLY on Wasm, it pins the real trigger.
    public abstract partial class BackendTestBase
    {
        private const int InfProbeCount = 7;

        private static void InfProbesKernel(Index1D i, ArrayView<float> input, ArrayView<float> output)
        {
            if (i != 0) return;

            // [0] literal +inf (verified host-side via float.IsPositiveInfinity)
            output[0] = float.PositiveInfinity;
            // [1] 1.0 < +inf  -> 1
            output[1] = (1.0f < float.PositiveInfinity) ? 1f : 0f;
            // [2] min-init sentinel: best = +inf; min over (all-positive) input
            float best = float.PositiveInfinity;
            for (int k = 0; k < 4; k++)
                if (input[k] < best) best = input[k];
            output[2] = best;
            // [3] x > +inf -> 0
            output[3] = (input[0] > float.PositiveInfinity) ? 1f : 0f;
            // [4] x == +inf -> 0 (finite input)
            output[4] = (input[0] == float.PositiveInfinity) ? 1f : 0f;
            // [5] computed +inf via 1/0 -> +inf
            float zero = input[4]; // = 0f from host
            output[5] = 1.0f / zero;
            // [6] nested-conditional + sentinel (TopK-shape): find min via if(first)/else if(<best)
            float best2 = float.PositiveInfinity;
            int bestIdx = -1;
            for (int k = 0; k < 4; k++)
            {
                if (bestIdx < 0) { best2 = input[k]; bestIdx = k; }
                else if (input[k] < best2) { best2 = input[k]; bestIdx = k; }
            }
            output[6] = best2;
        }

        [TestMethod]
        public async Task WasmInf_PositiveInfinity_BehavesCorrectly() => await RunTest(async accelerator =>
        {
            // all-positive finite input; input[4] = 0 for the computed-inf probe
            var input = new float[] { 3.0f, 7.0f, 2.0f, 5.0f, 0.0f };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<float>(InfProbeCount);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                InfProbesKernel);
            kernel((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();

            var r = await outBuf.CopyToHostAsync<float>();

            if (!float.IsPositiveInfinity(r[0]))
                throw new Exception($"[0] literal +inf wrong: got {r[0]} (IsPosInf={float.IsPositiveInfinity(r[0])})");
            if (r[1] != 1f)
                throw new Exception($"[1] (1.0 < +inf) wrong: got {r[1]}, expected 1 (+inf treated as <=1?)");
            if (Math.Abs(r[2] - 2.0f) > 1e-4)
                throw new Exception($"[2] min-init(+inf sentinel) wrong: got {r[2]}, expected 2 (+inf sentinel not > finite?)");
            if (r[3] != 0f)
                throw new Exception($"[3] (x > +inf) wrong: got {r[3]}, expected 0");
            if (r[4] != 0f)
                throw new Exception($"[4] (finite == +inf) wrong: got {r[4]}, expected 0");
            if (!float.IsPositiveInfinity(r[5]))
                throw new Exception($"[5] 1.0/0.0 wrong: got {r[5]}, expected +inf");
            if (Math.Abs(r[6] - 2.0f) > 1e-4)
                throw new Exception($"[6] nested-cond +inf sentinel min wrong: got {r[6]}, expected 2 (if/else-if mis-exec?)");
        });
    }
}
