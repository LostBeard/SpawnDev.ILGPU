using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU;

// ---------------------------------------------------------------------------------------
// Precompiled-shaders Layer 2 - the build-time worker LOGIC. Reflects a built assembly for
// [PrecompiledKernel] methods, runs Layer 1 (ShaderCompiler.Generate) per (kernel x profile),
// and writes the artifacts + a manifest the runtime loader (ShaderArtifactManifestLoader)
// consumes. Pure desktop/CI code (reflection + file IO + Layer 1) - no device, no JS.
//
// This is the substance the MSBuild task (task #4) drives: the task spawns a thin console
// (SpawnDev.ILGPU.Precompiler) which calls ShaderPrecompiler.Run against the just-built
// consuming assembly. Keeping the logic here (not in the console) makes it unit-testable
// in-process against any loaded assembly.
// ---------------------------------------------------------------------------------------

/// <summary>How precompiled artifacts are packaged. Mirrors the csproj enum.</summary>
public enum ShaderPackagingMode
{
    /// <summary>Write blobs as wwwroot content files (default; lazily fetched, browser-cacheable).</summary>
    Content,
    /// <summary>Bake blobs into the assembly as embedded resources (single-DLL / non-browser).</summary>
    Embedded,
    /// <summary>Both content files and embedded resources.</summary>
    Both,
}

/// <summary>Outcome of a <see cref="ShaderPrecompiler.Run"/> pass.</summary>
public sealed class ShaderPrecompileResult
{
    /// <summary>Number of (kernel x profile) artifacts written.</summary>
    public int ArtifactsWritten { get; set; }
    /// <summary>Number of [PrecompiledKernel] methods discovered.</summary>
    public int KernelsDiscovered { get; set; }
    /// <summary>Fatal problems (a declared kernel that did not transpile, an unknown profile, ...).</summary>
    public List<string> Errors { get; } = new();
    /// <summary>Non-fatal notes.</summary>
    public List<string> Warnings { get; } = new();
    /// <summary>The written manifest path (empty if nothing was written).</summary>
    public string ManifestPath { get; set; } = "";
    /// <summary>True when no fatal errors occurred.</summary>
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Build-time precompiler (Layer 2 worker). See file header.
/// </summary>
public static class ShaderPrecompiler
{
    /// <summary>
    /// Reflect <paramref name="assembly"/> for <see cref="PrecompiledKernelAttribute"/> methods,
    /// generate each (kernel x profile) via <see cref="ShaderCompiler.Generate(MethodInfo, CapabilityProfile, KernelSpecialization?)"/>,
    /// and write the shader + <c>.meta.json</c> sidecars + a <c>manifest.json</c> under
    /// <paramref name="outputDir"/> (e.g. <c>wwwroot/_shaders</c>). Deterministic + device-free.
    /// </summary>
    /// <param name="assembly">The built consuming assembly to scan.</param>
    /// <param name="outputDir">Directory to write artifacts + manifest into (created if absent).</param>
    /// <param name="globalProfiles">
    /// Optional profile names (the <c>&lt;SpawnDevPrecompileProfiles&gt;</c> set) applied as ADDITIONAL
    /// targets to every <c>[PrecompiledKernel]</c> method, on top of its explicit attributes.
    /// </param>
    /// <param name="packaging">Packaging mode (v1 writes content files for all modes; Embedded wiring is a follow-up).</param>
    public static ShaderPrecompileResult Run(
        Assembly assembly,
        string outputDir,
        IReadOnlyList<string>? globalProfiles = null,
        ShaderPackagingMode packaging = ShaderPackagingMode.Content)
    {
        if (assembly is null) throw new ArgumentNullException(nameof(assembly));
        if (string.IsNullOrWhiteSpace(outputDir)) throw new ArgumentException("outputDir required", nameof(outputDir));

        var result = new ShaderPrecompileResult();
        var manifest = new ShaderManifest { CodegenVersion = ShaderArtifactSerializer.CodegenVersion };
        Directory.CreateDirectory(outputDir);

        // Dedup (kernelId, profileCacheKey) so a kernel listed both explicitly and via the global
        // set is generated once.
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var method in EnumerateKernelMethods(assembly))
        {
            var attrs = method.GetCustomAttributes<PrecompiledKernelAttribute>().ToList();
            if (attrs.Count == 0) continue;
            result.KernelsDiscovered++;

            // Explicit per-kernel targets, plus any global profiles applied to this kernel.
            var targets = new List<CapabilityProfile>();
            foreach (var attr in attrs)
            {
                var p = CapabilityProfiles.Resolve(attr.Profile);
                if (p is null) { result.Errors.Add($"{KernelLabel(method)}: unknown profile '{attr.Profile}'"); continue; }
                targets.Add(p);
            }
            if (globalProfiles != null)
            {
                foreach (var name in globalProfiles)
                {
                    var p = CapabilityProfiles.Resolve(name);
                    if (p is null) { result.Warnings.Add($"global profile '{name}' is unknown - skipped"); continue; }
                    targets.Add(p);
                }
            }

            foreach (var profile in targets)
            {
                var profileKey = profile.ToCacheKeyString();
                var kernelId = ShaderArtifactCache.KernelId(method);
                if (!seen.Add(kernelId + "||" + profileKey)) continue;

                GeneratedKernel gen;
                try
                {
                    gen = ShaderCompiler.Generate(method, profile);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{KernelLabel(method)} @ {profile.Name}: generation threw: {ex.Message}");
                    continue;
                }
                if (gen.HasErrors)
                {
                    var msg = string.Join("; ", gen.Diagnostics
                        .Where(d => d.Severity == GeneratedKernelDiagnosticSeverity.Error)
                        .Select(d => d.Message));
                    result.Errors.Add($"{KernelLabel(method)} @ {profile.Name}: {msg}");
                    continue;
                }

                var contentBytes = gen.Binary ?? Encoding.UTF8.GetBytes(gen.Source ?? "");
                var hash = ShortHash(contentBytes);
                var safe = SafeName(method);
                var rel = $"{profile.Name}/{safe}.{hash}.{gen.FileExtension}";
                var metaRel = $"{profile.Name}/{safe}.{hash}.meta.json";

                var profileDir = Path.Combine(outputDir, profile.Name);
                Directory.CreateDirectory(profileDir);
                File.WriteAllBytes(Path.Combine(outputDir, rel), contentBytes);

                var meta = ShaderArtifactSerializer.ToMeta(gen, kernelId, rel, hash);
                File.WriteAllText(Path.Combine(outputDir, metaRel), ShaderArtifactSerializer.SerializeMeta(meta));

                manifest.Entries.Add(new ShaderManifestEntry
                {
                    KernelId = kernelId,
                    Backend = gen.Backend.ToString(),
                    ProfileName = profile.Name,
                    ProfileCacheKey = profileKey,
                    MetaFile = metaRel,
                    ArtifactFile = rel,
                    ContentHash = hash,
                });
                result.ArtifactsWritten++;
            }
        }

