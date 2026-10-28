using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Branches on values that differ between the threads of a warp although every operand is uniform. ILGPU's Uniforms
    // analysis (used by the PTX backend at O2, the default since 5.4.9) followed data only: a loop counter that threads
    // leave at different iterations is a phi of uniform values, and an atomic on one address gets uniform operands, so
    // both counted as uniform and the branch on them was emitted `bra.uni` - one path for the whole warp. Found through
    // SpawnDev.Codecs (Geordi 2026-10-28): the CUDA JPEG Huffman kernels went wrong at O2 (8 of 84 encoder fixtures, the
    // decoder "a coefficient run passes the end of the block"). The arms store, so no pass folds the branch into a select.
    public abstract partial class BackendTestBase
    {
        static void DivergentBranch_LoopCounterKernel(Index1D i, ArrayView<int> data, ArrayView<int> o)
        {
            int n = 0;
            while (n < data[i])
                n++;
            if (n > 3)
                o[i] = n * 7;
            else
                o[i] = -n;
        }

        [TestMethod]
        public async Task DivergentBranch_OnLoopCounter_EachThreadTakesItsOwnArm() => await RunTest(async accelerator =>
        {
            const int n = 256;
            var data = new int[n];
            for (int i = 0; i < n; i++) data[i] = (i * 7) % 9;   // 0..8, neighbours differ
            using var dData = accelerator.Allocate1D(data);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(DivergentBranch_LoopCounterKernel)(
                (Index1D)n, dData.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int want = data[i] > 3 ? data[i] * 7 : -data[i];
                if (got[i] != want)
                    throw new Exception($"thread {i} (loop count {data[i]}): {got[i]}, expected {want}");
            }
        });

        static void DivergentBranch_AtomicSlotKernel(Index1D i, ArrayView<int> counter, ArrayView<int> o)
        {
            int slot = Atomic.Add(ref counter[0], 1);
            if (slot < 5)
                o[i] = 1;
            else
                o[i] = 2;
        }

        [TestMethod]
        public async Task DivergentBranch_OnAtomicSlot_EachThreadTakesItsOwnArm() => await RunTest(async accelerator =>
        {
            const int n = 256;
            using var dCounter = accelerator.Allocate1D(new int[1]);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(DivergentBranch_AtomicSlotKernel)(
                (Index1D)n, dCounter.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            int ones = got.Count(v => v == 1), twos = got.Count(v => v == 2);
            if (ones != 5 || twos != n - 5)
                throw new Exception($"{ones} threads took slot < 5 and {twos} the other arm, expected 5 and {n - 5}");
        });
    }
}
