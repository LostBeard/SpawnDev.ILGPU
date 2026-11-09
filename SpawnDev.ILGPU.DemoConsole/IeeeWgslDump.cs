using SpawnDev.ILGPU;
using SpawnDev.ILGPU.CodeGen;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

// Offline WGSL of BackendTestBase.F64IeeeOpsKernel in each f64 emulation mode (no browser, no GPU), for naga.
// `ieee-wgsl <outDir>` writes f64ops_<Mode>.wgsl.
static class IeeeWgslDump
{
    public static Task<int> Run(string[] args)
    {
        var dir = args.Length > 1 ? args[1] : ".";
        Directory.CreateDirectory(dir);
        // The nested do-while kernel (PTX phi-binding bug; WebGL returned zeros) as GLSL and WGSL.
        File.WriteAllText(Path.Combine(dir, "nestedloop.vert"),
            ShaderCompiler.Generate(BackendTestBase.NestedLoopDoWhileKernelDelegate, CapabilityProfiles.WebGL2Baseline).Source);
        File.WriteAllText(Path.Combine(dir, "nestedloop.wgsl"),
            ShaderCompiler.Generate(BackendTestBase.NestedLoopDoWhileKernelDelegate, CapabilityProfiles.WebGPUBaseline).Source);
        foreach (var mode in new[] { F64EmulationMode.Ieee, F64EmulationMode.Dekker, F64EmulationMode.Ozaki })
        {
            var profile = CapabilityProfiles.WebGPUBaseline with { Float64Mode = mode };
            var generated = ShaderCompiler.Generate(BackendTestBase.F64IeeeOpsKernelDelegate, profile);
            var path = Path.Combine(dir, $"f64ops_{mode}.wgsl");
            File.WriteAllText(path, generated.Source);
            Console.WriteLine($"{path}: {generated.Source?.Length ?? 0} chars");
            var glslProfile = CapabilityProfiles.WebGL2Baseline with { Float64Mode = mode };
            var glsl = ShaderCompiler.Generate(BackendTestBase.F64IeeeOpsKernelDelegate, glslProfile);
            var glslPath = Path.Combine(dir, $"f64ops_{mode}.vert");
            File.WriteAllText(glslPath, glsl.Source);
            Console.WriteLine($"{glslPath}: {glsl.Source?.Length ?? 0} chars");
            foreach (var g in new[] { "F64IeeeG0", "F64IeeeG0b", "F64IeeeG0c", "F64IeeeG1", "F64IeeeG2", "F64IeeeG3" })
            {
                var m = typeof(BackendTestBase).GetMethod(g, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                    ?? throw new Exception(g + " not found");
                File.WriteAllText(Path.Combine(dir, $"{g}_{mode}.wgsl"), ShaderCompiler.Generate(m, profile).Source);
                File.WriteAllText(Path.Combine(dir, $"{g}_{mode}.vert"), ShaderCompiler.Generate(m, glslProfile).Source);
            }
        }
        return Task.FromResult(0);
    }
}
