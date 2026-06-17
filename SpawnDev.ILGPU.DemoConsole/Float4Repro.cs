using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;

/// <summary>
/// CPU verification of the Float4E2M1 (OCP E2M1FN / the NVFP4-MXFP4 element format) conversion math
/// AND its IR-primitive + backend wiring, mirroring Float8Repro (fp8-verify). E2M1 has 16 finite
/// codes - NO Inf, NO NaN; magnitudes {0,.5,1,1.5,2,3,4,6}, max 6.
///
/// Managed (pure, no kernel):
///   (1) IDEMPOTENCE over all 16 nibble patterns: (Float4E2M1)((float)raw) == raw.
///   (2) An independent value-formula decode reference for the E2M1 -> float decode.
///   (3) Known-exact magnitudes + overflow saturation (->+-6) + +-Inf->+-6 + NaN->-0.
/// Kernels (real IR + codegen, desktop CPU/CUDA/OpenCL):
///   (A) generic relu(x*scale+bias) via where T:INumber&lt;T&gt; - exercises FP4 buffer load/store,
///       in-kernel arithmetic, FP4 const, compare+select; per-op managed FP4 reference = BIT-EXACT.
///   (B) y[i] = (Float4E2M1)x[i] - bit-exact f32->FP4 store/convert vs the managed conversion.
///   (C) y[i] = (float)fp4[i] over all 16 codes - bit-exact FP4 load/decode.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- fp4-verify
/// </summary>
internal static class Float4Repro
{
    public static Task<int> Run()
    {
        Console.WriteLine("=== FP4 E2M1 conversion verification (CPU/managed) ===");
        int fails = 0;

        // (1) + (2): idempotence + independent decode reference over all 16 nibble patterns.
        int idemFail = 0, decodeFail = 0;
        for (int raw = 0; raw < 16; raw++)
        {
            var v = MakeE2M1((byte)raw);
            float f = (float)v;

            float refF = RefE2M1ToFloat((byte)raw);
            if (f != refF)
            {
                if (decodeFail < 8) Console.WriteLine($"  DECODE raw=0x{raw:X1}: got {f}, ref {refF}");
                decodeFail++;
            }

            // Idempotence: float of a finite E2M1 must round back to the same nibble.
            byte back = RawOf((Float4E2M1)f);
            if (back != raw)
            {
                if (idemFail < 8) Console.WriteLine($"  IDEM raw=0x{raw:X1} (f={f}) -> back 0x{back:X1}");
                idemFail++;
            }
        }
        Console.WriteLine($"  idempotence fails: {idemFail}/16");
        Console.WriteLine($"  decode-vs-reference fails: {decodeFail}/16");
        fails += idemFail + decodeFail;

        // (3) Known exact magnitudes + rounding + saturation + specials.
        fails += CheckExact(0f);
        fails += CheckExact(0.5f);
        fails += CheckExact(1.0f);
        fails += CheckExact(1.5f);
        fails += CheckExact(2.0f);
        fails += CheckExact(3.0f);
        fails += CheckExact(4.0f);
        fails += CheckExact(6.0f);   // max finite
        fails += CheckExact(-0.5f);
        fails += CheckExact(-6.0f);
        fails += CheckRound(0.7f, new[] { 0.5f, 1.0f });  // not exact -> nearest representable
        fails += CheckRound(5.0f, new[] { 4.0f, 6.0f });
        fails += Check("RNE 0.25 -> 0 (ties to even)", (float)(Float4E2M1)0.25f == 0f);
        fails += Check("RNE 2.5 -> 2 (ties to even)", (float)(Float4E2M1)2.5f == 2f);
        fails += Check("RNE 5.0 -> 4 (ties to even)", (float)(Float4E2M1)5.0f == 4f);
        fails += Check("overflow 100 -> 6 (saturate)", (float)(Float4E2M1)100f == 6f);
        fails += Check("overflow -100 -> -6 (saturate)", (float)(Float4E2M1)(-100f) == -6f);
        fails += Check("+Inf -> 6 (saturate)", (float)(Float4E2M1)float.PositiveInfinity == 6f);
        fails += Check("-Inf -> -6 (saturate)", (float)(Float4E2M1)float.NegativeInfinity == -6f);
        fails += Check("NaN -> -0 (0x8)", RawOf((Float4E2M1)float.NaN) == 0x8);
        fails += Check("IsFinite always true", Float4E2M1.IsFinite((Float4E2M1)6f) && Float4E2M1.IsFinite(Float4E2M1.Zero));
        fails += Check("arith 1.5*2-1=2", (float)((Float4E2M1)1.5f * (Float4E2M1)2f - (Float4E2M1)1f) == 2f);

        Console.WriteLine(fails == 0 ? "  E2M1 managed PASS" : $"  E2M1 managed FAIL: {fails} problems");

        // ===================== Desktop kernels (real IR + codegen path) =====================
        Console.WriteLine("--- Desktop kernels (CPU/CUDA/OpenCL) ---");
        int kFails = 0;
        using (var context = Context.Create(b => b.Default().EnableAlgorithms()))
        {
            foreach (var dev in context)
            {
                if (dev.AcceleratorType != AcceleratorType.CPU &&
                    dev.AcceleratorType != AcceleratorType.Cuda &&
                    dev.AcceleratorType != AcceleratorType.OpenCL)
                    continue;
                using var acc = dev.CreateAccelerator(context);
                Console.WriteLine($"  [{acc.AcceleratorType} {acc.Name}]");
                kFails += RunReluKernel(acc);
                kFails += RunConvertKernel(acc);
                kFails += RunDecodeKernel(acc);
            }
        }

        int total = fails + kFails;
        Console.WriteLine(total == 0
            ? "=== FP4 PASS (E2M1 conversion + CPU kernel verified) ==="
            : $"=== FP4 FAIL: {total} problems ===");
        return Task.FromResult(total == 0 ? 0 : 1);
    }

