using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;

/// <summary>
/// Validates the packed 4-bit STORAGE foundation: an ArrayView&lt;QInt4&gt; ([PackedBits(4)]) of N
/// elements allocates ceil(N/2) DEVICE bytes (2 nibbles/byte) - the real 4-bit memory win - while
/// every whole-byte type is byte-for-byte unchanged (byte=N, int=4N). Host-only (no kernel, no copy):
/// proves the allocation byte-math + the no-op default for existing types. The nibble load/store and
/// host pack/unpack are the next steps.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- packed-alloc-verify
/// </summary>
internal static class PackedAllocVerify
{
    public static Task<int> Run()
    {
        Console.WriteLine("=== Packed 4-bit storage allocation foundation ===");
        int fails = 0;
        using var ctx = Context.Create(b => b.Default());
        foreach (var dev in ctx)
        {
            // CPU is enough to validate the core allocation byte-math (shared by all backends -
            // every backend buffer allocates MemoryBuffer.LengthInBytes).
            if (dev.AcceleratorType != AcceleratorType.CPU) continue;
            using var acc = dev.CreateAccelerator(ctx);
            Console.WriteLine($"  [{acc.AcceleratorType}]");

            int[] sizes = { 1, 2, 3, 4, 5, 15, 16, 17, 255, 256, 257, 4096 };
            foreach (int n in sizes)
            {
                using var buf = acc.Allocate1D<QInt4>(n);
                long expectedBytes = (n + 1) / 2;          // ceil(n/2)
                long gotBytes = buf.LengthInBytes;
                long gotElems = buf.Length;
                if (gotBytes != expectedBytes || gotElems != n)
                {
                    Console.WriteLine($"    QInt4 N={n}: bytes={gotBytes} (want {expectedBytes}), elems={gotElems} (want {n})  FAIL");
                    fails++;
                }
            }
            Console.WriteLine($"    QInt4 packed (2/byte): {sizes.Length - 0} sizes checked, {fails} fail");

            // Whole-byte regression: byte = N bytes, int = 4N bytes, double = 8N bytes (unchanged).
            int rfails = 0;
            using (var b = acc.Allocate1D<byte>(100)) if (b.LengthInBytes != 100) { rfails++; Console.WriteLine($"    byte: {b.LengthInBytes} != 100"); }
            using (var i = acc.Allocate1D<int>(100)) if (i.LengthInBytes != 400) { rfails++; Console.WriteLine($"    int: {i.LengthInBytes} != 400"); }
            using (var d = acc.Allocate1D<double>(100)) if (d.LengthInBytes != 800) { rfails++; Console.WriteLine($"    double: {d.LengthInBytes} != 800"); }
            using (var h = acc.Allocate1D<global::ILGPU.Half>(100)) if (h.LengthInBytes != 200) { rfails++; Console.WriteLine($"    Half: {h.LengthInBytes} != 200"); }
            using (var f8 = acc.Allocate1D<global::ILGPU.Float8E4M3>(100)) if (f8.LengthInBytes != 100) { rfails++; Console.WriteLine($"    Float8E4M3: {f8.LengthInBytes} != 100"); }
            Console.WriteLine($"    whole-byte types unchanged: {(rfails == 0 ? "OK" : rfails + " FAIL")}");
            fails += rfails;

            // BitsPerElement static sanity.
            if (ArrayView<QInt4>.BitsPerElement != 4) { Console.WriteLine($"    ArrayView<QInt4>.BitsPerElement={ArrayView<QInt4>.BitsPerElement} != 4  FAIL"); fails++; }
            if (ArrayView<byte>.BitsPerElement != 8) { Console.WriteLine($"    ArrayView<byte>.BitsPerElement != 8  FAIL"); fails++; }
            if (ArrayView<int>.BitsPerElement != 32) { Console.WriteLine($"    ArrayView<int>.BitsPerElement != 32  FAIL"); fails++; }
        }

        Console.WriteLine(fails == 0
            ? "=== PACKED ALLOC PASS (QInt4 = 2/byte, whole-byte types unchanged) ==="
            : $"=== PACKED ALLOC FAIL: {fails} problems ===");
        return Task.FromResult(fails == 0 ? 0 : 1);
    }
}
