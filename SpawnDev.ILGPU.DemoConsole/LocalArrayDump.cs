using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Offline (desktop, no browser) Wasm COMPILE + disassemble harness for Tuvok's
/// device-local `new float[]` miscompile (2026-06-21). Compiles two kernels that
/// are identical except for HOW the local array is declared:
///   A) `new float[8]`            - the failing form (Tuvok's GemmDequant M-tile)
///   B) LocalMemory.Allocate<float>(8) - the working ILGPU idiom (LocalMemoryRepro)
/// Both: init 0, accumulate `acc[t] += in*2` in a loop, write `acc[t]` out. No
/// barriers. The Wasm run shows A wrong (reads 0) / B presumably right. This dumps
/// the emitted wasm + verbose alloca logs so we can SEE the addressing difference.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- local-array-dump
/// </summary>
internal static class LocalArrayDump
{
    private const int T = 8;

    // A) new float[] — the failing form
    private static void NewArrayKernel(
        Index1D g, ArrayView<float> input, ArrayView<float> output, ArrayView<int> p)
    {
        int G = p[0], TT = p[1];
        if (g >= G) return;
        var acc = new float[T];
        for (int t = 0; t < TT; t++) acc[t] = 0f;
        for (int t = 0; t < TT; t++) acc[t] += input[g * TT + t] * 2f;
        for (int t = 0; t < TT; t++) output[g * TT + t] = acc[t];
    }

    // B) LocalMemory.Allocate — the working idiom
    private static void LocalMemKernel(
        Index1D g, ArrayView<float> input, ArrayView<float> output, ArrayView<int> p)
    {
        int G = p[0], TT = p[1];
        if (g >= G) return;
        var acc = LocalMemory.Allocate<float>(T);
        for (int t = 0; t < TT; t++) acc[t] = 0f;
        for (int t = 0; t < TT; t++) acc[t] += input[g * TT + t] * 2f;
        for (int t = 0; t < TT; t++) output[g * TT + t] = acc[t];
    }

    private const int GS = 32;

    // C) new float[] read ACROSS barriers (group kernel, tree reduce per element) — Tuvok's exact shape.
    private static void NewArrayBarrierKernel(
        ArrayView<float> input, ArrayView<float> output, ArrayView<int> p)
    {
        int G = p[0], GS2 = p[2];
        int g = Grid.IdxX;
        int tid = Group.IdxX;
        var sh = SharedMemory.Allocate<float>(GS);
        var acc = new float[T];
        for (int t = 0; t < T; t++) acc[t] = 0f;
        if (g < G)
            for (int t = 0; t < T; t++) acc[t] += input[(g * T + t) * GS2 + tid];
        for (int t = 0; t < T; t++)
        {
            sh[tid] = acc[t];
            Group.Barrier();
            for (int stride = GS / 2; stride > 0; stride >>= 1)
            {
                if (tid < stride) sh[tid] += sh[tid + stride];
                Group.Barrier();
            }
            if (tid == 0 && g < G) output[g * T + t] = sh[0];
            Group.Barrier();
        }
    }

    public static async Task<int> Run()
    {
        Console.WriteLine("=== local-array-dump (Tuvok new float[] Wasm miscompile) ===");
        var context = Context.Create().Wasm().ToContext();
        WasmAccelerator acc;
        try { acc = await context.CreateWasmAcceleratorAsync(); }
        catch (Exception ex) { Console.Error.WriteLine($"[dump] no wasm accel: {ex.Message}"); return 2; }

        Console.WriteLine($"[dump] {acc.Name}, MaxGroupSize={acc.MaxNumThreadsPerGroup}");
        var outDir = Path.Combine(AppContext.BaseDirectory, "_localarray_dump");
        Directory.CreateDirectory(outDir);

        DumpOne(acc, "A_NewArray", outDir,
            () => acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>>(NewArrayKernel));
        DumpOne(acc, "B_LocalMem", outDir,
            () => acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>>(LocalMemKernel));
        DumpOne(acc, "C_NewArrayBarrier", outDir,
            () => acc.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<int>>(NewArrayBarrierKernel));

        Console.WriteLine($"[dump] wasm written to {outDir}");

        // Try WebGL GLSL dump (transpile is CPU-side; may work offline).
        try
        {
            var glBuilder = Context.Create();
            await SpawnDev.ILGPU.WebGL.WebGLContextExtensions.WebGL(glBuilder);
            using var glCtx = glBuilder.ToContext();
            var glAcc = SpawnDev.ILGPU.WebGL.WebGLContextExtensions.CreateWebGLAccelerator(glCtx, 0);
            SpawnDev.ILGPU.WebGL.Backend.WebGLBackend.LastGeneratedGLSL = null;
            glAcc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>>(NewArrayKernel);
            var glsl = SpawnDev.ILGPU.WebGL.Backend.WebGLBackend.LastGeneratedGLSL;
            if (glsl != null)
            {
                var p = Path.Combine(outDir, "A_NewArray.glsl");
                File.WriteAllText(p, glsl);
                Console.WriteLine($"[dump] WebGL GLSL written to {p} ({glsl.Length} chars)");
            }
            else Console.WriteLine("[dump] no LastGeneratedGLSL captured");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[dump] WebGL offline dump skipped: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine("=== done ===");
        return 0;
    }

    private static void DumpOne(WasmAccelerator acc, string label, string outDir, Action compile)
    {
        Console.WriteLine($"\n---- {label} ----");
        var prevVerbose = WasmBackend.VerboseLogging;
        var prevOut = Console.Out;
        var sw = new StringWriter();
        WasmBackend.VerboseLogging = true;
        Console.SetOut(sw);
        try { compile(); }
        catch (Exception ex) { Console.SetOut(prevOut); Console.WriteLine($"  [compile error] {ex}"); WasmBackend.VerboseLogging = prevVerbose; return; }
        finally { Console.SetOut(prevOut); WasmBackend.VerboseLogging = prevVerbose; }

        var log = sw.ToString();
        foreach (var raw in log.Split('\n'))
        {
            var l = raw.TrimEnd('\r').Trim();
            if (l.Contains("[Wasm-Alloca]") || l.Contains("[Wasm-Phase]") || l.Contains("[Wasm-SharedMem]")
                || l.StartsWith("Kernel params=") || (l.Contains("scratchPerThread") || l.Contains("phaseMode")))
                Console.WriteLine($"    {l}");
        }

        var bytes = WasmBackend.LastWasmBinary;
        if (bytes != null)
        {
            var path = Path.Combine(outDir, label + ".wasm");
            File.WriteAllBytes(path, bytes);
            Console.WriteLine($"  wrote {path} ({bytes.Length} bytes)");
        }
        else Console.WriteLine("  [no LastWasmBinary]");
    }
}
