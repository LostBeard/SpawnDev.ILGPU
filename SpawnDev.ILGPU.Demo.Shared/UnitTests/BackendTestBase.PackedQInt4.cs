using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using QInt4 = ILGPU.QInt4;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Packed 4-bit QInt4 (2 nibbles/byte; ArrayView<QInt4> of N allocates ceil(N/2) bytes) load + store
    // across backends. LOAD decodes the (idx&1) nibble of byte (idx>>1) and sign-extends; STORE is an
    // atomic word RMW (adjacent threads write the two nibbles of one byte). Desktop (CPU IL / CUDA /
    // OpenCL) is shipped; WebGPU (WGSL) is wired here. WebGL has no atomics (packed store impossible) and
    // Wasm's nibble path is pending - both gated until their codegen lands. CPU runs the literal managed
    // indexer (reads decode by value; in-kernel packed writes are fail-loud), so CPU does LOAD only.
    public abstract partial class BackendTestBase
    {
        static void QInt4LoadKernel(Index1D i, ArrayView<QInt4> x, ArrayView<int> y) => y[i] = x[i];
        static void QInt4StoreKernel(Index1D i, ArrayView<int> src, ArrayView<QInt4> dst) => dst[i] = (QInt4)src[i];

        /// <summary>
        /// Uploads pre-packed nibble bytes into an ArrayView&lt;QInt4&gt; and verifies the kernel reads
        /// each element back as the correct sign-extended int (-8..7) at both even/odd nibble positions.
        /// </summary>
        [TestMethod]
        public async Task PackedQInt4_Load_SignExtendedNibbles() => await RunTest(async accelerator =>
        {
            var type = accelerator.AcceleratorType;
            if (type == AcceleratorType.WebGL || type == AcceleratorType.Wasm)
                throw new UnsupportedTestException($"Packed QInt4 load not yet wired on {type} (WebGPU + desktop done).");

            int n = 256; // spans multiple groups; every nibble value at both byte positions
            var expected = new int[n];
            for (int i = 0; i < n; i++) expected[i] = (i % 16) - 8;
            var packed = new byte[(n + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((expected[2 * k] & 0xF) | ((expected[2 * k + 1] & 0xF) << 4));

            using var xBuf = accelerator.Allocate1D<QInt4>(n);
            using var yBuf = accelerator.Allocate1D<int>(n);
            var raw = ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView();
            if (raw.Length != packed.Length)
                throw new Exception($"raw packed view length {raw.Length} != expected {packed.Length}");
            raw.CopyFromCPU(packed);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>, ArrayView<int>>(QInt4LoadKernel);
            kernel(n, xBuf.View, yBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await yBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"QInt4 load mismatch at [{i}]: got {got[i]} expected {expected[i]}");
        });

        /// <summary>
        /// Stores int values -8..7 into a packed QInt4 buffer (atomic nibble RMW - adjacent threads write
        /// the two nibbles of one byte), reads them back, and verifies the round-trip. CPU is fail-loud on
        /// packed in-kernel writes (the managed ref indexer can't address a nibble); WebGL has no atomics.
        /// </summary>
        [TestMethod]
        public async Task PackedQInt4_StoreRoundTrip() => await RunTest(async accelerator =>
        {
            var type = accelerator.AcceleratorType;
            if (type == AcceleratorType.CPU)
                throw new UnsupportedTestException("Packed QInt4 in-kernel store is fail-loud on the CPU backend (managed ref indexer can't write a nibble).");
            if (type == AcceleratorType.WebGL)
                throw new UnsupportedTestException("Packed QInt4 store needs atomic word RMW; WebGL has no atomics.");
            if (type == AcceleratorType.Wasm)
                throw new UnsupportedTestException("Packed QInt4 store not yet wired on Wasm (WebGPU + desktop done).");

            int n = 256; // large enough that adjacent threads write the two nibbles of one byte concurrently
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = (i % 16) - 8;

            using var sBuf = accelerator.Allocate1D(input);
            using var dBuf = accelerator.Allocate1D<QInt4>(n);
            using var oBuf = accelerator.Allocate1D<int>(n);
            var kStore = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<QInt4>>(QInt4StoreKernel);
            var kLoad = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>, ArrayView<int>>(QInt4LoadKernel);
            kStore(n, sBuf.View, dBuf.View);
            kLoad(n, dBuf.View, oBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != input[i])
                    throw new Exception($"QInt4 store round-trip mismatch at [{i}]: got {got[i]} expected {input[i]}");
        });
    }
}
