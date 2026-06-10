using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU;

// ---------------------------------------------------------------------------------------
// Precompiled-shaders Layer 2 - cross-session serialization of a generated artifact to a
// sidecar (.meta.json) + a manifest, and back into a ShaderArtifact that Layer 3
// (ShaderArtifactCache) can register. The build-time worker WRITES these; the Blazor
// runtime loader READS them at startup.
//
// The shader TEXT/BYTES live in a separate file (.wgsl / .glsl / .wasm); the .meta.json
// carries only what is NOT recoverable from that file: the profile cache key, the kernel
// identity, the codegen version (for staleness), and the backend-specific CodegenMetadata
// (the dispatch info Layer 3 reconstructs from - none of which is in the shader text).
//
// DETERMINISM: all DTOs use stable property names and the serializer sorts nothing
// implicitly that would reorder (the lists preserve emit order), so (artifact -> json) is
// reproducible - a build invariant (see Plans/precompiled-shaders.md S11.4).
// ---------------------------------------------------------------------------------------

/// <summary>
/// JSON-friendly stand-in for a <c>(int ParamIdx, int FieldIdx)</c> tuple. System.Text.Json
/// does not serialize <see cref="ValueTuple"/> fields (they are fields, not properties), so
/// the i64-spinlock index list is mapped through this named record.
/// </summary>
public sealed class SpinlockIndexDto
{
    /// <summary>IR parameter index.</summary>
    public int ParamIdx { get; set; }
    /// <summary>Struct field index (0 for direct params).</summary>
    public int FieldIdx { get; set; }
}

/// <summary>
/// JSON-friendly stand-in for <see cref="DynamicSharedOverrideInfo"/>, which is an immutable
/// readonly struct (get-only props + a constructor). System.Text.Json deserializes a struct
/// via its implicit parameterless constructor and cannot then set get-only properties, so it
/// would silently round-trip to default values - this get/set DTO is mapped explicitly instead.
/// </summary>
public sealed class DynamicSharedOverrideDto
{
    /// <summary>The WGSL override constant name.</summary>
    public string ConstantName { get; set; } = "";
    /// <summary>The WGSL variable name for the shared-memory array.</summary>
    public string VariableName { get; set; } = "";
    /// <summary>The allocation index within ILGPU's dynamic shared allocation list.</summary>
    public int AllocaIndex { get; set; }
    /// <summary>The size of one element in bytes.</summary>
    public int ElementSize { get; set; }
}

/// <summary>
/// JSON-serializable form of <see cref="WebGPUBackend.WebGPUKernelMetadata"/> - the WebGPU
/// dispatch metadata Layer 3 needs to rebuild a compiled kernel from cached WGSL without the
/// transpiler. <see cref="ScalarPackingEntry"/>, <see cref="CoalesceGroupEntry"/> and
/// <see cref="DynamicSharedOverrideInfo"/> serialize directly (public get/set, or a
/// name-matching constructor for the readonly struct); only the i64-spinlock tuples need a DTO.
/// </summary>
public sealed class WebGpuKernelMetadataDto
{
    /// <summary>Expected storage-buffer binding count.</summary>
    public int ExpectedBindingCount { get; set; }
    /// <summary>Dynamic shared-memory overrides.</summary>
    public List<DynamicSharedOverrideDto> DynamicSharedOverrides { get; set; } = new();
    /// <summary>Scalar-packing manifest.</summary>
    public List<ScalarPackingEntry> ScalarPackingManifest { get; set; } = new();
    /// <summary>(param, field) indices using i64 spinlock companion buffers.</summary>
    public List<SpinlockIndexDto> I64SpinlockParamIndices { get; set; } = new();
    /// <summary>Coalesce-group manifest.</summary>
    public List<CoalesceGroupEntry> CoalesceManifest { get; set; } = new();
}

