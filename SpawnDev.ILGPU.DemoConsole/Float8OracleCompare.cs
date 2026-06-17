using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using ILGPU;

/// <summary>
/// Validates the production FP8 conversions (ILGPU.Float8E4M3 / Float8E5M2) against the
/// ml_dtypes reference (the impl PyTorch / JAX float8_e4m3fn / float8_e5m2 share). The oracle
/// JSONs are produced by _research/fp8_oracle/gen_e4m3_oracle.py / gen_e5m2_oracle.py.
///
/// Answers the convention question flagged in Float8E4M3.cs's header (overflow saturate-to-448
/// vs overflow-to-NaN) with EVIDENCE - it runs the same managed (Float8*)f / (float)v operators
/// production uses (and which fp8-verify already proves the 6 GPU backends mirror bit-exact).
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- fp8-oracle [oracleDir]
/// </summary>
internal static class Float8OracleCompare
{
    public static Task<int> Run(string[] args)
    {
        string dir = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "_research", "fp8_oracle");
        dir = Path.GetFullPath(dir);
        Console.WriteLine($"=== FP8 oracle comparison (ml_dtypes float8_e4m3fn / e5m2) ===");
        Console.WriteLine($"oracle dir: {dir}");

        int total = 0;
        total += CompareE4M3(Path.Combine(dir, "oracle_e4m3.json"));
        total += CompareE5M2(Path.Combine(dir, "oracle_e5m2.json"));

