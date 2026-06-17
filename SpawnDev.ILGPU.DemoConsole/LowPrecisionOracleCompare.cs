using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using ILGPU;

/// <summary>
/// Validates the production BFloat16 and ILGPU.Half conversions against the authoritative references
/// (ml_dtypes.bfloat16 = PyTorch/JAX bfloat16; numpy.float16 = IEEE binary16). 16-bit types are fully
/// enumerable, so this is EXHAUSTIVE: all 65536 decode patterns + all 65536 round-trip identities +
/// a probe set (RNE midpoints / overflow / subnormal / specials). Oracle JSONs from
/// _research/fp8_oracle/gen_bf16_f16_oracle.py.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- bf16-f16-oracle [oracleDir]
/// </summary>
internal static class LowPrecisionOracleCompare
{
    public static Task<int> Run(string[] args)
    {
        string dir = args.Length > 1
            ? args[1]
            : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "_research", "fp8_oracle");
        dir = Path.GetFullPath(dir);
        Console.WriteLine("=== bf16 / float16 oracle comparison (ml_dtypes.bfloat16 / numpy.float16) ===");
        Console.WriteLine($"oracle dir: {dir}");

        int total = 0;
        total += Compare(Path.Combine(dir, "oracle_bfloat16.json"), "BFloat16",
            raw => (float)Unsafe.As<ushort, BFloat16>(ref raw),
            f => { var v = (BFloat16)f; return Unsafe.As<BFloat16, ushort>(ref v); },
            isNaN: r => (r & 0x7F80) == 0x7F80 && (r & 0x007F) != 0,
            isInf: r => (r & 0x7FFF) == 0x7F80);
        total += Compare(Path.Combine(dir, "oracle_float16.json"), "Half",
            raw => (float)Unsafe.As<ushort, ILGPU.Half>(ref raw),
            f => { var v = (ILGPU.Half)f; return Unsafe.As<ILGPU.Half, ushort>(ref v); },
            isNaN: r => (r & 0x7C00) == 0x7C00 && (r & 0x03FF) != 0,
            isInf: r => (r & 0x7FFF) == 0x7C00);

        Console.WriteLine();
        Console.WriteLine(total == 0
            ? "RESULT: managed bf16 + Half conversions MATCH their references exactly."
            : $"RESULT: {total} divergences vs the references (see above).");
        return Task.FromResult(total == 0 ? 0 : 1);
    }

    private static int Compare(string path, string name,
        Func<ushort, float> decode, Func<float, ushort> encode,
        Func<int, bool> isNaN, Func<int, bool> isInf)
    {
        Console.WriteLine($"\n--- {name} ---");
        if (!File.Exists(path)) { Console.WriteLine($"  MISSING {path} (run gen_bf16_f16_oracle.py)"); return 1; }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;

        // DECODE: all 65536 patterns (index = raw 16-bit value). No ambiguity - any mismatch is a bug.
        var decodeArr = root.GetProperty("decode");
        int n = decodeArr.GetArrayLength();
        int decodeFail = 0; string firstDecode = null;
        for (int raw = 0; raw < n; raw++)
        {
            uint oracleBits = decodeArr[raw].GetUInt32();
            float got = decode((ushort)raw);
            float oracle = BitConverter.UInt32BitsToSingle(oracleBits);
            if (float.IsNaN(got) && float.IsNaN(oracle)) continue;
            uint gotBits = BitConverter.SingleToUInt32Bits(got);
            if (gotBits != oracleBits)
            {
                if (decodeFail < 6) Console.WriteLine($"  DECODE 0x{raw:X4}: managed 0x{gotBits:X8} != oracle 0x{oracleBits:X8}");
                decodeFail++;
            }
        }
        Console.WriteLine($"  decode (all {n} patterns): {n - decodeFail}/{n}  mismatches: {decodeFail}");

        // ROUND-TRIP IDENTITY: every representable value's f32 must encode back to its own pattern
        // (finite + Inf; NaN excepted - any NaN pattern is acceptable as long as it stays NaN).
        int rtFail = 0; string firstRt = null;
        for (int raw = 0; raw < n; raw++)
        {
            uint oracleBits = decodeArr[raw].GetUInt32();
            float f = BitConverter.UInt32BitsToSingle(oracleBits);
            if (float.IsNaN(f)) continue;                 // NaN round-trip handled by the probe/special checks
            ushort back = encode(f);
            if (back != raw)
            {
                if (rtFail < 6) { firstRt ??= $"0x{raw:X4} (f={f}) -> back 0x{back:X4}"; }
                rtFail++;
            }
        }
        Console.WriteLine($"  round-trip identity (all {n}): {n - rtFail}/{n}  fails: {rtFail}" + (firstRt != null ? $"  e.g. {firstRt}" : ""));

        // ENCODE PROBES: RNE midpoints / overflow / subnormal / dense sweep. NaN/Inf-tolerant.
        var enc = root.GetProperty("encode");
        int probeFail = 0; string firstProbe = null; int probes = enc.GetArrayLength();
        foreach (var row in enc.EnumerateArray())
        {
            uint inBits = row.GetProperty("f32bits").GetUInt32();
            int oracle = row.GetProperty("raw16").GetInt32();
            float input = BitConverter.UInt32BitsToSingle(inBits);
            int got = encode(input);
            if (got == oracle) continue;
            if (isNaN(got) && isNaN(oracle)) continue;    // both NaN-pattern
            probeFail++;
            firstProbe ??= $"input {input} (0x{inBits:X8}) -> managed 0x{got:X4} vs oracle 0x{oracle:X4}";
        }
        Console.WriteLine($"  encode probes: {probes - probeFail}/{probes}  divergences: {probeFail}" + (firstProbe != null ? $"  e.g. {firstProbe}" : ""));

        // Specials
        var sp = root.GetProperty("specials");
        foreach (var key in new[] { "pos_inf", "neg_inf", "nan" })
        {
            var e = sp.GetProperty(key);
            float input = BitConverter.UInt32BitsToSingle(e.GetProperty("f32bits").GetUInt32());
            int oracle = e.GetProperty("raw16").GetInt32();
            int got = encode(input);
            bool ok = got == oracle || (isNaN(got) && isNaN(oracle));
            Console.WriteLine($"  special {key}: managed 0x{got:X4} oracle 0x{oracle:X4} {(ok ? "OK" : "DIFF")}");
        }

        return decodeFail + rtFail + probeFail;
    }
}
