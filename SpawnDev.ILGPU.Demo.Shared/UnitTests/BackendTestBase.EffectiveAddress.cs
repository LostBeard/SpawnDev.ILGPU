using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part: a view's effective address, read on the host and widened to long.
    //
    // In the browser's .NET runtime (the Mono interpreter on wasm32) `(long)` of an address made by Unsafe.AsPointer is
    // wrong: the interpreter's conv.i8 of a managed pointer on 32-bit emits its conversion without operands and leaves the
    // value typed as a 4-byte pointer (transform.c, CEE_CONV_I8 / STACK_TYPE_MP, .NET 10.0.14 and main, 2026-11-19).
    // ILGPU's LoadEffectiveAddressAsPtr was `new IntPtr(Unsafe.AsPointer(...))`, so once a caller inlined it (the interpreter
    // inlines at tier-up, after the code gets hot):
    //  - read as 8 bytes, the result had the right low word and a garbage high word (SpawnDev.Codecs' sub-view copies threw
    //    "Target copy range [30064771072, ...)" = 7 * 2^32, 5.4.39's WasmMemoryBuffer fix);
    //  - stored into a long local, the runtime ABORTED ("[MONO] .../interp/transform.c:1132", g_error "Store local stack
    //    type mismatch"): the page's .NET runtime is dead, every later test "never ran".
    // LoadEffectiveAddressAsPtr is now integer arithmetic on the buffer's native pointer (ArrayView.cs), never a managed
    // pointer. Red / green 2026-11-19 on the WebGPU + Wasm lanes: the loop below aborted the runtime before the change and
    // runs in ~15 ms after it, while the plain BCL pattern (Unsafe.AsPointer then (long)) still returns 0x0000003200000000
    // for 0 in the same page. Host code only: the accelerator only provides the views.
    // Upstream: dotnet/runtime#137923; repro github.com/LostBeard/dotnet-wasm-aspointer-long-test.
    public abstract partial class BackendTestBase
    {
        [TestMethod(Timeout = 60000)]
        public async Task EffectiveAddress_WidenedToLong_HotLoop_MatchesIndex() => await RunTest(async accelerator =>
        {
            const int n = 20736;
            using var buffer = accelerator.Allocate1D<int>(n);
            using var bytes = accelerator.Allocate1D<byte>(n);
            await Task.Yield();
            ArrayView<int> view = buffer.View;
            ArrayView<byte> byteView = bytes.View;
            long intBase = (long)view.LoadEffectiveAddressAsPtr();
            long byteBase = (long)byteView.LoadEffectiveAddressAsPtr();
            for (int k = 0; k < 20000; k++)
            {
                int idx = (k * 7) % n;
                long a = (long)view.SubView(idx, 1).LoadEffectiveAddressAsPtr();
                if (a - intBase != idx * 4L)
                    throw new Exception($"{accelerator.AcceleratorType}: int view, iteration {k}, index {idx}: (long) address " +
                        $"{a} (0x{a:X16}) - base {intBase} != {idx * 4L}");
                long b = byteView.SubView(idx, 1).LoadEffectiveAddressAsPtr().ToInt64();
                if (b - byteBase != idx)
                    throw new Exception($"{accelerator.AcceleratorType}: byte view, iteration {k}, index {idx}: ToInt64() address " +
                        $"{b} (0x{b:X16}) - base {byteBase} != {idx}");
            }
        });
    }
}
