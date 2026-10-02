using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Scalar arenas + bind-group reuse (WebGPU record batching): WebGPUBackend.BatchScalarArena puts every per-dispatch
    // scalar binding of a batch into 256-byte slots of two arena buffers (read: packed scalars + view strides;
    // read_write: struct scalars), uploaded in one writeBuffer per arena; WebGPUBackend.BatchBindGroupReuse caches the
    // JS bind groups. These tests dispatch far more kernels than an arena starts with, WITHOUT flushing, so a batch
    // overflows and regrows mid-run, and then run the same sequence again with NEW values, which must hit the bind-group
    // cache and still read the new scalars (a stale slot or a wrong slot offset shows up as a wrong value). They run on
    // every backend (the kernels are plain); the arena/counter assertions are WebGPU-only.
    public abstract partial class BackendTestBase
    {
        static void ScalarArena_PackedKernel(Index1D idx, ArrayView<int> output, int slot, int value)
        {
            output[slot] = value + idx;
        }

        public struct ScalarArenaParams
        {
            public int Slot;
            public int Value;
            public float Scale;
        }

        static void ScalarArena_StructKernel(Index1D idx, ArrayView<float> output, ScalarArenaParams p)
        {
            output[p.Slot] = p.Value * p.Scale + idx;
        }

        static void ScalarArena_StrideKernel(Index1D idx, ArrayView2D<int, Stride2D.DenseX> output, int row, int value)
        {
            output[idx, row] = value + idx * 1000;
        }

        [TestMethod]
        public async Task ScalarArena_ManyDispatchesOneBatch_OverflowAndReuse() => await RunEmulatedTest(async accelerator =>
        {
            // WebGPU record batching only. (The kernels scatter - output[slot] from a 1-thread dispatch - which WebGL's
            // render-to-texture kernels cannot do.)
            if (accelerator is not WebGPUAccelerator) throw new UnsupportedTestException("WebGPU record batching only");
            const int Packed = 1500;    // > the read arena's first capacity (512 slots): overflows + regrows mid-batch
            const int Structs = 300;    // > the read_write arena's first capacity (64 slots)
            const int Rows = 600, Cols = 4;
            var packedK = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int>(ScalarArena_PackedKernel);
            var structK = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ScalarArenaParams>(ScalarArena_StructKernel);
            var strideK = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView2D<int, Stride2D.DenseX>, int, int>(ScalarArena_StrideKernel);

            using var packedOut = accelerator.Allocate1D<int>(Packed);
            using var structOut = accelerator.Allocate1D<float>(Structs);
            using var strideOut = accelerator.Allocate2DDenseX<int>(new LongIndex2D(Cols, Rows));

            bool webgpu = accelerator is WebGPUAccelerator;
            long readBefore = WebGPUBackend.ScalarArenaSlotsUsed[0], rwBefore = WebGPUBackend.ScalarArenaSlotsUsed[1];
            long overflowBefore = WebGPUBackend.ScalarArenaOverflows;
            (long Hits, long Misses) bgBefore = (0, 0);

            // Passes over the SAME dispatch sequence (same buffers, same order) with different values. The first ones
            // overflow and regrow the arenas (each overflow submits early, so their slot assignment differs pass to pass);
            // a pass with NO overflow ran as one batch from slot 0, so the pass after it binds exactly the same slots and
            // its bind groups come from the cache - and must still see the new scalars. That pass is the measured one.
            const int MaxPasses = 10;
            int passes = 0;
            bool measured = !webgpu, measuring = false;
            for (int pass = 0; pass < MaxPasses && !measured; pass++)
            {
                passes++;
                int bias = pass * 100_000;
                long overflowsAtStart = WebGPUBackend.ScalarArenaOverflows;
                if (measuring) bgBefore = WebGPUBackend.BatchBindGroupCounters;
                for (int i = 0; i < Packed; i++) packedK((Index1D)1, packedOut.View, i, i * 7 + 3 + bias);
                for (int i = 0; i < Structs; i++) structK((Index1D)1, structOut.View, new ScalarArenaParams { Slot = i, Value = i + bias, Scale = 0.5f });
                for (int r = 0; r < Rows; r++) strideK((Index1D)Cols, strideOut.View, r, r * 3 + bias);
                await accelerator.SynchronizeAsync();

                var packed = await packedOut.CopyToHostAsync<int>();
                for (int i = 0; i < Packed; i++)
                    if (packed[i] != i * 7 + 3 + bias)
                        throw new Exception($"pass {pass}: packed-scalar dispatch {i} wrote {packed[i]}, expected {i * 7 + 3 + bias} (wrong or stale arena slot)");
                var structs = await structOut.CopyToHostAsync<float>();
                for (int i = 0; i < Structs; i++)
                    if (structs[i] != (i + bias) * 0.5f)
                        throw new Exception($"pass {pass}: struct-scalar dispatch {i} wrote {structs[i]}, expected {(i + bias) * 0.5f} (wrong or stale read_write arena slot)");
                var strided = await strideOut.View.BaseView.CopyToHostAsync<int>();
                for (int r = 0; r < Rows; r++)
                    for (int c = 0; c < Cols; c++)
                    {
                        int got = strided[r * Cols + c], want = r * 3 + bias + c * 1000;
                        if (got != want)
                            throw new Exception($"pass {pass}: strided dispatch row {r} col {c} = {got}, expected {want} (wrong stride slot)");
                    }
                if (measuring) measured = true;
                else if (webgpu && WebGPUBackend.ScalarArenaOverflows == overflowsAtStart) measuring = true;
            }

            if (webgpu && WebGPUBackend.EnableDispatchBatching && WebGPUBackend.BatchScalarArena)
            {
                long readUsed = WebGPUBackend.ScalarArenaSlotsUsed[0] - readBefore, rwUsed = WebGPUBackend.ScalarArenaSlotsUsed[1] - rwBefore;
                if (readUsed < passes * (Packed + Rows))
                    throw new Exception($"read arena: {readUsed} slots used, expected >= {passes * (Packed + Rows)} - packed/stride scalars did not go through the arena (batching off?)");
                if (rwUsed < passes * Structs)
                    throw new Exception($"read_write arena: {rwUsed} slots used, expected >= {passes * Structs} - struct scalars did not go through the arena");
                if (!measured)
                    throw new Exception($"the arenas were still regrowing after {MaxPasses} passes - they should settle after a few");
                if (WebGPUBackend.ScalarArenaOverflows == overflowBefore)
                    throw new Exception("no arena overflow - the test no longer exercises the mid-batch regrow (raise Packed/Structs)");
                if (WebGPUBackend.BatchBindGroupReuse && measured)
                {
                    var bg = WebGPUBackend.BatchBindGroupCounters;
                    long hits = bg.Hits - bgBefore.Hits, misses = bg.Misses - bgBefore.Misses;
                    if (hits < (Packed + Structs + Rows) * 9 / 10)
                    {
                        using var plan = SpawnDev.SpawnJS.SpawnJSRuntime.Instance.GlobalThis?.JSRef?.Get<SpawnDev.SpawnJS.SpawnJSObjectReference?>("ilgpuWebGPUPlan");
                        throw new Exception($"bind-group reuse: last pass {hits} hits / {misses} misses over {Packed + Structs + Rows} dispatches identical to the previous pass's - expected nearly all hits "
                            + $"(cache drops {plan?.Get<double>("bgDrops")}, last miss key {plan?.Get<string>("bgLastKey")}, overflows {WebGPUBackend.ScalarArenaOverflows - overflowBefore})");
                    }
                }
            }
        });
    }
}
