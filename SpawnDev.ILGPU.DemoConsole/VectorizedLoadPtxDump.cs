using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

/// <summary>
/// Verifies whether ILGPU's PTX backend emits a 128-bit vectorized load (`ld.global.v4`)
/// for a 16-byte-aligned struct-of-4 load - the decode GEMV weight-bandwidth lever Tuvok
/// asked about. Compiles two kernels and dumps their PTX:
///   A) 4 SCALAR uint loads w[4*i+0..3]            -> expect 4x `ld.global.u32`
///   B) one 16-byte STRUCT load via AsAligned16()  -> expect `ld.global.v4.u32`
/// Confirms (or refutes) that the vectorized path already exists in the library, so the
/// consumer just needs a 16-byte struct view + an alignment hint - no ILGPU change.
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- vectorized-load-ptx
/// </summary>
internal static class VectorizedLoadPtxDump
{
    [StructLayout(LayoutKind.Sequential)]
    private struct U4 { public uint a, b, c, d; }

    // A) four separate scalar uint loads from adjacent elements.
    static void ScalarLoadsKernel(Index1D i, ArrayView<uint> w, ArrayView<uint> o)
    {
        int b = i * 4;
        o[i] = w[b] + w[b + 1] + w[b + 2] + w[b + 3];
    }

    // B) one 16-byte struct load from a 16-byte-aligned view.
    static void StructLoadKernel(Index1D i, ArrayView<U4> w, ArrayView<uint> o)
    {
        var v = w.AsAligned16()[i];
        o[i] = v.a + v.b + v.c + v.d;
    }

    public static Task<int> Run()
    {
        using var context = Context.Create(b => b.Cuda());
        var dev = context.GetCudaDevice(0);
        if (dev == null) { Console.WriteLine("[vectorized-load-ptx] no CUDA device"); return Task.FromResult(1); }
        using var acc = dev.CreateCudaAccelerator(context);
        Console.WriteLine($"[vectorized-load-ptx] {acc.Name}");

        var outDir = Path.Combine(Path.GetTempPath(), "vectorized_load_ptx");
        Directory.CreateDirectory(outDir);

        DumpPtx(acc, nameof(ScalarLoadsKernel), "A_scalar", outDir);
        DumpPtx(acc, nameof(StructLoadKernel), "B_struct16", outDir);
        return Task.FromResult(0);
    }

    private static void DumpPtx(CudaAccelerator acc, string methodName, string label, string outDir)
    {
        var method = typeof(VectorizedLoadPtxDump).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)!;
        var compiled = acc.Backend.Compile(
            EntryPointDescription.FromImplicitlyGroupedKernel(method),
            new KernelSpecialization(256, null));
        var ptx = (compiled as PTXCompiledKernel)?.PTXAssembly ?? "(not PTX)";
        var path = Path.Combine(outDir, label + ".ptx");
        File.WriteAllText(path, ptx);

        // ILGPU emits generic-state-space loads: `ld.v4.b32` (128-bit vectorized, = SASS
        // LDG.E.128) vs scalar `ld.b32` - NOT the `.global.u32` mnemonic.
        int v4 = CountOccurrences(ptx, "ld.v4.b32");
        int v2 = CountOccurrences(ptx, "ld.v2.b32");
        int scalar = CountOccurrences(ptx, "ld.b32") - CountOccurrences(ptx, "ld.param.b32");
        Console.WriteLine($"[vectorized-load-ptx] {label}: ld.v4.b32={v4}, ld.v2.b32={v2}, scalar ld.b32={scalar} -> {path}");
        foreach (var line in ptx.Split('\n'))
            if (line.Contains("ld.v4.b32") || line.Contains("ld.v2.b32")) Console.WriteLine("    " + line.Trim());
    }

    private static int CountOccurrences(string s, string sub)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
        return n;
    }
}
