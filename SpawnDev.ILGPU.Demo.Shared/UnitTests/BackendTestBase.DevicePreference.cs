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
    }
}
