using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.ILGPU;
using System.Diagnostics;

/// <summary>
/// INVESTIGATION PROBE (not a PMT test): isolates the CPU-backend per-kernel-launch latency that
/// makes the ML GGUFDecodeKVCache test take 20-145 s for a sub-second workload (and trips the 30 s
/// PMT timeout as an apparent "hang"). Pure ILGPU, no ML, no browser. Launches many tiny
/// AutoGrouped kernels on the CPU accelerator and reports the per-launch latency distribution, so we
/// can see whether launches occasionally stall for 100 ms..10 s (the .NET Barrier DiscontinuousWait
/// exponential-backoff re-poll signature) with the CPU otherwise idle.
///
/// Usage: cpu-launch-lat [iters] [distinctKernels]
///   iters           total kernel launches (default 4000)
///   distinctKernels rotate through N distinct AutoGrouped kernels (default 1) to mimic a real
///                   graph that alternates kernels (forces group-size / participant changes).
/// </summary>
static class CpuLaunchLatencyProbe
{
    const int GBSize = 64;
    // Shared-memory tree reduction with in-kernel Group.Barrier — the exact pattern of the GGUF
    // decode GEMV / softmax reductions that the trivial element-wise probe kernels lack.
    static void GroupBarrierReduce(ArrayView<float> input, ArrayView<float> output)
    {
        int g = Grid.IdxX;
        int tid = Group.IdxX;
        var sh = SharedMemory.Allocate<float>(GBSize);
        sh[tid] = input[g * GBSize + tid];
        Group.Barrier();
        for (int s = GBSize / 2; s > 0; s >>= 1)
        {
            if (tid < s) sh[tid] += sh[tid + s];
            Group.Barrier();
        }
        if (tid == 0) output[g] = sh[0];
    }

    static void K0(Index1D i, ArrayView<float> a, ArrayView<float> b) { a[i] = b[i] + 1f; }
    static void K1(Index1D i, ArrayView<float> a, ArrayView<float> b) { a[i] = b[i] * 2f - 0.5f; }
    static void K2(Index1D i, ArrayView<float> a, ArrayView<float> b) { a[i] = MathF.Sqrt(MathF.Abs(b[i]) + 1f); }

    public static Task<int> Run(string[] args)
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        int iters = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 4000;
        int distinct = args.Length > 2 && int.TryParse(args[2], out var d) ? Math.Clamp(d, 1, 3) : 1;

        var modeStr = Environment.GetEnvironmentVariable("CPU_MODE") ?? "Auto";
        var mode = Enum.TryParse<CPUAcceleratorMode>(modeStr, out var m) ? m : CPUAcceleratorMode.Auto;
        using var context = Context.Create(b => b.CPU());
        using var acc = context.CreateCPUAccelerator(0, mode);
        Console.WriteLine($"[cpu-launch-lat] CPUAcceleratorMode={mode}");
        Console.WriteLine($"[cpu-launch-lat] CPU NumThreads={acc.NumThreads} NumMultiprocessors={acc.NumMultiprocessors} " +
                          $"MaxThreadsPerGroup={acc.MaxNumThreadsPerGroup} WarpSize={acc.WarpSize} cores={Environment.ProcessorCount}");
        Console.WriteLine($"[cpu-launch-lat] iters={iters} distinctKernels={distinct}");

        using var a = acc.Allocate1D<float>(64);
        using var b = acc.Allocate1D<float>(64);

        var kernels = new Action<Index1D, ArrayView<float>, ArrayView<float>>[] { K0, K1, K2 };
        var loaded = new Action<Index1D, ArrayView<float>, ArrayView<float>>[distinct];
        for (int i = 0; i < distinct; i++)
            loaded[i] = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(kernels[i]);

        // Warm up each kernel (compile + first launch out of the measurement).
        for (int i = 0; i < distinct; i++) { loaded[i](64, a.View, b.View); acc.Synchronize(); }

