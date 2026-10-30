using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Float4E2M1 = ILGPU.Float4E2M1;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Sub-views of the packed 4-bit types (QInt4, Float4E2M1: two elements per byte) passed to kernels. Every byte
    // address of a view was Index * ElementSize, and ElementSize is 1 for these types, so a sub-view at element k
    // started at byte k instead of k / 2: CUDA, OpenCL, Velocity and Wasm read the wrong elements and wrote past the
    // view (into neighbouring buffers). A device pointer cannot start inside a byte, so SubView of a packed view now
    // throws at an odd offset (on every backend, the CPU one too: one contract).
    public abstract partial class BackendTestBase
    {
        static int PackedTestValue(int i) => i % 15 - 7;

        static byte[] PackQInt4(int length, Func<int, int> value)
        {
            var packed = new byte[(length + 1) / 2];
            for (int i = 0; i < length; i++)
                packed[i >> 1] |= (byte)((value(i) & 0xF) << ((i & 1) * 4));
            return packed;
        }

        static void PackedQInt4ReadKernel(Index1D i, ArrayView<QInt4> src, ArrayView<int> dst) => dst[i] = src[i];
        static void PackedFp4ReadKernel(Index1D i, ArrayView<Float4E2M1> src, ArrayView<float> dst) => dst[i] = (float)src[i];
        static void PackedQInt4AddOneKernel(Index1D i, ArrayView<QInt4> w) => w[i] = (QInt4)((int)w[i] + 1);

        // Read through even-offset sub-views: every backend (packed loads work everywhere).
        [TestMethod]
        public async Task PackedSubView_EvenOffsets_ReadMatchesHost() => await RunTest(async accelerator =>
        {
            const int len = 4096, n = 1000;
            using var q4Buf = accelerator.Allocate1D<QInt4>(len);
            ((IContiguousArrayView)q4Buf.View.BaseView).AsRawArrayView().CopyFromCPU(PackQInt4(len, PackedTestValue));
            var fp4Values = new float[len];
            for (int i = 0; i < len; i++) fp4Values[i] = Fp4ExactValues[i % Fp4ExactValues.Length];
            using var fp4Buf = accelerator.Allocate1D<Float4E2M1>(len);
            ((IContiguousArrayView)fp4Buf.View.BaseView).AsRawArrayView().CopyFromCPU(PackFp4(fp4Values));
            using var intOut = accelerator.Allocate1D<int>(n);
            using var floatOut = accelerator.Allocate1D<float>(n);
            var readQ4 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>, ArrayView<int>>(PackedQInt4ReadKernel);
            var readFp4 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Float4E2M1>, ArrayView<float>>(PackedFp4ReadKernel);

            // 2: the first nonzero byte-aligned offset; 1024 / 3000: later chunks, as a chunked launch makes them
            foreach (int at in new[] { 0, 2, 1024, 3000 })
            {
                readQ4(n, q4Buf.View.SubView(at, n), intOut.View);
                readFp4(n, fp4Buf.View.SubView(at, n), floatOut.View);
                await accelerator.SynchronizeAsync();
                var gotQ4 = await intOut.CopyToHostAsync<int>();
                var gotFp4 = await floatOut.CopyToHostAsync<float>();
                for (int i = 0; i < n; i++)
                {
                    if (gotQ4[i] != PackedTestValue(at + i))
                        throw new Exception($"QInt4 SubView({at}, {n}) read on {BackendName}, element {i}: got {gotQ4[i]}, expected {PackedTestValue(at + i)}");
                    if (gotFp4[i] != fp4Values[at + i])
                        throw new Exception($"Float4E2M1 SubView({at}, {n}) read on {BackendName}, element {i}: got {gotFp4[i]}, expected {fp4Values[at + i]}");
                }
            }
            Console.WriteLine($"[PackedSubView] QInt4 / Float4E2M1 reads at 0, 2, 1024, 3000 on {BackendName} ✓");
        });

        // Write in place through chunked sub-views: the packed-store backends (CPU and WebGL cannot store a nibble).
        [TestMethod]
        public async Task PackedSubView_EvenOffsets_WriteInPlace() => await RunTest(async accelerator =>
        {
            var t = accelerator.AcceleratorType;
            if (t == AcceleratorType.CPU || t == AcceleratorType.WebGL)
                throw new UnsupportedTestException($"Packed 4-bit stores (atomic-RMW nibble store) are unsupported on {t}.");

            const int len = 4096, chunk = 1024;
            using var q4Buf = accelerator.Allocate1D<QInt4>(len);
            ((IContiguousArrayView)q4Buf.View.BaseView).AsRawArrayView().CopyFromCPU(PackQInt4(len, PackedTestValue));
            // a buffer allocated right after it: a write past the end of the sub-view lands here on some backends
            using var neighbour = accelerator.Allocate1D<QInt4>(len);
            ((IContiguousArrayView)neighbour.View.BaseView).AsRawArrayView().CopyFromCPU(PackQInt4(len, _ => 3));
            using var intOut = accelerator.Allocate1D<int>(len);
            var addOne = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>>(PackedQInt4AddOneKernel);
            var readQ4 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>, ArrayView<int>>(PackedQInt4ReadKernel);

            // +1 over the whole buffer in chunks, then +1 once more on SubView(2, 500) only
            for (int at = 0; at < len; at += chunk)
                addOne(chunk, q4Buf.View.SubView(at, chunk));
            addOne(500, q4Buf.View.SubView(2, 500));
            readQ4(len, q4Buf.View, intOut.View);
            await accelerator.SynchronizeAsync();
            var got = await intOut.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
            {
                int raw = PackedTestValue(i) + 1 + (i >= 2 && i < 502 ? 1 : 0);
                int want = ((raw & 0xF) ^ 8) - 8; // wraps like the 4-bit store
                if (got[i] != want)
                    throw new Exception($"QInt4 chunked SubView write on {BackendName}, element {i}: got {got[i]}, expected {want}");
            }
            readQ4(len, neighbour.View, intOut.View);
            await accelerator.SynchronizeAsync();
            got = await intOut.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (got[i] != 3)
                    throw new Exception($"QInt4 SubView writes on {BackendName} changed the next buffer, element {i}: got {got[i]}, expected 3");
            Console.WriteLine($"[PackedSubView] QInt4 chunked SubView writes on {BackendName} ✓");
        });

        // A packed sub-view must start on a byte: an odd 4-bit offset throws at SubView, on every backend.
        [TestMethod]
        public async Task PackedSubView_OddOffset_Throws() => await RunTest(async accelerator =>
        {
            using var q4Buf = accelerator.Allocate1D<QInt4>(64);
            using var fp4Buf = accelerator.Allocate1D<Float4E2M1>(64);
            Expect(() => q4Buf.View.SubView(3, 10), "ArrayView1D<QInt4>.SubView(3, 10)");
            Expect(() => q4Buf.View.BaseView.SubView(1), "ArrayView<QInt4>.SubView(1)");
            Expect(() => fp4Buf.View.SubView(2, 10).SubView(1, 4), "Float4E2M1 SubView(2).SubView(1)");
            // even offsets, and a sub-view of a sub-view that lands on a byte, are fine
            _ = q4Buf.View.SubView(2, 10).SubView(4, 4);
            _ = fp4Buf.View.SubView(1 + 1, 10);
            await Task.CompletedTask;
            Console.WriteLine($"[PackedSubView] odd 4-bit offsets throw on {BackendName} ✓");

            static void Expect(Func<object> subView, string what)
            {
                try { subView(); }
                catch (ArgumentOutOfRangeException) { return; }
                throw new Exception($"{what} did not throw: a packed 4-bit view cannot start inside a byte");
            }
        });
    }
}
