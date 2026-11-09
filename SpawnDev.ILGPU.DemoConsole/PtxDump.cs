using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

// Offline PTX of a test kernel, no CUDA device used (PTXBackend on its own): for reading the codegen while the GPU is
// busy. `ptx-dump [O0|O1|O2] [out.ptx]`. Kernel: BackendTestBase.NestedLoopDoWhileKernel (Geordi's CUDA nested-loop
// miscompile, 2026-11-08).
static class PtxDump
{
    public static Task<int> Run(string[] args)
    {
        var level = args.Length > 1 ? Enum.Parse<OptimizationLevel>(args[1]) : OptimizationLevel.O2;
        using var context = Context.Create(b => b.Optimize(level));
        using var backend = new PTXBackend(context, new CudaCapabilityContext(CudaArchitecture.SM_89),
            CudaArchitecture.SM_89, CudaInstructionSet.ISA_85, null);
        var kernelDelegate = args.Length > 3 && args[3] == "divpack"
            ? BackendTestBase.DivergentPackEarlyReturnLongDelegate
            : BackendTestBase.NestedLoopDoWhileKernelDelegate;
        var entry = EntryPointDescription.FromImplicitlyGroupedKernel(kernelDelegate.Method);
        var compiled = (PTXCompiledKernel)backend.Compile(entry, default(KernelSpecialization));
        if (args.Length > 2) File.WriteAllText(args[2], compiled.PTXAssembly);
        else Console.WriteLine(compiled.PTXAssembly);
        return Task.FromResult(0);
    }
}
