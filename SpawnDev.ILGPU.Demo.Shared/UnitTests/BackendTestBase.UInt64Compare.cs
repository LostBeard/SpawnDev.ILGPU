using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // ulong <, <=, >, >= across the sign bit, in the kernel and in a helper that is not inlined. ILGPU's IR has no UInt64
    // type: a ulong compare is an Int64 compare flagged unsigned. WebGL's emulated 64-bit compare ignored the flag and
    // called the signed i64_lt, so `err < ulong.MaxValue` was `err < -1` (false) and a helper's 64-bit `<` emitted
    // `uvec2 < uvec2`. Found through SpawnDev.Codecs (Geordi 2026-10-27): the BC7 encoder's `if (err < best)` with
    // `best = ulong.MaxValue` never took a candidate on WebGL, so every block came out 0x40 then zeros.
    public abstract partial class BackendTestBase
    {
        static readonly ulong[] UInt64CompareValues =
        {
            0UL, 1UL, 0x7FFFFFFFUL, 0x80000000UL, 0xFFFFFFFFUL, 0x100000000UL, 0x7FFFFFFFFFFFFFFFUL,
            0x8000000000000000UL, 0x8000000000000001UL, 0xFFFFFFFF00000000UL, 0xFFFFFFFFFFFFFFFEUL, ulong.MaxValue,
        };

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int UInt64CompareHelper(ulong a, ulong b) =>
            (a < b ? 1 : 0) | (a <= b ? 2 : 0) | (a > b ? 4 : 0) | (a >= b ? 8 : 0);

        static void UInt64CompareKernel(Index1D i, ArrayView<int> words, ArrayView<int> o)
        {
            int n = words.IntLength / 2;
            ulong a = (ulong)(uint)words[(i / n) * 2] | ((ulong)(uint)words[(i / n) * 2 + 1] << 32);
            ulong b = (ulong)(uint)words[(i % n) * 2] | ((ulong)(uint)words[(i % n) * 2 + 1] << 32);
            o[i * 4 + 0] = (a < b ? 1 : 0) | (a <= b ? 2 : 0) | (a > b ? 4 : 0) | (a >= b ? 8 : 0);
            o[i * 4 + 1] = UInt64CompareHelper(a, b);
            o[i * 4 + 2] = (a < ulong.MaxValue ? 1 : 0) | (0x8000000000000000UL > a ? 2 : 0);
            // The BC7 shape: a running minimum that starts at ulong.MaxValue.
            ulong best = ulong.MaxValue;
            int took = 0;
            if (a < best) { best = a; took = 1; }
            if (b < best) { best = b; took |= 2; }
            o[i * 4 + 3] = took | ((int)(uint)(best >> 32) == (int)(uint)((a < b ? a : b) >> 32) ? 4 : 0);
        }

        [TestMethod]
        public async Task UInt64Compare_AcrossSignBit_MatchesOracle() => await RunTest(async accelerator =>
        {
            var v = UInt64CompareValues;
            int n = v.Length;
            var words = new int[n * 2];
            for (int k = 0; k < n; k++) { words[k * 2] = (int)(uint)v[k]; words[k * 2 + 1] = (int)(uint)(v[k] >> 32); }
            using var dWords = accelerator.Allocate1D(words);
            using var dO = accelerator.Allocate1D<int>(n * n * 4);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(UInt64CompareKernel)(
                (Index1D)(n * n), dWords.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            string[] what = { "kernel compare", "helper compare", "compare with a constant", "running minimum" };
            for (int i = 0; i < n * n; i++)
            {
                ulong a = v[i / n], b = v[i % n];
                int bits = (a < b ? 1 : 0) | (a <= b ? 2 : 0) | (a > b ? 4 : 0) | (a >= b ? 8 : 0);
                ulong best = ulong.MaxValue;
                int took = 0;
                if (a < best) { best = a; took = 1; }
                if (b < best) { best = b; took |= 2; }
                var want = new[] { bits, bits, (a < ulong.MaxValue ? 1 : 0) | (0x8000000000000000UL > a ? 2 : 0), took | 4 };
                for (int k = 0; k < 4; k++)
                    if (got[i * 4 + k] != want[k])
                        throw new Exception($"{what[k]} of 0x{a:X16} and 0x{b:X16}: {got[i * 4 + k]}, expected {want[k]}");
            }
        });
    }
}
