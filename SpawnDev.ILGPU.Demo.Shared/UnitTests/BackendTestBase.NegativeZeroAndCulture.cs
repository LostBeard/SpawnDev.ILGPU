using System;
using System.Globalization;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    public abstract partial class BackendTestBase
    {
        // A -0f constant came out as +0 on WebGPU and WebGL (Tuvok 2026-11-09: SpawnDev.ILGPU.ML's HalfToFloat(0x8000),
        // `return sign == 1 ? -0f : 0f;`). The generators printed it as "-0.0", the NEGATION of 0.0, which the shader
        // compiler folds to +0. The sign of zero is observable (1 / -0 = -inf, copysign, a stored bit pattern).
        static void NegativeZeroConstantKernel(Index1D i, ArrayView<int> flag, ArrayView<float> f, ArrayView<double> d)
        {
            f[i] = flag[i] == 1 ? -0f : 0f;
            d[i] = flag[i] == 1 ? -0.0 : 0.0;
        }

        [TestMethod]
        public async Task NegativeZeroConstant_KeepsSignBit() => await RunTest(async accelerator =>
        {
            var flags = new[] { 1, 0, 1, 1, 0, 0, 1, 0 };
            int n = flags.Length;
            using var dFlag = accelerator.Allocate1D(flags);
            using var dF = accelerator.Allocate1D<float>(n);
            using var dD = accelerator.Allocate1D<double>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>, ArrayView<double>>(NegativeZeroConstantKernel);
            kernel((Index1D)n, dFlag.View, dF.View, dD.View);
            var f = await dF.CopyToHostAsync<float>();
            var d = await dD.CopyToHostAsync<double>();
            for (int i = 0; i < n; i++)
            {
                uint wantF = flags[i] == 1 ? 0x80000000u : 0u;
                ulong wantD = flags[i] == 1 ? 0x8000000000000000ul : 0ul;
                if (BitConverter.SingleToUInt32Bits(f[i]) != wantF)
                    throw new Exception($"float [{i}] flag {flags[i]}: bits 0x{BitConverter.SingleToUInt32Bits(f[i]):X8}, want 0x{wantF:X8}");
                if (BitConverter.DoubleToUInt64Bits(d[i]) != wantD)
                    throw new Exception($"double [{i}] flag {flags[i]}: bits 0x{BitConverter.DoubleToUInt64Bits(d[i]):X16}, want 0x{wantD:X16}");
            }
        });

        // Generators print numbers into source text (WGSL, GLSL, PTX, OpenCL C). Under the caller's culture, de-DE
        // printed 0.5f as "0,5" and sv-SE (ICU) printed -5 with U+2212 MINUS SIGN, so the source did not compile.
        // Backend.Compile generates under the invariant culture (5.4.21). The kernel is compiled for the first time
        // inside the culture scope (its own method), with fractional, negative and exponent constants.
        static void CultureConstantsKernel(Index1D i, ArrayView<float> x, ArrayView<float> f, ArrayView<int> k, ArrayView<double> d)
        {
            f[i] = x[i] * 0.5f - 2.25f + 1.5e-3f * i;
            k[i] = i * -7 - 5;
            d[i] = x[i] * -0.125 + 3.75;
        }

        [TestMethod]
        public async Task CodegenUnderCommaDecimalCulture_MatchesHost() => await RunTest(async accelerator =>
        {
            CultureInfo? culture = null;
            foreach (var name in new[] { "sv-SE", "de-DE", "fr-FR" })
            {
                try
                {
                    var c = new CultureInfo(name);
                    if (c.NumberFormat.NumberDecimalSeparator == ",") { culture = c; break; }
                }
                catch (CultureNotFoundException) { }
            }
            if (culture == null)
                throw new UnsupportedTestException("No culture with a comma decimal separator on this runtime (invariant globalization)");

            const int n = 64;
            var x = new float[n];
            for (int i = 0; i < n; i++) x[i] = (i - 20) * 0.75f;
            var callerCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = culture;
            try
            {
                using var dX = accelerator.Allocate1D(x);
                using var dF = accelerator.Allocate1D<float>(n);
                using var dK = accelerator.Allocate1D<int>(n);
                using var dD = accelerator.Allocate1D<double>(n);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<int>, ArrayView<double>>(CultureConstantsKernel);
                if (CultureInfo.CurrentCulture.Name != culture.Name)
                    throw new Exception($"Kernel load changed the caller's culture to '{CultureInfo.CurrentCulture.Name}'");
                kernel((Index1D)n, dX.View, dF.View, dK.View, dD.View);
                var f = await dF.CopyToHostAsync<float>();
                var k = await dK.CopyToHostAsync<int>();
                var d = await dD.CopyToHostAsync<double>();
                for (int i = 0; i < n; i++)
                {
                    float wantF = x[i] * 0.5f - 2.25f + 1.5e-3f * i;
                    int wantK = i * -7 - 5;
                    double wantD = x[i] * -0.125 + 3.75;
                    if (Math.Abs(f[i] - wantF) > 1e-5f * Math.Max(1f, Math.Abs(wantF)) || k[i] != wantK || Math.Abs(d[i] - wantD) > 1e-6)
                        throw new Exception($"[{i}] under {culture.Name}: f {f[i]} (want {wantF}), k {k[i]} (want {wantK}), d {d[i]} (want {wantD})");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = callerCulture;
            }
        });
    }
}
