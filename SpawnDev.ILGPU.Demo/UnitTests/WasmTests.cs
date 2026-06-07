using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.BlazorJS.Cryptography;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;

namespace SpawnDev.ILGPU.Demo.UnitTests
{
    /// <summary>
    /// Wasm backend tests. Inherits all shared tests from BackendTestBase.
    /// v4.6.0: Fiber-based phase dispatch. 182 pass / 0 fail.
    /// </summary>
    public class WasmTests : BackendTestBase
    {
        public WasmTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
        protected override string BackendName => "Wasm";

        protected override async Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
        {
            var builder = Context.Create()
                //.Optimize(OptimizationLevel.Debug) // DEBUG: test showed no effect on intermittent failure
                .EnableAlgorithms()
                .EnableWasmAlgorithms()
                .Wasm();
            var context = builder.ToContext();
            WasmBackend.VerboseLogging = false;
            var accelerator = await context.CreateWasmAcceleratorAsync();
            return (context, accelerator);
        }

        // Verifies the opt-in host-buffer race DETECTOR (WasmMemoryBuffer.DetectHostBufferRaces).
        // RunKernel registers the per-buffer in-flight intent SYNCHRONOUSLY at queue time, so a
        // synchronous host read on the same JS turn (no await between dispatch and read) always
        // observes intents>0 and MUST throw; after a real drain (SynchronizeAsync) intents==0 and
        // the identical read succeeds with correct data. This is the mechanism for enumerating the
        // sync-readback races that the async APIs (CopyToHostAsync/CopyFromAsync/MemSetToZeroAsync/
        // SynchronizeAsync) are meant to replace. Wasm-only (the detector lives on WasmMemoryBuffer).
        [TestMethod]
        public async Task DetectHostBufferRaceTest() => await RunTest(async accelerator =>
        {
            const int count = 256;
            using var buf = accelerator.Allocate1D<int>(count);
            var fill = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
                (i, v) => v[i] = i + 1);

            bool prior = WasmMemoryBuffer.DetectHostBufferRaces;
            WasmMemoryBuffer.DetectHostBufferRaces = true;
            try
            {
                // Unawaited dispatch: intent incremented synchronously; kernel not yet drained.
                fill((Index1D)count, buf.View);

                bool threw = false;
                try
                {
                    // Synchronous readback -> WasmMemoryBuffer.CopyTo -> detector guard.
                    _ = buf.View.BaseView.GetAsArray(accelerator.DefaultStream);
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                if (!threw)
                    throw new Exception(
                        "Detector did not fire on a synchronous read during an in-flight dispatch.");

                // Correct pattern: drain first, then the identical sync read succeeds.
                await accelerator.SynchronizeAsync();
                var ok = buf.View.BaseView.GetAsArray(accelerator.DefaultStream);
                if (ok[0] != 1 || ok[count - 1] != count)
                    throw new Exception(
                        $"Post-drain readback wrong: [0]={ok[0]} [last]={ok[count - 1]} (expected 1..{count}).");
            }
            finally
            {
                WasmMemoryBuffer.DetectHostBufferRaces = prior;
            }
        });

        // ═══════════════════════════════════════════════════════════════
        // INVARIANT GUARD: wait/notify dispatcher barriers must ship OFF.
        // memory.atomic.wait32/notify races on V8 (chromium#490434403 family):
        // large multi-group RadixSorts corrupt (1.4M: 1067 sort-order violations,
        // 500K: 187, 1M: duplicate keys) while small sorts pass. Re-confirmed
        // 2026-05-24 — see Plans/wasm-waitnotify-still-races-2026-05-24.md. Pure
        // spin is the correct path. If this test fails, someone flipped the default
        // to the known-broken wait/notify barrier; the RadixSort canaries below are
        // the behavioral detectors. The flag stays only as a one-flip re-test harness.
        // ═══════════════════════════════════════════════════════════════
        [TestMethod]
        public Task WasmWaitNotifyBarriersDefaultOffTest()
        {
            if (WasmBackend.UseWaitNotifyBarriers)
                throw new Exception(
                    "WasmBackend.UseWaitNotifyBarriers must default to false. wait/notify " +
                    "dispatcher barriers race on V8 (large sorts corrupt) — see " +
                    "Plans/wasm-waitnotify-still-races-2026-05-24.md. Pure-spin barriers are " +
                    "the correct shipping path; the flag is a default-off re-test harness only.");
            return Task.CompletedTask;
        }

        // ═══════════════════════════════════════════════════════════════
        // DIAGNOSTIC: Struct shuffle (mimics RadixSort pre-sort)
        // ═══════════════════════════════════════════════════════════════

        struct DiagPair
        {
            public float Key;
            public int Value;
            public DiagPair(float k, int v) { Key = k; Value = v; }
        }

        // Kernel: write struct from separate key+value arrays
        static void DiagPairWriteKernel(
            Index1D index,
            ArrayView<float> keys,
            ArrayView<int> values,
            ArrayView<DiagPair> output)
        {
            output[index] = new DiagPair(keys[index], values[index]);
        }

        // Kernel: load struct from source, write to shuffled position
        static void StructShuffleKernel(
            Index1D index,
            ArrayView<DiagPair> source,
            ArrayView<DiagPair> dest,
            ArrayView<int> positions)
        {
            var pair = source[index];
            int pos = positions[index];
            dest[pos] = pair;
        }

        // Kernel: extract key field from struct array
        static void StructExtractKeyKernel(
            Index1D index,
            ArrayView<DiagPair> pairs,
            ArrayView<float> keys)
        {
            keys[index] = pairs[index].Key;
        }

