using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Scalar-buffer pooling (WebGPU): every dispatch with scalar kernel parameters needs a 256-byte
    // GPU buffer to hold the packed `_scalar_params`. WebGPUBackend.EnableBufferPooling decides
    // whether that buffer is RECYCLED after its batch is submitted or CREATED AND DESTROYED per
    // dispatch.
    //
    // 🔴 WHY THIS TEST EXISTS. Pooling shipped disabled, with this reason on the property:
    //
    //     "WARNING: Disabled by default because WebGPU Queue.Submit is asynchronous. Pooled buffers
    //      may be reused before the GPU finishes reading from them."
    //
    // Nobody ever measured that, and the cost of believing it is large: MEASURED 2026-09-15, one
    // Kokoro TTS pass on WebGPU created and destroyed 15,450 scalar buffers
    // (WebGPUAccelerator.ScalarDestroyedByPoolingOff), all 256 bytes, all identical in shape - driver
    // allocation work on the critical path of every single dispatch, and the host side is what makes
    // a new-length utterance slower than realtime.
    //
    // The stated hazard is real ONLY if queue.writeBuffer can land on a buffer that an ALREADY
    // SUBMITTED dispatch has not finished reading. Buffers are returned to the pool in
    // WebGPUStream.FlushPending, immediately AFTER Queue.Submit - never before - so the sequence the
    // warning describes is:
    //
    //     writeBuffer(A, scalars#1), submit(dispatch#1), [A returned, A rented again],
    //     writeBuffer(A, scalars#2), submit(dispatch#2)
    //
    // If writeBuffer is a QUEUE OPERATION it is ordered behind submit(dispatch#1) and dispatch#1 sees
    // scalars#1. If it is an immediate poke at GPU memory it can overwrite A while dispatch#1 is
    // still executing and dispatch#1 silently computes with scalars#2 - the same class of corruption
    // as BindGroupCache_BatchedDispatches_DoNotShareStaleScalars, but caused by the pool instead of
    // the cache.
    //
    // ⚠️ This test does NOT take the spec's word for it either way. It runs the identical dispatch
    // sequence twice - once with pooling OFF (the control: a fresh buffer per dispatch, which cannot
    // race by construction) and once with pooling ON - and requires the two results to be
    // BIT-IDENTICAL. Same kernel, same order, same accumulate, so any difference at all is the pool
    // handing out a buffer that was still being read.
    public abstract partial class BackendTestBase
    {
        // ACCUMULATES, so every round in the sequence leaves a trace in the output. With a plain
        // assignment the last round would overwrite the others and a corrupted early round would be
        // invisible - the failure this test exists to catch would pass.
        static void ScalarPool_AccumScaleAddKernel(
            Index1D idx, ArrayView<float> input, ArrayView<float> output, float mul, float add)
        {
            output[idx] += input[idx] * mul + add;
        }

        // 🔴 WEBGPU-ONLY, AND NOT AS A WAY TO DODGE A RED. The scalar pool IS a WebGPU backend feature -
        // WebGPUBackend.EnableBufferPooling, WebGPUAccelerator's per-device pool - so on any other backend
        // these tests toggle a flag nothing reads and then verify the same kernel twice. MEASURED on the
        // full six-backend sweep 2026-09-15: they passed vacuously on CPU/CUDA/OpenCL/Wasm and FAILED on
        // WebGL, because GpuTestVerify.CompareBuffers uses Atomic.Max and WebGL has no atomics at all.
        // Scoping a test to the backend whose feature it tests is not gating a failure; running it
        // everywhere was the bug, and it hid four vacuous passes behind two honest reds.
        private static void RequireWebGPUForScalarPool(Accelerator accelerator)
        {
            if (accelerator is not WebGPUAccelerator)
                throw new UnsupportedTestException(
                    "the scalar-params buffer pool is a WebGPU backend feature (WebGPUBackend."
                  + "EnableBufferPooling); on other backends this would assert nothing");
        }

        [TestMethod]
        public async Task ScalarBufferPool_RecycledAcrossSubmits_MatchesUnpooled() => await RunEmulatedTest(async accelerator =>
        {
            RequireWebGPUForScalarPool(accelerator);

            // Big enough that a dispatch is still executing on the GPU while the host is already
            // renting the recycled buffer and writing the NEXT round's scalars into it. A 256-element
            // dispatch finishes before the host gets back around and would hide the race.
            const int N = 1 << 21;   // 2M floats, 8 MB per buffer
            const int Rounds = 16;

            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = (i % 1024) * 0.03125f - 16f;

            // Distinct scalars per round. If any round's dispatch reads a LATER round's scalars, the
            // accumulated sum changes - there is no pair of rounds whose swap cancels out.
            var muls = new float[Rounds];
            var adds = new float[Rounds];
            for (int r = 0; r < Rounds; r++) { muls[r] = 1f + r * 0.5f; adds[r] = r * 3f - 7f; }

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                ScalarPool_AccumScaleAddKernel);

            // One round per submit. Flush() - NOT SynchronizeAsync() - is the whole point: Flush
            // submits without waiting, which is what returns the scalar buffer to the pool while its
            // dispatch may still be running. A Synchronize here would wait for completion and make
            // recycling trivially safe, i.e. it would test nothing.
            async Task RunSequenceAsync(MemoryBuffer1D<float, Stride1D.Dense> input,
                                        MemoryBuffer1D<float, Stride1D.Dense> output)
            {
                output.View.CopyFromCPU(new float[N]);
                for (int r = 0; r < Rounds; r++)
                {
                    k((Index1D)N, input.View, output.View, muls[r], adds[r]);
                    accelerator.Flush();
                }
                await accelerator.SynchronizeAsync();
            }

            var webgpu = accelerator as WebGPUAccelerator;
            bool poolingWas = WebGPUBackend.EnableBufferPooling;
            try
            {
                using var input = accelerator.Allocate1D(src);
                using var pooledOut = accelerator.Allocate1D<float>(N);
                using var unpooledOut = accelerator.Allocate1D<float>(N);

                // CONTROL: pooling OFF. A fresh 256-byte buffer per dispatch cannot be recycled, so
                // no dispatch can have its scalars rewritten under it. This is the reference.
                WebGPUBackend.EnableBufferPooling = false;
                await RunSequenceAsync(input, unpooledOut);

                // UNDER TEST: pooling ON, same sequence, same buffers, same order.
                WebGPUBackend.EnableBufferPooling = true;
                long destroysBefore = WebGPUAccelerator.ScalarDestroyedByPoolingOff
                                    + WebGPUAccelerator.ScalarDestroyedByPoolOverflow;
                await RunSequenceAsync(input, pooledOut);
                long destroysAfter = WebGPUAccelerator.ScalarDestroyedByPoolingOff
                                   + WebGPUAccelerator.ScalarDestroyedByPoolOverflow;

                // GPU-side compare - reads back 2 floats, not 2M (Rule 4: bulk data stays on the GPU
                // in tests too).
                var (meanError, maxError) = await GpuTestVerify.CompareBuffers(
                    accelerator, pooledOut, unpooledOut, N);

                if (maxError != 0f)
                {
                    // Name the specific corruption if the delta matches it, so the failure message is
                    // a diagnosis and not just a number.
                    throw new Exception(
                        $"ScalarBufferPool: pooled result differs from unpooled at max |delta| {maxError:E3} " +
                        $"(mean {meanError:E3}) over {Rounds} single-dispatch submits. The two runs are the " +
                        "SAME kernel, buffers, scalars and order - the ONLY difference is that the pooled run " +
                        "recycles the 256-byte _scalar_params buffer after submit. A non-zero delta means " +
                        "queue.writeBuffer overwrote a scalar buffer that an already-submitted dispatch had " +
                        "not finished reading, so EnableBufferPooling is genuinely unsafe and the warning on " +
                        "the property is correct. Keep it disabled and find another way to kill the per-" +
                        "dispatch buffer churn.");
                }

                // The pooled run must actually have POOLED. Without this the test passes vacuously if
                // the flag never reaches the allocation path (a stale build, a second gate, an
                // accelerator that ignores it) - identical results for the boring reason.
                if (webgpu != null)
                {
                    if (destroysAfter != destroysBefore)
                        throw new Exception(
                            $"ScalarBufferPool: {destroysAfter - destroysBefore} scalar buffers were still " +
                            "DESTROYED during the pooled run (poolingOff + poolOverflow counters). Pooling " +
                            "did not engage, so the comparison above proved nothing about it.");

                    // Rounds dispatches, one submit each, recycling one buffer -> the pool must be
                    // holding something at the end. Zero means nothing was ever returned to it.
                    if (WebGPUAccelerator.PooledScalarBufferCount == 0)
                        throw new Exception(
                            "ScalarBufferPool: the pool is EMPTY after the pooled run, so no buffer was ever " +
                            "returned to it and none was ever reused. The run under test was not a pooled run.");
                }
            }
            finally
            {
                await accelerator.SynchronizeAsync();
                WebGPUBackend.EnableBufferPooling = poolingWas;
            }
        });

        // The production shape. ScalarBufferPool_RecycledAcrossSubmits_MatchesUnpooled submits after
        // every single dispatch, which is the TIGHTEST recycle (the buffer goes back to the pool while
        // its own dispatch is almost certainly still running) but NOT the shape SpawnDev.ILGPU.ML
        // actually runs: Kokoro is ~1,850 nodes with THREE drains, so hundreds of dispatches are
        // recorded into one command encoder and then hundreds of scalar buffers are returned to the
        // pool at once - and the NEXT batch rents those same buffers back.
        //
        // That batch boundary is where a pool interacts with the two things already known to bite here:
        // buffers returned only at FlushPending, and a bind-group cache that owns its own scalar buffer.
        // Same control as above - pooling off vs on, bit-identical required.
        [TestMethod]
        public async Task ScalarBufferPool_BatchedDispatches_MatchesUnpooled() => await RunEmulatedTest(async accelerator =>
        {
            RequireWebGPUForScalarPool(accelerator);

            const int N = 1 << 18;          // 256K floats
            const int Batches = 4;
            const int PerBatch = 24;        // 96 dispatches, 4 submits: the many-per-flush shape

            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = (i % 512) * 0.0625f - 8f;

            var muls = new float[Batches * PerBatch];
            var adds = new float[Batches * PerBatch];
            for (int d = 0; d < muls.Length; d++) { muls[d] = 0.5f + d * 0.125f; adds[d] = d * 0.25f - 5f; }

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, float, float>(
                ScalarPool_AccumScaleAddKernel);

            async Task RunSequenceAsync(MemoryBuffer1D<float, Stride1D.Dense> input,
                                        MemoryBuffer1D<float, Stride1D.Dense> output)
            {
                output.View.CopyFromCPU(new float[N]);
                int d = 0;
                for (int b = 0; b < Batches; b++)
                {
                    for (int j = 0; j < PerBatch; j++, d++)
                        k((Index1D)N, input.View, output.View, muls[d], adds[d]);   // NO flush inside
                    accelerator.Flush();   // one submit per batch -> PerBatch buffers returned at once
                }
                await accelerator.SynchronizeAsync();
            }

            var webgpu = accelerator as WebGPUAccelerator;
            bool poolingWas = WebGPUBackend.EnableBufferPooling;
            try
            {
                using var input = accelerator.Allocate1D(src);
                using var pooledOut = accelerator.Allocate1D<float>(N);
                using var unpooledOut = accelerator.Allocate1D<float>(N);

                WebGPUBackend.EnableBufferPooling = false;
                await RunSequenceAsync(input, unpooledOut);

                WebGPUBackend.EnableBufferPooling = true;
                long destroysBefore = WebGPUAccelerator.ScalarDestroyedByPoolingOff
                                    + WebGPUAccelerator.ScalarDestroyedByPoolOverflow;
                await RunSequenceAsync(input, pooledOut);
                long destroysAfter = WebGPUAccelerator.ScalarDestroyedByPoolingOff
                                   + WebGPUAccelerator.ScalarDestroyedByPoolOverflow;

                var (meanError, maxError) = await GpuTestVerify.CompareBuffers(
                    accelerator, pooledOut, unpooledOut, N);

                if (maxError != 0f)
                    throw new Exception(
                        $"ScalarBufferPool (batched): pooled result differs from unpooled at max |delta| " +
                        $"{maxError:E3} (mean {meanError:E3}) over {Batches} batches of {PerBatch} dispatches. " +
                        "Recycling survives one-dispatch-per-submit but NOT the batched graph shape - a buffer " +
                        "returned at FlushPending is being rewritten while a dispatch from that batch is still " +
                        "reading it.");

                if (webgpu != null && destroysAfter != destroysBefore)
                    throw new Exception(
                        $"ScalarBufferPool (batched): {destroysAfter - destroysBefore} scalar buffers were still " +
                        $"DESTROYED during the pooled run. Either pooling did not engage, or " +
                        $"MaxPooledScalarBuffers ({WebGPUBackend.MaxPooledScalarBuffers}) is below the " +
                        $"{PerBatch} buffers one batch returns at once - in which case the overflow is destroyed " +
                        "at every flush and pooling buys nothing.");
            }
            finally
            {
                await accelerator.SynchronizeAsync();
                WebGPUBackend.EnableBufferPooling = poolingWas;
            }
        });
    }
}
