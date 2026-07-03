using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WebGPU dispatch-plan capture/replay (the browser twin of CUDA graph capture): a capture pass
    // records every dispatch [pipeline, bindGroup, dims] into a JS-side plan; ReplayAsync re-encodes
    // the whole sequence with ONE interop crossing. Guards: recording correctness, scalar-params
    // buffer retention (a replayed dispatch must see ITS scalars, not a pool-recycled overwrite),
    // replay-reads-fresh-input semantics, repeatability, and post-dispose accelerator health.
    public abstract partial class BackendTestBase
    {
        static void PlanScaleKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst)
            => dst[i] = src[i] * 2f;

        static void PlanAddKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst, int addend)
            => dst[i] = src[i] + addend;

        static void PlanSquareKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst)
            => dst[i] = src[i] * src[i];

        static float PlanExpected(float a) { var b = a * 2f; var c = b + 10; return c * c; }

        /// <summary>
        /// Captures a 3-kernel chained sequence (including a scalar-parameter kernel) on WebGPU,
        /// then replays it twice against FRESH input data and verifies outputs match the CPU
        /// reference each time - proving the plan re-executes real compute against current buffer
        /// contents with a single interop crossing. Also verifies the accelerator dispatches
        /// normally after the plan is disposed (retained scalar buffers returned to the pool).
        /// </summary>
        [TestMethod]
        public async Task DispatchPlan_CaptureReplay_MultiKernel_MatchesCPU() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webGpu)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: dispatch-plan capture is the WebGPU replay primitive (CUDA has graph capture; Wasm/WebGL batching is separate)");

            const int n = 4096;
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(PlanScaleKernel);
            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int>(PlanAddKernel);
            var k3 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(PlanSquareKernel);

            using var a = accelerator.Allocate1D<float>(n);
            using var b = accelerator.Allocate1D<float>(n);
            using var c = accelerator.Allocate1D<float>(n);
            using var d = accelerator.Allocate1D<float>(n);

            void RunChain()
            {
                k1((Index1D)n, a.View, b.View);
                k2((Index1D)n, b.View, c.View, 10);
                k3((Index1D)n, c.View, d.View);
            }

            float[] MakeInput(int seed)
            {
                var rng = new Random(seed);
                var input = new float[n];
                for (int i = 0; i < n; i++) input[i] = (float)(rng.NextDouble() * 4 - 2);
                return input;
            }

            async Task VerifyOutput(float[] input, string label)
            {
                var got = await d.CopyToHostAsync<float>();
                for (int i = 0; i < n; i++)
                {
                    var expected = PlanExpected(input[i]);
                    if (MathF.Abs(got[i] - expected) > MathF.Abs(expected) * 1e-5f + 1e-4f)
                        throw new Exception($"{label}: mismatch at [{i}]: expected {expected}, got {got[i]}");
                }
            }

            // Capture pass: executes normally AND records.
            var input1 = MakeInput(1);
            a.View.CopyFromCPU(input1);
            var plan = webGpu.BeginDispatchCapture();
            RunChain();
            var sealedPlan = webGpu.EndDispatchCapture();
            await accelerator.SynchronizeAsync();
            if (!ReferenceEquals(plan, sealedPlan)) throw new Exception("EndDispatchCapture returned a different plan");
            if (plan.DispatchCount != 3) throw new Exception($"expected 3 recorded dispatches, got {plan.DispatchCount}");
            await VerifyOutput(input1, "capture pass");

            using (plan)
            {
                // Replay 1: FRESH input written into the captured input buffer; one crossing re-runs the chain.
                var input2 = MakeInput(2);
                a.View.CopyFromCPU(input2);
                var encoded = await plan.ReplayAsync();
                if (encoded != 3) throw new Exception($"replay encoded {encoded} dispatches, expected 3");
                await accelerator.SynchronizeAsync();
                await VerifyOutput(input2, "replay 1");

                // Replay 2: repeatability with a third input.
                var input3 = MakeInput(3);
                a.View.CopyFromCPU(input3);
                await plan.ReplayAsync();
                await accelerator.SynchronizeAsync();
                await VerifyOutput(input3, "replay 2");

                // Replay 3: the captured input refreshed by a KERNEL DISPATCH (not a writeBuffer)
                // with NO sync in between - the video-pipeline pattern (per-frame preprocess into
                // the stable input, then replay). ReplayAsync must flush the accelerator's pending
                // encoder BEFORE its own submit, or the replay reads the PREVIOUS data (the
                // 2026-07-03 stale-replay bug, caught by the DA3 video-path gate).
                var input5 = MakeInput(9);
                var input5Halved = new float[n];
                for (int i = 0; i < n; i++) input5Halved[i] = input5[i] / 2f;
                using (var staging = accelerator.Allocate1D<float>(n))
                {
                    staging.View.CopyFromCPU(input5Halved);
                    k1((Index1D)n, staging.View, a.View);   // a = 2*staging = input5, PENDING in the encoder
                    await plan.ReplayAsync();               // must flush the pending dispatch first
                    await accelerator.SynchronizeAsync();
                    await VerifyOutput(input5, "replay 3 (dispatch-written input, no pre-sync)");
                }

                // Timed replay: per-pass GPU timestamps aggregated by pipeline label. Must stay
                // CORRECT (it re-runs the same dispatches - output must match) and, when the device
                // has 'timestamp-query', report all 3 passes with labels. Skip-quietly when the
                // feature is absent (JSON says so) - correctness is still asserted either way.
                var input4t = MakeInput(5);
                a.View.CopyFromCPU(input4t);
                var timedJson = await plan.ReplayTimedAsync();
                await VerifyOutput(input4t, "timed replay");
                if (webGpu.NativeAccelerator.HasTimestampQuery)
                {
                    // The device HAS the feature - the timed path must fully work, not quietly
                    // report unsupported.
                    if (!timedJson.Contains("\"supported\":true"))
                        throw new Exception($"device has timestamp-query but timed replay reported: {timedJson}");
                    if (!timedJson.Contains("\"passes\":3"))
                        throw new Exception($"timed replay expected 3 passes: {timedJson}");
                    if (!timedJson.Contains("\"kernels\":[") || timedJson.Contains("(unlabeled)"))
                        throw new Exception($"timed replay missing kernel labels (pipeline Label plumbing broke): {timedJson}");
                }
                Console.WriteLine($"[DispatchPlan] timed replay (HasTimestampQuery={webGpu.NativeAccelerator.HasTimestampQuery}): {timedJson}");
            }

            // Post-dispose health: the plan returned its retained scalar buffers; normal dispatch works.
            var input4 = MakeInput(4);
            a.View.CopyFromCPU(input4);
            RunChain();
            await accelerator.SynchronizeAsync();
            await VerifyOutput(input4, "post-dispose direct run");
        });
    }
}
