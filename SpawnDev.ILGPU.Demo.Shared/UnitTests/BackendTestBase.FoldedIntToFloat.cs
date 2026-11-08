using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Integer -> float conversion of a value ILGPU constant-folds. IRBuilder.CreateConvert folded an Int8/16/32 constant
    // to a float through UInt64Value / Int64Value, but the raw value of an Int32 constant is its sign-extended long: the
    // uint 3000000000 (raw -1294967296) became (ulong)-1294967296 = 1.8446744E+19 under conv.r.un. Loaded or non-foldable
    // uints were right. Found by Lore through SpawnGen's GLSL-to-ILGPU compiler (ComfyUI's Film Grain shader hashes with
    // uint and divides by float(0xffffffffu)), on OpenCL, 2026-11-08. Every IR backend folds the same way; the CPU
    // accelerator runs the IL and was never affected.
    public abstract partial class BackendTestBase
    {
        const int FoldedIntToFloatCount = 14;

        static readonly string[] FoldedIntToFloatNames =
        {
            "(float)3000000000u", "(float)0xffffffffu", "(float)0x80000000u", "(float)2891336453u / (float)0xffffffffu",
            "(float)(double)3000000000u", "(float)-5", "(float)int.MinValue", "(float)(byte)200", "(float)(sbyte)-100",
            "(float)(ushort)60000", "(float)(short)-30000", "(float)0xF000000000000000ul", "(float)-3000000000L",
            "(float)(uint)(i + 7) * 747796405u + 2891336453u (not foldable)",
        };

        // Each case adds a thread-index term multiplied by zero, which ILGPU folds away, leaving a constant the IR converts.
        // The host runs this same method, so the expected values are .NET's own conversions.
        static float FoldedIntToFloatCase(int i, int k) => k switch
        {
            0 => 3000000000u + (uint)i * 0u,
            1 => 0xffffffffu + (uint)i * 0u,
            2 => 0x80000000u + (uint)i * 0u,
            3 => (2891336453u + (uint)i * 0u) / (float)(0xffffffffu + (uint)i * 0u),
            4 => (float)(double)(3000000000u + (uint)i * 0u),
            5 => -5 + i * 0,
            6 => int.MinValue + i * 0,
            7 => (byte)(200 + i * 0),
            8 => (sbyte)(-100 + i * 0),
            9 => (ushort)(60000 + i * 0),
            10 => (short)(-30000 + i * 0),
            11 => 0xF000000000000000ul + (ulong)i * 0ul,
            12 => -3000000000L + i * 0L,
            _ => (uint)(i + 7) * 747796405u + 2891336453u,
        };

        // One output per thread (WebGL captures one).
        static void FoldedIntToFloatKernel(Index1D i, ArrayView<float> output) =>
            output[i] = FoldedIntToFloatCase(i, i);

        [TestMethod]
        public async Task Conversion_FoldedIntegerConstantToFloat_MatchesHost() => await RunTest(async accelerator =>
        {
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(FoldedIntToFloatKernel);
            using var output = accelerator.Allocate1D<float>(FoldedIntToFloatCount);
            kernel((Index1D)FoldedIntToFloatCount, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<float>();

            var errors = new System.Collections.Generic.List<string>();
            for (int k = 0; k < FoldedIntToFloatCount; k++)
            {
                float expected = FoldedIntToFloatCase(k, k);
                if (BitConverter.SingleToInt32Bits(got[k]) != BitConverter.SingleToInt32Bits(expected))
                    errors.Add($"[{k}] {FoldedIntToFloatNames[k]}: expected {expected:R}, got {got[k]:R}");
            }
            if (errors.Count > 0)
                throw new Exception($"{errors.Count} of {FoldedIntToFloatCount} folded int->float conversions wrong: {string.Join("; ", errors)}");
        });
    }
}
