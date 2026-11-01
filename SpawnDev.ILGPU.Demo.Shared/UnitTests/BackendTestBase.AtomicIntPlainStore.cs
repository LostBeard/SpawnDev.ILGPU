using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // An INT buffer the kernel updates with Atomic.Add AND writes / reads with plain stores and loads: the append idiom
    // (claim a slot with a counter at [0], write the item at [1 + slot]). WebGPU declares the buffer array<atomic<i32>>,
    // so every plain access must be atomicStore / atomicLoad. The uint/float version of this was fixed in 5.4.9
    // (AtomicBuffer_PlainStoreAndLoad_MatchesOracle); the int one emitted `*p = v` - an i32 assigned to an atomic<i32>,
    // which Tint rejects (naga accepts it), so the pipeline was invalid and the launch silently did nothing.
    // Found through SpawnDev.ILGPU.ML's face decode kernel (Tuvok 2026-11-01).
    public abstract partial class BackendTestBase
    {
        static void AtomicIntPlain_AppendKernel(Index1D i, ArrayView1D<int, Stride1D.Dense> buf)
        {
            int slot = Atomic.Add(ref buf[0], 1);
            buf[1 + slot] = i;
        }

        static void AtomicIntPlain_ReadBackKernel(Index1D i, ArrayView1D<int, Stride1D.Dense> buf, ArrayView1D<int, Stride1D.Dense> o)
        {
            // Plain load and plain store of the same atomic-typed buffer, plus one more atomic so it stays atomic.
            int v = buf[1 + i];
            buf[1 + i] = v * 3;
            Atomic.Add(ref buf[0], 0);
            o[i] = buf[1 + i];
        }

        [TestMethod]
        public async Task AtomicIntBuffer_AppendSlotAndPlainStore_MatchesOracle() => await RunTest(async accelerator =>
        {
            const int n = 1000;
            var init = new int[1 + n];
            for (int k = 1; k < init.Length; k++) init[k] = -7;
            using var buf = accelerator.Allocate1D(init);
            using var o = accelerator.Allocate1D<int>(n);
            int[] h, ho;
            try
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>>(AtomicIntPlain_AppendKernel)((Index1D)n, buf.View);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>>(AtomicIntPlain_ReadBackKernel)(
                    (Index1D)n, buf.View, o.View);
                // WebGL compiles at first dispatch / readback, so the readbacks are inside the try too.
                h = await buf.CopyToHostAsync<int>();
                ho = await o.CopyToHostAsync<int>();
            }
            catch (Exception ex) when (accelerator.AcceleratorType == AcceleratorType.WebGL)
            {
                // WebGL has no atomics. Its Atomic.Add emulation cannot return the old value, and here the buffer is also
                // stored to: it must refuse the kernel, not return a counter of 0 (it did, until 5.4.15).
                if (ex is UnsupportedKernelFeatureException)
                    throw new UnsupportedTestException("WebGL rejects a used Atomic.Add result (UnsupportedKernelFeatureException), as it must");
                var chain = new System.Text.StringBuilder();
                for (Exception? e = ex; e != null; e = e.InnerException) chain.Append(e.GetType().FullName).Append(" > ");
                throw new Exception($"WebGL must throw UnsupportedKernelFeatureException itself; got {chain}");
            }
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new Exception("WebGL accepted a kernel that uses Atomic.Add's old value; it must throw UnsupportedKernelFeatureException");
            if (h[0] != n) throw new Exception($"counter = {h[0]}, expected {n} (an invalid pipeline leaves it 0)");
            // Every thread index appears exactly once in [1..n] (order is the race's), then times 3.
            var seen = new bool[n];
            for (int k = 0; k < n; k++)
            {
                int v = h[1 + k];
                if (v % 3 != 0 || v / 3 < 0 || v / 3 >= n) throw new Exception($"slot {k} = {v}, expected 3 * (a thread index)");
                if (seen[v / 3]) throw new Exception($"thread {v / 3} appears twice");
                seen[v / 3] = true;
                if (ho[k] != v) throw new Exception($"read back {k} = {ho[k]}, expected {v}");
            }
        });
    }
}
