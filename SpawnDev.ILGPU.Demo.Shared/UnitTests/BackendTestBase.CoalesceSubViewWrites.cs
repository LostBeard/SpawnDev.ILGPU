using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A kernel parameter written ONLY through a sub-view must not be treated as read-only.
    //
    // Over 10 storage bindings, the WebGPU codegen gathers the parameters a kernel never writes into one COPIED binding
    // (direct-param coalesce). Its write scan resolved each store target with a walk that stopped at a SubViewValue, so
    // `var f = recon.SubView(..); f[i] = x;` counted recon as read-only: the dispatcher gathered it into the copy and every
    // write was lost. SpawnDev.Codecs Av1EncodeFrameBatchStepKernel (per-frame SubViews of its view params): a batch of ONE
    // frame encoded differently from the single-frame kernel on WebGPU only; Vp8EntropyStepKernel lost its partition0
    // output the same way (2026-10-26). These kernels write their outputs only through sub-views (a constant offset, and a per-thread
    // offset), with enough other views to trigger the coalesce. One store site per output, at the thread's own slot.
    public abstract partial class BackendTestBase
    {
        static void SubViewWrittenByteParamsKernel(
            Index1D idx,
            ArrayView<byte> outA, ArrayView<byte> outB,
            ArrayView<byte> v0, ArrayView<byte> v1, ArrayView<byte> v2,
            ArrayView<byte> v3, ArrayView<byte> v4, ArrayView<byte> v5,
            ArrayView<byte> v6, ArrayView<byte> v7, ArrayView<byte> v8)
        {
            int i = idx;
            int sum = v0[i] + v1[i] + v2[i] + v3[i] + v4[i] + v5[i] + v6[i] + v7[i] + v8[i];
            var a = outA.SubView(i, 1);
            a[0] = (byte)sum;
            int off = i & 1;
            outB.SubView(off, outB.Length - off)[i - off] = (byte)(sum ^ 0x5A);
        }

        [TestMethod]
        public async Task Coalesce_ByteParamWrittenOnlyThroughSubView_KeepsWrites() => await RunEmulatedTest(async accelerator =>
        {
            const int len = 256;
            var inputs = new byte[9][];
            var bufs = new MemoryBuffer1D<byte, Stride1D.Dense>[9];
            try
            {
                for (int f = 0; f < 9; f++)
                {
                    inputs[f] = new byte[len];
                    for (int i = 0; i < len; i++) inputs[f][i] = (byte)((f + 3) * 11 + i * (f + 1));
                    bufs[f] = accelerator.Allocate1D(inputs[f]);
                }
                using var outA = accelerator.Allocate1D(new byte[len]);
                using var outB = accelerator.Allocate1D(new byte[len]);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<byte>, ArrayView<byte>,
                    ArrayView<byte>, ArrayView<byte>, ArrayView<byte>,
                    ArrayView<byte>, ArrayView<byte>, ArrayView<byte>,
                    ArrayView<byte>, ArrayView<byte>, ArrayView<byte>>(SubViewWrittenByteParamsKernel);
                kernel((Index1D)len, outA.View, outB.View,
                    bufs[0].View, bufs[1].View, bufs[2].View, bufs[3].View, bufs[4].View,
                    bufs[5].View, bufs[6].View, bufs[7].View, bufs[8].View);
                await accelerator.SynchronizeAsync();
                var a = await outA.CopyToHostAsync<byte>();
                var b = await outB.CopyToHostAsync<byte>();
                for (int i = 0; i < len; i++)
                {
                    int sum = 0;
                    for (int f = 0; f < 9; f++) sum += inputs[f][i];
                    if (a[i] != (byte)sum)
                        throw new Exception($"outA (written through SubView) at {i}: expected {(byte)sum}, got {a[i]}");
                    if (b[i] != (byte)(sum ^ 0x5A))
                        throw new Exception($"outB (written through a SubView at a per-thread offset) at {i}: expected {(byte)(sum ^ 0x5A)}, got {b[i]}");
                }
            }
            finally
            {
                for (int f = 0; f < 9; f++) bufs[f]?.Dispose();
            }
        });

        static void SubViewWrittenIntParamsKernel(
            Index1D idx,
            ArrayView<int> outA, ArrayView<int> outB,
            ArrayView<int> v0, ArrayView<int> v1, ArrayView<int> v2,
            ArrayView<int> v3, ArrayView<int> v4, ArrayView<int> v5,
            ArrayView<int> v6, ArrayView<int> v7, ArrayView<int> v8)
        {
            int i = idx;
            int sum = v0[i] + 2 * v1[i] + 3 * v2[i] + v3[i] - v4[i] + v5[i] + v6[i] + v7[i] + v8[i];
            var a = outA.SubView(i, 1);
            a[0] = sum;
            int off = i & 1;
            outB.SubView(off, outB.Length - off)[i - off] = sum * 7 - 1;
        }

        [TestMethod]
        public async Task Coalesce_IntParamWrittenOnlyThroughSubView_KeepsWrites() => await RunEmulatedTest(async accelerator =>
        {
            const int len = 256;
            var inputs = new int[9][];
            var bufs = new MemoryBuffer1D<int, Stride1D.Dense>[9];
            try
            {
                for (int f = 0; f < 9; f++)
                {
                    inputs[f] = new int[len];
                    for (int i = 0; i < len; i++) inputs[f][i] = (f + 1) * 1000 + i * (f + 2);
                    bufs[f] = accelerator.Allocate1D(inputs[f]);
                }
                using var outA = accelerator.Allocate1D(new int[len]);
                using var outB = accelerator.Allocate1D(new int[len]);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>>(SubViewWrittenIntParamsKernel);
                kernel((Index1D)len, outA.View, outB.View,
                    bufs[0].View, bufs[1].View, bufs[2].View, bufs[3].View, bufs[4].View,
                    bufs[5].View, bufs[6].View, bufs[7].View, bufs[8].View);
                await accelerator.SynchronizeAsync();
                var a = await outA.CopyToHostAsync<int>();
                var b = await outB.CopyToHostAsync<int>();
                for (int i = 0; i < len; i++)
                {
                    int sum = inputs[0][i] + 2 * inputs[1][i] + 3 * inputs[2][i] + inputs[3][i] - inputs[4][i]
                        + inputs[5][i] + inputs[6][i] + inputs[7][i] + inputs[8][i];
                    if (a[i] != sum)
                        throw new Exception($"outA (written through SubView) at {i}: expected {sum}, got {a[i]}");
                    if (b[i] != sum * 7 - 1)
                        throw new Exception($"outB (written through a SubView at a per-thread offset) at {i}: expected {sum * 7 - 1}, got {b[i]}");
                }
            }
            finally
            {
                for (int f = 0; f < 9; f++) bufs[f]?.Dispose();
            }
        });
    }
}
