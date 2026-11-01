using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A kernel's output must reach its buffer (or the launch must throw) after a long-lived set of buffers has grown twice: allocate a
    // bigger buffer, zero it, device-copy the old rows in, keep the old buffers alive, then ~5,000 small host writes into sub-views, then
    // one-store-per-thread kernel into a FRESH output buffer. On WebGL every output read back as the fresh buffer's zeros with no error
    // (SpawnDev.ILGPU.ML DeviceVectorIndex, Tuvok 2026-11-01); every other backend was right. The sizes are DeviceVectorIndex's: 384-float
    // rows, 1,024 -> 8,192 -> 16,384 slots, 10,000 live.
    public abstract partial class BackendTestBase
    {
        static void GrowthScoreKernel(Index1D i, ArrayView1D<float, Stride1D.Dense> rows, ArrayView1D<float, Stride1D.Dense> query,
            ArrayView1D<int, Stride1D.Dense> valid, ArrayView1D<float, Stride1D.Dense> scores, int dim)
        {
            int s = i;
            float v = -1f;
            if (valid[s] != 0)
            {
                float d = 0f;
                long o = (long)s * dim;
                for (int k = 0; k < dim; k++) d += rows[o + k] * query[k];
                v = d;
            }
            scores[s] = v;
        }

        [TestMethod(Timeout = 600000)]
        public async Task KernelOutput_AfterTwoGrowthsAndManySmallHostWrites_IsWritten() => await RunTest(async accelerator =>
        {
            const int dim = 384, firstBulk = 5000, singles = 5000;
            int cap = 1024, high = 0;
            var retired = new List<IDisposable>();
            var rows = accelerator.Allocate1D<float>((long)cap * dim); rows.MemSetToZero();
            var valid = accelerator.Allocate1D<int>(cap); valid.MemSetToZero();
            var host = new List<float[]>();
            float[] Row(int n) { var r = new float[dim]; var rnd = new Random(n); for (int k = 0; k < dim; k++) r[k] = (float)(rnd.NextDouble() * 2 - 1); return r; }
            void Ensure(int needed)
            {
                if (needed <= cap) return;
                int nc = cap; while (nc < needed) nc *= 2;
                var r2 = accelerator.Allocate1D<float>((long)nc * dim); r2.MemSetToZero();
                var v2 = accelerator.Allocate1D<int>(nc); v2.MemSetToZero();
                r2.View.SubView(0, (long)cap * dim).CopyFrom(rows.View);
                v2.View.SubView(0, cap).CopyFrom(valid.View);
                retired.Add(rows); retired.Add(valid);          // kept alive, as DeviceVectorIndex does
                rows = r2; valid = v2; cap = nc;
            }
            try
            {
                // 1. one bulk add: 5,000 rows (grows 1,024 -> 8,192)
                Ensure(high + firstBulk);
                var bulk = new float[firstBulk * dim];
                for (int n = 0; n < firstBulk; n++) { var r = Row(n); host.Add(r); r.CopyTo(bulk, n * dim); }
                rows.View.SubView((long)high * dim, (long)firstBulk * dim).CopyFromCPU(bulk);
                var ones = new int[firstBulk]; Array.Fill(ones, 1);
                valid.View.SubView(high, firstBulk).CopyFromCPU(ones);
                high += firstBulk;
                // 2. 5,000 single adds, each two small host writes (grows 8,192 -> 16,384 at the 8,193rd)
                for (int n = 0; n < singles; n++)
                {
                    Ensure(high + 1);
                    var r = Row(firstBulk + n); host.Add(r);
                    rows.View.SubView((long)high * dim, dim).CopyFromCPU(r);
                    valid.View.SubView(high, 1).CopyFromCPU(new[] { 1 });
                    high++;
                }
                // 3. score every slot into a FRESH buffer and read it back
                var q = Row(999_999);
                using var query = accelerator.Allocate1D<float>(dim);
                query.View.CopyFromCPU(q);
                using var scores = accelerator.Allocate1D<float>(cap);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<int, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, int>(GrowthScoreKernel);
                kernel(high, rows.View, query.View, valid.View, scores.View, dim);
                var got = await scores.View.SubView(0, high).CopyToHostAsync<float, Stride1D.Dense>();
                int bad = 0, firstBad = -1;
                for (int s = 0; s < high; s++)
                {
                    double want = 0; for (int k = 0; k < dim; k++) want += (double)host[s][k] * q[k];
                    if (Math.Abs(got[s] - want) > 1e-3 * Math.Max(1, Math.Abs(want))) { if (bad++ == 0) firstBad = s; }
                }
                if (bad > 0)
                    throw new Exception($"{bad} of {high} scores wrong (capacity {cap}); first slot {firstBad}: got {got[firstBad]}, expected a dot product");
            }
            finally
            {
                rows.Dispose(); valid.Dispose();
                foreach (var b in retired) b.Dispose();
            }
        });
    }
}
