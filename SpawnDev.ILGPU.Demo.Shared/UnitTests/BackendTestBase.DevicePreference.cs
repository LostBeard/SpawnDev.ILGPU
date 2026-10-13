using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // DevicePreference.OrderByPreference on a BROWSER context: the documented WebGPU > WebGL > Wasm > CPU order.
    // It is what the sync CreatePreferredAccelerator(requirements) picks from. Added 2026-10-08 (PR #9 review): the
    // first cut ranked the browser backends in one bucket sorted by MemorySize, so Wasm (reports 2 GB) came ahead
    // of WebGL (256 MB), and ahead of WebGPU whenever its maxBufferSize is under 2 GB.
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task DevicePreference_Browser_WebGPUThenWebGLThenWasmThenCPU() => await RunTest(async acc =>
        {
            // Context-level check, independent of the backend under test: run it once, on the WebGPU lane.
            if (acc.AcceleratorType != AcceleratorType.WebGPU)
                throw new UnsupportedTestException("Context-level ranking check; runs on the WebGPU lane only");
            using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
            var ordered = context.Devices.OrderByPreference();
            var desc = string.Join(", ", ordered.Select(d => $"{d.AcceleratorType}({d.MemorySize >> 20} MB)"));
            Console.WriteLine($"[DevicePreference] browser order: {desc}");
            int Pos(AcceleratorType t) => ordered.ToList().FindIndex(d => d.AcceleratorType == t);
            var expected = new[] { AcceleratorType.WebGPU, AcceleratorType.WebGL, AcceleratorType.Wasm, AcceleratorType.CPU };
            int last = -1;
            foreach (var t in expected)
            {
                int p = Pos(t);
                if (p < 0)
                {
                    if (t == AcceleratorType.WebGPU || t == AcceleratorType.Wasm)
                        throw new Exception($"Browser context has no {t} device: {desc}");
                    continue;   // WebGL / CPU registration is host-dependent; order is checked among those present
                }
                if (p < last) throw new Exception($"{t} ranked ahead of a backend that should beat it: {desc}");
                last = p;
            }
            if (context.GetBestDevice().AcceleratorType != AcceleratorType.WebGPU)
                throw new Exception($"GetBestDevice() = {context.GetBestDevice().AcceleratorType}, expected WebGPU: {desc}");
        });

        /// <summary>
        /// RequiresSubGroups / RequiresFloat16Native on a BROWSER context follow what the device can really do
        /// (2026-10-13). WebGPU: selection must agree with the features a freshly created accelerator on that
        /// device actually got (before, WebGPU always failed RequiresFloat16Native and always passed
        /// RequiresSubGroups). Wasm and CPU run Warp.* correctly (emulated / simulated) and must pass
        /// RequiresSubGroups; WebGL must not. ForceEmulatedF16 must turn native f16 off for selection too.
        /// </summary>
        [TestMethod]
        public async Task AcceleratorRequirements_Browser_SubgroupsAndNativeF16FollowTheDevice() => await RunTest(async acc =>
        {
            if (acc.AcceleratorType != AcceleratorType.WebGPU)
                throw new UnsupportedTestException("Context-level selection check; runs on the WebGPU lane only");
            using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
            var subgroups = new AcceleratorRequirements { RequiresSubGroups = true };
            var f16Native = new AcceleratorRequirements { RequiresFloat16Native = true };

            var wgpuDevice = SpawnDev.ILGPU.WebGPU.WebGPUContextExtensions.GetWebGPUDevices(context)[0];
            bool realF16, realSubgroups;
            using (var fresh = (SpawnDev.ILGPU.WebGPU.WebGPUAccelerator)await wgpuDevice.CreateAcceleratorAsync(context))
            {
                realF16 = fresh.NativeAccelerator.HasShaderF16;
                realSubgroups = fresh.NativeAccelerator.HasSubgroups;
            }
            Console.WriteLine($"[Requirements] WebGPU adapter: shader-f16={realF16} subgroups={realSubgroups}");

            // A second accelerator on the SAME device. A GPUAdapter creates one device; until 2026-10-13 the second
            // accelerator re-used the consumed adapter and threw "adapter is consumed".
            using (var second = await wgpuDevice.CreateAcceleratorAsync(context))
            {
                using var buf = second.Allocate1D<int>(64);
                var k = second.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>((i, v) => v[i] = i * 7);
                k((Index1D)64, buf.View);
                var got = await buf.CopyToHostAsync<int>();
                for (int i = 0; i < 64; i++)
                    if (got[i] != i * 7) throw new Exception($"second accelerator on one WebGPU device: [{i}] = {got[i]}, expected {i * 7}");
            }
            if (wgpuDevice.Satisfies(f16Native) != realF16)
                throw new Exception($"WebGPU Satisfies(RequiresFloat16Native)={wgpuDevice.Satisfies(f16Native)} but the accelerator got shader-f16={realF16}");
            if (wgpuDevice.Satisfies(subgroups) != realSubgroups)
                throw new Exception($"WebGPU Satisfies(RequiresSubGroups)={wgpuDevice.Satisfies(subgroups)} but the accelerator got subgroups={realSubgroups}");

            SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.ForceEmulatedF16 = true;
            try
            {
                if (wgpuDevice.Satisfies(f16Native))
                    throw new Exception("ForceEmulatedF16 is set but WebGPU still satisfies RequiresFloat16Native");
            }
            finally
            {
                SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.ForceEmulatedF16 = false;
            }

            foreach (var d in context.Devices)
            {
                bool expected = d.AcceleratorType switch
                {
                    AcceleratorType.WebGL => false,
                    AcceleratorType.WebGPU => realSubgroups,
                    _ => true,   // Wasm (emulated warps), CPU (simulated)
                };
                if (d.Satisfies(subgroups) != expected)
                    throw new Exception($"{d.AcceleratorType} Satisfies(RequiresSubGroups)={d.Satisfies(subgroups)}, expected {expected}");
            }

            // End to end: with a shader-f16 adapter, native f16 now selects WebGPU (it used to fall to the CPU accelerator).
            if (realF16)
            {
                using var picked = await context.CreatePreferredAcceleratorAsync(f16Native);
                if (picked.AcceleratorType != AcceleratorType.WebGPU)
                    throw new Exception($"RequiresFloat16Native picked {picked.AcceleratorType}; the WebGPU adapter has shader-f16");
            }
        });
    }
}
