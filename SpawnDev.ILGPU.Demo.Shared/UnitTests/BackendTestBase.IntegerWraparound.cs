using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Signed integer arithmetic in a kernel WRAPS, as it does in C# (unchecked): int.MaxValue + 1 == int.MinValue.
    //
    // Found 2026-11-13 with DemoConsole `int-op-cost`: on OpenCL the loop
    //     x = (x * 1664525 + 1013904223) & 0x7FFFFFFF;  acc += x;
    // returned other sums than CUDA and a host computation. The OpenCL backend emitted the multiply and the add as
    // signed `int` operations, and in OpenCL C (as in C) signed overflow is undefined: NVIDIA's compiler concluded
    // that a product of non-negative values plus a positive constant is non-negative and DELETED the mask. Any
    // kernel that relies on wrap-around (hashes, random generators, checksums, fixed-point code) could differ
    // silently on OpenCL. The backend now does signed +, -, *, << and unary - on the unsigned type of the same
    // width (wrap-around is defined there) and converts back.
    //
    // The shapes: the loop that exposed it (32 and 64 bit), and the expressions a compiler folds when it may assume
    // "no overflow": (a + c) > a, (a * 2) / 2, a + b - b compared, -a at the minimum, a shift into the sign bit.
    // Every backend, against the same expressions computed on the host.
    public abstract partial class BackendTestBase
    {
        const int IntWrap_Slots = 12;

        static void IntWrap_MaskedLcg32Kernel(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
        {
            int x = seed[i], acc = 0;
            for (int k = 0; k < iterations; k++)
            {
                x = (x * 1664525 + 1013904223) & 0x7FFFFFFF;
                acc += x;
            }
            sum[i] = acc;
        }

        static void IntWrap_MaskedLcg64Kernel(Index1D i, ArrayView<long> seed, ArrayView<long> sum, int iterations)
        {
            long x = seed[i], acc = 0;
            for (int k = 0; k < iterations; k++)
            {
                x = (x * 6364136223846793005L + 1442695040888963407L) & 0x7FFFFFFFFFFFFFFFL;
                acc += x;
            }
            sum[i] = acc;
        }

        static void IntWrap_Ops32Kernel(Index1D i, ArrayView<int> a, ArrayView<int> b, ArrayView<int> o)
        {
            int x = a[i], y = b[i];
            int at = i.X * IntWrap_Slots;
            o[at + 0] = x + y;
            o[at + 1] = x - y;
            o[at + 2] = x * y;
            o[at + 3] = -x;
            o[at + 4] = x << (y & 31);
            o[at + 5] = (x + 1) > x ? 1 : 0;              // false at int.MaxValue
            o[at + 6] = (x + y) < x ? 1 : 0;              // true when a positive y wraps the sum
            o[at + 7] = (x * 2) / 2;                      // not x when x * 2 wraps
            o[at + 8] = (x - 1) < x ? 1 : 0;              // false at int.MinValue
            o[at + 9] = (x + 1013904223) & 0x7FFFFFFF;    // the mask must stay
            o[at + 10] = (x * 1664525) >> 3;
            o[at + 11] = x + y - y == x ? 1 : 0;          // always true with wrap-around
        }

        static void IntWrap_Ops64Kernel(Index1D i, ArrayView<long> a, ArrayView<long> b, ArrayView<long> o)
        {
            long x = a[i], y = b[i];
            int at = i.X * IntWrap_Slots;
            o[at + 0] = x + y;
            o[at + 1] = x - y;
            o[at + 2] = x * y;
            o[at + 3] = -x;
            o[at + 4] = x << (int)(y & 63);
            o[at + 5] = (x + 1) > x ? 1 : 0;
            o[at + 6] = (x + y) < x ? 1 : 0;
            o[at + 7] = (x * 2) / 2;
            o[at + 8] = (x - 1) < x ? 1 : 0;
            o[at + 9] = (x + 1442695040888963407L) & 0x7FFFFFFFFFFFFFFFL;
            o[at + 10] = (x * 6364136223846793005L) >> 3;
            o[at + 11] = x + y - y == x ? 1 : 0;
        }

        static int[] IntWrap_Values32()
        {
            var v = new List<int>
            {
                0, 1, -1, 2, -2, 46340, 46341, -46341, 65535, 65536, 0x3FFFFFFF, 0x40000000, 0x40000001,
                int.MaxValue, int.MaxValue - 1, int.MinValue, int.MinValue + 1, 1013904223, -1013904223, 0x7FFF0000,
            };
            var rng = new Random(20261113);
            for (int k = 0; k < 44; k++) v.Add(rng.Next(int.MinValue, int.MaxValue));
            return v.ToArray();
        }

        [TestMethod]
        public async Task IntegerWraparound_Int32_MatchesHost() => await RunTest(async accelerator =>
        {
            // every value against every value
            int[] values = IntWrap_Values32();
            int n = values.Length * values.Length;
            var a = new int[n]; var b = new int[n];
            for (int p = 0; p < n; p++) { a[p] = values[p / values.Length]; b[p] = values[p % values.Length]; }
            var expected = new int[n * IntWrap_Slots];
            unchecked
            {
                for (int p = 0; p < n; p++)
                {
                    int x = a[p], y = b[p], at = p * IntWrap_Slots;
                    expected[at + 0] = x + y;
                    expected[at + 1] = x - y;
                    expected[at + 2] = x * y;
                    expected[at + 3] = -x;
                    expected[at + 4] = x << (y & 31);
                    expected[at + 5] = (x + 1) > x ? 1 : 0;
                    expected[at + 6] = (x + y) < x ? 1 : 0;
                    expected[at + 7] = (x * 2) / 2;
                    expected[at + 8] = (x - 1) < x ? 1 : 0;
                    expected[at + 9] = (x + 1013904223) & 0x7FFFFFFF;
                    expected[at + 10] = (x * 1664525) >> 3;
                    expected[at + 11] = x + y - y == x ? 1 : 0;
                }
            }
            using var dA = accelerator.Allocate1D(a);
            using var dB = accelerator.Allocate1D(b);
            using var dO = accelerator.Allocate1D<int>(n * IntWrap_Slots);
            var ops = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(IntWrap_Ops32Kernel);
            ops(n, dA.View, dB.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            string[] names = { "x + y", "x - y", "x * y", "-x", "x << (y & 31)", "(x + 1) > x", "(x + y) < x", "(x * 2) / 2", "(x - 1) < x",
                "(x + 1013904223) & 0x7FFFFFFF", "(x * 1664525) >> 3", "x + y - y == x" };
            var failures = new List<string>();
            for (int p = 0; p < n && failures.Count < 12; p++)
                for (int s = 0; s < IntWrap_Slots; s++)
                    if (got[p * IntWrap_Slots + s] != expected[p * IntWrap_Slots + s])
                        failures.Add($"{names[s]} with x = {a[p]}, y = {b[p]}: {got[p * IntWrap_Slots + s]}, expected {expected[p * IntWrap_Slots + s]}");
            if (failures.Count > 0) throw new Exception($"{accelerator.AcceleratorType}: signed 32-bit arithmetic does not wrap:\n" + string.Join("\n", failures));

            // the loop that exposed it
            const int threads = 256, iterations = 500;
            var seed = new int[threads];
            for (int t = 0; t < threads; t++) seed[t] = unchecked(t * 7919 + 12345);
            var expectedSum = new int[threads];
            unchecked
            {
                for (int t = 0; t < threads; t++)
                {
                    int x = seed[t], acc = 0;
                    for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x; }
                    expectedSum[t] = acc;
                }
            }
            using var dSeed = accelerator.Allocate1D(seed);
            using var dSum = accelerator.Allocate1D<int>(threads);
            var lcg = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(IntWrap_MaskedLcg32Kernel);
            lcg(threads, dSeed.View, dSum.View, iterations);
            await accelerator.SynchronizeAsync();
            var gotSum = await dSum.CopyToHostAsync<int>();
            for (int t = 0; t < threads; t++)
                if (gotSum[t] != expectedSum[t])
                    throw new Exception($"{accelerator.AcceleratorType}: masked generator loop, thread {t} (seed {seed[t]}): sum {gotSum[t]}, expected {expectedSum[t]}");
        });

        [TestMethod]
        public async Task IntegerWraparound_Int64_MatchesHost() => await RunEmulatedTest(async accelerator =>
        {
            var values = new List<long>
            {
                0, 1, -1, 2, -2, 3037000499L, 3037000500L, -3037000500L, 0xFFFFFFFFL, 0x100000000L, 0x3FFFFFFFFFFFFFFFL,
                0x4000000000000000L, 0x4000000000000001L, long.MaxValue, long.MaxValue - 1, long.MinValue, long.MinValue + 1,
                1442695040888963407L, -1442695040888963407L, int.MaxValue, int.MinValue,
            };
            var rng = new Random(20261114);
            for (int k = 0; k < 19; k++) values.Add(rng.NextInt64(long.MinValue, long.MaxValue));
            int n = values.Count * values.Count;
            var a = new long[n]; var b = new long[n];
            for (int p = 0; p < n; p++) { a[p] = values[p / values.Count]; b[p] = values[p % values.Count]; }
            var expected = new long[n * IntWrap_Slots];
            unchecked
            {
                for (int p = 0; p < n; p++)
                {
                    long x = a[p], y = b[p]; int at = p * IntWrap_Slots;
                    expected[at + 0] = x + y;
                    expected[at + 1] = x - y;
                    expected[at + 2] = x * y;
                    expected[at + 3] = -x;
                    expected[at + 4] = x << (int)(y & 63);
                    expected[at + 5] = (x + 1) > x ? 1 : 0;
                    expected[at + 6] = (x + y) < x ? 1 : 0;
                    expected[at + 7] = (x * 2) / 2;
                    expected[at + 8] = (x - 1) < x ? 1 : 0;
                    expected[at + 9] = (x + 1442695040888963407L) & 0x7FFFFFFFFFFFFFFFL;
                    expected[at + 10] = (x * 6364136223846793005L) >> 3;
                    expected[at + 11] = x + y - y == x ? 1 : 0;
                }
            }
            using var dA = accelerator.Allocate1D(a);
            using var dB = accelerator.Allocate1D(b);
            using var dO = accelerator.Allocate1D<long>(n * IntWrap_Slots);
            var ops = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>, ArrayView<long>>(IntWrap_Ops64Kernel);
            ops(n, dA.View, dB.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<long>();
            string[] names = { "x + y", "x - y", "x * y", "-x", "x << (y & 63)", "(x + 1) > x", "(x + y) < x", "(x * 2) / 2", "(x - 1) < x",
                "(x + c) & mask", "(x * c) >> 3", "x + y - y == x" };
            var failures = new List<string>();
            for (int p = 0; p < n && failures.Count < 12; p++)
                for (int s = 0; s < IntWrap_Slots; s++)
                    if (got[p * IntWrap_Slots + s] != expected[p * IntWrap_Slots + s])
                        failures.Add($"{names[s]} with x = {a[p]}, y = {b[p]}: {got[p * IntWrap_Slots + s]}, expected {expected[p * IntWrap_Slots + s]}");
            if (failures.Count > 0) throw new Exception($"{accelerator.AcceleratorType}: signed 64-bit arithmetic does not wrap:\n" + string.Join("\n", failures));

            const int threads = 128, iterations = 300;
            var seed = new long[threads];
            for (int t = 0; t < threads; t++) seed[t] = unchecked(t * 7919L * 1000003L + 12345);
            var expectedSum = new long[threads];
            unchecked
            {
                for (int t = 0; t < threads; t++)
                {
                    long x = seed[t], acc = 0;
                    for (int k = 0; k < iterations; k++) { x = (x * 6364136223846793005L + 1442695040888963407L) & 0x7FFFFFFFFFFFFFFFL; acc += x; }
                    expectedSum[t] = acc;
                }
            }
            using var dSeed = accelerator.Allocate1D(seed);
            using var dSum = accelerator.Allocate1D<long>(threads);
            var lcg = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, ArrayView<long>, int>(IntWrap_MaskedLcg64Kernel);
            lcg(threads, dSeed.View, dSum.View, iterations);
            await accelerator.SynchronizeAsync();
            var gotSum = await dSum.CopyToHostAsync<long>();
            for (int t = 0; t < threads; t++)
                if (gotSum[t] != expectedSum[t])
                    throw new Exception($"{accelerator.AcceleratorType}: masked 64-bit generator loop, thread {t}: sum {gotSum[t]}, expected {expectedSum[t]}");
        });
    }
}
