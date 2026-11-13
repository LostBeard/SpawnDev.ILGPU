using System.Diagnostics;
using System.Globalization;
using ILGPU;
using ILGPU.Backends.OpenCL;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;

// What an integer operation costs in a kernel's inner loop, per backend: the same loop with one operation swapped.
// `int-op-cost [cuda|opencl] [threads] [iterations]`. Each thread runs `iterations` steps of a small generator and
// folds op(x) into a sum; the time difference between two variants is the cost of the operation (the generator and
// the loop are the same in all of them). Uses the GPU. Prints ms per launch (median of 7) and the checksum, which
// must be equal for variants that compute the same values (div16 / shr4, div2 / shr1 on non-negative x).
//
// Why (2026-11-13): ILGPU's PTX leaves "x / 16" as div.s32 with an immediate; NVIDIA's OpenCL compiler emits shifts
// for the same source. Does the assembler behind PTX reduce it, or is every such division paid for?
static class IntOpCostProbe
{
    static void Div16(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x / 16; }
        sum[i] = acc;
    }

    static void Shr4(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x >> 4; }
        sum[i] = acc;
    }

    static void Div2(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x / 2; }
        sum[i] = acc;
    }

    static void Shr1(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x >> 1; }
        sum[i] = acc;
    }

    // a divisor that is not a power of two: a multiply-high sequence when reduced
    static void Div7(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x / 7; }
        sum[i] = acc;
    }

    // a divisor only known at run time (the quantizer's case): never reducible
    static void DivVar(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0, d = (seed[i] & 7) + 9;
        for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x / d; }
        sum[i] = acc;
    }

    // the loop alone
    static void None(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = (x * 1664525 + 1013904223) & 0x7FFFFFFF; acc += x; }
        sum[i] = acc;
    }

    // a signed division of a value that can be negative (the general case the compiler must handle)
    static void Div16Signed(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = x * 1664525 + 1013904223; acc += x / 16; }
        sum[i] = acc;
    }

    static void Shr4SignedFix(Index1D i, ArrayView<int> seed, ArrayView<int> sum, int iterations)
    {
        int x = seed[i], acc = 0;
        for (int k = 0; k < iterations; k++) { x = x * 1664525 + 1013904223; acc += (x + ((x >> 31) & 15)) >> 4; }
        sum[i] = acc;
    }

    public static Task<int> Run(string[] args)
    {
        string backend = args.Length > 1 ? args[1] : "cuda";
        int threads = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 1 << 20;
        int iterations = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 2000;
        using var context = backend == "opencl" ? Context.Create(b => b.OpenCL()) : Context.Create(b => b.Cuda());
        using Accelerator acc = backend == "opencl" ? context.CreateCLAccelerator(0) : context.CreateCudaAccelerator(0);
        using var seed = acc.Allocate1D<int>(threads);
        using var sum = acc.Allocate1D<int>(threads);
        var seeds = new int[threads];
        for (int i = 0; i < threads; i++) seeds[i] = i * 7919 + 12345;
        seed.CopyFromCPU(seeds);

        var variants = new (string name, Action<Index1D, ArrayView<int>, ArrayView<int>, int> kernel)[]
        {
            ("loop only", None), ("x / 16", Div16), ("x >> 4", Shr4), ("x / 2", Div2), ("x >> 1", Shr1), ("x / 7", Div7),
            ("x / d (run time)", DivVar), ("signed x / 16", Div16Signed), ("signed shift form", Shr4SignedFix),
        };
        Console.WriteLine($"int-op-cost {acc.AcceleratorType} {acc.Name}: {threads} threads x {iterations} iterations, ms per launch (median of 7)");
        foreach (var (name, kernel) in variants)
        {
            var launch = acc.LoadAutoGroupedStreamKernel(kernel);
            launch(threads, seed.View, sum.View, iterations);
            acc.Synchronize();
            var ms = new double[7];
            for (int r = 0; r < ms.Length; r++)
            {
                var sw = Stopwatch.StartNew();
                launch(threads, seed.View, sum.View, iterations);
                acc.Synchronize();
                ms[r] = sw.Elapsed.TotalMilliseconds;
            }
            Array.Sort(ms);
            int[] result = sum.GetAsArray1D();
            long checksum = 0;
            foreach (int v in result) checksum += v;
            var compiled = acc.CompileKernel(global::ILGPU.Backends.EntryPoints.EntryPointDescription.FromImplicitlyGroupedKernel(kernel.Method));
            string text = compiled is PTXCompiledKernel ptx ? ptx.PTXAssembly : compiled is CLCompiledKernel cl ? cl.Source : "";
            if (args.Length > 4)
            {
                Directory.CreateDirectory(args[4]);
                string safe = name.Replace(" / ", "_div_").Replace(" >> ", "_shr_").Replace(' ', '_').Replace("(", "").Replace(")", "");
                File.WriteAllText(Path.Combine(args[4], $"{acc.AcceleratorType}_{safe}.txt"), text);
            }
            int divs = 0;
            foreach (string line in text.Split('\n')) if (line.Contains("div.") || line.Contains(" / ")) divs++;
            Console.WriteLine($"  {name,-20} {ms[3].ToString("F2", CultureInfo.InvariantCulture),8} ms   checksum {checksum,22}   division lines in the generated code: {divs}");
        }
        return Task.FromResult(0);
    }
}
