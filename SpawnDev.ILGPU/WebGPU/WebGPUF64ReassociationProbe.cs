using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Asks a WebGPU device whether its shader compiler breaks the UNGUARDED Dekker / Ozaki error-free transforms
    /// (<see cref="F64ErrorTermGuard.Auto"/>). WGSL lets an implementation reassociate (spec 15.7.5); Mesa's NIR does, and
    /// turns s = a + b; s - a into b, so every error term is 0 and a double keeps only its f32 part (llvmpipe, 2026-11-27).
    /// D3D12 on an RTX 4070 keeps them. The guard that survives reassociation costs time (0.054 vs 0.085 ns per f64
    /// multiply-add there), so it goes only where it is needed.
    /// <para>
    /// The probe runs the library's own unguarded <c>f64_add</c> (TwoSum, Fast2Sum) and <c>f64_mul</c> (Veltkamp split,
    /// two-product, Fast2Sum) on f32 inputs whose exact sum and product an f32 cannot hold, and checks that the components
    /// add up to them exactly: a compiler that cancels the error terms returns the rounded f32 alone. Only those
    /// functions and their dependencies are compiled (no load / store conversion code: the first probe, built on the
    /// IEEE load and store, took ~330 ms of shader compile at every accelerator creation on an RTX 4070); the Dekker and
    /// Ozaki shaders compile concurrently, and the answer is kept per adapter for the process.
    /// </para>
    /// </summary>
    internal static class WebGPUF64ReassociationProbe
    {
        // a + b: 1 + 2^-30 (the f32 sum rounds to 1, the error term is 2^-30); c * c: (1 + 2^-12)^2 = 1 + 2^-11 + 2^-24
        // (the f32 product rounds the 2^-24 away, the two-product error term carries it)
        static readonly float[] Inputs = { 1f, MathF.ScaleB(1f, -30), 1f + MathF.ScaleB(1f, -12), 1f + MathF.ScaleB(1f, -12) };
        static readonly double ExactSum = 1.0 + Math.ScaleB(1.0, -30);
        static readonly double ExactProduct = 1.0 + Math.ScaleB(1.0, -11) + Math.ScaleB(1.0, -24);

        const string Bindings = @"
@group(0) @binding(0) var<storage, read> probe_in : array<f32>;
@group(0) @binding(1) var<storage, read_write> probe_out : array<f32>;
";

        // One small shader with the three error-free transforms everything else is built from, unguarded: TwoSum
        // (f32_two_sum), Fast2Sum (f32_quick_two_sum) and the split + two-product (f64_two_prod), the same expressions
        // Dekker's f64_add / f64_mul / load / store and Ozaki's arithmetic write inline. Every function more costs compile
        // time at accelerator creation: with Dekker's f64_add and f64_mul in it the probe added ~25 ms to a cold first
        // creation on an RTX 4070 (D3D12), with Ozaki's f64_mul ~50 ms more.
        const string Body = Bindings + @"
@compute @workgroup_size(1) fn main() {
  let t = f32_two_sum(probe_in[0], probe_in[1]);
  let q = f32_quick_two_sum(probe_in[0], probe_in[1]);
  let p = f64_two_prod(probe_in[2], probe_in[3]);
  probe_out[0] = t.x; probe_out[1] = t.y; probe_out[2] = p.x; probe_out[3] = p.y; probe_out[4] = q.x; probe_out[5] = q.y;
}
";

        static readonly Dictionary<string, bool> Results = new(StringComparer.Ordinal);

        /// <summary>Milliseconds the last probe that ran took (compile, dispatch, readback); a cached answer costs none.</summary>
        internal static double LastProbeMilliseconds { get; private set; }

        /// <summary>Where the last probe's time went (pipeline, dispatch, readback), for the creation-cost report.</summary>
        internal static string LastProbeBreakdown { get; private set; } = "";

        /// <summary>Forgets the per-adapter answers, so the next accelerator creation probes again (the creation-cost test).</summary>
        internal static void ClearCache() { lock (Results) Results.Clear(); }

        /// <summary>
        /// True when <paramref name="native"/>'s compiler breaks the unguarded transforms (Dekker or Ozaki), so the backend
        /// must guard them. <paramref name="adapterKey"/> identifies the adapter for the per-process cache.
        /// </summary>
        public static async Task<bool> BreaksUnguardedAsync(WebGPUNativeAccelerator native, string adapterKey)
        {
            lock (Results) if (Results.TryGetValue(adapterKey, out var known)) return known;
            bool breaks;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                breaks = await RunAsync(native);
                LastProbeMilliseconds = sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                // A probe that cannot answer is no evidence that unguarded doubles are right: guard (correct everywhere).
                WebGPUBackend.Log($"[WebGPU] f64 reassociation probe failed ({ex.GetType().Name}: {ex.Message.Replace('\n', ' ').Replace('\r', ' ')}); guarding f64 error terms");
                return true;
            }
            lock (Results) Results[adapterKey] = breaks;
            if (WebGPUBackend.VerboseLogging)
                WebGPUBackend.Log($"[WebGPU] f64 reassociation probe on '{adapterKey}' ({LastProbeMilliseconds:F1} ms): {(breaks ? "the compiler breaks unguarded error-free transforms; guard on" : "unguarded transforms kept; guard off")}");
            return breaks;
        }

        static async Task<bool> RunAsync(WebGPUNativeAccelerator native)
        {
            string wgsl = WGSLEmulationLibrary.GetUnguardedF64ProbeLibrary(Body) + Body;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // the buffers and the upload first: they do not need the pipeline, so they happen while it compiles
            using var input = new WebGPUBuffer<float>(native, Inputs.Length);
            using var output = new WebGPUBuffer<float>(native, 6);
            input.CopyFromHost(Inputs);
            var pipeline = WebGPUComputeShader.CreateAsync(native, wgsl, "main", label: "ilgpu_f64_reassociation_probe");
            using var shader = await pipeline;
            double tPipeline = sw.Elapsed.TotalMilliseconds;
            shader.SetBuffer(0, input).SetBuffer(1, output);
            shader.Dispatch(1);
            double tDispatch = sw.Elapsed.TotalMilliseconds;
            var got = await output.CopyToHostAsync();
            LastProbeBreakdown = $"buffers + pipeline {tPipeline:F1} ms, dispatch {tDispatch - tPipeline:F1} ms, readback {sw.Elapsed.TotalMilliseconds - tDispatch:F1} ms";
            double twoSum = (double)got[0] + got[1], product = (double)got[2] + got[3], quick = (double)got[4] + got[5];
            bool breaks = twoSum != ExactSum || product != ExactProduct || quick != ExactSum;
            if (breaks && WebGPUBackend.VerboseLogging)
                WebGPUBackend.Log($"[WebGPU] f64 reassociation probe: two-sum {got[0]:R} + {got[1]:R} (exact {ExactSum:R}), two-product {got[2]:R} + {got[3]:R} (exact {ExactProduct:R}), quick two-sum {got[4]:R} + {got[5]:R}");
            return breaks;
        }
    }
}
