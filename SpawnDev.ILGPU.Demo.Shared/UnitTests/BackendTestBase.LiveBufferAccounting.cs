using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// WebGPU live-buffer accounting (<see cref="WebGPUBufferAccounting.LiveStorageBytes"/> etc.): an owned storage buffer counts
/// from creation to Dispose, and its cached readback staging buffer counts from the first readback to Dispose.
/// The numbers are what a consumer uses to find who holds GPU memory, so they must move by exactly the right
/// amounts - an accounting that drifts is worse than none.
/// </summary>
public abstract partial class BackendTestBase
{
    [TestMethod]
    public async Task WebGPU_LiveBufferAccounting_TracksAllocateReadbackDispose() => await RunTest(async accelerator =>
    {
        if (accelerator is not WebGPUAccelerator)
            throw new UnsupportedTestException("WebGPU-only accounting.");

        const int N = 1 << 20;             // 4 MiB of floats
        const long Bytes = N * 4L;
        long storage0 = WebGPUBufferAccounting.LiveStorageBytes, staging0 = WebGPUBufferAccounting.LiveStagingBytes;
        int count0 = WebGPUBufferAccounting.LiveBufferCount;

        var buf = accelerator.Allocate1D<float>(N);
        if (WebGPUBufferAccounting.LiveStorageBytes - storage0 != Bytes)
            throw new Exception($"allocate: storage bytes moved by {WebGPUBufferAccounting.LiveStorageBytes - storage0}, expected {Bytes}");
        bool listed = false;
        foreach (var (label, bytes) in WebGPUBufferAccounting.LargestLiveBuffers(64))
            if (bytes == Bytes && label.StartsWith("Storage#", StringComparison.Ordinal)) listed = true;
        if (!listed) throw new Exception("allocate: the 4 MiB buffer is not among the largest live buffers");

        buf.MemSetToZero();
        await accelerator.SynchronizeAsync();
        _ = await buf.View.CopyToHostAsync();   // full readback creates the cached staging buffer
        long stagingGrowth = WebGPUBufferAccounting.LiveStagingBytes - staging0;
        if (stagingGrowth < Bytes)
            throw new Exception($"readback: staging bytes moved by {stagingGrowth}, expected >= {Bytes}");

        buf.Dispose();
        if (WebGPUBufferAccounting.LiveStorageBytes != storage0 || WebGPUBufferAccounting.LiveStagingBytes != staging0
            || WebGPUBufferAccounting.LiveBufferCount != count0)
            throw new Exception(
                $"dispose: storage {WebGPUBufferAccounting.LiveStorageBytes - storage0:+#;-#;0}, staging " +
                $"{WebGPUBufferAccounting.LiveStagingBytes - staging0:+#;-#;0}, count {WebGPUBufferAccounting.LiveBufferCount - count0:+#;-#;0} " +
                "left over after Dispose - the accounting leaks.");
    });
}
