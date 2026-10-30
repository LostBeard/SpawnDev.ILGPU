using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Half = ILGPU.Half;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Sub-views of the emulated 1- and 2-byte float types passed as kernel arguments. OpenCL declared an emulated view
    // parameter with the wrong pointer type and added the sub-view offset to it: an emulated Half sub-view (NVIDIA has no
    // cl_khr_fp16) started at twice its byte offset, so its writes ran past the view into the rest of the buffer and into
    // neighbouring buffers. Found by Tuvok: SpawnDev.ILGPU.ML's SD merge blends a 50.6M-element Half tensor in 2^24-element
    // SubView chunks and corrupted other tensors on OpenCL only. FP8 had the opposite mismatch (a `global float*`
    // parameter, a `uchar*` field) and only came out right because the cast was applied before the add.
    public abstract partial class BackendTestBase
    {
        static void HalfSubViewArgKernel(Index1D i, ArrayView<Half> src, ArrayView<Half> dst) =>
            dst[i] = (Half)((float)src[i] + 1f);
        static void BFloat16SubViewArgKernel(Index1D i, ArrayView<BFloat16> src, ArrayView<BFloat16> dst) =>
            dst[i] = (BFloat16)((float)src[i] + 1f);
        static void Float8E4M3SubViewArgKernel(Index1D i, ArrayView<Float8E4M3> src, ArrayView<Float8E4M3> dst) =>
            dst[i] = (Float8E4M3)((float)src[i] + 1f);
        static void Float8E5M2SubViewArgKernel(Index1D i, ArrayView<Float8E5M2> src, ArrayView<Float8E5M2> dst) =>
            dst[i] = (Float8E5M2)((float)src[i] + 1f);

        [TestMethod]
        public async Task EmulatedSubWordView_SubViewArguments_ReadAndWriteInPlace() => await RunTest(async accelerator =>
        {
            await CheckEmulatedSubViewArgs<Half>(accelerator, "Half", HalfSubViewArgKernel, v => (Half)v, v => (float)v);
            await CheckEmulatedSubViewArgs<BFloat16>(accelerator, "BFloat16", BFloat16SubViewArgKernel, v => (BFloat16)v, v => (float)v);
            await CheckEmulatedSubViewArgs<Float8E4M3>(accelerator, "Float8E4M3", Float8E4M3SubViewArgKernel, v => (Float8E4M3)v, v => (float)v);
            await CheckEmulatedSubViewArgs<Float8E5M2>(accelerator, "Float8E5M2", Float8E5M2SubViewArgKernel, v => (Float8E5M2)v, v => (float)v);
            Console.WriteLine($"[EmulatedSubWordView] Half / BFloat16 / FP8 SubView arguments at 261 and 1000 on {BackendName} ✓");
        });

        async Task CheckEmulatedSubViewArgs<T>(Accelerator accelerator, string name,
            Action<Index1D, ArrayView<T>, ArrayView<T>> kernel, Func<float, T> from, Func<T, float> to) where T : unmanaged
        {
            const int n = 1000, fill = 7;
            // room past the view for an offset applied 2x or 4x too far, so a wrong start lands inside this buffer and
            // is caught here instead of corrupting some other allocation
            const int len = n + 4 * 1000;
            var src = new T[len];
            var dst = new T[len];
            for (int i = 0; i < len; i++) { src[i] = from(i % 13); dst[i] = from(fill); }
            using var srcBuf = accelerator.Allocate1D(src);
            using var dstBuf = accelerator.Allocate1D(dst);
            var k = accelerator.LoadAutoGroupedStreamKernel(kernel);
            // 261: not a multiple of 4 bytes and past one 256-byte binding alignment step; 1000: the second chunk of a
            // chunked launch, as in the SD merge
            foreach (int start in new[] { 261, 1000 })
            {
                dstBuf.CopyFromCPU(dst);
                k(n, srcBuf.View.SubView(start, n), dstBuf.View.SubView(start, n));
                await accelerator.SynchronizeAsync();
                var got = await dstBuf.CopyToHostAsync();
                for (int j = 0; j < len; j++)
                {
                    // the kernel adds 1 to the STORED source value (E5M2 holds 9 as 8), then rounds again
                    float want = j >= start && j < start + n ? to(from(to(from(j % 13)) + 1f)) : fill;
                    if (to(got[j]) != want)
                        throw new Exception($"{name} SubView({start}, {n}) arguments on {BackendName}, element {j}: got {to(got[j])}, expected {want}" +
                            (j >= start && j < start + n ? "" : " (outside the sub-view)"));
                }
            }
        }
    }
}
