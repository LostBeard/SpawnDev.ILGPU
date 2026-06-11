using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

/// <summary>
/// A precompiled/generated shader artifact for one (kernel, profile, specialization) tuple.
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
/// capability-profile key, specialization key) to a precompiled or runtime-generated
/// <see cref="ShaderArtifact"/>, so the kernel-compile path can use a ready artifact instead of
/// running the IL-&gt;shader transpiler.
///
/// POPULATED by two sources, identically:
/// - the build-time MSBuild manifest (Layer 2), which calls <see cref="Register(string, string, ShaderArtifact)"/>;
/// - the runtime warm path, which registers what it just transpiled (so repeated loads in a
///   session skip re-generation even without build-time precompilation).
///
/// CORRECTNESS (global Rule 1): the cache key must FULLY determine the generated shader, or it
/// silently serves the wrong kernel. The key has THREE segments, each closing a distinct
/// specialization-variant source (verified by Seven, 2026-06-11):
/// - <see cref="KernelId"/> = declaring type + name + **generic method arguments** + parameter
///   types + a **dynamic-assembly tag**. Generic args carry RadixSort's <c>TOperation</c> (sort
///   direction), invisible in the parameter list. The dynamic tag separates
///   <c>DelegateSpecializationRewriter</c> synthetic methods, which are NOMINAL TWINS (identical
///   type/method/param names) distinguished only by their emitted assembly.
/// - the full profile cache key (device capabilities the generator branches on).
/// - <see cref="SpecKey"/> = the explicit <see cref="KernelSpecialization"/> (workgroup size),
///   which the original code only ACCIDENTALLY captured by folding into the profile.
///
/// Kernels with <see cref="SpecializedValue{T}"/> parameters are NOT cacheable here
/// (<see cref="UsesRuntimeValueSpecialization"/>): the value is baked as an IR constant INSIDE
/// <c>SpecializationCache.SpecializeKernel</c>, upstream of and invisible to the backend
/// compile hook, so no complete key exists at the hook. Skipping is correct, not a workaround -
/// that higher cache already memoizes the compiled kernel per (accelerator, value).
///
/// KERNEL IDENTITY uses the method's full signature (NOT an MVID/metadata token, which differ per
/// build, and NEVER <see cref="object.GetHashCode"/>, a non-unique heuristic).
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

    /// <summary>Diagnostic: a snapshot of all current cache keys (kernelId||profile||spec), newline-joined.</summary>
    public static string KeysSnapshot() => string.Join(" ;; ", Cache.Keys.OrderBy(k => k, StringComparer.Ordinal));

    private static string TypeId(Type t) => t.FullName ?? t.Name;

    /// <summary>
    /// The stable, build-independent identity of a kernel method: declaring-type full name + method
    /// name + GENERIC METHOD ARGUMENTS + parameter type full names + a tag for dynamically-emitted
    /// methods. Identical between the build-time precompile task and the runtime load path for the
    /// same source. Non-generic, statically-emitted kernels (everything <c>[PrecompiledKernel]</c>
    /// targets) produce the same string as a plain signature, so existing manifests stay valid.
    /// </summary>
    public static string KernelId(MethodInfo method)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        var paramSig = string.Join(",",
            method.GetParameters().Select(p => TypeId(p.ParameterType)));
        var declaring = method.DeclaringType?.FullName ?? "<global>";
        // Generic METHOD arguments are program identity (RadixSort's TOperation IS the sort
        // direction - AscendingInt32 vs DescendingInt32) but never appear in the parameter list.
        var genericSig = method.IsGenericMethod
            ? "<" + string.Join(",", method.GetGenericArguments().Select(TypeId)) + ">"
            : "";
        // Dynamically-emitted methods can be NOMINAL TWINS across emitted assemblies
        // (DelegateSpecializationRewriter names every variant's type "DelegateSpecKernel" and method
        // "<orig>_Specialized"); the emitted assembly name (a GUID) is the unique discriminator.
        // Static assemblies stay OUT of the id so build-time manifest ids remain stable across machines.
        var dynamicTag = method.Module.Assembly.IsDynamic
            ? "@" + method.Module.Assembly.GetName().Name
            : "";
        return $"{declaring}.{method.Name}{genericSig}({paramSig}){dynamicTag}";
    }

    /// <summary>
    /// The specialization segment of the cache key - the explicit <see cref="KernelSpecialization"/>
    /// (the whole struct is these two properties; ILGPU's own kernel key includes them all).
    /// </summary>
    public static string SpecKey(in KernelSpecialization s) =>
        (s.MaxNumThreadsPerGroup?.ToString() ?? "_") + "/" +
        (s.MinNumGroupsPerMultiprocessor?.ToString() ?? "_");

    /// <summary>
    /// The spec segment for a kernel compiled with no explicit specialization - the offline/manifest
    /// case and a plain auto-grouped load. Offline static kernels register under this; a static
    /// runtime load looks up under this, so an offline artifact still hits.
    /// </summary>
    public static readonly string EmptySpecKey = SpecKey(KernelSpecialization.Empty);

    /// <summary>
    /// True if the kernel has any <see cref="SpecializedValue{T}"/> parameter. Such kernels are
    /// NOT cacheable at the backend compile hook: the runtime value is baked into the IR upstream
    /// (in <c>SpecializationCache</c>), so two distinct values are indistinguishable here. The
    /// caller must skip both lookup and registration for them (zero loss - the value is memoized
    /// by the per-value compiled-kernel cache one level up, and such kernels are never
    /// offline-precompilable).
    /// </summary>
    public static bool UsesRuntimeValueSpecialization(MethodInfo method)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        return method.GetParameters().Any(p =>
            p.ParameterType.IsGenericType &&
            p.ParameterType.GetGenericTypeDefinition() == typeof(SpecializedValue<>));
    }

    private static string Key(string kernelId, string profileCacheKey, string specKey) =>
        kernelId + "||" + profileCacheKey + "||" + specKey;

    /// <summary>
    /// Register an artifact by raw (kernel id, profile key) at the EMPTY specialization - used by the
    /// build-time manifest (offline static kernels carry no explicit specialization).
    /// </summary>
    public static void Register(string kernelId, string profileCacheKey, ShaderArtifact artifact)
    {
        if (kernelId is null) throw new ArgumentNullException(nameof(kernelId));
        if (profileCacheKey is null) throw new ArgumentNullException(nameof(profileCacheKey));
        Cache[Key(kernelId, profileCacheKey, EmptySpecKey)] = artifact ?? throw new ArgumentNullException(nameof(artifact));
    }

    /// <summary>Register an artifact for a method + profile at the EMPTY specialization (offline path).</summary>
    public static void Register(MethodInfo method, CapabilityProfile profile, ShaderArtifact artifact) =>
        Register(KernelId(method), profile.ToCacheKeyString(), artifact);

    /// <summary>
    /// Register an artifact for a method + profile + explicit specialization (the runtime warm path,
    /// which knows the real <see cref="KernelSpecialization"/>).
    /// </summary>
    public static void Register(MethodInfo method, CapabilityProfile profile, in KernelSpecialization specialization, ShaderArtifact artifact)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        Cache[Key(KernelId(method), profile.ToCacheKeyString(), SpecKey(specialization))] =
            artifact ?? throw new ArgumentNullException(nameof(artifact));
    }

    /// <summary>Convenience: register a generated kernel's artifact for its profile (offline, EMPTY spec).</summary>
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
    /// Look up a cached artifact for a method + profile at the EMPTY specialization. Convenience for
    /// callers with no explicit specialization (offline-style lookups).
    /// </summary>
    public static bool TryGet(MethodInfo method, CapabilityProfile profile, out ShaderArtifact artifact) =>
        TryGet(method, profile, KernelSpecialization.Empty, out artifact);

    /// <summary>
    /// Look up a cached artifact for a method + profile + explicit specialization. Increments
    /// hit/miss counters. Returns false (miss) when no exact-tuple artifact exists - the caller then
    /// falls back to runtime generation.
    /// </summary>
    public static bool TryGet(MethodInfo method, CapabilityProfile profile, in KernelSpecialization specialization, out ShaderArtifact artifact)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        artifact = null!;
        if (!Enabled)
        {
            Interlocked.Increment(ref _misses);
            return false;
        }
        bool found = Cache.TryGetValue(
            Key(KernelId(method), profile.ToCacheKeyString(), SpecKey(specialization)), out artifact!);
        if (found) Interlocked.Increment(ref _hits);
        else Interlocked.Increment(ref _misses);
        return found;
    }

    /// <summary>True if an artifact exists for the method + profile at the EMPTY spec (no counters).</summary>
    public static bool Contains(MethodInfo method, CapabilityProfile profile) =>
        Cache.ContainsKey(Key(KernelId(method), profile.ToCacheKeyString(), EmptySpecKey));

    /// <summary>
    /// True if an artifact exists for a raw (kernel id, profile key) at the EMPTY spec - used by the
    /// Layer 2 manifest loader to skip re-fetching an already-registered artifact. No counters.
    /// </summary>
    public static bool ContainsKey(string kernelId, string profileCacheKey) =>
        Cache.ContainsKey(Key(kernelId, profileCacheKey, EmptySpecKey));

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
