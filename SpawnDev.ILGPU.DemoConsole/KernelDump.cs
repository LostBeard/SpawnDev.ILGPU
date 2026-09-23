using System;
using System.IO;
using System.Reflection;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

// Offline shader dump for ANY static kernel method on BackendTestBase (public or private): writes
// the WGSL, GLSL and Wasm that the browser backends would compile, with no device or browser.
//   dotnet run -c Release --project SpawnDev.ILGPU.DemoConsole -- kernel-dump <MethodName> [outDir] [--ozaki] [--simd] [--verbose]
// --ozaki switches the WGSL/GLSL f64 emulation from the default Dekker (vec2) to Ozaki (vec4).
// --verbose turns on the Wasm backend's codegen log (unhandled intrinsics, mappings).
// --simd forces the Wasm SIMD128 kernel_simd variant (the browser default; desktop reports no SIMD).
// Default outDir: %TEMP%\kernel_dump. Disassemble the .wasm with `wasm2wat --enable-threads`.
public static class KernelDump
{
    public static Task<int> Run(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("usage: kernel-dump <BackendTestBase static kernel method name> [outDir]");
            return Task.FromResult(1);
        }
        bool ozaki = Array.IndexOf(args, "--ozaki") >= 0;
        bool simd = Array.IndexOf(args, "--simd") >= 0;
        bool verbose = Array.IndexOf(args, "--verbose") >= 0;
        args = Array.FindAll(args, a => a != "--ozaki" && a != "--simd" && a != "--verbose");
        string name = args[1];
        var method = typeof(SpawnDev.ILGPU.Demo.Shared.UnitTests.BackendTestBase).GetMethod(
            name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception($"static method '{name}' not found on BackendTestBase");
        string dir = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "kernel_dump");
        Directory.CreateDirectory(dir);
        var spec = new KernelSpecialization(64, null);

        foreach (var (tag, baseProfile) in new[] { ("wgsl", CapabilityProfiles.WebGPUFull), ("glsl", CapabilityProfiles.WebGL2Baseline) })
        {
            var profile = ozaki ? baseProfile with { Float64Mode = F64EmulationMode.Ozaki, Name = baseProfile.Name.Replace("Dekker", "Ozaki") } : baseProfile;
            var g = ShaderCompiler.Generate(method, profile, spec);
            var path = Path.Combine(dir, ozaki ? $"{name}.ozaki.{tag}" : $"{name}.{tag}");
            File.WriteAllText(path, g.Source);
            Console.WriteLine($"{tag}: {g.Source!.Length} chars -> {path}");
        }
        SpawnDev.ILGPU.Wasm.Backend.WasmBackend.ForceSimd = simd;
        SpawnDev.ILGPU.Wasm.Backend.WasmBackend.VerboseLogging = verbose;
        var w = ShaderCompiler.Generate(method, CapabilityProfiles.WasmDefault, spec);
        var wasmPath = Path.Combine(dir, $"{name}.wasm");
        File.WriteAllBytes(wasmPath, w.Binary!);
        Console.WriteLine($"wasm: {w.Binary!.Length} bytes -> {wasmPath}");
        return Task.FromResult(0);
    }
}
