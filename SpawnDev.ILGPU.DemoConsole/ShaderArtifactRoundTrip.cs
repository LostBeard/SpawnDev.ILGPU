using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

/// <summary>
/// Device-free round-trip check for the precompiled-shaders Layer 2 serializer
/// (<see cref="ShaderArtifactSerializer"/>): fully-populated WebGPU codegen metadata ->
/// sidecar JSON -> back -> reconstructed metadata, asserting every field survives - in
/// particular the immutable <c>DynamicSharedOverrideInfo</c> struct (constructor-based JSON
/// deserialization) and the <c>(int,int)</c> i64-spinlock tuples (mapped via DTO). Plus a
/// manifest round-trip and a determinism check. Run: <c>dotnet run -- shader-roundtrip</c>.
/// </summary>
public static class ShaderArtifactRoundTrip
{
    public static int Run()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {what}");
            if (!ok) fails++;
        }

        // Fully-populated WebGPU metadata (every field Layer 3 reconstructs from).
        var meta = new WebGPUBackend.WebGPUKernelMetadata
        {
            ExpectedBindingCount = 7,
            DynamicSharedOverrides = new List<DynamicSharedOverrideInfo>
            {
                new("DYNAMIC_SHARED_SIZE_0", "shared0", 0, 4),
                new("DYNAMIC_SHARED_SIZE_1", "shared1", 1, 8),
            },
            ScalarPackingManifest = new List<ScalarPackingEntry>
            {
                new() { ParamIndex = 2, ByteOffset = 0, ByteSize = 4, WgslType = "i32" },
                new()
                {
                    ParamIndex = 3, ByteOffset = 4, ByteSize = 8, WgslType = "u32",
                    IsEmulatedF64 = true, IsViewOffset = true, ViewBindingIndex = 5,
                },
            },
            I64SpinlockParamIndices = new List<(int, int)> { (1, 0), (4, 2) },
            CoalesceManifest = new List<CoalesceGroupEntry>
            {
                new()
                {
                    BindingIndex = 6, BindingName = "param6_i32_coalesced", ElementTypeKey = "i32",
                    IsDirectParam = true, MemberDirectParamIndices = new List<int> { 6, 7, 8 },
                },
            },
        };

        var sidecar = new ShaderArtifactMeta
        {
            Backend = AcceleratorType.WebGPU.ToString(),
            ProfileName = "WebGPU-Dekker-Subgroups-NativeF16",
            ProfileCacheKey = "test-key",
            KernelId = "Ns.Type.Method(System.Int32)",
            CodegenVersion = ShaderArtifactSerializer.CodegenVersion,
            ArtifactFile = "k.abcd1234.wgsl",
            ContentHash = "abcd1234",
            WebGpu = ShaderArtifactSerializer.ToDto(meta),
        };

        var json = ShaderArtifactSerializer.SerializeMeta(sidecar);
        Console.WriteLine(json);
        var back = ShaderArtifactSerializer.DeserializeMeta(json);

        Check(back.Backend == "WebGPU", "backend");
        Check(back.ProfileCacheKey == "test-key", "profileCacheKey");
        Check(back.WebGpu != null, "webgpu metadata present");

        var rt = ShaderArtifactSerializer.FromDto(back.WebGpu!);
        Check(rt.ExpectedBindingCount == 7, "ExpectedBindingCount");
        Check(rt.DynamicSharedOverrides.Count == 2, "DynamicSharedOverrides count");
        Check(rt.DynamicSharedOverrides[1].ConstantName == "DYNAMIC_SHARED_SIZE_1"
              && rt.DynamicSharedOverrides[1].VariableName == "shared1"
              && rt.DynamicSharedOverrides[1].AllocaIndex == 1
              && rt.DynamicSharedOverrides[1].ElementSize == 8,
              "DynamicSharedOverrideInfo struct ctor round-trip (the STJ immutable-struct risk)");
        Check(rt.ScalarPackingManifest.Count == 2
              && rt.ScalarPackingManifest[1].IsEmulatedF64
              && rt.ScalarPackingManifest[1].IsViewOffset
              && rt.ScalarPackingManifest[1].ViewBindingIndex == 5
              && rt.ScalarPackingManifest[1].ByteSize == 8,
              "ScalarPackingEntry round-trip");
        Check(rt.I64SpinlockParamIndices.Count == 2
              && rt.I64SpinlockParamIndices[0].ParamIdx == 1 && rt.I64SpinlockParamIndices[0].FieldIdx == 0
              && rt.I64SpinlockParamIndices[1].ParamIdx == 4 && rt.I64SpinlockParamIndices[1].FieldIdx == 2,
              "(int,int) spinlock tuple DTO round-trip");
        Check(rt.CoalesceManifest.Count == 1
              && rt.CoalesceManifest[0].IsDirectParam
              && rt.CoalesceManifest[0].BindingName == "param6_i32_coalesced"
              && rt.CoalesceManifest[0].MemberDirectParamIndices.SequenceEqual(new[] { 6, 7, 8 }),
              "CoalesceGroupEntry round-trip");

        var art = ShaderArtifactSerializer.ToArtifact(back, "// wgsl source", null);
        Check(art.Backend == AcceleratorType.WebGPU
              && art.Source == "// wgsl source"
              && art.CodegenMetadata is WebGPUBackend.WebGPUKernelMetadata,
              "ToArtifact reconstruct");

        // Manifest round-trip.
        var manifest = new ShaderManifest
        {
            CodegenVersion = ShaderArtifactSerializer.CodegenVersion,
            Entries =
            {
                new ShaderManifestEntry
                {
                    KernelId = "k", Backend = "WebGPU", ProfileName = "WebGPU-Dekker",
                    ProfileCacheKey = "ck", MetaFile = "a.meta.json", ArtifactFile = "a.wgsl",
                    ContentHash = "h",
                },
            },
        };
        var mjson = ShaderArtifactSerializer.SerializeManifest(manifest);
        var mback = ShaderArtifactSerializer.DeserializeManifest(mjson);
        Check(mback.Entries.Count == 1
              && mback.Entries[0].KernelId == "k"
              && mback.Entries[0].ArtifactFile == "a.wgsl"
              && mback.CodegenVersion == manifest.CodegenVersion,
              "manifest round-trip");

        // Determinism: serialize twice -> identical bytes (build invariant).
        Check(ShaderArtifactSerializer.SerializeMeta(back) == ShaderArtifactSerializer.SerializeMeta(back),
              "determinism (serialize twice identical)");

        // ---- Layer 2 runtime loader (opt-in, lazy) end-to-end with an in-memory fetch ----
        ShaderArtifactCache.Clear();
        ShaderArtifactManifestLoader.Reset();

        var probe = typeof(ShaderArtifactRoundTrip).GetMethod(
            nameof(WarmProbeKernel), BindingFlags.Static | BindingFlags.NonPublic)!;
        var profile = CapabilityProfiles.WebGPUFull;
        var profileKey = profile.ToCacheKeyString();
        var kernelId = ShaderArtifactCache.KernelId(probe);

        var sidecar2 = new ShaderArtifactMeta
        {
            Backend = "WebGPU", ProfileName = profile.Name, ProfileCacheKey = profileKey,
            KernelId = kernelId, CodegenVersion = ShaderArtifactSerializer.CodegenVersion,
            ArtifactFile = "k.aaaa.wgsl", ContentHash = "aaaa",
            WebGpu = ShaderArtifactSerializer.ToDto(meta),
        };
        var manifest2 = new ShaderManifest
        {
            CodegenVersion = ShaderArtifactSerializer.CodegenVersion,
            Entries =
            {
                new ShaderManifestEntry
                {
                    KernelId = kernelId, Backend = "WebGPU", ProfileName = profile.Name,
                    ProfileCacheKey = profileKey, MetaFile = "k.aaaa.meta.json",
                    ArtifactFile = "k.aaaa.wgsl", ContentHash = "aaaa",
                },
            },
        };
        const string baseUrl = "https://app/_shaders/";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [baseUrl + "manifest.json"] = Encoding.UTF8.GetBytes(ShaderArtifactSerializer.SerializeManifest(manifest2)),
            [baseUrl + "k.aaaa.meta.json"] = Encoding.UTF8.GetBytes(ShaderArtifactSerializer.SerializeMeta(sidecar2)),
            [baseUrl + "k.aaaa.wgsl"] = Encoding.UTF8.GetBytes("// wgsl body"),
        };
        Func<string, Task<byte[]>> fetch = url =>
            files.TryGetValue(url, out var b) ? Task.FromResult(b) : throw new Exception("404 " + url);

        Check(!ShaderArtifactManifestLoader.TryWarmAsync(probe, profile).GetAwaiter().GetResult(),
              "warm is a no-op when not configured (WebWorkers-safe)");

        ShaderArtifactManifestLoader.Configure(baseUrl + "manifest.json", fetch);
        Check(ShaderArtifactManifestLoader.TryWarmAsync(probe, profile).GetAwaiter().GetResult(),
              "lazy per-kernel warm fetches + registers the artifact");
        Check(ShaderArtifactCache.ContainsKey(kernelId, profileKey),
              "cache contains the warmed artifact (next load would hit)");
        Check(!ShaderArtifactManifestLoader.TryWarmAsync(probe, CapabilityProfiles.WasmDefault).GetAwaiter().GetResult(),
              "warm is a no-op for an unmatched profile (exact match)");

        ShaderArtifactManifestLoader.Reset();
        ShaderArtifactCache.Clear();

        Console.WriteLine(fails == 0
            ? "=== ALL ROUND-TRIP CHECKS PASS ==="
            : $"=== {fails} CHECK(S) FAILED ===");
        return fails == 0 ? 0 : 1;
    }

    // Probe kernel for the loader warm test - only its identity (signature) is used.
    private static void WarmProbeKernel(Index1D i, ArrayView<float> data) => data[i] = i;

    // A real [PrecompiledKernel]-decorated kernel for the end-to-end build->emit->load test below.
    [PrecompiledKernel(AcceleratorType.WebGPU, "WebGPU-Dekker")]
    private static void PrecompileE2EKernel(Index1D i, ArrayView<float> data) => data[i] = i * 2f;

    /// <summary>
    /// End-to-end Layer 2: run the BUILD-TIME precompiler (ShaderPrecompiler.Run) over this
    /// assembly, then have the RUNTIME loader (ShaderArtifactManifestLoader) consume the emitted
    /// manifest + sidecars via a file-backed fetch and register into the cache - proving the whole
    /// pipeline (reflect -> generate -> write -> fetch -> reconstruct -> cache hit) with no device
    /// or browser. Run: <c>dotnet run -- precompile-e2e</c>.
    /// </summary>
    public static int RunPrecompileE2E()
    {
        int fails = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"[{(ok ? "PASS" : "FAIL")}] {what}");
            if (!ok) fails++;
        }

        var outDir = Path.Combine(Path.GetTempPath(), "spawndev_precompile_e2e_" + Guid.NewGuid().ToString("N"));
        try
        {
            // BUILD-TIME: reflect this assembly, generate + write artifacts + manifest.
            var result = ShaderPrecompiler.Run(typeof(ShaderArtifactRoundTrip).Assembly, outDir);
            Check(result.Success, $"precompile success (errors: {string.Join(" | ", result.Errors)})");
            Check(result.KernelsDiscovered >= 1, $"discovered [PrecompiledKernel] methods ({result.KernelsDiscovered})");
            Check(result.ArtifactsWritten >= 1, $"artifacts written ({result.ArtifactsWritten})");
            Check(File.Exists(result.ManifestPath), "manifest.json written");

            var profile = CapabilityProfiles.WebGPUBaseline; // Name == "WebGPU-Dekker"
            var probe = typeof(ShaderArtifactRoundTrip).GetMethod(
                nameof(PrecompileE2EKernel), BindingFlags.Static | BindingFlags.NonPublic)!;
            var kernelId = ShaderArtifactCache.KernelId(probe);
            var profileKey = profile.ToCacheKeyString();

            // Verify the emitted shader file + sidecar exist on disk.
            var manifest = ShaderArtifactSerializer.DeserializeManifest(File.ReadAllText(result.ManifestPath));
            var entry = manifest.Entries.FirstOrDefault(e => e.KernelId == kernelId && e.ProfileCacheKey == profileKey);
            Check(entry != null, "manifest has an entry for the e2e kernel + profile");
            if (entry != null)
            {
                Check(File.Exists(Path.Combine(outDir, entry.ArtifactFile)), $"shader file on disk ({entry.ArtifactFile})");
                Check(File.Exists(Path.Combine(outDir, entry.MetaFile)), $"sidecar .meta.json on disk ({entry.MetaFile})");
            }

            // RUNTIME: loader consumes the emitted manifest via a file-backed fetch.
            ShaderArtifactCache.Clear();
            ShaderArtifactManifestLoader.Reset();
            ShaderArtifactManifestLoader.Configure(
                result.ManifestPath.Replace('\\', '/'),
                url => Task.FromResult(File.ReadAllBytes(url)));
            int warmed = ShaderArtifactManifestLoader.WarmAllAsync(profile).GetAwaiter().GetResult();
            Check(warmed >= 1, $"loader warmed {warmed} artifact(s) from the emitted manifest");
            Check(ShaderArtifactCache.ContainsKey(kernelId, profileKey),
                  "cache now contains the precompiled e2e kernel (a real load would HIT, skip transpile)");

            // Lazy per-kernel path too.
            ShaderArtifactCache.Clear();
            ShaderArtifactManifestLoader.Reset();
            ShaderArtifactManifestLoader.Configure(
                result.ManifestPath.Replace('\\', '/'),
                url => Task.FromResult(File.ReadAllBytes(url)));
            bool lazy = ShaderArtifactManifestLoader.TryWarmAsync(probe, profile).GetAwaiter().GetResult();
            Check(lazy && ShaderArtifactCache.ContainsKey(kernelId, profileKey),
                  "lazy TryWarmAsync fetches + registers the one kernel on demand");
        }
        finally
        {
            ShaderArtifactManifestLoader.Reset();
            ShaderArtifactCache.Clear();
            try { Directory.Delete(outDir, recursive: true); } catch { /* best-effort */ }
        }

        Console.WriteLine(fails == 0 ? "=== PRECOMPILE E2E PASS ===" : $"=== {fails} CHECK(S) FAILED ===");
        return fails == 0 ? 0 : 1;
    }
}
