using System;
using System.IO;
using System.Threading.Tasks;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Crypto;

/// <summary>
/// Offline GLSL dump of Autolykos2.GenerateDatasetKernel and MineKernel - measures generated shader size
/// without needing a live browser, to check whether the WebGL struct-field TF fix (splitting
/// each Element256 ulong field into lo/hi uint varyings) pushed an already-Blake2b-heavy
/// kernel into ANGLE shader-compile-time blowup territory. Run:
///   dotnet run --project SpawnDev.ILGPU.DemoConsole -- autolykos2-dataset-glsl
/// </summary>
internal static class Autolykos2DatasetGlslDump
{
    public static Task<int> Run()
    {
        var profile = CapabilityProfiles.WebGL2Baseline;
        var outDir = Path.Combine(Path.GetTempPath(), "autolykos2_dataset_glsl");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[autolykos2-dataset-glsl] out dir: {outDir}");
        var spec = new KernelSpecialization(256, null);
        foreach (var name in new[] { nameof(Autolykos2.GenerateDatasetKernel), nameof(Autolykos2.MineKernel) })
        {
            var m = typeof(Autolykos2).GetMethod(name)!;
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var result = ShaderCompiler.Generate(m, profile, spec);
                sw.Stop();
                var glsl = result.Source ?? "(null)";
                var lineCount = glsl.Split('\n').Length;
                var path = Path.Combine(outDir, name + ".glsl");
                File.WriteAllText(path, glsl);
                Console.WriteLine($"  {name}: codegen took {sw.ElapsedMilliseconds}ms");
                Console.WriteLine($"  len={glsl.Length} chars, {lineCount} lines, hasErrors={result.HasErrors} -> {path}");
                if (result.HasErrors)
                    foreach (var d in result.Diagnostics)
                        Console.WriteLine($"  [{d.Severity}] {d.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {name} EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            }
        }
        return Task.FromResult(0);
    }
}
