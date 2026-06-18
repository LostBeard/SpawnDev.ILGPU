using System;
using System.Linq;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;

/// <summary>
/// Validates the packed 4-bit QInt4 STORE path on the desktop GPU backends (CUDA/OpenCL):
/// a kernel writes dst[i] = (QInt4)src[i] into an ArrayView&lt;QInt4&gt; (2 nibbles/byte), then the
/// raw packed bytes are read back and unpacked to confirm every nibble landed in the right byte and
/// position. N is large so that for every byte the two adjacent threads write its two nibbles
/// CONCURRENTLY - a non-atomic byte read-modify-write would race and clobber, so this exercises the
/// atomic-word-RMW requirement (a small-N test can false-pass a racy store).
///
/// The CPU (IL) backend runs the literal managed indexer, whose ref model cannot write a nibble in
/// place; packed in-kernel stores must fail loud there rather than silently lose writes, so this
/// harness asserts CPU THROWS (and treats a throw as the expected, correct behavior).
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- packed-qint4-store-verify
/// </summary>
internal static class PackedQInt4StoreVerify
{
    private static void StoreKernel(Index1D i, ArrayView<int> src, ArrayView<QInt4> dst) =>
        dst[i] = (QInt4)src[i];

    public static Task<int> Run()
    {
        Console.WriteLine("=== Packed QInt4 nibble STORE verification (CPU/CUDA/OpenCL) ===");

        // Large N to force concurrent adjacent-nibble writes into the same byte (race stress).
        int n = 8192;
        int[] src = new int[n];
        for (int i = 0; i < n; i++)
            src[i] = (i % 16) - 8;                  // -8..7, sign-extended round-trip
        byte[] expectedPacked = new byte[(n + 1) / 2];
        for (int k = 0; k < expectedPacked.Length; k++)
        {
            int lo = src[2 * k] & 0xF;
            int hi = src[2 * k + 1] & 0xF;
            expectedPacked[k] = (byte)(lo | (hi << 4));
        }

        int totalFails = 0;
        using var ctx = Context.Create(b => b.Default().EnableAlgorithms());
        foreach (var dev in ctx)
        {
            var type = dev.AcceleratorType;
            if (type != AcceleratorType.CPU && type != AcceleratorType.Cuda && type != AcceleratorType.OpenCL)
                continue;

            using var acc = dev.CreateAccelerator(ctx);

            // CPU (IL) backend: the literal managed ref indexer cannot address a nibble for a write.
            // The correct behavior is a loud failure (UnsupportedKernelFeatureException), not a silent
            // lost write. Accept either a compile/dispatch throw OR a detectable wrong result -> the
            // former is the intended contract.
            if (type == AcceleratorType.CPU)
            {
                bool threw = false;
                try
                {
                    using var xBufCpu = acc.Allocate1D<int>(src);
                    using var dBufCpu = acc.Allocate1D<QInt4>(n);
                    var kCpu = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<QInt4>>(StoreKernel);
                    kCpu((int)n, xBufCpu.View, dBufCpu.View);
                    acc.Synchronize();
                }
                catch (Exception ex)
                {
                    threw = true;
                    Console.WriteLine($"  [CPU] STORE fail-loud OK ({ex.GetType().Name})");
                }
                if (!threw)
                {
                    Console.WriteLine("  [CPU] STORE did NOT fail loud - packed in-kernel write must throw on CPU  FAIL");
                    totalFails++;
                }
                continue;
            }

            try
            {
                using var srcBuf = acc.Allocate1D<int>(src);
                using var dstBuf = acc.Allocate1D<QInt4>(n);
                // Pre-zero the packed buffer so a missed write is detectable.
                var rawZero = ((IContiguousArrayView)dstBuf.View.BaseView).AsRawArrayView();
                rawZero.CopyFromCPU(new byte[rawZero.Length]);

                var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<QInt4>>(StoreKernel);
                kernel((int)n, srcBuf.View, dstBuf.View);
                acc.Synchronize();

                var raw = ((IContiguousArrayView)dstBuf.View.BaseView).AsRawArrayView();
                byte[] got = new byte[raw.Length];
                raw.CopyToCPU(got);
                int fails = 0;
                for (int k = 0; k < expectedPacked.Length; k++)
                    if (got[k] != expectedPacked[k]) fails++;
                if (fails == 0)
                    Console.WriteLine($"  [{type}] STORE PASS ({expectedPacked.Length}/{expectedPacked.Length} packed bytes correct, N={n})");
                else
                {
                    var firstBad = Enumerable.Range(0, expectedPacked.Length).First(k => got[k] != expectedPacked[k]);
                    Console.WriteLine($"  [{type}] STORE FAIL ({fails}/{expectedPacked.Length}); first bad byte {firstBad} got=0x{got[firstBad]:X2} want=0x{expectedPacked[firstBad]:X2}");
                }
                totalFails += fails;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [{type}] EXCEPTION: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                totalFails++;
            }
        }

        Console.WriteLine(totalFails == 0
            ? "=== PACKED QInt4 STORE PASS (CUDA + OpenCL atomic-word-RMW; CPU fail-loud) ==="
            : $"=== PACKED QInt4 STORE: {totalFails} problems ===");
        return Task.FromResult(totalFails == 0 ? 0 : 1);
    }
}