        [TestMethod]
        public async Task WasmStructShuffleDiagTest() => await RunTest(async accelerator =>
        {
            int n = 8;
            var pairs = new DiagPair[n];
            var positions = new int[n];
            for (int i = 0; i < n; i++)
            {
                pairs[i] = new DiagPair((float)(i + 1), i * 10);
                positions[i] = n - 1 - i; // reverse: 7,6,5,...,0
            }

            using var srcBuf = accelerator.Allocate1D(pairs);
            using var dstBuf = accelerator.Allocate1D<DiagPair>(n);
            using var posBuf = accelerator.Allocate1D(positions);
            using var keyBuf = accelerator.Allocate1D<float>(n);

            // Shuffle structs to reversed positions
            var shuffleKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<DiagPair>, ArrayView<int>>(StructShuffleKernel);
            shuffleKernel(n, srcBuf.View.AsContiguous(), dstBuf.View.AsContiguous(), posBuf.View);
            await accelerator.SynchronizeAsync();

            // Extract keys from shuffled array
            var extractKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<float>>(StructExtractKeyKernel);
            extractKernel(n, dstBuf.View.AsContiguous(), keyBuf.View);
            await accelerator.SynchronizeAsync();

            var keys = await keyBuf.CopyToHostAsync<float>();
            // After reverse shuffle: dest[7]=pair(1,0), dest[6]=pair(2,10), ..., dest[0]=pair(8,70)
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(n - i); // 8,7,6,...,1
                if (MathF.Abs(keys[i] - expected) > 0.001f)
                    throw new Exception($"StructShuffle key at [{i}]: expected={expected}, got={keys[i]}");
            }
        });

        // Barrier version: load struct, use shared memory + barrier, write to shuffled pos
        static void StructBarrierShuffleKernel(
            Index1D index,
            ArrayView<DiagPair> source,
            ArrayView<DiagPair> dest)
        {
            var sharedKeys = SharedMemory.Allocate<float>(256);
            var pair = source[Group.IdxX];
            sharedKeys[Group.IdxX] = pair.Key;
            Group.Barrier();
            int pos = Group.DimX - 1 - Group.IdxX;
            dest[pos] = pair;
        }

        // ExclusiveScan version: load struct, scan key, write struct at scanned position
        static void StructScanShuffleKernel(
            Index1D index,
            ArrayView<DiagPair> source,
            ArrayView<DiagPair> dest,
            ArrayView<int> debugOut)
        {
            var sharedHist = SharedMemory.Allocate<int>(256);

            // Load struct
            var pair = source[Group.IdxX];

            // Build histogram (1 per element, like RadixSort with 1 bucket)
            sharedHist[Group.IdxX] = 1;
            Group.Barrier();

            // ExclusiveScan on histogram
            int scanned = GroupExtensions.ExclusiveScan<int,
                global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(sharedHist[Group.IdxX]);
            Group.Barrier();

            // Write struct at scanned position (identity shuffle for uniform histogram)
            dest[scanned] = pair;

            // Debug: write what we see
            if (Group.IdxX < debugOut.Length)
            {
                debugOut[Group.IdxX] = (int)pair.Key;
            }
        }

        [TestMethod]
        public async Task WasmStructBarrierShuffleDiagTest() => await RunTest(async accelerator =>
        {
            int n = 8;
            var pairs = new DiagPair[n];
            for (int i = 0; i < n; i++)
                pairs[i] = new DiagPair((float)(i + 1), i * 10);

            using var srcBuf = accelerator.Allocate1D(pairs);
            using var dstBuf = accelerator.Allocate1D<DiagPair>(n);

            var kernel = accelerator.LoadStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<DiagPair>>(StructBarrierShuffleKernel);
            kernel(new KernelConfig(1, n), (Index1D)n, srcBuf.View.AsContiguous(), dstBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            // Extract keys from result
            using var keyBuf = accelerator.Allocate1D<float>(n);
            var extractKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<float>>(StructExtractKeyKernel);
            extractKernel(n, dstBuf.View.AsContiguous(), keyBuf.View);
            await accelerator.SynchronizeAsync();

            var keys = await keyBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(n - i); // 8,7,6,...,1
                if (MathF.Abs(keys[i] - expected) > 0.001f)
                    throw new Exception($"StructBarrierShuffle key at [{i}]: expected={expected}, got={keys[i]}");
            }
        });

        // Predicate version: conditional struct load with default fallback (like RadixSort inRange check)
        static void StructPredicateKernel(
            Index1D index,
            ArrayView<DiagPair> source,
            ArrayView<DiagPair> dest,
            int validCount)
        {
            bool inRange = Group.IdxX < validCount;
            // Default value (like RadixSort's operation.DefaultValue)
            DiagPair value = new DiagPair(0f, 0);
            if (inRange)
                value = source[Group.IdxX];
            // Write — should write loaded value for valid threads, default for invalid
            dest[Group.IdxX] = value;
        }

