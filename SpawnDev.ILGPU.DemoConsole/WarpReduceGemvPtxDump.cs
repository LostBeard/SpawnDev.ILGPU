using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

/// <summary>
/// Dumps the generated PTX for a warp-per-column GEMV that reduces via Warp.ShuffleDown
/// (no shared memory, no barriers) - the Ollama decode recipe Tuvok asked about. Confirms
/// whether ILGPU's PTX backend emits native `shfl.sync.down.b32` for Warp.ShuffleDown, which
/// is the crux of whether a warp-reduce GEMV can reach bandwidth on the 4070.
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- warp-reduce-gemv-ptx
/// </summary>
internal static class WarpReduceGemvPtxDump
{
    // Warp-per-column GEMV, quant-free (the reduction structure is what matters here).
    // One warp computes output[n]; lane strides the K-loop; warp-shuffle tree reduces.
    static void WarpGemvKernel(
        Index1D idx,
        ArrayView<float> input,    // [K]
        ArrayView<float> weight,   // [N*K]
        ArrayView<float> output,   // [N]
        int K, int N)
    {
        int warpSize = Warp.WarpSize;
        int lane = Warp.LaneIdx;
        int n = idx / warpSize;            // global thread / warpSize -> column
        float partial = 0f;
        if (n < N)
        {
            int rowBase = n * K;
            for (int k = lane; k < K; k += warpSize)
                partial += input[k] * weight[rowBase + k];
        }
        // Warp-shuffle tree reduction (no shared mem, no barriers).
        for (int offset = warpSize / 2; offset > 0; offset >>= 1)
            partial += Warp.ShuffleDown(partial, offset);
        if (lane == 0 && n < N)
            output[n] = partial;
    }

    public static Task<int> Run()
    {
        using var context = Context.Create(b => b.Cuda().EnableAlgorithms());
        var dev = context.GetCudaDevice(0);
        if (dev == null) { Console.WriteLine("[warp-reduce-gemv-ptx] no CUDA device"); return Task.FromResult(1); }
        using var acc = dev.CreateCudaAccelerator(context);
        Console.WriteLine($"[warp-reduce-gemv-ptx] {acc.Name}, WarpSize={acc.WarpSize}, MaxThreads/Group={acc.MaxNumThreadsPerGroup}");

        var method = typeof(WarpReduceGemvPtxDump).GetMethod(nameof(WarpGemvKernel), BindingFlags.NonPublic | BindingFlags.Static)!;
        var compiled = acc.Backend.Compile(
            EntryPointDescription.FromImplicitlyGroupedKernel(method),
            new KernelSpecialization(256, null));
        var ptx = (compiled as PTXCompiledKernel)?.PTXAssembly ?? "(not PTX)";
        var outDir = Path.Combine(Path.GetTempPath(), "warp_reduce_gemv_ptx");
        Directory.CreateDirectory(outDir);
        var path = Path.Combine(outDir, "WarpGemvKernel.ptx");
        File.WriteAllText(path, ptx);

        int shfl = CountOccurrences(ptx, "shfl");
        int bar = CountOccurrences(ptx, "bar.sync");
        int sharedDecl = CountOccurrences(ptx, ".shared");
        Console.WriteLine($"[warp-reduce-gemv-ptx] len={ptx.Length} -> {path}");
        Console.WriteLine($"[warp-reduce-gemv-ptx] shfl count={shfl}, bar.sync count={bar}, .shared decls={sharedDecl}");
        // Print the shfl lines for proof.
        foreach (var line in ptx.Split('\n'))
            if (line.Contains("shfl")) Console.WriteLine("    " + line.Trim());
        return Task.FromResult(0);
    }

    private static int CountOccurrences(string s, string sub)
    {
        int n = 0, i = 0;
        while ((i = s.IndexOf(sub, i, StringComparison.Ordinal)) >= 0) { n++; i += sub.Length; }
        return n;
    }
}
