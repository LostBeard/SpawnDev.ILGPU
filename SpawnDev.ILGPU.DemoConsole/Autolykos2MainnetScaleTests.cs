using System;
using System.Linq;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Crypto;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// Tier 2 (production-scale) Autolykos2 correctness test, per the plan file
/// (D:\users\tj\Projects\_Brainstorm_2026_09_21). Deliberately NOT part of the shared
/// BackendTestBase partial (BackendTestBase.Autolykos2.cs, which stays small-N for fast
/// cross-backend iteration) - a full PlaywrightMultiTest sweep runs every class in
/// Program.cs's SetTestTypes concurrently across lanes, and this test's real ~2GB dataset
/// would blow out memory pressure across those lanes the same way HeavyModel-class tests are
/// excluded by convention elsewhere in this repo. Run scoped, deliberately, not swept:
///   PMT_FILTER=Autolykos2Mainnet dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj
/// </summary>
public class Autolykos2MainnetScaleTests : BackendTestBase
{
    public Autolykos2MainnetScaleTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
    protected override string BackendName => "CUDA (Autolykos2 mainnet-scale)";

    protected override Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
    {
        var context = Context.Create(builder => builder.AllAccelerators().EnableAlgorithms());
        var cudaDevices = context.GetCudaDevices();
        if (cudaDevices.Count == 0)
        {
            context.Dispose();
            throw new UnsupportedTestException("No CUDA devices found - this test is CUDA-only (see class remarks)");
        }
        var accelerator = cudaDevices[0].CreateAccelerator(context);
        return Task.FromResult<(Context, Accelerator)>((context, accelerator));
    }

