using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    /// <summary>
    /// A block reached from BOTH arms of an if/else through a nested short-circuit, e.g. the
    /// round-half-to-even step <c>if (r2 &gt; den || (r2 == den &amp;&amp; (q &amp; 1) == 1)) q++;</c>:
    ///
    ///     X: if (r2 &gt; den)  -&gt; T (q + 1)  else -&gt; F
    ///     F: if (r2 == den) -&gt; G          else -&gt; M
    ///     G: if (q odd)     -&gt; T          else -&gt; M
    ///
    /// T is the shared convergence block, two branches down on the false side. The loop-aware WGSL
    /// walker emitted T under the first <c>if</c> and, finding it visited when G branched to it, emitted
    /// an EMPTY block - the phi copy for that edge was lost and q read its default, 0. MEASURED
    /// 2026-09-23 in SpawnDev.ILGPU.ML's area resample (DAv3 preprocessing): exact .5 ties with an odd
    /// quotient came out 0 on WebGPU only, the same pixels every run; CUDA/OpenCL/CPU/Wasm/WebGL right.
    ///
    /// Three shapes, because WGSL has three structured emitters: a kernel WITH a loop (the loop-aware
    /// walker that had the bug), a kernel WITHOUT one, and the same logic in a [NoInlining] helper
    /// (separate helper codegen path). Every branch combination, against the same expression on the host.
    /// </summary>
    public abstract partial class BackendTestBase
    {
        static readonly int[] ScPhiQ = { 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6, 7, 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly int[] ScPhiR = { 5, 5, 5, 5, 5, 5, 5, 5, 10, 10, 10, 10, 10, 10, 10, 10, 15, 15, 15, 15, 15, 15, 15, 15 };
        const int ScPhiDen = 10;

        static int ScPhiRound(int q, int r2, int den)
        {
            if (r2 > den || (r2 == den && (q & 1) == 1)) q++;
            return q;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int ScPhiRoundHelper(int q, int r2, int den)
        {
            if (r2 > den || (r2 == den && (q & 1) == 1)) q++;
            return q;
        }

        static void ScPhiLoopKernel(Index1D i, ArrayView<int> q, ArrayView<int> r, ArrayView<int> o)
        {
            // A data-dependent loop, so the kernel goes through the loop-aware structured walker.
            int acc = 0;
            for (int k = 0; k <= q[i]; k++) acc += k;
            int v = q[i];
            if (r[i] > ScPhiDen || (r[i] == ScPhiDen && (v & 1) == 1)) v++;
            o[i] = v * 1000 + acc;
        }

        static void ScPhiNoLoopKernel(Index1D i, ArrayView<int> q, ArrayView<int> r, ArrayView<int> o)
        {
            int v = q[i];
            if (r[i] > ScPhiDen || (r[i] == ScPhiDen && (v & 1) == 1)) v++;
            o[i] = v;
        }

        static void ScPhiHelperKernel(Index1D i, ArrayView<int> q, ArrayView<int> r, ArrayView<int> o)
        {
            int acc = 0;
            for (int k = 0; k <= q[i]; k++) acc += k;
            o[i] = ScPhiRoundHelper(q[i], r[i], ScPhiDen) * 1000 + acc;
        }

        [TestMethod]
        public async Task ShortCircuit_SharedBlockPhi_AllEmitters() => await RunTest(async accelerator =>
        {
            int n = ScPhiQ.Length;
            using var q = accelerator.Allocate1D(ScPhiQ);
            using var r = accelerator.Allocate1D(ScPhiR);
            using var o = accelerator.Allocate1D<int>(n);
            var failures = new List<string>();

            async Task Check(string shape, Action<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>> kernel, bool withLoop)
            {
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(kernel);
                k((Index1D)n, q.View, r.View, o.View);
                await accelerator.SynchronizeAsync();
                var got = await o.CopyToHostAsync<int>();
                for (int j = 0; j < n; j++)
                {
                    int acc = 0;
                    for (int t = 0; t <= ScPhiQ[j]; t++) acc += t;
                    int want = withLoop ? ScPhiRound(ScPhiQ[j], ScPhiR[j], ScPhiDen) * 1000 + acc : ScPhiRound(ScPhiQ[j], ScPhiR[j], ScPhiDen);
                    if (got[j] != want)
                        failures.Add($"{shape}: q={ScPhiQ[j]} r2={ScPhiR[j]} got {got[j]} want {want}");
                }
            }

            await Check("loop kernel", ScPhiLoopKernel, withLoop: true);
            await Check("no-loop kernel", ScPhiNoLoopKernel, withLoop: false);
            await Check("helper", ScPhiHelperKernel, withLoop: true);
            if (failures.Count > 0)
                throw new Exception($"short-circuit shared-block phi wrong ({failures.Count}): {string.Join("; ", failures.Take(12))}");
        });
        // `if (a || b) { ..early returns..; maybe k = 0 } else if (c) return ..;` then a block BOTH arms reach, then the
        // exit. The arms meet BEFORE the if's merge (the early returns make the merge the function's exit join), so the
        // short-circuit flag lowering (TryEmitShortCircuitAsFlag) emitted the else arm first, which took the shared
        // `if (k != 0)` block and the rest of the function as visited; the flagged arm then emitted only its inner phi
        // copies and fell off the end: the result kept its default 0 (WebGPU only; musl's log1pf has exactly this
        // shape: IntrinsicMath.Log1P(-0.5f) returned 0, Data 2026-11-18).
        static int ScConvergeBody(int x)
        {
            int k = 1;
            int f = 0;
            if (x < 100 || x > 1000)
            {
                if (x < -50) return -7;
                if (x > 5000) return x;
                if (x < 0) { k = 0; f = x; }
            }
            else if (x > 500)
                return x * 2;
            if (k != 0) f = x * 3 + 1;
            return f * 2 + k;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int ScConvergeHelper(int x) => ScConvergeBody(x);

        static void ScConvergeNoLoopKernel(Index1D i, ArrayView<int> x, ArrayView<int> o) => o[i] = ScConvergeBody(x[i]);

        static void ScConvergeLoopKernel(Index1D i, ArrayView<int> x, ArrayView<int> o)
        {
            int acc = 0;
            for (int t = 0; t <= (x[i] & 7); t++) acc += t;
            o[i] = ScConvergeBody(x[i]) * 100 + acc;
        }

        static void ScConvergeHelperKernel(Index1D i, ArrayView<int> x, ArrayView<int> o) => o[i] = ScConvergeHelper(x[i]);

        [TestMethod]
        public async Task ShortCircuit_ArmsConvergeBeforeMerge_AllEmitters() => await RunTest(async accelerator =>
        {
            var inputs = new List<int>();
            for (int v = -120; v <= 6200; v += 7) inputs.Add(v);
            inputs.AddRange(new[] { -51, -50, -1, 0, 99, 100, 500, 501, 1000, 1001, 5000, 5001 });
            int n = inputs.Count;
            using var dx = accelerator.Allocate1D(inputs.ToArray());
            using var o = accelerator.Allocate1D<int>(n);
            var failures = new List<string>();
            async Task Check(string shape, Action<Index1D, ArrayView<int>, ArrayView<int>> kernel, Func<int, int> want)
            {
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(kernel);
                k((Index1D)n, dx.View, o.View);
                var got = await o.CopyToHostAsync<int>();
                int bad = 0;
                for (int j = 0; j < n; j++)
                    if (got[j] != want(inputs[j]) && bad++ < 4)
                        failures.Add($"{shape}: x={inputs[j]} got {got[j]} want {want(inputs[j])}");
                if (bad > 4) failures.Add($"{shape}: {bad} of {n} wrong");
            }
            await Check("no-loop kernel", ScConvergeNoLoopKernel, ScConvergeBody);
            await Check("loop kernel", ScConvergeLoopKernel, v => { int acc = 0; for (int t = 0; t <= (v & 7); t++) acc += t; return ScConvergeBody(v) * 100 + acc; });
            await Check("helper", ScConvergeHelperKernel, ScConvergeBody);
            if (failures.Count > 0)
                throw new Exception($"short-circuit arms converging before the merge: {string.Join("; ", failures)}");
        });
    }
}
