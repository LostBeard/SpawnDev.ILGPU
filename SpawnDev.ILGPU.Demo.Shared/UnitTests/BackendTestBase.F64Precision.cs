using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Emulated f64 (Dekker double-float on WebGPU/WebGL) must keep the LOW half of values that are not exact in f32.
    // DoubleDivisionTest allows 1% error, so a lost low half passes it. Found on WebGL: 1.0 / 1e-14 came back as
    // 1 / float(1e-14) = 100000001754833 (PointerAlias_ViewStoredInTwoBranches_DeclaredOnce, 2026-09-28).
    // Each element records three things separately so a failure names its stage: the loaded value stored back, the
    // quotient 1/x, and a kernel constant stored directly.
    public abstract partial class BackendTestBase
    {
        static void F64LowHalfKernel(Index1D i, ArrayView1D<double, Stride1D.Dense> v, ArrayView1D<double, Stride1D.Dense> outp)
        {
            double x = v[i];
            outp[i * 3] = x;
            outp[i * 3 + 1] = 1.0 / x;
            outp[i * 3 + 2] = 1e-14;
        }

        [TestMethod]
        public async Task F64_NonF32ExactValues_KeepLowHalf() => await RunTest(async accelerator =>
        {
            var input = new double[] { 1e-14, 0.1, 1.0 / 3.0, 1e-20, 12345.678901234567, -2.718281828459045 };
            using var v = accelerator.Allocate1D(input);
            using var outp = accelerator.Allocate1D<double>(input.Length * 3);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<double, Stride1D.Dense>, ArrayView1D<double, Stride1D.Dense>>(
                F64LowHalfKernel)(input.Length, v.View, outp.View);
            await accelerator.SynchronizeAsync();
            var got = await outp.CopyToHostAsync<double>();

            // Double-float carries ~48 mantissa bits; 1e-12 relative is far inside that and far outside f32 (6e-8).
            var errors = new System.Collections.Generic.List<string>();
            void Check(string what, double actual, double expected)
            {
                if (!(Math.Abs(actual - expected) <= Math.Abs(expected) * 1e-12))
                    errors.Add($"{what} = {actual:R}, expected {expected:R}");
            }
            for (int i = 0; i < input.Length; i++)
            {
                Check($"load/store[{i}]", got[i * 3], input[i]);
                Check($"1/x[{i}] (x={input[i]:R})", got[i * 3 + 1], 1.0 / input[i]);
                Check($"constant[{i}]", got[i * 3 + 2], 1e-14);
            }
            if (errors.Count > 0) throw new Exception(string.Join("; ", errors));
        });

        // A constant assigned in a branch (a phi), after the same constant was compared against.
        static void F64BranchConstantKernel(Index1D i, ArrayView1D<double, Stride1D.Dense> v, ArrayView1D<double, Stride1D.Dense> outp)
        {
            double x = v[i];
            if (!(x > 1e-14))
                x = 1e-14;
            outp[i * 2] = x;
            outp[i * 2 + 1] = 1.0 / x;
        }

        // Same, without the compare against that constant.
        static void F64BranchConstantNoCompareKernel(Index1D i, ArrayView1D<double, Stride1D.Dense> v, ArrayView1D<double, Stride1D.Dense> outp)
        {
            double x = v[i];
            if (!(x > 0.5))
                x = 1e-14;
            outp[i * 2] = x;
            outp[i * 2 + 1] = 1.0 / x;
        }

        [TestMethod]
        public async Task F64_BranchAssignedConstant_KeepsLowHalf() => await RunTest(async accelerator =>
        {
            var input = new double[] { 1e-20, 0.25, 3.0 };
            var errors = new System.Collections.Generic.List<string>();
            foreach (var (name, kernel, threshold) in new (string, Action<Index1D, ArrayView1D<double, Stride1D.Dense>, ArrayView1D<double, Stride1D.Dense>>, double)[]
                { ("compare", F64BranchConstantKernel, 1e-14), ("no-compare", F64BranchConstantNoCompareKernel, 0.5) })
            {
                using var v = accelerator.Allocate1D(input);
                using var outp = accelerator.Allocate1D<double>(input.Length * 2);
                accelerator.LoadAutoGroupedStreamKernel(kernel)(input.Length, v.View, outp.View);
                await accelerator.SynchronizeAsync();
                var got = await outp.CopyToHostAsync<double>();
                for (int i = 0; i < input.Length; i++)
                {
                    double x = input[i] > threshold ? input[i] : 1e-14;
                    if (!(Math.Abs(got[i * 2] - x) <= x * 1e-12)) errors.Add($"{name} x[{i}] = {got[i * 2]:R}, expected {x:R}");
                    if (!(Math.Abs(got[i * 2 + 1] - 1.0 / x) <= 1.0 / x * 1e-12)) errors.Add($"{name} 1/x[{i}] = {got[i * 2 + 1]:R}, expected {1.0 / x:R}");
                }
            }
            if (errors.Count > 0) throw new Exception(string.Join("; ", errors));
        });

        // A group barrier under a branch on an emulated double computed from a scalar parameter. The double arithmetic
        // passes its error terms through an opaque zero (WGSL _f32_opq, 5.4.56) so a reassociating compiler (Mesa's NIR)
        // cannot cancel them; that zero must be read in a way WGSL's uniformity analysis still calls uniform, or this
        // kernel stops compiling ("workgroupBarrier must only be called from uniform control flow": measured with the
        // zero in a var<private>, 2026-11-27). Also checks the sum keeps its low half: 0.1 * k + 2.0 is not an f32.
        static void F64BarrierUnderDoubleBranchKernel(ArrayView<double> v, ArrayView<double> outp, double threshold)
        {
            var sh = SharedMemory.Allocate<double>(64);
            int t = Group.IdxX;
            double s = threshold + 1.0;
            sh[t] = v[t];
            if (s > 1.5)
            {
                Group.Barrier();
                outp[t] = sh[(t + 1) % 64] + s;
            }
            else
            {
                outp[t] = -1.0;
            }
        }

        [TestMethod]
        public async Task F64_BarrierUnderBranchOnDouble_CompilesAndKeepsLowHalf() => await RunTest(async accelerator =>
        {
            if (!accelerator.Device.Satisfies(new global::SpawnDev.ILGPU.AcceleratorRequirements { RequiresBarriers = true, RequiresSharedMemory = true }))
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: the kernel uses shared memory and a group barrier");
            const int n = 64;
            var input = new double[n];
            for (int k = 0; k < n; k++) input[k] = 0.1 * k;
            using var v = accelerator.Allocate1D(input);
            using var outp = accelerator.Allocate1D<double>(n);
            accelerator.LoadStreamKernel<ArrayView<double>, ArrayView<double>, double>(F64BarrierUnderDoubleBranchKernel)(
                new KernelConfig(1, n), v.View, outp.View, 1.0);
            await accelerator.SynchronizeAsync();
            var got = await outp.CopyToHostAsync<double>();
            var errors = new System.Collections.Generic.List<string>();
            for (int k = 0; k < n && errors.Count < 4; k++)
            {
                double want = input[(k + 1) % n] + 2.0;
                if (!(Math.Abs(got[k] - want) <= Math.Abs(want) * 1e-12)) errors.Add($"out[{k}] = {got[k]:R}, expected {want:R}");
            }
            if (errors.Count > 0) throw new Exception(string.Join("; ", errors));
        });
    }
}
