using System;
using System.IO;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Offline Wasm +inf codegen probe (Tuvok finding #2: +inf silently mis-evaluated on Wasm).
/// Generates the Wasm binary for a kernel that uses float.PositiveInfinity as a literal,
/// in a comparison, and as a min-init sentinel - writes it to disk so we can `wasm2wat
/// --enable-threads` it and read how +inf is emitted. No browser, no dispatch.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- wasm-inf
/// </summary>
internal static class WasmInfProbe
{
    private static void InfProbeKernel(Index1D i, ArrayView<float> input, ArrayView<float> output)
    {
        // (1) literal +inf
        output[0] = float.PositiveInfinity;
        // (2) 1.0 < +inf  -> expect 1
        output[1] = (1.0f < float.PositiveInfinity) ? 1f : 0f;
        // (3) min-init sentinel: best = +inf; take min over input -> expect min(input)
        float best = float.PositiveInfinity;
        for (int k = 0; k < 4; k++)
            if (input[k] < best) best = input[k];
        output[2] = best;
        // (4) input[0] > +inf -> expect 0
        output[3] = (input[0] > float.PositiveInfinity) ? 1f : 0f;
        // (5) input[0] == +inf -> expect 0 for finite input
        output[4] = (input[0] == float.PositiveInfinity) ? 1f : 0f;
    }

    public static Task<int> Run()
    {
        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "_wasm_inf_probe");
        Directory.CreateDirectory(outDir);

        var gen = ShaderCompiler.Generate(
            (Action<Index1D, ArrayView<float>, ArrayView<float>>)InfProbeKernel,
            CapabilityProfiles.WasmDefault);

        var bin = gen.Binary ?? Array.Empty<byte>();
        string path = Path.Combine(outDir, "InfProbeKernel.wasm");
        File.WriteAllBytes(path, bin);
        Console.WriteLine($"[wasm-inf] wrote {bin.Length} bytes -> {path}");

        // Scan the raw bytes for the f32.const +inf encoding (0x43 0x00 0x00 0x80 0x7F)
        // and -inf (0x43 0x00 0x00 0x80 0xFF) so we can see if the literal landed correctly
        // even without wasm2wat.
        int posInf = 0, negInf = 0;
        for (int b = 0; b + 4 < bin.Length; b++)
        {
            if (bin[b] == 0x43 && bin[b + 1] == 0x00 && bin[b + 2] == 0x00 && bin[b + 3] == 0x80 && bin[b + 4] == 0x7F)
                posInf++;
            if (bin[b] == 0x43 && bin[b + 1] == 0x00 && bin[b + 2] == 0x00 && bin[b + 3] == 0x80 && bin[b + 4] == 0xFF)
                negInf++;
        }
        Console.WriteLine($"[wasm-inf] f32.const +inf (0x43 00 00 80 7F) occurrences = {posInf}");
        Console.WriteLine($"[wasm-inf] f32.const -inf (0x43 00 00 80 FF) occurrences = {negInf}");
        Console.WriteLine($"[wasm-inf] disassemble: wasm2wat --enable-threads \"{path}\"");
        return Task.FromResult(0);
    }
}
