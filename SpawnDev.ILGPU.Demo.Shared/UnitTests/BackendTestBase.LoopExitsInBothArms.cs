using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A loop that leaves from both arms of an if / else HUNG on Velocity, even with a single thread (2026-10-29,
    // SpawnDev.Codecs' CELT band recursion, quant_partition as an explicit-stack loop): reverse post order put the first arm's
    // exit block between two loop-body blocks, and Velocity's branch builder, which jumps to a loop's first exit once no
    // lane is left in it, then fell through into the body, which jumped back to that exit, forever. Each loop also has a
    // guard exit after far more iterations than it needs, so a backend whose loop runs on reports a wrong value; it cannot
    // catch THIS hang, which spins with no lane active (the guard never runs): red-checked without the fix, both tests hung
    // on both Velocity lanes until the process was killed.
    // The tree walk then failed on WebGPU (the guard caught it: -1 for every thread deeper than 0): the WGSL walker took
    // the "push the second child" block as the merge of the "pop and resume the parent" arm, and that arm, which goes back
    // to the loop header on its own, ended without `continue`, falling into the push.
    public abstract partial class BackendTestBase
    {
        private const int LoopExitsArmsStride = 16, LoopExitsArmsGuard = 256;

        // while (true) { if (s == 0) { ...; if (sp == 0) break; } else { ...; if (sp == 0) break; } }
        private static void LoopExitsInBothArmsKernel(Index1D i, ArrayView<int> v, ArrayView<int> o)
        {
            int at = i * LoopExitsArmsStride;
            int sp = v[at];
            int r = 0, guard = 0;
            while (true)
            {
                if (++guard > LoopExitsArmsGuard) { r = -1; break; }
                int s = v[at + 1 + sp];
                if (s == 0) { r += 1; sp--; if (sp == 0) break; }
                else { r += 3; sp--; if (sp == 0) break; }
            }
            o[i] = r;
        }

        private static int LoopExitsInBothArmsHost(int[] v, int i)
        {
            int at = i * LoopExitsArmsStride, sp = v[at], r = 0;
            while (true)
            {
                int s = v[at + 1 + sp];
                if (s == 0) { r += 1; sp--; if (sp == 0) break; }
                else { r += 3; sp--; if (sp == 0) break; }
            }
            return r;
        }

        // A tree walk on an explicit frame stack: the loop pushes a child and continues, a leaf (with inner loops) pops and
        // breaks at the root, a parent resumes after each child. The shape of an IR backend's replacement for recursion.
        private static int LoopExitsStackWalk(ArrayView<int> stk, int at, int depth, int seed)
        {
            const int frame = 4;
            int sp = 1, ret = 0, guard = 0;
            stk[at] = depth; stk[at + 1] = seed; stk[at + 2] = 0;
            while (true)
            {
                if (++guard > LoopExitsArmsGuard) { ret = -1; break; }
                int f = at + (sp - 1) * frame;
                int stage = stk[f + 2];
                if (stage == 0)
                {
                    int d = stk[f], s = stk[f + 1];
                    if (d > 0)
                    {
                        stk[f + 2] = 1;
                        int c = f + frame;
                        stk[c] = d - 1; stk[c + 1] = s * 3 + 1; stk[c + 2] = 0;
                        sp++;
                        continue;
                    }
                    int k = s & 7, leaf = 0;
                    while (k > 0) { leaf += k; k--; }
                    for (int j = 0; j < (s & 3); j++) leaf ^= j + 1;
                    ret = leaf;
                    sp--;
                    if (sp == 0) break;
                    continue;
                }
                if (stage == 1)
                {
                    stk[f + 3] = ret;
                    int c = f + frame;
                    stk[c] = stk[f] - 1; stk[c + 1] = stk[f + 1] * 5 + 2; stk[c + 2] = 0;
                    stk[f + 2] = 2;
                    sp++;
                    continue;
                }
                ret = stk[f + 3] * 2 + ret;
                sp--;
                if (sp == 0) break;
            }
            return ret;
        }

        private static void LoopExitsStackWalkKernel(Index1D i, ArrayView<int> stk, ArrayView<int> o)
        {
            o[i] = LoopExitsStackWalk(stk, i * 32, i % 4, i * 7 + 3);
        }

        private static int LoopExitsStackWalkHost(int depth, int seed)
        {
            if (depth == 0)
            {
                int k = seed & 7, leaf = 0;
                while (k > 0) { leaf += k; k--; }
                for (int j = 0; j < (seed & 3); j++) leaf ^= j + 1;
                return leaf;
            }
            return LoopExitsStackWalkHost(depth - 1, seed * 3 + 1) * 2 + LoopExitsStackWalkHost(depth - 1, seed * 5 + 2);
        }

        [TestMethod(Timeout = 60000)]
        public async Task LoopExits_InBothArmsOfAnIf_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var v = new int[n * LoopExitsArmsStride];
            for (int t = 0; t < n; t++)
            {
                int at = t * LoopExitsArmsStride;
                v[at] = 1 + t % 7;                                  // 1..7 iterations, different per lane
                for (int j = 1; j < LoopExitsArmsStride; j++) v[at + j] = ((t * 5 + j) / 2) & 1;
            }
            using var dV = accelerator.Allocate1D(v);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(LoopExitsInBothArmsKernel)(n, dV.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync();
            for (int t = 0; t < n; t++)
            {
                int want = LoopExitsInBothArmsHost(v, t);
                if (got[t] != want)
                    throw new Exception($"thread {t}: {got[t]}, expected {want}" + (got[t] == -1 ? " (the loop never exited: guard hit)" : ""));
            }
        });

        [TestMethod(Timeout = 60000)]
        public async Task LoopExits_ExplicitStackTreeWalk_MatchesHost() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("The frame stack is written at computed indices (scatter stores); WebGL Transform Feedback captures one record per thread (RequiresScatterStores).");
            const int n = 64;
            using var dStk = accelerator.Allocate1D<int>(n * 32);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(LoopExitsStackWalkKernel)(n, dStk.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync();
            for (int t = 0; t < n; t++)
            {
                int want = LoopExitsStackWalkHost(t % 4, t * 7 + 3);
                if (got[t] != want)
                    throw new Exception($"thread {t} (depth {t % 4}): {got[t]}, expected {want}" + (got[t] == -1 ? " (the loop never exited: guard hit)" : ""));
            }
        });
    }
}