        var times = new double[iters];
        var sw = new Stopwatch();
        var total = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++)
        {
            var k = loaded[i % distinct];
            sw.Restart();
            k(64, a.View, b.View);
            acc.Synchronize();
            times[i] = sw.Elapsed.TotalMilliseconds;
        }
        total.Stop();

        Array.Sort(times);
        double sum = 0; foreach (var t in times) sum += t;
        int o1 = 0, o10 = 0, o100 = 0, o1000 = 0;
        foreach (var t in times) { if (t > 1) o1++; if (t > 10) o10++; if (t > 100) o100++; if (t > 1000) o1000++; }
        double Pct(double p) => times[Math.Clamp((int)(iters * p), 0, iters - 1)];

        Console.WriteLine($"[cpu-launch-lat] SYNC  total={total.Elapsed.TotalSeconds:F2}s mean={sum / iters:F3}ms " +
                          $"p50={Pct(0.50):F3} p90={Pct(0.90):F3} p99={Pct(0.99):F3} max={times[iters - 1]:F2}ms");
        Console.WriteLine($"[cpu-launch-lat] SYNC   launches >1ms={o1}  >10ms={o10}  >100ms={o100}  >1000ms={o1000}");

        // GROUP-BARRIER phase: launch a shared-mem reduction kernel (in-kernel Group.Barrier) many
        // times via explicit KernelConfig(numGroups, GBSize) — the GGUF GEMV/softmax pattern.
        RunGroupBarrierPhase(acc, Math.Min(iters, 40));

        return RunAsyncPhase(acc, a, b, loaded, Math.Min(iters, 2000));
    }

    static void RunGroupBarrierPhase(CPUAccelerator acc, int iters)
    {
        const int numGroups = 256;
        const float expected = GBSize * (GBSize + 1) / 2f; // sum 1..64 = 2080, exact in float
        var host = new float[(long)numGroups * GBSize];
        for (int g = 0; g < numGroups; g++)
            for (int t = 0; t < GBSize; t++)
                host[g * GBSize + t] = t + 1;
        using var input = acc.Allocate1D(host);
        using var output = acc.Allocate1D<float>(numGroups);
        var k = acc.LoadStreamKernel<ArrayView<float>, ArrayView<float>>(GroupBarrierReduce);
        var warm = Stopwatch.StartNew();
        k(new KernelConfig(numGroups, GBSize), input.View, output.View); acc.Synchronize(); // warm
        var warmOut = output.GetAsArray1D();
        int wrong = 0; for (int g = 0; g < numGroups; g++) if (warmOut[g] != expected) wrong++;
        Console.WriteLine($"[cpu-launch-lat] GROUPBAR warm launch = {warm.Elapsed.TotalMilliseconds:F1}ms ({numGroups} groups x{GBSize}, in-kernel Group.Barrier) " +
                          $"CORRECTNESS: {(wrong == 0 ? "PASS (all " + numGroups + " groups == " + expected + ")" : "FAIL (" + wrong + " groups wrong, first=" + warmOut[0] + ")")}");

        var times = new double[iters];
        var sw = new Stopwatch();
        var total = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++)
        {
            sw.Restart();
            k(new KernelConfig(numGroups, GBSize), input.View, output.View);
            acc.Synchronize();
            times[i] = sw.Elapsed.TotalMilliseconds;
            Console.WriteLine($"[cpu-launch-lat] GROUPBAR iter {i} = {times[i]:F1}ms");
        }
        total.Stop();

        Array.Sort(times);
        double sum = 0; foreach (var t in times) sum += t;
        int o1 = 0, o10 = 0, o100 = 0, o1000 = 0;
        foreach (var t in times) { if (t > 1) o1++; if (t > 10) o10++; if (t > 100) o100++; if (t > 1000) o1000++; }
        double Pct(double p) => times[Math.Clamp((int)(iters * p), 0, iters - 1)];
        Console.WriteLine($"[cpu-launch-lat] GROUPBAR total={total.Elapsed.TotalSeconds:F2}s mean={sum / iters:F3}ms " +
                          $"p50={Pct(0.50):F3} p90={Pct(0.90):F3} p99={Pct(0.99):F3} max={times[iters - 1]:F2}ms (iters={iters}, {numGroups} groups x{GBSize})");
        Console.WriteLine($"[cpu-launch-lat] GROUPBAR launches >1ms={o1}  >10ms={o10}  >100ms={o100}  >1000ms={o1000}");
        Console.WriteLine(times[iters - 1] > 100
            ? "[cpu-launch-lat] >>> PATHOLOGICAL in-kernel Group.Barrier stalls (>100ms) — THIS is the GGUFDecodeKVCache slowness."
            : "[cpu-launch-lat] group-barrier launches uniformly fast.");
    }

    // Mimics the ML decode-step pattern that IS slow: per iteration do a kernel launch + an
    // async GPU->GPU CopyFromAsync + await SynchronizeAsync + an async GPU->CPU CopyToHostAsync.
    static async Task<int> RunAsyncPhase(
        CPUAccelerator acc, MemoryBuffer1D<float, Stride1D.Dense> a, MemoryBuffer1D<float, Stride1D.Dense> b,
        Action<Index1D, ArrayView<float>, ArrayView<float>>[] loaded, int iters)
    {
        using var read = acc.Allocate1D<float>(64);
        var times = new double[iters];
        var sw = new Stopwatch();
        var total = Stopwatch.StartNew();
        for (int i = 0; i < iters; i++)
        {
            var k = loaded[i % loaded.Length];
            sw.Restart();
            k(64, a.View, b.View);
            await read.View.CopyFromAsync(a.View);           // GPU->GPU async (the ML CopyFromAsync)
            await acc.SynchronizeAsync();                     // the ML drain
            _ = await read.CopyToHostAsync<float>();          // GPU->CPU async readback
            times[i] = sw.Elapsed.TotalMilliseconds;
        }
        total.Stop();

        Array.Sort(times);
        double sum = 0; foreach (var t in times) sum += t;
        int o1 = 0, o10 = 0, o100 = 0, o1000 = 0;
        foreach (var t in times) { if (t > 1) o1++; if (t > 10) o10++; if (t > 100) o100++; if (t > 1000) o1000++; }
        double Pct(double p) => times[Math.Clamp((int)(iters * p), 0, iters - 1)];

        Console.WriteLine($"[cpu-launch-lat] ASYNC total={total.Elapsed.TotalSeconds:F2}s mean={sum / iters:F3}ms " +
                          $"p50={Pct(0.50):F3} p90={Pct(0.90):F3} p99={Pct(0.99):F3} max={times[iters - 1]:F2}ms (iters={iters})");
        Console.WriteLine($"[cpu-launch-lat] ASYNC  steps >1ms={o1}  >10ms={o10}  >100ms={o100}  >1000ms={o1000}");
        Console.WriteLine(times[iters - 1] > 100
            ? "[cpu-launch-lat] PATHOLOGICAL async-step stalls (>100ms) — matches the GGUFDecodeKVCache slowness; CPU async readback path is the culprit."
            : "[cpu-launch-lat] async steps uniformly fast — slowness is elsewhere (kernel variety / compile churn).");
        return 0;
    }
}