/// <summary>
/// The full sidecar record for one (kernel, profile) artifact - serialized to
/// <c>{kernelName}.{shorthash}.meta.json</c> next to its shader file.
/// </summary>
public sealed class ShaderArtifactMeta
{
    /// <summary>Backend (<see cref="AcceleratorType"/> name).</summary>
    public string Backend { get; set; } = "";
    /// <summary>The profile name (human label).</summary>
    public string ProfileName { get; set; } = "";
    /// <summary>The full profile cache key (the value actually matched at runtime).</summary>
    public string ProfileCacheKey { get; set; } = "";
    /// <summary>Stable kernel identity (<see cref="ShaderArtifactCache.KernelId"/>).</summary>
    public string KernelId { get; set; } = "";
    /// <summary>Codegen/profile-schema version stamp for staleness detection.</summary>
    public string CodegenVersion { get; set; } = "";
    /// <summary>Relative path (from the manifest) to the shader source/binary file.</summary>
    public string ArtifactFile { get; set; } = "";
    /// <summary>Content hash of the shader file (immutable-cache filename + integrity).</summary>
    public string ContentHash { get; set; } = "";
    /// <summary>WebGPU codegen metadata. Null for backends without reconstruct metadata (v1: Wasm/WebGL).</summary>
    public WebGpuKernelMetadataDto? WebGpu { get; set; }
}

/// <summary>One manifest row: a kernel/profile artifact and where its files live.</summary>
public sealed class ShaderManifestEntry
{
    /// <summary>Stable kernel identity.</summary>
    public string KernelId { get; set; } = "";
    /// <summary>Backend name.</summary>
    public string Backend { get; set; } = "";
    /// <summary>Profile name (human label).</summary>
    public string ProfileName { get; set; } = "";
    /// <summary>Full profile cache key (matched at runtime).</summary>
    public string ProfileCacheKey { get; set; } = "";
    /// <summary>Relative path to the <c>.meta.json</c> sidecar.</summary>
    public string MetaFile { get; set; } = "";
    /// <summary>Relative path to the shader source/binary file.</summary>
    public string ArtifactFile { get; set; } = "";
    /// <summary>Content hash of the shader file.</summary>
    public string ContentHash { get; set; } = "";
}

/// <summary>The top-level manifest written to <c>wwwroot/_shaders/manifest.json</c>.</summary>
public sealed class ShaderManifest
{
    /// <summary>Codegen/profile-schema version this manifest was built at.</summary>
    public string CodegenVersion { get; set; } = "";
    /// <summary>All precompiled artifacts.</summary>
    public List<ShaderManifestEntry> Entries { get; set; } = new();
}

