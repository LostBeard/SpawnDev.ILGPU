using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // F64EmulationMode.Ieee: emulated double bit-identical to .NET, per operation. Geordi 2026-11-08: SpawnDev.Codecs' FLAC
    // LPC analysis (r -= a * b chains, divisions, compares on doubles in registers) chose different coefficients on WebGPU
    // than on the CPU/OpenCL even in Ozaki mode, which keeps ~96 bits between operations and rounds to binary64 only on a
    // store. The operands cover full 53-bit significands, near-ties of add/mul/fma, exact IEEERemainder ties, subnormals,
    // overflow and the specials; the expected values are .NET's on the host. NaN payloads are not compared (only NaN-ness).
    public abstract partial class BackendTestBase
    {
        const int F64IeeeOuts = 20;

        static void F64IeeeOpsKernel(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> zs, ArrayView<double> o)
        {
            double x = xs[i], y = ys[i], z = zs[i];
            int b = i * F64IeeeOuts;
            o[b + 0] = x + y;
            o[b + 1] = x - y;
            o[b + 2] = x * y;
            o[b + 3] = x / y;
            o[b + 4] = Math.Sqrt(Math.Abs(x));
            o[b + 5] = Math.FusedMultiplyAdd(x, y, z);
            o[b + 6] = x % y;
            o[b + 7] = global::ILGPU.Algorithms.XMath.IEEERemainder(x, y);
            o[b + 8] = (double)(float)x;
            o[b + 9] = Math.Floor(x);
            o[b + 10] = Math.Round(x);
            o[b + 11] = Math.Min(x, y);
            o[b + 12] = Math.Max(x, y);
            o[b + 13] = x < y ? 1.0 : 0.0;
            o[b + 14] = x == y ? 1.0 : 0.0;
            o[b + 15] = x <= y ? 1.0 : 0.0;
            // register-resident chain (every step rounds in .NET): the shape of an LPC / Levinson-Durbin recursion
            double r = x;
            for (int k = 0; k < 8; k++)
            {
                r -= y * z;
                r *= 1.0 - z * z;
            }
            o[b + 16] = r;
            o[b + 17] = (double)(long)x;
            o[b + 18] = Math.Ceiling(x);
            o[b + 19] = Math.Truncate(x);
        }

        // The test runs the ops in GROUPS, one kernel each (the op indices of F64IeeeExpected): D3D's FXC (WebGL on
        // Windows) compiles superlinearly in shader size, and one all-ops kernel of the Ozaki family took 130-190 s
        // (F64EmulationExactness). Each group writes its own buffer at i * groupSize + j (WebGL's positional layout).
        // At most 6 doubles per group: each is two uint transform-feedback varyings on WebGL.
        static readonly int[][] F64IeeeGroups =
        {
            new[] { 0, 1, 2, 8, 9, 10 },
            new[] { 11, 12, 13, 14, 15, 17 },
            new[] { 18, 19 },
            new[] { 3, 4 },
            new[] { 5, 16 },
            new[] { 6, 7 },
        };

        static void F64IeeeG0(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> zs, ArrayView<double> o)
        {
            double x = xs[i], y = ys[i];
            int b = i * 6;
            o[b + 0] = x + y;
            o[b + 1] = x - y;
            o[b + 2] = x * y;
            o[b + 3] = (double)(float)x;
            o[b + 4] = Math.Floor(x);
            o[b + 5] = Math.Round(x);
        }

        static void F64IeeeG0b(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> zs, ArrayView<double> o)
        {
            double x = xs[i], y = ys[i];
            int b = i * 6;
            o[b + 0] = Math.Min(x, y);
            o[b + 1] = Math.Max(x, y);
            o[b + 2] = x < y ? 1.0 : 0.0;
            o[b + 3] = x == y ? 1.0 : 0.0;
            o[b + 4] = x <= y ? 1.0 : 0.0;
            o[b + 5] = (double)(long)x;
        }

        static void F64IeeeG0c(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> zs, ArrayView<double> o)
        {
            double x = xs[i];
            o[i * 2] = Math.Ceiling(x);
            o[i * 2 + 1] = Math.Truncate(x);
        }

        static void F64IeeeG1(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> zs, ArrayView<double> o)
        {
            double x = xs[i], y = ys[i];
            o[i * 2] = x / y;
            o[i * 2 + 1] = Math.Sqrt(Math.Abs(x));
        }

        static void F64IeeeG2(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> zs, ArrayView<double> o)
        {
            double x = xs[i], y = ys[i], z = zs[i];
            o[i * 2] = Math.FusedMultiplyAdd(x, y, z);
            double r = x;
            for (int k = 0; k < 8; k++)
            {
                r -= y * z;
                r *= 1.0 - z * z;
            }
            o[i * 2 + 1] = r;
        }

        static void F64IeeeG3(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<double> zs, ArrayView<double> o)
        {
            double x = xs[i], y = ys[i];
            o[i * 2] = x % y;
            o[i * 2 + 1] = global::ILGPU.Algorithms.XMath.IEEERemainder(x, y);
        }

        /// <summary>(long)x as .NET 9+ defines it: truncate, NaN -> 0, out of range -> long.MinValue / long.MaxValue.</summary>
        static long SaturatingToInt64(double x) =>
            double.IsNaN(x) ? 0L : x >= 9223372036854775808.0 ? long.MaxValue : x <= -9223372036854775808.0 ? long.MinValue : (long)x;

        static double F64IeeeExpected(double x, double y, double z, int op)
        {
            switch (op)
            {
                case 0: return x + y;
                case 1: return x - y;
                case 2: return x * y;
                case 3: return x / y;
                case 4: return Math.Sqrt(Math.Abs(x));
                // The oracle must not depend on the runtime the test runs on: .NET in the browser (Mono WASM) returns +0
                // for fma(4.87e-304, -1.24e-309, 0), where desktop .NET 10 and IEEE 754 give -0 (the exact result is
                // negative), and converts out-of-range doubles to long.MinValue where .NET 9+ saturates. SoftFma's
                // FusedMultiplyAddF64 is bit-identical to desktop .NET (16M cases, CHANGELOG 5.4.18).
                case 5: return SoftFma.FusedMultiplyAddF64(x, y, z);
                case 6: return x % y;
                case 7: return Math.IEEERemainder(x, y);
                case 8: return (double)(float)x;
                case 9: return Math.Floor(x);
                case 10: return Math.Round(x);
                case 11: return Math.Min(x, y);
                case 12: return Math.Max(x, y);
                case 13: return x < y ? 1.0 : 0.0;
                case 14: return x == y ? 1.0 : 0.0;
                case 15: return x <= y ? 1.0 : 0.0;
                case 16:
                    {
                        double r = x;
                        for (int k = 0; k < 8; k++) { r -= y * z; r *= 1.0 - z * z; }
                        return r;
                    }
                case 17: return (double)SaturatingToInt64(x);
                case 18: return Math.Ceiling(x);
                default: return Math.Truncate(x);
            }
        }

        static (double[] xs, double[] ys, double[] zs) F64IeeeOperands(int n)
        {
            ulong s = 0x9E3779B97F4A7C15UL;
            ulong Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
            double Bits(ulong v) => BitConverter.UInt64BitsToDouble(v);
            double Rand(int kind)
            {
                ulong r = Next();
                return kind switch
                {
                    0 => Bits((r & 0x800FFFFFFFFFFFFFUL) | ((ulong)(1023 - 30 + (int)(Next() % 60)) << 52)),  // full significands
                    1 => Bits((r & 0x800FFFFFFFFFFFFFUL) | ((ulong)(1 + (int)(Next() % 100)) << 52)),          // near underflow
                    2 => Bits(r & 0x800FFFFFFFFFFFFFUL),                                                       // subnormal
                    3 => Bits((r & 0x800FFFFFFFFFFFFFUL) | ((ulong)(2046 - (int)(Next() % 100)) << 52)),      // near overflow
                    4 => (double)(long)(r >> 40) * Math.ScaleB(1.0, (int)(Next() % 40) - 20),                   // short significands
                    5 => new[] { 0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, double.NaN, 1.0, double.Epsilon, double.MaxValue }[Next() % 8],
                    _ => Bits(r),                                                                             // any bits
                };
            }
            var xs = new double[n]; var ys = new double[n]; var zs = new double[n];
            for (int i = 0; i < n; i++)
            {
                double x = Rand((int)(Next() % 7)), y = Rand((int)(Next() % 7)), z = Rand((int)(Next() % 7));
                switch (i % 6)
                {
                    case 1: y = BitConverter.UInt64BitsToDouble(BitConverter.DoubleToUInt64Bits(x) + (Next() % 5) - 2); break; // cancellation
                    case 2: x = (double)((Next() & 0x7FFFFFF) | 0x4000001); y = (double)((Next() & 0x7FFFFFF) | 0x4000001); break; // 54-bit products: mul ties
                    case 3: { y = (double)((long)(Next() >> 40) | 1) * Math.ScaleB(1.0, (int)(Next() % 60) - 30); x = ((long)(Next() % 1000) + 0.5) * y; break; } // IEEERemainder ties
                    case 4: z = -(x * y); break;                                                          // FMA keeps the product's error
                    case 5: { double big = Math.ScaleB(1.0, 53); x = big + (double)(Next() % 8) - 4.0; y = 0.5 * (double)(Next() % 7); break; } // add ties
                }
                xs[i] = x; ys[i] = y; zs[i] = z;
            }
            return (xs, ys, zs);
        }

        [TestMethod]
        public async Task F64Ieee_Ops_BitExactWithDotNet() => await RunTest(async accelerator =>
        {
            F64EmulationMode saved;
            if (accelerator is WebGPUAccelerator webgpu) { saved = webgpu.F64Mode; webgpu.F64Mode = F64EmulationMode.Ieee; }
            else if (accelerator is WebGLAccelerator webgl) { saved = webgl.F64Mode; webgl.F64Mode = F64EmulationMode.Ieee; }
            else throw new UnsupportedTestException("F64EmulationMode.Ieee is the emulated-double mode of WebGPU / WebGL.");
            try
            {
                const int n = 8192;
                var (xs, ys, zs) = F64IeeeOperands(n);
                using var xb = accelerator.Allocate1D(xs);
                using var yb = accelerator.Allocate1D(ys);
                using var zb = accelerator.Allocate1D(zs);
                var got = new double[n * F64IeeeOuts];
                var groupKernels = new Action<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>[]
                    { F64IeeeG0, F64IeeeG0b, F64IeeeG0c, F64IeeeG1, F64IeeeG2, F64IeeeG3 };
                for (int g = 0; g < F64IeeeGroups.Length; g++)
                {
                    var ops = F64IeeeGroups[g];
                    using var ob = accelerator.Allocate1D<double>(n * ops.Length);
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var kernel = accelerator.LoadAutoGroupedStreamKernel(groupKernels[g]);
                    kernel(n, xb.View, yb.View, zb.View, ob.View);
                    await accelerator.SynchronizeAsync();
                    Console.WriteLine($"[F64Ieee] {accelerator.AcceleratorType} group {g}: load + first run {sw.Elapsed.TotalMilliseconds:F0} ms");
                    var part = await ob.CopyToHostAsync<double>();
                    for (int i = 0; i < n; i++)
                        for (int j = 0; j < ops.Length; j++)
                            got[i * F64IeeeOuts + ops[j]] = part[i * ops.Length + j];
                }
                string[] names = { "x+y", "x-y", "x*y", "x/y", "Sqrt|x|", "Fma", "x%y", "IEEERemainder", "(double)(float)x", "Floor",
                    "Round", "Min", "Max", "x<y", "x==y", "x<=y", "chain", "(double)(long)x", "Ceiling", "Truncate" };
                var wrong = new int[F64IeeeOuts];
                var first = new string?[F64IeeeOuts];
                for (int i = 0; i < n; i++)
                    for (int op = 0; op < F64IeeeOuts; op++)
                    {
                        double want = F64IeeeExpected(xs[i], ys[i], zs[i], op), g = got[i * F64IeeeOuts + op];
                        bool same = double.IsNaN(want) ? double.IsNaN(g) : BitConverter.DoubleToInt64Bits(want) == BitConverter.DoubleToInt64Bits(g);
                        if (!same && wrong[op]++ == 0)
                            first[op] = $"{names[op]}(x={xs[i]:R}, y={ys[i]:R}, z={zs[i]:R}) = {g:R}, .NET {want:R}";
                    }
                var report = new System.Text.StringBuilder();
                for (int op = 0; op < F64IeeeOuts; op++)
                    if (wrong[op] > 0) report.AppendLine($"{names[op]}: {wrong[op]}/{n} wrong, first: {first[op]}");
                if (report.Length > 0) throw new Exception("Ieee f64 differs from .NET:\n" + report);
            }
            finally
            {
                if (accelerator is WebGPUAccelerator w) w.F64Mode = saved;
                else if (accelerator is WebGLAccelerator g) g.F64Mode = saved;
            }
        });

        static void F64OzakiCompareKernel(Index1D i, ArrayView<double> xs, ArrayView<double> ys, ArrayView<int> o)
        {
            double x = xs[i], y = ys[i];
            int b = i * 6;
            o[b + 0] = x < y ? 1 : 0;
            o[b + 1] = x <= y ? 1 : 0;
            o[b + 2] = x > y ? 1 : 0;
            o[b + 3] = x >= y ? 1 : 0;
            o[b + 4] = x == y ? 1 : 0;
            o[b + 5] = x != y ? 1 : 0;
        }

        // Ozaki keeps a double in three f32 components (24 + 24 + 5 significand bits); its comparisons looked at the first
        // two only, so doubles differing in their lowest bits compared equal. Pairs of full 53-bit doubles one to a few
        // units in the last place apart, plus equal pairs, +-0 and the specials, against .NET.
        [TestMethod]
        public async Task F64Ozaki_Compare_FullSignificand() => await RunTest(async accelerator =>
        {
            F64EmulationMode saved;
            if (accelerator is WebGPUAccelerator webgpu) { saved = webgpu.F64Mode; webgpu.F64Mode = F64EmulationMode.Ozaki; }
            else if (accelerator is WebGLAccelerator webgl) { saved = webgl.F64Mode; webgl.F64Mode = F64EmulationMode.Ozaki; }
            else throw new UnsupportedTestException("F64EmulationMode.Ozaki is an emulated-double mode of WebGPU / WebGL.");
            try
            {
                const int n = 4096;
                var xs = new double[n]; var ys = new double[n];
                ulong s = 0x2545F4914F6CDD1DUL;
                ulong Next() { s ^= s << 13; s ^= s >> 7; s ^= s << 17; return s; }
                var specials = new[] { 0.0, -0.0, double.PositiveInfinity, double.NegativeInfinity, 1.0, -1.0 };
                for (int i = 0; i < n; i++)
                {
                    // full significand, exponent within the f32 range (Ozaki splits into f32 components)
                    ulong bits = (Next() & 0x800FFFFFFFFFFFFFUL) | ((ulong)(1023 - 60 + (int)(Next() % 120)) << 52);
                    double x = BitConverter.UInt64BitsToDouble(bits);
                    double y = (i % 4) switch
                    {
                        0 => BitConverter.UInt64BitsToDouble(bits + 1 + Next() % 7),
                        1 => BitConverter.UInt64BitsToDouble(bits - 1 - Next() % 7),
                        2 => x,
                        _ => specials[Next() % (ulong)specials.Length],
                    };
                    if (i % 4 == 3) x = specials[Next() % (ulong)specials.Length];
                    xs[i] = x; ys[i] = y;
                }
                using var xb = accelerator.Allocate1D(xs);
                using var yb = accelerator.Allocate1D(ys);
                using var ob = accelerator.Allocate1D<int>(n * 6);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<int>>(F64OzakiCompareKernel);
                kernel(n, xb.View, yb.View, ob.View);
                await accelerator.SynchronizeAsync();
                var got = await ob.CopyToHostAsync<int>();
                string[] names = { "<", "<=", ">", ">=", "==", "!=" };
                int wrong = 0; string? first = null;
                for (int i = 0; i < n; i++)
                {
                    double x = xs[i], y = ys[i];
                    bool[] want = { x < y, x <= y, x > y, x >= y, x == y, x != y };
                    for (int c = 0; c < 6; c++)
                        if ((got[i * 6 + c] != 0) != want[c] && wrong++ == 0)
                            first = $"{x:R} {names[c]} {y:R}: got {got[i * 6 + c] != 0}, .NET {want[c]}";
                }
                if (wrong > 0) throw new Exception($"Ozaki compares: {wrong} of {n * 6} wrong, first: {first}");
            }
            finally
            {
                if (accelerator is WebGPUAccelerator w) w.F64Mode = saved;
                else if (accelerator is WebGLAccelerator g) g.F64Mode = saved;
            }
        });

        /// <summary>The Ieee ops kernel, for offline WGSL / GLSL validation (DemoConsole ieee-wgsl).</summary>
        public static Delegate F64IeeeOpsKernelDelegate =>
            (Action<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>)F64IeeeOpsKernel;
    }
}
