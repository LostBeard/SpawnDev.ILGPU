using System.Text;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Emits the in-kernel single-value GroupExtensions.ExclusiveScan exerciser for the
/// pure-Node oversubscription harness (wasm-inkernel-scan-repro). This isolates the EXACT
/// primitive item 2 changes: the in-kernel single-value scan (-> WasmGroupExtensions, the
/// same call RadixSortKernel1 uses for its per-group histogram scan). Mirrors Geordi's
/// WasmTests.OversubInKernelScanKernel, with groupSize hardcoded to 256 (a SpecializedValue
/// only specializes at dispatch, which the offline emit never does) and the unused Index1D
/// param dropped so the dispatcher ABI is 18 system params + one dense view.
///
/// All-1 input => exclusive scan == Group.IdxX, so each group's output segment must read
/// 0..255. 4 scans/thread mirrors RadixSort's unrollFactor=4. ONE dispatch per round.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- inkernel-emit [outDir]
/// </summary>
internal static class WasmInKernelScanEmit
{
    private const int GS = 256;

    private static void InKernelExclusiveScanKernel(ArrayView1D<int, Stride1D.Dense> output)
    {
        // PER-GROUP DISTINCT scan input (pseudorandom hash of the group index). An all-1
        // input CANNOT detect the one-publication-behind store-vanish: the scan shared
        // region is reused across the sequential groups and every group's scan of 1s
        // publishes IDENTICAL values, so a stale slot reads correct by accident (the
        // same lesson as the scan repro's per-tile-distinct input / Captain's -256 tell).
        // Exclusive scan of all-v => Group.IdxX * v(group); a stale publication from
        // group g-1 yields Group.IdxX * v(g-1) - a visible, fingerprinting delta.
        int v = 1 + (int)(((uint)((Grid.IdxX + 1) * unchecked((int)2654435761u)) >> 8) % 251);
        var scanMem = SharedMemory.Allocate<int>(GS * 4);
        for (int j = 0; j < 4; j++)
            scanMem[Group.IdxX + Group.DimX * j] = v;
        Group.Barrier();
        for (int j = 0; j < 4; j++)
            scanMem[Group.IdxX + Group.DimX * j] =
                GroupExtensions.ExclusiveScan<int, AddInt32>(scanMem[Group.IdxX + Group.DimX * j]);
        Group.Barrier();
        int gid = Grid.IdxX * GS + Group.IdxX;
        if (gid < output.Length)
            output[gid] = scanMem[Group.IdxX]; // bucket-0 exclusive scan of all-v == Group.IdxX * v
    }

    public static async Task<int> Run(string outDir)
    {
        Console.WriteLine($"=== Wasm inkernel-scan emit -> {outDir} ===");
        Directory.CreateDirectory(outDir);

        var captured = new List<(string name, byte[] bytes, string info)>();
        WasmBackend.OnKernelCompiled = (name, bytes, info) =>
        {
            captured.Add((name ?? $"kernel{captured.Count}", bytes, info ?? ""));
        };

        var context = Context.Create()
            .EnableAlgorithms()
            .EnableWasmAlgorithms()
            .Wasm()
            .ToContext();

        WasmAccelerator accelerator;
        try { accelerator = await context.CreateWasmAcceleratorAsync(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[inkernel-emit] accelerator create failed: {ex.Message}");
            return 2;
        }
        Console.WriteLine($"[inkernel-emit] {accelerator.Name}, MaxGroupSize={accelerator.MaxNumThreadsPerGroup}");

        // LoadStreamKernel compiles eagerly via LoadKernel (no dispatch needed).
        try
        {
            accelerator.LoadStreamKernel<ArrayView1D<int, Stride1D.Dense>>(InKernelExclusiveScanKernel);
            Console.WriteLine("[inkernel-emit] InKernelExclusiveScanKernel compiled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[inkernel-emit] compile note: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine($"[inkernel-emit] captured {captured.Count} compiled kernel(s):");
        int idx = 0;
        var manifest = new StringBuilder();
        manifest.AppendLine("{");
        manifest.AppendLine($"  \"maxGroupSize\": {accelerator.MaxNumThreadsPerGroup},");
        manifest.AppendLine("  \"kernels\": [");
        foreach (var (name, bytes, info) in captured)
        {
            string safe = $"{idx:00}_{Sanitize(name)}";
            string wasmPath = Path.Combine(outDir, safe + ".wasm");
            File.WriteAllBytes(wasmPath, bytes);
            Console.WriteLine($"  [{idx}] {name}  ({bytes.Length}b)  info: {info}");
            manifest.AppendLine($"    {{ \"index\": {idx}, \"name\": \"{name}\", \"wasm\": \"{safe}.wasm\", \"bytes\": {bytes.Length}, \"info\": \"{info.Replace("\"", "'")}\" }}{(idx < captured.Count - 1 ? "," : "")}");
            idx++;
        }
        manifest.AppendLine("  ]");
        manifest.AppendLine("}");
        File.WriteAllText(Path.Combine(outDir, "manifest.json"), manifest.ToString());
        Console.WriteLine($"[inkernel-emit] wrote {captured.Count} wasm + manifest.json to {outDir}");
        WasmBackend.OnKernelCompiled = null;
        return 0;
    }

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
