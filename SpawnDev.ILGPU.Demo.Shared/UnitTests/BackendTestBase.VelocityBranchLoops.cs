using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // `if (c) { loop } else { loop }`: both loops exit to the merge block. Velocity (the default desktop CPU backend)
    // jumped from a loop that no lane entered straight to that exit, over the else arm, so with c false NOTHING the kernel
    // stored landed (silent; found through SpawnDev.Codecs VorbisResidueAccumulateGpu, Geordi 2026-10-26). Every backend,
    // against a CPU oracle (WebGL refuses the loops: one element per store site): each arm taken by all lanes, and the
    // arms split between lanes; with and without an outer loop.
    public abstract partial class BackendTestBase
    {
        static void BranchLoops_Kernel(Index1D idx, ArrayView<int> t, ArrayView<int> mode, int n, int m)
        {
            for (int i = 0; i < n; i++)
            {
                if (mode[idx] == 1) { for (int d = 0; d < m; d++) t[idx * 16 + i * 4 + d] += 100 + d; }
                else { for (int d = 0; d < m; d++) t[idx * 16 + i * 4 + d] += 1 + d; }
            }
        }

        static void BranchLoops_NoOuterKernel(Index1D idx, ArrayView<int> t, ArrayView<int> mode, int n, int m)
        {
            if (mode[idx] == 1) { for (int d = 0; d < m; d++) t[idx * 16 + d] += 100 + d; }
            else { for (int d = 0; d < m; d++) t[idx * 16 + d] += 1 + d; }
        }

        [TestMethod]
        public async Task BranchLoops_LoopInEachArm_EveryArmStoresOrIsRefusedOnWebGL() => await RunEmulatedTest(async accelerator =>
        {
            const int threads = 40, n = 4, m = 4;
            var kernels = new (string Name, Action<Index1D, ArrayView<int>, ArrayView<int>, int, int> Kernel, int Iterations)[]
            {
                ("outer loop", accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int, int>(BranchLoops_Kernel), n),
                ("no outer loop", accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int, int>(BranchLoops_NoOuterKernel), 1),
            };
            foreach (var (name, kernel, iterations) in kernels)
                foreach (var pattern in new[] { "all else", "all then", "split" })
                {
                    var mode = new int[threads];
                    for (int i = 0; i < threads; i++)
                        mode[i] = pattern == "all then" ? 1 : pattern == "split" ? i % 3 == 0 ? 1 : 0 : 0;
                    var initial = new int[threads * 16];
                    for (int i = 0; i < initial.Length; i++) initial[i] = 1000 + i;
                    using var dT = accelerator.Allocate1D(initial);
                    using var dMode = accelerator.Allocate1D(mode);
                    try
                    {
                        kernel((Index1D)threads, dT.View, dMode.View, n, m);
                        await accelerator.SynchronizeAsync();
                    }
                    catch (Exception ex) when (accelerator is SpawnDev.ILGPU.WebGL.WebGLAccelerator)
                    {
                        // WebGL captures one element per store site per thread: these loops are refused, never wrong.
                        bool refused = false;
                        for (var e = ex; e != null; e = e.InnerException)
                            if (e.Message.Contains("wrote two different elements")) refused = true;
                        if (!refused) throw;
                        continue;
                    }
                    var got = await dT.CopyToHostAsync<int>();
                    var want = (int[])initial.Clone();
                    for (int t = 0; t < threads; t++)
                        for (int i = 0; i < iterations; i++)
                            for (int d = 0; d < m; d++)
                                want[t * 16 + i * 4 + d] += mode[t] == 1 ? 100 + d : 1 + d;
                    for (int i = 0; i < want.Length; i++)
                        if (got[i] != want[i])
                            throw new Exception($"{name}, {pattern}: t[{i}] = {got[i]}, expected {want[i]}");
                }
        });
    }
}
