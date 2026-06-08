using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // MemoryPressure.AllocateWithReclaim: the flush -> reclaim -> retry mechanism a pool composes
    // with for allocation-time VRAM eviction. These drive the real helper logic with allocate/reclaim
    // thunks on a real accelerator (the Synchronize flush runs for real); the thunks simulate the
    // allocation success/OOM contract so we can hit every branch deterministically without forcing a
    // real device OOM.
    public abstract partial class BackendTestBase
    {
        /// <summary>Happy path: allocate succeeds first try, reclaim is never invoked.</summary>
        [TestMethod]
        public async Task AllocateWithReclaim_SuccessNoReclaimTest() => await RunTest(async accelerator =>
        {
            int allocCalls = 0;
            bool reclaimCalled = false;
            int result = accelerator.AllocateWithReclaim(
                () => { allocCalls++; return 42; },
                () => { reclaimCalled = true; return 0L; });

            if (result != 42)
                throw new Exception($"AllocateWithReclaim returned {result}, expected 42");
            if (allocCalls != 1)
                throw new Exception($"allocate called {allocCalls} times, expected 1 (no retry on success)");
            if (reclaimCalled)
                throw new Exception("reclaim must NOT run when the first allocation succeeds");
        });

        /// <summary>Pressure path: first allocate throws, reclaim runs, retry succeeds.</summary>
        [TestMethod]
        public async Task AllocateWithReclaim_RetriesAfterReclaimTest() => await RunTest(async accelerator =>
        {
            int allocCalls = 0;
            bool reclaimCalled = false;
            int result = accelerator.AllocateWithReclaim(
                () =>
                {
                    allocCalls++;
                    if (allocCalls == 1)
                        throw new InvalidOperationException("simulated device OOM");
                    return 7;
                },
                () => { reclaimCalled = true; return 4096L; });

            if (result != 7)
                throw new Exception($"AllocateWithReclaim returned {result}, expected 7 after reclaim+retry");
            if (allocCalls != 2)
                throw new Exception($"allocate called {allocCalls} times, expected 2 (one failure + one retry)");
            if (!reclaimCalled)
                throw new Exception("reclaim must run after the first allocation fails");
        });

        /// <summary>
        /// Exhausted path: allocate always throws. reclaim runs once, then it rethrows with the
        /// describeState message (carrying the reclaimed byte count) and the original failure inside.
        /// </summary>
        [TestMethod]
        public async Task AllocateWithReclaim_ThrowsWhenStillExhaustedTest() => await RunTest(async accelerator =>
        {
            int allocCalls = 0;
            int reclaimCalls = 0;
            bool threw = false;
            string message = "";
            Exception? inner = null;
            try
            {
                accelerator.AllocateWithReclaim<int>(
                    () => { allocCalls++; throw new InvalidOperationException("always OOM"); },
                    () => { reclaimCalls++; return 9_437_184L; },
                    reclaimed => $"out of memory; reclaimed {reclaimed / 1048576}MB; live set huge");
            }
            catch (Exception ex)
            {
                threw = true;
                message = ex.Message;
                inner = ex.InnerException;
            }

            if (!threw)
                throw new Exception("AllocateWithReclaim must rethrow when the retry also fails");
            if (allocCalls != 2)
                throw new Exception($"allocate called {allocCalls} times, expected 2 (initial + one retry)");
            if (reclaimCalls != 1)
                throw new Exception($"reclaim called {reclaimCalls} times, expected exactly 1");
            if (!message.Contains("reclaimed 9MB"))
                throw new Exception($"exception message should carry the describeState text, got: {message}");
            if (inner is null || inner.Message != "always OOM")
                throw new Exception("the original allocation failure must be the inner exception");
        });

        /// <summary>Works with a real ILGPU allocation (succeeds first try) and yields a usable buffer.</summary>
        [TestMethod]
        public async Task AllocateWithReclaim_RealBufferTest() => await RunTest(async accelerator =>
        {
            const int n = 256;
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = i * 2.0f;

            bool reclaimCalled = false;
            using var buf = accelerator.AllocateWithReclaim(
                () => accelerator.Allocate1D(data),
                () => { reclaimCalled = true; return 0L; });

            await accelerator.SynchronizeAsync();
            var got = await buf.CopyToHostAsync<float>();

            if (reclaimCalled)
                throw new Exception("reclaim must NOT run when the real allocation succeeds");
            for (int i = 0; i < n; i++)
                if (got[i] != i * 2.0f)
                    throw new Exception($"buffer round-trip mismatch at [{i}]: expected {i * 2.0f}, got {got[i]}");
        });
    }
}
