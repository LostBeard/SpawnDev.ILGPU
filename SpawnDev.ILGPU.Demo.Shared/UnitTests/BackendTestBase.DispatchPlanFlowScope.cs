using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A dispatch-plan recording belongs to the async flow that called BeginDispatchCapture. Before 2026-11-08 it
    // was accelerator-wide: with AssistZ's video and audio loops running, YOLO's capture recorded 280 dispatches
    // where a quiet capture gives 250 (every replay re-ran the other loops' kernels), and the video loop's
    // external-image copy threw "cannot run while a WebGPU dispatch plan is recording" from a flow that was not
    // recording anything (Riker, 2026-11-06).
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// A second async flow, started BEFORE the capture, dispatches in the middle of the capture window.
        /// The plan must hold only the capturing flow's 3 dispatches; the other flow must see no active plan
        /// and its work must execute (correct output) but must NOT be re-run by a replay.
        /// </summary>
        [TestMethod]
        public async Task DispatchPlan_Capture_IgnoresOtherAsyncFlows() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webGpu)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: dispatch-plan capture is the WebGPU replay primitive");

            const int n = 4096;
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(PlanScaleKernel);
            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int>(PlanAddKernel);
            var k3 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(PlanSquareKernel);

            using var a = accelerator.Allocate1D<float>(n);
            using var b = accelerator.Allocate1D<float>(n);
            using var c = accelerator.Allocate1D<float>(n);
            using var d = accelerator.Allocate1D<float>(n);
            using var x = accelerator.Allocate1D<float>(n);
            using var y = accelerator.Allocate1D<float>(n);

            float[] MakeInput(int seed)
            {
                var rng = new Random(seed);
                var input = new float[n];
                for (int i = 0; i < n; i++) input[i] = (float)(rng.NextDouble() * 4 - 2);
                return input;
            }

            async Task Verify(MemoryBuffer1D<float, Stride1D.Dense> buf, Func<int, float> expected, string label)
            {
                var got = await buf.CopyToHostAsync<float>();
                for (int i = 0; i < n; i++)
                {
                    var e = expected(i);
                    if (MathF.Abs(got[i] - e) > MathF.Abs(e) * 1e-5f + 1e-4f)
                        throw new Exception($"{label}: mismatch at [{i}]: expected {e}, got {got[i]}");
                }
            }

            var inputA1 = MakeInput(11);
            var inputX1 = MakeInput(12);
            a.View.CopyFromCPU(inputA1);
            x.View.CopyFromCPU(inputX1);

            // The "other loop": started (and suspended) BEFORE the capture, so its ExecutionContext carries no
            // recording - exactly like AssistZ's video/audio loops.
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            WebGPUDispatchPlan? planSeenByOtherFlow = null;
            async Task OtherFlow()
            {
                await go.Task;
                planSeenByOtherFlow = webGpu.ActiveDispatchPlan;   // asserted LAST, so a red-check proves the count + replay checks
                k3((Index1D)n, x.View, y.View);   // y = x^2, dispatched INSIDE the capture window
                k3((Index1D)n, x.View, y.View);
            }
            var other = OtherFlow();

            var plan = webGpu.BeginDispatchCapture();
            if (!ReferenceEquals(webGpu.ActiveDispatchPlan, plan))
                throw new Exception("the capturing flow does not see its own plan as active");
            k1((Index1D)n, a.View, b.View);
            go.SetResult();
            await other;                          // the other flow's 2 dispatches run mid-capture
            if (!ReferenceEquals(webGpu.ActiveDispatchPlan, plan))
                throw new Exception("the capturing flow lost its plan across an await");
            k2((Index1D)n, b.View, c.View, 10);
            k3((Index1D)n, c.View, d.View);
            var sealedPlan = webGpu.EndDispatchCapture();
            await accelerator.SynchronizeAsync();

            using (plan)
            {
                if (!ReferenceEquals(plan, sealedPlan)) throw new Exception("EndDispatchCapture returned a different plan");
                if (plan.DispatchCount != 3)
                    throw new Exception($"expected the capturing flow's 3 dispatches, the plan recorded {plan.DispatchCount} (another flow's dispatches leaked into the recording)");
                await Verify(d, i => PlanExpected(inputA1[i]), "capture pass");
                await Verify(y, i => inputX1[i] * inputX1[i], "other flow's mid-capture dispatch");

                // Replay with fresh inputs in BOTH flows' sources: d must follow a, y must NOT follow x (a replay
                // that re-runs the other flow's kernel would overwrite y with the new x squared).
                var inputA2 = MakeInput(21);
                var inputX2 = MakeInput(22);
                a.View.CopyFromCPU(inputA2);
                x.View.CopyFromCPU(inputX2);
                var encoded = await plan.ReplayAsync();
                await accelerator.SynchronizeAsync();
                await Verify(d, i => PlanExpected(inputA2[i]), "replay");
                await Verify(y, i => inputX1[i] * inputX1[i], "other flow's output after replay (must be untouched)");
                if (encoded != 3) throw new Exception($"replay encoded {encoded} dispatches, expected 3");
                if (planSeenByOtherFlow != null)
                    throw new Exception("a flow that did not begin the capture saw it as active (recording is not flow-scoped)");
            }
        });

        /// <summary>
        /// BeginDispatchCapture inside an async helper does not carry the recording back to the helper's caller
        /// (an async method's AsyncLocal change is undone on return). That must fail LOUD at EndDispatchCapture,
        /// not hand back an empty plan, and must leave the accelerator able to capture again.
        /// </summary>
        [TestMethod]
        public async Task DispatchPlan_EndFromNonOwnerFlow_ThrowsAndFreesAccelerator() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webGpu)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: dispatch-plan capture is the WebGPU replay primitive");

            const int n = 1024;
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(PlanScaleKernel);
            using var a = accelerator.Allocate1D<float>(n);
            using var b = accelerator.Allocate1D<float>(n);

            async Task<WebGPUDispatchPlan> BeginInHelper()
            {
                await Task.Yield();
                return webGpu.BeginDispatchCapture();
            }
            var orphan = await BeginInHelper();
            if (webGpu.ActiveDispatchPlan != null)
                throw new Exception("a capture begun inside an async helper leaked out to the caller's flow");
            k1((Index1D)n, a.View, b.View);
            if (orphan.DispatchCount != 0)
                throw new Exception($"the caller's dispatch was recorded into a plan its flow does not own ({orphan.DispatchCount})");

            bool threw = false;
            try { webGpu.EndDispatchCapture(); }
            catch (InvalidOperationException) { threw = true; }
            if (!threw) throw new Exception("EndDispatchCapture from a non-owner flow returned a plan instead of throwing");

            // The accelerator is free again: a correct capture works.
            var plan = webGpu.BeginDispatchCapture();
            k1((Index1D)n, a.View, b.View);
            using var sealedPlan = webGpu.EndDispatchCapture();
            if (sealedPlan.DispatchCount != 1) throw new Exception($"recapture recorded {sealedPlan.DispatchCount}, expected 1");
            await accelerator.SynchronizeAsync();
        });
    }
}