        // Stable manifest ordering (determinism): sort entries by (kernelId, profile, backend).
        manifest.Entries.Sort((a, b) =>
        {
            int c = string.CompareOrdinal(a.KernelId, b.KernelId);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.ProfileName, b.ProfileName);
            return c != 0 ? c : string.CompareOrdinal(a.Backend, b.Backend);
        });

        result.ManifestPath = Path.Combine(outputDir, "manifest.json");
        File.WriteAllText(result.ManifestPath, ShaderArtifactSerializer.SerializeManifest(manifest));
        return result;
    }

    private static IEnumerable<MethodInfo> EnumerateKernelMethods(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            MethodInfo[] methods;
            try { methods = type.GetMethods(flags); }
            catch { continue; }
            foreach (var m in methods)
                yield return m;
        }
    }

    private static string KernelLabel(MethodInfo m) =>
        (m.DeclaringType?.FullName ?? "<global>") + "." + m.Name;

    /// <summary>Filename-safe short name for a kernel method (the content hash makes it unique).</summary>
    private static string SafeName(MethodInfo m)
    {
        var raw = (m.DeclaringType?.Name ?? "Global") + "." + m.Name;
        return Regex.Replace(raw, "[^A-Za-z0-9._]", "_");
    }

    /// <summary>First 16 lowercase hex chars of the SHA-256 of the content (immutable-cache key).</summary>
    private static string ShortHash(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()[..16];
}
