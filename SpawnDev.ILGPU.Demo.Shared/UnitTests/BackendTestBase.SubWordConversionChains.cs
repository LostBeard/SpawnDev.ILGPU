using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Integer conversion chains through 8 / 16-bit types, against the host. IL keeps every sub-word value on the evaluation
    // stack extended to 32 bits by its own type, and conv.u8 / conv.r.un read that 32-bit value unsigned. ILGPU's IR has no
    // unsigned types, and IRBuilder.CreateConvert lost the sign of a narrow conversion's result: (ushort)(short)x became the
    // signed (short)x (a same-type sign change was an identity), the widening of (short)(ushort)x took the inner conversion's
    // source sign and zero-extended, two widenings of different signs folded into one ((long)(uint)(short)x sign-extended to
    // 64 bits), and (long)(uint)(int)l / (long)(int)(uint)l took the inner sign over the instruction's. 20 of these 22 chains
    // were wrong on every IR backend (CUDA, OpenCL, Velocity; the CPU accelerator runs the IL). Found through SpawnDev.Codecs:
    // libopus' SHL16 ((short)(ushort)((ushort)a << shift)) in celt_rsqrt_norm made the CELT encoder's pitch gain wrong.
    public abstract partial class BackendTestBase
    {
        const int ConvChainCount = 22;

        static long[] ConvChainsHost(int x, short s, long l) => new long[]
        {
            (short)(ushort)((ushort)x << 1),
            (int)(short)(ushort)x,
            (long)(short)(ushort)x,
            (int)(sbyte)(byte)x,
            (long)(sbyte)(byte)x,
            (long)(uint)(short)x,
            (long)(uint)(sbyte)x,
            (int)(ushort)(short)x,
            (long)(ushort)(short)x,
            (long)(short)(sbyte)x,
            (long)(ushort)(sbyte)x,
            (long)(float)(short)(ushort)x,
            (long)(uint)(ushort)(short)x,
            (long)(uint)s,
            (long)(ulong)(uint)s,
            (long)(short)(ushort)(s + 1),
            (long)(uint)(int)l,
            (long)(int)(uint)l,
            (long)(ulong)(uint)(int)l,
            ((x & 1) != 0 ? (ushort)x : (ushort)(x >> 3)) + 1L,
            (long)(float)(uint)(short)x,
            (long)(double)(uint)(int)l,
        };

        static readonly string[] ConvChainNames =
        {
            "(short)(ushort)((ushort)x << 1)", "(int)(short)(ushort)x", "(long)(short)(ushort)x", "(int)(sbyte)(byte)x",
            "(long)(sbyte)(byte)x", "(long)(uint)(short)x", "(long)(uint)(sbyte)x", "(int)(ushort)(short)x", "(long)(ushort)(short)x",
            "(long)(short)(sbyte)x", "(long)(ushort)(sbyte)x", "(long)(float)(short)(ushort)x", "(long)(uint)(ushort)(short)x",
            "(long)(uint)s", "(long)(ulong)(uint)s", "(long)(short)(ushort)(s + 1)", "(long)(uint)(int)l", "(long)(int)(uint)l",
            "(long)(ulong)(uint)(int)l", "(x & 1) != 0 ? (ushort)x : (ushort)(x >> 3)) + 1L", "(long)(float)(uint)(short)x",
            "(long)(double)(uint)(int)l",
        };

        // One output per thread (thread i computes chain i % ConvChainCount of input i / ConvChainCount): WebGL captures one.
        static void ConvChainsKernel(Index1D i, ArrayView<int> xs, ArrayView<short> ss, ArrayView<long> ls, ArrayView<long> o)
        {
            int j = i / ConvChainCount, k = i % ConvChainCount;
            int x = xs[j];
            short s = ss[j];
            long l = ls[j];
            long r;
            if (k == 0) r = (short)(ushort)((ushort)x << 1);
            else if (k == 1) r = (int)(short)(ushort)x;
            else if (k == 2) r = (long)(short)(ushort)x;
            else if (k == 3) r = (int)(sbyte)(byte)x;
            else if (k == 4) r = (long)(sbyte)(byte)x;
            else if (k == 5) r = (long)(uint)(short)x;
            else if (k == 6) r = (long)(uint)(sbyte)x;
            else if (k == 7) r = (int)(ushort)(short)x;
            else if (k == 8) r = (long)(ushort)(short)x;
            else if (k == 9) r = (long)(short)(sbyte)x;
            else if (k == 10) r = (long)(ushort)(sbyte)x;
            else if (k == 11) r = (long)(float)(short)(ushort)x;
            else if (k == 12) r = (long)(uint)(ushort)(short)x;
            else if (k == 13) r = (long)(uint)s;
            else if (k == 14) r = (long)(ulong)(uint)s;
            else if (k == 15) r = (long)(short)(ushort)(s + 1);
            else if (k == 16) r = (long)(uint)(int)l;
            else if (k == 17) r = (long)(int)(uint)l;
            else if (k == 18) r = (long)(ulong)(uint)(int)l;
            else if (k == 19) r = ((x & 1) != 0 ? (ushort)x : (ushort)(x >> 3)) + 1L;
            else if (k == 20) r = (long)(float)(uint)(short)x;
            else r = (long)(double)(uint)(int)l;
            o[i] = r;
        }

        [TestMethod]
        public async Task SubWordConversionChains_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 128;
            var xs = new int[n];
            var ss = new short[n];
            var ls = new long[n];
            int[] edges = { 0, -1, 0x8000, 0x7FFF, 0xFFFF, 0x80, 0xFF, -0x8001, 0x7F, 0x10000 };
            var rng = new Random(0x5C0);
            for (int k = 0; k < n; k++)
            {
                xs[k] = k < edges.Length ? edges[k] : rng.Next(int.MinValue, int.MaxValue);
                ss[k] = (short)rng.Next(-32768, 32768);
                ls[k] = rng.NextInt64(long.MinValue, long.MaxValue);
            }
            using var dX = accelerator.Allocate1D(xs);
            using var dS = accelerator.Allocate1D(ss);
            using var dL = accelerator.Allocate1D(ls);
            using var dO = accelerator.Allocate1D<long>(n * ConvChainCount);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<short>, ArrayView<long>, ArrayView<long>>(
                ConvChainsKernel)((Index1D)(n * ConvChainCount), dX.View, dS.View, dL.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<long>();
            for (int i = 0; i < n; i++)
            {
                var want = ConvChainsHost(xs[i], ss[i], ls[i]);
                for (int k = 0; k < ConvChainCount; k++)
                    if (got[i * ConvChainCount + k] != want[k])
                        throw new Exception($"{ConvChainNames[k]} with x = 0x{xs[i]:X}, s = {ss[i]}, l = 0x{ls[i]:X}: " +
                            $"0x{got[i * ConvChainCount + k]:X}, expected 0x{want[k]:X}");
            }
        });
    }
}
