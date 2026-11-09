using System.Runtime.InteropServices;
using System.Text;
using ILGPU;
using ILGPU.Runtime.Cuda;

// Replays PTXBackend.GenerateLibDeviceCode for one libdevice wrapper and prints NVVM's result + program log (the backend
// drops both on failure). CPU only: NVVM runs on the host. `nvvm-probe` (CUDA 13: kernels that call __ilgpu__nv_ceil /
// __nv_fmod / __nv_expf failed to JIT with "unknown symbol", 5.4.19 sweep).
static class NvvmProbe
{
    const string Prefix = @"
            target triple = ""nvptx64-unknown-cuda""
            target datalayout = ""e-p:64:64:64-i1:8:8-i8:8:8-i16:16:16-i32:32:32-i64:64:64-f32:32:32-f64:64:64-v16:16:16-v32:32:32-v64:64:64-v128:128:128-n16:32:64""";

    static string Wrapper(string type, string name) => $@"
            declare {type} @{name}({type} %x)

            define {type} @__ilgpu_{name}({type} %x) {{
            entry:
                %call = call {type} @{name}({type} %x)
                ret {type} %call
            }}";

    public static unsafe Task<int> Run(string[] args)
    {
        using var context = Context.Create(b => b.Cuda());
        var nvvmPath = context.Properties.LibNvvmPath!;
        var libDevicePath = context.Properties.LibDevicePath!;
        Console.WriteLine($"nvvm: {nvvmPath}\nlibdevice: {libDevicePath}");
        var api = NvvmAPI.Create(nvvmPath, libDevicePath);
        api.GetVersion(out int vMaj, out int vMin);
        api.GetIRVersion(out int irMaj, out int irMin, out int dbgMaj, out int dbgMin);
        Console.WriteLine($"NVVM {vMaj}.{vMin}, IR {irMaj}.{irMin}, debug {dbgMaj}.{dbgMin}");
        foreach (var (type, name) in new[] { ("double", "__nv_cos"), ("double", "__nv_ceil"), ("float", "__nv_expf"), ("double", "__nv_floor") })
        {
            var module = $"\n            !nvvmir.version = !{{!0}}\n            !0 = !{{i32 {irMaj}, i32 0}}\n" + Prefix + "\n" + Wrapper(type, name);
            api.CreateProgram(out var program);
            var bytes = Encoding.ASCII.GetBytes(module);
            NvvmResult r;
            fixed (byte* p = bytes) r = api.AddModuleToProgram(program, new IntPtr(p), new IntPtr(bytes.Length), null);
            var step = "add";
            if (r == NvvmResult.NVVM_SUCCESS)
            {
                step = "lazy-add libdevice";
                fixed (byte* p = api.LibDeviceBytes) r = api.LazyAddModuleToProgram(program, new IntPtr(p), new IntPtr(api.LibDeviceBytes.Length), null);
            }
            if (r == NvvmResult.NVVM_SUCCESS)
            {
                step = "compile";
                var opt = Encoding.ASCII.GetBytes("-arch=compute_89\0");
                fixed (byte* o = opt)
                {
                    var values = stackalloc IntPtr[1];
                    values[0] = new IntPtr(o);
                    r = api.CompileProgram(program, 1, new IntPtr(values));
                }
            }
            api.GetProgramLog(program, out var log);
            string ptxHead = "";
            if (r == NvvmResult.NVVM_SUCCESS && api.GetCompiledResult(program, out var ptx) == NvvmResult.NVVM_SUCCESS)
                ptxHead = string.Join(" | ", (ptx ?? "").Split('\n').Where(l => l.Contains(".func") || l.Contains(".version") || l.Contains(".target")).Take(4));
            Console.WriteLine($"{name}: {step} -> {r}; log: {log?.Trim()}; ptx: {ptxHead}");
            api.DestroyProgram(ref program);
        }
        // What ILGPU emits for a kernel that needs libdevice (compile only, nothing launched).
        foreach (var algorithms in new[] { false, true })
        {
            using var ctx = Context.Create(b => { b.Cuda(); if (algorithms) b.EnableAlgorithms(); });
            using var acc = ctx.CreateCudaAccelerator(0);
            var entry = ILGPU.Backends.EntryPoints.EntryPointDescription.FromImplicitlyGroupedKernel(
                (algorithms ? (Action<ILGPU.Index1D, ILGPU.ArrayView<double>, ILGPU.ArrayView<double>>)FloorHelperKernel : FloorCeilKernel).Method);
            var compiled = (ILGPU.Backends.PTX.PTXCompiledKernel)acc.CompileKernel(entry);
            var lines = compiled.PTXAssembly.Split('\n');
            Console.WriteLine($"--- algorithms={algorithms}: {lines.Length} PTX lines; libdevice refs:");
            foreach (var l in lines.Where(l => l.Contains("__nv_") || l.Contains("//.version") || l.Contains("cvt.rpi") || l.Contains("cvt.rmi")).Take(12))
                Console.WriteLine("   " + l.Trim());
        }
        return Task.FromResult(0);
    }

    static void FloorCeilKernel(ILGPU.Index1D i, ILGPU.ArrayView<double> a, ILGPU.ArrayView<double> o) =>
        o[i] = Math.Floor(a[i]) + Math.Ceiling(a[i]);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static double FloorHelper(double x) => Math.Floor(x);
    static void FloorHelperKernel(ILGPU.Index1D i, ILGPU.ArrayView<double> a, ILGPU.ArrayView<double> o) =>
        o[i] = Math.Floor(a[i]) + FloorHelper(a[i]);
}
