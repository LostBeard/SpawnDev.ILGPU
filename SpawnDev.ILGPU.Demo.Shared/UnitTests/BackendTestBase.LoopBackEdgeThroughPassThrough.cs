using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-25 (SpawnDev.Codecs, found by Geordi): the lossless WebP encoder never finished on WebGPU. Its prefix-code
    // writer (Vp8lEncodeGpu.WriteCodeDefinition) run-length codes the code lengths in a `while (i < n)` whose body is an
    // if/else with each arm ENDING in inner loops. The zero arm's last inner loop exits through a code-free block that
    // jumps to the outer loop's header; the WGSL generator skips such pass-through blocks after a loop, and the moves of
    // that block's edge (the OUTER loop's back edge) were emitted by nobody: EmitIntermediateBlocksToExit stops its exit
    // chain at an enclosing header on purpose. So `i` and `nt` never advanced after a zero length and the loop spun
    // forever (device lost on a real GPU). The other arm's inner loop branches to the outer header directly, which the
    // break path handles - that arm was right. CPU/CUDA/OpenCL/Wasm were never affected. Same shape in SilkPulses.
    //
    // The test kernel counts its outer iterations in DEVICE MEMORY and bails out after n of them, so the broken code
    // returns -1 instead of hanging the GPU (a register counter is a loop-carried value and was lost the same way).
    public abstract partial class BackendTestBase
    {
        const int RleSegment = 280; // a VP8L alphabet: 256 literals + 24 length prefixes

        static int RleTokensGuarded(ArrayView<int> w, ArrayView<int> tokSym, ArrayView<int> tokExtra,
            ArrayView<int> iters, int t, int lb, int n)
        {
            int nt = 0;
            int prev = 8;
            int i = 0;
            while (i < n)
            {
                int g = iters[t] + 1;
                iters[t] = g;
                if (g > n) return -1;
                int v = w[lb + i];
                int run = 1;
                bool same = (i + run < n);
                while (same)
                {
                    bool eq = w[lb + i + run] == v;
                    if (eq) run++;
                    same = eq & (i + run < n);
                }
                i += run;
                if (v == 0)
                {
                    while (run >= 3)
                    {
                        if (run >= 11)
                        {
                            int r = run > 138 ? 138 : run;
                            tokSym[lb + nt] = 18;
                            tokExtra[lb + nt] = r - 11;
                            run -= r;
                        }
                        else
                        {
                            int r = run > 10 ? 10 : run;
                            tokSym[lb + nt] = 17;
                            tokExtra[lb + nt] = r - 3;
                            run -= r;
                        }
                        nt++;
                    }
                    while (run > 0)
                    {
                        tokSym[lb + nt] = 0;
                        tokExtra[lb + nt] = 0;
                        nt++;
                        run--;
                    }
                }
                else
                {
                    if (v != prev)
                    {
                        tokSym[lb + nt] = v;
                        tokExtra[lb + nt] = 0;
                        nt++;
                        run--;
                        prev = v;
                    }
                    while (run >= 3)
                    {
                        int r = run > 6 ? 6 : run;
                        tokSym[lb + nt] = 16;
                        tokExtra[lb + nt] = r - 3;
                        nt++;
                        run -= r;
                    }
                    while (run > 0)
                    {
                        tokSym[lb + nt] = v;
                        tokExtra[lb + nt] = 0;
                        nt++;
                        run--;
                    }
                }
            }
            return nt;
        }

        static void RleTokensGuardedKernel(Index1D t, ArrayView<int> w, ArrayView<int> tokSym, ArrayView<int> tokExtra,
            ArrayView<int> iters, ArrayView<int> count, int segments)
        {
            if (t >= segments) return;
            count[t] = RleTokensGuarded(w, tokSym, tokExtra, iters, t, (int)t * RleSegment, RleSegment);
        }

        [TestMethod]
        public async Task LoopBackEdge_ThroughPassThroughBlock_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // WebGL writes one output element per thread at its own slot; each thread here writes a token stream.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: in-kernel multi-store per thread is not supported.");

            const int segments = 256;
            const int total = segments * RleSegment;
            // Code lengths like a real prefix code: runs of zeros (short and long: tokens 17 and 18), runs of one
            // length (token 16), changes of length. Segment 0 is all zeros, segment 1 has no zero at all.
            var lengths = new int[total];
            var rng = new Random(20261025);
            for (int s = 0; s < segments; s++)
            {
                int k = 0;
                while (k < RleSegment)
                {
                    int run = rng.Next(4) switch { 0 => 1 + rng.Next(2), 1 => 3 + rng.Next(8), 2 => 11 + rng.Next(140), _ => 1 + rng.Next(20) };
                    int v = s == 0 ? 0 : s == 1 ? 1 + rng.Next(15) : rng.Next(3) == 0 ? 0 : 1 + rng.Next(15);
                    for (int j = 0; j < run && k < RleSegment; j++) lengths[s * RleSegment + k++] = v;
                }
            }

            // Host oracle: the same tokens, computed on the CPU.
            var wantSym = new int[total];
            var wantExtra = new int[total];
            var wantCount = new int[segments];
            for (int s = 0; s < segments; s++)
            {
                int lb = s * RleSegment, nt = 0, prev = 8, i = 0;
                void Emit(int sym, int extra) { wantSym[lb + nt] = sym; wantExtra[lb + nt] = extra; nt++; }
                while (i < RleSegment)
                {
                    int v = lengths[lb + i];
                    int run = 1;
                    while (i + run < RleSegment && lengths[lb + i + run] == v) run++;
                    i += run;
                    if (v == 0)
                    {
                        while (run >= 3)
                        {
                            int r = run >= 11 ? Math.Min(run, 138) : Math.Min(run, 10);
                            Emit(run >= 11 ? 18 : 17, run >= 11 ? r - 11 : r - 3);
                            run -= r;
                        }
                        for (; run > 0; run--) Emit(0, 0);
                    }
                    else
                    {
                        if (v != prev) { Emit(v, 0); run--; prev = v; }
                        while (run >= 3) { int r = Math.Min(run, 6); Emit(16, r - 3); run -= r; }
                        for (; run > 0; run--) Emit(v, 0);
                    }
                }
                wantCount[s] = nt;
            }

            using var wBuf = accelerator.Allocate1D(lengths);
            using var symBuf = accelerator.Allocate1D<int>(total);
            using var extraBuf = accelerator.Allocate1D<int>(total);
            using var itersBuf = accelerator.Allocate1D(new int[segments]);
            using var countBuf = accelerator.Allocate1D<int>(segments);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>,
                ArrayView<int>, ArrayView<int>, int>(RleTokensGuardedKernel);
            kernel(segments, wBuf.View, symBuf.View, extraBuf.View, itersBuf.View, countBuf.View, segments);
            await accelerator.SynchronizeAsync();

            var count = await countBuf.CopyToHostAsync();
            for (int s = 0; s < segments; s++)
                if (count[s] != wantCount[s])
                    throw new Exception($"run-length token count on {BackendName}, segment {s}: got {count[s]}, expected {wantCount[s]}" +
                        (count[s] == -1 ? " (-1 = the outer loop ran more than n times: its loop-carried values did not advance)" : ""));
            var sym = await symBuf.CopyToHostAsync();
            var extra = await extraBuf.CopyToHostAsync();
            for (int s = 0; s < segments; s++)
                for (int k = 0; k < wantCount[s]; k++)
                {
                    int j = s * RleSegment + k;
                    if (sym[j] != wantSym[j] || extra[j] != wantExtra[j])
                        throw new Exception($"run-length token on {BackendName}, segment {s}, token {k}: got ({sym[j]}, {extra[j]}), expected ({wantSym[j]}, {wantExtra[j]})");
                }
            Console.WriteLine($"[LoopBackEdge] {segments} segments of {RleSegment} code lengths, run-length tokens match the CPU oracle on {BackendName} ✓");
        });
    }
}
