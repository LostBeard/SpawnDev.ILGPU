using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Loops whose induction variable is multiplied, divided or shifted, with constant bounds, against the host: the IR's loop
    // unroller takes their trip count from InductionVariableBounds.TryGetTripCount. It worked on magnitudes, so it miscounted
    // negative sequences whose bound is a power of two (`for (x = -1; x > -1024; x *= 2)` ran 11 times when unrolled, 10 in IL),
    // and took Math.Abs of a bound of int.MinValue, which threw an OverflowException at compile time (a kernel that does not
    // compile on any IR backend). Found through SpawnDev.Codecs: CELT encoder stage kernels with constant arguments. The trip
    // count of such a loop now comes from running its own 32-bit arithmetic; one that does not end within 64 steps is not
    // unrolled.
    public abstract partial class BackendTestBase
    {
        const int TripCountLoops = 8;

        static int TripCountLoopHost(int k)
        {
            int acc = 0;
            switch (k)
            {
                case 0: for (int x = -1; x > -1024; x *= 2) acc++; break;
                case 1: for (int x = 1; x < 1024; x *= 2) acc++; break;
                case 2: for (int x = -1; -1024 < x; x *= 2) acc++; break;
                case 3: for (int x = int.MinValue; x < -1; x /= 2) acc++; break;
                case 4: for (int x = -1; x > int.MinValue; x *= 2) acc++; break;
                case 5: for (int x = int.MinValue; x != 0; x >>= 1) { acc++; if (acc > 40) break; } break;
                case 6: for (int x = -3; x >= int.MinValue / 2; x *= 2) acc++; break;
                default: for (uint x = 0x80000000u; x > 3; x >>= 3) acc++; break;
            }
            return acc;
        }

        // One loop per thread (WebGL captures one output per thread); each loop's bounds are constants, so it can unroll.
        static void TripCountLoopsKernel(Index1D i, ArrayView<int> o)
        {
            int acc = 0;
            int k = i % TripCountLoops;
            if (k == 0) { for (int x = -1; x > -1024; x *= 2) acc++; }
            else if (k == 1) { for (int x = 1; x < 1024; x *= 2) acc++; }
            else if (k == 2) { for (int x = -1; -1024 < x; x *= 2) acc++; }
            else if (k == 3) { for (int x = int.MinValue; x < -1; x /= 2) acc++; }
            else if (k == 4) { for (int x = -1; x > int.MinValue; x *= 2) acc++; }
            else if (k == 5) { for (int x = int.MinValue; x != 0; x >>= 1) { acc++; if (acc > 40) break; } }
            else if (k == 6) { for (int x = -3; x >= int.MinValue / 2; x *= 2) acc++; }
            else { for (uint x = 0x80000000u; x > 3; x >>= 3) acc++; }
            o[i] = acc;
        }

        [TestMethod]
        public async Task MultiplicativeLoops_TripCount_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = TripCountLoops * 4;
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(TripCountLoopsKernel)((Index1D)n, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int want = TripCountLoopHost(i % TripCountLoops);
                if (got[i] != want)
                    throw new Exception($"loop {i % TripCountLoops} (thread {i}): {got[i]} iterations, expected {want}");
            }
        });
    }
}
