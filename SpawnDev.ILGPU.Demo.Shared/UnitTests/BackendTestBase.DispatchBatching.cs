using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Dispatch batching (WebGPUBackend.EnableDispatchBatching): on WebGPU a plain dispatch is recorded as numeric
    // records and the whole batch - scalar uploads, clears, coalesce copies, passes - is submitted in ONE JS
    // crossing (wwwroot/webgpuDispatchPlan.js submitBatch). These tests pin the two things that can go wrong with
    // that: the ORDER of queue work, and the bytes each dispatch binds. Every other backend runs the same bodies
    // as ordinary regression tests.
    public abstract partial class BackendTestBase
    {
        static void BatchAxpbIntKernel(Index1D i, ArrayView<int> x, ArrayView<int> y, int a, int b)
            => y[i] = a * x[i] + b;

        // One thread per row (no atomics - WebGL has none), reading a 2D view: its stride buffer is a per-dispatch upload.
        // `cols` is a scalar on purpose: this test is about batching, and Wasm's 2D IntExtent is its own bug
        // (View2D_RowLoop_ExtentAndElements).
        static void BatchRowSum2DKernel(Index1D r, ArrayView2D<int, Stride2D.DenseX> m, ArrayView<int> y, int cols)
        {
            int sum = 0;
            for (int c = 0; c < cols; c++) sum += m[r, c] * (c + 1);
            y[r] = sum;
        }

        static void BatchFillKernel(Index1D i, ArrayView<int> v, int baseValue) => v[i] = baseValue + i;

        // Per row: the 2D extent the kernel sees and each element it reads - 7 values at the positional r*7+slot
        // layout (WebGL's one-record-per-thread contract). MEASURED 2026-09-30: Wasm returns the view's TOTAL length
        // (185) for BOTH IntExtent.X and IntExtent.Y - its launcher passes (ptr, length, stride, stride2) per view and
        // the codegen answers every extent field from `length`. Elements themselves are read correctly.
        static void View2DProbeKernel(Index1D r, ArrayView2D<int, Stride2D.DenseX> m, ArrayView<int> outp)
        {
            outp[r * 7] = m.IntExtent.Y;
            outp[r * 7 + 1] = m.IntExtent.X;
            for (int c = 0; c < 5; c++) outp[r * 7 + 2 + c] = m[r, c];
        }

        [TestMethod]
        public async Task View2D_RowLoop_ExtentAndElements() => await RunTest(async accelerator =>
        {
            const int rows = 37, cols = 5;
            using var mBuf = accelerator.Allocate2DDenseX<int>(new LongIndex2D(rows, cols));
            using var outBuf = accelerator.Allocate1D<int>(rows * 7);
            var fill = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(BatchFillKernel);
            var probe = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView2D<int, Stride2D.DenseX>, ArrayView<int>>(View2DProbeKernel);
            fill(rows * cols, mBuf.View.AsContiguous(), 11);
            await accelerator.SynchronizeAsync();
            probe(rows, mBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            var errs = new List<string>();
            for (int r = 0; r < rows && errs.Count < 6; r++)
            {
                var exp = new int[] { cols, rows, 11 + r, 11 + r + rows, 11 + r + 2 * rows, 11 + r + 3 * rows, 11 + r + 4 * rows };
                for (int k = 0; k < 7; k++)
                    if (got[r * 7 + k] != exp[k]) { errs.Add($"row {r}: [{string.Join(",", got.Skip(r * 7).Take(7))}] exp [{string.Join(",", exp)}]"); break; }
            }
            if (errs.Count > 0) throw new Exception(string.Join(" | ", errs));
        });

        /// <summary>
        /// A long chain of dispatches whose result depends on their ORDER: overlapping writes through sub-views at
        /// offsets that are not 256-aligned (so the view-offset scalars matter), a per-dispatch scalar pair, clears
        /// between dispatches, and a 2D view (its stride buffer is a per-dispatch upload too). Everything is
        /// submitted by a single SynchronizeAsync, so on WebGPU it is one batch. Run with batching ON and OFF; both
        /// must equal the CPU reference exactly (integers - no rounding to excuse a difference).
        /// </summary>
        [TestMethod]
        public async Task DispatchBatching_OrderedChain_MatchesReference() => await RunTest(async accelerator =>
        {
            bool prev = WebGPUBackend.EnableDispatchBatching;
            try
            {
                foreach (bool batching in new[] { true, false })
                {
                    WebGPUBackend.EnableDispatchBatching = batching;
                    await RunOrderedChain(accelerator, batching);
                }
            }
            finally
            {
                WebGPUBackend.EnableDispatchBatching = prev;
            }
        });

        async Task RunOrderedChain(Accelerator accelerator, bool batching)
        {
            const int n = 4096, len = 100, rows = 37, cols = 5;
            var x = new int[n];
            for (int i = 0; i < n; i++) x[i] = (i * 7919) % 1000 - 500;
            var expected = new int[n];

            using var xBuf = accelerator.Allocate1D(x);
            using var yBuf = accelerator.Allocate1D<int>(n);
            using var mBuf = accelerator.Allocate2DDenseX<int>(new LongIndex2D(rows, cols));
            var axpb = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int, int>(BatchAxpbIntKernel);
            var rowSum = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView2D<int, Stride2D.DenseX>, ArrayView<int>, int>(BatchRowSum2DKernel);
            var fill = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(BatchFillKernel);

            // MemSetToZeroAsync is the cross-backend contract ("ordered after prior kernels"); on WebGPU it is still a
            // clearBuffer recorded into the stream - i.e. a clear record in the middle of the batch - with no flush.
            await yBuf.View.MemSetToZeroAsync(accelerator.DefaultStream);
            for (int k = 0; k < 48; k++)
            {
                int off = 7 * k + 3, xOff = off + 5, a = k - 20, b = 3 * k + 1;
                axpb(len, xBuf.View.SubView(xOff, len), yBuf.View.SubView(off, len), a, b);
                for (int i = 0; i < len; i++) expected[off + i] = a * x[xOff + i] + b;
                if (k % 11 == 10)
                {
                    // A clear BETWEEN dispatches: must land after the writes before it and before the ones after it.
                    int cOff = 7 * (k - 4), cLen = 64;
                    await yBuf.View.SubView(cOff, cLen).MemSetToZeroAsync(accelerator.DefaultStream);
                    for (int i = 0; i < cLen; i++) expected[cOff + i] = 0;
                }
            }
            // 2D view (stride upload) reading a matrix produced by a fill still pending in the same batch.
            fill(rows * cols, mBuf.View.AsContiguous(), 11);
            int rowsBase = 2000;
            rowSum(rows, mBuf.View, yBuf.View.SubView(rowsBase, rows), cols);
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    expected[rowsBase + r] += (11 + r + c * rows) * (c + 1);   // DenseX: X is the contiguous axis

            await accelerator.SynchronizeAsync();
            var got = await yBuf.CopyToHostAsync<int>();
            // Report every mismatching RUN, not just the first element: an ordering fault and an offset fault
            // look identical at one index and completely different as a list of ranges.
            var runs = new List<string>();
            int bad = 0;
            for (int i = 0; i < n; i++)
            {
                if (got[i] == expected[i]) continue;
                int start = i;
                while (i + 1 < n && got[i + 1] != expected[i + 1]) i++;
                bad += i - start + 1;
                if (runs.Count < 12) runs.Add($"[{start}..{i}] got {got[start]} exp {expected[start]}");
            }
            if (bad > 0)
                throw new Exception($"batching={batching}: {bad} of {n} wrong in {runs.Count}+ runs: {string.Join("; ", runs)}");
        }

        static void BatchCopyKernel(Index1D i, ArrayView<int> src, ArrayView<int> dst) => dst[i] = src[i];

        /// <summary>
        /// Disposing a buffer whose MemSetToZero is still PENDING (recorded, not yet submitted) must not break the NEXT,
        /// unrelated submit. Reported by Tuvok 2026-09-30 from SpawnScene: allocate, MemSetToZero, Dispose before any
        /// flush, then dispatch anything -> "Buffer ... used in submit while destroyed". Both the record batch and the
        /// per-dispatch encoder hold the clear until submit; Dispose must submit first.
        /// </summary>
        [TestMethod]
        public async Task Dispose_WithPendingClear_DoesNotBreakNextSubmit() => await RunTest(async accelerator =>
        {
            bool prev = WebGPUBackend.EnableDispatchBatching;
            try
            {
                foreach (bool batching in new[] { true, false })
                {
                    WebGPUBackend.EnableDispatchBatching = batching;
                    await accelerator.SynchronizeAsync();   // start from nothing pending
                    var doomed = accelerator.Allocate1D<int>(1);
                    doomed.MemSetToZero();                  // recorded, not submitted
                    doomed.Dispose();                       // before any flush
                    using var src = accelerator.Allocate1D(new[] { 7, 8, 9 });
                    using var dst = accelerator.Allocate1D<int>(3);
                    var copy = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(BatchCopyKernel);
                    copy(3, src.View, dst.View);            // the unrelated dispatch
                    await accelerator.SynchronizeAsync();
                    var got = await dst.CopyToHostAsync<int>();
                    if (got[0] != 7 || got[1] != 8 || got[2] != 9)
                        throw new Exception($"batching={batching}: unrelated dispatch after the dispose read [{string.Join(",", got)}]");
                }
            }
            finally
            {
                WebGPUBackend.EnableDispatchBatching = prev;
            }
        });

        /// <summary>
        /// Host uploads between dispatches must land exactly where they are issued: dispatch 1 reads A, a small
        /// CopyFromCPU rewrites A, dispatch 2 reads A - repeated several times with no sync in between, so on WebGPU
        /// the whole sequence (reads, uploads, reads) is one batch and the uploads are ordered records (a staging copy
        /// each), not flushes. Every read must see exactly the upload before it. Batching ON and OFF.
        /// </summary>
        [TestMethod]
        public async Task DispatchBatching_HostUploadsBetweenDispatches_AreOrdered() => await RunTest(async accelerator =>
        {
            bool prev = WebGPUBackend.EnableDispatchBatching;
            try
            {
                foreach (bool batching in new[] { true, false })
                {
                    WebGPUBackend.EnableDispatchBatching = batching;
                    const int n = 96, rounds = 6;
                    using var a = accelerator.Allocate1D<int>(n);
                    var outs = new MemoryBuffer1D<int, Stride1D.Dense>[rounds];
                    var copy = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(BatchCopyKernel);
                    try
                    {
                        for (int r = 0; r < rounds; r++) outs[r] = accelerator.Allocate1D<int>(n);
                        await accelerator.SynchronizeAsync();
                        for (int r = 0; r < rounds; r++)
                        {
                            var data = new int[n];
                            for (int i = 0; i < n; i++) data[i] = r * 1000 + i;
                            a.View.CopyFromCPU(data);          // must land AFTER the previous round's read of `a`
                            copy(n, a.View, outs[r].View);     // must read THIS round's upload
                        }
                        await accelerator.SynchronizeAsync();
                        for (int r = 0; r < rounds; r++)
                        {
                            var got = await outs[r].CopyToHostAsync<int>();
                            for (int i = 0; i < n; i++)
                                if (got[i] != r * 1000 + i)
                                    throw new Exception($"batching={batching}: round {r} read [{i}] = {got[i]}, expected {r * 1000 + i}");
                        }
                    }
                    finally
                    {
                        foreach (var o in outs) o?.Dispose();
                    }
                }
            }
            finally
            {
                WebGPUBackend.EnableDispatchBatching = prev;
            }
        });

        /// <summary>
        /// A coalesced kernel (13 views, over the 10-binding limit) whose inputs were ALL produced by dispatches that
        /// are still pending. The coalesce gather must run after them. It used to be a separate command encoder
        /// submitted on the spot, i.e. BEFORE everything still pending in the stream - so it gathered the inputs'
        /// previous contents (here: zeros).
        /// </summary>
        [TestMethod]
        public async Task Coalesce_InputsFromPendingDispatches_ReadFreshData() => await RunEmulatedTest(async accelerator =>
        {
            const int len = 256;
            var bufs = new MemoryBuffer1D<int, Stride1D.Dense>[13];
            try
            {
                for (int f = 0; f < 13; f++) bufs[f] = accelerator.Allocate1D<int>(len);
                using var output = accelerator.Allocate1D<int>(len);
                var fill = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(BatchFillKernel);
                var sum = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>>(DirectParamSumKernel);
                // make sure the zero-init is DONE, so a stale gather reads zeros rather than racing
                await accelerator.SynchronizeAsync();

                for (int f = 0; f < 13; f++) fill(len, bufs[f].View, (f + 1) * 100000);
                sum((Index1D)len, output.View,
                    bufs[0].View, bufs[1].View, bufs[2].View,
                    bufs[3].View, bufs[4].View, bufs[5].View,
                    bufs[6].View, bufs[7].View, bufs[8].View,
                    bufs[9].View, bufs[10].View, bufs[11].View,
                    bufs[12].View);
                await accelerator.SynchronizeAsync();
                var result = await output.CopyToHostAsync<int>();
                for (int i = 0; i < len; i++)
                {
                    int expected = 0;
                    for (int f = 0; f < 13; f++) expected += (f + 1) * 100000 + i;
                    if (result[i] != expected)
                        throw new Exception($"coalesce after pending fills: i={i} expected {expected}, got {result[i]}");
                }
            }
            finally
            {
                for (int f = 0; f < 13; f++) bufs[f]?.Dispose();
            }
        });
    }
}
