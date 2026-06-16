using System;
using System.Numerics;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

/// <summary>
/// Desktop repro for Tuvok's generic INumber&lt;T&gt; Half/bf16 codegen gaps (DevComms
/// tuvok-to-geordi-generic-INumber-kernel-codegen-gaps). ONE generic kernel
/// y[i] = relu(x[i]*scale + bias) loaded for float (control) / ILGPU.Half / ILGPU.BFloat16 on the
/// DESKTOP backends (CPU/CUDA/OpenCL) — captures the exact exception + stack for:
///   gap #1 PTX generic bf16  (CUDA: KeyNotFoundException 'BFloat16' at compile)
///   gap #2 low-p SCALAR param (OpenCL: CL_INVALID_ARG_SIZE at launch)
/// No browser. WebGPU/WebGL/Wasm covered by the BackendTestBase regression test.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- generic-precision-repro
/// </summary>
internal static class GenericPrecisionRepro
{
    private static void FusedReluGeneric<T>(Index1D i,
        ArrayView1D<T, Stride1D.Dense> x, ArrayView1D<T, Stride1D.Dense> y, T scale, T bias)
        where T : unmanaged, INumber<T>
    {
        T v = x[i] * scale + bias;
        y[i] = v > T.Zero ? v : T.Zero;
    }

    public static async Task<int> Run()
    {
        using var context = Context.Create(b => b.Default().EnableAlgorithms());
        foreach (var dev in context)
        {
            // Desktop backends only (CPU/CUDA/OpenCL). Skip if none.
            if (dev.AcceleratorType != AcceleratorType.CPU &&
                dev.AcceleratorType != AcceleratorType.Cuda &&
                dev.AcceleratorType != AcceleratorType.OpenCL)
                continue;

            using var acc = dev.CreateAccelerator(context);
            Console.WriteLine($"\n=== {acc.AcceleratorType} ({acc.Name}) ===");
            TryRun<float>(acc, "float ", v => v, v => v);
            TryRun<global::ILGPU.Half>(acc, "Half  ", v => (global::ILGPU.Half)v, v => (float)v);
            TryRun<global::ILGPU.BFloat16>(acc, "bf16  ", v => (global::ILGPU.BFloat16)v, v => (float)v);
        }
        return 0;
    }

    private static void TryRun<T>(Accelerator acc, string label, Func<float, T> toT, Func<T, float> toF)
        where T : unmanaged, INumber<T>
    {
        const int n = 257; const float scale = 1.5f, bias = -0.25f;
        var x = new T[n];
        var expected = new float[n];
        var rng = new Random(7);
        for (int i = 0; i < n; i++)
        {
            float xf = (float)(rng.NextDouble() * 4 - 2);
            x[i] = toT(xf);
            float v = xf * scale + bias;
            expected[i] = v > 0f ? v : 0f;
        }

        try
        {
            using var inBuf = acc.Allocate1D(x);
            using var outBuf = acc.Allocate1D<T>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, Stride1D.Dense>, T, T>(FusedReluGeneric<T>);
            k(n, inBuf.View, outBuf.View, toT(scale), toT(bias));
            acc.Synchronize();
            var got = outBuf.GetAsArray1D();

            int bad = 0; int firstBad = -1; float gotF0 = 0, expF0 = 0;
            for (int i = 0; i < n; i++)
            {
                float g = toF(got[i]);
                float tol = MathF.Max(3e-2f, MathF.Abs(expected[i]) * 3e-2f);
                if (MathF.Abs(g - expected[i]) > tol)
                {
                    if (bad == 0) { firstBad = i; gotF0 = g; expF0 = expected[i]; }
                    bad++;
                }
            }
            if (bad == 0)
                Console.WriteLine($"  {label}: OK (compiles + runs + matches CPU)");
            else
                Console.WriteLine($"  {label}: WRONG OUTPUT {bad}/{n}, first@{firstBad} got={gotF0} want={expF0} (rawbits got[{firstBad}]={RawBits(got[firstBad])})");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {label}: {ex.GetType().Name}: {ex.Message}");
            // Recurse inner exceptions (InternalCompilerException wraps the real KeyNotFoundException).
            var inner = ex.InnerException;
            int depth = 0;
            while (inner != null && depth < 4)
            {
                Console.WriteLine($"     INNER[{depth}] {inner.GetType().Name}: {inner.Message}");
                var st = (inner.StackTrace ?? "").Split('\n');
                for (int i = 0; i < st.Length && i < 3; i++)
                    Console.WriteLine($"        {st[i].Trim()}");
                inner = inner.InnerException; depth++;
            }
        }
    }

    private static string RawBits<T>(T v) where T : unmanaged
    {
        if (System.Runtime.CompilerServices.Unsafe.SizeOf<T>() == 2)
            return System.Runtime.CompilerServices.Unsafe.As<T, ushort>(ref v).ToString();
        return v.ToString() ?? "?";
    }
}
