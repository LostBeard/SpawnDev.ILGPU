using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Sub-word views (byte / short) that start at an element offset which is not a multiple of 4 bytes.
    // WebGPU binds a view at a 256-byte-aligned offset and passes the remainder to the shader.
    public abstract partial class BackendTestBase
    {
        static void SubViewByteReadKernel(Index1D i, ArrayView<byte> src, ArrayView<int> dst) => dst[i] = src[i];
        static void SubViewShortReadKernel(Index1D i, ArrayView<short> src, ArrayView<int> dst) => dst[i] = src[i];
        static void SubViewByteWriteKernel(Index1D i, ArrayView<byte> dst) => dst[i] = (byte)((int)i * 7 + 3);

        [TestMethod]
        public async Task SubWordView_SubViewAtOddOffset_ReadAndWrite() => await RunTest(async accelerator =>
        {
            const int n = 1000;
            var bytes = new byte[n + 300];
            var shorts = new short[n + 300];
            for (int i = 0; i < bytes.Length; i++) { bytes[i] = (byte)(i * 13 + 1); shorts[i] = (short)(i * 97 - 30000); }
            using var bBuf = accelerator.Allocate1D(bytes);
            using var sBuf = accelerator.Allocate1D(shorts);
            using var outBuf = accelerator.Allocate1D<int>(n);
            var kb = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>>(SubViewByteReadKernel);
            var ks = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(SubViewShortReadKernel);

            // 5 and 3 (not multiples of 4 bytes), and 261 (past one 256-byte binding alignment step).
            foreach (int start in new[] { 5, 3, 261 })
            {
                kb(n, bBuf.View.SubView(start, n), outBuf.View);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync();
                for (int i = 0; i < n; i++)
                    if (got[i] != bytes[start + i])
                        throw new Exception($"byte SubView({start}) read on {BackendName}, element {i}: got {got[i]}, expected {bytes[start + i]}");

                ks(n, sBuf.View.SubView(start, n), outBuf.View);
                await accelerator.SynchronizeAsync();
                got = await outBuf.CopyToHostAsync();
                for (int i = 0; i < n; i++)
                    if (got[i] != shorts[start + i])
                        throw new Exception($"short SubView({start}) read on {BackendName}, element {i}: got {got[i]}, expected {shorts[start + i]}");
            }

            // Write through a byte SubView at 5: bytes outside it must be untouched.
            using var wBuf = accelerator.Allocate1D(bytes);
            var kw = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>>(SubViewByteWriteKernel);
            kw(n, wBuf.View.SubView(5, n));
            await accelerator.SynchronizeAsync();
            var w = await wBuf.CopyToHostAsync();
            for (int j = 0; j < w.Length; j++)
            {
                byte want = j >= 5 && j < 5 + n ? (byte)((j - 5) * 7 + 3) : bytes[j];
                if (w[j] != want)
                    throw new Exception($"byte SubView(5) write on {BackendName}, byte {j}: got {w[j]}, expected {want}");
            }

            Console.WriteLine($"[SubWordView] byte/short SubViews at 3, 5, 261 on {BackendName}: read + write ✓");
        });
    }
}
