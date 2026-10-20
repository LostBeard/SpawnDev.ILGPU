using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A loop with two exits: an early `return` from inside it and the normal exit. The Velocity backend (SIMD, masked lanes) parks the lanes that leave a loop in the mask of the exit
    // block they leave to and continues once no lane is left in the loop; it used to jump straight to the exit block of the LAST lane to leave, which skipped the block where the lanes
    // that returned early were waiting: they never reached the merge block, never stored, and their output stayed uninitialised (HelperCodegen_EarlyReturnSkipsLaterLoop_CallerBranches,
    // 60 of the ML library's Velocity failures were the divide, this was one of the wrong-answer ones). Every other backend must give the same answers, so this runs on all of them.
    public abstract partial class BackendTestBase
    {
        static bool LoopReturnsEarly(ArrayView<float> m, int b, int n, float bar)
        {
            for (int i = 0; i < n; i++) { if (m[b + i] > bar) return false; }
            return true;
        }

        static bool LoopReturnsEarlyThenLoops(ArrayView<float> m, int b, int rows, int cols, float bar)
        {
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++) { if (m[b + r * cols + c] > bar) return false; }
            float s = 0f;
            for (int i = 0; i < rows * cols; i++) s += m[b + i];
            return s > 0f;
        }

        static void LoopReturnsEarlyKernel(Index1D t, ArrayView<float> m, ArrayView<float> o, int rows, int cols, float bar) =>
            o[t] = LoopReturnsEarly(m, t * rows * cols, rows * cols, bar) ? 1f : -1f;

        static void LoopReturnsEarlyThenLoopsKernel(Index1D t, ArrayView<float> m, ArrayView<float> o, int rows, int cols, float bar) =>
            o[t] = LoopReturnsEarlyThenLoops(m, t * rows * cols, rows, cols, bar) ? 1f : -1f;

        [TestMethod]
        public async Task LoopEarlyReturn_LanesLeavingThroughEitherExitStore() => await RunTest(async accelerator =>
        {
            const int threads = 64, rows = 5, cols = 4;
            const float bar = 0.97f;                       // about half of the threads see a value above it somewhere in their row block
            var rng = new Random(11);
            var m = new float[threads * rows * cols];
            for (int i = 0; i < m.Length; i++) m[i] = (float)rng.NextDouble();
            var expected = new float[threads]; int early = 0;
            for (int t = 0; t < threads; t++)
            {
                bool ok = true;
                for (int i = 0; i < rows * cols && ok; i++) if (m[t * rows * cols + i] > bar) ok = false;
                expected[t] = ok ? 1f : -1f; if (!ok) early++;
            }
            if (early == 0 || early == threads) throw new Exception($"test data must mix early and normal exits ({early}/{threads} early)");

            using var mBuf = accelerator.Allocate1D(m);
            using var o1 = accelerator.Allocate1D<float>(threads); using var o2 = accelerator.Allocate1D<float>(threads);
            o1.MemSetToZero(); o2.MemSetToZero();          // a lane that never stores stays 0: neither 1 nor -1
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int, float>(LoopReturnsEarlyKernel);
            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, int, int, float>(LoopReturnsEarlyThenLoopsKernel);
            k1((Index1D)threads, mBuf.View, o1.View, rows, cols, bar);
            k2((Index1D)threads, mBuf.View, o2.View, rows, cols, bar);
            await accelerator.SynchronizeAsync();
            var a1 = await o1.CopyToHostAsync<float>(); var a2 = await o2.CopyToHostAsync<float>();
            for (int t = 0; t < threads; t++)
            {
                if (a1[t] != expected[t]) throw new Exception($"loop with an early return: thread {t} got {a1[t]}, expected {expected[t]} ({early}/{threads} return early)");
                if (a2[t] != expected[t]) throw new Exception($"nested loops with an early return and a later loop: thread {t} got {a2[t]}, expected {expected[t]} ({early}/{threads} return early)");
            }
        });
    }
}
