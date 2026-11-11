using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // The length of a view passed to a kernel as a host SubView, read INSIDE the kernel (view.Length / IntLength). WebGPU
    // binds a storage buffer from the 256-byte boundary below the view and sizes the binding to include that padding, so
    // arrayLength() of the binding counted the padding as elements: a view not starting on a 256-byte boundary read a
    // Length that much too large (found by ContiguousAccess_ScatterAndGather_MatchHost in the 5.4.28 release sweep:
    // `o[(i * 7) % o.IntLength]` wrote the wrong elements). Offsets on both sides of the boundary (64 ints = 256 bytes),
    // a direct ArrayView and an ArrayView1D (a different binding path), 32- and 64-bit and float elements.
    public abstract partial class BackendTestBase
    {
        static void SubViewLengthIntKernel(Index1D i, ArrayView<int> v, ArrayView<int> o) => o[i] = v.IntLength * 2 + (int)(v.Length & 1);

        static void SubViewLengthLongKernel(Index1D i, ArrayView<long> v, ArrayView<int> o) => o[i] = v.IntLength;

        static void SubViewLengthFloatKernel(Index1D i, ArrayView<float> v, ArrayView<int> o) => o[i] = v.IntLength;

        static void SubViewLength1DKernel(Index1D i, ArrayView1D<int, Stride1D.Dense> v, ArrayView<int> o) => o[i] = v.IntLength;

        [TestMethod]
        public async Task SubViewLength_HostSubViewAtAnyOffset_ReadsItsOwnLength() => await RunTest(async accelerator =>
        {
            const int total = 400, threads = 3;
            using var ints = accelerator.Allocate1D<int>(total);
            using var longs = accelerator.Allocate1D<long>(total);
            using var floats = accelerator.Allocate1D<float>(total);
            using var o = accelerator.Allocate1D<int>(threads);
            var kInt = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(SubViewLengthIntKernel);
            var kLong = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<int>>(SubViewLengthLongKernel);
            var kFloat = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<int>>(SubViewLengthFloatKernel);
            var k1D = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>, ArrayView<int>>(SubViewLength1DKernel);

            async Task Check(string what, int want, int[] got)
            {
                for (int t = 0; t < threads; t++)
                    if (got[t] != want)
                        throw new Exception($"{what}: thread {t} read length {got[t]}, expected {want}");
                await Task.CompletedTask;
            }

            foreach (int offset in new[] { 0, 1, 3, 31, 32, 33, 63, 64, 65 })
                foreach (int length in new[] { 1, 7, 100, total - offset })
                {
                    if (offset + length > total) continue;
                    kInt((Index1D)threads, ints.View.SubView(offset, length), o.View);
                    await Check($"ArrayView<int>.SubView({offset}, {length})", length * 2 + (length & 1), await o.CopyToHostAsync<int>());
                    kLong((Index1D)threads, longs.View.SubView(offset, length), o.View);
                    await Check($"ArrayView<long>.SubView({offset}, {length})", length, await o.CopyToHostAsync<int>());
                    kFloat((Index1D)threads, floats.View.SubView(offset, length), o.View);
                    await Check($"ArrayView<float>.SubView({offset}, {length})", length, await o.CopyToHostAsync<int>());
                    k1D((Index1D)threads, ints.View.SubView(offset, length).AsDense(), o.View);
                    await Check($"ArrayView1D<int>.SubView({offset}, {length})", length, await o.CopyToHostAsync<int>());
                }
        });
    }
}
