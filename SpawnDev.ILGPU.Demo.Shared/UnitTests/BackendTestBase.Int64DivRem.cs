using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 64-bit integer division and remainder. WebGPU and WebGL emulate a long as two 32-bit words (vec2<u32> / uvec2), and
    // `/` and `%` on those vectors divided the words INDEPENDENTLY: right only while both values fit in 32 bits (WGSL
    // defines the high words' 0 / 0; GLSL does not - SpawnDev.Codecs' MDCT argument reduction came out wrong on WebGL),
    // wrong for negative values and values of 2^32 and more on both. Signed and unsigned, against .NET.
    public abstract partial class BackendTestBase
    {
        static readonly long[] DivRemDividends =
        {
            0, 1, 7, -7, 2145, 1L << 32, (1L << 32) + 5, -(1L << 33) - 3, 0x123456789ABCDEFL, -0x123456789ABCDEFL,
            long.MaxValue, long.MinValue + 1, 1000000007L * 999999937L, -5, 4294967295L, 4294967296L * 3 + 1,
        };
        static readonly long[] DivRemDivisors = { 1, 3, -3, 512, 4294967297L, -(1L << 31), 0x10000000FL, 1000000007L };

        static void Int64DivRemKernel(Index1D idx, ArrayView<long> a, ArrayView<long> b, ArrayView<long> quot, ArrayView<long> rem,
            ArrayView<ulong> uquot, ArrayView<ulong> urem)
        {
            long x = a[idx], y = b[idx];
            quot[idx] = x / y;
            rem[idx] = x % y;
            uquot[idx] = (ulong)x / (ulong)y;
            urem[idx] = (ulong)x % (ulong)y;
        }

        [TestMethod]
        public async Task Int64_DivRem_SignedAndUnsigned_MatchDotNet() => await RunEmulatedTest(async accelerator =>
        {
            int n = DivRemDividends.Length * DivRemDivisors.Length;
            var a = new long[n];
            var b = new long[n];
            for (int i = 0; i < DivRemDividends.Length; i++)
                for (int j = 0; j < DivRemDivisors.Length; j++)
                {
                    a[i * DivRemDivisors.Length + j] = DivRemDividends[i];
                    b[i * DivRemDivisors.Length + j] = DivRemDivisors[j];
                }
            using var dA = accelerator.Allocate1D(a);
            using var dB = accelerator.Allocate1D(b);
            using var dQ = accelerator.Allocate1D<long>(n);
            using var dR = accelerator.Allocate1D<long>(n);
            using var dUQ = accelerator.Allocate1D<ulong>(n);
            using var dUR = accelerator.Allocate1D<ulong>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>, ArrayView<long>, ArrayView<long>, ArrayView<ulong>, ArrayView<ulong>>(
                Int64DivRemKernel)((Index1D)n, dA.View, dB.View, dQ.View, dR.View, dUQ.View, dUR.View);
            await accelerator.SynchronizeAsync();
            var q = await dQ.CopyToHostAsync<long>();
            var r = await dR.CopyToHostAsync<long>();
            var uq = await dUQ.CopyToHostAsync<ulong>();
            var ur = await dUR.CopyToHostAsync<ulong>();
            for (int i = 0; i < n; i++)
            {
                if (q[i] != a[i] / b[i]) throw new Exception($"{a[i]} / {b[i]} = {q[i]}, expected {a[i] / b[i]}");
                if (r[i] != a[i] % b[i]) throw new Exception($"{a[i]} % {b[i]} = {r[i]}, expected {a[i] % b[i]}");
                if (uq[i] != (ulong)a[i] / (ulong)b[i]) throw new Exception($"(ulong){a[i]} / (ulong){b[i]} = {uq[i]}, expected {(ulong)a[i] / (ulong)b[i]}");
                if (ur[i] != (ulong)a[i] % (ulong)b[i]) throw new Exception($"(ulong){a[i]} % (ulong){b[i]} = {ur[i]}, expected {(ulong)a[i] % (ulong)b[i]}");
            }
        });
    }
}
