using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WebGPUBackend.GuardF64ErrorTerms (5.4.56): on (the default) the Dekker / Ozaki error-free transforms read an opaque
    // zero from _scalar_params so a reassociating compiler cannot cancel their error terms; off they are unguarded
    // (_f32_opq is the identity, no zero slot). Checks the generated WGSL both ways and that guarded doubles keep their
    // low half, and reports (without failing: it is a property of the device's compiler) whether the unguarded
    // transforms survive on this device: D3D12 measured yes, Mesa llvmpipe no.
    public abstract partial class BackendTestBase
    {
        static void F64GuardOnKernel(Index1D i, ArrayView1D<double, Stride1D.Dense> v, ArrayView1D<double, Stride1D.Dense> outp)
        {
            double x = v[i];
            outp[i] = x + 1.0 / x;
        }

        static void F64GuardOffKernel(Index1D i, ArrayView1D<double, Stride1D.Dense> v, ArrayView1D<double, Stride1D.Dense> outp)
        {
            double x = v[i];
            outp[i] = x + 1.0 / x;
        }

        [TestMethod]
        public async Task F64_GuardF64ErrorTerms_SwitchSelectsTheLibrary() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator webgpu)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: GuardF64ErrorTerms is a WebGPU switch");
            if (webgpu.Backend.F64Mode is not (F64EmulationMode.Dekker or F64EmulationMode.Ozaki))
                throw new UnsupportedTestException($"WebGPU {webgpu.Backend.F64Mode}: the guard applies to Dekker and Ozaki only");
            var input = new double[] { 0.1, 1.0 / 3.0, 1e-14, 12345.678901234567, 33554431.0, -2.718281828459045 };
            var errors = new System.Collections.Generic.List<string>();

            async Task<double[]> Run(Action<Index1D, ArrayView1D<double, Stride1D.Dense>, ArrayView1D<double, Stride1D.Dense>> kernel)
            {
                using var v = accelerator.Allocate1D(input);
                using var outp = accelerator.Allocate1D<double>(input.Length);
                accelerator.LoadAutoGroupedStreamKernel(kernel)(input.Length, v.View, outp.View);
                await accelerator.SynchronizeAsync();
                return await outp.CopyToHostAsync<double>();
            }
            int Wrong(double[] got)
            {
                int bad = 0;
                for (int k = 0; k < input.Length; k++)
                {
                    double want = input[k] + 1.0 / input[k];
                    if (!(Math.Abs(got[k] - want) <= Math.Abs(want) * 1e-12)) bad++;
                }
                return bad;
            }

            bool was = webgpu.Backend.GuardF64ErrorTerms;
            try
            {
                webgpu.Backend.GuardF64ErrorTerms = true;
                var on = await Run(F64GuardOnKernel);
                string onSrc = WebGPUBackend.WGSLRegistry[nameof(F64GuardOnKernel)].Source;
                if (!onSrc.Contains("fn _f32_opq(") || !onSrc.Contains("^ _scalar_params["))
                    errors.Add("guard on: the WGSL has no _f32_opq reading the zero slot");
                int onWrong = Wrong(on);
                if (onWrong > 0) errors.Add($"guard on: {onWrong} of {input.Length} doubles lost their low half (first {on[0]:R})");

                webgpu.Backend.GuardF64ErrorTerms = false;
                var off = await Run(F64GuardOffKernel);
                string offSrc = WebGPUBackend.WGSLRegistry[nameof(F64GuardOffKernel)].Source;
                if (!offSrc.Contains("fn _f32_opq(x: f32) -> f32 { return x; }") || offSrc.Contains("^ _scalar_params["))
                    errors.Add("guard off: the WGSL still guards the error-free transforms");
                int offWrong = Wrong(off);
                Console.WriteLine($"[report] WebGPU {webgpu.Backend.F64Mode} unguarded error-free transforms on this device: " +
                    (offWrong == 0 ? "kept (the compiler does not reassociate them)" : $"LOST on {offWrong} of {input.Length} (the compiler reassociates; keep the guard on)") +
                    $"; F64ErrorTermGuard.Auto chose the guard {(was ? "ON" : "OFF")} (probe {WebGPUF64ReassociationProbe.LastProbeMilliseconds:F1} ms at accelerator creation)");
                // Auto must never leave a device wrong: off only where the unguarded transforms are right
                if (!was && offWrong > 0)
                    errors.Add($"F64ErrorTermGuard.Auto left the guard off, but unguarded doubles are wrong on this device ({offWrong} of {input.Length})");
            }
            finally
            {
                webgpu.Backend.GuardF64ErrorTerms = was;
            }
            if (errors.Count > 0) throw new Exception(string.Join("; ", errors));
        });

        // What F64ErrorTermGuard.Auto costs at accelerator creation: the probe (two small shaders and a readback) runs
        // while the creation loads its helper script and byte kernels. Fresh accelerators on the same device, probe cache
        // cleared each round, Auto against Off (no probe); median of 3, reported, not asserted (a timing).
        [TestMethod]
        public async Task F64ErrorTermGuard_AutoProbe_CreationCost_Report() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: F64ErrorTermGuard is a WebGPU option");
            WebGPUILGPUDevice device;
            {
                var devices = accelerator.Context.GetWebGPUDevices(); // a ref struct: not kept across the awaits below
                if (devices.Count == 0) throw new UnsupportedTestException("no WebGPU device in this context");
                device = devices[0];
            }
            var auto = new System.Collections.Generic.List<double>();
            var off = new System.Collections.Generic.List<double>();
            for (int round = 0; round < 3; round++)
            {
                foreach (var guard in new[] { F64ErrorTermGuard.Auto, F64ErrorTermGuard.Off })
                {
                    if (guard == F64ErrorTermGuard.Auto) WebGPUF64ReassociationProbe.ClearCache();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var acc = await device.CreateAcceleratorAsync(accelerator.Context, new WebGPUBackendOptions { F64ErrorTermGuard = guard });
                    sw.Stop();
                    acc.Dispose();
                    (guard == F64ErrorTermGuard.Auto ? auto : off).Add(sw.Elapsed.TotalMilliseconds);
                }
            }
            auto.Sort(); off.Sort();
            Console.WriteLine($"[report] WebGPU accelerator creation, median of 3: F64ErrorTermGuard.Auto {auto[1]:F1} ms, Off {off[1]:F1} ms " +
                $"(the probe alone: {WebGPUF64ReassociationProbe.LastProbeMilliseconds:F1} ms, overlapped with the rest of the creation)");
        });
    }
}
