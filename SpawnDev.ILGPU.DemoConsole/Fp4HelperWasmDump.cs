using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Dumps the emitted Wasm binary for kernels that load a packed QUInt4/QInt4/Float4 view through a
/// [NoInlining] helper, to diagnose the helper-side packed-4-bit load path. Run:
///   dotnet run --project SpawnDev.ILGPU.DemoConsole -- fp4-helper-wasm
/// then: wasm2wat --enable-threads &lt;out&gt;.wasm
/// </summary>
internal static class Fp4HelperWasmDump
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int QUInt4HelperLoad(ArrayView<QUInt4> v, int i) => v[i];
    static void QUInt4ViaHelperKernel(Index1D i, ArrayView<QUInt4> packed, ArrayView<int> outI)
        => outI[i] = QUInt4HelperLoad(packed, i.X);

    [MethodImpl(MethodImplOptions.NoInlining)]
    static int QInt4HelperLoad(ArrayView<QInt4> v, int i) => v[i];
    static void QInt4ViaHelperKernel(Index1D i, ArrayView<QInt4> packed, ArrayView<int> outI)
        => outI[i] = QInt4HelperLoad(packed, i.X);

    public static async Task<int> Run()
    {
        WasmBackend.VerboseLogging = true; var context = Context.Create().Wasm().ToContext();
        WasmAccelerator acc;
        try { acc = await context.CreateWasmAcceleratorAsync(); }
        catch (Exception ex) { Console.WriteLine($"[fp4-helper-wasm] no Wasm accel: {ex.Message}"); return 2; }

        var outDir = Path.Combine(Path.GetTempPath(), "fp4_helper_wasm");
        Directory.CreateDirectory(outDir);
        Console.WriteLine($"[fp4-helper-wasm] out dir: {outDir}");

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var m = typeof(Fp4HelperWasmDump).GetMethod(nameof(QUInt4ViaHelperKernel), flags)!;
        Console.WriteLine("[fp4-helper-wasm] === Backend.Compile QUInt4ViaHelperKernel (forces helper codegen) ===");
        acc.Backend.Compile(
            ILGPU.Backends.EntryPoints.EntryPointDescription.FromImplicitlyGroupedKernel(m),
            new KernelSpecialization(256, null));
        Console.WriteLine("[fp4-helper-wasm] done.");
        return 0;
    }
}
