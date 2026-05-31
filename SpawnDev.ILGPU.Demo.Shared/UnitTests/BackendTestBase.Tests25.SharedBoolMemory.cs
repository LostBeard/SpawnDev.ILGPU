using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression guard for SharedMemory.Allocate<bool> on WebGPU (Tuvok 2026-05-31).
    // WGSL forbids `bool` in the workgroup address space, so a naive `var<workgroup> array<bool,N>`
    // silently fails shader compilation and the kernel never runs (returns zeros). This was
    // surfaced by ILGPUUniqueAsyncTest (UniqueKernel uses a shared bool tile). The fix declares
    // bool shared arrays as array<i32,N> and converts at load/store only when the IR value is
    // genuinely bool. This test exercises that path directly: a shared bool tile written under a
    // barrier and read back via the same consecutive-duplicate-marking pattern Unique uses, so a
    // regression (e.g. wrong remap, an unresolved pointer cast, or a select/assign type error)
    // fails here with a clear WGSL/Tint message rather than a silent zero deep inside Unique.
    public abstract partial class BackendTestBase
    {
        internal static void SharedBoolMemoryKernel(
            ArrayView<int> input,
            ArrayView<int> result,
            SpecializedValue<int> tileSize,
            Index1D numIterationsPerGroup)
        {
            var tileInfo = new TileInfo(input.IntLength, numIterationsPerGroup);
            var temp = SharedMemory.Allocate<bool>(256);

            for (int i = tileInfo.StartIndex; i < tileInfo.MaxLength; i += Group.DimX)
            {
                if (Group.IsFirstThread && i == 0)
                    temp[i] = true; // first element is always "unique"
                else
                    temp[i] = input[i] != input[i - 1]; // bool stored to shared
            }
            Group.Barrier();

            if (Group.IsFirstThread)
            {
                var count = 0;
                var maxLength = XMath.Min(temp.IntLength, tileInfo.MaxLength);
                for (var i = 0; i < maxLength; i++)
                {
                    result[1 + i] = temp[i] ? 1 : 0; // bool read back from shared
                    if (temp[i]) count++;
                }
                result[0] = count;
            }
        }

        [TestMethod]
        public virtual async Task SharedBoolMemoryTest() => await RunTest(async accelerator =>
        {
            int[] data = { 1, 1, 2, 3, 3, 3, 4, 5, 5 };
            int[] expectedFlags = { 1, 0, 1, 1, 0, 0, 1, 1, 0 };
            const int expectedCount = 5;

            using var inBuf = accelerator.Allocate1D(data);
            using var resBuf = accelerator.Allocate1D<int>(data.Length + 1);
            await resBuf.View.MemSetToZeroAsync(accelerator.DefaultStream);

            var spec = accelerator.AcceleratorType == AcceleratorType.WebGPU
                ? new KernelSpecialization(accelerator.MaxNumThreadsPerGroup, null)
                : KernelSpecialization.Empty;
            var kernel = accelerator.LoadKernel<
                ArrayView<int>, ArrayView<int>, SpecializedValue<int>, Index1D>(
                SharedBoolMemoryKernel, spec);

            var (gridDim, groupDim) = accelerator.ComputeGridStrideLoopExtent(
                data.Length, out int numIterationsPerGroup);
            kernel(
                accelerator.DefaultStream,
                (gridDim, groupDim),
                inBuf.View,
                resBuf.View,
                new SpecializedValue<int>(groupDim * numIterationsPerGroup),
                numIterationsPerGroup);
            // Surfaces any WGSL shader-compile error (e.g. bool in workgroup) as a hard failure
            // rather than a silent zero.
            await accelerator.SynchronizeAsync();

            var r = await resBuf.View.CopyToCPUAsync(accelerator.DefaultStream);
            if (r[0] != expectedCount)
                throw new Exception(
                    $"SharedBoolMemory: unique count {r[0]}, expected {expectedCount} " +
                    $"(shared bool tile likely failed — bool is illegal in WGSL workgroup memory; " +
                    $"must be remapped to i32).");
            for (int i = 0; i < expectedFlags.Length; i++)
                if (r[1 + i] != expectedFlags[i])
                    throw new Exception(
                        $"SharedBoolMemory: flag[{i}]={r[1 + i]}, expected {expectedFlags[i]}.");
        });
    }
}
