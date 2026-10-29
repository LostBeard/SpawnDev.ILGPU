using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Sub-word return values of helpers (byte, ushort, sbyte, short), read by the caller as int / long and compared with
    // loaded bytes, against the host. The frontend narrowed a return value to the return type without its sign (`ret`
    // popped with no TargetUnsigned), and 5.4.12-local.4's CreateConvert widened an 8 / 16-bit conversion result by that
    // result's own sign instead of the sign the caller asked for: `return (byte)crc;` with crc >= 0x80 came back
    // sign-extended (0x95 as -107) on every IR backend (CUDA, OpenCL, Velocity, WebGPU, WebGL, Wasm; the CPU accelerator runs
    // the IL). Found through SpawnDev.Codecs (Geordi): FlacCrcGpu.Compute8, so every FLAC frame whose header CRC-8 was
    // 0x80 or more failed its CRC check (status 2) on the IR backends.
    public abstract partial class BackendTestBase
    {
        const int SubWordReturnCases = 8;

        static byte SubWordCrc8(int a, int b)
        {
            int crc = 0;
            for (int i = 0; i < 2; i++)
            {
                crc ^= i == 0 ? a & 0xFF : b & 0xFF;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 0x80) != 0) crc = (crc << 1) ^ 0x07;
                    else crc <<= 1;
                }
                crc &= 0xFF;
            }
            return (byte)crc;
        }

        static ushort SubWordUShortOf(int x) => (ushort)(x * 40503);
        static sbyte SubWordSByteOf(int x) => (sbyte)(x * 77);
        static short SubWordShortOf(int x) => (short)(x * 40503);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static byte SubWordByteNotInlined(int x) => (byte)(x * 151);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static ushort SubWordUShortNotInlined(int x) => (ushort)(x * 40503);

        static long SubWordReturnHost(int k, int x, byte loaded) => k switch
        {
            0 => SubWordCrc8(x, x >> 8),
            1 => SubWordCrc8(x, x >> 8) == loaded ? 1 : 0,
            2 => SubWordUShortOf(x),
            3 => (long)SubWordUShortOf(x) << 3,
            4 => SubWordSByteOf(x),
            5 => SubWordShortOf(x),
            6 => SubWordByteNotInlined(x),
            _ => SubWordUShortNotInlined(x) + 1L,
        };

        // One value per thread (WebGL captures one output per thread).
        static void SubWordReturnKernel(Index1D i, ArrayView<int> xs, ArrayView<byte> loaded, ArrayView<long> o)
        {
            int k = i % SubWordReturnCases;
            int x = xs[i];
            long r;
            if (k == 0) r = SubWordCrc8(x, x >> 8);
            else if (k == 1) r = SubWordCrc8(x, x >> 8) == loaded[i] ? 1 : 0;
            else if (k == 2) r = SubWordUShortOf(x);
            else if (k == 3) r = (long)SubWordUShortOf(x) << 3;
            else if (k == 4) r = SubWordSByteOf(x);
            else if (k == 5) r = SubWordShortOf(x);
            else if (k == 6) r = SubWordByteNotInlined(x);
            else r = SubWordUShortNotInlined(x) + 1L;
            o[i] = r;
        }

        [TestMethod]
        public async Task SubWordReturnValues_ReadByTheCaller_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = SubWordReturnCases * 16;
            var xs = new int[n];
            var loaded = new byte[n];
            for (int i = 0; i < n; i++)
            {
                xs[i] = i * 2654435 + 977;
                loaded[i] = SubWordCrc8(xs[i], xs[i] >> 8);
            }
            int high = 0;
            for (int i = 0; i < n; i++) if (loaded[i] >= 0x80) high++;
            if (high == 0) throw new Exception("test data has no CRC of 0x80 or more");
            using var dX = accelerator.Allocate1D(xs);
            using var dL = accelerator.Allocate1D(loaded);
            using var dO = accelerator.Allocate1D<long>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<byte>, ArrayView<long>>(SubWordReturnKernel)(
                (Index1D)n, dX.View, dL.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<long>();
            for (int i = 0; i < n; i++)
            {
                long want = SubWordReturnHost(i % SubWordReturnCases, xs[i], loaded[i]);
                if (got[i] != want)
                    throw new Exception($"case {i % SubWordReturnCases} (thread {i}, x {xs[i]}): {got[i]}, expected {want}");
            }
        });
    }
}
