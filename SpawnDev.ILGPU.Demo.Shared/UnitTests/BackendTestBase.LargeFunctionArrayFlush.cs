using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WORKAROUND guard for https://github.com/LostBeard/SpawnDev.ILGPU/issues/10 (upstream crbug 585125915). On an NVIDIA
    // RTX 4070 (Chrome / Edge, D3D12) a kernel with more than 6608 bytes of function-scope arrays per thread, followed by
    // any other dispatch in the same submit, leaves the device to die on a later large kernel (DXGI_ERROR_DEVICE_HUNG).
    // WebGPUBackend.NvidiaLargeFunctionArrayFlush submits right after such a kernel on NVIDIA. The hang itself cannot be a
    // unit test (it resets the display driver); these pin the mechanics: WHERE the submit falls, that nothing else
    // flushes, that a captured plan keeps the boundary, and that the WGSL measure is right. Results are checked against
    // a CPU oracle on every backend.
    public abstract partial class BackendTestBase
    {
        const int LargeFnArrayLength = 1100;   // 4400 bytes per thread: over the 4096 default threshold
        const int SmallFnArrayLength = 512;    // 2048 bytes per thread: under it

        static void LargeFnArrayKernel(Index1D i, ArrayView<int> o)
        {
            var a = LocalMemory.Allocate<int>(LargeFnArrayLength);
            for (int k = 0; k < LargeFnArrayLength; k++) a[k] = k * 3 + i;
            int sum = 0;
            for (int k = 0; k < LargeFnArrayLength; k++) sum += a[(k * 7 + i) % LargeFnArrayLength];
            o[i] = sum;
        }

        static void SmallFnArrayKernel(Index1D i, ArrayView<int> o)
        {
            var a = LocalMemory.Allocate<int>(SmallFnArrayLength);
            for (int k = 0; k < SmallFnArrayLength; k++) a[k] = k * 3 + i;
            int sum = 0;
            for (int k = 0; k < SmallFnArrayLength; k++) sum += a[(k * 7 + i) % SmallFnArrayLength];
            o[i] = sum;
        }

        static void FnArrayFollowUpKernel(Index1D i, ArrayView<int> src, ArrayView<int> dst) => dst[i] = src[i] + 1;

        static int FnArrayOracle(int i, int length)
        {
            int sum = 0;
            for (int k = 0; k < length; k++) sum += ((k * 7 + i) % length) * 3 + i;
            return sum;
        }

        static void CheckFnArrayOutput(int[] got, int length, int plus, string label)
        {
            for (int i = 0; i < got.Length; i++)
            {
                int want = FnArrayOracle(i, length) + plus;
                if (got[i] != want) throw new Exception($"{label}: [{i}] = {got[i]}, expected {want}");
            }
        }

        /// <summary>The large-array kernel and a dependent follow-up give the oracle's results on every backend.</summary>
        [TestMethod]
        public async Task LargeFunctionArray_ThenFollowUp_MatchesOracle() => await RunTest(async accelerator =>
        {
            const int n = 64;
            using var a = accelerator.Allocate1D<int>(n);
            using var b = accelerator.Allocate1D<int>(n);
            var large = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LargeFnArrayKernel);
            var follow = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(FnArrayFollowUpKernel);
            large((Index1D)n, a.View);
            follow((Index1D)n, a.View, b.View);
            await accelerator.SynchronizeAsync();
            CheckFnArrayOutput(await a.CopyToHostAsync<int>(), LargeFnArrayLength, 0, "large");
            CheckFnArrayOutput(await b.CopyToHostAsync<int>(), LargeFnArrayLength, 1, "follow-up");
        });

        /// <summary>
        /// WebGPU: on NVIDIA the stream submits right after the large-array kernel (before the follow-up is recorded), and
        /// only then; with the switch off, or for a kernel under the threshold, it does not. On other vendors it never does.
        /// </summary>
        [TestMethod]
        public async Task LargeFunctionArray_NvidiaFlush_SubmitsRightAfterKernel() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webGpu)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: the submit boundary is a WebGPU dispatch-path workaround");

            const int n = 64;
            using var a = accelerator.Allocate1D<int>(n);
            using var b = accelerator.Allocate1D<int>(n);
            var large = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LargeFnArrayKernel);
            var small = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(SmallFnArrayKernel);
            var follow = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(FnArrayFollowUpKernel);

            // Warm: compile all three and drain, so no compile-time flush sits inside the measured windows.
            large((Index1D)n, a.View); small((Index1D)n, a.View); follow((Index1D)n, a.View, b.View);
            await accelerator.SynchronizeAsync();

            // The measure the dispatch path uses, read from the compiled WGSL.
            long largeBytes = WgslFunctionArrays.EstimateBytes(WebGPUBackend.WGSLRegistry[nameof(LargeFnArrayKernel)].Source);
            long smallBytes = WgslFunctionArrays.EstimateBytes(WebGPUBackend.WGSLRegistry[nameof(SmallFnArrayKernel)].Source);
            if (largeBytes < LargeFnArrayLength * 4L || largeBytes <= WebGPUBackend.NvidiaLargeFunctionArrayFlushBytes)
                throw new Exception($"large kernel measured {largeBytes} bytes; expected at least {LargeFnArrayLength * 4} and over {WebGPUBackend.NvidiaLargeFunctionArrayFlushBytes}");
            if (smallBytes < SmallFnArrayLength * 4L || smallBytes > WebGPUBackend.NvidiaLargeFunctionArrayFlushBytes)
                throw new Exception($"small kernel measured {smallBytes} bytes; expected {SmallFnArrayLength * 4}..{WebGPUBackend.NvidiaLargeFunctionArrayFlushBytes}");

            bool nvidia = webGpu.IsNvidiaAdapter;
            bool prev = WebGPUBackend.NvidiaLargeFunctionArrayFlush;
            try
            {
                async Task Window(Action<Index1D, ArrayView<int>> kernel, bool switchOn, int length, bool expectFlush, string label)
                {
                    WebGPUBackend.NvidiaLargeFunctionArrayFlush = switchOn;
                    long submits0 = webGpu.StreamSubmitCount, flushes0 = webGpu.LargeFunctionArrayFlushCount;
                    kernel((Index1D)n, a.View);
                    long submitsAfterKernel = webGpu.StreamSubmitCount - submits0;   // before the follow-up is recorded
                    long flushes = webGpu.LargeFunctionArrayFlushCount - flushes0;
                    follow((Index1D)n, a.View, b.View);
                    long submitsBeforeSync = webGpu.StreamSubmitCount - submits0;
                    await accelerator.SynchronizeAsync();
                    if (expectFlush && (flushes != 1 || submitsAfterKernel != 1 || submitsBeforeSync != 1))
                        throw new Exception($"{label}: expected one submit right after the kernel and none after the follow-up; " +
                            $"workaround flushes {flushes}, submits after kernel {submitsAfterKernel}, before sync {submitsBeforeSync}");
                    if (!expectFlush && (flushes != 0 || submitsBeforeSync != 0))
                        throw new Exception($"{label}: expected no submit before the sync; workaround flushes {flushes}, submits {submitsBeforeSync}");
                    CheckFnArrayOutput(await a.CopyToHostAsync<int>(), length, 0, label);
                    CheckFnArrayOutput(await b.CopyToHostAsync<int>(), length, 1, label + " follow-up");
                }

                await Window(large, true, LargeFnArrayLength, nvidia, $"large, switch on ({(nvidia ? "NVIDIA" : "not NVIDIA")})");
                await Window(large, false, LargeFnArrayLength, false, "large, switch off");
                await Window(small, true, SmallFnArrayLength, false, "small, switch on");
            }
            finally
            {
                WebGPUBackend.NvidiaLargeFunctionArrayFlush = prev;
            }
        });

        /// <summary>
        /// WebGPU: a captured plan carries the submit boundary after the large-array kernel (on NVIDIA) and replays to the
        /// oracle's results.
        /// </summary>
        [TestMethod]
        public async Task LargeFunctionArray_DispatchPlan_KeepsSubmitBoundary() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webGpu)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: dispatch-plan capture is the WebGPU replay primitive");

            const int n = 64;
            using var a = accelerator.Allocate1D<int>(n);
            using var b = accelerator.Allocate1D<int>(n);
            var large = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LargeFnArrayKernel);
            var follow = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(FnArrayFollowUpKernel);
            large((Index1D)n, a.View); follow((Index1D)n, a.View, b.View);
            await accelerator.SynchronizeAsync();

            bool prev = WebGPUBackend.NvidiaLargeFunctionArrayFlush;
            WebGPUBackend.NvidiaLargeFunctionArrayFlush = true;
            try
            {
                var plan = webGpu.BeginDispatchCapture();
                try
                {
                    large((Index1D)n, a.View);
                    follow((Index1D)n, a.View, b.View);
                    webGpu.EndDispatchCapture();
                    int wantBoundaries = webGpu.IsNvidiaAdapter ? 1 : 0;
                    if (plan.SubmitBoundaryCount != wantBoundaries)
                        throw new Exception($"plan has {plan.SubmitBoundaryCount} submit boundaries, expected {wantBoundaries}");
                    await accelerator.SynchronizeAsync();

                    await b.View.MemSetToZeroAsync(accelerator.DefaultStream);
                    await accelerator.SynchronizeAsync();
                    await plan.ReplayAsync();
                    await accelerator.SynchronizeAsync();
                    CheckFnArrayOutput(await b.CopyToHostAsync<int>(), LargeFnArrayLength, 1, "replayed follow-up");
                }
                finally
                {
                    plan.Dispose();
                }
            }
            finally
            {
                WebGPUBackend.NvidiaLargeFunctionArrayFlush = prev;
            }
        });

        /// <summary>The WGSL measure: function / private arrays counted (structs, vectors, nesting, comments ignored),
        /// workgroup / storage / uniform not. Pure; runs on every backend.</summary>
        [TestMethod]
        public Task WgslFunctionArrays_Measure()
        {
            void Expect(string wgsl, long want, string label)
            {
                long got = WgslFunctionArrays.EstimateBytes(wgsl);
                if (got != want) throw new Exception($"{label}: measured {got}, expected {want}");
            }
            Expect("fn main() { var big : array<i32, 1589>; var small : array<i32, 64>; }", 1653 * 4, "two function arrays");
            Expect("var<private> p : array<u32, 100>;\nfn f() {}", 400, "module var<private>");
            Expect("var<workgroup> w : array<f32, 4096>;\n@group(0) @binding(0) var<storage, read_write> s : array<u32>;\n" +
                   "@group(0) @binding(1) var<uniform> u : vec4<u32>;\nfn f() { var x : i32 = 0; var y = 3; }", 0, "workgroup / storage / uniform / scalars");
            Expect("// var c : array<i32, 9999>;\n/* var d : array<i32, 9999>; */ fn f() { var e : array<vec2<u32>, 10>; }", 80, "comments ignored, vec2");
            Expect("struct S { a : i32, b : vec3<f32>, }\nfn f() { var s : array<S, 10>; }", (4 + 16) * 10, "struct element, vec3 as 4 lanes");
            Expect("fn f() { var m : array<array<f32, 4>, 8>; var variable_name : i32; }", 128, "nested array");
            Expect("fn f() { var q : array<f32, N>; }", 1L << 20, "non-literal length counts as large");
            Expect("fn f() { var r : array<f16, 3u>; }", 12, "u suffix, f16 counted as 4");
            return Task.CompletedTask;
        }
    }
}
