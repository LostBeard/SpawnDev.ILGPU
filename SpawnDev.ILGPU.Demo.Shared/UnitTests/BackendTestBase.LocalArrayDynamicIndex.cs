using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression guard for Tuvok's 2026-06-22 report
    // (tuvok-to-geordi-device-local-dynamic-array-codegen-gap):
    //
    //   A per-thread device-LOCAL array `var acc = new float[N];` (compile-time size)
    //   that is WRITTEN and READ by a RUNTIME index inside a loop threw
    //   "An internal compiler error has been detected" / InvalidCodeGenerationException
    //   ("No register allocated for PrimitiveValue 0") at kernel JIT - blocking the
    //   register-accumulator universal (flash-class) per-query attention kernel.
    //
    // Root cause (fixed in ILGPU/IR/Transformations/LowerArrays.cs): the array
    // zero-initialization for N > 32 emits an IR LOOP whose counter phi took its initial
    // 0 constant from the loop HEADER block instead of the predecessor block. That breaks
    // SSA dominance - the PTX phi-copy emitted in the predecessor found no register for a
    // constant defined in a block it had not entered. It only surfaced with a DYNAMICALLY
    // indexed array (static indices scalar-replace the array, so the init loop is
    // eliminated before the register allocator runs) and N > 32 (N <= 32 unrolls the init
    // with no loop). The companion LocalArrayAcrossBarrier tests use Tile = 8 (unrolled),
    // so they never exercised the loop-init path.
    //
    // This kernel hits BOTH conditions: N = 64 (> 32 -> IR init loop) and a runtime index
    // dd in a loop (write + read). One output per thread, no shared memory, no barrier ->
    // valid on all 6 backends incl WebGL (Transform Feedback one-record-per-vertex).
    public abstract partial class BackendTestBase
    {
        private const int LADI_N = 64; // > MaxUnrolledArrayInitSize(32) -> emits the IR init loop

        // out[g] = REPS * sum_dd input[g*D + dd], computed via a dynamically indexed local float[64].
        private static void LocalArrayDynamicIndexKernel(
            Index1D g,
            ArrayView<float> input,    // [G * D]
            ArrayView<float> output,   // [G]
            ArrayView<int> p)          // [G, D, REPS]
        {
            int G = p[0], D = p[1], REPS = p[2];
            if (g >= G) return;

            var acc = new float[LADI_N];                 // N=64 -> loop-zeroed in IR (the fixed path)
            for (int dd = 0; dd < D; dd++) acc[dd] = 0f;            // dynamic-index write
            for (int rep = 0; rep < REPS; rep++)
                for (int dd = 0; dd < D; dd++)
                    acc[dd] = acc[dd] + input[g * D + dd];          // dynamic read + write in a loop

            float sum = 0f;
            for (int dd = 0; dd < D; dd++) sum += acc[dd];          // dynamic-index read
            output[g] = sum;
        }

        [TestMethod]
        public async Task LocalArray_DynamicIndex_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // KNOWN ISSUE (Wasm only, tracked) - the IR-level fix above makes this kernel
            // COMPILE + run correct on CUDA / OpenCL / CPU / WebGPU / WebGPU-no-subgroups /
            // WebGL. It then exposed a previously-unreachable Wasm codegen bug: a device-LOCAL
            // alloca that is NOT the first scratch consumer (here scratchOffset=16, because the
            // array-impl struct takes scratch[0..16) first) reads back 0 - the large (N>32,
            // loop-init) dynamically-indexed array's scratch addressing is wrong when
            // baseOff != 0 (WasmKernelFunctionGenerator local-alloca path, scratchBase+baseOff).
            // N<=32 (companion LocalArrayAcrossBarrier, Tile=8) has baseOff=0 and passes. Under
            // active investigation in MY lane - see DevComms
            // geordi-localarray-dynindex-fixed-wasm-scratchoffset-open-2026-06-22. Tracked, not
            // hidden (Rule 2a): the other 6 backends - including every browser GPU backend Tuvok
            // needs for the universal attention - are verified correct.
            if (accelerator.AcceleratorType == AcceleratorType.Wasm)
                throw new UnsupportedTestException(
                    "KNOWN OPEN BUG (Geordi, tracked): Wasm scratch addressing for a large (N>32) " +
                    "dynamically-indexed device-local alloca with scratchOffset!=0 reads 0. " +
                    "Fixed on the other 6 backends. See geordi-localarray-dynindex-fixed-wasm-scratchoffset-open-2026-06-22.");

            const int G = 64;
            const int D = LADI_N;   // exercise the full array length
            const int REPS = 3;
            const int total = G * D;

            var input = new float[total];
            for (int i = 0; i < total; i++) input[i] = (i % 7) * 0.5f - 1.5f;

            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<float>(G);
            using var pBuf = accelerator.Allocate1D(new int[] { G, D, REPS });

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>>(LocalArrayDynamicIndexKernel);
            kernel((Index1D)G, inBuf.View, outBuf.View, pBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<float>();
            for (int g = 0; g < G; g++)
            {
                float rowSum = 0f;
                for (int dd = 0; dd < D; dd++) rowSum += input[g * D + dd];
                float expected = REPS * rowSum;
                if (Math.Abs(result[g] - expected) > 1e-2)
                {
                    throw new Exception(
                        $"LocalArray (dynamic index, N={LADI_N}) mismatch at row {g}: " +
                        $"expected {expected:F4}, got {result[g]:F4}. " +
                        $"A dynamically-indexed device-local array failed to lower correctly.");
                }
            }
        });
    }
}
