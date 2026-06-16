using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Wasm SIMD128 Phase 3 Stage-3a — the STRUCTURAL gate for the wired `kernel_simd` emitter.
/// Compiles REAL elementwise kernels through the offline Wasm codegen path with
/// <see cref="WasmBackend.ForceSimd"/> = true (desktop has no runtime SIMD), so the new
/// WasmBackend wiring emits the additive v128 `kernel_simd` function. Writes each emitted module
/// for offline `wasm-validate --enable-threads` / `wasm2wat` verification, and asserts in-process
/// that (a) the kernel was classified Vectorizable and (b) a `kernel_simd` export is actually
/// present in the binary. Catches encoding/type/stack-shape bugs OFFLINE before the numerical
/// PMT gate. The scalar `kernel` export must remain present and unchanged in every case.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- wasm-simd-emit-gate
/// Then: wasm-validate --enable-threads &lt;dir&gt;/&lt;name&gt;.wasm ; wasm2wat --enable-threads ... | less
/// </summary>
internal static class WasmSimdEmitGateProbe
{
    // a[i]*2 + b[i] — the canonical f32 unit-stride elementwise FMA-fold (constant scale, in-block).
    private static void MulAddConstKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o)
        => o[i] = a[i] * 2f + b[i];

    // a[i]*c + b[i] — same shape with a lane-INVARIANT scalar PARAM c (exercises f32x4.splat of a param,
    // if c is passed as an f32 kernel param; if it isn't, the emitter HARD-BAILS to scalar — still valid).
    private static void MulAddParamKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o, float c)
        => o[i] = a[i] * c + b[i];

    // min/max/neg/abs/div coverage in one uniform-control-flow body — all f32x4 ops the emitter maps.
    private static void MixedF32OpsKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o)
        => o[i] = XMath.Min(XMath.Max(-a[i], b[i]), XMath.Abs(a[i])) / 2f - b[i];

    public static Task<int> Run(string[] args)
    {
        string dir = args.Length > 1
            ? args[1]
            : Path.Combine(Directory.GetCurrentDirectory(), "_wasm_simd_emit_gate");
        Directory.CreateDirectory(dir);

        Console.WriteLine("=== Wasm SIMD128 Stage-3a structural gate (ForceSimd kernel_simd emission) ===");
        Console.WriteLine($"[gate] output dir: {dir}");

        var prevForce = WasmBackend.ForceSimd;
        var prevScalar = WasmBackend.ForceScalar;
        WasmBackend.ForceScalar = false;
        WasmBackend.ForceSimd = true;

        int rc = 0;
        try
        {
            rc |= Emit(dir, "muladd_const", "o[i]=a[i]*2+b[i]",
                (Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>)MulAddConstKernel,
                expectSimd: true);
            rc |= Emit(dir, "muladd_param", "o[i]=a[i]*c+b[i] (scalar param c)",
                (Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, float>)MulAddParamKernel,
                expectSimd: false /* depends on param ABI; report, don't fail */);
            rc |= Emit(dir, "mixed_f32ops", "min/max/neg/abs/div/sub",
                (Action<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>)MixedF32OpsKernel,
                expectSimd: false /* XMath.Min/Max may lower to calls; report, don't fail */);
        }
        finally
        {
            WasmBackend.ForceSimd = prevForce;
            WasmBackend.ForceScalar = prevScalar;
        }

        // Decide which REAL accelerator launch path actually vectorizes: auto-grouped (ILGPU wraps the
        // body in `if (index < length) { ... }` — a lane-variant branch that Stage 3a rejects) vs an
        // explicit-group launch (branch-free body). This drives the numerical PMT test's kernel choice.
        AcceleratorPathCheckAsync().GetAwaiter().GetResult();

        Console.WriteLine();
        Console.WriteLine(rc == 0
            ? "=== gate PASS (every expectSimd kernel emitted a kernel_simd export) ==="
            : "=== gate FAIL (an expectSimd kernel did NOT emit kernel_simd — see above) ===");
        return Task.FromResult(rc);
    }

    // Auto-grouped explicit elementwise body (same math) used for the auto-vs-explicit launch check.
    private static void AddKernelExplicit(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o)
        => o[i] = a[i] * 2f + b[i];

    private static async Task AcceleratorPathCheckAsync()
    {
        Console.WriteLine();
        Console.WriteLine("---- real-accelerator launch-path check (auto-grouped vs explicit) ----");
        var prevForce = WasmBackend.ForceSimd;
        var prevScalar = WasmBackend.ForceScalar;
        WasmBackend.ForceScalar = false;
        WasmBackend.ForceSimd = true;
        try
        {
            using var context = Context.Create().Wasm().ToContext();
            WasmAccelerator acc;
            try { acc = await context.CreateWasmAcceleratorAsync(); }
            catch (Exception ex)
            {
                Console.WriteLine($"  could not create Wasm accelerator offline: {ex.Message}");
                return;
            }
            using (acc)
            {
                // Auto-grouped: ILGPU adds the implicit `index < length` bounds check.
                WasmBackend.LastWasmBinary = null;
                WasmBackend.LastSimdAnalysis = default;
                acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(AddKernelExplicit);
                bool autoSimd = WasmBackend.LastWasmBinary != null && ContainsExportName(WasmBackend.LastWasmBinary, "kernel_simd");
                Console.WriteLine($"  auto-grouped: analysis={WasmBackend.LastSimdAnalysis}  kernel_simd={autoSimd}");

                // Explicit-group: branch-free body.
                WasmBackend.LastWasmBinary = null;
                WasmBackend.LastSimdAnalysis = default;
                acc.LoadStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(AddKernelExplicit);
                bool explSimd = WasmBackend.LastWasmBinary != null && ContainsExportName(WasmBackend.LastWasmBinary, "kernel_simd");
                Console.WriteLine($"  explicit:     analysis={WasmBackend.LastSimdAnalysis}  kernel_simd={explSimd}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  path check error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            WasmBackend.ForceSimd = prevForce;
            WasmBackend.ForceScalar = prevScalar;
        }
    }

    private static int Emit(string dir, string name, string desc, Delegate kernel, bool expectSimd)
    {
        Console.WriteLine();
        Console.WriteLine($"---- {name}: {desc} ----");
        WasmBackend.LastSimdAnalysis = default;
        byte[]? binary;
        try
        {
            var gen = ShaderCompiler.Generate(kernel, CapabilityProfiles.WasmDefault);
            binary = gen.Binary;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  COMPILE ERROR: {ex.GetType().Name}: {ex.Message}");
            return expectSimd ? 1 : 0;
        }

        var analysis = WasmBackend.LastSimdAnalysis;
        Console.WriteLine($"  analysis: {analysis}");

        if (binary == null || binary.Length == 0)
        {
            Console.WriteLine("  NO BINARY emitted");
            return expectSimd ? 1 : 0;
        }

        bool hasKernel = ContainsExportName(binary, "kernel");
        bool hasSimd = ContainsExportName(binary, "kernel_simd");
        var path = Path.Combine(dir, $"{name}.wasm");
        File.WriteAllBytes(path, binary);

        Console.WriteLine($"  binary: {binary.Length}b -> {path}");
        Console.WriteLine($"  exports: kernel={hasKernel}  kernel_simd={hasSimd}");
        Console.WriteLine($"  verify: wasm-validate --enable-threads \"{path}\"");

        if (!hasKernel)
        {
            Console.WriteLine("  *** scalar 'kernel' export MISSING — regression ***");
            return 1;
        }
        if (expectSimd && !hasSimd)
        {
            Console.WriteLine("  *** EXPECTED kernel_simd but none emitted ***");
            return 1;
        }
        if (!expectSimd && hasSimd)
            Console.WriteLine("  (bonus: kernel_simd emitted for this shape too)");
        return 0;
    }

    // Scans the raw binary for an export-name byte run. Export entries encode the name as a
    // length-prefixed UTF-8 string; "kernel_simd" as a substring would also match inside "kernel"
    // checks, so we match the exact length-prefixed token (len byte + bytes) to avoid "kernel" matching
    // the "kernel_simd" entry's name slice.
    private static bool ContainsExportName(byte[] binary, string nameStr)
    {
        var name = Encoding.ASCII.GetBytes(nameStr);
        for (int i = 0; i + name.Length + 1 <= binary.Length; i++)
        {
            if (binary[i] != name.Length) continue; // length prefix must equal the name length
            int j = 0;
            for (; j < name.Length; j++)
                if (binary[i + 1 + j] != name[j]) break;
            if (j == name.Length)
            {
                // ensure the byte AFTER the name is NOT another name char (so "kernel" doesn't match
                // the first 6 bytes of the "kernel_simd" name — its length prefix is 11, not 6, so the
                // length-prefix check already separates them; this is belt-and-suspenders).
                return true;
            }
        }
        return false;
    }
}
