using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WGSL: the uniformity transform's synthetic tile-loop counter (`_uf_tile_iter`) was declared (`var`) at its FIRST use -
    // inside the block of the first thread-strided loop it rewrote. A later thread-strided loop in a SIBLING scope then only
    // assigned it: "no definition in scope for identifier: `_uf_tile_iter`" and the pipeline was invalid. Found by
    // SpawnScene's GpuGlobalPositioner.CholeskyKernel (2026-09-29): a dense Cholesky solve in ONE workgroup - thread-strided
    // loops between barriers inside the column loop, then more in the forward and back substitutions after it. The fix
    // declares every synthetic counter once at function scope.
    //
    // Desktop red-check: dump the WGSL (ShaderCompiler.Generate) of this kernel, or of SpawnScene's CholeskyKernel
    // (SpawnScene.Tests GpuBundleAdjusterWgslDump.DumpPositionerWgsl), and run `naga` on it - it fails before the fix.
    public abstract partial class BackendTestBase
    {
        private const int SCS_N = 40;          // unknowns: more than the group, so every strided loop iterates
        private const int SCS_GroupSize = 32;

        // Solves A x = b for SPD A (n x n, row-major, factored in place into its lower Cholesky factor) in one workgroup.
        private static void SyntheticCounterScopeCholeskyKernel(ArrayView<float> a, ArrayView<float> b, ArrayView<float> x,
            ArrayView<int> failed, int n)
        {
            int t = Group.IdxX, dim = Group.DimX;
            for (int j = 0; j < n; j++)
            {
                if (t == 0)
                {
                    float s = a[j * n + j];
                    if (!(s > 0f)) { failed[0] = 1; s = 1f; }
                    a[j * n + j] = XMath.Sqrt(s);
                }
                Group.Barrier();
                float l = a[j * n + j];
                for (int i = j + 1 + t; i < n; i += dim) a[i * n + j] /= l;
                Group.Barrier();
                for (int i = j + 1 + t; i < n; i += dim)
                {
                    float lij = a[i * n + j];
                    for (int k = j + 1; k <= i; k++) a[i * n + k] -= lij * a[k * n + j];
                }
                Group.Barrier();
            }
            for (int i = t; i < n; i += dim) x[i] = b[i];
            Group.Barrier();
            for (int j = 0; j < n; j++)
            {
                if (t == 0) x[j] = x[j] / a[j * n + j];
                Group.Barrier();
                float xj = x[j];
                for (int i = j + 1 + t; i < n; i += dim) x[i] -= a[i * n + j] * xj;
                Group.Barrier();
            }
            for (int j = n - 1; j >= 0; j--)
            {
                if (t == 0) x[j] = x[j] / a[j * n + j];
                Group.Barrier();
                float xj = x[j];
                for (int i = t; i < j; i += dim) x[i] -= a[j * n + i] * xj;
                Group.Barrier();
            }
        }

        [TestMethod]
        public async Task SyntheticCounter_SiblingStridedLoops_CholeskySolve() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("Group barriers are structurally unsupported on WebGL.");
            const int n = SCS_N;
            // A = M Mᵀ + n I (SPD, well conditioned), b = A x_true.
            var m = new double[n * n];
            for (int i = 0; i < n * n; i++) m[i] = ((i * 7919) % 17) / 17.0 - 0.5;
            var a = new double[n * n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                {
                    double s = i == j ? n : 0;
                    for (int k = 0; k < n; k++) s += m[i * n + k] * m[j * n + k];
                    a[i * n + j] = s;
                }
            var xTrue = new double[n];
            for (int i = 0; i < n; i++) xTrue[i] = (i % 5) - 2 + 0.25 * i / n;
            var bh = new float[n];
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int k = 0; k < n; k++) s += a[i * n + k] * xTrue[k];
                bh[i] = (float)s;
            }
            var af = new float[n * n];
            for (int i = 0; i < n * n; i++) af[i] = (float)a[i];

            using var aBuf = accelerator.Allocate1D(af);
            using var bBuf = accelerator.Allocate1D(bh);
            using var xBuf = accelerator.Allocate1D<float>(n);
            using var failBuf = accelerator.Allocate1D<int>(1);
            failBuf.MemSetToZero();
            int group = Math.Min(SCS_GroupSize, accelerator.MaxNumThreadsPerGroup);
            accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<int>, int>(
                SyntheticCounterScopeCholeskyKernel)(new KernelConfig(1, group), aBuf.View, bBuf.View, xBuf.View, failBuf.View, n);
            await accelerator.SynchronizeAsync();
            var got = await xBuf.CopyToHostAsync<float>();
            var failed = await failBuf.CopyToHostAsync<int>();
            if (failed[0] != 0) throw new Exception("the factorisation hit a non-positive pivot on an SPD matrix");
            for (int i = 0; i < n; i++)
                if (!(Math.Abs(got[i] - xTrue[i]) <= 1e-3))
                    throw new Exception($"x[{i}] = {got[i]:R}, expected {xTrue[i]:R}");
        });
    }
}
