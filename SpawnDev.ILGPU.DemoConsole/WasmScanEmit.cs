using System.Text;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Emits the REAL generated inclusive-scan kernel(s) to disk for the pure-Node repro
/// harness (wasm-scan-repro). Compiles `accelerator.CreateScan&lt;int,...,AddInt32&gt;(Inclusive)`
/// on a desktop Wasm accelerator (compile works offline; only dispatch needs workers) and
/// dumps every compiled kernel's wasm binary + parsed metadata. This lets the Node harness
/// run the ACTUAL kernel (not a hand-written model) so we reproduce the real multi-tile
/// scan/broadcast race in a terminal — no Chromium, no FO76, controllable workers.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- scan-emit [outDir]
/// </summary>
internal static class WasmScanEmit
{
    public static async Task<int> Run(string outDir, bool radix = false)
    {
        Console.WriteLine($"=== Wasm {(radix ? "radix" : "scan")}-kernel emit -> {outDir} ===");
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
            Console.Error.WriteLine($"[scan-emit] accelerator create failed: {ex.Message}");
            return 2;
        }
        Console.WriteLine($"[scan-emit] {accelerator.Name}, MaxGroupSize={accelerator.MaxNumThreadsPerGroup}");

        // Compile the real inclusive scan. CreateScan eagerly LoadKernel-compiles (no dispatch).
        try
        {
            if (radix)
            {
                var rs = accelerator.CreateRadixSort<int, Stride1D.Dense, ILGPU.Algorithms.RadixSortOperations.AscendingInt32>();
                Console.WriteLine("[scan-emit] CreateRadixSort<int,Dense,AscendingInt32> compiled (pass1/scan/pass2).");
            }
            else
            {
                var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Inclusive);
                Console.WriteLine("[scan-emit] CreateScan<int,Dense,Dense,AddInt32>(Inclusive) compiled.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[scan-emit] compile note: {ex.GetType().Name}: {ex.Message}");
        }

        Console.WriteLine($"[scan-emit] captured {captured.Count} compiled kernel(s):");
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
        Console.WriteLine($"[scan-emit] wrote {captured.Count} wasm + manifest.json to {outDir}");
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
