using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Arithmetic on comparison results, against the host. Roslyn keeps `c ? 1 : 0` as the raw comparison (clt / cgt / ceq
    // push an int32 0 or 1), so -(c ? 1 : 0) is `clt; neg` and ~(c ? 1 : 0) is `clt; not`. ILGPU's frontend held the
    // comparison as a bool and made the IL neg / not a Neg / Not of that bool, the logical not: Velocity computed 1 for
    // `-(a < 0 ? 1 : 0)` with a >= 0 and 0 with a < 0 (0 and -1 in IL). Found through SpawnDev.Codecs: libopus'
    // ec_laplace_encode takes `s = -(val < 0)` for the sign, so the CELT encoder's coarse energy came out wrong on Velocity.
    // The binary forms (add, sub, mul, shift of comparisons) already widened and are here as guards.
    public abstract partial class BackendTestBase
    {
        const int ComparisonArithmeticCount = 8;

        static int ComparisonArithmeticHost(int a, int b, int k) => k switch
        {
            0 => -(a < 0 ? 1 : 0),
            1 => ~(a < b ? 1 : 0),
            2 => (a < b ? 1 : 0) + (a > 3 ? 1 : 0),
            3 => (a < b ? 1 : 0) - (b < 0 ? 1 : 0),
            4 => (a == b ? 1 : 0) * 7 + (a > b ? 1 : 0),
            5 => (a < b ? 1 : 0) << 3,
            6 => ((a < 0 ? 1 : 0) + (b < 0 ? 1 : 0)) * ((a & 1) == 0 ? 1 : 0),
            _ => -(a > b ? 1 : 0) & 0x55,
        };

        // One output per thread: thread i computes expression i % 8 of input i / 8 (WebGL captures one output per thread).
        static void ComparisonArithmeticKernel(Index1D i, ArrayView<int> av, ArrayView<int> bv, ArrayView<int> o)
        {
            int j = i / ComparisonArithmeticCount, k = i % ComparisonArithmeticCount;
            int a = av[j], b = bv[j];
            int r;
            if (k == 0) r = -(a < 0 ? 1 : 0);
            else if (k == 1) r = ~(a < b ? 1 : 0);
            else if (k == 2) r = (a < b ? 1 : 0) + (a > 3 ? 1 : 0);
            else if (k == 3) r = (a < b ? 1 : 0) - (b < 0 ? 1 : 0);
            else if (k == 4) r = (a == b ? 1 : 0) * 7 + (a > b ? 1 : 0);
            else if (k == 5) r = (a < b ? 1 : 0) << 3;
            else if (k == 6) r = ((a < 0 ? 1 : 0) + (b < 0 ? 1 : 0)) * ((a & 1) == 0 ? 1 : 0);
            else r = -(a > b ? 1 : 0) & 0x55;
            o[i] = r;
        }

        [TestMethod]
        public async Task ComparisonArithmetic_NegNotAddOfComparisons_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 48;
            var a = new int[n];
            var b = new int[n];
            var rng = new Random(0xB001);
            for (int k = 0; k < n; k++)
            {
                a[k] = rng.Next(-10, 11);
                b[k] = k % 5 == 0 ? a[k] : rng.Next(-10, 11);
            }
            using var dA = accelerator.Allocate1D(a);
            using var dB = accelerator.Allocate1D(b);
            using var dO = accelerator.Allocate1D<int>(n * ComparisonArithmeticCount);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(ComparisonArithmeticKernel)(
                (Index1D)(n * ComparisonArithmeticCount), dA.View, dB.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            string[] names = { "-(a < 0 ? 1 : 0)", "~(a < b ? 1 : 0)", "(a < b ? 1 : 0) + (a > 3 ? 1 : 0)", "(a < b ? 1 : 0) - (b < 0 ? 1 : 0)",
                "(a == b ? 1 : 0) * 7 + (a > b ? 1 : 0)", "(a < b ? 1 : 0) << 3", "((a < 0 ? 1 : 0) + (b < 0 ? 1 : 0)) * ((a & 1) == 0 ? 1 : 0)",
                "-(a > b ? 1 : 0) & 0x55" };
            for (int i = 0; i < n; i++)
                for (int k = 0; k < ComparisonArithmeticCount; k++)
                {
                    int want = ComparisonArithmeticHost(a[i], b[i], k);
                    if (got[i * ComparisonArithmeticCount + k] != want)
                        throw new Exception($"{names[k]} with a = {a[i]}, b = {b[i]}: {got[i * ComparisonArithmeticCount + k]}, expected {want}");
                }
        });
    }
}
