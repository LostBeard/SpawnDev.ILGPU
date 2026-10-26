using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A store goes to ITS address: elements a kernel does not write keep their values.
    //
    // WebGL writes outputs through transform feedback, one varying per store site, copied back to the vertex's own
    // element; the GLSL computed the store's address and dropped it (Plans/webgl-store-addresses-2026-10-26.md). So a
    // store to `buf[k + n]` landed at buf[n] (SpawnDev.Codecs DecorrelateKernel: samples[0], never written, came back
    // changed), and a thread that skipped a conditional store still overwrote its own element. Every other backend
    // writes the real address. These run on every backend against a CPU oracle.
    public abstract partial class BackendTestBase
    {
        // Geordi's DecorrelateKernel shape: thread n reads buf[n] and buf[k + n] and writes ONLY buf[k + n].
        static void StoreAddress_OffSlotKernel(Index1D idx, ArrayView<int> buf, int k)
        {
            buf[k + idx] = buf[idx] - buf[k + idx];
        }

        // Every third thread stores; the others store nothing.
        static void StoreAddress_ConditionalKernel(Index1D idx, ArrayView<int> output, ArrayView<int> input)
        {
            if (idx % 3 == 0)
                output[idx] = input[idx] * 2 + 1;
        }

        // One store site, `count` elements per thread (a run-time trip count, so the loop is not unrolled into sites).
        static void StoreAddress_OneSiteManyElementsKernel(Index1D idx, ArrayView<int> output, int count)
        {
            for (int k = 0; k < count; k++)
                output[idx * count + k] = idx * 100 + k;
        }

        [TestMethod]
        public async Task StoreAddress_OneSiteManyElements_RunsOrIsRefusedOnWebGL() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 32, count = 3;
            using var dOut = accelerator.Allocate1D<int>(n * count);
            try
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(StoreAddress_OneSiteManyElementsKernel)((Index1D)n, dOut.View, count);
                await accelerator.SynchronizeAsync();
                var got = await dOut.CopyToHostAsync<int>();
                if (accelerator is SpawnDev.ILGPU.WebGL.WebGLAccelerator)
                    throw new Exception("WebGL ran a store site that writes several elements per thread; it must refuse it (it captures one per site)");
                for (int i = 0; i < n; i++)
                    for (int k = 0; k < count; k++)
                        if (got[i * count + k] != i * 100 + k)
                            throw new Exception($"output[{i * count + k}] = {got[i * count + k]}, expected {i * 100 + k}");
            }
            catch (Exception ex) when (accelerator is SpawnDev.ILGPU.WebGL.WebGLAccelerator
                && !ex.Message.Contains("it must refuse it"))
            {
                bool refused = false;
                for (var e = ex; e != null; e = e.InnerException)
                    if (e.Message.Contains("wrote two different elements")) refused = true;
                if (!refused) throw;
                // The refused dispatch must not poison the accelerator: WebGL kept the faulted task in its pending
                // list, and every later SynchronizeAsync threw this same error again.
                var input = new int[n];
                for (int i = 0; i < n; i++) input[i] = i * 3;
                using var dIn = accelerator.Allocate1D(input);
                using var dNext = accelerator.Allocate1D(new int[n]);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(StoreAddress_ConditionalKernel)((Index1D)n, dNext.View, dIn.View);
                await accelerator.SynchronizeAsync();
                var next = await dNext.CopyToHostAsync<int>();
                for (int i = 0; i < n; i += 3)
                    if (next[i] != input[i] * 2 + 1)
                        throw new Exception($"the dispatch after a refused one: output[{i}] = {next[i]}, expected {input[i] * 2 + 1}");
            }
        });

        [TestMethod]
        public async Task StoreAddress_OffSlotStore_WritesOnlyItsElement() => await RunEmulatedTest(async accelerator =>
        {
            const int k = 64;
            var data = new int[2 * k];
            for (int i = 0; i < data.Length; i++) data[i] = i * 13 - 400;
            using var dBuf = accelerator.Allocate1D(data);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(StoreAddress_OffSlotKernel)((Index1D)k, dBuf.View, k);
            await accelerator.SynchronizeAsync();
            var got = await dBuf.CopyToHostAsync<int>();
            for (int i = 0; i < k; i++)
                if (got[i] != data[i])
                    throw new Exception($"buf[{i}] (never written) = {got[i]}, expected it unchanged at {data[i]}");
            for (int i = 0; i < k; i++)
                if (got[k + i] != data[i] - data[k + i])
                    throw new Exception($"buf[{k + i}] = {got[k + i]}, expected {data[i] - data[k + i]}");
        });

        [TestMethod]
        public async Task StoreAddress_ConditionalStore_LeavesOtherElements() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 96;
            var input = new int[n];
            var initial = new int[n];
            for (int i = 0; i < n; i++) { input[i] = i * 5 - 30; initial[i] = 7000 + i; }
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D(initial);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(StoreAddress_ConditionalKernel)((Index1D)n, dOut.View, dIn.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int want = i % 3 == 0 ? input[i] * 2 + 1 : initial[i];
                if (got[i] != want)
                    throw new Exception($"output[{i}] = {got[i]}, expected {want}{(i % 3 == 0 ? "" : " (not stored: must keep its value)")}");
            }
        });
    }
}
