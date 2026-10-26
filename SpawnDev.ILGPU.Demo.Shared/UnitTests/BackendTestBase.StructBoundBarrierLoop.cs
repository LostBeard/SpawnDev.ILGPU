using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A barrier inside a loop whose bounds come from a STRUCT parameter.
    //
    // WebGPU bound a by-value struct parameter as `var<storage, read_write>`, and Tint's uniformity analysis treats every
    // load from read_write storage as non-uniform, so the loop counted as non-uniform control flow and the shader was
    // rejected: "'workgroupBarrier' must only be called from uniform control flow ... reading from 'v_N' may result in a
    // non-uniform value" (SpawnDev.Codecs Vp8/Vp9EncodeWavefrontGroupKernel, a wave-front loop from p.DiagAndRMin to
    // diagEnd with Group.Barrier between diagonals, 2026-10-26). Struct parameters are bound `read` now, like the packed
    // scalars. The kernel exchanges values through shared memory between the barriers, so a missing barrier is wrong too.
    public abstract partial class BackendTestBase
    {
        public struct StructBoundLoopParams
        {
            public int Start;
            public int End;
            public int Scale;
        }

        const int StructBoundLoopGroupSize = 64;

        static void StructBoundBarrierLoopKernel(ArrayView<int> data, ArrayView<int> output, StructBoundLoopParams p)
        {
            var shared = SharedMemory.Allocate<int>(StructBoundLoopGroupSize);
            int t = Group.IdxX;
            int g = Grid.GlobalIndex.X;
            int acc = 0;
            for (int d = p.Start; d < p.End; d++)
            {
                shared[t] = data[g] * d + p.Scale;
                Group.Barrier();
                acc += shared[(t + d) % StructBoundLoopGroupSize];
                Group.Barrier();
            }
            output[g] = acc;
        }

        [TestMethod]
        public async Task StructParam_BoundsBarrierLoop_ShaderIsUniform_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            const int groups = 3, n = groups * StructBoundLoopGroupSize;
            var data = new int[n];
            for (int i = 0; i < n; i++) data[i] = i * 3 - 70;
            var p = new StructBoundLoopParams { Start = 2, End = 9, Scale = 11 };
            using var dData = accelerator.Allocate1D(data);
            using var dOut = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadStreamKernel<ArrayView<int>, ArrayView<int>, StructBoundLoopParams>(StructBoundBarrierLoopKernel);
            kernel(new KernelConfig(groups, StructBoundLoopGroupSize), dData.View, dOut.View, p);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int g = 0; g < n; g++)
            {
                int group = g / StructBoundLoopGroupSize, t = g % StructBoundLoopGroupSize, want = 0;
                for (int d = p.Start; d < p.End; d++)
                    want += data[group * StructBoundLoopGroupSize + (t + d) % StructBoundLoopGroupSize] * d + p.Scale;
                if (got[g] != want)
                    throw new Exception($"output[{g}] = {got[g]}, expected {want}");
            }
        });
    }
}
