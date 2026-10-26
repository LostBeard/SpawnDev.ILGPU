using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Loads through a sub-view cut INSIDE the kernel at a nonzero offset.
    //
    // WebGL dropped the sub-view's offset: the GLSL element address took only the index, so `input.SubView(4, 8)[i]`
    // read input[i] instead of input[4 + i] (silent wrong values - a sub-view passed as a kernel ARGUMENT was right,
    // its offset rides in u_paramN_offset). The GLSL generator now adds every sub-view offset of the chain at the
    // element address, like the WGSL generator. Int views, a nested sub-view, and a byte (sub-word) view.
    public abstract partial class BackendTestBase
    {
        static void SubViewLoad_IntKernel(Index1D idx, ArrayView<int> output, ArrayView<int> input)
        {
            var s = input.SubView(4, 8);
            output[idx] = s[idx % 8] * 10 + (int)s.Length;
        }

        static void SubViewLoad_NestedKernel(Index1D idx, ArrayView<int> output, ArrayView<int> input, int start)
        {
            var outer = input.SubView(start, 20);
            var inner = outer.SubView(3, 6);
            output[idx] = inner[idx % 6];
        }

        static void SubViewLoad_ByteKernel(Index1D idx, ArrayView<int> output, ArrayView<byte> input)
        {
            var s = input.SubView(5, 7);
            output[idx] = s[idx % 7];
        }

        [TestMethod]
        public async Task SubViewLoad_InKernelOffset_ReadsTheOffsetElements() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 64, start = 9;
            var ints = new int[40];
            for (int i = 0; i < ints.Length; i++) ints[i] = i * 7 - 50;
            var bytes = new byte[24];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 11 + 3);
            using var dInts = accelerator.Allocate1D(ints);
            using var dBytes = accelerator.Allocate1D(bytes);
            using var dA = accelerator.Allocate1D<int>(n);
            using var dB = accelerator.Allocate1D<int>(n);
            using var dC = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(SubViewLoad_IntKernel)((Index1D)n, dA.View, dInts.View);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(SubViewLoad_NestedKernel)((Index1D)n, dB.View, dInts.View, start);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<byte>>(SubViewLoad_ByteKernel)((Index1D)n, dC.View, dBytes.View);
            await accelerator.SynchronizeAsync();
            var a = await dA.CopyToHostAsync<int>();
            var b = await dB.CopyToHostAsync<int>();
            var c = await dC.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (a[i] != ints[4 + i % 8] * 10 + 8)
                    throw new Exception($"int sub-view: output[{i}] = {a[i]}, expected {ints[4 + i % 8] * 10 + 8}");
                if (b[i] != ints[start + 3 + i % 6])
                    throw new Exception($"nested sub-view: output[{i}] = {b[i]}, expected {ints[start + 3 + i % 6]}");
                if (c[i] != bytes[5 + i % 7])
                    throw new Exception($"byte sub-view: output[{i}] = {c[i]}, expected {bytes[5 + i % 7]}");
            }
        });
    }
}
