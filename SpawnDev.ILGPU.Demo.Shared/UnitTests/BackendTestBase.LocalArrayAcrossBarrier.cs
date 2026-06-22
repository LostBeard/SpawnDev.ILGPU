using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression guard for Tuvok's 2026-06-21 report
    // (tuvok-to-geordi-ilgpu-wasm-device-local-array-miscompiles-2026-06-21):
    //
    //   A device kernel that accumulates into a LOCAL ARRAY `var acc = new float[N];`
    //   indexed in a loop returns WRONG numerical results on the Wasm backend, while
    //   being byte-exact on CPU / CUDA / OpenCL / WebGPU / WebGL.
    //
    // Where it bit: FusedDequantMatMul.GemmDequantQ4_K_MultiRowImpl / Q6_K (SpawnDev.ILGPU.ML)
    // - the M>1 multi-row dequant GEMM, which fills a `float[GemmMTile]` accumulator in the
    // K-loop, then reads each element ACROSS a barrier-bearing group tree-reduction. Tuvok's
    // isolation proved the single variable is `new float[]` vs scalars: unrolling the array to
    // scalars made it correct on Wasm; the array form was wrong on Wasm only.
    //
    // The two kernels below split the failure mode in half so the root cause is unambiguous:
    //   * LocalArray_NoBarrier   - local array filled + written straight out, NO barrier.
    //   * LocalArray_AcrossBarrier - local array filled, then each element read across a
    //                                shared-memory tree reduction (Group.Barrier between reads).
    //                                This is the exact FusedDequantMatMul shape, minus dequant.
    // If NoBarrier passes but AcrossBarrier fails on Wasm, the bug is the phase-mode fiber
    // state-save region overlapping the local-alloca scratch region (the array does not survive
    // a barrier yield), NOT plain local-array addressing.
    public abstract partial class BackendTestBase
    {
        private const int LAB_GroupSize = 32; // group size for the tree reduction
        private const int LAB_Tile = 8;       // local-array length == FusedDequantMatMul.GemmMTile

        // === Control: local array, NO barrier ===
        // out[g*T + t] = sum over the T-loop fill of acc[t]; one thread per group (G threads total).
        // acc[t] = input[g*T + t] * 2. Plain local-array load/store, no shared memory, no barrier.
        // One thread per row g. Fills a device-local float[T] from input (dynamic index
        // store), then reads every element back (dynamic index load) to produce ONE
        // output per thread. One-output-per-thread keeps it WebGL-compatible (Transform
        // Feedback captures one record per vertex — a multi-output-per-thread loop would
        // collapse to the last write; that is the WebGL scatter limit, NOT a local-array
        // bug). Still exercises the exact `new float[]` fill+read that miscompiled on Wasm.
        private static void LocalArrayNoBarrierKernel(
            Index1D g,
            ArrayView<float> input,    // [G * T]
            ArrayView<float> output,   // [G]
            ArrayView<int> p)          // [G, T]
        {
            int G = p[0], T = p[1];
            if (g >= G) return;
            var acc = new float[LAB_Tile];
            for (int t = 0; t < T; t++) acc[t] = 0f;
            for (int t = 0; t < T; t++) acc[t] += input[g * T + t] * 2f;
            float sum = 0f;
            for (int t = 0; t < T; t++) sum += acc[t];
            output[g] = sum;
        }

        [TestMethod]
        public async Task LocalArray_NoBarrier_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            const int G = 64;
            const int T = LAB_Tile;
            const int total = G * T;

            var input = new float[total];
            for (int i = 0; i < total; i++) input[i] = (i % 11) * 0.5f - 2.0f;

            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<float>(G);
            using var pBuf = accelerator.Allocate1D(new int[] { G, T });

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>>(LocalArrayNoBarrierKernel);
            kernel((Index1D)G, inBuf.View, outBuf.View, pBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<float>();
            for (int g = 0; g < G; g++)
            {
                float expected = 0f;
                for (int t = 0; t < T; t++) expected += input[g * T + t] * 2f;
                if (Math.Abs(result[g] - expected) > 1e-3)
                {
                    throw new Exception(
                        $"LocalArray (no barrier) mismatch at row {g}: " +
                        $"expected {expected:F4}, got {result[g]:F4} " +
                        $"(a new float[] whose element address is mis-lowered reads garbage).");
                }
            }
        });

        // === Repro: local array read ACROSS barriers (the FusedDequantMatMul shape) ===
        // One group per column g. Each thread fills a local float[T] from its lane's input, then
        // each acc[t] is tree-reduced across the group with barriers and written to output[g*T+t].
        // output[g*T + t] = sum over tid of input[(g*T + t)*GS + tid].
        private static void LocalArrayAcrossBarrierKernel(
            ArrayView<float> input,    // [G * T * GS]
            ArrayView<float> output,   // [G * T]
            ArrayView<int> p)          // [G, T, GS]
        {
            int G = p[0], GS = p[2];
            // Use the compile-time-constant tile length for every loop that wraps a
            // Group.Barrier(), so WGSL uniformity analysis can prove the barriers run
            // in uniform control flow (a runtime bound `p[1]` makes WebGPU reject the
            // shader). LAB_Tile == the runtime p[1] by construction.
            const int T = LAB_Tile;
            int g = Grid.IdxX;
            int tid = Group.IdxX;

            var sh = SharedMemory.Allocate<float>(LAB_GroupSize);
            var acc = new float[LAB_Tile];          // <-- the device-local array under test
            for (int t = 0; t < T; t++) acc[t] = 0f;

            if (g < G)
            {
                for (int t = 0; t < T; t++)
                    acc[t] += input[(g * T + t) * GS + tid];
            }

            // Reduce each row's partial across the group. sh is reused per row (barrier between).
            // acc[t] is read AFTER prior iterations executed Group.Barrier() many times -> the
            // array must survive barrier yields.
            for (int t = 0; t < T; t++)
            {
                sh[tid] = acc[t];
                Group.Barrier();
                for (int stride = LAB_GroupSize / 2; stride > 0; stride >>= 1)
                {
                    if (tid < stride) sh[tid] += sh[tid + stride];
                    Group.Barrier();
                }
                if (tid == 0 && g < G) output[g * T + t] = sh[0];
                Group.Barrier();
            }
        }

        [TestMethod]
        public async Task LocalArray_AcrossBarrier_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // Shared-memory tree reduction is structurally unsupported on WebGL (no shared mem / no
            // barriers) - same skip as GemvGroupReduce / the real ML M>1 GEMM.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("In-kernel shared-memory reduction is structurally unsupported on WebGL.");

            const int G = 12;
            const int T = LAB_Tile;
            const int GS = LAB_GroupSize;
            const int total = G * T * GS;

            var input = new float[total];
            for (int i = 0; i < total; i++) input[i] = ((i % 13) - 6) * 0.25f;

            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<float>(G * T);
            using var pBuf = accelerator.Allocate1D(new int[] { G, T, GS });

            var kernel = accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<int>>(
                LocalArrayAcrossBarrierKernel);
            kernel(new KernelConfig(G, GS), inBuf.View, outBuf.View, pBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<float>();
            for (int g = 0; g < G; g++)
            {
                for (int t = 0; t < T; t++)
                {
                    double expected = 0;
                    for (int tid = 0; tid < GS; tid++)
                        expected += input[(g * T + t) * GS + tid];
                    int outIdx = g * T + t;
                    if (Math.Abs(result[outIdx] - expected) > 1e-2)
                        throw new Exception(
                            $"LocalArray (across barrier) mismatch at output[{outIdx}] (group {g}, t {t}): " +
                            $"expected {expected:F4}, got {result[outIdx]:F4}. " +
                            $"A local float[] that does not survive the barrier yield reads clobbered values.");
                }
            }
        });
    }
}