/// <summary>
/// Serializes generated artifacts to/from the Layer 2 sidecar + manifest JSON, and maps the
/// WebGPU codegen metadata across the JSON boundary. Pure, deterministic, JS-free - usable in
/// the build-time worker AND the Blazor runtime loader.
/// </summary>
public static class ShaderArtifactSerializer
{
    /// <summary>The codegen version stamp baked into every artifact + manifest (= profile schema version).</summary>
    public static string CodegenVersion => $"v{CapabilityProfile.CurrentSchemaVersion}";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // DynamicSharedOverrideInfo is an immutable struct deserialized via its constructor.
        IncludeFields = false,
    };

    // ---- meta.json (per-artifact sidecar) ----

    /// <summary>Serialize a sidecar record to indented JSON.</summary>
    public static string SerializeMeta(ShaderArtifactMeta meta) =>
        JsonSerializer.Serialize(meta, Options);

    /// <summary>Deserialize a sidecar record from JSON.</summary>
    public static ShaderArtifactMeta DeserializeMeta(string json) =>
        JsonSerializer.Deserialize<ShaderArtifactMeta>(json, Options)
        ?? throw new FormatException("ShaderArtifactMeta JSON deserialized to null.");

    // ---- manifest.json ----

    /// <summary>Serialize the manifest to indented JSON.</summary>
    public static string SerializeManifest(ShaderManifest manifest) =>
        JsonSerializer.Serialize(manifest, Options);

    /// <summary>Deserialize the manifest from JSON.</summary>
    public static ShaderManifest DeserializeManifest(string json) =>
        JsonSerializer.Deserialize<ShaderManifest>(json, Options)
        ?? throw new FormatException("ShaderManifest JSON deserialized to null.");

    // ---- WebGPU CodegenMetadata <-> DTO ----

    /// <summary>Map the runtime WebGPU metadata to its JSON DTO (build-time write path).</summary>
    public static WebGpuKernelMetadataDto ToDto(WebGPUBackend.WebGPUKernelMetadata meta) => new()
    {
        ExpectedBindingCount = meta.ExpectedBindingCount,
        DynamicSharedOverrides = meta.DynamicSharedOverrides
            .Select(d => new DynamicSharedOverrideDto
            {
                ConstantName = d.ConstantName,
                VariableName = d.VariableName,
                AllocaIndex = d.AllocaIndex,
                ElementSize = d.ElementSize,
            })
            .ToList(),
        ScalarPackingManifest = meta.ScalarPackingManifest.ToList(),
        I64SpinlockParamIndices = meta.I64SpinlockParamIndices
            .Select(t => new SpinlockIndexDto { ParamIdx = t.ParamIdx, FieldIdx = t.FieldIdx })
            .ToList(),
        CoalesceManifest = meta.CoalesceManifest.ToList(),
    };

    /// <summary>Map the JSON DTO back to the runtime WebGPU metadata (runtime read path).</summary>
    public static WebGPUBackend.WebGPUKernelMetadata FromDto(WebGpuKernelMetadataDto dto) => new()
    {
        ExpectedBindingCount = dto.ExpectedBindingCount,
        DynamicSharedOverrides = dto.DynamicSharedOverrides
            .Select(d => new DynamicSharedOverrideInfo(
                d.ConstantName, d.VariableName, d.AllocaIndex, d.ElementSize))
            .ToList(),
        ScalarPackingManifest = dto.ScalarPackingManifest,
        I64SpinlockParamIndices = dto.I64SpinlockParamIndices
            .Select(d => (d.ParamIdx, d.FieldIdx))
            .ToList(),
        CoalesceManifest = dto.CoalesceManifest,
    };

    // ---- GeneratedKernel -> sidecar, and sidecar -> ShaderArtifact ----

    /// <summary>
    /// Build the sidecar record + the shader file payload from a <see cref="GeneratedKernel"/>.
    /// The caller writes <paramref name="artifactFileName"/> (the returned source/binary) and the
    /// <c>.meta.json</c> (<see cref="SerializeMeta"/> of the returned meta) to disk, then adds a
    /// manifest entry. <paramref name="contentHash"/> is the hash of the shader file content.
    /// </summary>
    public static ShaderArtifactMeta ToMeta(
        GeneratedKernel gen, string kernelId, string artifactFileName, string contentHash)
    {
        var meta = new ShaderArtifactMeta
        {
            Backend = gen.Backend.ToString(),
            ProfileName = gen.Profile.Name,
            ProfileCacheKey = gen.Profile.ToCacheKeyString(),
            KernelId = kernelId,
            CodegenVersion = CodegenVersion,
            ArtifactFile = artifactFileName,
            ContentHash = contentHash,
        };
        if (gen.CodegenMetadata is WebGPUBackend.WebGPUKernelMetadata wmeta)
            meta.WebGpu = ToDto(wmeta);
        return meta;
    }

    /// <summary>
    /// Reconstruct a <see cref="ShaderArtifact"/> from a sidecar record + the shader file content
    /// (one of <paramref name="source"/> / <paramref name="binary"/> per backend). Ready to hand to
    /// <see cref="ShaderArtifactCache.Register(string, string, ShaderArtifact)"/>.
    /// </summary>
    public static ShaderArtifact ToArtifact(ShaderArtifactMeta meta, string? source, byte[]? binary)
    {
        var backend = Enum.Parse<AcceleratorType>(meta.Backend);
        return new ShaderArtifact
        {
            Backend = backend,
            ProfileCacheKey = meta.ProfileCacheKey,
            Source = source,
            Binary = binary,
            CodegenMetadata = meta.WebGpu is { } dto ? FromDto(dto) : null,
        };
    }
}
