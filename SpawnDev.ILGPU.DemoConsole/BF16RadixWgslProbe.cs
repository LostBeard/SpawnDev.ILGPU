using System;
using System.IO;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.ILGPU;

/// <summary>
/// Dumps the WGSL for a bf16 ExtractRadixBits kernel under the EMULATED-f16 WebGPU profile
/// (CapabilityProfiles.WebGPUBaseline) - the exact path the failing bf16 radix sort uses on a
/// shader-f16 device (where Half takes the native path instead). No device/browser.
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- bf16-radix-emit
/// </summary>
internal static class BF16RadixWgslProbe
{
    // Replicates the radix kernel's shape: a STRUCT param bundling views (body-struct), with
    // the bf16 keys as a view FIELD - the param0_f0 shape from the failing WGSL.
    private struct BF16Bundle
    {
        public ArrayView<BFloat16> Keys;
        public ArrayView<int> Flags;
    }

    private static void BF16ExtractKernel(
        Index1D i, ArrayView<BFloat16> keys, ArrayView<int> flags, int bit)
    {
        DescendingBFloat16 op = default;
        flags[i] = op.ExtractRadixBits(keys[i], bit, 1);
    }

    private static void BF16BodyStructKernel(Index1D i, BF16Bundle b, int bit)
    {
        DescendingBFloat16 op = default;
        b.Flags[i] = op.ExtractRadixBits(b.Keys[i], bit, 1);
    }

    public static Task<int> Run()
    {
        var kernel = (Action<Index1D, BF16Bundle, int>)BF16BodyStructKernel;
        var outPath = @"D:\users\tj\Projects\SpawnDev.ILGPU\bf16-radix.wgsl";

        try
        {
            var result = ShaderCompiler.Generate(kernel, CapabilityProfiles.WebGPUBaseline);
            var wgsl = result.Source ?? "";
            File.WriteAllText(outPath, wgsl);
            Console.WriteLine($"[bf16-radix-emit] HasErrors={result.HasErrors} len={wgsl.Length} -> {outPath}");
            Console.WriteLine($"[bf16-radix-emit] defines _f32_to_bf16: {wgsl.Contains("fn _f32_to_bf16")}");
            Console.WriteLine($"[bf16-radix-emit] calls _f32_to_bf16:   {wgsl.Contains("_f32_to_bf16(")}");
            Console.WriteLine($"[bf16-radix-emit] defines _bf16_to_f32: {wgsl.Contains("fn _bf16_to_f32")}");
            Console.WriteLine($"[bf16-radix-emit] calls _bf16_to_f32:   {wgsl.Contains("_bf16_to_f32(")}");
            foreach (var diag in result.Diagnostics)
                Console.WriteLine($"[bf16-radix-emit] diag: {diag}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[bf16-radix-emit] EXCEPTION: {ex}");
            return Task.FromResult(1);
        }
        return Task.FromResult(0);
    }
}
