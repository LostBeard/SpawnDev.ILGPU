using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline GLSL dump: reading ONE field of a struct-array element into a scalar buffer
/// (<c>keys[i] = pairs[i].Key * 2f</c>) returned 0 for every element on WebGL (2026-09-28,
/// BackendTestBase.SimdAddressShapes), while copying whole elements (<c>dst[i] = src[i]</c>) is right.
/// Dumps both kernels for comparison. Run:
///   dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- struct-field-glsl
/// </summary>
internal static class StructFieldGlslDump
{
    public struct FieldPair
    {
        public float Key;
        public int Value;
    }

    static void FieldReadKernel(Index1D i, ArrayView<FieldPair> pairs, ArrayView<float> keys) =>
        keys[i] = pairs[i].Key * 2f;

    static void ValueFieldReadKernel(Index1D i, ArrayView<FieldPair> pairs, ArrayView<int> values) =>
        values[i] = pairs[i].Value + 1;

    static void WholeCopyKernel(Index1D i, ArrayView<FieldPair> src, ArrayView<FieldPair> dst) =>
        dst[i] = src[i];

    public static Task<int> Run()
    {
        var profile = CapabilityProfiles.WebGL2Baseline;
        var outDir = Path.Combine(Path.GetTempPath(), "struct_field_glsl");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[struct-field-glsl] out dir: {outDir}");
        var spec = new KernelSpecialization(256, null);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        foreach (var name in new[] { nameof(FieldReadKernel), nameof(ValueFieldReadKernel), nameof(WholeCopyKernel) })
        {
            var m = typeof(StructFieldGlslDump).GetMethod(name, flags)!;
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
