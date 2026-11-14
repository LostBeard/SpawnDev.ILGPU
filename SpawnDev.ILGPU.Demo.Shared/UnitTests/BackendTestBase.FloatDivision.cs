using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // fp32 division and square root must be what .NET computes: correctly rounded (IEEE 754). OpenCL C allows 2.5 ulp
    // for `/` and 3 ulp for sqrt unless a program is built with -cl-fp32-correctly-rounded-divide-sqrt, and AMD uses
    // the slack: RX 590, 24f / 48 = 0.49999997 (0x3EFFFFFF), so floor(y * 24f / 48) chose the wrong source row in
    // SpawnDev.ILGPU.ML's nearest upsample (FLUX.2 VAE decode wrong by 0.27 relRMS; Lore + Tuvok 2026-11-14). WGSL and
    // GLSL ES allow the same 2.5 ulp. Bit-exact against the host: quotients that are exactly representable (the
    // integer-ratio case of the ML bug), the floor of y * a / b, and random operands over the normal range.
    public abstract partial class BackendTestBase
    {
        public static void FloatDivisionKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> quotient, ArrayView<float> root, ArrayView<int> floorRow)
        {
            quotient[i] = a[i] / b[i];
            root[i] = MathF.Sqrt(MathF.Abs(a[i]));
            floorRow[i] = (int)MathF.Floor(i * a[i] / b[i]);
        }

        [TestMethod]
        public async Task FloatDivision_AndSqrt_CorrectlyRounded_MatchHost() => await RunTest(async accelerator =>
        {
            var (bad, detail) = await FloatDivisionCheckAsync(accelerator);
            if (bad > 0)
                throw new Exception(detail);
        });

        /// <summary>Runs <see cref="FloatDivisionKernel"/> and counts the results that differ from .NET bit for bit.</summary>
        public static async Task<(int Bad, string Detail)> FloatDivisionCheckAsync(Accelerator accelerator)
        {
            var rng = new Random(20261114);
            var a = new System.Collections.Generic.List<float>();
            var b = new System.Collections.Generic.List<float>();
            // integer ratios whose quotient is exact (24 / 48 = 0.5 was 0.49999997 on AMD OpenCL)
            foreach (int den in new[] { 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 3 * 128, 640, 1000 })
                for (int num = 1; num <= den * 2; num++)
                {
                    a.Add(num * 3);
                    b.Add(den * 3);
                }
            // random normal operands, both signs
            while (a.Count < 16384)
            {
                a.Add((float)((rng.NextDouble() * 2 - 1) * Math.Pow(2, rng.Next(-60, 60))));
                b.Add((float)((rng.NextDouble() * 2 - 1) * Math.Pow(2, rng.Next(-60, 60))) + 1e-30f);
            }
            int n = a.Count;
            using var dA = accelerator.Allocate1D(a.ToArray());
            using var dB = accelerator.Allocate1D(b.ToArray());
            using var dQ = accelerator.Allocate1D<float>(n);
            using var dR = accelerator.Allocate1D<float>(n);
            using var dF = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<int>>(FloatDivisionKernel);
            kernel(n, dA.View, dB.View, dQ.View, dR.View, dF.View);
            var q = await dQ.CopyToHostAsync<float>();
            var r = await dR.CopyToHostAsync<float>();
            var f = await dF.CopyToHostAsync<int>();
            int badQ = 0, badR = 0, badF = 0;
            string firstQ = "", firstR = "", firstF = "";
            for (int i = 0; i < n; i++)
            {
                float eq = a[i] / b[i];
                float er = MathF.Sqrt(MathF.Abs(a[i]));
                float prod = i * a[i];
                int ef = (int)MathF.Floor(prod / b[i]);
                if (BitConverter.SingleToInt32Bits(q[i]) != BitConverter.SingleToInt32Bits(eq))
                { if (badQ++ == 0) firstQ = $"{a[i]:R} / {b[i]:R} = {q[i]:R} (0x{BitConverter.SingleToInt32Bits(q[i]):X8}), expected {eq:R} (0x{BitConverter.SingleToInt32Bits(eq):X8})"; }
                if (BitConverter.SingleToInt32Bits(r[i]) != BitConverter.SingleToInt32Bits(er))
                { if (badR++ == 0) firstR = $"sqrt({MathF.Abs(a[i]):R}) = {r[i]:R}, expected {er:R}"; }
                if (f[i] != ef && !float.IsInfinity(prod / b[i]) && Math.Abs(prod / b[i]) < 1e9f)
                { if (badF++ == 0) firstF = $"floor({i} * {a[i]:R} / {b[i]:R}) = {f[i]}, expected {ef}"; }
            }
            return (badQ + badR + badF,
                $"{accelerator.AcceleratorType} ({accelerator.Name}): of {n}: {badQ} quotients differ from .NET (first: {firstQ}); {badR} square roots (first: {firstR}); {badF} floor rows (first: {firstF})");
        }
    }
}
