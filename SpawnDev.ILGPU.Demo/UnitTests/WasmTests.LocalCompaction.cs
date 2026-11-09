using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.UnitTests
{
    // Wasm local reuse (WasmLocalCompaction, 5.4.21). The code generators give every IR value its own Wasm local, so a
    // kernel with a lot of inlined straight-line code declared more locals than V8 accepts (50,000 per function):
    // SpawnDev.Codecs' VP9 loop filter (Vp9LoopFilterKernels.WavefrontKernel) declared 68,382 and its module failed with
    // "WebAssembly.compile(): Compiling function #24 failed: local count too large" (Geordi, 2026-11-09).
    public partial class WasmTests
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint LcMix(uint x, uint k)
        {
            x ^= x >> 13;
            x *= 0x5BD1E995u + k;
            x ^= x >> 15;
            x ^= x << 7;
            x -= k * 3u;
            return x + k;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Lc1(uint x, uint k) => LcMix(LcMix(LcMix(LcMix(x, k), k + 1), k + 2), k + 3);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Lc2(uint x, uint k) => Lc1(Lc1(Lc1(Lc1(x, k), k + 4), k + 8), k + 12);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Lc3(uint x, uint k) => Lc2(Lc2(Lc2(Lc2(x, k), k + 16), k + 32), k + 48);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Lc4(uint x, uint k) => Lc3(Lc3(Lc3(Lc3(x, k), k + 64), k + 128), k + 192);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Lc5(uint x, uint k) => Lc4(Lc4(Lc4(Lc4(x, k), k + 256), k + 512), k + 768);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint Lc6(uint x, uint k) => Lc5(Lc5(Lc5(Lc5(x, k), k + 1024), k + 2048), k + 3072);

        // 4^6 = 4096 inlined LcMix calls of ~14 IR values each, one dependent chain (nothing folds or merges): ~57,000 locals
        static void LocalCompactionKernel(Index1D i, ArrayView<uint> data) => data[i] = Lc6(data[i], 7u);

        [TestMethod(Timeout = 300000)]
        public async Task Wasm_KernelOverV8LocalLimit_CompilesAndMatchesHost()
        {
            const int n = 1024;
            var input = new uint[n];
            for (int i = 0; i < n; i++) input[i] = (uint)(i * 2654435761u);
            var expected = new uint[n];
            for (int i = 0; i < n; i++) expected[i] = Lc6(input[i], 7u);

            string? info = null;
            var previous = WasmBackend.OnKernelCompiled;
            WasmBackend.OnKernelCompiled = (name, bin, i) => { info = i; previous?.Invoke(name, bin, i); };
            try
            {
                using var context = Context.Create().Wasm().ToContext();
                using var accelerator = await context.CreateWasmAcceleratorAsync();
                using var buf = accelerator.Allocate1D(input);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>>(LocalCompactionKernel);
                kernel(n, buf.View);
                await accelerator.SynchronizeAsync();
                var got = await buf.CopyToHostAsync<uint>();

                // The kernel must really be over the limit before reuse, or this test proves nothing.
                var m = Regex.Match(info ?? "", @"locals=(\d+) \(after reuse (\d+);");
                if (!m.Success)
                    throw new Exception($"No local reuse in the compile info: '{info}'");
                int before = int.Parse(m.Groups[1].Value), after = int.Parse(m.Groups[2].Value);
                if (before <= 50000)
                    throw new Exception($"The test kernel declares only {before} locals before reuse; it must exceed V8's 50,000");
                if (after >= 50000)
                    throw new Exception($"{after} locals after reuse (V8's limit is 50,000)");
                for (int i = 0; i < n; i++)
                    if (got[i] != expected[i])
                        throw new Exception($"[{i}] = {got[i]}, host {expected[i]} (locals {before} -> {after})");
                Console.WriteLine($"[Wasm-LocalCompaction] locals {before} -> {after}");
            }
            finally
            {
                WasmBackend.OnKernelCompiled = previous;
            }
        }
    }
}
