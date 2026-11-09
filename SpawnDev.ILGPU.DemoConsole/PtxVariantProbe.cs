using System.Reflection;
using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

// Runs BackendTestBase.NestedLoopDoWhileKernel on CUDA from ILGPU's PTX as generated and from edited copies of it, and
// prints each variant's outputs: tells a PTX-text defect from a ptxas/driver one (Geordi's CUDA nested-loop miscompile,
// 2026-11-08). `ptx-variant-run` (uses the GPU).
static class PtxVariantProbe
{
    static readonly int[] Bands = { 4, 4, 4, 4, 4, 4, 6, 6, 8, 8, 10, 12, 16, 20, 24, 28, 34, 42, 50, 54, 76, 158, 0 };
    static readonly int[] Cases = { 70, 7, 255, 13, 7, 255, 76, 8, 255, 115, 8, 255, 88, 7, 6, 69, 9, 4, 288, 15, 3, 0, 7, 255, 1, 0, 0, 200, 2, 2 };

    public static Task<int> Run(string[] args)
    {
        foreach (var level in new[] { OptimizationLevel.O2, OptimizationLevel.O1, OptimizationLevel.O0 })
            RunLevel(level);
        return Task.FromResult(0);
    }

    static void RunLevel(OptimizationLevel level)
    {
        Console.WriteLine($"== {level}");
        using var context = Context.Create(b => b.Cuda().Optimize(level));
        using var acc = context.CreateCudaAccelerator(0);
        var entry = EntryPointDescription.FromImplicitlyGroupedKernel(BackendTestBase.NestedLoopDoWhileKernelDelegate.Method);
        var compiled = (PTXCompiledKernel)acc.CompileKernel(entry);
        var variants = new (string name, string ptx)[]
        {
            ("as generated", compiled.PTXAssembly),
            ("bra.uni -> bra", compiled.PTXAssembly.Replace("bra.uni\t", "bra\t")),
        };
        if (variants[1].ptx == variants[0].ptx) Console.WriteLine("(no bra.uni in this PTX)");
        int n = Cases.Length / 3;
        var ctor = typeof(PTXCompiledKernel).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)[0];
        foreach (var (name, ptx) in variants)
        {
            var k = (PTXCompiledKernel)ctor.Invoke(new object?[] { context, compiled.EntryPoint, compiled.Info, ptx });
            using var kernel = acc.LoadAutoGroupedKernel(k);
            var launch = kernel.CreateLauncherDelegate<Action<AcceleratorStream, Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>>();
            using var b = acc.Allocate1D(Bands);
            using var c = acc.Allocate1D(Cases);
            using var o = acc.Allocate1D<int>(n * 4);
            launch(acc.DefaultStream, n, b.View, c.View, o.View);
            acc.Synchronize();
            var got = o.GetAsArray1D();
            Console.WriteLine($"[{name}] " + string.Join(" | ", Enumerable.Range(0, n).Select(i => $"{got[4 * i]},{got[4 * i + 1]},{got[4 * i + 2]},{got[4 * i + 3]}")));
        }
        Console.WriteLine("ptx .version line: " + compiled.PTXAssembly.Split('\n').FirstOrDefault(l => l.StartsWith(".version")));
    }
}
