using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

/// <summary>
/// Dumps the generated PTX for a kernel that loads a packed FP4 view through a [NoInlining] helper.
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- fp4-helper-ptx
/// </summary>
internal static class Fp4HelperPtxDump
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static float Fp4HelperLoad(ArrayView<Float4E2M1> v, int i) => v[i];
    static void Fp4ViaHelperKernel(Index1D i, ArrayView<Float4E2M1> packed, ArrayView<float> outF)
        => outF[i] = Fp4HelperLoad(packed, i.X);

    public static Task<int> Run()
    {
        using var context = Context.Create(b => b.Cuda().EnableAlgorithms());
        var dev = context.GetCudaDevice(0);
        if (dev == null) { Console.WriteLine("[fp4-helper-ptx] no CUDA device"); return Task.FromResult(1); }
        using var acc = dev.CreateCudaAccelerator(context);

        var method = typeof(Fp4HelperPtxDump).GetMethod(nameof(Fp4ViaHelperKernel), BindingFlags.NonPublic | BindingFlags.Static)!;
        var compiled = acc.Backend.Compile(
            EntryPointDescription.FromImplicitlyGroupedKernel(method),
            new KernelSpecialization(256, null));
        var ptx = (compiled as PTXCompiledKernel)?.PTXAssembly ?? "(not PTX)";
        var outDir = Path.Combine(Path.GetTempPath(), "fp4_helper_ptx");
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "Fp4ViaHelperKernel.ptx");
        File.WriteAllText(path, ptx);
        Console.WriteLine($"[fp4-helper-ptx] len={ptx.Length} -> {path}");
        return Task.FromResult(0);
    }
}
