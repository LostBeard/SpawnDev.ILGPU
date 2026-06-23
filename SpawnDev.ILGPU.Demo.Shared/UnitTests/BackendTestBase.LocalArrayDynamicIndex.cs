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
    //
    // SECOND ROOT CAUSE (fixed 2026-06-23, also in LowerArrays.cs): the N>32 init-loop fix
    // above unmasked a Wasm-only "reads 0" bug. The rewriter positions new values at
    // value.Index + 1, so the alloca/view/defaultElement created while lowering NewArray sit
    // AFTER `value` in the block; SplitBlock(value) then pushed that setup into the loop EXIT
    // block, leaving the array view DEFINED in the exit while the zero-init loop body (and the
    // struct) USE it - an SSA dominance violation. GPU backends rematerialize the static alloca
    // address at each use and tolerated it; the Wasm state machine executes the literal block
    // order, so the zero-init loop ran with an unset (0) view local and clobbered low linear
    // memory instead of the array. Fixed by inserting the setup BEFORE `value`
    // (builder.SetupInsertPosition(value)) so it stays in the dominating current block while
    // SplitBlock(value) exports only the user code. Now correct on ALL 6 backends.
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
            // Verified correct on ALL 6 backends (CUDA / OpenCL / CPU / WebGPU /
            // WebGPU-no-subgroups / WebGL / Wasm) after both LowerArrays fixes - see the
            // class-level comment for the two root causes.
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
