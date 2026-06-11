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

        // Scalar-parameter kernel: `mul` packs into _scalar_params via the ScalarPackingManifest,
        // which is codegen metadata NOT recoverable from the WGSL text. If the runtime cache fails
        // to carry/reconstruct it, this dispatch silently produces garbage. This test is the
        // dispatch-correctness proof for the precompiled cache hit path (Layer 3).
        private static void PrecompiledShaders_ScaleKernel(
            Index1D i, ArrayView<float> input, ArrayView<float> output, float mul)
            => output[i] = input[i] * mul;

        [TestMethod]
        public async Task PrecompiledShaders_RuntimeCache_HitProducesCorrectDispatch() =>
            await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException("WebGPU-only runtime cache test.");

            ShaderArtifactCache.Clear();
            ShaderArtifactCache.ResetStats();

            const int n = 256;
            const float mul = 3f;
            var src = new float[n];
            for (int i = 0; i < n; i++) src[i] = i;
            using var inBuf = accelerator.Allocate1D(src);
            using var outBuf = accelerator.Allocate1D<float>(n);

            // (1) WARM: first load runs codegen (cache MISS) and warm-registers the artifact.
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float>(
                PrecompiledShaders_ScaleKernel);
            k1((Index1D)n, inBuf.View, outBuf.View, mul);
            await accelerator.SynchronizeAsync();
            var r1 = await outBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs(r1[i] - src[i] * mul) > 1e-3f)
                    throw new Exception($"WARM dispatch wrong @{i}: {r1[i]} != {src[i] * mul}");
            if (ShaderArtifactCache.Misses == 0)
                throw new Exception("Expected a cache MISS on the first compile.");

            // (2) Clear the FRAMEWORK kernel cache so the next load re-enters Backend.Compile,
            // where our precompiled cache now HITS (it is a separate static, untouched by ClearCache).
            accelerator.ClearCache(ClearCacheMode.Everything);
            long hitsBefore = ShaderArtifactCache.Hits;

            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float>(
                PrecompiledShaders_ScaleKernel);
            if (ShaderArtifactCache.Hits <= hitsBefore)
                throw new Exception(
                    $"Expected a precompiled-cache HIT after ClearCache; hits={ShaderArtifactCache.Hits} " +
                    $"misses={ShaderArtifactCache.Misses} count={ShaderArtifactCache.Count}.");

            // (3) Dispatch the RECONSTRUCTED kernel — must be correct (proves the cached metadata,
            // esp. the scalar packing, rebuilt a faithful kernel).
            using var outBuf2 = accelerator.Allocate1D<float>(n);
            k2((Index1D)n, inBuf.View, outBuf2.View, mul);
            await accelerator.SynchronizeAsync();
            var r2 = await outBuf2.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs(r2[i] - src[i] * mul) > 1e-3f)
                    throw new Exception(
                        $"RECONSTRUCTED (cache-hit) dispatch WRONG @{i}: {r2[i]} != {src[i] * mul} " +
                        "— cached codegen metadata (scalar packing) did not rebuild a faithful kernel.");
        });

        // The Layer-1 -> Layer-3 BRIDGE: an OFFLINE-generated artifact (what a build-time precompile +
        // manifest produces) must be HIT by the runtime load path and dispatch correctly. This proves
        // the offline ShaderCompiler.Generate output (WGSL + captured codegen metadata) is a faithful,
        // runtime-usable precompiled artifact - the whole point of build-time precompilation.
        [TestMethod]
        public async Task PrecompiledShaders_OfflineArtifact_HitsAndDispatches() =>
            await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only.");

            ShaderArtifactCache.Clear();
            ShaderArtifactCache.ResetStats();

            // Generate OFFLINE for this device's profile (no warm run) and register - exactly what a
            // build-time precompile + manifest does.
            var profile = CapabilityProfiles.FromAccelerator(webgpu, webgpu.EnabledFeatures);
            var km = ((Action<Index1D, ArrayView<float>, ArrayView<float>, float>)
                PrecompiledShaders_ScaleKernel).Method;
            var generated = ShaderCompiler.Generate(km, profile);
            ShaderArtifactCache.Register(km, generated);

            // Clear the FRAMEWORK kernel cache so the load below RE-ENTERS Backend.Compile (where the
            // precompiled cache is consulted). Without this, a prior test in the same run that already
            // compiled this kernel leaves it in ILGPU's framework cache -> the load returns that copy
            // without ever calling Backend.Compile -> our artifact is never looked up (order-dependent
            // false failure; the artifact cache is a SEPARATE static, untouched by ClearCache). Mirrors
            // PrecompiledShaders_RuntimeCache_HitProducesCorrectDispatch.
            accelerator.ClearCache(ClearCacheMode.Everything);

            const int n = 256;
            const float mul = 4f;
            var src = new float[n];
            for (int i = 0; i < n; i++) src[i] = i + 1;
            using var inBuf = accelerator.Allocate1D(src);
            using var outBuf = accelerator.Allocate1D<float>(n);

            long hitsBefore = ShaderArtifactCache.Hits;
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float>(
                PrecompiledShaders_ScaleKernel);
            if (ShaderArtifactCache.Hits <= hitsBefore)
                throw new Exception(
                    $"Offline artifact NOT hit by runtime load (cache-key misalignment). " +
                    $"offlineKey={profile.ToCacheKeyString()} hits={ShaderArtifactCache.Hits} " +
                    $"misses={ShaderArtifactCache.Misses} count={ShaderArtifactCache.Count}. KEYS=[{ShaderArtifactCache.KeysSnapshot()}]");

            k((Index1D)n, inBuf.View, outBuf.View, mul);
            await accelerator.SynchronizeAsync();
            var r = await outBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs(r[i] - src[i] * mul) > 1e-3f)
                    throw new Exception($"Offline-artifact dispatch WRONG @{i}: {r[i]} != {src[i] * mul}");
        });

        // Drift guard (Seven, 2026-06-11): the runtime cache-LOOKUP profile
        // (WebGPUBackend.ProfileForThisBackend) and the registration/offline profile
        // (CapabilityProfiles.FromAccelerator) are two hand-maintained builders of the SAME device
        // profile. If they drift on ANY key field, an artifact registered with one is never found by
        // the other = a SILENT cache miss (exactly how the WarpSize=1-vs-32 drift surfaced). Assert the
        // KEY STRINGS are identical so a future drift fails LOUDLY with both keys printed, not as a
        // generic "offline artifact NOT hit".
        [TestMethod]
        public async Task PrecompiledShaders_ProfileBuilders_ProduceIdenticalKeyStrings() =>
            await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only (the runtime cache hook is WebGPU).");

            var lookupKey = webgpu.Backend.ProfileForThisBackend().ToCacheKeyString();
            var registrationKey =
                CapabilityProfiles.FromAccelerator(webgpu, webgpu.EnabledFeatures).ToCacheKeyString();
            if (lookupKey != registrationKey)
                throw new Exception(
                    "Profile-key DRIFT: ProfileForThisBackend (cache lookup) and FromAccelerator " +
                    "(registration/offline) produce DIFFERENT cache keys -> precompiled artifacts will " +
                    $"never hit. lookup=[{lookupKey}] registration=[{registrationKey}]");
            await Task.CompletedTask;
        });
    }
}