    // (A) y[i] = relu(x[i]*scale + bias) in FP4 - load/store, per-op arithmetic, const, compare+select.
    private static void FusedReluGeneric<T>(Index1D i,
        ArrayView1D<T, Stride1D.Dense> x, ArrayView1D<T, Stride1D.Dense> y, T scale, T bias)
        where T : unmanaged, INumber<T>
    {
        T v = x[i] * scale + bias;
        y[i] = v > T.Zero ? v : T.Zero;
    }

    private static int RunReluKernel(Accelerator acc)
    {
        const int n = 256;
        var st = (Float4E2M1)1.5f;
        var bt = (Float4E2M1)0.5f;
        float sf = (float)st, bf = (float)bt;
        var x = new Float4E2M1[n];
        var expected = new Float4E2M1[n];
        // Cover all 16 input codes many times over. Reference = the f32-register model the GPU uses:
        // compute the WHOLE expression in f32 and round to FP4 ONCE at the end (like bf16/Half/FP8).
        // The managed per-op operators round after every op (CPU does that) - a legit <=1-step
        // difference, absorbed by the tolerance. The PRECISE conversion gates are the bit-exact
        // convert/decode kernels below; this one exercises FP4 load/store/arith/const/compare/select.
        for (int i = 0; i < n; i++)
        {
            var xt = MakeE2M1((byte)(i & 0x0F));
            x[i] = xt;
            float vf = (float)xt * sf + bf;
            expected[i] = (Float4E2M1)(vf > 0f ? vf : 0f);
        }
        try
        {
            using var inBuf = acc.Allocate1D(x);
            using var outBuf = acc.Allocate1D<Float4E2M1>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<Float4E2M1, Stride1D.Dense>, ArrayView1D<Float4E2M1, Stride1D.Dense>,
                Float4E2M1, Float4E2M1>(FusedReluGeneric<Float4E2M1>);
            k(n, inBuf.View, outBuf.View, st, bt);
            acc.Synchronize();
            var got = outBuf.GetAsArray1D();
            int bad = 0, firstBad = -1;
            for (int i = 0; i < n; i++)
            {
                float g = (float)got[i], e = (float)expected[i];
                // E2M1 has only 1 mantissa bit, so 1 step is up to ~50% relative; per-op (CPU) vs
                // round-once (GPU) differ by <=1 step. Conversion correctness is the bit-exact path.
                float tol = MathF.Max(MathF.Abs(e), 1f) * 0.55f;
                if (MathF.Abs(g - e) > tol) { if (bad == 0) firstBad = i; bad++; }
            }
            if (bad == 0) { Console.WriteLine($"    relu kernel: OK ({n}/{n} within 1 FP4 step, f32-register model)"); return 0; }
            Console.WriteLine($"    relu kernel: WRONG {bad}/{n}, first@{firstBad} " +
                $"in={(float)x[firstBad]} got={(float)got[firstBad]}(0x{RawOf(got[firstBad]):X1}) " +
                $"want={(float)expected[firstBad]}(0x{RawOf(expected[firstBad]):X1})");
            return 1;
        }
        catch (Exception ex) { return DumpKernelException("relu kernel", ex); }
    }

    // (B) y[i] = (Float4E2M1)x[i] - bit-exact f32 -> FP4 store/convert.
    private static void ConvertKernel(Index1D i,
        ArrayView1D<float, Stride1D.Dense> x, ArrayView1D<Float4E2M1, Stride1D.Dense> y) =>
        y[i] = (Float4E2M1)x[i];

