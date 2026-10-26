using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-26 (SpawnDev.Codecs FLAC encoder on WebGL, found by Geordi): a local struct passed `ref` into a helper too
    // big to inline at first (FLAC's UTF-8 frame-number writer) stays a struct variable reached through field addresses.
    // The GLSL generator declared the struct with `struct_N(0)` ("Number of constructor parameters does not match the
    // number of structure fields") and read and wrote the int-typed field ADDRESS variables instead of the fields
    // ("cannot convert from 'highp 2-component vector of uint' to 'highp int'" at every access of the long field): the
    // shader did not compile, and the lost context hung the rest of the WebGL lane. Now a field address of a local struct
    // is the field's lvalue and the declaration has every field. Shape: SpawnDev.Codecs FlacBitWriterGpu +
    // FlacFrameWriterGpu.WriteUtf8Number, with the output bytes folded into a checksum (one store per thread).
    public abstract partial class BackendTestBase
    {
        private struct LsfWriterState
        {
            public int CurrentByte;
            public int BitPos;
            public long OutLen;
        }

        private static void LsfWrite(ref LsfWriterState state, ref long ck, uint value, int bits)
        {
            while (bits > 0)
            {
                int free = 8 - state.BitPos;
                int take = free < bits ? free : bits;
                int shift = bits - take;
                uint mask = take == 32 ? 0xFFFFFFFFu : ((1u << take) - 1);
                uint chunk = (value >> shift) & mask;
                state.CurrentByte = (state.CurrentByte << take) | (int)chunk;
                state.BitPos += take;
                bits -= take;
                if (state.BitPos == 8)
                {
                    ck = ck * 31 + (state.CurrentByte & 0xFF) + state.OutLen;
                    state.OutLen++;
                    state.CurrentByte = 0;
                    state.BitPos = 0;
                }
            }
        }

        private static void LsfWriteUtf8(
            ref LsfWriterState w, ref long ck, ulong value)
        {
            if (value < 0x80)
            {
                LsfWrite(ref w, ref ck, (uint)value, 8);
            }
            else if (value < 0x800)
            {
                LsfWrite(ref w, ref ck, 0b110u, 3);
                LsfWrite(ref w, ref ck, (uint)(value >> 6), 5);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)(value & 0x3F), 6);
            }
            else if (value < 0x10000)
            {
                LsfWrite(ref w, ref ck, 0b1110u, 4);
                LsfWrite(ref w, ref ck, (uint)(value >> 12), 4);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 6) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)(value & 0x3F), 6);
            }
            else if (value < 0x200000)
            {
                LsfWrite(ref w, ref ck, 0b11110u, 5);
                LsfWrite(ref w, ref ck, (uint)(value >> 18), 3);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 12) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 6) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)(value & 0x3F), 6);
            }
            else if (value < 0x4000000)
            {
                LsfWrite(ref w, ref ck, 0b111110u, 6);
                LsfWrite(ref w, ref ck, (uint)(value >> 24), 2);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 18) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 12) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 6) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)(value & 0x3F), 6);
            }
            else
            {
                LsfWrite(ref w, ref ck, 0b1111110u, 7);
                LsfWrite(ref w, ref ck, (uint)(value >> 30), 1);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 24) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 18) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 12) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)((value >> 6) & 0x3F), 6);
                LsfWrite(ref w, ref ck, 0b10u, 2);
                LsfWrite(ref w, ref ck, (uint)(value & 0x3F), 6);
            }
        }

        private static long LsfRun(long value, int i)
        {
            var w = new LsfWriterState();
            w.OutLen = i * 3;
            long ck = 0;
            LsfWriteUtf8(ref w, ref ck, (ulong)value);
            return ck ^ ((long)w.CurrentByte << 40) ^ ((long)w.BitPos << 48) ^ w.OutLen;
        }

        private static void LocalStructFieldsUtf8Kernel(Index1D i, ArrayView<long> values, ArrayView<long> output)
        {
            var w = new LsfWriterState();
            w.OutLen = i.X * 3;
            long ck = 0;
            LsfWriteUtf8(ref w, ref ck, (ulong)values[i]);
            output[i] = ck ^ ((long)w.CurrentByte << 40) ^ ((long)w.BitPos << 48) ^ w.OutLen;
        }

        [TestMethod]
        public async Task LocalStructFields_RefStructThroughBigHelper_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // Every UTF-8 length (1..7 bytes): values below 2^7, 2^11, 2^16, 2^21, 2^26, 2^31, 2^36, at both ends.
            const int N = 4096;
            var values = new long[N];
            long[] limits = { 0x80, 0x800, 0x10000, 0x200000, 0x4000000, 0x80000000, 0x1000000000 };
            for (int k = 0; k < N; k++)
            {
                long hi = limits[k % limits.Length];
                long lo = k % limits.Length == 0 ? 0 : limits[k % limits.Length - 1];
                values[k] = (k & 8) != 0 ? hi - 1 : lo + ((k * 2654435761L) & 0x7FFFFFFFFL) % (hi - lo);
            }
            using var valueBuf = accelerator.Allocate1D(values);
            using var outBuf = accelerator.Allocate1D(new long[N]);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(LocalStructFieldsUtf8Kernel);
            kernel(N, valueBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<long>();
            for (int k = 0; k < N; k++)
            {
                long want = LsfRun(values[k], k);
                if (got[k] != want)
                    throw new Exception($"LocalStructFields: value {values[k]} at {k}: expected {want}, got {got[k]}.");
            }
        });
    }
}
