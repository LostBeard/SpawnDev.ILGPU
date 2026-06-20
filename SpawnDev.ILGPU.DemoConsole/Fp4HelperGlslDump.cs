using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline GLSL dump of a kernel that loads a packed-4-bit view through a [NoInlining] HELPER fn
/// (routes through GLSLFunctionGenerator, not the kernel generator). Run:
///   dotnet run --project SpawnDev.ILGPU.DemoConsole -- fp4-helper-glsl
/// </summary>
internal static class Fp4HelperGlslDump
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
        var profile = CapabilityProfiles.WebGL2Baseline;
        var outDir = Path.Combine(Path.GetTempPath(), "fp4_helper_glsl");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[fp4-helper-glsl] out dir: {outDir}");
        var spec = new KernelSpecialization(256, null);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        foreach (var name in new[] { nameof(Fp4ViaHelperKernel), nameof(QInt4ViaHelperKernel) })
        {
            var m = typeof(Fp4HelperGlslDump).GetMethod(name, flags)!;
            try
            {
                var result = ShaderCompiler.Generate(m, profile, spec);
                var glsl = result.Source ?? "(null)";
                var path = Path.Combine(outDir, name + ".glsl");
                File.WriteAllText(path, glsl);
                Console.WriteLine($"  [{name}] len={glsl.Length} hasErrors={result.HasErrors} -> {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{name}] EXCEPTION: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        return Task.FromResult(0);
    }
}
