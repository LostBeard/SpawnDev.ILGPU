using System;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-26 (SpawnDev.Codecs, found with Geordi): Chrome's GPU process grew 600 MB/s to 16 GB while creating the
    // shader MODULE of Vp8KeyframeDecodeStepKernel (66k lines) and was killed ("A valid external Instance reference no
    // longer exists"); the VP8/VP9 entropy kernels died the same way. Tint's module step costs about (function-scope vars
    // in scope) x (branches + loops), and nothing for a `let`; the WGSL generator hoisted EVERY value to a function-scope
    // var. Now a value assigned once per scope is a `let` at its assignment (WGSLKernelFunctionGenerator
    // .ConvertSingleAssignmentVarsToLet): this kernel had 168 vars and 11 lets on 5.4.5, and has 3 vars and 176 lets.
    public abstract partial class BackendTestBase
    {
        private const int WLV_Steps = 64;

        // 64 data-dependent branch steps per thread (unrolled by ILGPU into many values and branches).
        private static void WgslLetValuesBranchStepsKernel(Index1D i, ArrayView<int> data, ArrayView<int> output)
        {
            int acc = i;
            for (int s = 0; s < WLV_Steps; s++)
            {
                int d = data[i * WLV_Steps + s];
                if (d > 0) acc = acc * 3 + d;
                else acc ^= d * 7 + s;
                if ((acc & 15) == 3) acc += s;
            }
            output[i] = acc;
        }

        [TestMethod]
        public async Task WgslLetValues_BranchySteps_MatchOracle_AndWgslUsesLets() => await RunTest(async accelerator =>
        {
            // The shape: generated WGSL keeps a var only for values assigned in several merging places.
            if (accelerator.AcceleratorType == AcceleratorType.WebGPU)
            {
                var method = typeof(BackendTestBase).GetMethod(nameof(WgslLetValuesBranchStepsKernel),
                    BindingFlags.NonPublic | BindingFlags.Static);
                var wgsl = ShaderCompiler.Generate(method!, CapabilityProfiles.WebGPUBaseline).Source ?? "";
                int vars = Regex.Matches(wgsl, @"(?m)^\s*var v_").Count;
                int lets = Regex.Matches(wgsl, @"(?m)^\s*let v_").Count;
                if (lets < 100 || vars * 10 > lets)
                    throw new Exception($"WgslLetValues: generated WGSL has {vars} `var v_` and {lets} `let v_` declarations; single-assignment values must be lets (5.4.5: 168 vars, 11 lets).");
            }

            // The results: every backend against a CPU oracle (the WebGPU lane runs the converted WGSL).
            const int N = 4096;
            var data = new int[N * WLV_Steps];
            for (int k = 0; k < data.Length; k++) data[k] = (int)((k * 2654435761u) >> 20) % 201 - 100;
            using var dataBuf = accelerator.Allocate1D(data);
            using var outBuf = accelerator.Allocate1D(new int[N]);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(WgslLetValuesBranchStepsKernel);
            kernel(N, dataBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int acc = i;
                for (int s = 0; s < WLV_Steps; s++)
                {
                    int d = data[i * WLV_Steps + s];
                    if (d > 0) acc = acc * 3 + d;
                    else acc ^= d * 7 + s;
                    if ((acc & 15) == 3) acc += s;
                }
                if (got[i] != acc)
                    throw new Exception($"WgslLetValues: thread {i}: expected {acc}, got {got[i]}.");
            }
        });
    }
}
