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

    // Reads a per-step value from a STABLE-pointer device buffer. This is the exact
    // decode mechanism: the captured kernel reads token-id / KV-pos from a device buffer
    // whose address is fixed at capture time; the host mutates that buffer's CONTENTS
    // between replays (outside capture). Proves the recommended update path is sound.
    static void AddParamKernel(Index1D idx, ArrayView<float> data, ArrayView<float> param)
    {
        if (idx < data.Length)
            data[idx] += param[0];
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

        // ============== PER-REPLAY UPDATE (the decode mechanism) ==============
        // Capture a kernel that reads from a stable-pointer device buffer, then mutate
        // that buffer's CONTENTS between replays and confirm each replay sees the new
        // value. This is exactly how Tuvok varies token-id / KV-pos per token.
        bool updateWorks = await ProvePerReplayUpdate(acc);

        Console.WriteLine($"[cuda-graph-capture] per-replay device-buffer update: {(updateWorks ? "PASS" : "FAIL")}");

        stream.Dispose();

        // ============== ROUTING via WithDefaultStream (.ML's can't-miss path) ==============
        // .ML launches through *StreamKernel (bind accelerator.DefaultStream at launch
        // time). Swapping DefaultStream to a capturable stream must reroute those launches
        // so they're captured - no per-call-site change. Proves Tuvok's §2 ask.
        bool routingWorks = await ProveDefaultStreamRouting(acc);
        Console.WriteLine($"[cuda-graph-capture] WithDefaultStream routing (*StreamKernel captured): {(routingWorks ? "PASS" : "FAIL")}");

        return correct && captureWasInert && updateWorks && routingWorks ? 0 : 1;
    }

    private static async Task<bool> ProvePerReplayUpdate(CudaAccelerator acc)
    {
        const int L = 256;
        var stream = (CudaStream)acc.CreateStream();
        var launch = acc.LoadAutoGroupedKernel<Index1D, ArrayView<float>, ArrayView<float>>(AddParamKernel);
        using var data = acc.Allocate1D<float>(L);
        using var param = acc.Allocate1D<float>(1);   // STABLE pointer; only contents change

        // Warm up + capture ONE AddParam launch (records the param buffer's address).
        param.CopyFromCPU(new[] { 0f });
        data.MemSetToZero(stream);
        launch(stream, (int)data.Length, data.View, param.View);
        stream.Synchronize();

        data.MemSetToZero(stream);
        stream.Synchronize();
        stream.BeginCapture();
        launch(stream, (int)data.Length, data.View, param.View);
        using var graph = stream.EndCapture();
        using var exec = graph.Instantiate();

        // REALISTIC decode pattern: update the stable-pointer param buffer, launch the
        // graph, sync (decode syncs every token to sample), repeat. Sync each step makes
        // the host->device update unambiguously ordered before the replay that reads it.
        float[] perStep = { 1f, 10f, 100f };
        foreach (var v in perStep)
        {
            param.CopyFromCPU(stream, new[] { v });
            exec.Launch(stream);
            await stream.SynchronizeAsync();   // mirrors per-token sample point
        }

        var result = data.GetAsArray1D();
        stream.Dispose();
        // 1 + 10 + 100 = 111 on every element if each replay read the updated buffer.
        Console.WriteLine($"[cuda-graph-capture]   update probe (sync/step): data[0]={result[0]} (expect 111: 1+10+100)");
        for (int i = 0; i < L; i++)
            if (result[i] != 111f) return false;
        return true;
    }

    private static async Task<bool> ProveDefaultStreamRouting(CudaAccelerator acc)
    {
        const int L = 256, S = 64, R = 50;
        var capStream = (CudaStream)acc.CreateStream();
        // The *StreamKernel launcher (binds accelerator.DefaultStream at launch time).
        var streamLaunch = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(IncKernel);
        using var data = acc.Allocate1D<float>(L);

        bool ok;
        // Swap DefaultStream -> capStream for the whole capture; the *StreamKernel launches
        // now hit capStream and get recorded instead of running on the (un-capturable) NULL
        // default stream.
        using (acc.WithDefaultStream(capStream))
        {
            // warm up + reset on the capturable stream
            data.MemSetToZero(capStream);
            streamLaunch((int)data.Length, data.View);
            capStream.Synchronize();
            data.MemSetToZero(capStream);
            capStream.Synchronize();

            capStream.BeginCapture();
            for (int i = 0; i < S; i++)
                streamLaunch((int)data.Length, data.View);   // no explicit stream - uses DefaultStream==capStream
            using var graph = capStream.EndCapture();

            // Capture status must have been active during; the *StreamKernels were captured
            // (if they'd hit the NULL stream, EndCapture would have thrown / captured nothing).
            using var exec = graph.Instantiate();
            for (int r = 0; r < R; r++)
                exec.Launch(capStream);
            await capStream.SynchronizeAsync();

            var res = data.GetAsArray1D();
            float expected = (float)S * R;
            ok = res[0] == expected && res[L - 1] == expected;
            Console.WriteLine($"[cuda-graph-capture]   routing probe: data[0]={res[0]} (expect {expected}: S={S}*R={R})");
        }
        // DefaultStream restored to the NULL stream here.
        capStream.Dispose();
        return ok;
    }
}
