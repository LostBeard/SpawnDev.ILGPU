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

        // ENCODE: f32 -> e4m3. Categorize the divergences (the flagged convention lives here).
        int encExact = 0, encOverflowSatVsNaN = 0, encOther = 0, n = 0;
        string firstSat = null, firstOther = null;
        foreach (var row in root.GetProperty("encode").EnumerateArray())
        {
            n++;
            uint inBits = row.GetProperty("f32bits").GetUInt32();
            byte oracle = (byte)row.GetProperty("e4m3").GetInt32();
            float input = BitConverter.UInt32BitsToSingle(inBits);
            Float8E4M3 v = (Float8E4M3)input;
            byte got = Unsafe.As<Float8E4M3, byte>(ref v);
            if (got == oracle) { encExact++; continue; }
            bool gotNaN = (got & 0x7F) == 0x7F, oracleNaN = (oracle & 0x7F) == 0x7F;
            bool gotSat = (got & 0x7F) == 0x7E;   // managed saturates to +-448
            if (gotSat && oracleNaN)
            {
                encOverflowSatVsNaN++;
                firstSat ??= $"input {input} -> managed 0x{got:X2} (448) vs oracle 0x{oracle:X2} (NaN)";
            }
            else
            {
                encOther++;
                if (firstOther == null && encOther <= 1)
                    firstOther = $"input {input} (0x{inBits:X8}) -> managed 0x{got:X2} vs oracle 0x{oracle:X2}";
            }
        }
        Console.WriteLine($"  [saturating cast] encode exact: {encExact}/{n}");
        Console.WriteLine($"  [saturating cast] encode overflow saturate-vs-NaN divergences (EXPECTED - convention): {encOverflowSatVsNaN}" + (firstSat != null ? $"  e.g. {firstSat}" : ""));
        Console.WriteLine($"  [saturating cast] encode OTHER divergences (rounding/subnormal - real bugs if >0): {encOther}" + (firstOther != null ? $"  e.g. {firstOther}" : ""));

        // fn convention: FromSingleFn must match float8_e4m3fn EXACTLY, including overflow->NaN.
        int fnExact = 0, fnFail = 0; string firstFnFail = null;
        foreach (var row in root.GetProperty("encode").EnumerateArray())
        {
            uint inBits = row.GetProperty("f32bits").GetUInt32();
            byte oracle = (byte)row.GetProperty("e4m3").GetInt32();
            float input = BitConverter.UInt32BitsToSingle(inBits);
            Float8E4M3 v = Float8E4M3.FromSingleFn(input);
            byte got = Unsafe.As<Float8E4M3, byte>(ref v);
            if (got == oracle) { fnExact++; continue; }
            bool gotNaN = (got & 0x7F) == 0x7F, oracleNaN = (oracle & 0x7F) == 0x7F;
            if (gotNaN && oracleNaN) { fnExact++; continue; } // both NaN-slot (sign tolerant)
            fnFail++;
            firstFnFail ??= $"input {input} (0x{inBits:X8}) -> FromSingleFn 0x{got:X2} vs oracle 0x{oracle:X2}";
        }
        Console.WriteLine($"  [FromSingleFn] encode exact vs float8_e4m3fn: {fnExact}/{n}");
        Console.WriteLine($"  [FromSingleFn] divergences (must be 0): {fnFail}" + (firstFnFail != null ? $"  e.g. {firstFnFail}" : ""));

        // Specials (also covered by the encode sweep's Inf/NaN inputs; printed explicitly):
        var sp = root.GetProperty("specials");
        PrintSpecialE4("+Inf", sp.GetProperty("pos_inf"));
        PrintSpecialE4("-Inf", sp.GetProperty("neg_inf"));
        PrintSpecialE4("NaN", sp.GetProperty("nan"));

        // Decode bugs + encode "other" (rounding/subnormal) + any FromSingleFn divergence are
        // unconditional failures. The saturating cast's overflow->448 vs reference NaN is the
        // documented CONVENTION (counted separately above, NOT auto-failed).
        return decodeFail + encOther + fnFail;
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
