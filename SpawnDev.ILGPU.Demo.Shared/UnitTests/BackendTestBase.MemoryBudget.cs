using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Accelerator.AllocatedDeviceBytes / DeviceMemoryBudgetBytes (5.4.50). Only CUDA (and AMD OpenCL) can report free memory, so
    // on the other backends the memory-pressure reclaim had nothing to measure against and switched itself off. A budget the
    // application sets gives every backend a number: budget minus what ILGPU allocated and has not disposed.
    public abstract partial class BackendTestBase
    {
        sealed class IdleBufferReclaimer : IDeviceMemoryReclaimer
        {
            MemoryBuffer1D<byte, Stride1D.Dense>? _idle;
            public int Reclaims;
            public IdleBufferReclaimer(MemoryBuffer1D<byte, Stride1D.Dense> idle) => _idle = idle;
            public long ReclaimableBytes => _idle?.LengthInBytes ?? 0;
            public long LastUsedTimestamp => 0;
            public long Reclaim(long bytesWanted)
            {
                var idle = _idle;
                if (idle == null) return 0;
                long bytes = idle.LengthInBytes;
                _idle = null;
                idle.Dispose();
                Reclaims++;
                return bytes;
            }
        }

        [TestMethod(Timeout = 60000)]
        public async Task MemoryBudget_AllocatedBytesFollowEveryBuffer() => await RunTest(async accelerator =>
        {
            long before = accelerator.AllocatedDeviceBytes;
            var a = accelerator.Allocate1D<int>(1000);
            var b = accelerator.Allocate1D<byte>(4096);
            long during = accelerator.AllocatedDeviceBytes;
            if (during - before != 4000 + 4096)
                throw new Exception($"after two allocations AllocatedDeviceBytes grew by {during - before}, expected 8096");
            a.Dispose();
            a.Dispose();   // a second dispose must not be counted again
            if (accelerator.AllocatedDeviceBytes - before != 4096)
                throw new Exception($"after disposing one (twice) it is {accelerator.AllocatedDeviceBytes - before} over the start, expected 4096");
            b.Dispose();
            if (accelerator.AllocatedDeviceBytes != before)
                throw new Exception($"after disposing both it is {accelerator.AllocatedDeviceBytes - before} over the start, expected 0");
            await Task.CompletedTask;
        });

        // ReclaimableDeviceBytes (5.4.51): what the registered holders could give back, so an owner can tell a model's own
        // bytes (allocated growth) from the idle pool scratch a run leaves behind (reclaimable growth). SpawnDev.ILGPU.ML's
        // ModelResidency measured 7.3 GB of "kept models" with 4.9 GB allocated in all before it could tell them apart.
        [TestMethod(Timeout = 60000)]
        public async Task MemoryBudget_ReclaimableBytesSumTheHolders() => await RunTest(async accelerator =>
        {
            const int KB = 1024;
            long before = accelerator.ReclaimableDeviceBytes();
            var a = new IdleBufferReclaimer(accelerator.Allocate1D<byte>(256 * KB));
            var b = new IdleBufferReclaimer(accelerator.Allocate1D<byte>(64 * KB));
            accelerator.RegisterReclaimer(a);
            accelerator.RegisterReclaimer(b);
            try
            {
                if (accelerator.ReclaimableDeviceBytes() - before != 320 * KB)
                    throw new Exception($"two holders of 256 + 64 KB: ReclaimableDeviceBytes grew by {accelerator.ReclaimableDeviceBytes() - before}, expected {320 * KB}");
                a.Reclaim(1);
                if (accelerator.ReclaimableDeviceBytes() - before != 64 * KB)
                    throw new Exception($"after one gave its buffer back: {accelerator.ReclaimableDeviceBytes() - before} over the start, expected {64 * KB}");
                accelerator.UnregisterReclaimer(b);
                if (accelerator.ReclaimableDeviceBytes() != before)
                    throw new Exception($"after unregistering the other: {accelerator.ReclaimableDeviceBytes() - before} over the start, expected 0");
            }
            finally
            {
                accelerator.UnregisterReclaimer(a);
                accelerator.UnregisterReclaimer(b);
                b.Reclaim(1);
            }
            await Task.CompletedTask;
        });

        [TestMethod(Timeout = 60000)]
        public async Task MemoryBudget_FreeMemoryAndReclaimFollowTheBudget() => await RunTest(async accelerator =>
        {
            const int KB = 1024;
            long? savedHeadroom = accelerator.DeviceMemoryHeadroomBytes;
            MemoryBuffer1D<byte, Stride1D.Dense>? idle = null;
            MemoryBuffer1D<byte, Stride1D.Dense>? used = null;
            IdleBufferReclaimer? reclaimer = null;
            bool hadNumber = accelerator.TryGetFreeMemory(out _);
            try
            {
                accelerator.DeviceMemoryHeadroomBytes = 0;
                idle = accelerator.Allocate1D<byte>(512 * KB);
                reclaimer = new IdleBufferReclaimer(idle);
                accelerator.RegisterReclaimer(reclaimer);
                // an allocation with a reclaimer registered and no budget: on a backend with no free-memory number this
                // switches the memory-pressure check off for the accelerator; the budget set below must switch it on again
                accelerator.Allocate1D<byte>(16).Dispose();
                long live = accelerator.AllocatedDeviceBytes;
                accelerator.DeviceMemoryBudgetBytes = live + 512 * KB;
                if (!accelerator.TryGetFreeMemory(out long free) || free != 512 * KB)
                    throw new Exception($"with a budget of {512 * KB} bytes more than is allocated, TryGetFreeMemory gives {free} (expected {512 * KB})");

                long reclaimsBefore = MemoryPressure.ProactiveReclaimCount;
                used = accelerator.Allocate1D<byte>(700 * KB);   // 700 KB do not fit beside the idle 512: it has to go
                if (reclaimer.Reclaims != 1 || MemoryPressure.ProactiveReclaimCount != reclaimsBefore + 1)
                    throw new Exception($"the allocation beyond the budget reclaimed {reclaimer.Reclaims} time(s) ({MemoryPressure.ProactiveReclaimCount - reclaimsBefore} counted), expected 1");
                if (accelerator.AllocatedDeviceBytes != live - 512 * KB + 700 * KB)
                    throw new Exception($"AllocatedDeviceBytes is {accelerator.AllocatedDeviceBytes}, expected {live - 512 * KB + 700 * KB}");
                idle = null; // the reclaimer disposed it

                accelerator.DeviceMemoryBudgetBytes = null;
                bool number = accelerator.TryGetFreeMemory(out _);
                if (number != hadNumber)
                    throw new Exception($"without a budget TryGetFreeMemory answers {number}, before the test it answered {hadNumber}");
                await Task.CompletedTask;
            }
            finally
            {
                accelerator.DeviceMemoryBudgetBytes = null;
                accelerator.DeviceMemoryHeadroomBytes = savedHeadroom;
                if (reclaimer != null) accelerator.UnregisterReclaimer(reclaimer);
                idle?.Dispose();
                used?.Dispose();
            }
        });
    }
}
