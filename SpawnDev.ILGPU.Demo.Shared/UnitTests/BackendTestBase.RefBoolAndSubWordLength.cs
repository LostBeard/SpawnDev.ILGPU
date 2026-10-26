using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Two WebGPU codegen bugs SpawnDev.Codecs' Snappy codec found (Geordi, 2026-10-26):
    //  - A `ref bool` local passed to a helper: IL keeps a bool as a byte, so the inlined helper writes it through a
    //    byte pointer cast of the bool local, and the WGSL assigned the VALUE to an undeclared variable and stored
    //    through it ("no definition in scope for identifier") - shader creation failed.
    //  - `IntLength` of a byte (sub-word) view whose length is not a multiple of 4: arrayLength() counts the binding's
    //    u32 words, so the length came back rounded up to a multiple of 4.
    public abstract partial class BackendTestBase
    {
        static int RefBool_ReadElement(int x, ref bool isCopy, ref int len)
        {
            if ((x & 3) != 0)
            {
                isCopy = true;
                len = x >> 2;
                return 2;
            }
            isCopy = false;
            len = x + 1;
            return 1;
        }

        static void RefBoolThroughHelperKernel(Index1D idx, ArrayView<int> output, ArrayView<int> input)
        {
            bool isCopy = false;
            int len = 0;
            int hdr = RefBool_ReadElement(input[idx], ref isCopy, ref len);
            output[idx] = (isCopy ? 1000 : 0) + len * 10 + hdr;
        }

        [TestMethod]
        public async Task RefBool_ThroughHelper_SetsAndReadsTheLocal() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 64;
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = i * 7 + (i % 5);
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(RefBoolThroughHelperKernel)((Index1D)n, dOut.View, dIn.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                bool isCopy = false; int len = 0;
                int hdr = RefBool_ReadElement(input[i], ref isCopy, ref len);
                int want = (isCopy ? 1000 : 0) + len * 10 + hdr;
                if (got[i] != want)
                    throw new Exception($"output[{i}] = {got[i]}, expected {want}");
            }
        });

        static void SubWordLengthKernel(Index1D idx, ArrayView<int> output, ArrayView<byte> bytes, ArrayView<short> shorts)
        {
            if (idx == 0)
            {
                output[0] = bytes.IntLength;
                output[1] = shorts.IntLength;
                output[2] = bytes[bytes.IntLength - 1];
            }
        }

        [TestMethod]
        public async Task SubWordView_IntLength_IsExactNotRoundedToWords() => await RunEmulatedTest(async accelerator =>
        {
            var bytes = new byte[29];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 9 + 1);
            using var dBytes = accelerator.Allocate1D(bytes);
            using var dShorts = accelerator.Allocate1D(new short[7]);
            using var dOut = accelerator.Allocate1D<int>(3);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<byte>, ArrayView<short>>(SubWordLengthKernel);
            // whole buffers (29 bytes, 7 shorts) and then sub-views of odd lengths at odd offsets
            foreach (var (byteView, shortView, byteLast) in new[]
            {
                (dBytes.View, dShorts.View, bytes[28]),
                (dBytes.View.SubView(3, 13), dShorts.View.SubView(1, 5), bytes[15]),
                (dBytes.View.SubView(0, 1), dShorts.View.SubView(0, 3), bytes[0]),
            })
            {
                kernel((Index1D)1, dOut.View, byteView, shortView);
                await accelerator.SynchronizeAsync();
                var got = await dOut.CopyToHostAsync<int>();
                if (got[0] != byteView.IntLength || got[1] != shortView.IntLength || got[2] != byteLast)
                    throw new Exception($"byte view of {byteView.IntLength}: IntLength {got[0]}, last byte {got[2]} (expected {byteLast}); " +
                        $"short view of {shortView.IntLength}: IntLength {got[1]}");
            }
        });
    }
}
