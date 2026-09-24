using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // buffer.CopyToHostAsync<T>(offset, count) on a MemoryBuffer1D used to read the WHOLE buffer and
    // slice it on the host. The values were right, so CopyToHostPartialReadback passed, but a 4-byte
    // counter read from a large buffer paid for every byte of it (SpawnScene reads prefixes of
    // capacity-sized training buffers this way). The values run on every backend; the byte count is
    // WebGPU-only, where ProfileReadbackBytes lives.
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task CopyToHostPartialReadback_CopiesOnlyTheRange() => await RunTest(async accelerator =>
        {
            const int len = 1 << 18; // 1 MiB of ints
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(MyKernel);
            kernel((Index1D)len, buf.View, 0); // buf[i] = i
            await accelerator.SynchronizeAsync();

            bool webgpu = accelerator is WebGPUAccelerator;
            bool wasOn = WebGPUBackend.EnableDispatchProfiling;
            WebGPUBackend.EnableDispatchProfiling = true;
            try
            {
                WebGPUBackend.ResetDispatchProfiling();
                var got = await buf.CopyToHostAsync<int>(100_000, 4);
                long bytes = WebGPUBackend.ProfileReadbackBytes;

                if (got.Length != 4) throw new Exception($"expected 4 elements, got {got.Length}");
                for (int i = 0; i < 4; i++)
                    if (got[i] != 100_000 + i)
                        throw new Exception($"got[{i}] expected {100_000 + i}, got {got[i]}");

                if (webgpu && bytes != 16)
                    throw new Exception(
                        $"a 4-int read copied {bytes:N0} bytes GPU->CPU (expected 16); " +
                        $"the whole {len * 4:N0}-byte buffer crossed to read a range of it");
            }
            finally
            {
                WebGPUBackend.EnableDispatchProfiling = wasOn;
                WebGPUBackend.ResetDispatchProfiling();
            }
        });
    }
}
