using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// WGSL counterpart to VectorizedLoadPtxDump: does the WGSL codegen emit a 128-bit vec4 load
/// (`array&lt;vec4&lt;f32&gt;&gt;` binding + single element load) for a 16-byte-aligned struct-of-4
/// view via AsAligned16(), the same construct that yields `ld.v4.b32` on PTX? Or does struct
/// flattening collapse it to `array&lt;f32&gt;` + 4 scalar loads? Seven's GEMM-core make-or-break.
/// Compiles two kernels to WGSL offline (no browser) and dumps + summarizes them.
///   A) 4 scalar f32 loads w[4*i+0..3]
///   B) one 16-byte struct load via AsAligned16()
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- vectorized-load-wgsl
/// </summary>
internal static class VectorizedLoadWgslProbe
{
    [StructLayout(LayoutKind.Sequential)]
    private struct F4 { public float a, b, c, d; }

    // A) four separate scalar f32 loads from adjacent elements.
    private static void ScalarLoadsKernel(Index1D i, ArrayView<float> w, ArrayView<float> o)
    {
        int b = i * 4;
        o[i] = w[b] + w[b + 1] + w[b + 2] + w[b + 3];
    }

    // B) one 16-byte struct load from a 16-byte-aligned view (mirrors the PTX proof, f32).
    private static void StructLoadKernel(Index1D i, ArrayView<F4> w, ArrayView<float> o)
    {
        var v = w.AsAligned16()[i];
        o[i] = v.a + v.b + v.c + v.d;
    }

    public static Task<int> Run()
    {
        var outDir = @"D:\users\tj\Projects\SpawnDev.ILGPU\vectorized_load_wgsl";
        Directory.CreateDirectory(outDir);
        Dump((Action<Index1D, ArrayView<float>, ArrayView<float>>)ScalarLoadsKernel, "A_scalar", outDir);
        Dump((Action<Index1D, ArrayView<F4>, ArrayView<float>>)StructLoadKernel, "B_struct16", outDir);
        return Task.FromResult(0);
    }

    private static void Dump(Delegate kernel, string label, string outDir)
    {
        try
        {
            var result = ShaderCompiler.Generate(kernel, CapabilityProfiles.WebGPUBaseline);
            var wgsl = result.Source ?? "";
            var path = Path.Combine(outDir, label + ".wgsl");
            File.WriteAllText(path, wgsl);
            int vec4Bind = CountOccurrences(wgsl, "array<vec4<f32>>");
            int f32Bind = CountOccurrences(wgsl, "array<f32>");
            int vec4Ty = CountOccurrences(wgsl, "vec4<f32>");
            Console.WriteLine($"[vectorized-load-wgsl] {label}: HasErrors={result.HasErrors} len={wgsl.Length}");
            Console.WriteLine($"    array<vec4<f32>> bindings={vec4Bind}, array<f32> bindings={f32Bind}, any vec4<f32>={vec4Ty} -> {path}");
            // Show the storage-buffer binding declarations (the binding type = the load width).
            foreach (var line in wgsl.Split('\n'))
                if (line.Contains("@group") || line.Contains("var<storage") || line.Contains("array<"))
                    Console.WriteLine("    BIND " + line.Trim());
            foreach (var diag in result.Diagnostics)
                Console.WriteLine($"    diag: {diag}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[vectorized-load-wgsl] {label} EXCEPTION: {ex.Message}");
        }
    }

    private static int CountOccurrences(string s, string sub)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
        return n;
    }
}
