using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Phi moves of a conditional branch or a switch whose edges bind different phis, against the host.
    //
    // A loop latch `if (sp == 0) break; else continue;` binds the loop header's phis on the back edge (values that pass
    // through unchanged bind to themselves and are saved to temporaries first) and the exit block's phis on the exit edge,
    // from the same values. The PTX and OpenCL code generators emit the two edges' moves one after the other and kept the
    // back edge's temporaries for the exit edge, which does not run the back edge's moves: the exit read registers written
    // only on an earlier back edge, or never (stale or garbage values; on CUDA the CELT encoder's range coder state, and a
    // kernel that never finished). The OpenCL switch emitted its phi moves after the switch statement, where no case runs
    // them: a case that jumps straight to a block with phis read stale values. Found through SpawnDev.Codecs: the CELT band
    // encoder's iterative partition loop failed on CUDA and OpenCL only.
    public abstract partial class BackendTestBase
    {
        // Stage sequences of the latch loop: 0 changes x and y and pops, 1 pushes, 2 pops without a change.
        static readonly int[] EdgePhiStages =
        {
            1, 1, 2, 0, 2, 0, 0, 0,
            1, 2, 1, 0, 2, 0, 0, 0,
            1, 1, 1, 2, 0, 0, 2, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            2, 0, 0, 0, 0, 0, 0, 0,
            1, 0, 2, 0, 0, 0, 0, 0,
            1, 2, 1, 2, 1, 0, 0, 2,
            1, 1, 0, 1, 2, 2, 0, 0,
        };

        static int EdgePhiLatchLoopHost(int[] st, int t)
        {
            int x = 7, y = 100, sp = 1, i = 0;
            while (true)
            {
                int s = st[t * 8 + (i & 7)];
                i++;
                if (s == 0)
                {
                    x = x * 3 + 1; y -= x;
                    sp--;
                    if (sp == 0) break;
                    continue;
                }
                if (s == 1) { sp++; continue; }
                sp--;
                if (sp == 0) break;
            }
            return x * 1000 + y;
        }

        static void EdgePhiLatchLoopKernel(Index1D t, ArrayView<int> st, ArrayView<int> o)
        {
            int x = 7, y = 100, sp = 1, i = 0;
            while (true)
            {
                int s = st[t * 8 + (i & 7)];
                i++;
                if (s == 0)
                {
                    x = x * 3 + 1; y -= x;
                    sp--;
                    if (sp == 0) break;
                    continue;
                }
                if (s == 1) { sp++; continue; }
                sp--;
                if (sp == 0) break;
            }
            o[t] = x * 1000 + y;
        }

        static int EdgePhiSwitchHost(int[] st, int t)
        {
            int x = st[t] * 7 + 3, y = st[t + 1] - 4;
            switch (st[t] & 7)
            {
                case 0: x = 5; y = 1; break;
                case 1: x = 9; break;
                case 2: break;
                case 4: y = x + 2; x = 11; break;
                default: break;
            }
            return x * 1000 + y;
        }

        static void EdgePhiSwitchKernel(Index1D t, ArrayView<int> st, ArrayView<int> o)
        {
            int x = st[t] * 7 + 3, y = st[t + 1] - 4;
            switch (st[t] & 7)
            {
                case 0: x = 5; y = 1; break;
                case 1: x = 9; break;
                case 2: break;
                case 4: y = x + 2; x = 11; break;
                default: break;
            }
            o[t] = x * 1000 + y;
        }

        [TestMethod]
        public async Task EdgePhiBindings_LoopLatchExit_MatchHost() => await RunTest(async accelerator =>
        {
            int n = EdgePhiStages.Length / 8;
            using var dS = accelerator.Allocate1D(EdgePhiStages);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(EdgePhiLatchLoopKernel)((Index1D)n, dS.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
            {
                int want = EdgePhiLatchLoopHost(EdgePhiStages, t);
                if (got[t] != want)
                    throw new Exception($"stage sequence {t}: {got[t]}, expected {want}");
            }
        });

        [TestMethod]
        public async Task EdgePhiBindings_SwitchCaseToPhiBlock_MatchHost() => await RunTest(async accelerator =>
        {
            int n = EdgePhiStages.Length - 1;
            var st = new int[EdgePhiStages.Length];
            for (int i = 0; i < st.Length; i++) st[i] = (i * 5 + EdgePhiStages[i]) % 16;
            using var dS = accelerator.Allocate1D(st);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(EdgePhiSwitchKernel)((Index1D)n, dS.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
            {
                int want = EdgePhiSwitchHost(st, t);
                if (got[t] != want)
                    throw new Exception($"thread {t} (selector {st[t] & 7}): {got[t]}, expected {want}");
            }
        });
    }
}
