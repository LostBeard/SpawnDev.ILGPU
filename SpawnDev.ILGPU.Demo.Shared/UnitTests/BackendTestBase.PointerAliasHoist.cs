using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // One view stored in two separate branches (one of them returns early). The WGSL post-processor lifts each block's
    // simple pointer alias (`let v_N = &paramX;`) to function scope - and the structured emitter can emit the same IR
    // value's code in both branches, so the lifted alias was declared TWICE: "redeclaration of 'v_421'" (Tint) and the
    // pipeline was invalid. Found by SpawnScene's GpuBundleAdjuster.PointSolveKernel on WebGPU (b34, 2026-09-28).
    //
    // ⚠️ Only RELEASE IL reproduces it (Debug IL takes another path), and only with f64 elements: PMT publishes the demo
    // in Release, so the browser lanes see the failing shape. Desktop red-check: build SpawnScene.Tests -c Release and
    // `naga` the generated WGSL of its WgslAliasRepro.TwoBranchStoreRepro - "redefinition of `v_53`" before the fix.
    public abstract partial class BackendTestBase
    {
        static void PointerAliasTwoBranchKernel(Index1D i, ArrayView1D<double, Stride1D.Dense> v, ArrayView1D<double, Stride1D.Dense> flags,
            ArrayView1D<double, Stride1D.Dense> outp)
        {
            double x = v[i];
            if (!(x > -1.0 && x < 2.0))
            {
                flags[0] = 1.0;
                outp[i] = 0.0;
                return;
            }
            if (!(x > 1e-14))
            {
                x = 1e-14;
                flags[1] = 1.0;
            }
            outp[i] = 1.0 / x;
        }

        [TestMethod]
        public async Task PointerAlias_ViewStoredInTwoBranches_DeclaredOnce() => await RunTest(async accelerator =>
        {
            // Out of range (early return), tiny (clamped), ordinary - all three paths taken.
            var input = new double[] { -3.0, 0.5, 1e-20, 1.25, 5.0, 0.0, 1.9, -0.5 };
            using var v = accelerator.Allocate1D(input);
            using var flags = accelerator.Allocate1D<double>(2);
            using var outp = accelerator.Allocate1D<double>(input.Length);
            flags.MemSetToZero();
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<double, Stride1D.Dense>, ArrayView1D<double, Stride1D.Dense>,
                ArrayView1D<double, Stride1D.Dense>>(PointerAliasTwoBranchKernel)(input.Length, v.View, flags.View, outp.View);
            await accelerator.SynchronizeAsync();
            var got = await outp.CopyToHostAsync<double>();
            var gotFlags = await flags.CopyToHostAsync<double>();

            for (int i = 0; i < input.Length; i++)
            {
                double x = input[i];
                double expected = !(x > -1.0 && x < 2.0) ? 0.0 : 1.0 / (x > 1e-14 ? x : 1e-14);
                double tol = Math.Abs(expected) * 1e-12;
                if (!(Math.Abs(got[i] - expected) <= tol))
                    throw new Exception($"out[{i}] = {got[i]:R}, expected {expected:R} (input {x:R})");
            }
            if (gotFlags[0] != 1.0 || gotFlags[1] != 1.0)
                throw new Exception($"flags = [{gotFlags[0]}, {gotFlags[1]}], expected [1, 1]");
        });
    }
}
