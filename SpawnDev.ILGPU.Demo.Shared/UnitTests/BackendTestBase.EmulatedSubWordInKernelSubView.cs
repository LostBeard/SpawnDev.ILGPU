using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Half = ILGPU.Half;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Sub-views of the emulated 1- and 2-byte float types and of packed 4-bit views taken INSIDE a kernel
    // (`view.SubView(off, n)[i]`), the in-kernel twin of EmulatedSubWordView_SubViewArguments_ReadAndWriteInPlace (which covers
    // sub-views passed as arguments). OpenCL computed an emulated Half element address as `&base[idx]` on its `float*` view
    // field: a Load / Store of that address was right (it is emitted from the remembered base + index), but an address that
    // became the BASE of another one carried a doubled offset, so element i of an in-kernel sub-view at off was element
    // 2 * off + i. Packed 4-bit addresses used the nibble index as a byte index. Found by Tuvok (2026-11-02): SpawnDev.ILGPU.ML's
    // batch decoder slices its F16 KV cache per sequence in the kernel, and on OpenCL (NVIDIA: no cl_khr_fp16) batched steps
    // produced NaN logits and out-of-range reads.
    public abstract partial class BackendTestBase
    {
        static void HalfInKernelSubViewKernel(Index1D i, ArrayView<Half> src, ArrayView<Half> dst, int off, int n)
        {
            var s = src.SubView(off, n); var d = dst.SubView(off, n);
            d[i] = (Half)((float)s[i] + 1f);
        }
        static void BFloat16InKernelSubViewKernel(Index1D i, ArrayView<BFloat16> src, ArrayView<BFloat16> dst, int off, int n)
        {
            var s = src.SubView(off, n); var d = dst.SubView(off, n);
            d[i] = (BFloat16)((float)s[i] + 1f);
        }
        static void Float8E4M3InKernelSubViewKernel(Index1D i, ArrayView<Float8E4M3> src, ArrayView<Float8E4M3> dst, int off, int n)
        {
            var s = src.SubView(off, n); var d = dst.SubView(off, n);
            d[i] = (Float8E4M3)((float)s[i] + 1f);
        }
        static void Float8E5M2InKernelSubViewKernel(Index1D i, ArrayView<Float8E5M2> src, ArrayView<Float8E5M2> dst, int off, int n)
        {
            var s = src.SubView(off, n); var d = dst.SubView(off, n);
            d[i] = (Float8E5M2)((float)s[i] + 1f);
        }
        // Packed 4-bit: LOAD only (an in-kernel packed store is fail-loud on CPU and WebGL by design).
        static void QUInt4InKernelSubViewKernel(Index1D i, ArrayView<QUInt4> src, ArrayView<int> dst, int off, int n)
        {
            var s = src.SubView(off, n);
            dst[i] = s[i];
        }

        [TestMethod]
        public async Task EmulatedSubWordView_InKernelSubView_ReadAndWriteInPlace() => await RunTest(async accelerator =>
        {
            await CheckEmulatedInKernelSubView<Half>(accelerator, "Half", HalfInKernelSubViewKernel, v => (Half)v, v => (float)v);
            await CheckEmulatedInKernelSubView<BFloat16>(accelerator, "BFloat16", BFloat16InKernelSubViewKernel, v => (BFloat16)v, v => (float)v);
            await CheckEmulatedInKernelSubView<Float8E4M3>(accelerator, "Float8E4M3", Float8E4M3InKernelSubViewKernel, v => (Float8E4M3)v, v => (float)v);
            await CheckEmulatedInKernelSubView<Float8E5M2>(accelerator, "Float8E5M2", Float8E5M2InKernelSubViewKernel, v => (Float8E5M2)v, v => (float)v);
            Console.WriteLine($"[EmulatedSubWordView] Half / BFloat16 / FP8 in-kernel SubView at 261 and 1000 on {BackendName} ✓");
        });

        [TestMethod]
        public async Task PackedQUInt4_InKernelSubView_ReadsItsOwnElements() => await RunTest(async accelerator =>
        {
            const int n = 200, len = n + 4 * 1000;
            var values = new int[len];
            for (int j = 0; j < len; j++) values[j] = (j * 7 + 3) % 16;
            var packed = new byte[(len + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((values[2 * k] & 0xF) | ((2 * k + 1 < len ? values[2 * k + 1] & 0xF : 0) << 4));
            using var srcBuf = accelerator.Allocate1D<QUInt4>(len);
            ((IContiguousArrayView)srcBuf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
            using var dstBuf = accelerator.Allocate1D<int>(n);
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QUInt4>, ArrayView<int>, int, int>(QUInt4InKernelSubViewKernel);
            // even starts only: a packed sub-view must start on a byte. 262: not a multiple of 4 bytes; 1000: a later chunk
            foreach (int start in new[] { 262, 1000 })
            {
                k1(n, srcBuf.View, dstBuf.View, start, n);
                var got = await dstBuf.CopyToHostAsync<int>();
                for (int j = 0; j < n; j++)
                    if (got[j] != values[start + j])
                        throw new Exception($"QUInt4 in-kernel SubView({start}, {n})[{j}] on {BackendName}: got {got[j]}, expected {values[start + j]}" +
                            $" (element {2 * start + j} holds {(2 * start + j < len ? values[2 * start + j] : -1)})");
            }
            Console.WriteLine($"[PackedQUInt4] in-kernel SubView at 262 and 1000 on {BackendName} ✓");
        });

        async Task CheckEmulatedInKernelSubView<T>(Accelerator accelerator, string name,
            Action<Index1D, ArrayView<T>, ArrayView<T>, int, int> kernel, Func<float, T> from, Func<T, float> to) where T : unmanaged
        {
            const int n = 1000, fill = 7;
            // room past the view for an offset applied 2x or 4x too far, so a wrong start lands inside this buffer
            const int len = n + 4 * 1000;
            var src = new T[len];
            var dst = new T[len];
            for (int i = 0; i < len; i++) { src[i] = from(i % 13); dst[i] = from(fill); }
            using var srcBuf = accelerator.Allocate1D(src);
            using var dstBuf = accelerator.Allocate1D(dst);
            var k = accelerator.LoadAutoGroupedStreamKernel(kernel);
            foreach (int start in new[] { 261, 1000 })
            {
                dstBuf.CopyFromCPU(dst);
                k(n, srcBuf.View, dstBuf.View, start, n);
                await accelerator.SynchronizeAsync();
                var got = await dstBuf.CopyToHostAsync();
                for (int j = 0; j < len; j++)
                {
                    float want = j >= start && j < start + n ? to(from(to(from(j % 13)) + 1f)) : fill;
                    if (to(got[j]) != want)
                        throw new Exception($"{name} in-kernel SubView({start}, {n}) on {BackendName}, element {j}: got {to(got[j])}, expected {want}" +
                            (j >= start && j < start + n ? "" : " (outside the sub-view)"));
                }
            }
        }
    }
}
