using System;
using System.Linq;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;

/// <summary>
/// Validates the packed 4-bit QInt4 LOAD path end-to-end on the desktop backends (CPU/CUDA/OpenCL):
/// upload packed bytes (2 nibbles/byte) to an ArrayView&lt;QInt4&gt;, run a kernel y[i] = x[i] (the
/// implicit QInt4-&gt;int sign-extend over the packed nibble load), read back int and compare to the
/// expected sign-extended values across every nibble -8..7 and both even/odd positions.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- packed-int4-verify
/// </summary>
internal static class PackedQInt4Verify
{
    // Kernel: read a packed QInt4 element, sign-extend to int, store. Exercises the packed nibble
    // LOAD + the QInt4->int ConvertValue (identity over the i32 register the load produced).
    private static void LoadKernel(Index1D i, ArrayView<QInt4> x, ArrayView<int> y) => y[i] = x[i];

    public static Task<int> Run()
    {
        Console.WriteLine("=== Packed QInt4 nibble LOAD verification (CPU/CUDA/OpenCL) ===");

        // 32 elements: two full sweeps of -8..7 so every nibble value appears at both an even
        // (low-nibble) and an odd (high-nibble) byte position.
        int n = 32;
        int[] expected = new int[n];
        for (int i = 0; i < n; i++)
            expected[i] = (i % 16) - 8;             // -8..7, repeated
        // Pack 2 elements per byte: low nibble = element 2k, high nibble = element 2k+1.
        byte[] packed = new byte[(n + 1) / 2];
        for (int k = 0; k < packed.Length; k++)
        {
            int lo = expected[2 * k] & 0xF;
            int hi = expected[2 * k + 1] & 0xF;
            packed[k] = (byte)(lo | (hi << 4));
        }

        int totalFails = 0;
        using var ctx = Context.Create(b => b.Default().EnableAlgorithms());
        foreach (var dev in ctx)
        {
            var type = dev.AcceleratorType;
            if (type != AcceleratorType.CPU && type != AcceleratorType.Cuda && type != AcceleratorType.OpenCL)
                continue;

            // WIRED backends (packed QInt4 nibble load implemented + asserted). The CPU (IL) backend
            // runs the managed ArrayView<QInt4> indexer directly, which decodes the packed nibble by
            // value (ArrayView.LoadPackedElement). The separate Velocity SIMD accelerator
            // (AcceleratorType.Velocity, not exercised here) is a tracked follow-on.
            bool wired = type == AcceleratorType.Cuda || type == AcceleratorType.OpenCL
                || type == AcceleratorType.CPU;
            if (!wired)
            {
                Console.WriteLine($"  [{type}] PENDING - packed nibble load not yet wired (tracked)");
                continue;
            }

            using var acc = dev.CreateAccelerator(ctx);
            try
            {
                using var xBuf = acc.Allocate1D<QInt4>(n);
                using var yBuf = acc.Allocate1D<int>(n);

                // Raw packed-byte upload: AsRawArrayView() reports the packed LengthInBytes = ceil(n/2).
                var raw = ((IContiguousArrayView)xBuf.View.BaseView).AsRawArrayView();
                if (raw.Length != packed.Length)
                {
                    Console.WriteLine($"  [{type}] raw byte view length {raw.Length} != packed {packed.Length}  FAIL");
                    totalFails++;
                    continue;
                }
                raw.CopyFromCPU(packed);

                var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<QInt4>, ArrayView<int>>(LoadKernel);
                kernel((int)n, xBuf.View, yBuf.View);
                acc.Synchronize();

                int[] got = yBuf.GetAsArray1D();
                int fails = 0;
                for (int i = 0; i < n; i++)
                    if (got[i] != expected[i]) fails++;
                if (fails == 0)
                    Console.WriteLine($"  [{type}] LOAD PASS ({n}/{n} sign-extended nibbles correct)");
                else
                {
                    var firstBad = Enumerable.Range(0, n).First(i => got[i] != expected[i]);
                    Console.WriteLine($"  [{type}] LOAD FAIL ({fails}/{n}); first bad i={firstBad} got={got[firstBad]} want={expected[firstBad]}");
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
            ? "=== PACKED QInt4 LOAD PASS (wired: CPU + OpenCL + CUDA) ==="
            : $"=== PACKED QInt4 LOAD: {totalFails} problems ===");
        return Task.FromResult(totalFails == 0 ? 0 : 1);
    }
}
