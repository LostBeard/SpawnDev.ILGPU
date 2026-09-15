using System;
using System.Diagnostics;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    /// <summary>
    /// Where does <c>device.createBindGroup</c>'s time actually go - the crossing, the marshalling, or Dawn?
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 THIS EXISTS TO STOP A GUESS. MEASURED 2026-09-15, an uncaptured Kokoro pass on WebGPU spends
    /// <b>2,117 ms of 2,128 ms</b> of its whole bind-group phase inside the raw <c>device.CreateBindGroup</c>
    /// call - about <b>1.14 ms per call</b> across ~1,850 dispatches, roughly a quarter of the entire pass.
    /// Constructing the descriptors costs 11 ms, so there is nothing to pool on the .NET side.
    /// </para>
    /// <para>
    /// "1.14 ms" has three possible owners needing three different fixes:
    /// <list type="number">
    /// <item>the .NET-&gt;JS CROSSING - fix by batching many bind groups per crossing;</item>
    /// <item>MARSHALLING the descriptor - it is a POCO whose members are walked and rebuilt as a JS object
    /// per call (a layout reference, an entries array, and a nested resource object per entry), so the cost
    /// scales with members, not bytes - fix by passing primitives instead;</item>
    /// <item>DAWN's own validation - not fixable from here; the only levers left are caching the bind group
    /// or issuing fewer dispatches.</item>
    /// </list>
    /// Three timed loops over the SAME real descriptor separate them: a no-op crossing is (1); a no-op
    /// crossing that still marshals the descriptor is (1)+(2); the real call is (1)+(2)+(3).
    /// </para>
    /// <para>
    /// ⚠️ EVERY MEMBER IS A REAL OBJECT ON PURPOSE. A probe descriptor with <c>Layout = null</c> and
    /// <c>Buffer = null</c> marshals nothing but nulls and would under-report the very cost being measured.
    /// A real layout and real buffers are what production passes.
    /// </para>
    /// <para>
    /// ⚠️ REPORTS, DOES NOT GATE. Wall-clock interop cost on a shared browser; a threshold would be flaky,
    /// and a flaky red is worse than no signal. Read it with
    /// <c>PMT_LANES=WebGPU PMT_FILTER=BindGroupCost PMT_CONSOLE_LOG=BindGroupCost</c>.
    /// </para>
    /// </remarks>
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task BindGroupCost_WhereDoesCreateBindGroupGo() => await RunEmulatedTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only diagnostic (createBindGroup is a WebGPU call)");

            const int N = 300;
            var device = webgpu.NativeAccelerator.NativeDevice
                ?? throw new Exception("WebGPU device unavailable");

            // The helper module carrying the two no-ops (same one the plan replay imports).
            var baseUri = SpawnJSRuntime.Instance.AppBaseUri;
            var helperUrl = new Uri(new Uri(baseUri), "_content/SpawnDev.ILGPU/webgpuDispatchPlan.js").ToString();
            using (var _ = await SpawnJSRuntime.Instance.Import(helperUrl)) { }

            // ── A real layout and real buffers: three storage bindings, production's shape ───────
            using var layout = device.CreateBindGroupLayout(new GPUBindGroupLayoutDescriptor
            {
                Entries = new[]
                {
                    new GPUBindGroupLayoutEntry { Binding = 0, Visibility = GPUShaderStageFlags.COMPUTE, Buffer = new GPUBufferBindingLayout { Type = "storage" } },
                    new GPUBindGroupLayoutEntry { Binding = 1, Visibility = GPUShaderStageFlags.COMPUTE, Buffer = new GPUBufferBindingLayout { Type = "storage" } },
                    new GPUBindGroupLayoutEntry { Binding = 2, Visibility = GPUShaderStageFlags.COMPUTE, Buffer = new GPUBufferBindingLayout { Type = "read-only-storage" } },
                }
            });

            GPUBuffer MakeBuffer(ulong size) => device.CreateBuffer(new GPUBufferDescriptor
            {
                Label = "BindGroupCostProbe",
                Size = size,
                Usage = GPUBufferUsage.Storage | GPUBufferUsage.CopyDst,
                MappedAtCreation = false
            });

            using var b0 = MakeBuffer(1024);
            using var b1 = MakeBuffer(1024);
            using var b2 = MakeBuffer(256);

            // Built ONCE and reused, so all three loops measure per-CALL cost, never construction.
            var desc = new GPUBindGroupDescriptor
            {
                Layout = layout,
                Entries = new[]
                {
                    new GPUBindGroupEntry { Binding = 0, Resource = new GPUBufferBinding { Buffer = b0, Offset = 0, Size = 1024 } },
                    new GPUBindGroupEntry { Binding = 1, Resource = new GPUBufferBinding { Buffer = b1, Offset = 0, Size = 1024 } },
                    new GPUBindGroupEntry { Binding = 2, Resource = new GPUBufferBinding { Buffer = b2, Offset = 0, Size = 256 } },
                }
            };

            // Warm both paths once - the first call of anything here pays one-time setup.
            _ = SpawnJSRuntime.Instance.Call<int, int>("ilgpuWebGPUPlan.noop", 1);
            _ = SpawnJSRuntime.Instance.Call<GPUBindGroupDescriptor, int>("ilgpuWebGPUPlan.noopDescriptor", desc);
            device.CreateBindGroup(desc).Dispose();

            // ── 1. Raw crossing ──────────────────────────────────────────────────────────────────
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++)
                _ = SpawnJSRuntime.Instance.Call<int, int>("ilgpuWebGPUPlan.noop", i);
            sw.Stop();
            double crossingUs = sw.Elapsed.TotalMilliseconds / N * 1000.0;

            // ── 2. Crossing + marshalling the descriptor, no WebGPU work ────────────────────────
            sw.Restart();
            for (int i = 0; i < N; i++)
                _ = SpawnJSRuntime.Instance.Call<GPUBindGroupDescriptor, int>("ilgpuWebGPUPlan.noopDescriptor", desc);
            sw.Stop();
            double marshalUs = sw.Elapsed.TotalMilliseconds / N * 1000.0;

            // ── 3. The real thing ───────────────────────────────────────────────────────────────
            var groups = new GPUBindGroup[N];
            sw.Restart();
            for (int i = 0; i < N; i++)
                groups[i] = device.CreateBindGroup(desc);
            sw.Stop();
            double realUs = sw.Elapsed.TotalMilliseconds / N * 1000.0;
            foreach (var g in groups) g.Dispose();

            Console.WriteLine($"[BindGroupCost] {BackendName}: over {N} calls each - "
                            + $"crossing {crossingUs:F1} us | crossing+marshal {marshalUs:F1} us "
                            + $"| real createBindGroup {realUs:F1} us");

            if (sw.Elapsed.TotalMilliseconds <= 0 || crossingUs <= 0)
                throw new Exception(
                    $"{BackendName}: a timed loop measured zero, so the calls did not happen and none of "
                  + "these numbers mean anything.");

            // ⚠️ DELIBERATELY NOT REPORTED AS "Dawn = real - marshal". MEASURED 2026-09-15 that subtraction
            // came out NEGATIVE (-66.7 us), which is impossible if the real call were simply marshal +
            // Dawn - so the two probes are not cleanly nested. Most likely the no-op path and the method
            // call on the device's own JS reference do not marshal identically, and reading
            // `desc.entries.length` in the probe forces work the real call does differently. The
            // subtraction is unsound; the COMPARISON of magnitudes is not, so only that is reported.
            string verdict =
                marshalUs > crossingUs * 4 && marshalUs > realUs * 0.5
                    ? $"MARSHALLING THE DESCRIPTOR dominates. A bare crossing is {crossingUs:F0} us, but simply "
                    + $"marshalling this descriptor costs {marshalUs:F0} us against a real call's {realUs:F0} us - "
                    + "same order. The cost is walking the descriptor's members (a layout reference, an "
                    + "entries array, a nested resource object per entry), not the crossing and not Dawn. "
                    + "The fix is to pass primitives, or build N bind groups in ONE crossing."
                    : $"NOT marshalling-dominated here (crossing {crossingUs:F0} us, marshal {marshalUs:F0} us, "
                    + $"real {realUs:F0} us) - if the real call is much larger than both, the remainder is "
                    + "Dawn's own validation and the only levers are caching or fewer dispatches.";
            Console.WriteLine($"[BindGroupCost] {BackendName}: VERDICT - {verdict}");

            // ⚠️ AND THIS PROBE UNDERSTATES PRODUCTION. It reuses THREE buffers for all N calls, which is
            // the marshaller's best case; Kokoro binds different buffers on every dispatch and up to 10 of
            // them, and measures ~1,140 us per call against the ~200 us here. Do not quote this number as
            // the production cost - quote the RATIO.
        });
    }
}
