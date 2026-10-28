using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A synchronous MemSetToZero between two launches must act between them: after the kernel queued before it, before
    // the kernel queued after it. Wasm filled the SharedArrayBuffer at once, under a kernel still running, so the first
    // kernel's values landed on top of the zeros. Found through SpawnDev.Codecs (Geordi 2026-10-28): the WebP lossless
    // decoder zeroes its state at the start of each decode while the previous decode's kernels are in flight, and every
    // second decode on a reused decoder read garbage error codes (0x30010). Every backend, against a host oracle.
    public abstract partial class BackendTestBase
    {
        // Slow enough to still run when the host reaches the memset: the loop's result is always 0 but cannot be folded.
        static void MemSetOrdering_FillKernel(Index1D i, ArrayView<int> buf, int value, int spin)
        {
            int acc = 0;
            for (int k = 0; k < spin; k++)
                acc = (acc * 1103515245 + k + i) & 0x7FFFFFFF;
            buf[i] = value + (acc < 0 ? 1 : 0);
        }

        static void MemSetOrdering_AddKernel(Index1D i, ArrayView<int> buf) => buf[i] += 1;

        [TestMethod]
        public async Task MemSetToZero_BetweenLaunches_IsOrdered() => await RunTest(async accelerator =>
        {
            const int n = 1 << 16, spin = 4000;
            using var buf = accelerator.Allocate1D<int>(n);
            var fill = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int>(MemSetOrdering_FillKernel);
            var add = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(MemSetOrdering_AddKernel);
            for (int round = 0; round < 3; round++)
            {
                // Whole buffer: 5 everywhere, then zeroed, then + 1 -> 1. A memset that ran too early gives 6.
                fill((Index1D)n, buf.View, 5, spin);
                buf.View.MemSetToZero();
                add((Index1D)n, buf.View);
                await Check(round, "whole buffer", i => 1);
                // A sub-view (the decoder zeroes a sub-view of its state buffer): only the middle half is zeroed.
                fill((Index1D)n, buf.View, 7, spin);
                buf.View.SubView(n / 4, n / 2).MemSetToZero();
                add((Index1D)n, buf.View);
                await Check(round, "sub-view", i => i >= n / 4 && i < n / 4 + n / 2 ? 1 : 8);
            }

            async Task Check(int round, string what, Func<int, int> want)
            {
                await accelerator.SynchronizeAsync();
                var got = await buf.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                    if (got[i] != want(i))
                        throw new Exception($"round {round}, {what}: buf[{i}] = {got[i]}, expected {want(i)} (the memset did not run between the launches)");
            }
        });
    }
}
