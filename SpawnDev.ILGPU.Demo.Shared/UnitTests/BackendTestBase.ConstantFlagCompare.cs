using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A bool the frontend folds to a constant, then turned into an int. Roslyn makes `best` the constant -1L, so
    // `best == ulong.MaxValue` folds to true, `true | x` folds to true, and `differs ? 1 : 0` is `cgt.un differs, 0`:
    // an ordered compare of two Int1 constants, which IRBuilder's constant fold had no case for ("The type 'Int1' is
    // not supported in the scope of this compare operation", every backend, at kernel compile). The Int1-vs-constant
    // rewrite also treated every ordered kind as NotEqual. Found while debugging the SpawnDev.Codecs BC7 encoder
    // (2026-10-27). Every backend, against the host.
    public abstract partial class BackendTestBase
    {
        static void ConstantFlagCompareKernel(Index1D i, ArrayView<int> inp, ArrayView<int> o)
        {
            ulong best = ulong.MaxValue;
            bool differs = (best == ulong.MaxValue) | (inp[i] != 7);
            bool never = (best != ulong.MaxValue) & (inp[i] != 7);
            bool big = inp[i] > 3;
            o[i * 5 + 0] = differs ? 1 : 0;
            o[i * 5 + 1] = never ? 1 : 0;
            o[i * 5 + 2] = (differs ? 1 : 0) + (never ? 2 : 0) + (big ? 4 : 0);
            o[i * 5 + 3] = (big ? 1 : 0) >= 1 ? 1 : 0;
            o[i * 5 + 4] = (never ? 1 : 0) <= 0 ? 1 : 0;
        }

        [TestMethod]
        public async Task ConstantFlag_ToInt_MatchesHost() => await RunTest(async accelerator =>
        {
            var inp = new[] { 0, 3, 4, 7, 100, -5 };
            using var dI = accelerator.Allocate1D(inp);
            using var dO = accelerator.Allocate1D<int>(inp.Length * 5);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(ConstantFlagCompareKernel)(
                (Index1D)inp.Length, dI.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            for (int i = 0; i < inp.Length; i++)
            {
                int big = inp[i] > 3 ? 1 : 0;
                var want = new[] { 1, 0, 1 + 4 * big, big, 1 };
                for (int k = 0; k < 5; k++)
                    if (got[i * 5 + k] != want[k])
                        throw new Exception($"input {inp[i]}, output {k}: {got[i * 5 + k]}, expected {want[k]}");
            }
        });
    }
}
