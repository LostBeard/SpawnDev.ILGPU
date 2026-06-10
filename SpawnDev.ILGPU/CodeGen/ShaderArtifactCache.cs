using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

/// <summary>
/// A precompiled/generated shader artifact for one (kernel, profile) pair.
/// </summary>
public sealed record ShaderArtifact
{
    /// <summary>The backend that produced this artifact.</summary>
    public required AcceleratorType Backend { get; init; }
    /// <summary>The capability-profile cache key this artifact was generated for.</summary>
    public required string ProfileCacheKey { get; init; }
    /// <summary>Generated shader SOURCE (WGSL/GLSL). Null for Wasm.</summary>
    public string? Source { get; init; }
    /// <summary>Generated module BYTES (Wasm). Null for WebGPU/WebGL.</summary>
    public byte[]? Binary { get; init; }

    /// <summary>
    /// Opaque, backend-specific codegen metadata needed to RECONSTRUCT the compiled kernel
    /// without re-running the transpiler - e.g. for WebGPU the scalar-packing manifest, binding
    /// count, i64-spinlock indices, coalesce manifest, and dynamic-shared overrides that the
    /// DISPATCH path depends on (none of which are recoverable from the shader text alone). The
    /// backend that produced the artifact knows the concrete type and casts it back on a hit. For
    /// cross-session (Layer 2) the backend serializes this to a sidecar file alongside the shader.
    /// </summary>
    public object? CodegenMetadata { get; init; }
}

/// <summary>
/// The runtime shader-artifact cache (precompiled-shaders Layer 3). Maps a (stable kernel id,
/// capability-profile key) to a precompiled or runtime-generated <see cref="ShaderArtifact"/>, so
/// the kernel-compile path can use a ready artifact instead of running the IL-&gt;shader transpiler.
///
/// POPULATED by two sources, identically:
/// - the build-time MSBuild manifest (Layer 2), which calls <see cref="Register(string, string, ShaderArtifact)"/>;
/// - the runtime warm path, which registers what it just transpiled (so repeated loads in a
///   session skip re-generation even without build-time precompilation).
///
/// CORRECTNESS (global Rule 1): the cache is keyed by the FULL profile cache key, so an artifact is
/// only ever returned for a device whose profile matches exactly; a miss falls back to runtime
/// generation. The cache is a pure optimization that can never change results.
///
/// KERNEL IDENTITY is the method's full signature (declaring type + name + parameter types), which
/// is STABLE across builds and identical between the build-time task and the runtime - NOT an MVID
/// or metadata token (those differ per build, and the build-time manifest is compiled INTO the
/// assembly so it cannot reference its own MVID) and NEVER <see cref="object.GetHashCode"/> (a
/// non-unique heuristic; see the kernelId-collision lesson in Wasm/CLAUDE.md).
/// </summary>
public static class ShaderArtifactCache
{
    private static readonly ConcurrentDictionary<string, ShaderArtifact> Cache =
        new(StringComparer.Ordinal);

    private static long _hits;
    private static long _misses;

    /// <summary>Number of cache hits since process start / last <see cref="ResetStats"/>.</summary>
    public static long Hits => Interlocked.Read(ref _hits);
    /// <summary>Number of cache misses since process start / last <see cref="ResetStats"/>.</summary>
    public static long Misses => Interlocked.Read(ref _misses);
    /// <summary>Number of registered artifacts.</summary>
    public static int Count => Cache.Count;

    /// <summary>
    /// The stable, build-independent identity of a kernel method: declaring-type full name + method
    /// name + parameter type full names. Identical between the build-time precompile task and the
    /// runtime load path for the same source.
    /// </summary>
    public static string KernelId(MethodInfo method)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        var paramSig = string.Join(",",
            method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name));
        var declaring = method.DeclaringType?.FullName ?? "<global>";
        return $"{declaring}.{method.Name}({paramSig})";
    }

    private static string Key(string kernelId, string profileCacheKey) =>
        kernelId + "||" + profileCacheKey;

    /// <summary>Register an artifact by raw (kernel id, profile key) - used by the build-time manifest.</summary>
    public static void Register(string kernelId, string profileCacheKey, ShaderArtifact artifact)
    {
        if (kernelId is null) throw new ArgumentNullException(nameof(kernelId));
        if (profileCacheKey is null) throw new ArgumentNullException(nameof(profileCacheKey));
        Cache[Key(kernelId, profileCacheKey)] = artifact ?? throw new ArgumentNullException(nameof(artifact));
    }

    /// <summary>Register an artifact for a method + profile (used by the runtime warm path).</summary>
    public static void Register(MethodInfo method, CapabilityProfile profile, ShaderArtifact artifact) =>
        Register(KernelId(method), profile.ToCacheKeyString(), artifact);

    /// <summary>Convenience: register a generated kernel's artifact for its profile.</summary>
    public static void Register(MethodInfo method, GeneratedKernel generated) =>
        Register(method, generated.Profile, new ShaderArtifact
        {
            Backend = generated.Backend,
            ProfileCacheKey = generated.Profile.ToCacheKeyString(),
            Source = generated.Source,
            Binary = generated.Binary,
            CodegenMetadata = generated.CodegenMetadata,
        });

    /// <summary>
    /// Look up a cached artifact for a method + profile. Increments hit/miss counters. Returns
    /// false (miss) when no exact-profile artifact exists - the caller then falls back to runtime
    /// generation.
    /// </summary>
    public static bool TryGet(MethodInfo method, CapabilityProfile profile, out ShaderArtifact artifact)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        artifact = null!;
        if (!Enabled)
        {
            Interlocked.Increment(ref _misses);
            return false;
        }
        bool found = Cache.TryGetValue(Key(KernelId(method), profile.ToCacheKeyString()), out artifact!);
        if (found) Interlocked.Increment(ref _hits);
        else Interlocked.Increment(ref _misses);
        return found;
    }

    /// <summary>True if an artifact exists for the method + profile (no counter side effects).</summary>
    public static bool Contains(MethodInfo method, CapabilityProfile profile) =>
        Cache.ContainsKey(Key(KernelId(method), profile.ToCacheKeyString()));

    /// <summary>Remove all cached artifacts.</summary>
    public static void Clear() => Cache.Clear();

    /// <summary>Reset the hit/miss counters.</summary>
    public static void ResetStats()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
    }

    /// <summary>
    /// When false (default true), <see cref="TryGet"/> always reports a miss - a kill switch so a
    /// consumer can force pure runtime generation (debugging, A/B, or to bypass a suspect artifact).
    /// </summary>
    public static bool Enabled { get; set; } = true;
}
