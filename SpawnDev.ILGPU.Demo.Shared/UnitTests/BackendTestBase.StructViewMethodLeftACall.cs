using System.Runtime.CompilerServices;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// An instance method of a struct that holds a view, left a call by the Inliner (here NoInlining; in practice a call past
/// the kernel's cumulative inlining budget). Through SSA construction the call takes the struct's address (its `this`), so
/// the struct stays an alloca; WebGPU's kernel transformer then inlines the call (InlineAddressParameterCalls: a helper
/// taking an address cannot be a WGSL function) and, with no SSA pass after it, the generator wrote the struct as a WGSL
/// var whose view field was an i32, then indexed that i32: naga "Invalid sub-access into type", Tint rejects the shader.
/// Found 2026-10-29 in SpawnDev.Codecs' CELT band decode (CeltModeGpu.EBandsOf on a packed-table struct, past the budget).
/// Red-check: the WGSL of this kernel failed naga before the fix.
/// </summary>
public abstract partial class BackendTestBase
{
    /// <summary>A view plus a base offset, read through an instance method (the shape of a packed table struct).</summary>
    public struct ViewTableMethod
    {
        /// <summary>The table.</summary>
        public ArrayView<int> Data;
        /// <summary>Where this table starts in <see cref="Data"/>.</summary>
        public int Base;
        /// <summary>Element <paramref name="i"/> of the table.</summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Get(int i) => Data[Base + i];
    }

    private static void StructViewMethodCallKernel(Index1D i, ViewTableMethod t, ArrayView<int> o)
    {
        o[i] = t.Get(i & 7) * 3 + t.Get((i + 1) & 7);
    }

    [TestMethod]
    public async Task StructViewMethod_LeftACall_MatchesHost() => await RunTest(async accelerator =>
    {
        const int n = 64, baseAt = 5;
        var table = new int[baseAt + 8];
        for (int k = 0; k < table.Length; k++) table[k] = 1000 * k + 17;
        using var dTable = accelerator.Allocate1D(table);
        using var dO = accelerator.Allocate1D<int>(n);
        var t = new ViewTableMethod { Data = dTable.View, Base = baseAt };
        accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewTableMethod, ArrayView<int>>(StructViewMethodCallKernel)(n, t, dO.View);
        await accelerator.SynchronizeAsync();
        var got = await dO.CopyToHostAsync();
        for (int k = 0; k < n; k++)
        {
            int want = table[baseAt + (k & 7)] * 3 + table[baseAt + ((k + 1) & 7)];
            if (got[k] != want) throw new Exception($"thread {k}: {got[k]}, expected {want}");
        }
    });
}
