using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Byte-identical-to-runtime guard for the offline ShaderCompiler (precompiled-shaders Layer 1).
    // ShaderCompiler.Generate(profile-of-this-device) must equal the WGSL the LIVE WebGPU backend
    // emits for the same kernel - the correctness guarantee that a precompiled/cached artifact is a
    // faithful stand-in for runtime generation. WebGPU-only (WGSL string comparison). Observe-the-diff
    // form: on mismatch it reports the first divergence (e.g. a @workgroup_size from the auto-grouped
    // runtime vs the offline default) so we can pin the specialization rather than guess.
    public abstract partial class BackendTestBase
    {
        private static void PrecompiledShaders_DoubleKernel(
            Index1D i, ArrayView<float> input, ArrayView<float> output)
            => output[i] = input[i] * 2f;

        [TestMethod]
        public async Task PrecompiledShaders_OfflineWGSL_MatchesRuntime() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only WGSL byte-identical comparison.");

            // Capture the live WGSL the device's backend emits for this kernel.
            string? liveWgsl = null;
            Action<string, string, WGSLEntry> handler = (name, wgsl, info) =>
            {
                if (name.Contains("PrecompiledShaders_DoubleKernel", StringComparison.Ordinal))
                    liveWgsl = wgsl;
            };
            WebGPUBackend.OnShaderCompiled += handler;
            try
            {
                _ = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                    PrecompiledShaders_DoubleKernel);
            }
            finally { WebGPUBackend.OnShaderCompiled -= handler; }

            if (liveWgsl is null)
                throw new Exception("Did not capture live WGSL via OnShaderCompiled (kernel name not matched).");

            // Build a profile from THIS device's ACTUAL enabled features and generate OFFLINE.
            var profile = CapabilityProfiles.FromAccelerator(webgpu, webgpu.EnabledFeatures);
            var offline = ShaderCompiler.Generate(
                (Action<Index1D, ArrayView<float>, ArrayView<float>>)PrecompiledShaders_DoubleKernel,
                profile);
            var offlineWgsl = offline.Source ?? "";

            if (offlineWgsl != liveWgsl)
            {
                int min = Math.Min(offlineWgsl.Length, liveWgsl.Length);
                int diff = 0;
                while (diff < min && offlineWgsl[diff] == liveWgsl[diff]) diff++;
                int from = Math.Max(0, diff - 40);
                string ctxLive = liveWgsl.Substring(from, Math.Min(90, liveWgsl.Length - from));
                string ctxOff = offlineWgsl.Substring(from, Math.Min(90, offlineWgsl.Length - from));
                throw new Exception(
                    $"Offline WGSL != runtime. offlineLen={offlineWgsl.Length} liveLen={liveWgsl.Length} " +
                    $"firstDiff@{diff} features=[{string.Join(",", webgpu.EnabledFeatures)}] " +
                    $"offlineGroup={offline.Metadata.GroupSize}. " +
                    $"LIVE>>>{ctxLive}<<< OFFLINE>>>{ctxOff}<<<");
            }
        });
    }
}
