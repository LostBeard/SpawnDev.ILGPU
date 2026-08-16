using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

// ---------------------------------------------------------------------------------------
// Precompiled-shaders Layer 2 - runtime loader. Bridges the build-time-emitted
// wwwroot/_shaders/{manifest.json + sidecars} into the Layer 3 ShaderArtifactCache so a
// kernel load can HIT a precompiled artifact instead of transpiling.
//
// DESIGN (per TJ 2026-06-10):
// - OPT-IN, NO STARTUP HOOK. Nothing fetches until a consumer calls Configure(...) AND then
//   asks to warm. A SpawnDev.SpawnJS.WebWorkers worker (or service worker) that never runs a
//   kernel never touches the network. Not configured == today's behavior (pure runtime transpile).
// - LAZY. EnsureManifestAsync fetches the (small) manifest once, memoized. TryWarmAsync fetches
//   and registers exactly ONE kernel's artifact, on demand, right before that kernel is loaded -
//   so artifacts you never use are never downloaded. WarmAllAsync is an optional bulk preload.
// - TRANSPORT-AGNOSTIC. `fetch` is a Func<url, Task<byte[]>> the consumer supplies (BlazorJS fetch
//   in-browser, HttpClient on desktop), so the library core takes NO SpawnDev.SpawnJS dependency
//   and the whole thing is unit-testable with an in-memory fetch.
// - The async kernel-load path (transparent lazy: await the warm inside an async LoadKernel on a
//   miss) is built ON TOP of this mechanism - this type is what it calls.
//
// CORRECTNESS: exact profile-key match (Rule 1, v1); a manifest/sidecar whose CodegenVersion does
// not match the running build is ignored (treated as no precompiled artifact); any fetch/parse
// failure falls back silently to runtime transpilation (a cache MISS is always safe).
// ---------------------------------------------------------------------------------------

/// <summary>
/// Opt-in, lazy runtime loader for build-time precompiled shader artifacts (Layer 2). See the
/// file header for the design. Register a source with <see cref="Configure"/>, then warm kernels
/// on demand with <see cref="TryWarmAsync"/> (or bulk with <see cref="WarmAllAsync"/>).
/// </summary>
public static class ShaderArtifactManifestLoader
{
    private static readonly object Gate = new();
    private static string? _manifestUrl;
    private static Func<string, Task<byte[]>>? _fetch;
    private static Task<ShaderManifest?>? _manifestTask; // memoized single manifest fetch

    /// <summary>True once <see cref="Configure"/> has been called with a source.</summary>
    public static bool IsConfigured
    {
        get { lock (Gate) return _manifestUrl != null && _fetch != null; }
    }

    /// <summary>
    /// Opt in to precompiled-artifact loading. Stores the manifest URL and a transport delegate;
    /// performs NO fetch (lazy). Call once when wiring GPU work. <paramref name="fetch"/> maps a
    /// URL to its bytes (e.g. a SpawnDev.SpawnJS fetch in-browser, or <c>HttpClient</c> on desktop).
    /// </summary>
    public static void Configure(string manifestUrl, Func<string, Task<byte[]>> fetch)
    {
        if (manifestUrl is null) throw new ArgumentNullException(nameof(manifestUrl));
        if (fetch is null) throw new ArgumentNullException(nameof(fetch));
        lock (Gate)
        {
            _manifestUrl = manifestUrl;
            _fetch = fetch;
            _manifestTask = null; // re-point invalidates the memoized manifest
        }
    }

