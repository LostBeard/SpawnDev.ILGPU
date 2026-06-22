using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

/// <summary>
/// Proves the new ILGPU CUDA graph capture/replay API (CudaStream.BeginCapture /
/// EndCapture, CudaGraph.Instantiate, CudaGraphExec.Launch) works on real hardware and
/// actually collapses per-kernel host dispatch overhead - the lever Tuvok needs to beat
/// Ollama on LLM decode (~700 tiny launches/token dominated by ~25ms of CPU dispatch).
///
/// What it checks:
///   1. CORRECTNESS - a captured sequence of S increment kernels, replayed R times,
///      produces exactly S*R per element (CPU reference). Capture records but does NOT
///      execute, so only the replays count.
///   2. DISPATCH WIN - times R steps via direct per-kernel launches vs R single
///      cuGraphLaunch replays, with a trivial kernel so the delta is pure host overhead.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- cuda-graph-capture
/// </summary>
internal static class CudaGraphCaptureProof
{
    // Trivial kernel: one increment per element. Tiny GPU work so host dispatch dominates.
    static void IncKernel(Index1D idx, ArrayView<float> data)
    {
        if (idx < data.Length)
            data[idx] += 1.0f;
    }

    public static async Task<int> Run()
    {
        using var context = Context.Create(b => b.Cuda());
        var dev = context.GetCudaDevice(0);
        if (dev == null)
        {
            Console.WriteLine("[cuda-graph-capture] no CUDA device");
            return 1;
        }
        using var acc = dev.CreateCudaAccelerator(context);
        Console.WriteLine($"[cuda-graph-capture] {acc.Name}");

        if (!CudaStream.SupportsGraphCapture)
        {
            Console.WriteLine("[cuda-graph-capture] driver does not expose the graph API - update NVIDIA driver");
            return 1;
        }

        const int L = 1024;      // elements
        const int S = 256;       // kernel launches captured per "step" (stand-in for decode nodes)
        const int R = 200;       // replays / steps

        // Dedicated, CAPTURABLE stream (NOT the default NULL stream) + explicit-stream launcher.
        var stream = (CudaStream)acc.CreateStream();
        var launch = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>>(IncKernel);

        using var data = acc.Allocate1D<float>(L);

        // ---- Warm up (primes JIT + module load + any lazy alloc so capture allocates nothing) ----
        data.MemSetToZero(stream);
        launch(stream, (int)data.Length, data.View);
        stream.Synchronize();

        // ================= CORRECTNESS =================
        // Reset, then capture S launches (recorded, NOT executed -> data stays 0 after capture).
        data.MemSetToZero(stream);
        stream.Synchronize();

        stream.BeginCapture();
        for (int i = 0; i < S; i++)
            launch(stream, (int)data.Length, data.View);
        using var graph = stream.EndCapture();

        // Capture must not have executed anything.
        stream.Synchronize();
        var afterCapture = data.GetAsArray1D();
        bool captureWasInert = afterCapture[0] == 0f && afterCapture[L - 1] == 0f;

        using var exec = graph.Instantiate();
        exec.Upload(stream);

        for (int r = 0; r < R; r++)
            exec.Launch(stream);
        await stream.SynchronizeAsync();

        var result = data.GetAsArray1D();
        float expected = (float)S * R;
        bool correct = true;
        for (int i = 0; i < L; i++)
        {
            if (result[i] != expected) { correct = false; break; }
        }
        Console.WriteLine($"[cuda-graph-capture] capture inert (no exec during capture): {captureWasInert}");
        Console.WriteLine($"[cuda-graph-capture] replay correctness: {(correct ? "PASS" : "FAIL")} " +
                          $"(expected {expected}, got [{result[0]}..{result[L - 1]}], S={S} R={R})");

        // ================= DISPATCH WIN =================
        // Direct: R steps, each S host launches.
        data.MemSetToZero(stream);
        stream.Synchronize();
        var sw = Stopwatch.StartNew();
        for (int r = 0; r < R; r++)
        {
            for (int i = 0; i < S; i++)
                launch(stream, (int)data.Length, data.View);
        }
        stream.Synchronize();
        sw.Stop();
        double directMs = sw.Elapsed.TotalMilliseconds;

        // Graph: R steps, each ONE cuGraphLaunch.
        data.MemSetToZero(stream);
        stream.Synchronize();
        sw.Restart();
        for (int r = 0; r < R; r++)
            exec.Launch(stream);
        stream.Synchronize();
        sw.Stop();
        double graphMs = sw.Elapsed.TotalMilliseconds;

        Console.WriteLine($"[cuda-graph-capture] {S} launches x {R} steps:");
        Console.WriteLine($"    direct host launches : {directMs,8:F2} ms  ({directMs / R:F3} ms/step, {directMs / (R * S) * 1000:F2} us/launch)");
        Console.WriteLine($"    graph replay         : {graphMs,8:F2} ms  ({graphMs / R:F3} ms/step)");
        Console.WriteLine($"    dispatch speedup     : {directMs / graphMs,8:F2}x");

        stream.Dispose();
        return correct && captureWasInert ? 0 : 1;
    }
}
