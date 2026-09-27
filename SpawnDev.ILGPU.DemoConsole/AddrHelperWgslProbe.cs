using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using SpawnDev.ILGPU;

/// <summary>
/// Dumps the WGSL + GLSL of BackendTestBase.AddrHelperKernel (a LocalMemory-view helper called from 8 sites) so
/// the fn-def-vs-inlined outcome is visible without a browser. Run: dotnet run --project
/// SpawnDev.ILGPU.DemoConsole -- addr-helper-wgsl
/// </summary>
internal static class AddrHelperWgslProbe
{
    public static Task<int> Run(string kernelName = "AddrHelperKernel")
    {
        var m = typeof(SpawnDev.ILGPU.Demo.Shared.UnitTests.BackendTestBase)
            .GetMethod(kernelName, BindingFlags.NonPublic | BindingFlags.Static)!;
        foreach (var profile in new[] { CapabilityProfiles.WebGPUBaseline, CapabilityProfiles.Resolve("WebGL2-Dekker")! })
        {
            var r = ShaderCompiler.Generate(m, profile);
            var src = r.Source ?? "";
            var path = Path.Combine(Path.GetTempPath(), $"{kernelName}_{profile.Name}.txt");
            File.WriteAllText(path, src);
            int fnDefs = 0, idx = 0;
            while ((idx = src.IndexOf("AddrHelperSolve", idx, StringComparison.Ordinal)) >= 0) { fnDefs++; idx++; }
            Console.WriteLine($"[addr-helper] {profile.Name}: errors={r.HasErrors} len={src.Length} 'AddrHelperSolve' mentions={fnDefs} -> {path}");
        }
        return Task.FromResult(0);
    }
}
