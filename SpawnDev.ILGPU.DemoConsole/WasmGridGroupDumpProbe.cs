using System;
using System.IO;
using System.Threading.Tasks;
using ILGPU;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Offline dump of the emitted Wasm for GridGroupDimensionKernel (the Wasm-only deterministic failure:
/// explicit KernelConfig(1,64), Grid.GlobalIndex.X reads 0 for thread 1). Compiles the EXACT kernel
/// through the offline Wasm codegen path and writes the .wasm so wasm2wat can show the Grid.GlobalIndex
/// formula (Grid.Idx * Group.Dim + Group.Idx) against the system-param locals - to verify the codegen
/// matches the intended decomposition or expose where it diverges. No browser, no dispatch.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- wasm-gridgroup-dump
/// </summary>
internal static class WasmGridGroupDumpProbe
{
    // EXACT replica of BackendTestBase.GridGroupDimensionKernel.
    private static void GridGroupDimensionKernel(Index1D index, ArrayView<int> output)
    {
        int globalId = Grid.GlobalIndex.X;
        int localId = Group.IdxX;
        int groupId = Grid.IdxX;
        int groupDim = Group.DimX;
        int baseIdx = globalId * 4;
        output[baseIdx] = globalId;
        output[baseIdx + 1] = localId;
        output[baseIdx + 2] = groupId;
        output[baseIdx + 3] = groupDim;
    }

    public static Task<int> Run(string[] args)
    {
        string dir = args.Length > 1 ? args[1]
            : Path.Combine(Directory.GetCurrentDirectory(), "_wasm_gridgroup_dump");
        Directory.CreateDirectory(dir);

        Console.WriteLine("=== GridGroupDimensionKernel Wasm dump (ForceSimd to mirror the browser) ===");
        var prevScalar = WasmBackend.ForceScalar; var prevSimd = WasmBackend.ForceSimd;
        WasmBackend.ForceScalar = false; WasmBackend.ForceSimd = true; // browser has RuntimeSupportsWasmSimd=true
        try
        {
            WasmBackend.LastSimdAnalysis = default;
            var gen = ShaderCompiler.Generate(
                (Action<Index1D, ArrayView<int>>)GridGroupDimensionKernel,
                CapabilityProfiles.WasmDefault);
            var bin = gen.Binary ?? WasmBackend.LastWasmBinary;
            Console.WriteLine($"SimdAnalysis = {WasmBackend.LastSimdAnalysis}");
            if (bin != null)
            {
                bool hasSimd = false;
                var nm = System.Text.Encoding.ASCII.GetBytes("kernel_simd");
                for (int i = 0; i + nm.Length + 1 <= bin.Length && !hasSimd; i++)
                { if (bin[i] != nm.Length) continue; int j = 0; for (; j < nm.Length; j++) if (bin[i + 1 + j] != nm[j]) break; if (j == nm.Length) hasSimd = true; }
                Console.WriteLine($"*** kernel_simd emitted for this INT kernel? {hasSimd}  (MUST be false - int stores must bail) ***");
            }
            if (bin == null || bin.Length == 0) { Console.WriteLine("NO BINARY"); return Task.FromResult(2); }
            var path = Path.Combine(dir, "gridgroup.wasm");
            File.WriteAllBytes(path, bin);
            Console.WriteLine($"binary {bin.Length}b -> {path}");
            Console.WriteLine($"disasm: wasm2wat --enable-threads \"{path}\"");

            // The copy-OUT optimization skips copying back buffers NOT in WrittenParamIndices. If the
            // `output` param is missing here, the kernel's scatter writes (output[globalId*4]=...) never
            // reach the host -> the test reads stale zeros -> "GlobalId failed at 1. Got 0".
            Console.WriteLine($"WrittenParamIndices = [{string.Join(",", WasmBackend.LastWrittenParamIndices)}]");
            Console.WriteLine($"StoreTargetTrace ({WasmBackend.LastStoreTargetTrace.Count}):");
            foreach (var t in WasmBackend.LastStoreTargetTrace)
                Console.WriteLine($"   {t}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"COMPILE ERROR: {ex.GetType().Name}: {ex.Message}");
            return Task.FromResult(1);
        }
        finally { WasmBackend.ForceScalar = prevScalar; WasmBackend.ForceSimd = prevSimd; }
        return Task.FromResult(0);
    }
}
