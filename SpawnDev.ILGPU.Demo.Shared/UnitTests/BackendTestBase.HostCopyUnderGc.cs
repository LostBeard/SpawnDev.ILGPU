using System;
using System.Threading;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-25 (SpawnDev.Codecs, found by Geordi): `view.CopyFromCPU(ref data[0], n)` uploaded the wrong bytes about
    // once in 4,550 CUDA uploads while another process loaded every core. The synchronous `ref T` overloads of
    // CopyFromCPU / CopyToCPU passed Unsafe.AsPointer(ref data) to the driver without pinning; the copy runs in a
    // P/Invoke (preemptive mode), where a compacting GC may move the managed array, so the driver read (or wrote)
    // memory the array no longer occupied. Fix: both overloads pin the data until the copy is consumed, like the
    // Span overloads. This test uploads and downloads fresh arrays through the ref overloads while another thread
    // forces blocking compacting collections. Desktop only: the browser backends copy synchronously in managed code.
    public abstract partial class BackendTestBase
    {
        [TestMethod(Timeout = 120000)]
        public async Task HostCopy_RefOverloads_UnderCompactingGc_CopyTheRightBytes() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm)
                throw new UnsupportedTestException("Browser backends copy host data synchronously in managed code (no P/Invoke, no second thread).");

            // Bounded by time as well as count: a forced collection per copy is slow on a big process heap.
            const int maxIterations = 3000;
            const int minIterations = 200;
            var budget = TimeSpan.FromSeconds(20);
            const int length = 64 * 1024;
            using var deviceBuf = accelerator.Allocate1D<byte>(length);
            ArrayView<byte> view = deviceBuf.View;
            var check = new byte[length];

            using var stop = new CancellationTokenSource();
            var collector = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    // The fresh arrays live in gen 0, and a compacting gen 0 collection is what moves them.
                    GC.Collect(0, GCCollectionMode.Forced, blocking: true, compacting: true);
                    Thread.Yield();
                }
            });
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int done = 0;
            try
            {
                for (int it = 0; it < maxIterations && (it < minIterations || clock.Elapsed < budget); it++)
                {
                    // Garbage first, so the fresh array sits behind dead objects and a compaction moves it.
                    for (int g = 0; g < 8; g++) GC.KeepAlive(new byte[1024 + g]);
                    var upload = new byte[length];
                    byte seed = (byte)(it * 31 + 7);
                    for (int k = 0; k < length; k++) upload[k] = (byte)(seed + k * 13);

                    view.CopyFromCPU(ref upload[0], length);
                    view.CopyToCPU(new Span<byte>(check));
                    for (int k = 0; k < length; k++)
                        if (check[k] != upload[k])
                            throw new Exception($"CopyFromCPU(ref) on {BackendName}, iteration {it}, byte {k}: device has {check[k]}, uploaded {upload[k]}");

                    for (int g = 0; g < 8; g++) GC.KeepAlive(new byte[1024 + g]);
                    var download = new byte[length];
                    view.CopyToCPU(ref download[0], length);
                    for (int k = 0; k < length; k++)
                        if (download[k] != upload[k])
                            throw new Exception($"CopyToCPU(ref) on {BackendName}, iteration {it}, byte {k}: got {download[k]}, expected {upload[k]}");
                    done++;
                }
            }
            finally
            {
                stop.Cancel();
                await collector;
            }
            Console.WriteLine($"[HostCopyUnderGc] {done} uploads + downloads of {length} bytes through the ref overloads under compacting GC on {BackendName} in {clock.Elapsed.TotalSeconds:F1} s ✓");
        });
    }
}