        [TestMethod]
        public async Task WasmStructPredicateDiagTest() => await RunTest(async accelerator =>
        {
            int n = 8;
            var pairs = new DiagPair[n];
            for (int i = 0; i < n; i++)
                pairs[i] = new DiagPair((float)(i + 1), i * 10);

            using var srcBuf = accelerator.Allocate1D(pairs);
            using var dstBuf = accelerator.Allocate1D<DiagPair>(n);

            // All threads valid (validCount = n)
            var kernel = accelerator.LoadStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<DiagPair>, int>(StructPredicateKernel);
            kernel(new KernelConfig(1, n), (Index1D)n, srcBuf.View.AsContiguous(), dstBuf.View.AsContiguous(), n);
            await accelerator.SynchronizeAsync();

            using var keyBuf = accelerator.Allocate1D<float>(n);
            var extractKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<float>>(StructExtractKeyKernel);
            extractKernel(n, dstBuf.View.AsContiguous(), keyBuf.View);
            await accelerator.SynchronizeAsync();

            var keys = await keyBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(i + 1);
                if (MathF.Abs(keys[i] - expected) > 0.001f)
                    throw new Exception($"StructPredicate key at [{i}]: expected={expected}, got={keys[i]}");
            }
        });

        // Combined: Predicate + histogram + ExclusiveScan + struct store (mimics RadixSortKernel1)
        static void StructRadixMimicKernel(
            Index1D index,
            ArrayView<DiagPair> view,
            ArrayView<int> debugOut,
            int dataLength)
        {
            var scanMemory = SharedMemory.Allocate<int>(1024);

            bool inRange = Group.IdxX < dataLength;

            // Default + conditional load (like RadixSort)
            DiagPair value = new DiagPair(0f, 0);
            if (inRange)
                value = view[Group.IdxX];

            // Extract "bucket" from key (simple: just 0 or 1 based on key > 4)
            int bucket = value.Key > 4f ? 1 : 0;

            // Build histogram in shared memory (2 buckets)
            scanMemory[Group.IdxX] = 0;
            scanMemory[Group.IdxX + Group.DimX] = 0;
            if (inRange)
                scanMemory[Group.IdxX + Group.DimX * bucket] = 1;
            Group.Barrier();

            // ExclusiveScan on bucket 0
            int scan0 = GroupExtensions.ExclusiveScan<int,
                global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(
                scanMemory[Group.IdxX]);
            // ExclusiveScan on bucket 1
            int scan1 = GroupExtensions.ExclusiveScan<int,
                global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(
                scanMemory[Group.IdxX + Group.DimX]);
            Group.Barrier();

            // Compute position
            int pos = bucket == 0 ? scan0 : scan1;
            // Offset bucket 1 by bucket 0 count
            if (bucket == 1 && Group.IdxX == Group.DimX - 1)
            {
                // Last thread's scan0 + (its own contribution) = total bucket 0 count
            }

            // Just write struct to the same position for now (identity)
            if (inRange)
                view[Group.IdxX] = value;

            // Debug: write key as int
            if (Group.IdxX < debugOut.Length)
                debugOut[Group.IdxX] = (int)value.Key;
        }

        [TestMethod]
        public async Task WasmStructRadixMimicDiagTest() => await RunTest(async accelerator =>
        {
            int n = 8;
            var pairs = new DiagPair[n];
            for (int i = 0; i < n; i++)
                pairs[i] = new DiagPair((float)(i + 1), i * 10);

            using var srcBuf = accelerator.Allocate1D(pairs);
            using var debugBuf = accelerator.Allocate1D<int>(n);

            var kernel = accelerator.LoadStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<int>, int>(StructRadixMimicKernel);
            kernel(new KernelConfig(1, n), (Index1D)n, srcBuf.View.AsContiguous(), debugBuf.View, n);
            await accelerator.SynchronizeAsync();

            var debug = await debugBuf.CopyToHostAsync<int>();
            // Check debug output — should be key values as ints: 1,2,3,...,8
            string debugStr = string.Join(",", debug);
            for (int i = 0; i < n; i++)
            {
                int expected = i + 1;
                if (debug[i] != expected)
                    throw new Exception($"StructRadixMimic debug at [{i}]: expected={expected}, got={debug[i]}, all=[{debugStr}]");
            }

            // Also verify structs survived the round-trip
            using var keyBuf = accelerator.Allocate1D<float>(n);
            var extractKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<float>>(StructExtractKeyKernel);
            extractKernel(n, srcBuf.View.AsContiguous(), keyBuf.View);
            await accelerator.SynchronizeAsync();

            var keys = await keyBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(i + 1);
                if (MathF.Abs(keys[i] - expected) > 0.001f)
                    throw new Exception($"StructRadixMimic key at [{i}]: expected={expected}, got={keys[i]}, debug=[{debugStr}]");
            }
        });

        [TestMethod]
        public async Task WasmStructScanShuffleDiagTest() => await RunTest(async accelerator =>
        {
            int n = 8;
            var pairs = new DiagPair[n];
            for (int i = 0; i < n; i++)
                pairs[i] = new DiagPair((float)(i + 1), i * 10);

            using var srcBuf = accelerator.Allocate1D(pairs);
            using var dstBuf = accelerator.Allocate1D<DiagPair>(n);
            using var debugBuf = accelerator.Allocate1D<int>(n);

            var kernel = accelerator.LoadStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<DiagPair>, ArrayView<int>>(StructScanShuffleKernel);
            kernel(new KernelConfig(1, n), (Index1D)n, srcBuf.View.AsContiguous(), dstBuf.View.AsContiguous(), debugBuf.View);
            await accelerator.SynchronizeAsync();

            var debug = await debugBuf.CopyToHostAsync<int>();
            // ExclusiveScan of all-1s = [0,1,2,3,4,5,6,7] → identity permutation
            // So dest should be same order as source

            using var keyBuf = accelerator.Allocate1D<float>(n);
            var extractKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<float>>(StructExtractKeyKernel);
            extractKernel(n, dstBuf.View.AsContiguous(), keyBuf.View);
            await accelerator.SynchronizeAsync();

            var keys = await keyBuf.CopyToHostAsync<float>();
            string debugStr = string.Join(",", debug);
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(i + 1); // same order: 1,2,...,8
                if (MathF.Abs(keys[i] - expected) > 0.001f)
                    throw new Exception($"StructScanShuffle key at [{i}]: expected={expected}, got={keys[i]}, debug=[{debugStr}]");
            }
        });

        // ═══════════════════════════════════════════════════════════════
        // TRULY UNSUPPORTED — browser/Wasm hardware limitations (2)
        // ═══════════════════════════════════════════════════════════════

        // SubgroupShuffleTest + ReduceMinMaxTest now RUN on Wasm: Warp.Shuffle / SubWarpShuffle are
        // emulated via shared memory + barriers (WarpSize=8, EmitWarpShuffle in WasmKernelFunctionGenerator).

        // Body-struct ArrayView coalesce tests (added 2026-05-03 alongside the WebGPU
        // binding-count coalesce fix). Wasm body-struct decomp many-field bug FIXED
        // 2026-05-04 in WasmKernelFunctionGenerator.IsViewType — was returning `true`
        // for any struct whose first DirectField is AddressSpaceType, including
        // multi-view containers like ManyIntViewsStruct (12 ArrayView<int>). Now
        // counts AddressSpaceType DirectFields and only treats single-view structs
        // (ArrayView<T>, ArrayView1D<T,Stride>) as views; multi-view containers go
        // through the scalar-struct serialization path which already correctly
        // registers each view's buffer via ExtractBuffersFromStruct. No tests
        // gated on Wasm.

        // ═══════════════════════════════════════════════════════════════
        // HALF PRECISION — codegen wrong values (7). f16 promoted to f32 but
        // load/store as 2-byte causes wrong bit patterns. 2 of 9 pass.
        // ═══════════════════════════════════════════════════════════════

        // Half tests: un-skipped — f16↔f32 inline bit conversion in Load/Store.

        // UNSIGNED COMPARISON — FIXED: codegen now uses i32.lt_u/i64.lt_u for unsigned compares.

        // ═══════════════════════════════════════════════════════════════
        // COMPILATION ERRORS (1)
        // ═══════════════════════════════════════════════════════════════

        // AliasedBufferBindingTest: un-skipped — i64→i32 truncation in Store handler.

        // ═══════════════════════════════════════════════════════════════
        // RADIXSORT PAIRS — struct Load copies to scratch for snapshot semantics,
        // but pairs sort still produces wrong results. Needs WAT disassembly
        // of Gather/Sort/Scatter kernels with ShaderDebugService. (14 tests)
        // ═══════════════════════════════════════════════════════════════

        // Multi-dispatch struct test: write pairs in dispatch 1, read back in dispatch 2
        // This tests whether struct data survives copy-out → copy-in between dispatches
        // when the buffer is an int[] cast as DiagPair[] (like RadixSort pairs temp buffer)
        [TestMethod]
        public async Task WasmMultiDispatchStructDiagTest() => await RunTest(async accelerator =>
        {
            int n = 4;
            var keys = new float[] { 4f, 3f, 2f, 1f };
            var values = new int[] { 40, 30, 20, 10 };

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            // Allocate as int buffer, cast to pairs (same as RadixSort TempViewManager)
            using var intBuf = accelerator.Allocate1D<int>(n * 2);
            var pairsView = intBuf.View.AsContiguous().Cast<DiagPair>().SubView(0, n);

            // Dispatch 1: Write pairs to the cast view
            var writeKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<int>, ArrayView<DiagPair>>(DiagPairWriteKernel);
            writeKernel(n, keysBuf.View, valuesBuf.View, pairsView);
            await accelerator.SynchronizeAsync();

            // Dispatch 2: Read pairs back from the SAME cast view (tests copy-out → copy-in survival)
            using var outKeysBuf = accelerator.Allocate1D<float>(n);
            var readKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<DiagPair>, ArrayView<float>>(StructExtractKeyKernel);
            readKernel(n, pairsView, outKeysBuf.View);
            await accelerator.SynchronizeAsync();

            var outKeys = await outKeysBuf.CopyToHostAsync<float>();
            string keysStr = string.Join(",", outKeys);
            for (int i = 0; i < n; i++)
            {
                if (MathF.Abs(outKeys[i] - keys[i]) > 0.001f)
                    throw new Exception($"MultiDispatchStruct FAIL at [{i}]: expected={keys[i]}, got={outKeys[i]}, all=[{keysStr}]");
            }
        });

        // Gather-only test: create pairs from keys+values, read back via int view
        [TestMethod]
        public async Task WasmGatherOnlyDiagTest() => await RunTest(async accelerator =>
        {
            int n = 4;
            var keys = new float[] { 4f, 3f, 2f, 1f };
            var values = new int[] { 40, 30, 20, 10 };

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            // Allocate as int buffer, cast to pairs (same as RadixSort does)
            using var pairsBuf = accelerator.Allocate1D<int>(n * 2); // 4 pairs × 8 bytes = 32 bytes = 8 ints

            // Use our DiagPair write kernel to populate (simulates Gather)
            var writeKernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, ArrayView<int>, ArrayView<DiagPair>>(DiagPairWriteKernel);
            writeKernel(n, keysBuf.View, valuesBuf.View, pairsBuf.View.AsContiguous().Cast<DiagPair>().SubView(0, n));
            await accelerator.SynchronizeAsync();

            // Read back as raw ints to see what's actually in the buffer
            var rawInts = await pairsBuf.CopyToHostAsync<int>();
            // pairs[0] = DiagPair(4f, 40) → ints: [float_bits(4.0), 40]
            // pairs[1] = DiagPair(3f, 30) → ints: [float_bits(3.0), 30]
            string rawStr = string.Join(",", rawInts);

            // Verify: first pair should have key=4.0f (bits=0x40800000=1082130432) and value=40
            float key0 = BitConverter.Int32BitsToSingle(rawInts[0]);
            int val0 = rawInts[1];
            if (MathF.Abs(key0 - 4f) > 0.001f || val0 != 40)
                throw new Exception($"GatherOnly FAIL: raw=[{rawStr}], key0={key0}, val0={val0}, expected key0=4, val0=40");
        });

        // Minimal pairs sort — enabled for copy-in debugging
        [TestMethod]
        public async Task WasmMinimalPairsSortDiagTest() => await RunTest(async accelerator =>
        {
            // 256 elements — reliable with Fix B v4
            int n = 256;
            var keys = new float[n];
            var values = new int[n];
            var rng = new Random(42); // deterministic
            for (int j = 0; j < n; j++) { keys[j] = (float)(rng.NextDouble() * 10000.0); values[j] = j; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<float, int,
                global::ILGPU.Algorithms.RadixSortOperations.AscendingFloat>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            // Capture Wasm binaries for WAT analysis
            SpawnDev.ILGPU.Wasm.Backend.WasmBackend.AllWasmBinaries.Clear();
            SpawnDev.ILGPU.Wasm.Backend.WasmBackend.AllKernelInfos.Clear();
            SpawnDev.ILGPU.Wasm.Backend.WasmBackend.VerboseLogging = false;
            SpawnDev.ILGPU.Wasm.WasmAccelerator._dispatchCount = 0;
            SpawnDev.ILGPU.Wasm.WasmAccelerator._dispatchLog = "";

            var radixSort = accelerator.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense,
                global::ILGPU.Algorithms.RadixSortOperations.AscendingFloat>();

            // Capture kernel compilation summaries
            var binaries = SpawnDev.ILGPU.Wasm.Backend.WasmBackend.AllWasmBinaries;
            var infos = SpawnDev.ILGPU.Wasm.Backend.WasmBackend.AllKernelInfos;
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            int dispCount = SpawnDev.ILGPU.Wasm.WasmAccelerator._dispatchCount;
            string dispLog = SpawnDev.ILGPU.Wasm.WasmAccelerator._dispatchLog ?? "(none)";

            var sortedKeys = await keysBuf.CopyToHostAsync<float>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            // Also read raw temp buffer to see pairs data
            var rawTemp = await tempBuf.CopyToHostAsync<int>();
            string rawFirst8 = string.Join(",", rawTemp.Take(8));

            string keysStr = string.Join(",", sortedKeys);
            string valsStr = string.Join(",", sortedValues);

            // Capture kernel compilation info
            var kernelInfos = SpawnDev.ILGPU.Wasm.Backend.WasmBackend.AllKernelInfos;
            string kiStr = kernelInfos != null ? string.Join(" | ", kernelInfos.TakeLast(10)) : "(null)";

            // Check sort order and report exact violations
            var violations = new System.Collections.Generic.List<string>();
            for (int i = 1; i < n; i++)
            {
                if (sortedKeys[i] < sortedKeys[i - 1])
                    violations.Add($"order[{i}]:{sortedKeys[i-1]}>{sortedKeys[i]}");
            }
            // Check value tracking — values[j] = j, so sortedValues[i] should be
            // the original index of the key now at position i
            var valueErrors = new System.Collections.Generic.List<string>();
            for (int i = 0; i < n; i++)
            {
                int origIdx = sortedValues[i];
                if (origIdx < 0 || origIdx >= n)
                    valueErrors.Add($"val[{i}]={origIdx}(OOB)");
                else if (MathF.Abs(sortedKeys[i] - keys[origIdx]) > 0.001f)
                    valueErrors.Add($"val[{i}]={origIdx}:key={sortedKeys[i]}!=orig[{origIdx}]={keys[origIdx]}");
            }
            if (violations.Count > 0 || valueErrors.Count > 0)
            {
                string vStr = violations.Count > 0 ? string.Join(",", violations.Take(10)) : "none";
                string veStr = valueErrors.Count > 0 ? string.Join(",", valueErrors.Take(10)) : "none";
                throw new Exception($"PairsSort256: {violations.Count} order violations, {valueErrors.Count} value errors. Order: [{vStr}] Values: [{veStr}]");
            }
        });

        // RADIXSORT PAIRS — Option 1 scratch layout + memory.grow error handling applied.
        // 256-element float/int/uint pairs: PASS. Double/Long: intermittent 1-value errors.
        // 16K: 2 order violations. Under investigation — may be shared memory boundary or
        // cross-iteration contamination for 16-byte struct elements.
        // Double/Long pairs: un-skipped with i64.shr_u fix
        // Double/Long offset + index tests: un-skipped with unsigned shift fix
        // 16K/20K: consistently 2-4 violations. Unsigned shift fix helped double/long
        // but 16K int pairs still have intermittent corruption.
        // Float16 struct field load/store fixed (2-byte ops in all 5 IR handlers).
        // FloatAsIntCast/IntAsFloatCast fixed: use EmitF32ToF16/EmitF16ToF32 instead of
        // I32ReinterpretF32/F32ReinterpretI32 for Float16 — gives correct 16-bit bit patterns
        // for RadixSort onesComplementMask.

        // Large sort tests: 240s timeout (was 120s). Cold-start / small-batch
        // conditions can see 100-200s for tests that pass in 30-90s mid-sweep
        // (rc.27 1h17m full sweep had 4M at 42s; small-batch shows the same
        // tests at 60-200s). 240s = matches the precedent at
        // SubViewRange_HighDispatchCount (line 589).
        // Verified 2026-04-28 with 5-test medium-large RadixSort batch: all
        // pass well under 240s (40s-142s range).
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortThresholdProbeTest() => await base.RadixSortThresholdProbeTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortDescendingWithSentinelsTest() => await base.RadixSortDescendingWithSentinelsTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortRepeatedResortTest() => await base.RadixSortRepeatedResortTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortHeavyDuplicateKeysTest() => await base.RadixSortHeavyDuplicateKeysTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortDescendingOddCountTest() => await base.RadixSortDescendingOddCountTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortSpawnSceneSimulationTest() => await base.RadixSortSpawnSceneSimulationTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortDescending1_4MTest() => await base.RadixSortDescending1_4MTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortDescending2MTest() => await base.RadixSortDescending2MTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortDescending4MTest() => await base.RadixSortDescending4MTest();
        [TestMethod(Timeout = 240000)]
        public new async Task RadixSortAscending1_4MTest() => await base.RadixSortAscending1_4MTest();

        // 500 sequential dispatches x 2 kernels x SynchronizeAsync is genuinely slow on Wasm
        // under full-suite load (JS<->Wasm boundary + multi-worker barrier cost per dispatch).
        // Came in at 120173ms (just over the base 120s). Defense-in-depth headroom at 240s;
        // the cascade-safety fix lives in WasmAccelerator.DisposeAccelerator_SyncRoot.
        [TestMethod(Timeout = 240000)]
        public new async Task SubViewRange_HighDispatchCount() => await base.SubViewRange_HighDispatchCount();

        // ═══════════════════════════════════════════════════════════════
        // MULTI-GROUP SCAN
        // ═══════════════════════════════════════════════════════════════

        // DualScanKernelTest: un-skipped — MaxNumThreadsPerGroup increased to 256.
        // TwoPassScanSimulationTest: un-skipped — CopyFromBuffer handles Wasm-to-Wasm copies.

        // ═══════════════════════════════════════════════════════════════
        // BARRIER ISOLATION TESTS — isolate the 3+ worker failure
        // ═══════════════════════════════════════════════════════════════

        // Test 1: Simple scan with 32 threads, repeated 50 times to detect intermittent failures
        static void IsolationScan32Kernel(Index1D index, ArrayView<int> output)
        {
            var shared = SharedMemory.Allocate<int>(256);
            shared[Group.IdxX] = 2;
            Group.Barrier();
            if (Group.IdxX == 0)
            {
                for (int i = 1; i < Group.DimX; i++)
                    shared[i] = shared[i - 1] + shared[i];
            }
            Group.Barrier();
            output[Group.IdxX] = shared[Group.IdxX];
        }

        [TestMethod]
        public async Task WasmBarrierIsolation32Test() => await RunTest(async accelerator =>
        {
            int gs = 32;
            for (int run = 0; run < 50; run++)
            {
                using var buf = accelerator.Allocate1D<int>(gs);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(IsolationScan32Kernel);
                kernel(new KernelConfig(1, gs), (Index1D)gs, buf.View);
                await accelerator.SynchronizeAsync();
                var result = await buf.CopyToHostAsync<int>();
                for (int i = 0; i < gs; i++)
                {
                    int expected = (i + 1) * 2;
                    if (result[i] != expected)
                        throw new Exception($"Isolation32 run {run} pos {i}: expected {expected}, got {result[i]}");
                }
            }
        });

        // Test 2: Same scan with 256 threads (RadixSort groupSize)
        [TestMethod]
        public async Task WasmBarrierIsolation256Test() => await RunTest(async accelerator =>
        {
            int gs = 256;
            for (int run = 0; run < 50; run++)
            {
                using var buf = accelerator.Allocate1D<int>(gs);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(IsolationScan32Kernel);
                kernel(new KernelConfig(1, gs), (Index1D)gs, buf.View);
                await accelerator.SynchronizeAsync();
                var result = await buf.CopyToHostAsync<int>();
                for (int i = 0; i < gs; i++)
                {
                    int expected = (i + 1) * 2;
                    if (result[i] != expected)
                        throw new Exception($"Isolation256 run {run} pos {i}: expected {expected}, got {result[i]}");
                }
            }
        });

        // Test 4b: In-place scatter — read, scan, write back at scanned position (like RadixSort presort)
        static void IsolationPresortKernel(
            Index1D index,
            ArrayView<int> view, // read AND write to same buffer
            SpecializedValue<int> groupSize)
        {
            var scanMem = SharedMemory.Allocate<int>(256);

            bool inRange = Group.IdxX < view.Length;
            int value = inRange ? view[Group.IdxX] : 0;

            // Everyone contributes 1 to the histogram
            scanMem[Group.IdxX] = inRange ? 1 : 0;
            Group.Barrier();

            // ExclusiveScan gives each thread a unique position
            int pos = GroupExtensions.ExclusiveScan<int,
                global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(scanMem[Group.IdxX]);
            Group.Barrier();

            // Write value to scanned position (in-place shuffle — identity for all-1 histogram)
            if (inRange)
                view[pos] = value;
            Group.Barrier();
        }

        [TestMethod]
        public async Task WasmBarrierIsolationPresortTest() => await RunTest(async accelerator =>
        {
            int gs = 256;
            int numGroups = 12;
            int total = gs * numGroups;
            for (int run = 0; run < 10; run++)
            {
                using var buf = accelerator.Allocate1D<int>(total);
                var data = new int[total];
                for (int i = 0; i < total; i++) data[i] = i;
                buf.CopyFromCPU(data);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, SpecializedValue<int>>(IsolationPresortKernel);
                kernel(new KernelConfig(numGroups, gs), (Index1D)total, buf.View, SpecializedValue.New(gs));
                await accelerator.SynchronizeAsync();
                var result = await buf.CopyToHostAsync<int>();
                // Identity shuffle: each element should be at the same position
                for (int i = 0; i < total; i++)
                {
                    if (result[i] != i)
                        throw new Exception($"Presort run {run} pos {i}: expected {i}, got {result[i]}");
                }
            }
        });

        // Test 4a: FOUR ExclusiveScan calls with SpecializedValue (like RadixSort EXACTLY)
        static void IsolationMultiScanKernel(
            Index1D index,
            ArrayView<int> input,
            ArrayView<int> output,
            SpecializedValue<int> groupSize)
        {
            var scanMem = SharedMemory.Allocate<int>(groupSize * 4); // groupSize × unrollFactor

            // 4 scan operations (mimics RadixSort's unrollFactor=4)
            for (int j = 0; j < 4; j++)
            {
                scanMem[Group.IdxX + Group.DimX * j] = (j == 0 && Group.IdxX < input.Length) ? input[Group.IdxX] : 0;
            }
            Group.Barrier();

            for (int j = 0; j < 4; j++)
            {
                scanMem[Group.IdxX + Group.DimX * j] =
                    GroupExtensions.ExclusiveScan<int,
                        global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(
                        scanMem[Group.IdxX + Group.DimX * j]);
            }
            Group.Barrier();

            if (Group.IdxX < output.Length)
                output[Group.IdxX] = scanMem[Group.IdxX]; // first bucket's scan result
        }

        [TestMethod]
        public async Task WasmBarrierIsolationMultiScanTest() => await RunTest(async accelerator =>
        {
            int gs = 256;
            for (int run = 0; run < 20; run++)
            {
                using var inBuf = accelerator.Allocate1D<int>(gs);
                using var outBuf = accelerator.Allocate1D<int>(gs);
                var data = new int[gs];
                for (int i = 0; i < gs; i++) data[i] = 1;
                inBuf.CopyFromCPU(data);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, SpecializedValue<int>>(IsolationMultiScanKernel);
                kernel(new KernelConfig(1, gs), (Index1D)gs, inBuf.View, outBuf.View, SpecializedValue.New(gs));
                await accelerator.SynchronizeAsync();
                var result = await outBuf.CopyToHostAsync<int>();
                for (int i = 0; i < gs; i++)
                {
                    int expected = i; // exclusive scan of all-1s = [0,1,2,...,255]
                    if (result[i] != expected)
                        throw new Exception($"MultiScan run {run} pos {i}: expected {expected}, got {result[i]}");
                }
            }
        });

        // Test 4: ExclusiveScan via GroupExtensions (same path as RadixSort)
        static void IsolationGroupScanKernel(Index1D index, ArrayView<int> input, ArrayView<int> output)
        {
            int val = input[Group.IdxX];
            int scanned = GroupExtensions.ExclusiveScan<int,
                global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(val);
            output[Group.IdxX] = scanned;
        }

        [TestMethod]
        public async Task WasmBarrierIsolationGroupScanTest() => await RunTest(async accelerator =>
        {
            int gs = 32;
            for (int run = 0; run < 50; run++)
            {
                using var inBuf = accelerator.Allocate1D<int>(gs);
                using var outBuf = accelerator.Allocate1D<int>(gs);
                var data = new int[gs];
                for (int i = 0; i < gs; i++) data[i] = 2;
                inBuf.CopyFromCPU(data);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(IsolationGroupScanKernel);
                kernel(new KernelConfig(1, gs), (Index1D)gs, inBuf.View, outBuf.View);
                await accelerator.SynchronizeAsync();
                var result = await outBuf.CopyToHostAsync<int>();
                for (int i = 0; i < gs; i++)
                {
                    int expected = i * 2; // exclusive scan: [0, 2, 4, ...]
                    if (result[i] != expected)
                        throw new Exception($"GroupScan run {run} pos {i}: expected {expected}, got {result[i]}");
                }
            }
        });

        // Test 3: Multi-group (4 groups × 256 threads)
        static void IsolationMultiGroupKernel(Index1D index, ArrayView<int> output, int groupSize)
        {
            var shared = SharedMemory.Allocate<int>(256);
            shared[Group.IdxX] = 2;
            Group.Barrier();
            if (Group.IdxX == 0)
            {
                for (int i = 1; i < groupSize; i++)
                    shared[i] = shared[i - 1] + shared[i];
            }
            Group.Barrier();
            int gid = Grid.IdxX * groupSize + Group.IdxX;
            if (gid < output.Length)
                output[gid] = shared[Group.IdxX];
        }

        [TestMethod]
        public async Task WasmBarrierIsolationMultiGroupTest() => await RunTest(async accelerator =>
        {
            int gs = 256;
            int numGroups = 4;
            int total = gs * numGroups;
            for (int run = 0; run < 20; run++)
            {
                using var buf = accelerator.Allocate1D<int>(total);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, int>(IsolationMultiGroupKernel);
                kernel(new KernelConfig(numGroups, gs), (Index1D)total, buf.View, gs);
                await accelerator.SynchronizeAsync();
                var result = await buf.CopyToHostAsync<int>();
                for (int g = 0; g < numGroups; g++)
                {
                    for (int i = 0; i < gs; i++)
                    {
                        int expected = (i + 1) * 2;
                        int actual = result[g * gs + i];
                        if (actual != expected)
                            throw new Exception($"IsolationMultiGroup run {run} group {g} pos {i}: expected {expected}, got {actual}");
                    }
                }
            }
        });
        // Test 5b: 12 groups, 256 threads — matching RadixSort exactly
        [TestMethod]
        public async Task WasmBarrierIsolation12GroupTest() => await RunTest(async accelerator =>
        {
            int gs = 256;
            int numGroups = 12;
            int total = gs * numGroups;
            for (int run = 0; run < 20; run++)
            {
                using var buf = accelerator.Allocate1D<int>(total);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, int>(IsolationMultiGroupKernel);
                kernel(new KernelConfig(numGroups, gs), (Index1D)total, buf.View, gs);
                await accelerator.SynchronizeAsync();
                var result = await buf.CopyToHostAsync<int>();
                for (int g = 0; g < numGroups; g++)
                {
                    for (int i = 0; i < gs; i++)
                    {
                        int expected = (i + 1) * 2;
                        int actual = result[g * gs + i];
                        if (actual != expected)
                            throw new Exception($"Isolation12Group run {run} group {g} pos {i}: expected {expected}, got {actual}");
                    }
                }
            }
        });

        // Test 5c: Multi-dispatch (mimics RadixSort's pass1→scan→pass2 pattern)
        [TestMethod]
        public async Task WasmBarrierIsolationMultiDispatchTest() => await RunTest(async accelerator =>
        {
            int gs = 256;
            int numGroups = 12;
            int total = gs * numGroups;
            // Do 24 dispatches (matching RadixSort's 8 passes × 3 dispatches each)
            for (int dispatch = 0; dispatch < 24; dispatch++)
            {
                using var buf = accelerator.Allocate1D<int>(total);
                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, int>(IsolationMultiGroupKernel);
                kernel(new KernelConfig(numGroups, gs), (Index1D)total, buf.View, gs);
                await accelerator.SynchronizeAsync();
                var result = await buf.CopyToHostAsync<int>();
                for (int g = 0; g < numGroups; g++)
                {
                    int expected0 = 2; // first element always 2
                    int actual0 = result[g * gs];
                    if (actual0 != expected0)
                        throw new Exception($"MultiDispatch #{dispatch} group {g} pos 0: expected {expected0}, got {actual0}");
                    int expectedLast = gs * 2;
                    int actualLast = result[g * gs + gs - 1];
                    if (actualLast != expectedLast)
                        throw new Exception($"MultiDispatch #{dispatch} group {g} pos {gs-1}: expected {expectedLast}, got {actualLast}");
                }
            }
        });

        // Test 5e: Full RadixSort-like kernel — 4 scans + presort in grid-stride loop
        static void IsolationRadixLikeKernel(
            Index1D index,
            ArrayView<int> view,
            SpecializedValue<int> groupSize,
            int numGroups,
            int paddedLength)
        {
            var scanMemory = SharedMemory.Allocate<int>(groupSize * 4);
            int gridIdx = Grid.IdxX;

            for (int i = Grid.GlobalIndex.X; i < paddedLength; i += GridExtensions.GridStrideLoopStride)
            {
                bool inRange = i < view.Length;
                int value = 0;
                if (inRange)
                    value = view[i];

                int bits = value & 3; // 0-3 (like ExtractRadixBits)

                // Zero histogram
                for (int j = 0; j < 4; j++)
                    scanMemory[Group.IdxX + groupSize * j] = 0;
                if (inRange)
                    scanMemory[Group.IdxX + groupSize * bits] = 1;
                Group.Barrier();

                // 4 ExclusiveScan calls (like RadixSort)
                for (int j = 0; j < 4; j++)
                {
                    var addr = Group.IdxX + groupSize * j;
                    scanMemory[addr] = GroupExtensions.ExclusiveScan<int,
                        global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(scanMemory[addr]);
                }
                Group.Barrier();

                // Compute position and presort (in-place write)
                int pos = gridIdx * Group.DimX + scanMemory[Group.IdxX + groupSize * bits];
                if (inRange && pos < view.Length)
                    view[pos] = value;
                Group.Barrier();

                gridIdx += Grid.DimX;
            }
        }

        [TestMethod(Timeout = 120000)]
        public async Task WasmBarrierIsolationRadixLikeTest() => await RunTest(async accelerator =>
        {
            int n = 260000;
            int gs = 256;
            var (gridDim, groupDim) = accelerator.ComputeGridStrideLoopExtent(n);
            int paddedLength = ((n + gs - 1) / gs) * gs;
            int numGroups = gridDim;

            for (int run = 0; run < 3; run++)
            {
                using var buf = accelerator.Allocate1D<int>(n);
                var data = new int[n];
                var rng = new Random(42 + run);
                for (int i = 0; i < n; i++) data[i] = rng.Next(4); // values 0-3
                buf.CopyFromCPU(data);

                var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>,
                    SpecializedValue<int>, int, int>(IsolationRadixLikeKernel);
                kernel(new KernelConfig(gridDim, groupDim), (Index1D)n, buf.View,
                    SpecializedValue.New(gs), numGroups, paddedLength);
                await accelerator.SynchronizeAsync();

                // Just verify no OOB — the presort shuffle is valid if it doesn't crash
                var result = await buf.CopyToHostAsync<int>();
                // Check: all values should still be 0-3
                for (int i = 0; i < n; i++)
                {
                    if (result[i] < 0 || result[i] > 3)
                        throw new Exception($"RadixLike run {run} pos {i}: unexpected value {result[i]}");
                }
            }
        });

        // Test 5d: ExclusiveScan helper with 12 groups, 256 threads, grid-stride loop
        // Mimics RadixSort's internal scan pattern
        static void IsolationGridStrideScanKernel(
            Index1D index,
            ArrayView<int> input,
            ArrayView<int> output,
            SpecializedValue<int> groupSize,
            int numGroups,
            int paddedLength)
        {
            var scanMemory = SharedMemory.Allocate<int>(1024);
            int gridIdx = Grid.IdxX;
            for (int i = Grid.GlobalIndex.X; i < paddedLength; i += GridExtensions.GridStrideLoopStride)
            {
                bool inRange = i < input.Length;
                int val = inRange ? input[i] : 0;

                // Write to shared memory and scan (like RadixSort histogram)
                scanMemory[Group.IdxX] = val;
                Group.Barrier();

                int scanned = GroupExtensions.ExclusiveScan<int,
                    global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(scanMemory[Group.IdxX]);
                Group.Barrier();

                if (inRange)
                    output[i] = scanned;

                gridIdx += Grid.DimX;
            }
        }

        [TestMethod(Timeout = 120000)]
        public async Task WasmBarrierIsolationGridStrideScanTest() => await RunTest(async accelerator =>
        {
            int n = 500000;
            int gs = 256;
            var (gridDim, groupDim) = accelerator.ComputeGridStrideLoopExtent(n);
            int paddedLength = ((n + gs - 1) / gs) * gs;

            using var inBuf = accelerator.Allocate1D<int>(n);
            using var outBuf = accelerator.Allocate1D<int>(n);
            var data = new int[n];
            for (int i = 0; i < n; i++) data[i] = 1;
            inBuf.CopyFromCPU(data);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>,
                SpecializedValue<int>, int, int>(IsolationGridStrideScanKernel);
            kernel(new KernelConfig(gridDim, groupDim), (Index1D)n, inBuf.View, outBuf.View,
                SpecializedValue.New(gs), gridDim, paddedLength);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<int>();
            // Check first group's scan results: exclusive scan of all-1s = [0,1,2,...,255]
            for (int i = 0; i < Math.Min(gs, n); i++)
            {
                int expected = i; // exclusive scan of 1s = index
                if (result[i] != expected)
                    throw new Exception($"GridStrideScan pos {i}: expected {expected}, got {result[i]}");
            }
        });

        // Test 5: RadixSort at various sizes to find the failure threshold
        [TestMethod(Timeout = 300000)]
        public async Task WasmBarrierIsolationRadixSortSizeTest() => await RunTest(async accelerator =>
        {
            foreach (int size in new[] { 10000, 50000, 100000, 200000, 300000, 400000, 500000 })
            {
                var keys = new int[size];
                var rng = new Random(42);
                for (int i = 0; i < size; i++) keys[i] = rng.Next();

                using var keysBuf = accelerator.Allocate1D(keys);
                var tempSize = accelerator.ComputeRadixSortTempStorageSize<int,
                    global::ILGPU.Algorithms.RadixSortOperations.AscendingInt32>(size);
                using var tempBuf = accelerator.Allocate1D<int>(tempSize);

                var sort = accelerator.CreateRadixSort<int, Stride1D.Dense,
                    global::ILGPU.Algorithms.RadixSortOperations.AscendingInt32>();
                sort(accelerator.DefaultStream, keysBuf.View, tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();

                var result = await keysBuf.CopyToHostAsync<int>();
                int violations = 0;
                for (int i = 1; i < size; i++)
                {
                    if (result[i] < result[i - 1])
                        violations++;
                }
                if (violations > 0)
                    throw new Exception($"RadixSort size={size}: {violations} order violations");
            }
        });

        // ═══════════════════════════════════════════════════════════════
        // REGRESSION GUARD (Tuvok 2026-05-26) — the GROUP-barrier yield-escape.
        // Creates an OVERSUBSCRIBED accelerator (>> hardwareConcurrency workers) and runs a
        // multi-group barrier kernel that crosses the GROUP barrier repeatedly. The group-barrier
        // waiter MUST yield to JS past the spin threshold (like the phase barrier) and resume via
        // the GROUP-RESUME path, or the spinning waiters starve the descheduled not-yet-arrived
        // worker → livelock. Without the fix this HANGS (PMT 30s kill; prior Finding #3 = 2 timeouts
        // at 2x oversub); with it, completes in ~4s with correct output. If this ever times out,
        // someone removed/broke the group-barrier yield escape in GeneratePhaseDispatcher.
        // ═══════════════════════════════════════════════════════════════
        [TestMethod(Timeout = 60000)]
        public async Task WasmGroupBarrierOversubscriptionTest()
        {
            int hw = SpawnDev.ILGPU.Wasm.WasmILGPUDevice.GetHardwareConcurrency();
            int oversub = Math.Max(24, hw * 3); // force many more workers than cores
            var builder = Context.Create().EnableAlgorithms().EnableWasmAlgorithms().Wasm();
            var context = builder.ToContext();
            var acc = await context.CreateWasmAcceleratorAsync(
                new WasmBackendOptions { WorkerCount = oversub });
            try
            {
                int gs = 256, numGroups = 12, total = gs * numGroups;
                var kernel = acc.LoadStreamKernel<Index1D, ArrayView<int>, int>(IsolationMultiGroupKernel);
                for (int run = 0; run < 5; run++)
                {
                    using var buf = acc.Allocate1D<int>(total);
                    kernel(new KernelConfig(numGroups, gs), (Index1D)total, buf.View, gs);
                    await acc.SynchronizeAsync();
                    var result = await buf.CopyToHostAsync<int>();
                    for (int g = 0; g < numGroups; g++)
                        for (int i = 0; i < gs; i++)
                        {
                            int expected = (i + 1) * 2;
                            int actual = result[g * gs + i];
                            if (actual != expected)
                                throw new Exception($"GroupBarrierOversub run {run} group {g} pos {i}: expected {expected}, got {actual} (workers={oversub}, hw={hw})");
                        }
                }
            }
            finally
            {
                acc.Dispose();
                context.Dispose();
            }
        }
    }
}
