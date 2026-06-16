using System;
using System.Numerics;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;

/// <summary>
/// Desktop verification for Tuvok's ask: a transpilable GENERIC float&lt;-&gt;T conversion inside a
/// `where T : INumber&lt;T&gt;` kernel. Mirrors his MixedMeanGeneric (read low-p, accumulate in float,
/// write low-p) but using PrecisionConvert.ConvertToSingle / ConvertFromSingle instead of
/// float.CreateChecked / T.CreateChecked (which throw "System.Type is not supported" on every GPU
/// backend). Runs on CPU/CUDA/OpenCL and checks the per-row mean against a managed reference.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- precision-convert
/// </summary>
internal static class GenericConvertRepro
{
    // Per-row mean: read T, accumulate in float, write T - the precision-aware op shape.
    private static void MixedMeanGeneric<T>(Index1D row,
        ArrayView1D<T, Stride1D.Dense> input, ArrayView1D<T, Stride1D.Dense> output, int C)
        where T : unmanaged, INumber<T>
    {
        int b = row * C;
        float acc = 0f;
        for (int c = 0; c < C; c++)
            acc += PrecisionConvert.ConvertToSingle(input[b + c]); // T -> float
        output[row] = PrecisionConvert.ConvertFromSingle<T>(acc / C); // float -> T
    }

    // Pure float<->T round-trip with no accumulation - isolates conversion correctness.
    private static void RoundTripGeneric<T>(Index1D i,
        ArrayView1D<T, Stride1D.Dense> input, ArrayView1D<T, Stride1D.Dense> output)
        where T : unmanaged, INumber<T> =>
        output[i] = PrecisionConvert.ConvertFromSingle<T>(PrecisionConvert.ConvertToSingle(input[i]));

    public static async Task<int> Run()
    {
        Console.WriteLine("=== Generic in-kernel float<->T conversion (PrecisionConvert) ===");
        int fails = 0;
        using var context = Context.Create(b => b.Default().EnableAlgorithms());
        foreach (var dev in context)
        {
            if (dev.AcceleratorType != AcceleratorType.CPU &&
                dev.AcceleratorType != AcceleratorType.Cuda &&
                dev.AcceleratorType != AcceleratorType.OpenCL)
                continue;
            using var acc = dev.CreateAccelerator(context);
            Console.WriteLine($"  [{acc.AcceleratorType} {acc.Name}]");
            fails += RunOne<float>(acc, "float", v => v, v => v);
            fails += RunOne<global::ILGPU.Half>(acc, "Half ", v => (global::ILGPU.Half)v, v => (float)v);
            fails += RunOne<global::ILGPU.BFloat16>(acc, "bf16 ", v => (global::ILGPU.BFloat16)v, v => (float)v);
            fails += RunOne<global::ILGPU.Float8E4M3>(acc, "E4M3 ", v => (global::ILGPU.Float8E4M3)v, v => (float)v);
            fails += RunOne<global::ILGPU.Float8E5M2>(acc, "E5M2 ", v => (global::ILGPU.Float8E5M2)v, v => (float)v);
        }
        Console.WriteLine(fails == 0
            ? "=== PrecisionConvert PASS ==="
            : $"=== PrecisionConvert FAIL: {fails} problems ===");
        await Task.CompletedTask;
        return fails == 0 ? 0 : 1;
    }

    private static int RunOne<T>(Accelerator acc, string label, Func<float, T> toT, Func<T, float> toF)
        where T : unmanaged, INumber<T>
    {
        const int rows = 16, C = 8;
        var input = new T[rows * C];
        var rng = new Random(11);
        for (int i = 0; i < input.Length; i++)
            input[i] = toT((float)(rng.NextDouble() * 2 - 1));

        // Managed reference: same read-as-float / mean / write-as-T path.
        var expected = new T[rows];
        for (int r = 0; r < rows; r++)
        {
            float sum = 0f;
            for (int c = 0; c < C; c++) sum += toF(input[r * C + c]);
            expected[r] = toT(sum / C);
        }

        try
        {
            using var inBuf = acc.Allocate1D(input);
            using var outBuf = acc.Allocate1D<T>(rows);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, Stride1D.Dense>, int>(MixedMeanGeneric<T>);
            k(rows, inBuf.View, outBuf.View, C);
            acc.Synchronize();
            var got = outBuf.GetAsArray1D();

            // Tolerance, not bit-exact: the f32 ACCUMULATION inside the loop is reassociated/FMA-fused
            // differently on the GPU vs the managed ref, so the pre-narrowing sum can differ by ULPs
            // (visible for finer Half, rounded away for coarser bf16/fp8). That is float-accumulation
            // behavior, NOT a conversion error - the conversion correctness is the round-trip check below.
            int bad = 0, firstBad = -1;
            for (int r = 0; r < rows; r++)
            {
                float g = toF(got[r]), e = toF(expected[r]);
                bool bothNaN = float.IsNaN(g) && float.IsNaN(e);
                float tol = MathF.Max(2e-3f, MathF.Abs(e) * 2e-2f);
                if (!bothNaN && MathF.Abs(g - e) > tol) { if (bad == 0) firstBad = r; bad++; }
            }
            if (bad != 0)
            {
                Console.WriteLine($"    {label}: WRONG {bad}/{rows} first@{firstBad} got={toF(got[firstBad])} want={toF(expected[firstBad])}");
                return 1;
            }

            // Conversion correctness: a PURE round-trip ConvertFromSingle(ConvertToSingle(x)) with NO
            // accumulation must be BIT-EXACT vs the concrete (T)(float)x cast on every backend.
            using var rtIn = acc.Allocate1D(input);
            using var rtOut = acc.Allocate1D<T>(input.Length);
            var rtK = acc.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<T, Stride1D.Dense>, ArrayView1D<T, Stride1D.Dense>>(RoundTripGeneric<T>);
            rtK(input.Length, rtIn.View, rtOut.View);
            acc.Synchronize();
            var rt = rtOut.GetAsArray1D();
            int rtBad = 0; int rtFirst = -1;
            for (int i = 0; i < input.Length; i++)
            {
                // Reference = concrete cast round-trip (T)(float)x, exactly what the intrinsic lowers to.
                T refV = toT(toF(input[i]));
                float g = toF(rt[i]), e = toF(refV);
                bool bothNaN = float.IsNaN(g) && float.IsNaN(e);
                if (!bothNaN && g != e) { if (rtBad == 0) rtFirst = i; rtBad++; }
            }
            if (rtBad != 0)
            {
                Console.WriteLine($"    {label}: ROUND-TRIP WRONG {rtBad}/{input.Length} first@{rtFirst} got={toF(rt[rtFirst])} want={toF(toT(toF(input[rtFirst])))}");
                return 1;
            }
            Console.WriteLine($"    {label}: OK (mean within tol + round-trip {input.Length}/{input.Length} bit-exact)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"    {label}: {ex.GetType().Name}: {ex.Message}");
            var inner = ex.InnerException; int d = 0;
            while (inner != null && d < 3) { Console.WriteLine($"       INNER[{d}] {inner.GetType().Name}: {inner.Message}"); inner = inner.InnerException; d++; }
            return 1;
        }
    }
}
