using System;
using System.Threading.Tasks;
using ILGPU;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Offline validation of the Stage-3a SIMD uniformity analysis (WasmSimdAnalysis) on REAL kernel IR.
/// Compiles sample kernels through the offline Wasm codegen path (no browser) and prints
/// WasmBackend.LastSimdAnalysis for each — confirming the analysis classifies a clean element-wise
/// kernel as vectorizable and a data-dependent-control-flow kernel as not (Stage 3b).
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- simd-analyze
/// </summary>
internal static class WasmSimdAnalyzeProbe
{
    // Clean element-wise map: uniform control flow, no branches/barriers. EXPECT Vectorizable=true.
    private static void ElementwiseKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o)
        => o[i] = a[i] * 2f + b[i];

    // Element-wise with a computed (data-dependent) gather index — still uniform control flow, just a
    // lane-variant load address. EXPECT Vectorizable=true (the load result is lane-variant; the emitter
    // will gather it, but there is no lane-variant BRANCH).
    private static void GatherKernel(Index1D i, ArrayView<int> idx, ArrayView<float> src, ArrayView<float> o)
        => o[i] = src[idx[i]] + 1f;

    // Data-dependent loop bound -> the loop's branch condition is lane-variant. EXPECT Vectorizable=false
    // (needs Stage 3b masks).
    private static void DataDependentLoopKernel(Index1D i, ArrayView<int> counts, ArrayView<float> o)
    {
        float acc = 0f;
        int n = counts[i];
        for (int k = 0; k < n; k++) acc += k * 0.5f;
        o[i] = acc;
    }

    public static Task<int> Run()
    {
        Report("Elementwise (o[i]=a[i]*2+b[i])",
            (Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>)ElementwiseKernel);
        Report("Gather (o[i]=src[idx[i]]+1)",
            (Action<Index1D, ArrayView<int>, ArrayView<float>, ArrayView<float>>)GatherKernel);
        Report("DataDependentLoop (for k<counts[i])",
            (Action<Index1D, ArrayView<int>, ArrayView<float>>)DataDependentLoopKernel);
        return Task.FromResult(0);
    }

    private static void Report(string name, Delegate kernel)
    {
        WasmBackend.LastSimdAnalysis = default; // clear so a failed compile is visible
        try
        {
            var gen = ShaderCompiler.Generate(kernel, CapabilityProfiles.WasmDefault);
            var r = WasmBackend.LastSimdAnalysis;
            Console.WriteLine($"[simd-analyze] {name,-40} -> {r}  ({(gen.Binary?.Length ?? 0)} bytes)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[simd-analyze] {name,-40} -> COMPILE ERROR: {ex.Message}");
        }
    }
}