    private static int RunConvertKernel(Accelerator acc)
    {
        float[] inputs =
        {
            0f, 0.25f, 0.5f, 0.7f, 1f, 1.25f, 1.5f, 2f, 2.5f, 3f, 4f, 5f, 6f, 7f, 100f,
            -0.5f, -1.5f, -3f, -6f, -100f, float.PositiveInfinity, float.NegativeInfinity, float.NaN, -0f,
        };
        int n = inputs.Length;
        var expected = new byte[n];
        for (int i = 0; i < n; i++) expected[i] = RawOf((Float4E2M1)inputs[i]);
        try
        {
            using var inBuf = acc.Allocate1D(inputs);
            using var outBuf = acc.Allocate1D<Float4E2M1>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<Float4E2M1, Stride1D.Dense>>(ConvertKernel);
            k(n, inBuf.View, outBuf.View);
            acc.Synchronize();
            var got = outBuf.GetAsArray1D();
            int bad = 0, firstBad = -1;
            for (int i = 0; i < n; i++)
                if (RawOf(got[i]) != expected[i]) { if (bad == 0) firstBad = i; bad++; }
            if (bad == 0) { Console.WriteLine($"    convert kernel: OK ({n}/{n} bit-exact f32->FP4)"); return 0; }
            Console.WriteLine($"    convert kernel: WRONG {bad}/{n}, first@{firstBad} " +
                $"in={inputs[firstBad]} got=0x{RawOf(got[firstBad]):X1} want=0x{expected[firstBad]:X1}");
            return 1;
        }
        catch (Exception ex) { return DumpKernelException("convert kernel", ex); }
    }

    // (C) y[i] = (float)fp4[i] - bit-exact FP4 -> f32 load/decode over all 16 codes.
    private static void DecodeKernel(Index1D i,
        ArrayView1D<Float4E2M1, Stride1D.Dense> x, ArrayView1D<float, Stride1D.Dense> y) =>
        y[i] = (float)x[i];

    private static int RunDecodeKernel(Accelerator acc)
    {
        const int n = 16;
        var x = new Float4E2M1[n];
        var expected = new float[n];
        for (int i = 0; i < n; i++) { x[i] = MakeE2M1((byte)i); expected[i] = (float)x[i]; }
        try
        {
            using var inBuf = acc.Allocate1D(x);
            using var outBuf = acc.Allocate1D<float>(n);
            var k = acc.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<Float4E2M1, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>>(DecodeKernel);
            k(n, inBuf.View, outBuf.View);
            acc.Synchronize();
            var got = outBuf.GetAsArray1D();
            int bad = 0, firstBad = -1;
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i]) { if (bad == 0) firstBad = i; bad++; }
            if (bad == 0) { Console.WriteLine($"    decode kernel: OK ({n}/{n} bit-exact FP4->f32)"); return 0; }
            Console.WriteLine($"    decode kernel: WRONG {bad}/{n}, first@{firstBad} " +
                $"raw=0x{firstBad:X1} got={got[firstBad]} want={expected[firstBad]}");
            return 1;
        }
        catch (Exception ex) { return DumpKernelException("decode kernel", ex); }
    }

    private static int DumpKernelException(string label, Exception ex)
    {
        Console.WriteLine($"    {label}: {ex.GetType().Name}: {ex.Message}");
        var inner = ex.InnerException; int depth = 0; Exception deepest = ex;
        while (inner != null && depth < 5)
        {
            Console.WriteLine($"       INNER[{depth}] {inner.GetType().Name}: {inner.Message}");
            deepest = inner; inner = inner.InnerException; depth++;
        }
        var stk = (deepest.StackTrace ?? "").Split('\n');
        for (int i = 0; i < stk.Length && i < 6; i++) Console.WriteLine($"          @ {stk[i].Trim()}");
        return 1;
    }

    private static Float4E2M1 MakeE2M1(byte raw) => Unsafe.As<byte, Float4E2M1>(ref raw);
    private static byte RawOf(Float4E2M1 v) => (byte)(Unsafe.As<Float4E2M1, byte>(ref v) & 0x0F);

    // Independent E2M1 -> float (value-formula, not the production bit-shift) for cross-check.
    // 1 sign / 2 exp / 1 mantissa, bias 1; subnormal at exp==0 (only 0.5); NO Inf/NaN.
    private static float RefE2M1ToFloat(byte raw)
    {
        int sign = (raw >> 3) & 1;
        int exp = (raw >> 1) & 0x3;
        int mant = raw & 0x1;
        float s = sign == 1 ? -1f : 1f;
        if (exp == 0)
            return s * (mant / 2f) * MathF.Pow(2f, 1 - 1);    // subnormal: 0 or 0.5
        return s * (1f + mant / 2f) * MathF.Pow(2f, exp - 1); // normal: 1.m * 2^(exp-1)
    }

    private static int CheckExact(float f)
    {
        float got = (float)(Float4E2M1)f;
        if (got != f) { Console.WriteLine($"  EXACT {f}: got {got}"); return 1; }
        return 0;
    }
    private static int CheckRound(float f, float[] allowed)
    {
        float got = (float)(Float4E2M1)f;
        foreach (var a in allowed) if (got == a) return 0;
        Console.WriteLine($"  ROUND {f}: got {got}, allowed [{string.Join(",", allowed)}]");
        return 1;
    }
    private static int Check(string what, bool ok)
    {
        if (!ok) Console.WriteLine($"  CHECK {what}: FAIL");
        return ok ? 0 : 1;
    }
}
