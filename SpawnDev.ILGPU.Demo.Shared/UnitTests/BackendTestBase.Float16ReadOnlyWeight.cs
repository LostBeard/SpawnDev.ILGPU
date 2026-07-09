using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Half = ILGPU.Half;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression guard for the WebGPU read-only Float16 sub-word optimization (4.17.5+):
    // an ArrayView<Half> that a kernel only READS (a weight / activation input, never a store
    // target) is declared as a plain array<u32> binding and read with a plain indexed load
    // instead of array<atomic<u32>> + atomicLoad. The atomicLoad on a small weight buffer read
    // by tens of millions of threads serializes (~220x slower measured on RTX 4070); a plain
    // load of a never-atomically-written buffer is value-identical. The demotion must:
    //   (a) fire for read-only f16 params (correct values through the plain path), and
    //   (b) NOT fire for f16 params that are STORE targets (their packed sub-word stores still
    //       need the atomicAnd/atomicOr RMW) - proven by the mixed-kernel test where one f16
    //       param is read-only (plain) and another is written (atomic) in the SAME kernel.
    // The optimization is WebGPU-only codegen; every backend runs these kernels and must agree
    // with the CPU float oracle, so a misclassification anywhere shows as a value mismatch.
    public abstract partial class BackendTestBase
    {
        // Read-only f16 weight reduction: each output sums a window of f16 weights (mirrors the
        // Conv2DLowPWeightImpl reduction shape). `weight` is never stored -> demoted to plain load.
        static void F16WeightReductionKernel(
            Index1D i, ArrayView<Half> weight, ArrayView<float> output, int taps)
        {
            int k = (int)weight.Length;
            float sum = 0f;
            for (int t = 0; t < taps; t++)
                sum += (float)weight[(i + t) % k];
            output[i] = sum;
        }

        // Two read-only f16 inputs (both demoted to plain load) + f32 output.
        static void F16TwoInputsKernel(
            Index1D i, ArrayView<Half> a, ArrayView<Half> b, ArrayView<float> output)
            => output[i] = (float)a[i] * 2f + (float)b[i];

        // MIXED: read-only f16 weight (plain load) AND an f16 OUTPUT (atomic RMW store) in one kernel.
        static void F16MixedReadWriteKernel(
            Index1D i, ArrayView<Half> weight, ArrayView<float> act, ArrayView<Half> outHalf)
            => outHalf[i] = (Half)((float)weight[i] * act[i]);

        /// <summary>
        /// A read-only ArrayView&lt;Half&gt; weight buffer summed in a window per output element. On WebGPU
        /// this weight binding is demoted to plain array&lt;u32&gt; + plain indexed load; the result must match
        /// the CPU float oracle exactly (f16-&gt;f32 decode is lossless, so the sum of widened halves is exact).
        /// </summary>
        [TestMethod]
        public async Task Float16_ReadOnlyWeight_Reduction_MatchesReference() => await RunTest(async accelerator =>
        {
            int k = 300, n = 512, taps = 64;
            var wF = new Half[k];
            for (int j = 0; j < k; j++) wF[j] = (Half)(MathF.Sin(j * 0.37f) * 3.0f + 0.5f);

            using var wBuf = accelerator.Allocate1D(wF);
            using var oBuf = accelerator.Allocate1D<float>(n);
            var kern = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Half>, ArrayView<float>, int>(
                F16WeightReductionKernel);
            kern(n, wBuf.View, oBuf.View, taps);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<float>();

            for (int i = 0; i < n; i++)
            {
                float expected = 0f;
                for (int t = 0; t < taps; t++) expected += (float)wF[(i + t) % k];
                // Accumulation order is identical (ascending t); decode is exact. Tiny tol for f32 rounding.
                if (MathF.Abs(got[i] - expected) > 1e-3f * MathF.Max(1f, MathF.Abs(expected)))
                    throw new Exception(
                        $"f16 read-only weight reduction mismatch at [{i}]: expected={expected} got={got[i]} " +
                        "(demoted plain-load path corrupted the weight read?)");
            }
        });

        /// <summary>
        /// Two independent read-only ArrayView&lt;Half&gt; inputs in one kernel — both demoted to plain
        /// array&lt;u32&gt; bindings. Verifies multiple demotions coexist and read the correct values.
        /// </summary>
        [TestMethod]
        public async Task Float16_TwoReadOnlyInputs_MatchesReference() => await RunTest(async accelerator =>
        {
            int n = 400;
            var aF = new Half[n];
            var bF = new Half[n];
            for (int j = 0; j < n; j++) { aF[j] = (Half)(MathF.Cos(j * 0.11f) * 5f); bF[j] = (Half)(j * 0.03f - 6f); }

            using var aBuf = accelerator.Allocate1D(aF);
            using var bBuf = accelerator.Allocate1D(bF);
            using var oBuf = accelerator.Allocate1D<float>(n);
            var kern = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Half>, ArrayView<Half>, ArrayView<float>>(
                F16TwoInputsKernel);
            kern(n, aBuf.View, bBuf.View, oBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<float>();

            for (int i = 0; i < n; i++)
            {
                float expected = (float)aF[i] * 2f + (float)bF[i];
                if (MathF.Abs(got[i] - expected) > 1e-4f * MathF.Max(1f, MathF.Abs(expected)))
                    throw new Exception(
                        $"f16 two-read-only-inputs mismatch at [{i}]: expected={expected} got={got[i]}.");
            }
        });

        /// <summary>
        /// The critical separation case: ONE kernel with a read-only f16 weight (demoted to plain
        /// array&lt;u32&gt; + plain load) AND an f16 OUTPUT (must stay array&lt;atomic&lt;u32&gt;&gt; so the packed
        /// sub-word store keeps its atomicAnd/atomicOr RMW). If the read-only scan wrongly demoted the
        /// output, its store would lose atomicity (or emit an atomic op on a plain buffer = compile
        /// error); if it wrongly kept the weight atomic, the perf win is lost. Both halves are checked
        /// against the CPU oracle bit-exactly (f16 store is RNE, matching the managed HalfConversion).
        /// </summary>
        [TestMethod]
        public async Task Float16_MixedReadOnlyWeightAndF16Output_MatchesReference() => await RunTest(async accelerator =>
        {
            int n = 384;
            var wF = new Half[n];
            var actF = new float[n];
            for (int j = 0; j < n; j++) { wF[j] = (Half)(MathF.Sin(j * 0.21f) * 2f); actF[j] = MathF.Cos(j * 0.05f) * 1.5f; }

            using var wBuf = accelerator.Allocate1D(wF);
            using var actBuf = accelerator.Allocate1D(actF);
            using var outBuf = accelerator.Allocate1D<Half>(n);
            var kern = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Half>, ArrayView<float>, ArrayView<Half>>(
                F16MixedReadWriteKernel);
            kern(n, wBuf.View, actBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<Half>();

            for (int i = 0; i < n; i++)
            {
                // Kernel: f32 mul then _f32_to_f16 (RNE). Oracle: same f32 mul then managed Half round.
                float expected = (float)(Half)((float)wF[i] * actF[i]);
                float actual = (float)got[i];
                if (expected != actual)
                    throw new Exception(
                        $"f16 mixed read-only-weight + f16-output mismatch at [{i}]: expected={expected} got={actual} " +
                        "(read-only scan mis-classified the weight or the output?).");
            }
        });
    }
}
