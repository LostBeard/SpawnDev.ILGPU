using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // double -> Half / BFloat16 / FP8 / FP4 must round ONCE, to the nearest value of the type (ties to even), as
    // System.Half does from a double. Going through float first rounds twice: a double just above a midpoint of the type
    // becomes the exact midpoint in float and then ties to even, the wrong way. The inputs are midpoints of each type's
    // grid moved by +-k * 2^-30 (relative): below float precision, so a float detour collapses them onto the tie. The
    // expected value comes from the grid itself (nearest code by exact double distance), not from any conversion code.
    public abstract partial class BackendTestBase
    {
        public static void DoubleToHalfKernel(Index1D i, ArrayView<double> src, ArrayView<float> dst) => dst[i] = (float)(global::ILGPU.Half)src[i];
        public static void DoubleToBFloat16Kernel(Index1D i, ArrayView<double> src, ArrayView<float> dst) => dst[i] = (float)(BFloat16)src[i];
        public static void DoubleToE4M3Kernel(Index1D i, ArrayView<double> src, ArrayView<float> dst) => dst[i] = (float)(Float8E4M3)src[i];
        public static void DoubleToE5M2Kernel(Index1D i, ArrayView<double> src, ArrayView<float> dst) => dst[i] = (float)(Float8E5M2)src[i];
        public static void DoubleToE2M1Kernel(Index1D i, ArrayView<double> src, ArrayView<float> dst) => dst[i] = (float)(Float4E2M1)src[i];
        public static void DoubleToSingleKernel(Index1D i, ArrayView<double> src, ArrayView<float> dst) => dst[i] = (float)src[i];

        /// <summary>
        /// Smallest magnitude a double keeps every bit of weight >= 2^-126 at in the Dekker / Ozaki emulation (its
        /// components are f32, and D3D flushes f32 subnormals): the +-2^-30 relative offset of a midpoint at or above
        /// 2^-96 is still a normal f32. Below it those modes hold less (F64EmulationMode docs); Ieee holds every double.
        /// </summary>
        const double F32ComponentFloor = 1.2621774483536189e-29; // 2^-96

        /// <summary>
        /// Runs <paramref name="body"/> once per double representation the accelerator has: native doubles once (mode
        /// null); on WebGPU / WebGL once per emulation mode (Dekker and Ozaki hold f32 components only, see
        /// <see cref="F32ComponentFloor"/>; Ieee holds every double).
        /// </summary>
        static async Task ForEachDoubleRepresentation(Accelerator accelerator, Func<string, F64EmulationMode?, Task> body)
        {
            F64EmulationMode? saved = accelerator is WebGPUAccelerator webgpu ? webgpu.F64Mode
                : accelerator is WebGLAccelerator webgl ? webgl.F64Mode : null;
            if (saved == null)
            {
                await body(accelerator.AcceleratorType.ToString(), null);
                return;
            }
            try
            {
                foreach (var mode in new[] { F64EmulationMode.Dekker, F64EmulationMode.Ozaki, F64EmulationMode.Ieee })
                {
                    if (accelerator is WebGPUAccelerator w) w.F64Mode = mode;
                    else ((WebGLAccelerator)accelerator).F64Mode = mode;
                    await body($"{accelerator.AcceleratorType} {mode}", mode);
                }
            }
            finally
            {
                if (accelerator is WebGPUAccelerator w) w.F64Mode = saved.Value;
                else ((WebGLAccelerator)accelerator).F64Mode = saved.Value;
            }
        }

        /// <summary>The positive finite values of a type, ascending (sign-magnitude codes 0..max are monotonic).</summary>
        static double[] PositiveGrid(int maxCode, Func<int, float> decode)
        {
            var grid = new double[maxCode + 1];
            for (int c = 0; c <= maxCode; c++) grid[c] = decode(c);
            for (int c = 1; c <= maxCode; c++)
                if (!(grid[c] > grid[c - 1])) throw new Exception($"grid not ascending at code {c}");
            return grid;
        }

        /// <summary>The correctly rounded value of d on the grid: nearest by exact double distance, ties to the even code.</summary>
        static float NearestOnGrid(double[] grid, double d)
        {
            double m = Math.Abs(d);
            int hi = Array.BinarySearch(grid, m);
            double r;
            if (hi >= 0) r = grid[hi];
            else
            {
                hi = ~hi;
                if (hi >= grid.Length) throw new Exception($"{d:R} is beyond the grid");
                int lo = hi - 1;
                double dl = m - grid[lo], dh = grid[hi] - m; // exact: neighbours within a factor of 2 of m (or 0)
                r = dl < dh ? grid[lo] : dh < dl ? grid[hi] : grid[(lo & 1) == 0 ? lo : hi];
            }
            return (float)(d < 0 || (d == 0 && double.IsNegative(d)) ? -r : r);
        }

        static double[] MidpointInputs(double[] grid, int count, Random rng)
        {
            var list = new List<double>();
            while (list.Count < count)
            {
                int c = rng.Next(0, grid.Length - 1);
                double mid = (grid[c] + grid[c + 1]) / 2; // exact
                if (mid == 0) continue;
                int k = rng.Next(-3, 4);
                double d = mid + k * mid * Math.Pow(2, -30);
                list.Add(rng.Next(2) == 0 ? d : -d);
            }
            return list.ToArray();
        }

        [TestMethod]
        public async Task LowPrecisionFromDouble_RoundsOnce() => await RunTest(async accelerator =>
        {
            var rng = new Random(20261114);
            const int n = 4096;
            var types = new (string Name, int MaxCode, Func<int, float> Decode, Action<Index1D, ArrayView<double>, ArrayView<float>> Kernel, Func<double, float> Host)[]
            {
                ("Half", 0x7BFF, c => (float)global::ILGPU.Half.FromRawBits((ushort)c), DoubleToHalfKernel, d => (float)(global::ILGPU.Half)d),
                ("BFloat16", 0x7F7F, c => (float)BFloat16.FromRawBits((ushort)c), DoubleToBFloat16Kernel, d => (float)(BFloat16)d),
                ("Float8E4M3", 0x7E, c => (float)Float8E4M3.FromRawBits((byte)c), DoubleToE4M3Kernel, d => (float)(Float8E4M3)d),
                ("Float8E5M2", 0x7B, c => (float)Float8E5M2.FromRawBits((byte)c), DoubleToE5M2Kernel, d => (float)(Float8E5M2)d),
                ("Float4E2M1", 0x7, c => (float)Float4E2M1.FromRawBits((byte)c), DoubleToE2M1Kernel, d => (float)(Float4E2M1)d),
            };
            // The inputs once per type (one rng sequence), then each double representation of the accelerator
            var cases = new List<(string Name, double[] Grid, double[] Input, Action<Index1D, ArrayView<double>, ArrayView<float>> Kernel, Func<double, float> Host)>();
            foreach (var t in types)
            {
                var grid = PositiveGrid(t.MaxCode, t.Decode);
                var input = MidpointInputs(grid, n, rng);
                // The oracle itself, against .NET's own double -> Half (one rounding, IEEE)
                if (t.Name == "Half")
                    foreach (var d in input)
                        if (!SameFloat(NearestOnGrid(grid, d), (float)(System.Half)d))
                            throw new Exception($"test oracle disagrees with System.Half at {d:R}");
                cases.Add((t.Name, grid, input, t.Kernel, t.Host));
            }
            var failures = new List<string>();
            await ForEachDoubleRepresentation(accelerator, async (label, mode) =>
            {
                bool limited = mode is F64EmulationMode.Dekker or F64EmulationMode.Ozaki;
                foreach (var t in cases)
                {
                    var input = limited ? Array.FindAll(t.Input, d => Math.Abs(d) >= F32ComponentFloor) : t.Input;
                    // a limited run must still cover most of the grid, or it proves nothing
                    if (input.Length < t.Input.Length * 3 / 4) throw new Exception($"{label} {t.Name}: only {input.Length} inputs above the floor");
                    using var dSrc = accelerator.Allocate1D(input);
                    using var dDst = accelerator.Allocate1D<float>(input.Length);
                    var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<float>>(t.Kernel);
                    kernel(input.Length, dSrc.View, dDst.View);
                    var got = await dDst.CopyToHostAsync<float>();
                    int bad = 0, hostBad = 0; string first = "";
                    for (int k = 0; k < input.Length; k++)
                    {
                        float e = NearestOnGrid(t.Grid, input[k]);
                        if (!SameFloat(t.Host(input[k]), e)) hostBad++;
                        if (!SameFloat(got[k], e)) { if (bad++ == 0) first = $"{input[k]:R} -> {got[k]:R}, expected {e:R}"; }
                    }
                    if (hostBad > 0) failures.Add($"{t.Name}: the host's own (T)double is wrong on {hostBad}");
                    if (bad > 0) failures.Add($"{label} {t.Name}: {bad} of {input.Length} (first: {first})");
                }
            });
            if (failures.Count > 0)
                throw new Exception(string.Join("; ", failures));
        });

        /// <summary>
        /// (float)double rounds to the nearest float, ties to even, as .NET does: doubles outside the float exponent range
        /// give the nearest float (a subnormal, zero or infinity) and small doubles just past a float midpoint round up.
        /// WebGPU / WebGL Dekker and Ozaki loaded a double outside the f32 range as 1.m * 2^-126 or 1.m * 2^127 whatever
        /// its exponent (1e-300 -> 1.6e-38, 1e300 -> 2.5e38), and WebGPU dropped the low 29 bits of every double below
        /// 2^-79 (ldexp lowered to x * exp2(k) with exp2(k) flushed), so a double just above a float midpoint came back
        /// rounded down (2026-11-15).
        /// </summary>
        [TestMethod]
        public async Task DoubleToSingle_RoundsToNearest_AcrossTheFloatRange() => await RunTest(async accelerator =>
        {
            var inputs = new List<double>
            {
                1e-300, -1e-300, 1e300, -1e300, 5e-324, -5e-324, 1e-60, 1e60,
                3.168324620401429e-39, -7.5e-41, 1.401298464324817e-45, 7.006492321624087e-46, 7.006492321624085e-46,
                3.4028235677973366e38, -3.4028235677973366e38, 3.4028235677973370e38, double.MaxValue, -double.Epsilon, -0.0,
            };
            // Just past the midpoint of two neighbouring floats (both ways), 2^-100 to 2^-60: the half-ulp lives in the
            // second component of an emulated double (>= 2^-124, normal) and decides the rounding.
            var rng = new Random(20261115);
            while (inputs.Count < 2048)
            {
                float a = MathF.ScaleB(1f + rng.Next(1 << 23) / (float)(1 << 23), rng.Next(-100, -60));
                double mid = ((double)a + MathF.BitIncrement(a)) / 2;
                double d = mid + (rng.Next(2) == 0 ? 1 : -1) * mid * Math.Pow(2, -35);
                inputs.Add(rng.Next(2) == 0 ? d : -d);
            }
            // One double ulp either side of a float midpoint, 2^-60 to 2^100: only the last significand bits decide.
            // Ozaki's x + y + z + w tied to even here (one in 7 of the cases near a midpoint); Dekker keeps ~48 bits by
            // design (F64EmulationMode.Dekker), so it runs these only where the doc promises nothing less.
            // 1.2e-16 below the overflow midpoint: the nearest float is float.MaxValue, decided by the last 5 bits
            var fullSignificand = new List<double> { 3.4028235677973362e38, -3.4028235677973362e38 };
            while (fullSignificand.Count < 2048)
            {
                float a = MathF.ScaleB(1f + rng.Next(1 << 23) / (float)(1 << 23), rng.Next(-60, 100));
                double mid = ((double)a + MathF.BitIncrement(a)) / 2;
                double d = rng.Next(2) == 0 ? Math.BitIncrement(mid) : Math.BitDecrement(mid);
                fullSignificand.Add(rng.Next(2) == 0 ? d : -d);
            }
            var failures = new List<string>();
            await ForEachDoubleRepresentation(accelerator, async (label, mode) =>
            {
                bool limited = mode is F64EmulationMode.Dekker or F64EmulationMode.Ozaki;
                double[] input = mode == F64EmulationMode.Dekker ? inputs.ToArray() : [.. inputs, .. fullSignificand];
                using var dSrc = accelerator.Allocate1D(input);
                using var dDst = accelerator.Allocate1D<float>(input.Length);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<float>>(DoubleToSingleKernel);
                kernel(input.Length, dSrc.View, dDst.View);
                var got = await dDst.CopyToHostAsync<float>();
                int bad = 0; string first = "";
                for (int k = 0; k < input.Length; k++)
                {
                    float e = (float)input[k];
                    // f32 components on hardware that flushes subnormals: a subnormal result may come back as zero
                    bool ok = SameFloat(got[k], e) || (limited && float.IsSubnormal(e) && got[k] == 0f);
                    if (!ok && bad++ < 6) first += $" {input[k]:R} -> {got[k]:R} (expected {e:R});";
                }
                if (bad > 0) failures.Add($"{label}: {bad} of {input.Length}:{first}");
            });
            if (failures.Count > 0)
                throw new Exception(string.Join("; ", failures));
        });
    }
}