    /// <summary>Clear the configured source + memoized manifest (tests / re-point).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _manifestUrl = null;
            _fetch = null;
            _manifestTask = null;
        }
    }

    /// <summary>
    /// Fetch the manifest once (memoized). Returns null when not configured, on any fetch/parse
    /// failure, or when the manifest's <c>CodegenVersion</c> does not match this build (stale =&gt;
    /// treated as "no precompiled artifacts", never trusted).
    /// </summary>
    public static Task<ShaderManifest?> EnsureManifestAsync()
    {
        lock (Gate)
        {
            if (_manifestUrl is null || _fetch is null)
                return Task.FromResult<ShaderManifest?>(null);
            return _manifestTask ??= FetchManifestAsync(_manifestUrl, _fetch);
        }
    }

    private static async Task<ShaderManifest?> FetchManifestAsync(
        string url, Func<string, Task<byte[]>> fetch)
    {
        try
        {
            var bytes = await fetch(url).ConfigureAwait(false);
            var manifest = ShaderArtifactSerializer.DeserializeManifest(Encoding.UTF8.GetString(bytes));
            return manifest.CodegenVersion == ShaderArtifactSerializer.CodegenVersion ? manifest : null;
        }
        catch
        {
            return null; // no manifest / network / parse error -> silent fallback to runtime transpile
        }
    }

    /// <summary>
    /// Lazy per-kernel warm: if the manifest has a precompiled artifact for this (kernel, profile),
    /// fetch + register it into <see cref="ShaderArtifactCache"/> so the next load hits. Returns true
    /// if the artifact is now cached (or already was), false if not configured / not in the manifest /
    /// fetch failed. Idempotent - safe to await before every load.
    /// </summary>
    public static async Task<bool> TryWarmAsync(MethodInfo method, CapabilityProfile profile)
    {
        if (method is null) throw new ArgumentNullException(nameof(method));
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        string? manifestUrl;
        Func<string, Task<byte[]>>? fetch;
        lock (Gate) { manifestUrl = _manifestUrl; fetch = _fetch; }
        if (manifestUrl is null || fetch is null) return false;

        var profileKey = profile.ToCacheKeyString();
        var kernelId = ShaderArtifactCache.KernelId(method);
        if (ShaderArtifactCache.ContainsKey(kernelId, profileKey)) return true;

        var manifest = await EnsureManifestAsync().ConfigureAwait(false);
        if (manifest is null) return false;

        foreach (var e in manifest.Entries)
            if (e.KernelId == kernelId && e.ProfileCacheKey == profileKey)
                return await FetchAndRegisterAsync(e, manifestUrl, fetch).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Optional bulk preload: fetch + register every manifest artifact matching the profile (and the
    /// optional <paramref name="filter"/>). Returns the count now cached. Prefer <see cref="TryWarmAsync"/>
    /// for the lazy "only what you use" behavior; this is for consumers who want to warm up front.
    /// </summary>
    public static async Task<int> WarmAllAsync(
        CapabilityProfile profile, Func<ShaderManifestEntry, bool>? filter = null)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));

        string? manifestUrl;
        Func<string, Task<byte[]>>? fetch;
        lock (Gate) { manifestUrl = _manifestUrl; fetch = _fetch; }
        if (manifestUrl is null || fetch is null) return 0;

        var manifest = await EnsureManifestAsync().ConfigureAwait(false);
        if (manifest is null) return 0;

        var profileKey = profile.ToCacheKeyString();
        int count = 0;
        foreach (var e in manifest.Entries)
        {
            if (e.ProfileCacheKey != profileKey) continue;
            if (filter != null && !filter(e)) continue;
            if (ShaderArtifactCache.ContainsKey(e.KernelId, e.ProfileCacheKey)) { count++; continue; }
            if (await FetchAndRegisterAsync(e, manifestUrl, fetch).ConfigureAwait(false)) count++;
        }
        return count;
    }

    private static async Task<bool> FetchAndRegisterAsync(
        ShaderManifestEntry entry, string manifestUrl, Func<string, Task<byte[]>> fetch)
    {
        try
        {
            var baseUrl = BaseOf(manifestUrl);
            var metaJson = Encoding.UTF8.GetString(await fetch(baseUrl + entry.MetaFile).ConfigureAwait(false));
            var meta = ShaderArtifactSerializer.DeserializeMeta(metaJson);
            if (meta.CodegenVersion != ShaderArtifactSerializer.CodegenVersion) return false;

            var artifactBytes = await fetch(baseUrl + entry.ArtifactFile).ConfigureAwait(false);
            string? source = null;
            byte[]? binary = null;
            if (Enum.Parse<AcceleratorType>(entry.Backend) == AcceleratorType.Wasm)
                binary = artifactBytes;
            else
                source = Encoding.UTF8.GetString(artifactBytes);

            var artifact = ShaderArtifactSerializer.ToArtifact(meta, source, binary);
            ShaderArtifactCache.Register(entry.KernelId, entry.ProfileCacheKey, artifact);
            return true;
        }
        catch
        {
            return false; // any failure -> safe miss -> runtime transpile
        }
    }

    /// <summary>Directory portion of a URL (everything up to and including the last '/').</summary>
    private static string BaseOf(string url)
    {
        int i = url.LastIndexOf('/');
        return i >= 0 ? url.Substring(0, i + 1) : "";
    }
}
