using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Stores into a function-scope array (LocalMemory) inside a loop with a runtime trip count, at an index that is one
    // of two constants chosen by the loop variable, one of them 0 (`subset == 0 ? 0 : 8`). On an NVIDIA RTX 4070 under
    // Chrome / Edge (Dawn D3D12 + DXC, driver 32.0.15.9621) the driver's compile of the (correct) DXIL wrote the other
    // constant's element: subset 0's values landed at 8.. and subset 1's at 0... SwiftShader and FXC are correct.
    // WebGPU now adds a zero read from _scalar_params to such indices, which no compiler can fold
    // (WGSLCodeGenerator.FunctionArrayIndex). Found through SpawnDev.Codecs (Geordi 2026-10-27): the BC7 encoder's
    // candidate copy (`cand[lowAt + c] = res[c]`, lowAt = subset == 0 ? 0 : 8). Every backend, against a CPU oracle.
    public abstract partial class BackendTestBase
    {
        static void FunctionArray_SelectStoreKernel(Index1D i, ArrayView<int> counts, ArrayView<int> o)
        {
            var cand = LocalMemory.Allocate<int>(20);
            for (int k = 0; k < 20; k++) cand[k] = 0;
            int subsets = counts[i];
            for (int subset = 0; subset < subsets; subset++)
            {
                int lowAt = subset == 0 ? 0 : 8;
                int highAt = subset == 0 ? 4 : 12;
                for (int c = 0; c < 4; c++)
                {
                    cand[lowAt + c] = 100 + subset * 10 + c;
                    cand[highAt + c] = 200 + subset * 10 + c;
                }
                cand[16 + subset] = 300 + subset;
            }
            for (int k = 0; k < 20; k++) o[i * 20 + k] = cand[k];
        }

        [TestMethod]
        public async Task FunctionArrayStore_SelectIndexInLoop_MatchesOracle() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var counts = new int[n];
            for (int i = 0; i < n; i++) counts[i] = i % 3;   // 0, 1 or 2 subsets
            using var dCounts = accelerator.Allocate1D(counts);
            using var dO = accelerator.Allocate1D<int>(n * 20);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(FunctionArray_SelectStoreKernel)(
                (Index1D)n, dCounts.View, dO.View);
            await accelerator.SynchronizeAsync();
            var got = await dO.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                var want = new int[20];
                for (int subset = 0; subset < counts[i]; subset++)
                {
                    int lowAt = subset == 0 ? 0 : 8, highAt = subset == 0 ? 4 : 12;
                    for (int c = 0; c < 4; c++) { want[lowAt + c] = 100 + subset * 10 + c; want[highAt + c] = 200 + subset * 10 + c; }
                    want[16 + subset] = 300 + subset;
                }
                for (int k = 0; k < 20; k++)
                    if (got[i * 20 + k] != want[k])
                        throw new Exception($"thread {i} ({counts[i]} subsets): cand[{k}] = {got[i * 20 + k]}, expected {want[k]}");
            }
        });
    }
}
