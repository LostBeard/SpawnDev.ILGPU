using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Long chains of DEPENDENT launches queued with no synchronize in between - the training-loop pattern
    // (SpawnDev.ILGPU.ML's Snake trainer: ~35 launches per step, a sync every 8 steps). Each launch reads what
    // the previous one wrote, small and large dispatches interleave, and host writes land in the middle of
    // the queue. Written 2026-09-28 as the gate for Wasm dispatch batching (several queued small launches run
    // by one worker message): a batcher that reorders, merges, drops or double-runs a launch, or lets a launch
    // see a host write queued after it, fails here. Checked against a host replay of the same sequence.
    public abstract partial class BackendTestBase
    {
        static void QdoAddKernel(Index1D i, ArrayView<float> x, float v) => x[i] = x[i] + v;
        static void QdoMulAddKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst, float m) => dst[i] = src[i] * m + dst[i];
        static void QdoCopyKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst) => dst[i] = src[i];

        [TestMethod]
        public async Task QueuedDispatches_DependentChainWithHostWrites_ExactOrder() => await RunTest(async accelerator =>
        {
            const int small = 37, large = 40000;
            var add = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(QdoAddKernel);
            var mulAdd = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float>(QdoMulAddKernel);
            var copy = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(QdoCopyKernel);

            var hx = new float[large]; var hy = new float[large]; var hz = new float[small];
            for (int i = 0; i < large; i++) { hx[i] = i % 5; hy[i] = 1f; }
            using var x = accelerator.Allocate1D(hx);
            using var y = accelerator.Allocate1D(hy);
            using var z = accelerator.Allocate1D<float>(small);
            using var snapshot = accelerator.Allocate1D<float>(small);

            // Host replay of every step, in the same order.
            void HAdd(float[] a, int n, float v) { for (int i = 0; i < n; i++) a[i] += v; }
            void HMulAdd(float[] s, float[] d, int n, float m) { for (int i = 0; i < n; i++) d[i] = s[i] * m + d[i]; }

            // 1) 200 small in-place increments, each depending on the last; every 25th step a large one too.
            for (int step = 0; step < 200; step++)
            {
                add(small, x.View, 1f); HAdd(hx, small, 1f);
                if (step % 25 == 0) { mulAdd(large, x.View, y.View, 0.5f); HMulAdd(hx, hy, large, 0.5f); }
            }
            // 2) A launch reading the chain's result, queued before a host write to its source.
            copy(small, x.View.SubView(0, small), snapshot.View);
            var hsnap = new float[small]; Array.Copy(hx, hsnap, small);

            // 3) Host write to x while launches are still queued: launches queued BEFORE it (the copy
            //    above) must see the old values, launches queued AFTER it the new ones.
            var fresh = new float[large];
            for (int i = 0; i < large; i++) fresh[i] = 1000f + i % 7;
            x.View.CopyFromCPU(fresh);
            Array.Copy(fresh, hx, large);

            // 4) 150 more small dependent launches on the new data, into z and back.
            for (int step = 0; step < 150; step++)
            {
                add(small, x.View, 0.25f); HAdd(hx, small, 0.25f);
                mulAdd(small, x.View, y.View, 2f); HMulAdd(hx, hy, small, 2f);
            }
            copy(small, y.View.SubView(0, small), z.View);
            var hz2 = new float[small]; Array.Copy(hy, hz2, small);
            await accelerator.SynchronizeAsync();

            var gx = await x.CopyToHostAsync<float>();
            var gy = await y.CopyToHostAsync<float>();
            var gz = await z.CopyToHostAsync<float>();
            var gs = await snapshot.CopyToHostAsync<float>();
            for (int i = 0; i < small; i++)
            {
                if (gs[i] != hsnap[i]) throw new Exception($"snapshot (queued before the host write) [{i}] = {gs[i]}, expected {hsnap[i]}");
                if (gz[i] != hz2[i]) throw new Exception($"final copy [{i}] = {gz[i]}, expected {hz2[i]}");
            }
            for (int i = 0; i < large; i++)
            {
                if (gx[i] != hx[i]) throw new Exception($"x[{i}] = {gx[i]}, expected {hx[i]}");
                if (gy[i] != hy[i]) throw new Exception($"y[{i}] = {gy[i]}, expected {hy[i]}");
            }
        });
    }
}
