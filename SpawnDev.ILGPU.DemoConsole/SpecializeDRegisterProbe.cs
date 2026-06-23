using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;

/// <summary>
/// Probe for Tuvok's "register per-query attention" lever: does a compile-time-constant
/// head dim D make the `new float[D]` online-softmax accumulator SCALAR-REPLACE INTO
/// REGISTERS (no `.local` array) on CUDA, and up to what D?
///
/// Each kernel hardcodes a const D (the SpecializedValue&lt;int&gt; runtime path bakes the
/// SAME compile-time constant, so const-D PTX is representative). The accumulator loop is
/// the online-softmax shape: acc[d] = acc[d]*correction + weight*v[...], over a RUNTIME
/// key loop (SKV) - exactly the per-query attention inner accumulator. If the D-loop FULLY
/// unrolls, acc[0..D-1] become constant indices and the array scalar-replaces into
/// loop-carried registers across the key loop (the flash-class register accumulator).
///
/// LoopUnrolling FULL-unrolls only when tripCount &lt;= 64 AND bodyCost*tripCount &lt;= 320
/// (MaxTotalUnrolledBodyCost, calibrated for browser WGSL/Wasm shader-compile budgets).
/// This probe shows where that cap puts the register/spill cliff for the attention body.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- specialize-d-probe
/// </summary>
internal static class SpecializeDRegisterProbe
{
    // Online-softmax accumulator, each kernel with its OWN const D (compile-time constant
    // array size + loop bound, no reliance on inliner constant-folding an int param).
    // Runtime key loop (SKV) - not unrolled; the D-loop is the full-unroll candidate.
    static void Attn_D8(Index1D i, ArrayView<float> v, ArrayView<float> outF, int SKV)
    {
        const int D = 8;
        var acc = new float[D];
        for (int d = 0; d < D; d++) acc[d] = 0f;
        for (int j = 0; j < SKV; j++) { float w = 1.0f / (j + 1); for (int d = 0; d < D; d++) acc[d] = acc[d] * 0.97f + w * v[(j & 3) * D + d]; }
        float s = 0f; for (int d = 0; d < D; d++) s += acc[d]; outF[i] = s;
    }
    static void Attn_D16(Index1D i, ArrayView<float> v, ArrayView<float> outF, int SKV)
    {
        const int D = 16;
        var acc = new float[D];
        for (int d = 0; d < D; d++) acc[d] = 0f;
        for (int j = 0; j < SKV; j++) { float w = 1.0f / (j + 1); for (int d = 0; d < D; d++) acc[d] = acc[d] * 0.97f + w * v[(j & 3) * D + d]; }
        float s = 0f; for (int d = 0; d < D; d++) s += acc[d]; outF[i] = s;
    }
    static void Attn_D32(Index1D i, ArrayView<float> v, ArrayView<float> outF, int SKV)
    {
        const int D = 32;
        var acc = new float[D];
        for (int d = 0; d < D; d++) acc[d] = 0f;
        for (int j = 0; j < SKV; j++) { float w = 1.0f / (j + 1); for (int d = 0; d < D; d++) acc[d] = acc[d] * 0.97f + w * v[(j & 3) * D + d]; }
        float s = 0f; for (int d = 0; d < D; d++) s += acc[d]; outF[i] = s;
    }
    static void Attn_D64(Index1D i, ArrayView<float> v, ArrayView<float> outF, int SKV)
    {
        const int D = 64;
        var acc = new float[D];
        for (int d = 0; d < D; d++) acc[d] = 0f;
        for (int j = 0; j < SKV; j++) { float w = 1.0f / (j + 1); for (int d = 0; d < D; d++) acc[d] = acc[d] * 0.97f + w * v[(j & 3) * D + d]; }
        float s = 0f; for (int d = 0; d < D; d++) s += acc[d]; outF[i] = s;
    }
    static void Attn_D128(Index1D i, ArrayView<float> v, ArrayView<float> outF, int SKV)
    {
        const int D = 128;
        var acc = new float[D];
        for (int d = 0; d < D; d++) acc[d] = 0f;
        for (int j = 0; j < SKV; j++) { float w = 1.0f / (j + 1); for (int d = 0; d < D; d++) acc[d] = acc[d] * 0.97f + w * v[(j & 3) * D + d]; }
        float s = 0f; for (int d = 0; d < D; d++) s += acc[d]; outF[i] = s;
    }

    public static Task<int> Run()
    {
        using var context = Context.Create(b => b.Cuda());
        var dev = context.GetCudaDevice(0);
        if (dev == null) { Console.WriteLine("[specialize-d-probe] no CUDA device"); return Task.FromResult(1); }
        using var acc = dev.CreateCudaAccelerator(context);
        Console.WriteLine($"[specialize-d-probe] {acc.Name}");
        Console.WriteLine($"[specialize-d-probe] LoopUnrolling: full-unroll iff tripCount<=64 AND bodyCost*tripCount<=320");
        var outDir = Path.Combine(Path.GetTempPath(), "specialize_d_probe");
        Directory.CreateDirectory(outDir);

        foreach (var (name, mname) in new[]
        {
            ("D=8",  nameof(Attn_D8)),
            ("D=16", nameof(Attn_D16)),
            ("D=32", nameof(Attn_D32)),
            ("D=64", nameof(Attn_D64)),
            ("D=128",nameof(Attn_D128)),
        })
        {
            try
            {
                var method = typeof(SpecializeDRegisterProbe).GetMethod(mname, BindingFlags.NonPublic | BindingFlags.Static)!;
                var compiled = acc.Backend.Compile(
                    EntryPointDescription.FromImplicitlyGroupedKernel(method),
                    new KernelSpecialization(256, null));
                var ptx = (compiled as PTXCompiledKernel)?.PTXAssembly ?? "(not PTX)";
                File.WriteAllText(Path.Combine(outDir, mname + ".ptx"), ptx);

                // .local arrays + ld/st.local = the accumulator spilled to local memory
                // (NOT scalar-replaced). Pure-register = no .local array decl, no ld/st.local.
                int localDecls = CountOccurrences(ptx, ".local .align");
                int ldLocal = CountOccurrences(ptx, "ld.local");
                int stLocal = CountOccurrences(ptx, "st.local");
                bool registers = localDecls == 0 && ldLocal == 0 && stLocal == 0;
                Console.WriteLine($"  {name,-6} -> {(registers ? "REGISTERS (scalar-replaced)" : "LOCAL MEM (spilled)")}" +
                    $"  [.local decls={localDecls}, ld.local={ldLocal}, st.local={stLocal}, ptx={ptx.Length}b]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  {name,-6} -> COMPILE FAILED: {ex.GetType().Name}: {ex.Message}");
                var inner = ex.InnerException; int depth = 0;
                while (inner != null && depth < 6)
                {
                    Console.WriteLine($"        inner[{depth}] {inner.GetType().FullName}: {inner.Message}");
                    var st = (inner.StackTrace ?? "").Split('\n');
                    for (int s = 0; s < Math.Min(6, st.Length); s++) Console.WriteLine($"           {st[s].Trim()}");
                    inner = inner.InnerException; depth++;
                }
            }
        }
        Console.WriteLine($"[specialize-d-probe] PTX dumped to {outDir}");
        return Task.FromResult(0);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, idx = 0;
        while ((idx = haystack.IndexOf(needle, idx, StringComparison.Ordinal)) >= 0) { n++; idx += needle.Length; }
        return n;
    }
}
