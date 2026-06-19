using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Float4E2M1 = ILGPU.Float4E2M1;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // TRUE packed 4-bit Float4E2M1 (FP4) storage: ArrayView<Float4E2M1> of N allocates ceil(N/2) bytes
    // (2 nibbles/byte, 8 per 32-bit word), like QInt4 - the data stays packed in the buffer and the
    // E2M1 nibble decodes to f32 in-register at the load (no unpack-on-load). LOAD decodes the (idx&1)
    // nibble of byte (idx>>1); STORE is an atomic word RMW (adjacent threads write the two nibbles of
    // one byte). Mirrors BackendTestBase.PackedQInt4 but with E2M1 decode/encode instead of int extend.
    // Fp4Oracle/Fp4OracleMagnitudes live in BackendTestBase.FromRawBits (same partial class).
    public abstract partial class BackendTestBase
    {
        static void Float4LoadKernel(Index1D i, ArrayView<Float4E2M1> x, ArrayView<float> y) => y[i] = x[i];
        static void Float4StoreKernel(Index1D i, ArrayView<float> src, ArrayView<Float4E2M1> dst) => dst[i] = (Float4E2M1)src[i];

        /// <summary>
        /// Uploads pre-packed FP4 nibble bytes into an ArrayView&lt;Float4E2M1&gt; and verifies the kernel
        /// decodes each element back to the correct E2M1 float at both even/odd nibble positions (all 16
        /// codes, both byte halves). The independent oracle is the hardcoded E2M1 magnitude table.
        /// </summary>
        [TestMethod]
        public async Task PackedFloat4_Load_DecodesNibbles() => await RunTest(async accelerator =>
        {
            int n = 256; // spans multiple groups; every code 0..15 at both byte positions
            var codes = new int[n];
            for (int i = 0; i < n; i++) codes[i] = i % 16;
            var packed = new byte[(n + 1) / 2];
            for (int k = 0; k < packed.Length; k++)
                packed[k] = (byte)((codes[2 * k] & 0xF) | ((codes[2 * k + 1] & 0xF) << 4));

            using var xBuf = accelerator.Allocate1D<Float4E2M1>(n);
            using var yBuf = accelerator.Allocate1D<float>(n);
            var raw = ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView();
            if (raw.Length != packed.Length)
                throw new Exception($"raw packed view length {raw.Length} != expected {packed.Length}");
            raw.CopyFromCPU(packed);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Float4E2M1>, ArrayView<float>>(Float4LoadKernel);
            kernel(n, xBuf.View, yBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await yBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = Fp4Oracle(codes[i]);
                if (got[i] != expected && !(expected == 0f && got[i] == 0f))
                    throw new Exception($"FP4 packed load mismatch at [{i}] code 0x{codes[i]:X}: got {got[i]} expected {expected}.");
            }
        });

        /// <summary>
        /// Stores FP4-representable floats into a packed Float4E2M1 buffer (atomic nibble RMW - adjacent
        /// threads write the two nibbles of one byte), reads them back (E2M1 decode), and verifies the
        /// round-trip. CPU + WebGL stores are fail-loud (no atomics / managed ref can't write a nibble).
        /// </summary>
        [TestMethod]
        public async Task PackedFloat4_StoreRoundTrip() => await RunTest(async accelerator =>
        {
            var type = accelerator.AcceleratorType;
            if (type == AcceleratorType.CPU)
                throw new UnsupportedTestException("Packed FP4 in-kernel store is fail-loud on the CPU backend (managed ref indexer can't write a nibble).");
            if (type == AcceleratorType.WebGL)
                throw new UnsupportedTestException("Packed FP4 store needs atomic word RMW; WebGL has no atomics.");

            int n = 256;
            // The 16 exactly-representable FP4 magnitudes (signed), cycled - so encode is exact (no rounding).
            float[] mags = { 0f, 0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f };
            var input = new float[n];
            for (int i = 0; i < n; i++)
            {
                int code = i % 16;
                float m = mags[code & 0x7];
                input[i] = (code & 0x8) != 0 ? -m : m;
            }

            using var sBuf = accelerator.Allocate1D(input);
            using var dBuf = accelerator.Allocate1D<Float4E2M1>(n);
            using var oBuf = accelerator.Allocate1D<float>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<Float4E2M1>>(Float4StoreKernel)(
                n, sBuf.View, dBuf.View);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Float4E2M1>, ArrayView<float>>(Float4LoadKernel)(
                n, dBuf.View, oBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != input[i] && !(input[i] == 0f && got[i] == 0f))
                    throw new Exception($"FP4 store round-trip mismatch at [{i}]: got {got[i]} expected {input[i]}");
        });
    }
}
