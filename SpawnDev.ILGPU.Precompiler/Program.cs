using System;
using System.IO;
using System.Reflection;
using SpawnDev.ILGPU;

// ---------------------------------------------------------------------------------------
// SpawnDev.ILGPU.Precompiler - the spawnable build-time worker for precompiled-shaders
// Layer 2. The MSBuild .targets runs this after a consumer build (process isolation, per
// Plans/precompiled-shaders.md S11.6). It loads the just-built consuming assembly, then
// hands it to ShaderPrecompiler.Run (the actual logic lives in the library so it is unit-
// testable in-process; this is a thin host).
//
//   dotnet SpawnDev.ILGPU.Precompiler.dll <assemblyPath> <outputDir> [profiles;semicolon] [Content|Embedded|Both]
//
// Exit codes: 0 = success, 1 = a declared kernel failed to transpile / load error, 2 = bad args.
// ---------------------------------------------------------------------------------------

if (args.Length < 2)
{
    Console.Error.WriteLine(
        "usage: SpawnDev.ILGPU.Precompiler <assemblyPath> <outputDir> [profiles;semicolon] [Content|Embedded|Both]");
    return 2;
}

var assemblyPath = Path.GetFullPath(args[0]);
var outputDir = args[1];
string[]? profiles = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2])
    ? args[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : null;
var packaging = args.Length > 3 && Enum.TryParse<ShaderPackagingMode>(args[3], ignoreCase: true, out var pm)
    ? pm
    : ShaderPackagingMode.Content;

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"[precompile] assembly not found: {assemblyPath}");
    return 1;
}

// Resolve the consumer assembly's OWN dependencies from its output directory. This fires only
// when default resolution fails - SpawnDev.ILGPU itself resolves to THIS tool's already-loaded
// copy (same package version), preserving PrecompiledKernelAttribute type identity across the
// boundary (the classic plugin-load pitfall, avoided).
var probeDir = Path.GetDirectoryName(assemblyPath)!;
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    var simpleName = new AssemblyName(e.Name).Name;
    if (string.IsNullOrEmpty(simpleName)) return null;
    var candidate = Path.Combine(probeDir, simpleName + ".dll");
    return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
};

Assembly assembly;
try
{
    assembly = Assembly.LoadFrom(assemblyPath);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[precompile] failed to load '{assemblyPath}': {ex.Message}");
    return 1;
}

ShaderPrecompileResult result;
try
{
    result = ShaderPrecompiler.Run(assembly, outputDir, profiles, packaging);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[precompile] run failed: {ex}");
    return 1;
}

foreach (var w in result.Warnings) Console.WriteLine($"[precompile] warning: {w}");
foreach (var er in result.Errors) Console.Error.WriteLine($"[precompile] error: {er}");
Console.WriteLine(
    $"[precompile] {result.ArtifactsWritten} artifact(s) from {result.KernelsDiscovered} " +
    $"[PrecompiledKernel] method(s) -> {outputDir}" +
    (result.ArtifactsWritten > 0 ? $"  (manifest: {result.ManifestPath})" : ""));

return result.Success ? 0 : 1;
