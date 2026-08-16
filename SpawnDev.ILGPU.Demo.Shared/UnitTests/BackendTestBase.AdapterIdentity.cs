using System;
using System.Threading.Tasks;
using ILGPU.Runtime;
using SpawnDev.SpawnJS;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Harness guard for the 2026-07-03 SwiftShader discovery: the PMT Playwright BUNDLED Chromium
    // only ever exposed the SwiftShader SOFTWARE WebGPU adapter on the dev machine, so every WebGPU
    // perf number measured before the Channel=chrome harness fix was CPU-rasterizer time (correctness
    // results stand - SwiftShader computes correctly). This probe makes the adapter identity VISIBLE
    // in every run's results so a harness regression can never hide again: read its report line
    // before trusting any WebGPU perf number from the same sweep.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// Reports the live WebGPU adapter identity (vendor / architecture / device / description /
        /// isFallbackAdapter). isFallbackAdapter=true or arch=swiftshader means SOFTWARE WebGPU -
        /// every perf number from this browser session is CPU time, not GPU time.
        /// </summary>
        [TestMethod]
        public async Task WebGPU_AdapterIdentity_Probe() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: WebGPU-only adapter probe");
            var JS = SpawnJSRuntime.Instance;
            using var gpu = JS.Get<SpawnDev.SpawnJS.SpawnJSObject>("navigator.gpu");
            if (gpu == null) throw new Exception("navigator.gpu missing");
            using var adapter = await gpu.JSRef!.CallAsync<SpawnDev.SpawnJS.SpawnJSObject>("requestAdapter");
            if (adapter == null) throw new Exception("requestAdapter returned null");
            string vendor = "?", arch = "?", device = "?", desc = "?";
            bool fallback = false;
            try
            {
                using var info = adapter.JSRef!.Get<SpawnDev.SpawnJS.SpawnJSObject?>("info");
                if (info != null)
                {
                    vendor = info.JSRef!.Get<string?>("vendor") ?? "?";
                    arch = info.JSRef!.Get<string?>("architecture") ?? "?";
                    device = info.JSRef!.Get<string?>("device") ?? "?";
                    desc = info.JSRef!.Get<string?>("description") ?? "?";
                }
            }
            catch { }
            try { fallback = adapter.JSRef!.Get<bool?>("isFallbackAdapter") ?? false; } catch { }
            Console.WriteLine($"[AdapterProbe] vendor={vendor} arch={arch} device={device} desc='{desc}' isFallbackAdapter={fallback}");
            if (fallback || arch.Contains("swiftshader", StringComparison.OrdinalIgnoreCase))
                throw new Exception($"SOFTWARE WebGPU adapter in the harness: vendor={vendor} arch={arch} isFallbackAdapter={fallback} - perf numbers from this session are CPU-rasterizer time. Fix the browser launch (PMT Channel=chrome, no Vulkan feature flag, --disable-software-rasterizer).");
        });
    }
}
