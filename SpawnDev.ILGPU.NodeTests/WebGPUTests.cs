using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.NodeTests
{
    /// <summary>
    /// WebGPU backend under SpawnJS. Inherits every BlazorJS-clean shared kernel test from
    /// BackendTestBase and supplies the WebGPU accelerator. Parameterless constructor: the SpawnJS host
    /// has no crypto/WebTorrent to inject, and the inherited tests never need them.
    /// </summary>
    public class WebGPUTests : BackendTestBase
    {
        protected override string BackendName => "WebGPU";

        /// <summary>The F64ErrorTermGuard this run's accelerators use (Program.cs: --f64guard=auto|on|off; default Auto).</summary>
        public static F64ErrorTermGuard F64GuardForThisRun { get; set; } = F64ErrorTermGuard.Auto;
        static bool _firstCreationReported;

        protected override async Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
        {
            var builder = Context.Create().EnableAlgorithms();
            builder.EnableWebGPUAlgorithms();
            await builder.WebGPU();
            var context = builder.ToContext();
            var devices = context.GetWebGPUDevices();
            if (devices.Count == 0)
                throw new UnsupportedTestException("No WebGPU devices found");
            var options = new WebGPUBackendOptions
            {
                F64Emulation = F64EmulationMode.Ozaki,
                F64ErrorTermGuard = F64GuardForThisRun,
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var accelerator = await devices[0].CreateAcceleratorAsync(context, options);
            if (!_firstCreationReported)
            {
                _firstCreationReported = true;
                Console.WriteLine($"[report] first WebGPU accelerator creation in this process: {sw.Elapsed.TotalMilliseconds:F1} ms (F64ErrorTermGuard.{F64GuardForThisRun}; probe {SpawnDev.ILGPU.WebGPU.WebGPUF64ReassociationProbe.LastProbeMilliseconds:F1} ms: {SpawnDev.ILGPU.WebGPU.WebGPUF64ReassociationProbe.LastProbeBreakdown})");
            }
            return (context, accelerator);
        }

        protected override Task<(Context context, Accelerator accelerator)> CreateEmulatedAcceleratorAsync()
            => CreateAcceleratorAsync();

        /// <summary>Smoke test with no GPU dispatch: proves the reflection path and host are alive.</summary>
        [TestMethod]
        public Task InteropSizeOfClosedGenericTest()
        {
            int size = Interop.SizeOf(typeof(System.Collections.Generic.KeyValuePair<int, float>));
            if (size != 8) throw new Exception($"Interop.SizeOf(KeyValuePair<int,float>) returned {size}, expected 8");
            if (Interop.SizeOf(typeof(int)) != 4) throw new Exception("Interop.SizeOf(int) != 4");
            return Task.CompletedTask;
        }

        /// <summary>WebGPU accelerator init + buffer round trip, WebGPU-specific API path.</summary>
        [TestMethod]
        public async Task WebGPUBufferTransferTest()
        {
            var device = await WebGPUDevice.GetDefaultDeviceAsync();
            if (device == null) throw new UnsupportedTestException("No WebGPU devices found");
            using var accelerator = await device.CreateAcceleratorAsync();
            int length = 128;
            var data = Enumerable.Range(0, length).ToArray();
            using var buffer = accelerator.Allocate(data);
            var readBack = await buffer.CopyToHostAsync();
            if (readBack.Length != length) throw new Exception("Readback length mismatch");
            for (int i = 0; i < length; i++)
                if (readBack[i] != data[i]) throw new Exception($"Data mismatch at index {i}");
        }
    }
}
