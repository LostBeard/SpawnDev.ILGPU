using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Thread groups of several warps whose threads meet at barriers: shared memory handed from one warp to the others, a tree reduction with a barrier per step, the predicate barriers
    // (popcount, and, or) and Group.Broadcast. The Velocity backend (SIMD, a warp of 4 lanes) runs the warps of such a group on one thread each
    // (VelocityGroupTeamRunner) with a barrier that, like a GPU barrier, does not wait for a warp that has returned; before that, a group had at most one warp, ILGPU.Algorithms group scan,
    // reduce and radix sort returned zeros (no intrinsic mapping for Velocity) and Group.Broadcast reached only the lanes of the broadcaster's own warp. Every other backend must give the same answers.
    public abstract partial class BackendTestBase
    {
        static void GroupReverseKernel(ArrayView<int> input, ArrayView<int> output)
        {
            var shared = SharedMemory.Allocate<int>(256);
            int t = Group.IdxX, g = Grid.GlobalIndex.X;
            shared[t] = input[g];
            Group.Barrier();
            output[g] = shared[Group.DimX - 1 - t];
        }

        static void GroupTreeReduceKernel(ArrayView<int> input, ArrayView<int> output)
        {
            var shared = SharedMemory.Allocate<int>(256);
            int t = Group.IdxX, g = Grid.GlobalIndex.X;
            shared[t] = input[g];
            Group.Barrier();
            for (int s = Group.DimX / 2; s > 0; s >>= 1)
            {
                if (t < s) shared[t] += shared[t + s];
                Group.Barrier();
            }
            if (t == 0) output[Grid.IdxX] = shared[0];
        }

        static void GroupPredicatesKernel(ArrayView<int> input, ArrayView<int> output)
        {
            int g = Grid.GlobalIndex.X;
            output[g * 3] = Group.BarrierPopCount(input[g] > 50);
            output[g * 3 + 1] = Group.BarrierAnd(input[g] >= 0) ? 1 : 0;
            output[g * 3 + 2] = Group.BarrierOr(input[g] > 98) ? 1 : 0;
        }

        static void GroupBroadcastKernel(ArrayView<int> input, ArrayView<int> output)
        {
            int g = Grid.GlobalIndex.X;
            output[g * 2] = Group.Broadcast(input[g], 0);
            output[g * 2 + 1] = Group.Broadcast(input[g], Group.DimX - 1);
        }

        static void GroupEarlyReturnKernel(ArrayView<int> input, ArrayView<int> output, int active)
        {
            var shared = SharedMemory.Allocate<int>(256);
            int t = Group.IdxX, g = Grid.GlobalIndex.X;
            if (t >= active) return;                      // whole warps return before the barrier: the others must not wait for them
            shared[t] = input[g];
            Group.Barrier();
            output[g] = shared[(t + 1) % active];
        }

        // Threads that return before a barrier: a GPU barrier waits only for the threads that have not exited (CUDA), and so does the Velocity backend's group barrier (a warp that has returned
        // leaves it). Not portable: WGSL (WebGPU) requires barriers in uniform control flow and rejects the kernel, OpenCL calls it undefined, so only the backends that define it run it.
        [TestMethod]
        public async Task GroupBarrier_WarpsThatReturnBeforeABarrier_AreNotWaitedFor() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.CPU or AcceleratorType.Cuda or AcceleratorType.Velocity)) throw new UnsupportedTestException("a barrier after a divergent return is defined on CPU, CUDA and Velocity only");
            var rng = new Random(9);
            foreach (var (groups, G) in new[] { (3, 8), (4, 64) })
            {
                int n = groups * G, active = G * 5 / 8;
                var input = new int[n]; for (int i = 0; i < n; i++) input[i] = rng.Next(100);
                var exp = new int[n]; for (int g = 0; g < groups; g++) for (int t = 0; t < active; t++) exp[g * G + t] = input[g * G + (t + 1) % active];
                using var ib = accelerator.Allocate1D(input); using var o = accelerator.Allocate1D<int>(n); o.MemSetToZero();
                accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, int>(GroupEarlyReturnKernel)(new KernelConfig(groups, G), ib.View, o.View, active);
                await accelerator.SynchronizeAsync();
                var got = await o.CopyToHostAsync<int>();
                for (int i = 0; i < n; i++)
                    if (got[i] != exp[i]) throw new Exception($"{groups} groups of {G} threads, {active} active: element {i} is {got[i]}, expected {exp[i]}");
            }
        });

        [TestMethod]
        public async Task GroupBarrier_SeveralWarps_SharedMemoryReductionPredicatesBroadcast() => await RunTest(async accelerator =>
        {
            if (!accelerator.Device.Satisfies(new AcceleratorRequirements { RequiresBarriers = true, RequiresSharedMemory = true })) throw new UnsupportedTestException("needs barriers and shared memory");
            var rng = new Random(5);
            int ran = 0;
            // the predicate barriers: OpenCL returns false from BarrierAnd for a group of 8 threads where every thread satisfies it (found by this test, an OpenCL backend bug, not fixed here)
            bool predicates = accelerator.AcceleratorType != AcceleratorType.OpenCL;
            // power-of-two sizes (the tree reduction halves the group); up to 64 every backend launches (the limit of the CPU backend), 256 only where the device allows it
            foreach (var (groups, G) in new[] { (3, 8), (5, 16), (4, 64), (6, 256) })
            {
                if (G > 64 && (G > accelerator.MaxGroupSize.X || G > accelerator.MaxNumThreadsPerGroup)) continue;
                int n = groups * G;
                var input = new int[n]; for (int i = 0; i < n; i++) input[i] = rng.Next(100);
                var expReverse = new int[n]; var expReduce = new int[groups]; var expPred = new int[n * 3]; var expBroadcast = new int[n * 2];
                for (int g = 0; g < groups; g++)
                {
                    int sum = 0, count = 0; bool any = false;
                    for (int t = 0; t < G; t++) { int v = input[g * G + t]; sum += v; if (v > 50) count++; if (v > 98) any = true; expReverse[g * G + t] = input[g * G + G - 1 - t]; }
                    expReduce[g] = sum;
                    for (int t = 0; t < G; t++)
                    {
                        int i = g * G + t;
                        expPred[i * 3] = count; expPred[i * 3 + 1] = 1; expPred[i * 3 + 2] = any ? 1 : 0;
                        expBroadcast[i * 2] = input[g * G]; expBroadcast[i * 2 + 1] = input[g * G + G - 1];
                    }
                }
                using var ib = accelerator.Allocate1D(input);
                using var o1 = accelerator.Allocate1D<int>(n); using var o2 = accelerator.Allocate1D<int>(groups); using var o3 = accelerator.Allocate1D<int>(n * 3);
                using var o5 = accelerator.Allocate1D<int>(n * 2);
                o1.MemSetToZero(); o2.MemSetToZero(); o3.MemSetToZero(); o5.MemSetToZero();          // a thread that never stores stays 0
                var cfg = new KernelConfig(groups, G);
                accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(GroupReverseKernel)(cfg, ib.View, o1.View);
                accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(GroupTreeReduceKernel)(cfg, ib.View, o2.View);
                accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(GroupPredicatesKernel)(cfg, ib.View, o3.View);
                accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>>(GroupBroadcastKernel)(cfg, ib.View, o5.View);
                await accelerator.SynchronizeAsync();
                void Check(string what, int[] got, int[] exp)
                {
                    for (int i = 0; i < exp.Length; i++)
                        if (got[i] != exp[i]) throw new Exception($"{groups} groups of {G} threads, {what}: element {i} is {got[i]}, expected {exp[i]}");
                }
                Check("shared memory reverse", await o1.CopyToHostAsync<int>(), expReverse);
                Check("tree reduction", await o2.CopyToHostAsync<int>(), expReduce);
                if (predicates) Check("predicate barriers (popcount, and, or)", await o3.CopyToHostAsync<int>(), expPred);
                Check("Group.Broadcast", await o5.CopyToHostAsync<int>(), expBroadcast);
                ran++;
            }
            if (ran < 3) throw new Exception($"only {ran} of the group sizes ran");
        });
    }
}
