using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Thread-indexed loads and stores (view[i]): Velocity loads and stores a warp whose lanes are all active and address
    // consecutive elements as ONE vector access instead of one scalar access per lane. These tests pin what that fast
    // path must keep: the last, partly filled warp of a launch must not touch the elements past the end of the view;
    // views that start at an odd element (an unaligned vector address) read and write the right elements; inactive lanes
    // of a divergent branch must not be written even though the warp's addresses are consecutive; non-consecutive
    // addresses (a scatter and a gather) still take the per-lane path. Every check runs on every backend.
    public abstract partial class BackendTestBase
    {
        const int ContiguousCanary = -7;

        static void ContiguousInt32Kernel(Index1D i, ArrayView<int> d, ArrayView<int> o) => o[i] = d[i] * 3 + 1;

        static void ContiguousInt64Kernel(Index1D i, ArrayView<long> d, ArrayView<long> o) => o[i] = d[i] * 5 - (1L << 40);

        static void ContiguousDivergentKernel(Index1D i, ArrayView<int> d, ArrayView<int> o)
        {
            // consecutive addresses, but only the odd values' lanes are active
            if ((d[i] & 1) != 0) o[i] = d[i];
        }

        static void ContiguousScatterGatherKernel(Index1D i, ArrayView<int> d, ArrayView<int> o) =>
            o[(i * 7) % o.IntLength] = d[(i * 13) % d.IntLength];

        static int[] ContiguousData(int n, int seed)
        {
            var rnd = new Random(seed);
            var data = new int[n];
            for (int k = 0; k < n; k++) data[k] = rnd.Next(-100000, 100000);
            return data;
        }

        // Runs the kernel over views starting at `offset` inside buffers with `pad` canaries on both sides and returns
        // the whole output buffer; throws when a canary outside the view changed.
        async Task<T[]> ContiguousRun<T>(Accelerator accelerator, Action<Index1D, ArrayView<T>, ArrayView<T>> kernelMethod,
            T[] data, int offset, T canary, T[]? initialOut, string name) where T : unmanaged, IEquatable<T>
        {
            const int pad = 19;
            int n = data.Length;
            var inHost = new T[offset + n + pad];
            var outHost = new T[offset + n + pad];
            Array.Fill(inHost, canary);
            Array.Fill(outHost, canary);
            Array.Copy(data, 0, inHost, offset, n);
            if (initialOut != null) Array.Copy(initialOut, 0, outHost, offset, n);
            using var dIn = accelerator.Allocate1D(inHost);
            using var dOut = accelerator.Allocate1D(outHost);
            var kernel = accelerator.LoadAutoGroupedStreamKernel(kernelMethod);
            kernel((Index1D)n, dIn.View.SubView(offset, n), dOut.View.SubView(offset, n));
            var got = await dOut.CopyToHostAsync<T>();
            for (int k = 0; k < got.Length; k++)
                if ((k < offset || k >= offset + n) && !got[k].Equals(canary))
                    throw new Exception($"{name} (view at {offset}, {n} elements): element {k} outside the view was written ({got[k]})");
            return got;
        }

        [TestMethod]
        public async Task ContiguousAccess_Int32_PartialWarpAndOddStart() => await RunTest(async accelerator =>
        {
            foreach (int n in new[] { 1, 3, 1000, 1027 })
                foreach (int offset in new[] { 0, 1, 3 })
                {
                    var data = ContiguousData(n, 1111 + n + offset);
                    var got = await ContiguousRun(accelerator, ContiguousInt32Kernel, data, offset, ContiguousCanary, null, nameof(ContiguousInt32Kernel));
                    for (int k = 0; k < n; k++)
                        if (got[offset + k] != data[k] * 3 + 1)
                            throw new Exception($"Int32 (view at {offset}, {n} elements): o[{k}] = {got[offset + k]}, expected {data[k] * 3 + 1}");
                }
        });

        [TestMethod]
        public async Task ContiguousAccess_Int64_PartialWarpAndOddStart() => await RunTest(async accelerator =>
        {
            foreach (int n in new[] { 1, 5, 1000, 1027 })
                foreach (int offset in new[] { 0, 1, 3 })
                {
                    var data = Array.ConvertAll(ContiguousData(n, 2222 + n + offset), v => (long)v * 1000003L);
                    var got = await ContiguousRun(accelerator, ContiguousInt64Kernel, data, offset, (long)ContiguousCanary, null, nameof(ContiguousInt64Kernel));
                    for (int k = 0; k < n; k++)
                    {
                        long want = data[k] * 5 - (1L << 40);
                        if (got[offset + k] != want)
                            throw new Exception($"Int64 (view at {offset}, {n} elements): o[{k}] = {got[offset + k]}, expected {want}");
                    }
                }
        });

        [TestMethod]
        public async Task ContiguousAccess_DivergentLanes_InactiveLanesNotWritten() => await RunTest(async accelerator =>
        {
            const int n = 1000;
            const int untouched = 123456789;
            foreach (int offset in new[] { 0, 3 })
            {
                var data = ContiguousData(n, 3333 + offset);
                // runs of all-odd and all-even values too, so some warps are fully active and some fully inactive
                for (int k = 0; k < 64; k++) data[k] |= 1;
                for (int k = 64; k < 128; k++) data[k] &= ~1;
                var initial = new int[n];
                Array.Fill(initial, untouched);
                var got = await ContiguousRun(accelerator, ContiguousDivergentKernel, data, offset, ContiguousCanary, initial, nameof(ContiguousDivergentKernel));
                for (int k = 0; k < n; k++)
                {
                    int want = (data[k] & 1) != 0 ? data[k] : untouched;
                    if (got[offset + k] != want)
                        throw new Exception($"Divergent (view at {offset}): o[{k}] = {got[offset + k]}, expected {want} (value {data[k]})");
                }
            }
        });

        // Every lane reads the SAME element (a uniform index, like a matrix row value shared by the warp): Velocity loads it
        // once for the whole warp. Divergent lanes must still not be written.
        static void BroadcastInt32Kernel(Index1D i, ArrayView<int> d, ArrayView<int> o, int k)
        {
            int v = d[k];
            if (((d[i] ^ v) & 1) != 0) o[i] = v + i;
        }

        static void BroadcastInt64Kernel(Index1D i, ArrayView<long> d, ArrayView<long> o, int k) => o[i] = d[k] * 3 + d[i];

        [TestMethod]
        public async Task ContiguousAccess_BroadcastLoads_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 1000, untouched = 222222;
            var data = ContiguousData(n, 5555);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(BroadcastInt32Kernel);
            using var dIn = accelerator.Allocate1D(data);
            using var dOut = accelerator.Allocate1D<int>(n);
            foreach (int k in new[] { 0, 7, 999 })
            {
                var init = new int[n];
                Array.Fill(init, untouched);
                dOut.CopyFromCPU(init);
                kernel((Index1D)n, dIn.View, dOut.View, k);
                var got = await dOut.CopyToHostAsync<int>();
                for (int t = 0; t < n; t++)
                {
                    int want = ((data[t] ^ data[k]) & 1) != 0 ? data[k] + t : untouched;
                    if (got[t] != want)
                        throw new Exception($"Broadcast int32 (k = {k}): o[{t}] = {got[t]}, expected {want}");
                }
            }
            var data64 = Array.ConvertAll(data, v => (long)v * 7919L);
            var kernel64 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>, int>(BroadcastInt64Kernel);
            using var dIn64 = accelerator.Allocate1D(data64);
            using var dOut64 = accelerator.Allocate1D<long>(n);
            kernel64((Index1D)n, dIn64.View, dOut64.View, 13);
            var got64 = await dOut64.CopyToHostAsync<long>();
            for (int t = 0; t < n; t++)
                if (got64[t] != data64[13] * 3 + data64[t])
                    throw new Exception($"Broadcast int64: o[{t}] = {got64[t]}, expected {data64[13] * 3 + data64[t]}");
        });

        [TestMethod]
        public async Task ContiguousAccess_ScatterAndGather_MatchHost() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("Each thread writes a computed index (scatter stores); WebGL Transform Feedback captures one record per thread at its own slot (RequiresScatterStores).");
            const int n = 1000;   // coprime with 7 and 13: every element written once
            var data = ContiguousData(n, 4444);
            var got = await ContiguousRun(accelerator, ContiguousScatterGatherKernel, data, 1, ContiguousCanary, null, nameof(ContiguousScatterGatherKernel));
            for (int i = 0; i < n; i++)
            {
                int k = (i * 7) % n, want = data[(i * 13) % n];
                if (got[1 + k] != want)
                    throw new Exception($"Scatter/gather: o[{k}] = {got[1 + k]}, expected {want}");
            }
        });
    }
}
