using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // SpawnDev.Codecs' MDCT kernel shape (MdctKernel.MdctKernelBody): thread t computes one output of block t / n with a
    // 64-bit input base (blockIdx * 2n), an argument reduction in 64-bit modular arithmetic, and a 2n-term cosine sum.
    // On WebGL every block after the first came out wrong (Geordi 2026-10-26: N = 64, 4 blocks, first bad index 80).
    public abstract partial class BackendTestBase
    {
        static void MdctShapeKernel(Index1D threadIdx, ArrayView<float> input, ArrayView<float> output, int blockCount, int n, int firstBlock)
        {
            int t = threadIdx;
            int total = blockCount * n;
            if (t >= total) return;
            int local = t / n;
            int k = t - local * n;
            int blockIdx = firstBlock + local;
            long inBase = (long)blockIdx * 2L * n;
            long modulus = 8L * n;
            long halfModulus = 4L * n;
            long q = 2L * k + 1L;
            long deltaResidue = (2L * q) % modulus;
            long m0 = 1L + n;
            long residue = (m0 * q) % modulus;
            float invFourN = XMath.PI / (4.0f * n);
            float acc = 0.0f;
            for (int idx = 0; idx < 2 * n; idx++)
            {
                long folded = residue >= halfModulus ? residue - modulus : residue;
                float thetaReduced = invFourN * (float)folded;
                acc += input[inBase + idx] * XMath.Cos(thetaReduced);
                residue += deltaResidue;
                if (residue >= modulus) residue -= modulus;
            }
            output[t] = acc;
        }

        [TestMethod]
        public async Task MdctShape_FourBlocks_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 64, blocks = 4;
            var input = new float[blocks * 2 * n];
            var rng = new Random(91);
            for (int i = 0; i < input.Length; i++) input[i] = (float)(rng.NextDouble() * 2 - 1);
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<float>(blocks * n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int, int>(MdctShapeKernel)(
                (Index1D)(blocks * n), dIn.View, dOut.View, blocks, n, 0);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<float>();
            for (int t = 0; t < blocks * n; t++)
            {
                int b = t / n, k = t % n;
                double want = 0;
                for (int idx = 0; idx < 2 * n; idx++)
                    want += input[b * 2 * n + idx] * Math.Cos(Math.PI / (4.0 * n) * (2 * idx + 1 + n) * (2 * k + 1));
                if (Math.Abs(got[t] - want) > 1e-3)
                    throw new Exception($"output[{t}] (block {b}, k {k}) = {got[t]}, expected {want:G9}");
            }
        });
    }
}
