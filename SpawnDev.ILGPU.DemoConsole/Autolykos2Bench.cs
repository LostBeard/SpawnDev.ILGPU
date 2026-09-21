using System;
using System.Diagnostics;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using ILGPU.Runtime.CPU;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Crypto;

/// <summary>
/// Autolykos2 throughput benchmark: dataset generation time, then mining hashes/sec over a
/// fixed wall-clock window. This is the number that answers the brainstorm's open question -
/// "how close does SpawnDev.ILGPU's codegen get to hand-written native throughput on a real
/// memory-hard workload" - see D:\users\tj\Projects\_Brainstorm_2026_09_21\crypto-mining-funding-analysis.md.
///
/// MUST be run from a published Release build, never `dotnet run` or a Debug build - a prior
/// measurement in this repo found a 3.0x build-vs-publish gap (global AGENTS.md Rule 5a), and
/// this is exactly the kind of throughput claim that rule exists to protect:
///   dotnet publish -c Release SpawnDev.ILGPU.DemoConsole
///   D:\...\SpawnDev.ILGPU.DemoConsole\bin\Release\net10.0\publish\SpawnDev.ILGPU.DemoConsole.exe autolykos2-bench [options]
///
/// Options: --backend cuda|opencl|cpu (default: first available, preferring GPU)
///          --n &lt;count&gt;   dataset element count (default: Autolykos2.InitialN, 2^26, ~2GB -
///                        a real protocol constant, not a guess at today's live N; live N is
///                        higher due to the N-boost schedule since block 614,400 - pass a
///                        larger --n once you've looked up the current value if that matters)
///          --seconds &lt;n&gt; mining duration (default: 30)
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- autolykos2-bench   (for a quick sanity
///      check only - the actual number to trust must come from the published build per above)
/// </summary>
internal static class Autolykos2Bench
{
    public static async Task<int> Run(string[] args)
    {
        long n = Autolykos2.InitialN;
        string backend = "";
        int seconds = 30;

        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--n") n = long.Parse(args[i + 1]);
            else if (args[i] == "--backend") backend = args[i + 1].ToLowerInvariant();
            else if (args[i] == "--seconds") seconds = int.Parse(args[i + 1]);
        }

        using var context = Context.Create(b => b.AllAccelerators().EnableAlgorithms());
        Accelerator accelerator = backend switch
        {
            "cuda" => context.GetCudaDevice(0)!.CreateAccelerator(context),
            "opencl" => context.GetCLDevices()[0].CreateAccelerator(context),
            "cpu" => context.GetCPUDevice(0).CreateCPUAccelerator(context),
            _ => context.CreatePreferredAccelerator(new AcceleratorRequirements
            {
                RequiresInt64 = true,
                RequiresAtomics = true,
                RequiresScatterStores = true
            })
        };
        using (accelerator)
        {
            Console.WriteLine($"[autolykos2-bench] backend={accelerator.AcceleratorType} name={accelerator.Name}");
            Console.WriteLine($"[autolykos2-bench] N={n:N0} elements ({n * 32.0 / (1024 * 1024 * 1024):F2} GB)");

            using var dataset = accelerator.Allocate1D<Element256>(n);
            var genKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<Element256>, uint>(Autolykos2.GenerateDatasetKernel);

            const uint height = 1_500_000;
            var genSw = Stopwatch.StartNew();
            // n fits Index1D per Autolykos2.GenerateDatasetKernel's own remarks (MaxN < int.MaxValue).
            genKernel((int)n, dataset.View, height);
            await accelerator.SynchronizeAsync();
            genSw.Stop();
            Console.WriteLine($"[autolykos2-bench] dataset generation: {genSw.Elapsed.TotalSeconds:F2}s ({n / genSw.Elapsed.TotalSeconds:N0} elements/sec)");

            const ulong h0m = 0x0102030405060708UL, h1m = 0x1112131415161718UL,
                        h2m = 0x2122232425262728UL, h3m = 0x3132333435363738UL;
            // Target 0 everywhere: no nonce can ever satisfy "digest < 0", so the atomic/scatter
            // hit path is never taken - this measures steady-state mining throughput, the same
            // way real mining spends the overwhelming majority of its time not finding a block.
            const ulong target0 = 0, target1 = 0, target2 = 0, target3 = 0;

            using var winningNonces = accelerator.Allocate1D<ulong>(4);
            using var winningCount = accelerator.Allocate1D<int>(1);
            winningCount.MemSetToZero();

            var mineKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<Element256>, uint, ulong, ulong, ulong, ulong, ulong,
                ulong, ulong, ulong, ulong, ArrayView<ulong>, ArrayView<int>>(Autolykos2.MineKernel);

            const int batchSize = 4_000_000;
            ulong nonceBase = 0;
            long totalNonces = 0;
            var mineSw = Stopwatch.StartNew();
            while (mineSw.Elapsed.TotalSeconds < seconds)
            {
                mineKernel(batchSize, dataset.View, (uint)n, nonceBase, h0m, h1m, h2m, h3m,
                    target0, target1, target2, target3, winningNonces.View, winningCount.View);
                await accelerator.SynchronizeAsync();
                nonceBase += batchSize;
                totalNonces += batchSize;
            }
            mineSw.Stop();

            double hashesPerSec = totalNonces / mineSw.Elapsed.TotalSeconds;
            Console.WriteLine($"[autolykos2-bench] mined {totalNonces:N0} nonces in {mineSw.Elapsed.TotalSeconds:F2}s");
            Console.WriteLine($"[autolykos2-bench] RESULT: {hashesPerSec:N0} hashes/sec ({hashesPerSec / 1_000_000.0:F3} MH/s)");
        }

        return 0;
    }
}
