using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Disposing a buffer while work queued on it has not run yet is legal ILGPU: CUDA's free and WebGPU's destroy
    // both wait for it. The Wasm backend released the buffer's SharedArrayBuffer at once, so a queued kernel's copy
    // into or out of it failed with "RangeError: offset is out of bounds" (SpawnDev.Codecs' Mp3EncoderGpu, 2026-11-16:
    // a `using var` scratch buffer went out of scope before the encoder's final SynchronizeAsync).
    public abstract partial class BackendTestBase
    {
        static void DisposePendingFillKernel(Index1D i, ArrayView<int> target, int value) => target[i] = value + i;
        static void DisposePendingAddKernel(Index1D i, ArrayView<int> output, ArrayView<int> input) => output[i] += input[i];

        [TestMethod]
        public async Task DisposeWithPendingWork_QueuedWorkCompletes() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var fill = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(DisposePendingFillKernel);
            var add = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(DisposePendingAddKernel);
            var hostData = new int[n];
            for (int i = 0; i < n; i++) hostData[i] = 1000 * (i % 7);

            using var output = accelerator.Allocate1D<int>(n);
            output.MemSetToZero();
            {
                // scratch: written by a kernel, read by a kernel, overwritten by the host, read again - then
                // disposed with all of that still queued (no sync in between)
                using var scratch = accelerator.Allocate1D<int>(n);
                fill(n, scratch.View, 5);
                add(n, output.View, scratch.View);
                scratch.View.CopyFromCPU(hostData);
                add(n, output.View, scratch.View);
            }
            await accelerator.SynchronizeAsync();

            var got = await output.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int want = 5 + i + hostData[i];
                if (got[i] != want)
                    throw new Exception($"output[{i}] = {got[i]}, expected {want} (5 + {i} from the first read, {hostData[i]} from the host write)");
            }
        });
    }
}
