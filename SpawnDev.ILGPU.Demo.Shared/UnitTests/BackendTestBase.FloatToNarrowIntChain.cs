using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // (byte)(int)f is two conversions: truncate the float to an int, then keep the low 8 bits. The IR folded the pair into ONE
    // float-to-8-bit conversion (the float types sort after the integers, so "an existing narrowing followed by a smaller
    // narrowing" matched), and PTX implements a float-to-8-bit cvt as SATURATING to the 8-bit range: every value >= 128 came
    // out 127 on CUDA (SpawnDev.Codecs' float RGB -> RGBA8 kernel, 13.8 dB on CUDA vs 30 dB elsewhere; Geordi 2026-11-12).
    // The same fold made (int)(long)d one double-to-int conversion, which differs beyond the int range. Every value here is
    // in range for the FIRST conversion, so the C# result is defined; the host computes it the same way.
    public abstract partial class BackendTestBase
    {
        static void FloatToNarrowIntChainKernel(Index1D i, ArrayView<float> f, ArrayView<double> d,
            ArrayView<int> asByte, ArrayView<int> asSByte, ArrayView<int> asShort, ArrayView<int> asUShort, ArrayView<int> asIntViaLong)
        {
            float x = f[i];
            float c = x < 0f ? 0f : x > 255f ? 255f : x;
            asByte[i] = (byte)(int)c;
            asSByte[i] = (sbyte)(int)x;
            asShort[i] = (short)(int)x;
            asUShort[i] = (ushort)(int)x;
            asIntViaLong[i] = (int)(long)d[i];
        }

        [TestMethod]
        public async Task FloatToNarrowInt_ThroughInt_KeepsTheLowBits() => await RunTest(async accelerator =>
        {
            var f = new[] { 0f, 1.5f, 127.9f, 128.2f, 200.5f, 254.9f, 255f, 300.7f, -1.5f, -129.6f, 40000.5f, -40000.5f, 70000.9f, 32768.25f };
            var d = new[] { 0d, 1.5, -2.5, 2147483647.0, 3e9, -3e9, 5e9 + 0.75, -1e12 };
            int n = Math.Max(f.Length, d.Length);
            Array.Resize(ref f, n);
            Array.Resize(ref d, n);
            using var bf = accelerator.Allocate1D(f);
            using var bd = accelerator.Allocate1D(d);
            using var oB = accelerator.Allocate1D<int>(n);
            using var oSB = accelerator.Allocate1D<int>(n);
            using var oS = accelerator.Allocate1D<int>(n);
            using var oUS = accelerator.Allocate1D<int>(n);
            using var oL = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<double>,
                ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(FloatToNarrowIntChainKernel);
            kernel((Index1D)n, bf.View, bd.View, oB.View, oSB.View, oS.View, oUS.View, oL.View);
            var gB = await oB.CopyToHostAsync<int>();
            var gSB = await oSB.CopyToHostAsync<int>();
            var gS = await oS.CopyToHostAsync<int>();
            var gUS = await oUS.CopyToHostAsync<int>();
            var gL = await oL.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                float x = f[i];
                float c = x < 0f ? 0f : x > 255f ? 255f : x;
                int wantB = (byte)(int)c, wantSB = (sbyte)(int)x, wantS = (short)(int)x, wantUS = (ushort)(int)x;
                int wantL = unchecked((int)(long)d[i]);
                if (gB[i] != wantB) throw new Exception($"(byte)(int){c}: got {gB[i]}, expected {wantB}");
                if (gSB[i] != wantSB) throw new Exception($"(sbyte)(int){x}: got {gSB[i]}, expected {wantSB}");
                if (gS[i] != wantS) throw new Exception($"(short)(int){x}: got {gS[i]}, expected {wantS}");
                if (gUS[i] != wantUS) throw new Exception($"(ushort)(int){x}: got {gUS[i]}, expected {wantUS}");
                if (gL[i] != wantL) throw new Exception($"(int)(long){d[i]}: got {gL[i]}, expected {wantL}");
            }
        });
    }
}