    [TestMethod]
    public async Task Autolykos2Mainnet_DatasetGeneration_And_Mine() => await RunTest(async accelerator =>
    {
        const long n = Autolykos2.InitialN; // 2^26, ~2GB - real protocol constant, not toy scale
        const uint height = 1_500_000;

        // Force all 4 chunk slots even though this N fits one CUDA buffer easily (CUDA has no
        // OpenCL-style max-single-alloc cap queried by this codebase) - this is the only way to
        // exercise the chunk-boundary arithmetic (GetChunked/SetChunked in Autolykos2.cs) at real
        // scale rather than only via the toy-N correctness tests or an informal benchmark run.
        uint elementsPerChunk = (uint)(n / Autolykos2.ChunkCount);
        long lastChunkSize = n - 3L * elementsPerChunk;

        using var chunk0 = accelerator.Allocate1D<Element256>(elementsPerChunk);
        using var chunk1 = accelerator.Allocate1D<Element256>(elementsPerChunk);
        using var chunk2 = accelerator.Allocate1D<Element256>(elementsPerChunk);
        using var chunk3 = accelerator.Allocate1D<Element256>(lastChunkSize);

        var genKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, uint, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, uint>(
            Autolykos2.GenerateDatasetKernel);
        genKernel((int)n, elementsPerChunk, chunk0.View, chunk1.View, chunk2.View, chunk3.View, height);
        await accelerator.SynchronizeAsync();

        // One host copy of the whole dataset, used for both checks below. Needed anyway for the
        // mining check (a nonce's derived indices are effectively uniform-random across all of N,
        // so there's no sampling shortcut for that CPU reference the way there is for dataset
        // generation below).
        var hostDataset = new Element256[n];
        (await chunk0.CopyToHostAsync()).CopyTo(hostDataset, 0);
        (await chunk1.CopyToHostAsync()).CopyTo(hostDataset, elementsPerChunk);
        (await chunk2.CopyToHostAsync()).CopyTo(hostDataset, 2 * elementsPerChunk);
        (await chunk3.CopyToHostAsync()).CopyTo(hostDataset, 3 * elementsPerChunk);

        // Boundary-focused correctness sample: exhaustively checking all 67M elements would cost
        // roughly the ~130s of CPU-side Blake2b the CPU benchmark run took to build the whole
        // reference dataset (see crypto-mining-funding-analysis.md) just to check it once. Every
        // chunk boundary is exactly where an off-by-one in the chunk-selection arithmetic would
        // show up, so that's where the check budget is spent, not a shortcut around real coverage.
        long[] sampleIndices =
        {
            0, 1,
            elementsPerChunk - 1, elementsPerChunk, elementsPerChunk + 1,
            2 * elementsPerChunk - 1, 2 * elementsPerChunk, 2 * elementsPerChunk + 1,
            3 * elementsPerChunk - 1, 3 * elementsPerChunk, 3 * elementsPerChunk + 1,
            n / 2,
            n - 2, n - 1,
        };
        foreach (long i in sampleIndices)
        {
            var gpu = hostDataset[i];
            Autolykos2.GenerateDatasetElement((uint)i, height, out ulong e0, out ulong e1, out ulong e2, out ulong e3);
            if (gpu.W0 != e0 || gpu.W1 != e1 || gpu.W2 != e2 || gpu.W3 != e3)
                throw new Exception($"Autolykos2 mainnet-scale dataset element {i} (chunk {i / elementsPerChunk}) mismatch: " +
                    $"GPU={gpu.W0:x16}{gpu.W1:x16}{gpu.W2:x16}{gpu.W3:x16} CPU={e0:x16}{e1:x16}{e2:x16}{e3:x16}");
        }
        Console.WriteLine($"[Autolykos2Mainnet] Dataset generation at N={n:N0} ({n * 32.0 / (1024 * 1024 * 1024):F2} GB, " +
            $"{Autolykos2.ChunkCount} chunks): {sampleIndices.Length} boundary-sampled elements match CPU reference \u2713");

        // Mining correctness at real scale: same deterministic-unique-hit construction as the
        // small-N test (target = the tested range's own minimum digest, one increment above -
        // nothing else in the range can be smaller than the range's own minimum, by definition).
        const int nonceRange = 2000;
        const ulong nonceBase = 0;
        var header = new Element256(0x0102030405060708UL, 0x1112131415161718UL, 0x2122232425262728UL, 0x3132333435363738UL);

        ulong minD0 = ulong.MaxValue, minD1 = 0, minD2 = 0, minD3 = 0, minNonce = 0;
        for (ulong nonce = 0; nonce < nonceRange; nonce++)
        {
            Autolykos2.ComputeNonceDigest(hostDataset, (uint)n, nonce, header.W0, header.W1, header.W2, header.W3,
                out ulong d0, out ulong d1, out ulong d2, out ulong d3);
            bool smaller = d0 < minD0 || (d0 == minD0 && (d1 < minD1 || (d1 == minD1 &&
                (d2 < minD2 || (d2 == minD2 && d3 < minD3)))));
            if (nonce == 0 || smaller) { minNonce = nonce; minD0 = d0; minD1 = d1; minD2 = d2; minD3 = d3; }
        }
        var target = new Element256(minD0, minD1, minD2, minD3 + 1);

        using var winningNonces = accelerator.Allocate1D<ulong>(8);
        using var winningCount = accelerator.Allocate1D<int>(1);
        winningCount.MemSetToZero();
        var mineKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, uint, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, uint,
            ulong, Element256, Element256, ArrayView<ulong>, ArrayView<int>>(Autolykos2.MineKernel);
        mineKernel(nonceRange, elementsPerChunk, chunk0.View, chunk1.View, chunk2.View, chunk3.View,
            (uint)n, nonceBase, header, target, winningNonces.View, winningCount.View);
        await accelerator.SynchronizeAsync();

        int gpuCount = (await winningCount.CopyToHostAsync())[0];
        var gpuNonces = await winningNonces.CopyToHostAsync();
        if (gpuCount != 1 || gpuNonces[0] != minNonce)
            throw new Exception(
                $"Autolykos2 mainnet-scale mining mismatch: expected exactly nonce {minNonce}, " +
                $"GPU reported {gpuCount} hit(s): [{string.Join(",", gpuNonces.Take(Math.Max(gpuCount, 0)))}]");

        Console.WriteLine($"[Autolykos2Mainnet] Mining at N={n:N0}: nonce {minNonce} (of {nonceRange} tested) correctly identified as the unique hit \u2713");
    });
}
