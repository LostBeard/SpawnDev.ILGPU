using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Group.BarrierPopCount / BarrierAnd / BarrierOr (IR PredicateBarrier). Added 2026-10-13: nothing in the suite
    // called them, and the browser code generators did not implement them - Wasm emitted nothing, WebGPU only a
    // workgroupBarrier() (the count / AND / OR result was never computed), WebGL a comment - while the docs listed
    // them as working on WebGPU and Wasm.
    public abstract partial class BackendTestBase
    {
        private const int PredicateBarrierGroupSize = 64;
        private const int PredicateBarrierGroups = 4;

        // Group 0: every thread true. Group 1: every thread false. Group 2: every third thread true.
        // Group 3: only the last thread true. The second barrier uses a different predicate (lane < 5), so a
        // counter that is not reset between barriers shows up as a wrong second count.
        private static bool PredicateBarrierPredicate(int group, int lane) => group switch
        {
            0 => true,
            1 => false,
            2 => lane % 3 == 0,
            _ => lane == PredicateBarrierGroupSize - 1,
        };

        private static void PredicateBarrierKernel(
            ArrayView<int> pop, ArrayView<int> all, ArrayView<int> any, ArrayView<int> pop2)
        {
            int group = Grid.IdxX;
            int lane = Group.IdxX;
            int gid = Grid.GlobalIndex.X;
            bool p = group switch
            {
                0 => true,
                1 => false,
                2 => lane % 3 == 0,
                _ => lane == Group.DimX - 1,
            };
            pop[gid] = Group.BarrierPopCount(p);
            all[gid] = Group.BarrierAnd(p) ? 1 : 0;
            any[gid] = Group.BarrierOr(p) ? 1 : 0;
            pop2[gid] = Group.BarrierPopCount(lane < 5);
        }

        [TestMethod]
        public async Task PredicateBarrier_PopCountAndOr_MatchesReferenceTest() => await RunTest(async accelerator =>
        {
            const int n = PredicateBarrierGroupSize * PredicateBarrierGroups;
            using var pop = accelerator.Allocate1D<int>(n);
            using var all = accelerator.Allocate1D<int>(n);
            using var any = accelerator.Allocate1D<int>(n);
            using var pop2 = accelerator.Allocate1D<int>(n);
            int[] gotPop, gotAll, gotAny, gotPop2;
            try
            {
                var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                    PredicateBarrierKernel);
                kernel(new KernelConfig(PredicateBarrierGroups, PredicateBarrierGroupSize), pop.View, all.View, any.View, pop2.View);
                // WebGL compiles at first dispatch / readback, so the readbacks are inside the try too.
                gotPop = await pop.CopyToHostAsync<int>();
                gotAll = await all.CopyToHostAsync<int>();
                gotAny = await any.CopyToHostAsync<int>();
                gotPop2 = await pop2.CopyToHostAsync<int>();
            }
            catch (Exception ex) when (accelerator.AcceleratorType == AcceleratorType.WebGL)
            {
                // WebGL has no barriers or shared memory: it must refuse the kernel, not return garbage - and the
                // caller must be able to catch the documented type itself, not a wrapper around it.
                if (ex is UnsupportedKernelFeatureException)
                    throw new UnsupportedTestException("WebGL rejects predicate barriers (UnsupportedKernelFeatureException), as it must");
                var chain = new System.Text.StringBuilder();
                for (Exception? e = ex; e != null; e = e.InnerException) chain.Append(e.GetType().FullName).Append(" > ");
                throw new Exception($"WebGL must throw UnsupportedKernelFeatureException itself; got {chain}");
            }
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new Exception("WebGL accepted a kernel with a predicate barrier; it must throw UnsupportedKernelFeatureException");

            for (int g = 0; g < PredicateBarrierGroups; g++)
            {
                int count = 0;
                for (int l = 0; l < PredicateBarrierGroupSize; l++)
                    if (PredicateBarrierPredicate(g, l)) count++;
                int expAll = count == PredicateBarrierGroupSize ? 1 : 0;
                int expAny = count > 0 ? 1 : 0;
                for (int l = 0; l < PredicateBarrierGroupSize; l++)
                {
                    int i = g * PredicateBarrierGroupSize + l;
                    if (gotPop[i] != count)
                        throw new Exception($"BarrierPopCount group {g} lane {l}: got {gotPop[i]}, expected {count}");
                    if (gotAll[i] != expAll)
                        throw new Exception($"BarrierAnd group {g} lane {l}: got {gotAll[i]}, expected {expAll}");
                    if (gotAny[i] != expAny)
                        throw new Exception($"BarrierOr group {g} lane {l}: got {gotAny[i]}, expected {expAny}");
                    if (gotPop2[i] != 5)
                        throw new Exception($"second BarrierPopCount group {g} lane {l}: got {gotPop2[i]}, expected 5 (state carried over from the first barrier?)");
                }
            }
        });
    }
}
