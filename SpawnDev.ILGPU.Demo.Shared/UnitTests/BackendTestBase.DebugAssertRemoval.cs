using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;
using System.Text.RegularExpressions;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Every ArrayView indexer and IntLength carries a Trace.Assert (index in range, length fits an int). The browser
    // backends cannot implement an assertion, so their code generators emit nothing for DebugAssertOperation - but the
    // values that only feed the assertion stayed alive in IR (the assert is a root), so each view access emitted a dead
    // chain of emulated-i64 compares on WebGPU: TRELLIS.2's tiled attention shader had 408 of its 410 i64 calls and 32 %
    // of all assignments dead (gemm: 756 of 762, 47 %) - 420 KB / 607 KB of WGSL. RemoveDebugOperations drops the
    // assertion before code generation, so the chain dies in the dead code elimination that follows.
    public abstract partial class BackendTestBase
    {
        // 32 loads and 8 stores through views, like a tiled kernel: every one used to emit an assert chain.
        static void DebugAssertViewKernel(Index1D i, ArrayView1D<float, Stride1D.Dense> a, ArrayView1D<float, Stride1D.Dense> b, ArrayView1D<float, Stride1D.Dense> o, int n)
        {
            int ii = i;
            bool ok = ii < n;
            float acc = 0f;
            for (int k = 0; k < 32; k++)
                acc += ok ? a[ii + k] * b[k] : 0f;
            for (int k = 0; k < 8; k++)
                o[i * 8 + k] = acc + k;
        }

        const int DebugAssertN = 16;

        [TestMethod]
        public async Task DebugAssertRemoval_ViewKernel_CorrectOutput() => await RunTest(async accelerator =>
        {
            var a = new float[DebugAssertN + 32];
            var b = new float[32];
            for (int k = 0; k < a.Length; k++) a[k] = (k % 7) * 0.5f + 1f;
            for (int k = 0; k < b.Length; k++) b[k] = (k % 5) * 0.25f - 0.5f;
            using var aBuf = accelerator.Allocate1D(a);
            using var bBuf = accelerator.Allocate1D(b);
            using var oBuf = accelerator.Allocate1D<float>(DebugAssertN * 8);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int>(DebugAssertViewKernel)(DebugAssertN, aBuf.View, bBuf.View, oBuf.View, DebugAssertN);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<float>();
            for (int i = 0; i < DebugAssertN; i++)
            {
                float acc = 0f;
                for (int k = 0; k < 32; k++) acc += a[i + k] * b[k];
                for (int k = 0; k < 8; k++)
                    if (MathF.Abs(got[i * 8 + k] - (acc + k)) > 1e-3f)
                        throw new Exception($"o[{i * 8 + k}] = {got[i * 8 + k]}, expected {acc + k}");
            }
        });

        // The shader must carry no emulated 64-bit work for plain 32-bit view indexing. Red-checked 2026-10-21 with the
        // pass removed: the assert chains put i64_ge / i64_le / i64_to_i32 calls into main() for every access.
        [TestMethod]
        public async Task DebugAssertRemoval_ViewKernel_WgslHasNoI64Chains() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException("WebGPU-only: inspects the generated WGSL.");

            string? wgsl = null;
            Action<string, string, WGSLEntry> handler = (name, source, info) =>
            {
                if (name.Contains(nameof(DebugAssertViewKernel), StringComparison.Ordinal)) wgsl = source;
            };
            // A cache hit (the accelerator's kernel cache or the process-wide ShaderArtifactCache) never reaches the code generator.
            accelerator.ClearCache(ClearCacheMode.Everything);
            bool artifactCacheWas = SpawnDev.ILGPU.ShaderArtifactCache.Enabled;
            SpawnDev.ILGPU.ShaderArtifactCache.Enabled = false;
            WebGPUBackend.OnShaderCompiled += handler;
            try
            {
                _ = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int>(DebugAssertViewKernel);
            }
            finally
            {
                WebGPUBackend.OnShaderCompiled -= handler;
                SpawnDev.ILGPU.ShaderArtifactCache.Enabled = artifactCacheWas;
            }
            if (wgsl == null) throw new Exception($"Did not capture {nameof(DebugAssertViewKernel)}'s WGSL via OnShaderCompiled.");

            // Calls of the emulation helpers in the kernel body (the emulation library above it calls itself).
            int mainAt = wgsl.IndexOf("fn main(", StringComparison.Ordinal);
            if (mainAt < 0) throw new Exception($"{nameof(DebugAssertViewKernel)}: no main() in the WGSL.");
            int calls = 0;
            foreach (Match m in Regex.Matches(wgsl.Substring(mainAt), @"\bi64_(ge|le|lt|gt)\("))
                calls++;
            if (calls != 0)
                throw new Exception($"{nameof(DebugAssertViewKernel)}: WGSL calls the i64 emulation x{calls} for 32-bit view indexing ({wgsl.Length} chars): debug-assert chains are still emitted.");
            await Task.CompletedTask;
        });

        // SpawnDev.ILGPU.ML's CenterCropPadImpl: a loop that breaks out when the coordinate is out of range, then a select
        // between a view read and 0 - the shape that failed DXC validation on WebGPU once RemoveDebugOperations ran
        // (WebGPUTests.Op_CenterCropPad_CopiesData, found by Tuvok 2026-10-21).
        static void DebugAssertLoopBreakKernel(Index1D idx, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int rank = (int)paramsArr[0];
            int tmp = idx;
            bool inBounds = true;
            int srcIdx = 0;
            for (int d = rank - 1; d >= 0; d--)
            {
                int inSize = (int)paramsArr[1 + d];
                int outSize = (int)paramsArr[1 + rank + d];
                int inStride = (int)paramsArr[1 + 2 * rank + d];
                int outCoord = tmp % outSize;
                tmp /= outSize;
                int inCoord = outCoord - (outSize - inSize) / 2;
                if (inCoord < 0 || inCoord >= inSize) { inBounds = false; break; }
                srcIdx += inCoord * inStride;
            }
            output[idx] = inBounds ? input[srcIdx] : 0f;
        }

        [TestMethod]
        public async Task DebugAssertRemoval_LoopWithBreak_CenterCropPad_CorrectOutput() => await RunTest(async accelerator =>
        {
            // input 2x3 centered into 4x5 (pad) and a second case cropping 4x5 to 2x3: both directions of the centre offset
            foreach (var (inH, inW, outH, outW) in new[] { (2, 3, 4, 5), (4, 5, 2, 3), (2, 2, 2, 2) })
            {
                var input = new float[inH * inW];
                for (int i = 0; i < input.Length; i++) input[i] = i + 1;
                var prm = new float[] { 2, inH, inW, outH, outW, inW, 1 };
                var expected = new float[outH * outW];
                for (int oy = 0; oy < outH; oy++)
                    for (int ox = 0; ox < outW; ox++)
                    {
                        int iy = oy - (outH - inH) / 2, ix = ox - (outW - inW) / 2;
                        expected[oy * outW + ox] = iy >= 0 && iy < inH && ix >= 0 && ix < inW ? input[iy * inW + ix] : 0f;
                    }
                using var inBuf = accelerator.Allocate1D(input);
                using var outBuf = accelerator.Allocate1D<float>(outH * outW);
                using var pBuf = accelerator.Allocate1D(prm);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>>(DebugAssertLoopBreakKernel)(outH * outW, inBuf.View, outBuf.View, pBuf.View);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync<float>();
                for (int i = 0; i < expected.Length; i++)
                    if (got[i] != expected[i])
                        throw new Exception($"in {inH}x{inW} out {outH}x{outW}: out[{i}] = {got[i]}, expected {expected[i]}");
            }
        });
    }
}