        Console.WriteLine();
        Console.WriteLine(total == 0
            ? "RESULT: managed FP8 conversions MATCH the ml_dtypes reference exactly."
            : $"RESULT: {total} divergence categories vs ml_dtypes reference (see above).");
        return Task.FromResult(total == 0 ? 0 : 1);
    }

    private static int CompareE4M3(string path)
    {
        Console.WriteLine("\n--- Float8E4M3 vs float8_e4m3fn ---");
        if (!File.Exists(path)) { Console.WriteLine($"  MISSING {path}"); return 1; }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // DECODE: all 256 byte patterns. No convention ambiguity - any mismatch is a pure bug.
        int decodeFail = 0;
        foreach (var row in root.GetProperty("decode").EnumerateArray())
        {
            byte b = (byte)row.GetProperty("byte").GetInt32();
            uint oracleBits = row.GetProperty("f32bits").GetUInt32();
            float got = (float)Unsafe.As<byte, Float8E4M3>(ref b);
            float oracle = BitConverter.UInt32BitsToSingle(oracleBits);
            if (float.IsNaN(got) && float.IsNaN(oracle)) continue;
            uint gotBits = BitConverter.SingleToUInt32Bits(got);
            if (gotBits != oracleBits)
            {
                if (decodeFail < 8)
                    Console.WriteLine($"  DECODE 0x{b:X2}: managed {got} (0x{gotBits:X8}) != oracle {oracle} (0x{oracleBits:X8})");
                decodeFail++;
            }
        }
        Console.WriteLine($"  decode mismatches: {decodeFail}/256");

        // ENCODE: f32 -> e4m3. The DEFAULT (cast operator AND FromSingleFn) is now fn = bit-exact
        // to float8_e4m3fn, INCLUDING overflow->NaN. Any divergence here is a real bug.
        int n = 0, castFail = 0, fnFail = 0;
        string firstCastFail = null, firstFnFail = null;
        foreach (var row in root.GetProperty("encode").EnumerateArray())
        {
            n++;
            uint inBits = row.GetProperty("f32bits").GetUInt32();
            byte oracle = (byte)row.GetProperty("e4m3").GetInt32();
            float input = BitConverter.UInt32BitsToSingle(inBits);
            bool oracleNaN = (oracle & 0x7F) == 0x7F;

            Float8E4M3 c = (Float8E4M3)input;                 // cast operator = fn
            byte cb = Unsafe.As<Float8E4M3, byte>(ref c);
            if (!(cb == oracle || ((cb & 0x7F) == 0x7F && oracleNaN)))
            { castFail++; firstCastFail ??= $"input {input} (0x{inBits:X8}) -> cast 0x{cb:X2} vs oracle 0x{oracle:X2}"; }

            Float8E4M3 f = Float8E4M3.FromSingleFn(input);    // explicit fn (should equal the cast)
            byte fb = Unsafe.As<Float8E4M3, byte>(ref f);
            if (!(fb == oracle || ((fb & 0x7F) == 0x7F && oracleNaN)))
            { fnFail++; firstFnFail ??= $"input {input} (0x{inBits:X8}) -> FromSingleFn 0x{fb:X2} vs oracle 0x{oracle:X2}"; }
        }
        Console.WriteLine($"  [cast operator = fn] encode exact vs float8_e4m3fn: {n - castFail}/{n}  divergences (must be 0): {castFail}" + (firstCastFail != null ? $"  e.g. {firstCastFail}" : ""));
        Console.WriteLine($"  [FromSingleFn] encode exact vs float8_e4m3fn: {n - fnFail}/{n}  divergences (must be 0): {fnFail}" + (firstFnFail != null ? $"  e.g. {firstFnFail}" : ""));

        // FromSingleSaturating: the OPT-IN saturating convention. Expected = the fn oracle EXCEPT a
        // FINITE input the oracle maps to NaN (overflow) becomes +-448; +-Inf stays NaN.
        int satFail = 0; string firstSatFail = null;
        foreach (var row in root.GetProperty("encode").EnumerateArray())
        {
            uint inBits = row.GetProperty("f32bits").GetUInt32();
            byte oracle = (byte)row.GetProperty("e4m3").GetInt32();
            float input = BitConverter.UInt32BitsToSingle(inBits);
            bool oracleNaN = (oracle & 0x7F) == 0x7F;
            // expected saturating byte
            byte expSat;
            if (oracleNaN && !float.IsNaN(input) && !float.IsInfinity(input))
                expSat = (byte)((oracle & 0x80) | 0x7E);   // finite overflow -> +-448
            else
                expSat = oracle;                            // in-range / +-Inf / NaN = same as fn
            Float8E4M3 s = Float8E4M3.FromSingleSaturating(input);
            byte sb = Unsafe.As<Float8E4M3, byte>(ref s);
            bool ok = sb == expSat || ((sb & 0x7F) == 0x7F && (expSat & 0x7F) == 0x7F);
            if (!ok) { satFail++; firstSatFail ??= $"input {input} (0x{inBits:X8}) -> sat 0x{sb:X2} vs expected 0x{expSat:X2}"; }
        }
        Console.WriteLine($"  [FromSingleSaturating] matches saturating convention: {n - satFail}/{n}  divergences (must be 0): {satFail}" + (firstSatFail != null ? $"  e.g. {firstSatFail}" : ""));
        Console.WriteLine($"  [FromSingleFn] divergences (must be 0): {fnFail}" + (firstFnFail != null ? $"  e.g. {firstFnFail}" : ""));

        // Specials (also covered by the encode sweep's Inf/NaN inputs; printed explicitly):
        var sp = root.GetProperty("specials");
        PrintSpecialE4("+Inf", sp.GetProperty("pos_inf"));
        PrintSpecialE4("-Inf", sp.GetProperty("neg_inf"));
        PrintSpecialE4("NaN", sp.GetProperty("nan"));

        // Every path must be exact: decode, the fn cast operator, FromSingleFn, and the
        // FromSingleSaturating convention. All count as unconditional failures.
        return decodeFail + castFail + fnFail + satFail;
    }

    private static int CompareE5M2(string path)
    {
        Console.WriteLine("\n--- Float8E5M2 vs float8_e5m2 ---");
        if (!File.Exists(path)) { Console.WriteLine($"  MISSING {path}"); return 1; }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        int decodeFail = 0;
        foreach (var row in root.GetProperty("decode").EnumerateArray())
        {
            byte b = (byte)row.GetProperty("byte").GetInt32();
            uint oracleBits = row.GetProperty("f32bits").GetUInt32();
            float got = (float)Unsafe.As<byte, Float8E5M2>(ref b);
            if (float.IsNaN(got) && float.IsNaN(BitConverter.UInt32BitsToSingle(oracleBits))) continue;
            uint gotBits = BitConverter.SingleToUInt32Bits(got);
            if (gotBits != oracleBits)
            {
                if (decodeFail < 8)
                    Console.WriteLine($"  DECODE 0x{b:X2}: managed 0x{gotBits:X8} != oracle 0x{oracleBits:X8}");
                decodeFail++;
            }
        }
        Console.WriteLine($"  decode mismatches: {decodeFail}/256");

        int encExact = 0, encOther = 0, n = 0; string firstOther = null;
        foreach (var row in root.GetProperty("encode").EnumerateArray())
        {
            n++;
            uint inBits = row.GetProperty("f32bits").GetUInt32();
            byte oracle = (byte)row.GetProperty("e5m2").GetInt32();
            float input = BitConverter.UInt32BitsToSingle(inBits);
            Float8E5M2 v = (Float8E5M2)input;
            byte got = Unsafe.As<Float8E5M2, byte>(ref v);
            if (got == oracle) { encExact++; continue; }
            // both NaN-pattern (exp=11111, mant!=0) = match
            bool gotNaN = (got & 0x7C) == 0x7C && (got & 0x03) != 0;
            bool oracleNaN = (oracle & 0x7C) == 0x7C && (oracle & 0x03) != 0;
            if (gotNaN && oracleNaN) { encExact++; continue; }
            encOther++;
            if (firstOther == null) firstOther = $"input {input} (0x{inBits:X8}) -> managed 0x{got:X2} vs oracle 0x{oracle:X2}";
        }
        Console.WriteLine($"  encode exact (NaN-pattern tolerant): {encExact}/{n}");
        Console.WriteLine($"  encode divergences: {encOther}" + (firstOther != null ? $"  e.g. {firstOther}" : ""));

        var sp = root.GetProperty("specials");
        PrintSpecialE5("+Inf", sp.GetProperty("pos_inf"));
        PrintSpecialE5("-Inf", sp.GetProperty("neg_inf"));
        PrintSpecialE5("NaN", sp.GetProperty("nan"));
        return decodeFail + encOther;
    }

    private static void PrintSpecialE4(string name, JsonElement e)
    {
        uint inBits = e.GetProperty("f32bits").GetUInt32();
        byte oracle = (byte)e.GetProperty("e4m3").GetInt32();
        float input = BitConverter.UInt32BitsToSingle(inBits);
        Float8E4M3 v = (Float8E4M3)input;
        byte got = Unsafe.As<Float8E4M3, byte>(ref v);
        Console.WriteLine($"  special {name}: managed 0x{got:X2}  oracle 0x{oracle:X2}  {(got == oracle ? "OK" : "DIFF")}");
    }
    private static void PrintSpecialE5(string name, JsonElement e)
    {
        uint inBits = e.GetProperty("f32bits").GetUInt32();
        byte oracle = (byte)e.GetProperty("e5m2").GetInt32();
        float input = BitConverter.UInt32BitsToSingle(inBits);
        Float8E5M2 v = (Float8E5M2)input;
        byte got = Unsafe.As<Float8E5M2, byte>(ref v);
        Console.WriteLine($"  special {name}: managed 0x{got:X2}  oracle 0x{oracle:X2}  {(got == oracle ? "OK" : "DIFF")}");
    }
}
