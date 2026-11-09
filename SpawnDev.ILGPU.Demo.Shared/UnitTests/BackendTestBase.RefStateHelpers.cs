using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // [NoInlining] helpers taking a `ref` to a LOCAL structure of the kernel - SpawnDev.Codecs' VP9 parse shape (every
    // helper takes `ref Vp8BoolDecoderGpuState r`). On WebGPU (5.4.23) such a helper is a function with a ptr<function, T>
    // parameter instead of a copy at every call site (Parse: 821k lines of WGSL). Covered: the kernel passing its local
    // state, a helper passing its own reference on (with an early return out of a loop), a `ref int` accumulator, and a
    // view next to the reference.
    public abstract partial class BackendTestBase
    {
        struct RefShapeState
        {
            public uint Value;
            public int Count;
            public int Pos;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int RefShapeReadBit(ref RefShapeState s, ArrayView<byte> data)
        {
            if (s.Count < 8)
            {
                s.Value = (s.Value << 8) | data[s.Pos % data.IntLength];
                s.Pos++;
                s.Count += 8;
            }
            int bit = (int)(s.Value >> (s.Count - 1)) & 1;
            s.Count--;
            return bit;
        }

        // hands its own reference on; leaves the loop early on a large value
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int RefShapeReadBits(ref RefShapeState s, ArrayView<byte> data, int n)
        {
            int v = 0;
            for (int k = 0; k < n; k++)
            {
                v = (v << 1) | RefShapeReadBit(ref s, data);
                if (v > 40)
                    return -v;
            }
            return v;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RefShapeAccumulate(ref int total, ArrayView<byte> data, int at) =>
            total = total * 5 + data[at % data.IntLength];

        static void RefStateHelpersKernel(Index1D i, ArrayView<byte> data, ArrayView<int> output)
        {
            var s = new RefShapeState { Value = 0, Count = 0, Pos = i * 3 };
            int acc = 0;
            for (int k = 0; k < 6; k++)
                acc = acc * 7 + RefShapeReadBits(ref s, data, 3 + (k & 3));
            acc += RefShapeReadBit(ref s, data);
            int total = i;
            RefShapeAccumulate(ref total, data, i + 1);
            RefShapeAccumulate(ref total, data, s.Pos);
            output[i] = acc ^ s.Pos ^ (s.Count << 8) ^ (total << 12);
        }

        [TestMethod]
        public async Task RefStateHelpers_LocalStructByReference_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 96;
            var data = new byte[211];
            for (int k = 0; k < data.Length; k++) data[k] = (byte)((k * 151 + 7) ^ (k >> 2));
            using var dData = accelerator.Allocate1D(data);
            using var dOut = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>>(RefStateHelpersKernel);
            kernel((Index1D)n, dData.View, dOut.View);
            var got = await dOut.CopyToHostAsync<int>();

            // host oracle: the kernel method itself on CPU-accelerator views, one thread at a time
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hData = cpu.Allocate1D(data);
            using var hOut = cpu.Allocate1D<int>(n);
            for (int t = 0; t < n; t++) RefStateHelpersKernel(t, hData.View, hOut.View);
            var want = await hOut.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
                if (got[t] != want[t]) throw new Exception($"output[{t}] = {got[t]}, host {want[t]}");
        });
    }
}
