using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // System.Math / MathF.Log2, Cbrt, Asinh, Acosh and Atanh have no IL body; a kernel calling one failed to compile
    // (CUDA: ptxas "Unresolved extern function 'Log2'", Tuvok 2026-11-18). The frontend now remaps them: Log2 to the
    // Log2F IR op, the others to ports of musl's libm (ILGPU/IntrinsicMath.Libm.cs, which also adds IntrinsicMath.Log1P).
    // Checked on every input against the host: NaN / Inf exactly, finite results within LibmMaxUlp* of the reference.
    // Float references are the double functions rounded to float (correctly rounded in all but rare ties); double
    // references are the host's Math functions, which are not exact themselves (measured up to 2 ulp from the
    // correctly rounded result for cbrt on Windows).
    public abstract partial class BackendTestBase
    {
        /// <summary>Largest ulp error allowed for the float functions (measured per lane; see the [report] line).</summary>
        protected virtual int LibmMaxUlpFloat => 3; // worst measured: 3, WebGL Asinh (2026-11-18)

        /// <summary>Largest ulp error allowed for the double functions (measured per lane; see the [report] line).</summary>
        protected virtual int LibmMaxUlpDouble => 3; // worst measured: 3, Log1P where the host reference itself is 3 ulp off

        public static void LibmFloatKernel(Index1D i, ArrayView<float> x, ArrayView<float> cbrt, ArrayView<float> asinh,
            ArrayView<float> acosh, ArrayView<float> atanh, ArrayView<float> log2, ArrayView<float> log1p)
        {
            float v = x[i];
            cbrt[i] = MathF.Cbrt(v);
            asinh[i] = MathF.Asinh(v);
            acosh[i] = MathF.Acosh(v);
            atanh[i] = MathF.Atanh(v);
            log2[i] = MathF.Log2(v);
            log1p[i] = IntrinsicMath.Log1P(v);
        }

        public static void LibmDoubleKernel(Index1D i, ArrayView<double> x, ArrayView<double> cbrt, ArrayView<double> asinh,
            ArrayView<double> acosh, ArrayView<double> atanh, ArrayView<double> log2, ArrayView<double> log1p)
        {
            double v = x[i];
            cbrt[i] = Math.Cbrt(v);
            asinh[i] = Math.Asinh(v);
            acosh[i] = Math.Acosh(v);
            atanh[i] = Math.Atanh(v);
            log2[i] = Math.Log2(v);
            log1p[i] = IntrinsicMath.Log1P(v);
        }

        static readonly string[] LibmNames = { "Cbrt", "Asinh", "Acosh", "Atanh", "Log2", "Log1P" };

        /// <summary>log(1 + x) on the host: Math.Log(1 + x) is accurate for |x| &gt;= 0.5, 2 atanh(x / (2 + x)) below.</summary>
        static double LibmLog1PReference(double x) =>
            x == -1.0 ? double.NegativeInfinity : x < -1.0 ? double.NaN : double.IsPositiveInfinity(x) ? x :
            Math.Abs(x) >= 0.5 ? Math.Log(1.0 + x) : 2.0 * Math.Atanh(x / (2.0 + x));

        static double LibmReference(int f, double v) => f switch
        {
            0 => Math.Cbrt(v),
            1 => Math.Asinh(v),
            2 => v < 1.0 ? double.NaN : Math.Acosh(v), // by definition (the first browser run's reference gave -12.696 for -6603.9; see the [report] line)
            3 => Math.Atanh(v),
            4 => Math.Log2(v),
            _ => LibmLog1PReference(v),
        };

        static long LibmOrdered(float f) { int b = BitConverter.SingleToInt32Bits(f); return b < 0 ? int.MinValue - (long)b : b; }
        static long LibmOrdered(double d) { long b = BitConverter.DoubleToInt64Bits(d); return b < 0 ? long.MinValue - b : b; }

        /// <summary>WebGPU and WebGL may flush f32 subnormals to zero (WGSL and GLSL ES allow it; D3D does it).</summary>
        static bool LibmMayFlushSubnormals(Accelerator a) => a.AcceleratorType is AcceleratorType.WebGPU or AcceleratorType.WebGL;

        /// <summary>
        /// WebGPU and WebGL emulate double. F64EmulationMode documents the contract: every mode evaluates transcendental
        /// functions in f32, and the default modes have f32's exponent range. The double lane is held to that there:
        /// inputs inside f32's normal range, results compared at float precision.
        /// </summary>
        static bool LibmEmulatedDouble(Accelerator a) => a.AcceleratorType is AcceleratorType.WebGPU or AcceleratorType.WebGL;

        static bool LibmSubnormal(float f) => f != 0 && float.IsSubnormal(f);

        /// <summary>A float result against its reference: NaN with NaN, infinities and zeros by bits, finite values by ulp.</summary>
        static (bool Fail, long Ulp) LibmCompare(float g, float e, int maxUlp)
        {
            if (float.IsNaN(e) || float.IsInfinity(e) || float.IsNaN(g) || float.IsInfinity(g))
                return (BitConverter.SingleToInt32Bits(g) != BitConverter.SingleToInt32Bits(e) && !(float.IsNaN(g) && float.IsNaN(e)), 0);
            long ulp = Math.Abs(LibmOrdered(g) - LibmOrdered(e));
            return (ulp > maxUlp, ulp);
        }

        [TestMethod]
        public async Task MathFunctions_WithoutILBody_Float_MatchHost() => await RunTest(async accelerator =>
        {
            var inputs = new List<float>
            {
                0f, -0f, 1f, -1f, 2f, -2f, 0.5f, -0.5f, 1e-30f, -1e-30f, 1e30f, -1e30f, float.MaxValue, float.MinValue,
                float.PositiveInfinity, float.NegativeInfinity, float.NaN, 0.99999994f, -0.99999994f, 1.0000001f,
                4096f, 1f / 4096f, 2.4414062e-4f, 1.125f, 0.125f, 0.70710677f, 1.4142135f, 1e-7f, -1e-7f,
            };
            // every 4096th bit pattern: both signs, every exponent, subnormals and NaN payloads included
            for (ulong u = 0; u < 0x1_0000_0000UL; u += 4096)
                inputs.Add(BitConverter.Int32BitsToSingle((int)(uint)(u + (u >> 12) % 4093)));
            int n = inputs.Count;
            using var dX = accelerator.Allocate1D(inputs.ToArray());
            var outs = new MemoryBuffer1D<float, Stride1D.Dense>[6];
            for (int f = 0; f < 6; f++) outs[f] = accelerator.Allocate1D<float>(n);
            try
            {
                bool flush = LibmMayFlushSubnormals(accelerator);
                int flushed = 0;
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>,
                    ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(LibmFloatKernel);
                kernel(n, dX.View, outs[0].View, outs[1].View, outs[2].View, outs[3].View, outs[4].View, outs[5].View);
                var report = new List<string>();
                var failures = new List<string>();
                for (int f = 0; f < 6; f++)
                {
                    var got = await outs[f].CopyToHostAsync<float>();
                    long maxUlp = 0; float worstX = 0; int bad = 0; string first = "";
                    for (int i = 0; i < n; i++)
                    {
                        float x = inputs[i], g = got[i], e = (float)LibmReference(f, x);
                        var (fail, ulp) = LibmCompare(g, e, LibmMaxUlpFloat);
                        if (fail && flush)
                        {
                            // a flushed subnormal input behaves as a signed zero; a subnormal result may come back as zero
                            if (LibmSubnormal(x) && !LibmCompare(g, (float)LibmReference(f, x < 0 ? -0.0 : 0.0), LibmMaxUlpFloat).Fail)
                            { fail = false; ulp = 0; flushed++; }
                            else if (LibmSubnormal(e) && g == 0 && float.IsNegative(g) == float.IsNegative(e))
                            { fail = false; ulp = 0; flushed++; }
                        }
                        if (!fail && ulp > maxUlp) { maxUlp = ulp; worstX = x; }
                        if (fail && bad++ == 0)
                            first = $"{LibmNames[f]}({x:R}) = {g:R}, expected {e:R} ({ulp} ulp)";
                    }
                    report.Add($"{LibmNames[f]} {maxUlp} ulp (at {worstX:R})");
                    if (bad > 0) failures.Add($"{LibmNames[f]}: {bad} of {n} wrong, first {first}");
                }
                Console.WriteLine($"[report] {accelerator.AcceleratorType} float, max error of {n} inputs: {string.Join(", ", report)}" +
                    $"{(flush ? $"; {flushed} subnormal flushes accepted" : "")}; host Math.Acosh(-2, -6603.9004f, -2343069.942532864) = {Math.Acosh(-2.0):R}, {Math.Acosh((double)-6603.9004f):R}, {(float)Math.Acosh((double)-6603.9004f):R}, {Math.Acosh(-2343069.942532864):R}");
                if (failures.Count > 0)
                    throw new Exception($"{accelerator.AcceleratorType} ({accelerator.Name}), limit {LibmMaxUlpFloat} ulp: {string.Join("; ", failures)}");
            }
            finally
            {
                foreach (var o in outs) o.Dispose();
            }
        });

        // 262,144 inputs through WebGPU's Ozaki-emulated double took 32 s (the default limit is 30)
        [TestMethod(Timeout = 120000)]
        public async Task MathFunctions_WithoutILBody_Double_MatchHost() => await RunTest(async accelerator =>
        {
            var inputs = new List<double>
            {
                0.0, -0.0, 1.0, -1.0, 2.0, -2.0, 0.5, -0.5, 1e-300, -1e-300, 1e300, -1e300, double.MaxValue, double.MinValue,
                double.PositiveInfinity, double.NegativeInfinity, double.NaN, double.Epsilon, -double.Epsilon,
                0.9999999999999999, -0.9999999999999999, 1.0000000000000002, 67108864.0, 1.0 / 67108864.0, 1.125, 0.125,
                2.8062889183005044E-08, 1e-17, -1e-17,
            };
            var rng = new Random(20261118);
            bool emulated = LibmEmulatedDouble(accelerator);
            int f32Input = 0;
            if (emulated)
            {
                // f32's normal range only (F64EmulationMode: the default modes have f32's exponent range)
                inputs.RemoveAll(v => !double.IsNaN(v) && !double.IsInfinity(v) && v != 0 && (Math.Abs(v) < Math.ScaleB(1.0, -100) || Math.Abs(v) > Math.ScaleB(1.0, 100)));
                while (inputs.Count < 262144)
                    inputs.Add((rng.Next(2) == 0 ? 1 : -1) * Math.Pow(2, rng.NextDouble() * 200 - 100));
            }
            while (inputs.Count < 262144)
                inputs.Add(BitConverter.Int64BitsToDouble(rng.NextInt64() ^ ((long)rng.Next(2) << 63)));
            if (emulated)
            {
                // ...and 48 significant bits: Dekker holds about 48 (F64EmulationMode), so 1 - 2^-53 loads as 1
                for (int j = 0; j < inputs.Count; j++)
                    if (double.IsFinite(inputs[j]))
                        inputs[j] = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(inputs[j]) & ~0x1FL);
            }
            int n = inputs.Count;
            using var dX = accelerator.Allocate1D(inputs.ToArray());
            var outs = new MemoryBuffer1D<double, Stride1D.Dense>[6];
            for (int f = 0; f < 6; f++) outs[f] = accelerator.Allocate1D<double>(n);
            try
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>,
                    ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(LibmDoubleKernel);
                kernel(n, dX.View, outs[0].View, outs[1].View, outs[2].View, outs[3].View, outs[4].View, outs[5].View);
                var report = new List<string>();
                var failures = new List<string>();
                for (int f = 0; f < 6; f++)
                {
                    var got = await outs[f].CopyToHostAsync<double>();
                    long maxUlp = 0; double worstX = 0; int bad = 0; string first = "";
                    for (int i = 0; i < n; i++)
                    {
                        double x = inputs[i], g = got[i], e = LibmReference(f, x);
                        bool fail;
                        long ulp = 0;
                        if (emulated)
                        {
                            // documented contract: transcendental functions are evaluated in f32 (input rounded to f32),
                            // so compare at float precision with f(x), or with f of the f32-rounded input
                            (fail, ulp) = LibmCompare((float)g, (float)e, LibmMaxUlpFloat);
                            if (fail)
                            {
                                var (failF32, ulpF32) = LibmCompare((float)g, (float)LibmReference(f, (float)x), LibmMaxUlpFloat);
                                if (!failF32) { fail = false; ulp = ulpF32; f32Input++; }
                            }
                            if (!fail && ulp > maxUlp) { maxUlp = ulp; worstX = x; }
                        }
                        else if (double.IsNaN(e) || double.IsInfinity(e) || double.IsNaN(g) || double.IsInfinity(g))
                            fail = BitConverter.DoubleToInt64Bits(g) != BitConverter.DoubleToInt64Bits(e) && !(double.IsNaN(g) && double.IsNaN(e));
                        else
                        {
                            long d = LibmOrdered(g) - LibmOrdered(e);
                            ulp = d < 0 ? -d : d;
                            if (ulp > maxUlp) { maxUlp = ulp; worstX = x; }
                            fail = ulp > LibmMaxUlpDouble;
                        }
                        if (fail && bad++ == 0)
                            first = $"{LibmNames[f]}({x:R}) = {g:R}, expected {e:R} ({ulp} ulp)";
                    }
                    report.Add($"{LibmNames[f]} {maxUlp} ulp (at {worstX:R})");
                    if (bad > 0) failures.Add($"{LibmNames[f]}: {bad} of {n} wrong, first {first}");
                }
                Console.WriteLine($"[report] {accelerator.AcceleratorType} double{(emulated ? $" (emulated: f32 range, 48 bits, float-precision compare; {f32Input} matched f of the f32-rounded input)" : "")}, max error of {n} inputs: {string.Join(", ", report)}");
                if (failures.Count > 0)
                    throw new Exception($"{accelerator.AcceleratorType} ({accelerator.Name}), limit {(emulated ? LibmMaxUlpFloat + " float" : LibmMaxUlpDouble.ToString())} ulp: {string.Join("; ", failures)}");
            }
            finally
            {
                foreach (var o in outs) o.Dispose();
            }
        });
    }
}
