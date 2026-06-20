using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline WGSL dump of a kernel that loads a packed-4-bit view through a [NoInlining] HELPER fn
/// (routes through WGSLFunctionGenerator, not the kernel generator). Run:
///   dotnet run --project SpawnDev.ILGPU.DemoConsole -- fp4-helper-wgsl
/// </summary>
internal static class Fp4HelperWgslDump
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static float Fp4HelperLoad(ArrayView<Float4E2M1> v, int i) => v[i];
    static void Fp4ViaHelperKernel(Index1D i, ArrayView<Float4E2M1> packed, ArrayView<float> outF)
        => outF[i] = Fp4HelperLoad(packed, i.X);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int QInt4HelperLoad(ArrayView<QInt4> v, int i) => v[i];
    static void QInt4ViaHelperKernel(Index1D i, ArrayView<QInt4> packed, ArrayView<int> outI)
        => outI[i] = QInt4HelperLoad(packed, i.X);

    public static Task<int> Run()
    {
        var profile = CapabilityProfiles.WebGPUBaseline;
        var outDir = Path.Combine(Path.GetTempPath(), "fp4_helper_wgsl");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[fp4-helper-wgsl] out dir: {outDir}");
        var spec = new KernelSpecialization(256, null);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        foreach (var name in new[] { nameof(Fp4ViaHelperKernel), nameof(QInt4ViaHelperKernel) })
        {
            var m = typeof(Fp4HelperWgslDump).GetMethod(name, flags)!;
            try
            {
                var result = ShaderCompiler.Generate(m, profile, spec);
                var wgsl = result.Source ?? "(null)";
                var path = Path.Combine(outDir, name + ".wgsl");
                File.WriteAllText(path, wgsl);
                Console.WriteLine($"  [{name}] len={wgsl.Length} hasErrors={result.HasErrors} -> {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{name}] EXCEPTION: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        return Task.FromResult(0);
    }
}
