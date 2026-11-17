using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Wasm keeps a dispatch's buffers in its linear memory afterwards and writes them back only when something needs
    // them in the buffer (2026-11-17, WasmResidency + the pipeline worker's copy). Every way the host or another
    // dispatch can reach a buffer must still see every earlier kernel's results: host reads, host writes while work is
    // pending, a memset, a device copy in both directions, and dispatches that lay the memory out differently. The
    // launches mix 64-element sub-view launches (the pipelined path on Wasm) and whole-buffer launches (more items
    // than one pipelined worker takes: the serialized path). Checked against a CPU model on every backend.
    public abstract partial class BackendTestBase
    {
        static void ResidencyBumpKernel(Index1D i, ArrayView<int> v, int add) => v[i] += add;
        static void ResidencyAddIntoKernel(Index1D i, ArrayView<int> dst, ArrayView<int> src) => dst[i] += src[i];

        [TestMethod]
        public async Task BufferResidency_EveryHostAccessSeesKernelResults() => await RunTest(async accelerator =>
        {
            const int n = 1 << 17;   // > 65536 items: whole-buffer launches take Wasm's serialized path
            var bump = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int>(ResidencyBumpKernel);
            var addInto = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(ResidencyAddIntoKernel);
            var wasm = accelerator as SpawnDev.ILGPU.Wasm.WasmAccelerator;
            long hitsBefore = SpawnDev.ILGPU.Wasm.WasmAccelerator.BufferResidencyHits;

            var pool = new int[n];
            var aux = new int[n];
            for (int i = 0; i < n; i++) { pool[i] = i % 1000; aux[i] = 3 * (i % 17); }
            using var dPool = accelerator.Allocate1D(pool);
            using var dAux = accelerator.Allocate1D(aux);

            async Task Check(string step)
            {
                var gotPool = await dPool.CopyToHostAsync<int>();
                var gotAux = await dAux.CopyToHostAsync<int>();
                // every wrong range, so a failure shows which launch or write was lost
                var bad = new System.Text.StringBuilder();
                int wrong = 0;
                foreach (var (name, got, want) in new[] { ("pool", gotPool, pool), ("aux", gotAux, aux) })
                {
                    int start = -1;
                    for (int i = 0; i <= n; i++)
                    {
                        bool w = i < n && got[i] != want[i];
                        if (w) wrong++;
                        if (w && start < 0) start = i;
                        if (!w && start >= 0)
                        {
                            if (bad.Length < 600) bad.Append($" {name}[{start}..{i - 1}] (first: {got[start]} vs {want[start]})");
                            start = -1;
                        }
                    }
                }
                if (wrong > 0) throw new Exception($"{step}: {wrong} wrong:{bad}");
            }
            void Small(int offset, int add)
            {
                bump(64, dPool.View.SubView(offset, 64), add);
                for (int i = 0; i < 64; i++) pool[offset + i] += add;
            }
            void Big(int add)
            {
                bump(n, dPool.View, add);
                for (int i = 0; i < n; i++) pool[i] += add;
            }

            // 1. back-to-back launches of both kinds, then a host read
            for (int k = 0; k < 24; k++) Small((k * 977) % (n - 64), k + 1);
            for (int k = 0; k < 4; k++) Big(2);
            for (int k = 0; k < 6; k++) Small((k * 4099) % (n - 64), 7);
            await Check("after the first launches");

            // 2. more launches on top of what the host just read (the held ranges are clean now)
            for (int k = 0; k < 3; k++) Big(5);
            Small(100, 11);
            await Check("launches after a host read");

            // 3. a host write while launches are pending, then launches that read it
            Big(1);
            Small(200, 3);
            var fresh = new int[n];
            for (int i = 0; i < n; i++) fresh[i] = (i * 7) % 501;
            dPool.View.CopyFromCPU(fresh);
            Array.Copy(fresh, pool, n);
            Big(4);
            Small(300, 9);
            await Check("a host write between launches");

            // 3b. a PARTIAL host write right after launches: their results outside the written range must survive.
            // After a whole-buffer launch (Wasm: the serialized path holds the buffer) ...
            var patch = new int[100];
            for (int i = 0; i < patch.Length; i++) patch[i] = 90000 + i;
            Big(2);
            dPool.View.SubView(1000, patch.Length).CopyFromCPU(patch);
            Array.Copy(patch, 0, pool, 1000, patch.Length);
            Big(1);
            await Check("a partial host write after a whole-buffer launch");
            // ... and after small launches (Wasm: the pipeline worker holds it)
            Small(700, 4);
            Small(1500, 2);
            dPool.View.SubView(1200, patch.Length).CopyFromCPU(patch);
            Array.Copy(patch, 0, pool, 1200, patch.Length);
            Small(800, 1);
            await Check("a partial host write after small launches");
            Big(3);
            await accelerator.SynchronizeAsync();
            dPool.View.SubView(5000, patch.Length).CopyFromCPU(patch);   // nothing pending: written at once
            Array.Copy(patch, 0, pool, 5000, patch.Length);
            Small(5000, 1);
            await Check("a partial host write after SynchronizeAsync");

            // 4. a memset between launches
            Big(1);
            dAux.MemSetToZero();
            Array.Clear(aux);
            addInto(n, dAux.View, dPool.View);
            for (int i = 0; i < n; i++) aux[i] += pool[i];
            await Check("a memset between launches");

            // 5. device copies in both directions between launches
            Big(6);
            dAux.View.CopyFrom(dPool.View);
            Array.Copy(pool, aux, n);
            bump(n, dAux.View, 2);
            for (int i = 0; i < n; i++) aux[i] += 2;
            dPool.View.CopyFrom(dAux.View);
            Array.Copy(aux, pool, n);
            Small(400, 1);
            await Check("device copies between launches");

            // 6. dispatches that lay the memory out differently (argument order swapped), each reading the other's result
            for (int k = 0; k < 4; k++)
            {
                addInto(n, dAux.View, dPool.View);
                for (int i = 0; i < n; i++) aux[i] += pool[i];
                addInto(n, dPool.View, dAux.View);
                for (int i = 0; i < n; i++) pool[i] += aux[i];
                Small(500 + k, 1);
            }
            await Check("alternating layouts");

            if (wasm != null)
            {
                // the serialized path must have reused ranges in place (step 1 and 2's back-to-back Big launches)
                if (SpawnDev.ILGPU.Wasm.WasmAccelerator.EnableBufferResidency
                    && SpawnDev.ILGPU.Wasm.WasmAccelerator.BufferResidencyHits == hitsBefore)
                    throw new Exception("no copy-in was skipped: the residency path did not run");
                // code that reads the SharedArrayBuffer directly after SynchronizeAsync sees the results
                Big(3);
                await accelerator.SynchronizeAsync();
                var buffer = (SpawnDev.ILGPU.Wasm.WasmMemoryBuffer)dPool.Buffer;
                using var direct = new SpawnDev.SpawnJS.JSObjects.Int32Array(buffer.SharedBuffer, 0, n);
                var got = direct.ToArray();
                for (int i = 0; i < n; i++)
                    if (got[i] != pool[i]) throw new Exception($"direct SharedBuffer read after SynchronizeAsync: [{i}] = {got[i]}, expected {pool[i]}");
            }
        });
    }
}
