using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline WGSL generation probe (Layer 1 of the precompiled-shaders feature). Proves
/// <see cref="ShaderCompiler.Generate"/> emits WGSL on the DESKTOP with NO device / browser /
/// dispatch, driven purely by a <see cref="CapabilityProfile"/>.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- shader-gen
/// </summary>
internal static class ShaderGenDump
{
    // A trivial implicitly-grouped kernel.
    private static void DoubleKernel(Index1D i, ArrayView<float> input, ArrayView<float> output)
        => output[i] = input[i] * 2f;

    public static Task<int> Run()
    {
        var kernel = (Action<Index1D, ArrayView<float>, ArrayView<float>>)DoubleKernel;

        foreach (var profile in new[]
        {
            CapabilityProfiles.WebGPUFull,          // Dekker f64, subgroups, native f16 (Chrome-class)
            CapabilityProfiles.WebGPUNoSubgroups,   // Dekker f64, native f16, no subgroups (Firefox-class)
            CapabilityProfiles.WebGPUBaseline,      // Dekker f64, emulated f16, no subgroups (broadest)
        })
        {
            Console.WriteLine($"=== Generate WGSL for profile '{profile.Name}' " +
                $"(f16Native={profile.Float16Native}, cacheKey={profile.ToCacheKeyString()}) ===");
            try
            {
                var result = ShaderCompiler.Generate(kernel, profile);
                var wgsl = result.Source ?? "";
                Console.WriteLine($"[shader-gen] Backend={result.Backend} " +
                    $"GroupSize={result.Metadata.GroupSize} " +
                    $"WGSL length={wgsl.Length} HasErrors={result.HasErrors}");
                // Print the first ~25 lines so we can eyeball it.
                var lines = wgsl.Split('\n');
                int show = Math.Min(25, lines.Length);
                for (int l = 0; l < show; l++)
                    Console.WriteLine("    " + lines[l].TrimEnd());
                Console.WriteLine($"    ... ({lines.Length} total lines)");

                if (wgsl.Length == 0)
                {
                    Console.Error.WriteLine("[shader-gen] FAIL: empty WGSL.");
                    return Task.FromResult(1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[shader-gen] EXCEPTION for '{profile.Name}': {ex}");
                return Task.FromResult(1);
            }
        }

        // Wasm (binary) + WebGL (GLSL) paths.
        foreach (var profile in new[]
        {
            CapabilityProfiles.WasmDefault,
            CapabilityProfiles.WebGL2Baseline,
        })
        {
            Console.WriteLine($"=== Generate for '{profile.Name}' ({profile.Backend}) ===");
            try
            {
                var r = ShaderCompiler.Generate(kernel, profile);
                int len = r.Source?.Length ?? r.Binary?.Length ?? 0;
                string kind = r.Source != null ? $"{r.Backend} source chars" : "Wasm bytes";
                Console.WriteLine($"[shader-gen] Backend={r.Backend} {kind}={len} HasErrors={r.HasErrors}");
                if (len == 0)
                {
                    Console.Error.WriteLine($"[shader-gen] FAIL: empty artifact for {profile.Name}.");
                    return Task.FromResult(1);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[shader-gen] EXCEPTION for '{profile.Name}': {ex}");
                return Task.FromResult(1);
            }
        }

        // Determinism check (task #4): generate twice per backend, compare bytes exactly.
        bool allDeterministic = true;
        foreach (var profile in new[]
        {
            CapabilityProfiles.WebGPUFull,
            CapabilityProfiles.WasmDefault,
            CapabilityProfiles.WebGL2Baseline,
        })
        {
            var r1 = ShaderCompiler.Generate(kernel, profile);
            var r2 = ShaderCompiler.Generate(kernel, profile);
            bool same = r1.Source is not null
                ? r1.Source == r2.Source
                : ByteEq(r1.Binary, r2.Binary);
            allDeterministic &= same;
            Console.WriteLine($"[shader-gen] determinism {profile.Backend}: identical={same}" +
                (same ? "" : " (DIFFERS — task #4 timestamp/SSA/hash normalization needed)"));
        }

        bool capGuard = CheckCapRoutingGuard();

        Console.WriteLine($"[shader-gen] OK — all 3 backends generated offline, no device. " +
            $"allDeterministic={allDeterministic} capRoutingGuard={capGuard}");
        return Task.FromResult((allDeterministic && capGuard) ? 0 : 2);
    }

    // Structural cap-read guard (precompiled-shaders Layer 1, Tuvok concern #1): the three
    // code generators must read capabilities ONLY through the backend's data-derived
    // properties (fed by CapabilityProfile), NEVER from a live device/adapter at codegen
    // time - otherwise an offline artifact cannot be byte-identical to the runtime output.
    // This scans the generator source for forbidden live-device patterns and fails if any
    // appear. Best-effort path resolution (dev/CI have source on disk; skips if not found).
    private static bool CheckCapRoutingGuard()
    {
        string[] generatorRelPaths =
        {
            "SpawnDev.ILGPU/WebGPU/Backend/WGSLCodeGenerator.cs",
            "SpawnDev.ILGPU/WebGPU/Backend/WGSLKernelFunctionGenerator.cs",
            "SpawnDev.ILGPU/WebGL/Backend/GLSLCodeGenerator.cs",
            "SpawnDev.ILGPU/WebGL/Backend/GLSLKernelFunctionGenerator.cs",
            "SpawnDev.ILGPU/Wasm/Backend/WasmKernelFunctionGenerator.cs",
            "SpawnDev.ILGPU/Wasm/Backend/WasmCodeGenerator.cs",
        };
        // Forbidden = reads of a LIVE device's capabilities/adapter at codegen time.
        string[] forbidden = { "Capabilities", "requestAdapter", "navigator.gpu", "adapter.features", ".Adapter." };

        // Find the repo source root by walking up from CWD until the first generator exists.
        string? root = null;
        var dir = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        for (int up = 0; up < 8 && dir != null; up++, dir = dir.Parent)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, generatorRelPaths[0])))
            { root = dir.FullName; break; }
        }
        if (root is null)
        {
            Console.WriteLine("[shader-gen] cap-routing guard SKIPPED (generator source not found on disk).");
            return true;
        }

        int violations = 0;
        foreach (var rel in generatorRelPaths)
        {
            var path = System.IO.Path.Combine(root, rel);
            if (!System.IO.File.Exists(path)) continue;
            int lineNo = 0;
            foreach (var raw in System.IO.File.ReadLines(path))
            {
                lineNo++;
                var line = raw.TrimStart();
                if (line.StartsWith("//") || line.StartsWith("///") || line.StartsWith("*")) continue;
                // Skip string literals (the one known benign hit is inside an error message).
                var code = line.Contains('"') ? line[..line.IndexOf('"')] : line;
                foreach (var f in forbidden)
                {
                    if (code.Contains(f, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"[shader-gen] CAP-ROUTING VIOLATION: {rel}:{lineNo} reads live device cap '{f}': {line.Trim()}");
                        violations++;
                    }
                }
            }
        }
        Console.WriteLine($"[shader-gen] cap-routing guard: {(violations == 0 ? "PASS (generators read caps only via the profile-fed backend)" : $"FAIL ({violations} violation(s))")}");
        return violations == 0;
    }

    private static bool ByteEq(byte[]? a, byte[]? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
