using System;
using System.IO;
using System.Reflection;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

// Offline shader dump for ANY static kernel method on BackendTestBase (public or private): writes
// the WGSL, GLSL and Wasm that the browser backends would compile, with no device or browser.
//   dotnet run -c Release --project SpawnDev.ILGPU.DemoConsole -- kernel-dump <MethodName> [outDir] [--ozaki] [--simd] [--verbose] [--no-narrow]
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
        if (args[1] == "--all-wasm")
            return Task.FromResult(DumpAllWasm(args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "kernel_dump_all")));
        bool ozaki = Array.IndexOf(args, "--ozaki") >= 0;
        // --state-machine: WasmBackend.StructuredControlFlow = false (the A/B of WasmStructuredControlFlow.cs)
        if (Array.IndexOf(args, "--state-machine") >= 0)
            SpawnDev.ILGPU.Wasm.Backend.WasmBackend.StructuredControlFlow = false;
        args = Array.FindAll(args, a => a != "--state-machine");
        bool simd = Array.IndexOf(args, "--simd") >= 0;
        bool verbose = Array.IndexOf(args, "--verbose") >= 0;
        // --no-narrow keeps emulated 64-bit index arithmetic in the WGSL (WebGPUBackend.NarrowInt64LowWordArithmetic = false)
        if (Array.IndexOf(args, "--no-narrow") >= 0)
            SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.NarrowInt64LowWordArithmetic = false;
        args = Array.FindAll(args, a => a != "--ozaki" && a != "--simd" && a != "--verbose" && a != "--no-narrow");
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

    // kernel-dump --all-wasm [outDir]: every static void method of the test assembly whose parameters are all value
    // types is compiled to Wasm twice, with WasmBackend.StructuredControlFlow on (<name>.scf.wasm) and off
    // (<name>.sm.wasm); methods that are not kernels fail to compile and are skipped. index.tsv lists each kernel:
    // name, structured functions in the scf compile, scf bytes, sm bytes, scf error. Then run wasm-validate on them.
    static int DumpAllWasm(string dir)
    {
        Directory.CreateDirectory(dir);
        // KD_VERBOSE=1: the Wasm backend's codegen log (grep it for [Wasm-SM] / [Wasm-SCF] lines)
        SpawnDev.ILGPU.Wasm.Backend.WasmBackend.VerboseLogging = Environment.GetEnvironmentVariable("KD_VERBOSE") == "1";
        bool wgsl = Environment.GetEnvironmentVariable("KD_WGSL") == "1";
        if (wgsl) SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.VerboseLogging = true;
        var asm = typeof(SpawnDev.ILGPU.Demo.Shared.UnitTests.BackendTestBase).Assembly;
        var spec = new KernelSpecialization(64, null);
        var rows = new System.Text.StringBuilder();
        int kernels = 0, structuredKernels = 0, scfErrors = 0;
        var seen = new HashSet<string>();
        foreach (var type in asm.GetTypes())
        {
            if (type.ContainsGenericParameters) continue;
            foreach (var m in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (m.IsGenericMethodDefinition || m.ReturnType != typeof(void) || m.GetMethodBody() == null) continue;
                var ps = m.GetParameters();
                if (ps.Length == 0) continue;
                bool ok = true;
                foreach (var p in ps) if (!p.ParameterType.IsValueType || p.ParameterType.IsByRef) { ok = false; break; }
                if (!ok) continue;
                string name = $"{type.Name}.{m.Name}";
                if (!seen.Add(name)) name += "_" + m.MetadataToken.ToString("x");
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');

                byte[]? sm = null;
                SpawnDev.ILGPU.Wasm.Backend.WasmBackend.StructuredControlFlow = false;
                try { sm = Compile(m, spec); } catch { }
                if (sm == null) continue;   // not a kernel this path compiles
                kernels++;
                SpawnDev.ILGPU.Wasm.Backend.WasmBackend.StructuredControlFlow = true;
                int before = SpawnDev.ILGPU.Wasm.Backend.WasmBackend.StructuredFunctionCount;
                byte[]? scf = null;
                string err = "";
                try { scf = Compile(m, spec); }
                catch (Exception ex) { err = (ex.GetType().Name + ": " + ex.Message).Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' '); scfErrors++; }
                int structured = SpawnDev.ILGPU.Wasm.Backend.WasmBackend.StructuredFunctionCount - before;
                if (structured > 0) structuredKernels++;
                File.WriteAllBytes(Path.Combine(dir, name + ".sm.wasm"), sm);
                if (scf != null) File.WriteAllBytes(Path.Combine(dir, name + ".scf.wasm"), scf);
                if (wgsl)
                {
                    // KD_WGSL=1: also generate the WebGPU WGSL (its [WGSL-*] verbose lines name the path each kernel took)
                    try
                    {
                        try { ShaderCompiler.Generate(m, CapabilityProfiles.WebGPUFull, spec, explicitlyGrouped: false); }
                        catch (NotSupportedException) { ShaderCompiler.Generate(m, CapabilityProfiles.WebGPUFull, spec, explicitlyGrouped: true); }
                    }
                    catch (Exception ex) { Console.WriteLine($"[KD-WGSL] {name}: {ex.GetType().Name}: {ex.Message}"); }
                }
                rows.Append(name).Append('\t').Append(structured).Append('\t').Append(scf?.Length ?? -1).Append('\t').Append(sm.Length).Append('\t').Append(err).Append('\n');
            }
        }
        File.WriteAllText(Path.Combine(dir, "index.tsv"), rows.ToString());
        Console.WriteLine($"kernels {kernels}, with structured functions {structuredKernels}, structured compile errors {scfErrors} -> {dir}");
        return scfErrors == 0 ? 0 : 2;
    }

    static byte[] Compile(MethodInfo m, KernelSpecialization spec)
    {
        try { return ShaderCompiler.Generate(m, CapabilityProfiles.WasmDefault, spec, explicitlyGrouped: false).Binary!; }
        catch (NotSupportedException) { return ShaderCompiler.Generate(m, CapabilityProfiles.WasmDefault, spec, explicitlyGrouped: true).Binary!; }
    }
}
