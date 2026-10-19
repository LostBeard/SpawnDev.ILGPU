using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A loop with a compile-time trip count inside an `if`, whose result is used after the if. ILGPU's LoopUnrolling fully unrolled the
    // loop and rewired the branch into the exit block, but the exit block's phi (the join of the if: the value from before the loop
    // when the branch is not taken, the loop's result when it is) kept the cleared loop header as its source block. The phi then never
    // saw the loop's result: every thread that took the branch kept the value from before the loop, silently, on CUDA, OpenCL, WebGPU,
    // WebGL and Wasm (found writing a face-propagation kernel in SpawnDev.ILGPU.ML's device UV atlas; the same kernel with the
    // condition inside the loop, or the loop unrolled by hand, was right). Fixed by remapping the exit block's phi sources in the
    // full-unroll path of LoopUnrolling.
    public abstract partial class BackendTestBase
    {
        static void LoopInsideIfKernel(Index1D f, ArrayView<int> adj, ArrayView<int> label, ArrayView<int> vin, ArrayView<int> vout)
        {
            int m = vin[f];
            if (label[f] < 0)
                for (int e = 0; e < 3; e++)
                {
                    int g = adj[f * 3 + e];
                    if (g >= 0 && label[g] < 0 && vin[g] < m) m = vin[g];
                }
            vout[f] = m;
        }

        // the loop result is summed, not selected: the same join, a different live-out value
        static void LoopInsideIfSumKernel(Index1D f, ArrayView<int> adj, ArrayView<int> label, ArrayView<int> vin, ArrayView<int> vout)
        {
            int s = 1000;
            if (label[f] < 0)
                for (int e = 0; e < 4; e++) s += vin[adj[f * 3 + e % 3] & 1023] & 15;
            vout[f] = s;
        }

        [TestMethod]
        public async Task LoopInsideIf_ResultReachesTheJoin() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var adj = new int[n * 3]; var label = new int[n]; var vin = new int[n];
            var rnd = new Random(3);
            for (int i = 0; i < n; i++)
            {
                adj[i * 3] = (i + 1) % n; adj[i * 3 + 1] = (i + n - 1) % n; adj[i * 3 + 2] = i % 7 == 0 ? -1 : (i * 31 + 5) % n;
                label[i] = i % 11 == 0 ? 3 : -1; vin[i] = rnd.Next(1, 1 << 30);
            }
            var expectMin = new int[n]; var expectSum = new int[n];
            for (int f = 0; f < n; f++)
            {
                int m = vin[f];
                if (label[f] < 0)
                    for (int e = 0; e < 3; e++) { int g = adj[f * 3 + e]; if (g >= 0 && label[g] < 0 && vin[g] < m) m = vin[g]; }
                expectMin[f] = m;
                int s = 1000;
                if (label[f] < 0)
                    for (int e = 0; e < 4; e++) s += vin[adj[f * 3 + e % 3] & 1023] & 15;
                expectSum[f] = s;
            }
            using var ab = accelerator.Allocate1D(adj); using var lb = accelerator.Allocate1D(label); using var vi = accelerator.Allocate1D(vin);
            using var vo = accelerator.Allocate1D<int>(n);
            var kMin = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(LoopInsideIfKernel);
            kMin((Index1D)n, ab.View, lb.View, vi.View, vo.View);
            await accelerator.SynchronizeAsync();
            var gotMin = await vo.CopyToHostAsync<int>();
            int badMin = 0; for (int i = 0; i < n; i++) if (gotMin[i] != expectMin[i]) badMin++;
            var kSum = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(LoopInsideIfSumKernel);
            kSum((Index1D)n, ab.View, lb.View, vi.View, vo.View);
            await accelerator.SynchronizeAsync();
            var gotSum = await vo.CopyToHostAsync<int>();
            int badSum = 0; for (int i = 0; i < n; i++) if (gotSum[i] != expectSum[i]) badSum++;
            if (badMin != 0 || badSum != 0)
                throw new Exception($"a loop inside an if lost its result: {badMin} of {n} minima and {badSum} of {n} sums differ from the host (the exit block's phi kept the cleared loop header as its source)");
        });
    }
}
