using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Global scan (accelerator.CreateScan) at the sizes a consumer actually reaches, against a CPU prefix sum.
    //
    // The existing global-scan tests stop at 8,000 elements. SpawnScene's GPU densify compacts 600K-1.6M splats with
    // an exclusive scan, and on WebGPU it was WRONG from ~70K elements (2026-10-05, SpawnScene trainer gate):
    // n=615,764 had 271,700 (2% ones) to 599,380 (97% ones) wrong prefixes, the first at a 256-element chunk boundary
    // inside a tile, values noisy run to run. 4,095 and 4,097 passed. Sparse AND dense inputs, exclusive AND
    // inclusive, because they failed at different places.
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task GlobalExclusiveScanAtScaleTest() => await RunTest(async accelerator =>
            await CheckGlobalScanAtScale(accelerator, ScanKind.Exclusive));

        [TestMethod]
        public async Task GlobalInclusiveScanAtScaleTest() => await RunTest(async accelerator =>
            await CheckGlobalScanAtScale(accelerator, ScanKind.Inclusive));

        static async Task CheckGlobalScanAtScale(Accelerator accelerator, ScanKind kind)
        {
            var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(kind);
            var rng = new Random(20261005);
            var failures = new List<string>();
            foreach (int n in new[] { 4_097, 70_000, 615_764, 1_600_003 })
            {
                foreach (double density in new[] { 0.02, 0.97 })
                {
                    var data = new int[n];
                    for (int i = 0; i < n; i++) data[i] = rng.NextDouble() < density ? 1 : 0;
                    using var input = accelerator.Allocate1D(data);
                    using var output = accelerator.Allocate1D<int>(n);
                    using var temp = accelerator.Allocate1D<int>(Math.Max(1L, accelerator.ComputeScanTempStorageSize<int>(n)));
                    scan(accelerator.DefaultStream, input.View, output.View, temp.View);
                    await accelerator.SynchronizeAsync();
                    var got = await output.CopyToHostAsync<int>();

                    int run = 0, wrong = 0, first = -1;
                    for (int i = 0; i < n; i++)
                    {
                        int expected = kind == ScanKind.Exclusive ? run : run + data[i];
                        if (got[i] != expected) { wrong++; if (first < 0) first = i; }
                        run += data[i];
                    }
                    if (wrong > 0)
                    {
                        int expectedFirst = data.Take(first).Sum() + (kind == ScanKind.Inclusive ? data[first] : 0);
                        failures.Add($"n={n:N0} {(density < 0.5 ? "sparse" : "dense")}: {wrong:N0} wrong, first at {first:N0} " +
                            $"(got {got[first]:N0}, expected {expectedFirst:N0})");
                    }
                }
            }
            if (failures.Count > 0)
                throw new Exception($"{kind} scan wrong: " + string.Join("; ", failures));
        }
    }
}
