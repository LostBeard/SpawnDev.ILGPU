using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;

/// <summary>
/// CPU verification of the FP8 conversion math (no kernel, pure managed) BEFORE wiring the IR
/// primitive + backends on top — silent FP8 conversion bugs would corrupt ML. Checks E5M2:
///   (1) IDEMPOTENCE over all 256 raw patterns: (E5M2)((float)raw) == raw (a finite E5M2's float
///       must round back to the same E5M2). NaN excepted (bit pattern may differ, still NaN).
///   (2) An independent f-of-fields reference for the E5M2->float decode.
///   (3) Known exact values + specials (Inf/NaN/+-0/max-normal/overflow).
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- fp8-verify
/// </summary>
internal static class Float8Repro
{
    public static Task<int> Run()
    {
        Console.WriteLine("=== FP8 E5M2 conversion verification (CPU/managed) ===");
        int fails = 0;

        // (1) + (2): idempotence + independent decode reference over all 256 patterns.
        int idemFail = 0, decodeFail = 0;
        for (int raw = 0; raw < 256; raw++)
        {
            var v = MakeE5M2((byte)raw);
            float f = (float)v;

            // Independent reference decode of E5M2 -> float.
            float refF = RefE5M2ToFloat((byte)raw);
            bool bothNaN = float.IsNaN(f) && float.IsNaN(refF);
            if (!bothNaN && f != refF)
            {
                if (decodeFail < 6)
                    Console.WriteLine($"  DECODE raw=0x{raw:X2}: got {f}, ref {refF}");
                decodeFail++;
            }

            // Idempotence: float of a finite E5M2 must round back to the same byte.
            if (!float.IsNaN(f))
            {
                byte back = RawOf((Float8E5M2)f);
                if (back != raw)
                {
                    if (idemFail < 6)
                        Console.WriteLine($"  IDEM raw=0x{raw:X2} (f={f}) -> back 0x{back:X2}");
                    idemFail++;
                }
            }
        }
        Console.WriteLine($"  idempotence fails: {idemFail}/256 (NaN excepted)");
        Console.WriteLine($"  decode-vs-reference fails: {decodeFail}/256");
        fails += idemFail + decodeFail;

        // (3) Known exact values + specials.
        fails += CheckExact(1.0f);
        fails += CheckExact(2.0f);
        fails += CheckExact(0.5f);
        fails += CheckExact(1.5f);   // 1.10b - exact (2 mantissa bits)
        fails += CheckExact(1.25f);  // 1.01b - exact
        fails += CheckExact(-3.0f);
        fails += CheckExact(57344f); // E5M2 max normal
        fails += CheckExact(0f);
        fails += CheckRound(1.1f, new[] { 1.0f, 1.25f });   // not exact -> nearest representable
        fails += Check("overflow->Inf", float.IsPositiveInfinity((float)(Float8E5M2)100000f));
        fails += Check("+Inf round-trips", float.IsPositiveInfinity((float)(Float8E5M2)float.PositiveInfinity));
        fails += Check("-Inf round-trips", float.IsNegativeInfinity((float)(Float8E5M2)float.NegativeInfinity));
        fails += Check("NaN round-trips", float.IsNaN((float)(Float8E5M2)float.NaN));
        fails += Check("arith 1.5*2+(-0.5)=2.5", (float)((Float8E5M2)1.5f * (Float8E5M2)2f + (Float8E5M2)(-0.5f)) == 2.5f);

        Console.WriteLine(fails == 0
            ? "  E5M2 PASS"
            : $"  E5M2 FAIL: {fails} problems");

        // ===================== E4M3 =====================
        Console.WriteLine("--- E4M3 (no Inf; NaN=0x7F; max 448; saturate) ---");
        int e4Fails = 0, e4Idem = 0, e4Decode = 0;
        for (int raw = 0; raw < 256; raw++)
        {
            byte rb = (byte)raw;
            var v = Unsafe.As<byte, Float8E4M3>(ref rb);
            float f = (float)v;
            float refF = RefE4M3ToFloat((byte)raw);
            bool bothNaN = float.IsNaN(f) && float.IsNaN(refF);
            if (!bothNaN && f != refF)
            {
                if (e4Decode < 6) Console.WriteLine($"  DECODE raw=0x{raw:X2}: got {f}, ref {refF}");
                e4Decode++;
            }
            if (!float.IsNaN(f))
            {
                var ve = (Float8E4M3)f;
                byte back = Unsafe.As<Float8E4M3, byte>(ref ve);
                if (back != raw)
                {
                    if (e4Idem < 6) Console.WriteLine($"  IDEM raw=0x{raw:X2} (f={f}) -> back 0x{back:X2}");
                    e4Idem++;
                }
            }
        }
        Console.WriteLine($"  idempotence fails: {e4Idem}/256 (NaN excepted)");
        Console.WriteLine($"  decode-vs-reference fails: {e4Decode}/256");
        e4Fails += e4Idem + e4Decode;
        e4Fails += Check("E4M3 1.0 exact", (float)(Float8E4M3)1.0f == 1.0f);
        e4Fails += Check("E4M3 1.25 exact", (float)(Float8E4M3)1.25f == 1.25f);    // 1.010b (3 mantissa)
        e4Fails += Check("E4M3 max=448", (float)(Float8E4M3)448f == 448f);
        e4Fails += Check("E4M3 overflow saturates to 448", (float)(Float8E4M3)100000f == 448f);
        e4Fails += Check("E4M3 -overflow saturates to -448", (float)(Float8E4M3)(-100000f) == -448f);
        e4Fails += Check("E4M3 Inf->NaN", float.IsNaN((float)(Float8E4M3)float.PositiveInfinity));
        e4Fails += Check("E4M3 NaN->NaN", float.IsNaN((float)(Float8E4M3)float.NaN));
        e4Fails += Check("E4M3 +0", (float)(Float8E4M3)0f == 0f);
        e4Fails += Check("E4M3 arith 1.5*2-0.5=2.5", (float)((Float8E4M3)1.5f * (Float8E4M3)2f - (Float8E4M3)0.5f) == 2.5f);
        Console.WriteLine(e4Fails == 0 ? "  E4M3 PASS" : $"  E4M3 FAIL: {e4Fails} problems");

        int total = fails + e4Fails;
        Console.WriteLine(total == 0
            ? "=== FP8 PASS (E5M2 + E4M3 conversion verified) ==="
            : $"=== FP8 FAIL: {total} problems ===");
        return Task.FromResult(total == 0 ? 0 : 1);
    }

