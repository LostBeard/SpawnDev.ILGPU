using System.Text;
using System.Text.RegularExpressions;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Offline (desktop, no browser) Wasm COMPILE harness for the H8 shared-memory
/// alloca audit. WasmAccelerator.Create wraps the BlazorJSRuntime.JS lookup in a
/// try/catch (defaults to 4 cores when JS is absent), and CreateRadixSort* compiles
/// its kernels eagerly via LoadKernel BEFORE any dispatch - so the IL->wasm compile
/// path runs fully on desktop. We never dispatch (that needs workers); we only
/// compile and read back the emitted shared-memory alloca table that
/// WasmKernelFunctionGenerator.SetupSharedAllocations / GenerateCode(Alloca) log
/// under WasmBackend.VerboseLogging.
///
/// Goal: determine whether the barrier kernel RadixSortKernel1 (which inlines
/// GroupExtensions.ExclusiveScan UnrollFactor times, each allocating int[2048]
/// shared scratch) emits DISTINCT shared offsets for every alloca, or whether the
/// GenerateCode(Alloca) type+size FALLBACK collapses any two distinct shared allocas
/// onto one offset (== an overlap, the H8 prime suspect).
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- wasm-dump
/// </summary>
internal static class WasmCompileDump
{
    public static async Task<int> Run()
    {
        Console.WriteLine("=== Wasm offline compile dump (H8 shared-alloca audit) ===");

        var context = Context.Create()
            .EnableAlgorithms()
            .EnableWasmAlgorithms()
            .Wasm()
            .ToContext();

        WasmAccelerator accelerator;
        try
        {
            accelerator = await context.CreateWasmAcceleratorAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[wasm-dump] Could not create Wasm accelerator on desktop: {ex.Message}");
            Console.Error.WriteLine("[wasm-dump] (If this is a JS-required failure, the compile path cannot run offline.)");
            return 2;
        }

        Console.WriteLine($"[wasm-dump] Accelerator: {accelerator.Name}, MaxGroupSize={accelerator.MaxNumThreadsPerGroup}");

        // Each entry compiles one RadixSort variant. The descending int/float keys-only
        // and pairs variants are the exact failing canaries (RadixSortDescending*).
        DumpCompile("RadixSort<int, DescendingInt32> (keys-only)",
            () => accelerator.CreateRadixSort<int, Stride1D.Dense, DescendingInt32>());
        DumpCompile("RadixSort<float, DescendingFloat> (keys-only)",
            () => accelerator.CreateRadixSort<float, Stride1D.Dense, DescendingFloat>());
        DumpCompile("RadixSortPairs<float,int, DescendingFloat>",
            () => accelerator.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense, DescendingFloat>());
        DumpCompile("RadixSort<int, AscendingInt32> (keys-only)",
            () => accelerator.CreateRadixSort<int, Stride1D.Dense, AscendingInt32>());

        Console.WriteLine("=== wasm-dump done ===");
        return 0;
    }

    private static void DumpCompile(string label, Action compile)
    {
        Console.WriteLine();
        Console.WriteLine($"---- compiling: {label} ----");

        var prevVerbose = WasmBackend.VerboseLogging;
        var prevOut = Console.Out;
        var sw = new StringWriter();
        WasmBackend.VerboseLogging = true;
        Console.SetOut(sw);
        Exception? err = null;
        try
        {
            compile();
        }
        catch (Exception ex)
        {
            err = ex;
        }
        finally
        {
            Console.SetOut(prevOut);
            WasmBackend.VerboseLogging = prevVerbose;
        }

        if (err != null)
            Console.WriteLine($"  [compile error] {err.GetType().Name}: {err.Message}");

        var log = sw.ToString();
        var lines = log.Split('\n');

        // Per-kernel info lines (WasmBackend logs one "Kernel params=... sharedMem=... barriers=..."
        // per compiled kernel). Lets us attribute shared allocas to specific kernels and count how
        // many kernels CreateRadixSort actually compiled.
        var kernelInfoLines = new List<string>();
        foreach (var raw in lines)
        {
            var l = raw.TrimEnd('\r').Trim();
            if (l.StartsWith("Kernel params=") || l.Contains("sharedMem=") && l.Contains("barriers="))
                kernelInfoLines.Add(l);
        }
        Console.WriteLine($"  compiled kernels (info lines): {kernelInfoLines.Count}");
        foreach (var l in kernelInfoLines)
            Console.WriteLine($"    [k] {l}");

        // Group the shared-mem alloca lines per kernel/helper compile and detect overlaps.
        // Lines look like:
        //   [Wasm-SharedMem] Static array alloca v_123: offset=0, size=8192, arrayLen=2048, elemSize=4
        //   [Wasm-SharedMem] Scalar alloca v_45: offset=8192, size=4
        //   [Wasm-SharedMem] Alloca v_99: fallback match to v_123 (type=Int32, size=2048, addrSpace=Shared)
        var sharedLines = new List<string>();
        var fallbackLines = new List<string>();
        foreach (var raw in lines)
        {
            var l = raw.TrimEnd('\r');
            if (l.Contains("[Wasm-SharedMem]"))
            {
                if (l.Contains("fallback match"))
                    fallbackLines.Add(l.Trim());
                else
                    sharedLines.Add(l.Trim());
            }
        }

        Console.WriteLine($"  shared-alloca registration lines: {sharedLines.Count}");
        foreach (var l in sharedLines)
            Console.WriteLine($"    {l}");

        if (fallbackLines.Count > 0)
        {
            Console.WriteLine($"  *** FALLBACK alloca matches: {fallbackLines.Count} (each = a shared alloca that MISSED its primary key and was aliased by type+size) ***");
            foreach (var l in fallbackLines)
                Console.WriteLine($"    !! {l}");
        }
        else
        {
            Console.WriteLine("  no fallback alloca matches (all shared allocas resolved by primary key)");
        }

        // Parse (offset,size) pairs from the registration lines and report overlaps.
        var rx = new Regex(@"offset=(\d+).*?size=(\d+)");
        var segs = new List<(int off, int size, string line)>();
        foreach (var l in sharedLines)
        {
            var m = rx.Match(l);
            if (m.Success)
                segs.Add((int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), l));
        }
        segs.Sort((a, b) => a.off.CompareTo(b.off));
        bool anyOverlap = false;
        for (int i = 1; i < segs.Count; i++)
        {
            if (segs[i].off < segs[i - 1].off + segs[i - 1].size)
            {
                anyOverlap = true;
                Console.WriteLine($"  *** OVERLAP: [{segs[i - 1].off},+{segs[i - 1].size}) intersects [{segs[i].off},+{segs[i].size}) ***");
                Console.WriteLine($"      A: {segs[i - 1].line}");
                Console.WriteLine($"      B: {segs[i].line}");
            }
        }
        if (!anyOverlap && segs.Count > 0)
            Console.WriteLine("  no offset overlaps among registered shared allocas");
    }
}
