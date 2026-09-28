using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// Offline WGSL dump: an auto-grouped Index2D kernel over an odd 33x17 grid wrote (x=33, y=0) where (0, 1) belongs,
/// on WebGPU WITH subgroups only (WebGPUNoSubgroups / WebGL / Wasm / desktop right) - 2026-09-28 ILGPU sweep,
/// CpuLaneLoop_LaneIndependentKernels_ExactAndOnTheLaneLoop. Dumps BackendTestBase.LaneLoop2DKernel under both
/// WebGPU profiles so the index decomposition can be diffed. Run:
///   dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- lane2d-wgsl
/// </summary>
internal static class Lane2DWgslDump
{
    public static Task<int> Run()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "lane2d_wgsl");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[lane2d-wgsl] out dir: {outDir}");
        var m = typeof(BackendTestBase).GetMethod("LaneLoop2DKernel", BindingFlags.NonPublic | BindingFlags.Static)!;
        var spec = new KernelSpecialization(256, null);
        foreach (var (name, profile) in new[] { ("full", CapabilityProfiles.WebGPUFull), ("nosubgroups", CapabilityProfiles.WebGPUNoSubgroups) })
        {
            try
            {
                var result = ShaderCompiler.Generate(m, profile, spec);
                var src = result.Source ?? "(null)";
                var path = Path.Combine(outDir, name + ".wgsl");
                File.WriteAllText(path, src);
                Console.WriteLine($"  [{name}] len={src.Length} hasErrors={result.HasErrors} -> {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{name}] EXCEPTION: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }
        return Task.FromResult(0);
    }
}
