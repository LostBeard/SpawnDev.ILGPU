using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline WGSL dump of RadixSortKernel1 + RadixSortKernel2 for QInt4 (the WebGPU-only mis-sort)
/// vs Float4E2M1 (the working 1-byte packed sibling) and bf16 (working 2-byte). No device.
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- qint4-radix-wgsl
/// </summary>
internal static class QInt4RadixWgslDump
{
    public static Task<int> Run()
    {
        var algAsm = typeof(AscendingQInt4).Assembly;
        var ext = algAsm.GetType("ILGPU.Algorithms.RadixSortExtensions")!;
        var k1 = ext.GetMethod("RadixSortKernel1", BindingFlags.NonPublic | BindingFlags.Static)!;
        var k2 = ext.GetMethod("RadixSortKernel2", BindingFlags.NonPublic | BindingFlags.Static)!;
        var spec4 = algAsm.GetTypes().First(t => t.Name == "Specialization4");

        var dense = typeof(Stride1D.Dense);

        var profile = CapabilityProfiles.WebGPUBaseline; // emulated f16, broadest packed path
        var outDir = Path.Combine(Path.GetTempPath(), "qint4_radix_wgsl");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[qint4-radix-wgsl] out dir: {outDir}");

        // (label, T, TOp)
        var cases = new (string label, Type t, Type op)[]
        {
            ("QInt4", typeof(QInt4), typeof(AscendingQInt4)),
            ("FP4",   typeof(Float4E2M1), typeof(AscendingFloat4E2M1)),
        };

        foreach (var (label, t, op) in cases)
        {
            // Kernel1<T, TStride, TOperation, TSpecialization>
            var m1 = k1.MakeGenericMethod(t, dense, op, spec4);
            // Kernel2<T, TInputStride, TOutputStride, TOperation, TSpecialization>
            var m2 = k2.MakeGenericMethod(t, dense, dense, op, spec4);

            DumpOne($"{label}_Kernel1", m1, profile, outDir);
            DumpOne($"{label}_Kernel2", m2, profile, outDir);
        }
        Console.WriteLine("[qint4-radix-wgsl] done.");
        return Task.FromResult(0);
    }

    private static void DumpOne(string name, MethodInfo method, CapabilityProfile profile, string outDir)
    {
        try
        {
            var spec = new KernelSpecialization(profile.MaxNumThreadsPerGroup > 0 ? profile.MaxNumThreadsPerGroup : 256, null);
            var result = ShaderCompiler.Generate(method, profile, spec);
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
}
