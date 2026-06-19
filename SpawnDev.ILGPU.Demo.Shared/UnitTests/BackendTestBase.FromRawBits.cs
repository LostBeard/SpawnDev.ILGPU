using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using Half = ILGPU.Half;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Verifies the PUBLIC kernel-safe raw-bits decode path on the sub-word element types: a consumer
    // holding a nibble/byte/ushort pulled (by its own bit-math) out of a STILL-PACKED quant buffer
    // decodes it to the verified float IN-SHADER via `<Type>Extensions.RawBitsToFloat(code)`, with
    // the buffer never widened to f32. This is the in-kernel primitive Tuvok's MXFP4 ML lane needs
    // (DevComms gaps-found 2026-06-18) - it composes the ONE library decode instead of re-deriving
    // the E2M1 value table by hand, and keeps weights in native packed form (Rule 4 / no-unpack).
    //
    // RawBitsToFloat is pure int/float arithmetic (no struct construction), so it transpiles on all 6
    // backends - unlike `(float)FromRawBits(code)`, which does NOT lower on the browser backends
    // (they hold sub-word floats decoded-in-register, so building one from raw bits has no valid
    // lowering). FP4 is checked against a HARDCODED oracle table (independent of the library impl,
    // per Rule 1); FP8/bf16 compare the transpiled result against the managed decode (oracle-
    // validated to ml_dtypes/IEEE elsewhere). A separate host-only test covers FromRawBits/RawValue.
    public abstract partial class BackendTestBase
    {
        // The 16 OCP E2M1FN magnitudes by low-nibble code (bit 3 = sign). Independent oracle.
        static readonly float[] Fp4OracleMagnitudes = { 0f, 0.5f, 1f, 1.5f, 2f, 3f, 4f, 6f };

        static float Fp4Oracle(int code)
        {
            float mag = Fp4OracleMagnitudes[code & 0x7];
            return (code & 0x8) != 0 ? -mag : mag;
        }

        // out[i] = decode(packed code) IN-SHADER - the packed buffer (here ArrayView<int> codes) is
        // never widened; RawBitsToFloat unpacks one element to f32 in a register for use.
        static void Fp4RawBitsKernel(Index1D i, ArrayView<int> codes, ArrayView<float> outF)
            => outF[i] = Float4E2M1Extensions.RawBitsToFloat(codes[i]);

        static void Fp8E4M3RawBitsKernel(Index1D i, ArrayView<int> codes, ArrayView<float> outF)
            => outF[i] = Float8E4M3Extensions.RawBitsToFloat(codes[i]);

        static void Fp8E5M2RawBitsKernel(Index1D i, ArrayView<int> codes, ArrayView<float> outF)
            => outF[i] = Float8E5M2Extensions.RawBitsToFloat(codes[i]);

        static void Bf16RawBitsKernel(Index1D i, ArrayView<int> codes, ArrayView<float> outF)
            => outF[i] = BFloat16Extensions.RawBitsToFloat(codes[i]);

        [TestMethod]
        public async Task RawBitsToFloat_Float4E2M1_DecodeAll16_GpuMatchesOracle() => await RunTest(async acc =>
        {
            const int n = 16;
            var codes = new int[n];
            for (int c = 0; c < n; c++) codes[c] = c;
            using var inBuf = acc.Allocate1D(codes);
            using var outBuf = acc.Allocate1D<float>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>>(Fp4RawBitsKernel);
            k((Index1D)n, inBuf.View, outBuf.View);
            await acc.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<float>();
            for (int c = 0; c < n; c++)
            {
                float expected = Fp4Oracle(c);
                // code 0x8 is -0; +0 is acceptable too. Compare by value.
                if (got[c] != expected && !(expected == 0f && got[c] == 0f))
                    throw new Exception(
                        $"FP4 RawBitsToFloat wrong for code 0x{c:X}: expected {expected}, got {got[c]} " +
                        $"(in-shader decode mislowered on this backend).");
            }
        });

        [TestMethod]
        public async Task RawBitsToFloat_Float8E4M3_DecodeAll256_GpuMatchesManaged() => await RunTest(async acc =>
        {
            const int n = 256;
            var codes = new int[n];
            for (int c = 0; c < n; c++) codes[c] = c;
            using var inBuf = acc.Allocate1D(codes);
            using var outBuf = acc.Allocate1D<float>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>>(Fp8E4M3RawBitsKernel);
            k((Index1D)n, inBuf.View, outBuf.View);
            await acc.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<float>();
            for (int c = 0; c < n; c++)
            {
                float expected = Float8E4M3Extensions.RawBitsToFloat(c);
                bool bothNaN = float.IsNaN(expected) && float.IsNaN(got[c]);
                if (!bothNaN && got[c] != expected && !(expected == 0f && got[c] == 0f))
                    throw new Exception(
                        $"FP8 E4M3 RawBitsToFloat wrong for code 0x{c:X2}: expected {expected}, got {got[c]}.");
            }
        });

        [TestMethod]
        public async Task RawBitsToFloat_Float8E5M2_DecodeAll256_GpuMatchesManaged() => await RunTest(async acc =>
        {
            const int n = 256;
            var codes = new int[n];
            for (int c = 0; c < n; c++) codes[c] = c;
            using var inBuf = acc.Allocate1D(codes);
            using var outBuf = acc.Allocate1D<float>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>>(Fp8E5M2RawBitsKernel);
            k((Index1D)n, inBuf.View, outBuf.View);
            await acc.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<float>();
            for (int c = 0; c < n; c++)
            {
                float expected = Float8E5M2Extensions.RawBitsToFloat(c);
                bool bothNaN = float.IsNaN(expected) && float.IsNaN(got[c]);
                bool bothInf = float.IsInfinity(expected) && float.IsInfinity(got[c]) &&
                               MathF.Sign(expected) == MathF.Sign(got[c]);
                if (!bothNaN && !bothInf && got[c] != expected && !(expected == 0f && got[c] == 0f))
                    throw new Exception(
                        $"FP8 E5M2 RawBitsToFloat wrong for code 0x{c:X2}: expected {expected}, got {got[c]}.");
            }
        });

        [TestMethod]
        public async Task RawBitsToFloat_BFloat16_DecodeSweep_GpuMatchesManaged() => await RunTest(async acc =>
        {
            // Representative bf16 codes: zeros, small/large normals, sign variants (skip Inf/NaN exps).
            int[] codes = { 0x0000, 0x8000, 0x3F80, 0xBF80, 0x4000, 0x4040, 0x3F00, 0x4120, 0xC120, 0x0080, 0x7F00 };
            using var inBuf = acc.Allocate1D(codes);
            using var outBuf = acc.Allocate1D<float>(codes.Length);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>>(Bf16RawBitsKernel);
            k((Index1D)codes.Length, inBuf.View, outBuf.View);
            await acc.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<float>();
            for (int j = 0; j < codes.Length; j++)
            {
                float expected = BFloat16Extensions.RawBitsToFloat(codes[j]);
                if (got[j] != expected && !(expected == 0f && got[j] == 0f))
                    throw new Exception(
                        $"bf16 RawBitsToFloat wrong for code 0x{codes[j]:X4}: expected {expected}, got {got[j]}.");
            }
        });

        // Host-side round-trip of the FromRawBits factory + public RawValue getter (no kernel): the
        // raw code survives FromRawBits -> RawValue, and FromRawBits decodes the same as RawBitsToFloat.
        [TestMethod]
        public Task FromRawBits_HostRoundTrip_AllSubWordTypes()
        {
            for (int c = 0; c < 16; c++)
            {
                if (Float4E2M1.FromRawBits((byte)c).RawValue != (c & 0x0F))
                    throw new Exception($"FP4 RawValue round-trip wrong for 0x{c:X}.");
                if ((float)Float4E2M1.FromRawBits((byte)c) != Float4E2M1Extensions.RawBitsToFloat(c) &&
                    !(Float4E2M1Extensions.RawBitsToFloat(c) == 0f && (float)Float4E2M1.FromRawBits((byte)c) == 0f))
                    throw new Exception($"FP4 FromRawBits vs RawBitsToFloat disagree for 0x{c:X}.");
            }
            for (int c = 0; c < 256; c++)
            {
                if (Float8E4M3.FromRawBits((byte)c).RawValue != c)
                    throw new Exception($"FP8 E4M3 RawValue round-trip wrong for 0x{c:X2}.");
                if (Float8E5M2.FromRawBits((byte)c).RawValue != c)
                    throw new Exception($"FP8 E5M2 RawValue round-trip wrong for 0x{c:X2}.");
                float e4Host = (float)Float8E4M3.FromRawBits((byte)c);
                float e4Raw = Float8E4M3Extensions.RawBitsToFloat(c);
                if (e4Host != e4Raw && !(float.IsNaN(e4Host) && float.IsNaN(e4Raw)))
                    throw new Exception($"FP8 E4M3 FromRawBits vs RawBitsToFloat disagree for 0x{c:X2}.");
            }
            int[] u16 = { 0x0000, 0x3F80, 0xBF80, 0x4049, 0x7F00, 0x3C00, 0x8000 };
            foreach (int c in u16)
            {
                if (BFloat16.FromRawBits((ushort)c).RawValue != c)
                    throw new Exception($"bf16 RawValue round-trip wrong for 0x{c:X4}.");
                if (Half.FromRawBits((ushort)c).RawValue != c)
                    throw new Exception($"Half RawValue round-trip wrong for 0x{c:X4}.");
                float bHost = (float)BFloat16.FromRawBits((ushort)c);
                float bRaw = BFloat16Extensions.RawBitsToFloat(c);
                if (bHost != bRaw && !(float.IsNaN(bHost) && float.IsNaN(bRaw)))
                    throw new Exception($"bf16 FromRawBits vs RawBitsToFloat disagree for 0x{c:X4}.");
            }
            return Task.CompletedTask;
        }
    }
}
