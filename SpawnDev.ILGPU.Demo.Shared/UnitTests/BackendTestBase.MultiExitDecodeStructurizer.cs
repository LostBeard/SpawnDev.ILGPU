using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression (2026-06-20, Geordi, from Tuvok's MXFP4 WebGL report): a MULTI-EXIT in-register decode
    // (Float8E8M0Extensions.RawBitsToFloat) inlined BEFORE a loop whose body has ANOTHER multi-exit decode
    // (Float4E2M1Extensions.RawBitsToFloat) - exactly the MXFP4 dequant shape (decode an MX block's E8M0
    // scale, then decode the block's FP4 nibbles in a loop). The WebGL/GLSL structurizer DUPLICATES the
    // loop continuation into every exit arm of an inlined multi-exit function, so stacking two multi-exit
    // decoders exploded the GLSL ~4.8x (16k -> 77k lines) past WebGL's shader-compile limit -> compile fail
    // -> #blazor-error-ui -> 30s Playwright timeout (3 WebGL MXFP4 tests in SpawnDev.ILGPU.ML on 4.14.1).
    // Fixed by making BOTH RawBitsToFloat decoders SINGLE-EXIT / branchless (selects are expressions, not
    // control flow, so there is nothing for the structurizer to duplicate). This test reproduces the shape
    // so the LIBRARY itself fails if a decoder ever regresses to multi-exit - it doesn't depend on the ML
    // consumer to surface it (the standalone decode tests can't: they have no following loop to duplicate).
    public abstract partial class BackendTestBase
    {
        // Independent oracles reused from the same partial class: Fp4Oracle (BackendTestBase.FromRawBits) and
        // E8m0Oracle (BackendTestBase.Float8E8M0) - both Math.Pow / table based, NOT the library impl.

        // The MXFP4 shape: a multi-exit E8M0 scale decode BEFORE the loop, a multi-exit FP4 decode INSIDE it.
        static void Mxfp4ShapeKernel(Index1D i, ArrayView<int> scaleCodes, ArrayView<int> nibbleCodes, ArrayView<float> outF)
        {
            float scale = Float8E8M0Extensions.RawBitsToFloat(scaleCodes[i]);
            float acc = 0f;
            for (int k = 0; k < 8; k++)
                acc += Float4E2M1Extensions.RawBitsToFloat(nibbleCodes[i * 8 + k]) * scale;
            outF[i] = acc;
        }

        [TestMethod]
        public async Task MultiExitDecodeBeforeLoop_MXFP4Shape_CompilesAndMatches() => await RunTest(async acc =>
        {
            const int blocks = 64;
            var scaleCodes = new int[blocks];
            var nibbleCodes = new int[blocks * 8];
            for (int b = 0; b < blocks; b++)
            {
                scaleCodes[b] = 120 + (b % 16);                 // E8M0 exps 2^-7..2^8 - all NORMAL (no NaN/subnormal-FTZ)
                for (int k = 0; k < 8; k++) nibbleCodes[b * 8 + k] = (b + k) & 0xF; // sweep all 16 FP4 codes
            }
            using var sBuf = acc.Allocate1D(scaleCodes);
            using var nBuf = acc.Allocate1D(nibbleCodes);
            using var oBuf = acc.Allocate1D<float>(blocks);
            // If a decoder regresses to multi-exit, this LoadAutoGroupedStreamKernel -> WebGL GLSL compile
            // explodes and the dispatch faults (the bug Tuvok hit); on a healthy build it compiles + runs.
            var kern = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<float>>(Mxfp4ShapeKernel);
            kern((Index1D)blocks, sBuf.View, nBuf.View, oBuf.View);
            await acc.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<float>();
            for (int b = 0; b < blocks; b++)
            {
                float scale = E8m0Oracle(scaleCodes[b]); // independent oracle (Math.Pow), not the library impl
                float exp = 0f;
                for (int k = 0; k < 8; k++) exp += Fp4Oracle(nibbleCodes[b * 8 + k]) * scale;
                float tol = MathF.Abs(exp) * 1e-5f + 1e-5f;
                if (MathF.Abs(got[b] - exp) > tol)
                    throw new Exception($"MXFP4-shape block {b} ({BackendName}): expected {exp}, got {got[b]} (multi-exit decode structurizer regression?).");
            }
        });
    }
}
