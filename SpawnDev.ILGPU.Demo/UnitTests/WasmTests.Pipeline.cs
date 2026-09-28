using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.UnitTests
{
    /// <summary>Struct kernel argument for the pipeline tests (public: the emitted launcher must see it).</summary>
    public struct PipeAffine
    {
        public float A;
        public float B;
    }

    // Pipelined dispatch (WasmAccelerator.EnablePipelinedDispatch, 2026-09-28): small flat dispatches are
    // posted to one worker without waiting for the ones ahead of them, and the worker does the copy-in /
    // copy-out itself. These tests check the results against a host replay AND that the path actually ran
    // (PipelinedDispatchCount), so a silent fallback to the serialized path cannot pass them.
    public partial class WasmTests
    {
        static void PipeAddKernel(Index1D i, ArrayView<float> x, float v) => x[i] = x[i] + v;
        static void PipeAffineKernel(Index1D i, ArrayView<float> x, PipeAffine s) => x[i] = x[i] * s.A + s.B;
        static void PipeShiftKernel(Index1D i, ArrayView<float> src, ArrayView<float> dst) => dst[i] = src[i] + dst[i] * 0.5f;
        static void PipeIntAddKernel(Index1D i, ArrayView<int> x, int v) => x[i] = x[i] + v;
        static void PipeIntDivKernel(Index1D i, ArrayView<int> x, int d) => x[i] = x[i] / d;

        // A barrier kernel (serialized path): reverses each 64-element block through shared memory.
        static void PipeBlockReverseKernel(ArrayView<float> x)
        {
            var sh = SharedMemory.Allocate<float>(64);
            int t = Group.IdxX;
            int g = Grid.IdxX * 64 + t;
            sh[t] = x[g];
            Group.Barrier();
            x[g] = sh[63 - t];
        }

        [TestMethod(Timeout = 120000)]
        public async Task Wasm_Pipeline_DependentChain_MatchesHostReplay() => await RunTest(async accelerator =>
        {
            const int n = 128;
            var add = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(PipeAddKernel);
            var affine = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, PipeAffine>(PipeAffineKernel);
            var shift = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(PipeShiftKernel);
            var reverse = accelerator.LoadStreamKernel<ArrayView<float>>(PipeBlockReverseKernel);

            var hx = new float[n + 16];
            var hy = new float[n];
            for (int i = 0; i < hx.Length; i++) hx[i] = i * 0.5f;
            using var x = accelerator.Allocate1D(hx);
            using var y = accelerator.Allocate1D(hy);
            // First dispatch always takes the serialized path (module-cache bookkeeping); get it out of the way.
            add(n, x.View, 0f);
            await accelerator.SynchronizeAsync();

            long before = WasmAccelerator.PipelinedDispatchCount;
            int launches = 0;
            for (int step = 0; step < 120; step++)
            {
                add(n, x.View, 1f); for (int i = 0; i < n; i++) hx[i] += 1f;
                var s = new PipeAffine { A = 0.5f + (step % 3) * 0.25f, B = step * 0.125f };
                affine(n, x.View, s); for (int i = 0; i < n; i++) hx[i] = hx[i] * s.A + s.B;
                // Source is a SubView 16 elements in: the copy-in range starts mid-buffer.
                shift(n, x.View.SubView(16, n), y.View); for (int i = 0; i < n; i++) hy[i] = hx[i + 16] + hy[i] * 0.5f;
                launches += 3;
                if (step % 10 == 5)
                {
                    // A host write and a device copy in the middle of the queue: both go on the pipeline, and
                    // the launches after them must stay pipelined (the training-loop upload pattern).
                    var fresh = new float[8];
                    for (int i = 0; i < 8; i++) fresh[i] = step + i * 0.25f;
                    x.View.SubView(4, 8).CopyFromCPU(fresh); Array.Copy(fresh, 0, hx, 4, 8);
                    y.View.SubView(0, 16).CopyFrom(x.View.SubView(20, 16)); Array.Copy(hx, 20, hy, 0, 16);
                }
            }
            // A barrier kernel queued behind the pipeline, then more pipelined work behind it.
            reverse(new KernelConfig(n / 64, 64), y.View);
            for (int b = 0; b < n; b += 64) Array.Reverse(hy, b, 64);
            for (int step = 0; step < 40; step++)
            {
                shift(n, y.View, x.View); for (int i = 0; i < n; i++) hx[i] = hy[i] + hx[i] * 0.5f;
            }
            await accelerator.SynchronizeAsync();
            for (int step = 0; step < 40; step++)
            {
                add(n, y.View, 2f); for (int i = 0; i < n; i++) hy[i] += 2f;
                launches++;
            }
            await accelerator.SynchronizeAsync();

            var gx = await x.CopyToHostAsync<float>();
            var gy = await y.CopyToHostAsync<float>();
            for (int i = 0; i < hx.Length; i++)
                if (gx[i] != hx[i]) throw new Exception($"x[{i}] = {gx[i]}, expected {hx[i]}");
            for (int i = 0; i < n; i++)
                if (gy[i] != hy[i]) throw new Exception($"y[{i}] = {gy[i]}, expected {hy[i]}");

            long pipelined = WasmAccelerator.PipelinedDispatchCount - before;
            if (WasmAccelerator.EnablePipelinedDispatch && pipelined < launches)
                throw new Exception($"only {pipelined} of at least {launches} small launches took the pipelined path");
            Console.WriteLine($"[Pipeline] {pipelined} pipelined dispatches (at least {launches} expected)");
        });

        [TestMethod(Timeout = 120000)]
        public async Task Wasm_Pipeline_FailedDispatch_SkipsLaterAndRecovers() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var inc = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(PipeIntAddKernel);
            var div = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(PipeIntDivKernel);
            using var x = accelerator.Allocate1D<int>(n);
            inc(n, x.View, 0);
            div(n, x.View, 1);   // compile both kernels outside the failing batch
            await accelerator.SynchronizeAsync();

            long before = WasmAccelerator.PipelinedDispatchCount;
            for (int k = 0; k < 5; k++) inc(n, x.View, 1);
            div(n, x.View, 0);   // integer divide by zero traps in Wasm
            for (int k = 0; k < 5; k++) inc(n, x.View, 1);
            Exception? caught = null;
            try { await accelerator.SynchronizeAsync(); }
            catch (Exception ex) { caught = ex; }
            if (caught == null)
                throw new Exception("SynchronizeAsync did not report the trapped dispatch");
            long pipelined = WasmAccelerator.PipelinedDispatchCount - before;

            // The failure is reported once; the accelerator keeps working.
            await accelerator.SynchronizeAsync();
            var got = await x.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != 5)
                    throw new Exception($"x[{i}] = {got[i]}, expected 5: the 5 launches before the trap run, " +
                                        "the trapped one and everything queued after it do not");

            for (int k = 0; k < 10; k++) inc(n, x.View, 3);
            await accelerator.SynchronizeAsync();
            got = await x.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != 35) throw new Exception($"after recovery x[{i}] = {got[i]}, expected 35");

            if (WasmAccelerator.EnablePipelinedDispatch && pipelined < 6)
                throw new Exception($"only {pipelined} launches of the failing batch were pipelined");
            Console.WriteLine($"[Pipeline] failing batch: {pipelined} pipelined; error: {caught.Message}");
        });

        // Nobody waits here: no SynchronizeAsync, so no ping. The worker must still answer (its short timer),
        // otherwise a waiter that forgot to ping would hang instead of just being a few ms late.
        [TestMethod(Timeout = 60000)]
        public async Task Wasm_Pipeline_AnswersWithoutAWaiter() => await RunTest(async accelerator =>
        {
            const int n = 32;
            var inc = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(PipeIntAddKernel);
            using var x = accelerator.Allocate1D<int>(n);
            inc(n, x.View, 0);
            await accelerator.SynchronizeAsync();
            var wasm = (WasmAccelerator)accelerator;
            long before = WasmAccelerator.PipelinedDispatchCount;
            for (int k = 0; k < 20; k++) inc(n, x.View, 1);
            if (WasmAccelerator.EnablePipelinedDispatch && WasmAccelerator.PipelinedDispatchCount - before != 20)
                throw new Exception($"{WasmAccelerator.PipelinedDispatchCount - before} of 20 launches were pipelined");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (wasm.PipelinedInFlight > 0 && sw.ElapsedMilliseconds < 5000)
                await Task.Delay(10);
            if (wasm.PipelinedInFlight > 0)
                throw new Exception($"{wasm.PipelinedInFlight} pipelined dispatches still unanswered after 5 s with no waiter");
            Console.WriteLine($"[Pipeline] answered without a waiter after {sw.ElapsedMilliseconds} ms");
            await accelerator.SynchronizeAsync();
            var got = await x.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != 20) throw new Exception($"x[{i}] = {got[i]}, expected 20");
        });

        static void PipeFirstUse0(Index1D i, ArrayView<float> x) => x[i] = x[i] * 2f + 0f;
        static void PipeFirstUse1(Index1D i, ArrayView<float> x) => x[i] = x[i] * 3f + 1f;
        static void PipeFirstUse2(Index1D i, ArrayView<float> x) => x[i] = x[i] * 4f + 2f;
        static void PipeFirstUse3(Index1D i, ArrayView<float> x) => x[i] = x[i] * 5f + 3f;
        static void PipeFirstUse4(Index1D i, ArrayView<float> x) => x[i] = x[i] * 6f + 4f;
        static void PipeFirstUse5(Index1D i, ArrayView<float> x) => x[i] = x[i] * 7f + 5f;
        static void PipeFirstUse6(Index1D i, ArrayView<float> x) => x[i] = x[i] * 8f + 6f;
        static void PipeFirstUse7(Index1D i, ArrayView<float> x) => x[i] = x[i] * 9f + 7f;

        // Every kernel's FIRST use on the pipeline worker compiles and instantiates its module - the worker's
        // only real await - with dependent launches of an already-compiled kernel queued right behind it. If
        // the worker let a later message run during that await, the cheap launch would overtake the compile.
        [TestMethod(Timeout = 120000)]
        public async Task Wasm_Pipeline_FirstUseCompiles_StayInOrder() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var add = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(PipeAddKernel);
            var firsts = new Action<Index1D, ArrayView<float>>[]
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse0),
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse1),
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse2),
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse3),
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse4),
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse5),
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse6),
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(PipeFirstUse7),
            };
            var hx = new float[n];
            for (int i = 0; i < n; i++) hx[i] = i % 3;
            using var x = accelerator.Allocate1D(hx);
            add(n, x.View, 0f);
            await accelerator.SynchronizeAsync();

            long before = WasmAccelerator.PipelinedDispatchCount;
            for (int k = 0; k < firsts.Length; k++)
            {
                firsts[k](n, x.View); for (int i = 0; i < n; i++) hx[i] = hx[i] * (k + 2) + k;
                for (int j = 0; j < 4; j++) { add(n, x.View, 1f); for (int i = 0; i < n; i++) hx[i] += 1f; }
            }
            await accelerator.SynchronizeAsync();
            var got = await x.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != hx[i]) throw new Exception($"x[{i}] = {got[i]}, expected {hx[i]}: a launch overtook a first-use compile");
            long pipelined = WasmAccelerator.PipelinedDispatchCount - before;
            if (WasmAccelerator.EnablePipelinedDispatch && pipelined < 40)
                throw new Exception($"only {pipelined} of 40 launches were pipelined");
        });
    }
}
