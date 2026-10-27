using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // `flag = !flag` in a taken branch, where flag is a phi of the constants false / true (an unrolled best-of loop).
    // Velocity's Scalar2 warps hold a bool's true as 1 and the bool NOT was a bitwise NOT: ~1 = -2 is still true, so the
    // flip was lost and every later choice went wrong (SpawnDev.Codecs WebPLossyEncoderGpu on Velocity Scalar2, the first
    // device `Context.Builder.Velocity()` registers: 0 of 222 files identical). The reduced shape of its PickBestIntra16.
    // Every backend, against a CPU oracle; the VelocityScalar2Tests lane runs it on the scalar warps.
    public abstract partial class BackendTestBase
    {
        public struct BoolNotFlip_Score
        {
            public long D, SD, H, R, Score;
            public void Init() { D = 0; SD = 0; R = 0; H = 0; Score = 0x7fffffffffffffL; }
            public void SetRD(int lambda) => Score = (R + H) * lambda + 256L * (D + SD);
        }

        static void BoolNotFlip_Kernel(Index1D idx, ArrayView<int> input, ArrayView<int> output)
        {
            int lambda = input[4];
            var sA = new BoolNotFlip_Score();
            sA.Score = ((long)input[6] << 32) | (uint)input[5];
            var sB = new BoolNotFlip_Score();
            sB.Init();
            bool bestIsA = true;
            for (int mode = 0; mode < 4; ++mode)
            {
                var cur = new BoolNotFlip_Score();
                cur.D = mode * 3;
                cur.H = input[mode];
                cur.R = 100 - mode;
                cur.SetRD(lambda);
                long bestScore = bestIsA ? sA.Score : sB.Score;
                bool better = (mode == 0) | (cur.Score < bestScore);
                if (better)
                {
                    bestIsA = !bestIsA;
                    output[idx * 8 + 4 + mode] = 1;
                }
                output[idx * 8 + mode] = bestIsA ? 1 : 0;
            }
        }

        [TestMethod]
        public async Task BoolNot_FlipOfConstantPhiInBranch_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            int[] input = { 663, 919, 872, 919, 6348, -1, 0x7fffff };
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<int>(8);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(BoolNotFlip_Kernel)((Index1D)1, dIn.View, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            var want = new int[8];
            var sA = new BoolNotFlip_Score { Score = ((long)input[6] << 32) | (uint)input[5] };
            var sB = new BoolNotFlip_Score();
            sB.Init();
            bool bestIsA = true;
            for (int mode = 0; mode < 4; ++mode)
            {
                var cur = new BoolNotFlip_Score { D = mode * 3, H = input[mode], R = 100 - mode };
                cur.SetRD(input[4]);
                long bestScore = bestIsA ? sA.Score : sB.Score;
                if ((mode == 0) | (cur.Score < bestScore)) { bestIsA = !bestIsA; want[4 + mode] = 1; }
                want[mode] = bestIsA ? 1 : 0;
            }
            for (int i = 0; i < 8; i++)
                if (got[i] != want[i]) throw new Exception($"output[{i}] = {got[i]}, expected {want[i]} (got {string.Join(",", got)})");
        });
    }
}
