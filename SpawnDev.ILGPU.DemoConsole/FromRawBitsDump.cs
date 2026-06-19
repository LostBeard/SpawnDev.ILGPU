using System;
using System.IO;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Dumps the generated WGSL + GLSL for the in-kernel `(float)Float4E2M1.FromRawBits((byte)code)`
/// decode path (and the Half variant), to diagnose the browser construction-from-register gap.
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- fromrawbits-dump
/// </summary>
internal static class FromRawBitsDump
{
    static void Fp4Kernel(Index1D i, ArrayView<int> codes, ArrayView<float> outF)
        => outF[i] = (float)Float4E2M1.FromRawBits((byte)codes[i]);

    static void HalfKernel(Index1D i, ArrayView<int> codes, ArrayView<float> outF)
        => outF[i] = (float)ILGPU.Half.FromRawBits((ushort)codes[i]);

    public static Task<int> Run()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "fromrawbits-dump");
        Directory.CreateDirectory(outDir);

        var kernels = new (string name, Delegate k)[]
        {
            ("fp4", (Action<Index1D, ArrayView<int>, ArrayView<float>>)Fp4Kernel),
            ("half", (Action<Index1D, ArrayView<int>, ArrayView<float>>)HalfKernel),
        };

        foreach (var (name, k) in kernels)
        {
            foreach (var (tag, profile) in new[]
            {
                ("wgsl", CapabilityProfiles.WebGPUFull),
                ("wgsl-baseline", CapabilityProfiles.WebGPUBaseline),
                ("glsl", CapabilityProfiles.WebGL2Baseline),
            })
            {
                try
                {
                    var r = ShaderCompiler.Generate(k, profile);
                    var src = r.Source ?? "(no source)";
                    var path = Path.Combine(outDir, $"{name}_{tag}.txt");
                    File.WriteAllText(path, src);
                    Console.WriteLine($"[fromrawbits-dump] {name} {tag}: {src.Length} chars HasErrors={r.HasErrors} -> {path}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[fromrawbits-dump] {name} {tag}: EXCEPTION {ex.Message}");
                }
            }
        }
        Console.WriteLine($"[fromrawbits-dump] out dir: {outDir}");
        return Task.FromResult(0);
    }
}
