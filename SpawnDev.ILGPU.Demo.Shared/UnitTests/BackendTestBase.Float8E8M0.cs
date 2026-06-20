using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Float8E8M0 (OCP float8_e8m0fnu) - the shared power-of-two SCALE format for MX blocks. The headline
    // is the kernel-safe in-register decode Float8E8M0Extensions.RawBitsToFloat(scaleByte) = 2^(e-127),
    // e==0xFF -> NaN. Decode verified on all 6 backends against an INDEPENDENT spec oracle (Math.Pow,
    // not the library impl, per Rule 1); host FromSingle/FromRawBits/RawValue covered on the CPU side.
    public abstract partial class BackendTestBase
    {
        // Independent oracle: float8_e8m0fnu code e decodes to 2^(e-127); 0xFF is the only special (NaN).
        static float E8m0Oracle(int e)
        {
            e &= 0xFF;
            if (e == 0xFF) return float.NaN;
            return MathF.Pow(2f, e - 127);
        }

        // In-shader decode of a raw scale byte (held as int) - the packed MX block is never widened.
        static void E8m0RawBitsKernel(Index1D i, ArrayView<int> codes, ArrayView<float> outF)
            => outF[i] = Float8E8M0Extensions.RawBitsToFloat(codes[i]);

        [TestMethod]
        public async Task RawBitsToFloat_Float8E8M0_DecodeAll256_GpuMatchesSpec() => await RunTest(async acc =>
        {
            const int n = 256;
            var codes = new int[n];
            for (int c = 0; c < n; c++) codes[c] = c;
            using var inBuf = acc.Allocate1D(codes);
            using var outBuf = acc.Allocate1D<float>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>>(E8m0RawBitsKernel);
            k((Index1D)n, inBuf.View, outBuf.View);
            await acc.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<float>();
            for (int c = 0; c < n; c++)
            {
                float expected = E8m0Oracle(c);
                if (float.IsNaN(expected))
                {
                    if (!float.IsNaN(got[c]))
                        throw new Exception($"E8M0 RawBitsToFloat code 0x{c:X} ({BackendName}): expected NaN, got {got[c]}.");
                }
                // e==0 decodes to 2^-127, the only SUBNORMAL value. GPUs may flush subnormals to zero
                // (IEEE-754 FTZ; WebGL does), so accept 0 there - every other code is a normal float.
                else if (c == 0 && got[c] == 0f)
                {
                }
                else if (got[c] != expected)
                    throw new Exception($"E8M0 RawBitsToFloat code 0x{c:X} ({BackendName}): expected {expected} (2^{c - 127}), got {got[c]}.");
            }
        });

        [TestMethod]
        public Task Float8E8M0_HostDecodeEncodeRoundTrip() => RunTest(acc =>
        {
            // Host-side struct logic (no kernel) - run once on CPU.
            if (acc.AcceleratorType != AcceleratorType.CPU)
                throw new UnsupportedTestException("Host-only Float8E8M0 struct test (runs on CPU).");
            // Decode: RawValue round-trips, (float) decodes to 2^(e-127) / NaN.
            for (int e = 0; e < 256; e++)
            {
                var v = Float8E8M0.FromRawBits((byte)e);
                if (v.RawValue != e) throw new Exception($"E8M0 RawValue round-trip failed at {e}: got {v.RawValue}.");
                float f = (float)v, oracle = E8m0Oracle(e);
                if (float.IsNaN(oracle)) { if (!float.IsNaN(f)) throw new Exception($"E8M0 (float) code 0x{e:X}: expected NaN, got {f}."); }
                else if (f != oracle) throw new Exception($"E8M0 (float) code 0x{e:X}: expected {oracle}, got {f}.");
            }
            // Encode: exact powers of two round-trip to their exponent code; non-finite/<=0 -> NaN code 0xFF.
            for (int k = -127; k <= 127; k++)
            {
                float p = MathF.Pow(2f, k);
                var enc = Float8E8M0.FromSingle(p);
                int expectedCode = k + 127;
                if (enc.RawValue != expectedCode)
                    throw new Exception($"E8M0 FromSingle(2^{k}): expected code {expectedCode}, got {enc.RawValue}.");
            }
            if (Float8E8M0.FromSingle(float.NaN).RawValue != 0xFF) throw new Exception("E8M0 FromSingle(NaN) != 0xFF.");
            if (Float8E8M0.FromSingle(0f).RawValue != 0xFF) throw new Exception("E8M0 FromSingle(0) != 0xFF.");
            if (Float8E8M0.FromSingle(-2f).RawValue != 0xFF) throw new Exception("E8M0 FromSingle(-2) != 0xFF.");
            if (Float8E8M0.FromSingle(float.PositiveInfinity).RawValue != 0xFF) throw new Exception("E8M0 FromSingle(+Inf) != 0xFF.");
            return Task.CompletedTask;
        });
    }
}