    private static float RefE4M3ToFloat(byte raw)
    {
        int sign = (raw >> 7) & 1;
        int exp = (raw >> 3) & 0xF;
        int mant = raw & 0x7;
        float s = sign == 1 ? -1f : 1f;
        if ((raw & 0x7F) == 0x7F) return float.NaN;                  // the only NaN
        if (exp == 0) return s * (mant / 8f) * MathF.Pow(2f, 1 - 7); // subnormal / +-0
        return s * (1f + mant / 8f) * MathF.Pow(2f, exp - 7);        // normal
    }

    private static Float8E5M2 MakeE5M2(byte raw)
    {
        // No public byte ctor (internal); reinterpret a 1-byte struct from the raw byte.
        return Unsafe.As<byte, Float8E5M2>(ref raw);
    }
    private static byte RawOf(Float8E5M2 v) => Unsafe.As<Float8E5M2, byte>(ref v);

    // Independent E5M2 -> float (value-formula, not bit-shift) for cross-check.
    private static float RefE5M2ToFloat(byte raw)
    {
        int sign = (raw >> 7) & 1;
        int exp = (raw >> 2) & 0x1F;
        int mant = raw & 0x3;
        float s = sign == 1 ? -1f : 1f;
        if (exp == 0)
            return s * (mant / 4f) * MathF.Pow(2f, 1 - 15);          // subnormal (or +-0)
        if (exp == 0x1F)
            return mant == 0 ? s * float.PositiveInfinity : float.NaN; // Inf / NaN
        return s * (1f + mant / 4f) * MathF.Pow(2f, exp - 15);        // normal
    }

    private static int CheckExact(float f)
    {
        float got = (float)(Float8E5M2)f;
        if (got != f) { Console.WriteLine($"  EXACT {f}: got {got}"); return 1; }
        return 0;
    }
    private static int CheckRound(float f, float[] allowed)
    {
        float got = (float)(Float8E5M2)f;
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
