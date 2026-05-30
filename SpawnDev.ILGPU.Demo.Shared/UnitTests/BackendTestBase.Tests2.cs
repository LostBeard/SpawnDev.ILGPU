using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 2: Advanced tests (dynamic shared, int math, matrix, intrinsics, bit ops, histogram, loops, trig, barriers, select, advanced math, bitwise, large buffer, sequential, unsigned, atomic min/max, buffer reuse, grid/group dims)
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task DynamicSharedMemoryTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var data = new int[len];
            for (int i = 0; i < len; i++) data[i] = i;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(DynamicSharedKernel);
            kernel(new KernelConfig(1, len, SharedMemoryConfig.RequestDynamic<int>(len)), (Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != 63 - i) throw new Exception($"Dynamic shared failed at {i}. Expected {63 - i}, got {result[i]}");
        });

        [TestMethod]
        public async Task IntMathTest() => await RunTest(async accelerator =>
        {
            var input = new int[] { -5, 5, 20, 10, 3, -100 };
            int len = input.Length;
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(IntMathKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<int>();
            int[] expected = { 5, 5, 15, 15, 3, -100 };
            for (int i = 0; i < len; i++)
                if (result[i] != expected[i]) throw new Exception($"Int math failed at {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task MatrixMulTest() => await RunTest(async accelerator =>
        {
            int size = 4;
            var a = new float[size * size]; var b = new float[size * size];
            for (int i = 0; i < size; i++) for (int j = 0; j < size; j++) { a[i * size + j] = (i == j) ? 2.0f : 0.0f; b[i * size + j] = i + j; }
            using var bufA = accelerator.Allocate1D(a);
            using var bufB = accelerator.Allocate1D(b);
            using var bufC = accelerator.Allocate1D<float>(size * size);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index2D, ArrayView<float>, ArrayView<float>, ArrayView<float>, int>(MatrixMulKernel);
            kernel(new Index2D(size, size), bufA.View, bufB.View, bufC.View, size);
            await accelerator.SynchronizeAsync();
            var result = await bufC.CopyToHostAsync<float>();
            for (int i = 0; i < size * size; i++)
                if (MathF.Abs(result[i] - b[i] * 2.0f) > 0.01f) throw new Exception($"MatMul failed at {i}");
        });

        [TestMethod]
        public async Task SpecializedIntrinsicsTest() => await RunTest(async accelerator =>
        {
            var input = new float[] { 4.0f, 2.5f, -3.7f, 1.0f, 2.0f };
            int len = input.Length;
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(SpecializedIntrinsicsKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();
            if (MathF.Abs(result[0] - (1.0f / MathF.Sqrt(4.0f))) > 0.01f) throw new Exception($"Rsqrt failed. Got {result[0]}");
            if (MathF.Abs(result[4] - 0.5f) > 0.01f) throw new Exception($"Rcp failed. Got {result[4]}");
        });

        [TestMethod]
        public async Task BitManipulationTest() => await RunTest(async accelerator =>
        {
            var input = new int[] { 0xFF, 16, 256, 0x0F0F0F0F };
            int len = input.Length;
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(BitManipulationKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<int>();
            if (result[0] != System.Numerics.BitOperations.PopCount((uint)0xFF)) throw new Exception($"PopCount failed");
        });

        [TestMethod]
        public async Task HistogramTest() => await RunTest(async accelerator =>
        {
            int numBins = 4;
            int numItems = 64;
            var data = new int[numItems];
            for (int i = 0; i < numItems; i++) data[i] = i % numBins;
            using var bufData = accelerator.Allocate1D(data);
            using var bufBins = accelerator.Allocate1D<int>(numBins);
            bufBins.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(HistogramKernel);
            kernel((Index1D)numItems, bufData.View, bufBins.View);
            await accelerator.SynchronizeAsync();
            var result = await bufBins.CopyToHostAsync<int>();
            int expectedCount = numItems / numBins; // 64/4 = 16
            for (int i = 0; i < numBins; i++)
                if (result[i] != expectedCount) throw new Exception($"Histogram bin {i} failed. Expected {expectedCount}, got {result[i]}");
        });

        [TestMethod]
        public async Task NestedLoopBreakTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(NestedLoopBreakKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != 9) throw new Exception($"Nested loop break failed at {i}. Expected 9, got {result[i]}");
        });

        /// <summary>
        /// Tests that assignments made before a `break` in a for-loop are visible
        /// after the loop exits. This reproduces the transpiler break-PHI bug where
        /// intermediate basic blocks between break and merge are skipped.
        /// </summary>
        [TestMethod]
        public async Task LoopBreakAssignmentTest() => await RunTest(async accelerator =>
        {
            int len = 8;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LoopBreakAssignmentKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            // Expected: hitStep * 100 + hitValue
            // index 0: i=5, val=5 → 505
            // index 1: i=4, val=5 → 405
            // index 2: i=3, val=5 → 305
            // index 3: i=2, val=5 → 205
            // index 4: i=1, val=5 → 105
            // index 5: i=0, val=5 → 5  (hitStep=0, 0*100+5=5)
            // index 6: i=0, val=6 → 6
            // index 7: i=0, val=7 → 7
            int[] expected = { 505, 405, 305, 205, 105, 5, 6, 7 };
            for (int i = 0; i < len; i++)
                if (result[i] != expected[i]) throw new Exception($"Break-PHI assignment failed at index {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task HyperbolicTest() => await RunTest(async accelerator =>
        {
            var input = new float[] { 0.5f, 0.5f, 0.5f };
            int len = input.Length;
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(HyperbolicKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();
            if (MathF.Abs(result[0] - MathF.Sinh(0.5f)) > 0.01f) throw new Exception($"Sinh failed. Got {result[0]}");
            if (MathF.Abs(result[1] - MathF.Cosh(0.5f)) > 0.01f) throw new Exception($"Cosh failed. Got {result[1]}");
            if (MathF.Abs(result[2] - MathF.Tanh(0.5f)) > 0.01f) throw new Exception($"Tanh failed. Got {result[2]}");
        });

        [TestMethod]
        public async Task SharedMemoryBarrierTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(SharedMemoryBarrierKernel);
            kernel(new KernelConfig(1, len), (Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 32; i < len; i++)
                if (result[i] != (i - 32) * 2) throw new Exception($"Barrier test failed at {i}. Expected {(i - 32) * 2}, got {result[i]}");
        });

        [TestMethod]
        public async Task SelectTest() => await RunTest(async accelerator =>
        {
            var input = new int[] { -5, -1, 0, 1, 5, 100, -100, 50 };
            int len = input.Length;
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(SelectKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
            {
                int expected = (input[i] > 0) ? 1 : -1;
                if (result[i] != expected) throw new Exception($"Select failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task LinearBarrierTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            using var buf = accelerator.Allocate1D<int>(len);
            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(LinearBarrierKernel);
            kernel(new KernelConfig(1, len), (Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != (i + 1) % 64) throw new Exception($"Linear barrier failed at {i}. Expected {(i + 1) % 64}, got {result[i]}");
        });

        [TestMethod]
        public async Task AdvancedMathTest() => await RunTest(async accelerator =>
        {
            var input = new float[] { 0.5f, 1.0f, 1.5f, 2.0f };
            int len = input.Length;
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(AdvancedMathKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();
            for (int i = 0; i < len; i++)
            {
                float val = input[i];
                float expected = MathF.Tan(val) + MathF.Exp(val) + MathF.Log(MathF.Abs(val) + 1.0f) + MathF.Pow(val, 2.0f) + MathF.Min(val, 2.0f) + MathF.Max(val, 3.0f);
                if (MathF.Abs(result[i] - expected) > 0.1f)
                    throw new Exception($"Advanced math failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task BitwiseTest() => await RunTest(async accelerator =>
        {
            var data = new int[] { 0, 1, 5, 10, -1, 127, 255, 1024 };
            int len = data.Length;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(BitwiseKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
            {
                int val = data[i];
                int expected = (val << 1) + (val >> 1) + (val & 1) + (val | 1) + (val ^ 1) + (~val);
                if (result[i] != expected) throw new Exception($"Bitwise failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task InverseTrigTest() => await RunTest(async accelerator =>
        {
            var input = new float[] { 0.5f, 0.5f, 1.0f };
            int len = input.Length;
            using var bufIn = accelerator.Allocate1D(input);
            using var bufOut = accelerator.Allocate1D<float>(len);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(InverseTrigKernel);
            kernel((Index1D)len, bufIn.View, bufOut.View);
            await accelerator.SynchronizeAsync();
            var result = await bufOut.CopyToHostAsync<float>();
            if (MathF.Abs(result[0] - MathF.Asin(0.5f)) > 0.01f) throw new Exception($"Asin failed");
            if (MathF.Abs(result[1] - MathF.Acos(0.5f)) > 0.01f) throw new Exception($"Acos failed");
            if (MathF.Abs(result[2] - MathF.Atan(1.0f)) > 0.01f) throw new Exception($"Atan failed");
        });

        [TestMethod]
        public async Task LargeBufferTest() => await RunTest(async accelerator =>
        {
            int len = 4096;
            var data = new int[len]; for (int i = 0; i < len; i++) data[i] = i;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(LargeBufferKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i += 100)
                if (result[i] != i * 2) throw new Exception($"Large buffer failed at {i}. Expected {i * 2}, got {result[i]}");
        });

        [TestMethod]
        public async Task SequentialKernelTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var data = new int[len]; for (int i = 0; i < len; i++) data[i] = i;
            using var buf = accelerator.Allocate1D(data);
            var k1 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(SequentialKernel1);
            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(SequentialKernel2);
            k1((Index1D)len, buf.View); await accelerator.SynchronizeAsync();
            k2((Index1D)len, buf.View); await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != i * 2 + 10) throw new Exception($"Sequential failed at {i}. Expected {i * 2 + 10}, got {result[i]}");
        });

        [TestMethod]
        public async Task UnsignedIntTest() => await RunTest(async accelerator =>
        {
            var data = new uint[] { 100, 100, 100, 100 };
            int len = data.Length;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>>(UnsignedIntKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<uint>();
            uint[] expected = { 33, 0, 101, 200 };
            for (int i = 0; i < len; i++)
                if (result[i] != expected[i]) throw new Exception($"uint failed at {i}. Expected {expected[i]}, got {result[i]}");
        });

        [TestMethod]
        public async Task AtomicMinMaxTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var minData = new int[] { int.MaxValue };
            var maxData = new int[] { int.MinValue };
            using var bufMin = accelerator.Allocate1D(minData);
            using var bufMax = accelerator.Allocate1D(maxData);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(AtomicMinMaxKernel);
            kernel((Index1D)len, bufMin.View, bufMax.View);
            await accelerator.SynchronizeAsync();
            var minResult = await bufMin.CopyToHostAsync<int>();
            var maxResult = await bufMax.CopyToHostAsync<int>();
            if (minResult[0] != 0) throw new Exception($"Atomic min failed. Expected 0, got {minResult[0]}");
            if (maxResult[0] != 63) throw new Exception($"Atomic max failed. Expected 63, got {maxResult[0]}");
        });

        [TestMethod]
        public async Task BufferReuseTest() => await RunTest(async accelerator =>
        {
            int len = 64;
            var data = new int[len]; for (int i = 0; i < len; i++) data[i] = i;
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(BufferReuseKernel);
            for (int iter = 0; iter < 3; iter++) { kernel((Index1D)len, buf.View); await accelerator.SynchronizeAsync(); }
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < len; i++)
                if (result[i] != i + 3) throw new Exception($"Buffer reuse failed at {i}. Expected {i + 3}, got {result[i]}");
        });

        [TestMethod]
        public async Task GridGroupDimensionTest() => await RunTest(async accelerator =>
        {
            int numThreads = 64;
            using var buf = accelerator.Allocate1D<int>(numThreads * 4);
            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>>(GridGroupDimensionKernel);
            kernel(new KernelConfig(1, numThreads), (Index1D)numThreads, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < numThreads; i++)
            {
                int baseIdx = i * 4;
                if (result[baseIdx] != i) throw new Exception($"GlobalId failed at {i}. Got {result[baseIdx]}");
                if (result[baseIdx + 3] != numThreads) throw new Exception($"GroupDim failed at {i}. Got {result[baseIdx + 3]}");
            }
        });

        // Custom warp-shuffle reduction kernel — directly exercises WarpShuffle, LaneIdx, WarpSize,
        // and Atomic.Add on float. Uses a butterfly reduction pattern.
        // NOTE: WGSL has no native atomic<f32>. The WebGPU backend emulates float atomics via a
        // CAS loop on atomic<u32> with bitcast<f32/u32> conversions.
        static void WarpReduceSumKernel(Index1D index, ArrayView<float> input, ArrayView<float> output)
        {
            int i = index;
            float val = i < input.Length ? input[i] : 0f;

            // Warp-level butterfly reduction using shuffle
            int warpSize = Warp.WarpSize;
            for (int offset = warpSize / 2; offset > 0; offset /= 2)
                val += Warp.Shuffle(val, Warp.LaneIdx ^ offset);

            // Lane 0 of each warp atomically accumulates the partial sum.
            // Float atomic add is emulated via CAS loop in the WebGPU backend.
            if (Warp.LaneIdx == 0)
                Atomic.Add(ref output[0], val);
        }

        [TestMethod]
        public async Task ReduceMinMaxTest() => await RunTest(async accelerator =>
        {
            // This kernel uses Warp.Shuffle directly — requires subgroup shuffle support
            RequireFeature(accelerator, "subgroup_shuffle", "Warp shuffle reduction requires 'subgroup_shuffle' feature");

            // Tests warp shuffle emulation (or native subgroups) and float atomic add emulation.
            // Uses 64 elements to match the default WebGPU workgroup size so the butterfly
            // reduction reads from valid lanes. Expected sum = 1+2+...+64 = 2080.
            const int count = 64;
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = i + 1f;
            float expectedSum = count * (count + 1) / 2f;  // 2080

            using var inputBuf = accelerator.Allocate1D(data);
            using var outputBuf = accelerator.Allocate1D<float>(1);
            outputBuf.MemSetToZero();

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(WarpReduceSumKernel);
            kernel((Index1D)count, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<float>();
            if (MathF.Abs(result[0] - expectedSum) > 0.5f)
                throw new Exception($"WarpReduceSum expected {expectedSum}, got {result[0]}");
        });

        // Tests the high-level ILGPU Algorithms Reduce API (GroupExtensions.Reduce + AtomicApply).
        // This is the primary goal: using accelerator.Reduce<T, TReduction>() for min/max/sum.
        // Internally uses warp shuffles, shared memory, and integer atomics.
        [TestMethod]
        public async Task ILGPUReduceTest() => await RunTest(async accelerator =>
        {
            // Use the output-buffer overload to avoid blocking (CopyToCPU would deadlock in WASM).
            const int count = 256;
            var data = new int[count];
            for (int i = 0; i < count; i++) data[i] = i + 1; // 1..256

            using var inputBuf = accelerator.Allocate1D(data);

            // --- Max reduction: max(1..256) = 256 ---
            using var maxOut = accelerator.Allocate1D<int>(1);
            accelerator.Reduce<int, global::ILGPU.Algorithms.ScanReduceOperations.MaxInt32>(
                accelerator.DefaultStream, inputBuf.View, maxOut.View);
            await accelerator.SynchronizeAsync();
            var maxResult = await maxOut.CopyToHostAsync<int>();
            if (maxResult[0] != 256)
                throw new Exception($"Reduce<MaxInt32> expected 256, got {maxResult[0]}. struct={SpawnDev.ILGPU.Wasm.WasmAccelerator._lastStructSerialDebug}");

            // --- Min reduction: min(1..256) = 1 ---
            using var minOut = accelerator.Allocate1D<int>(1);
            accelerator.Reduce<int, global::ILGPU.Algorithms.ScanReduceOperations.MinInt32>(
                accelerator.DefaultStream, inputBuf.View, minOut.View);
            await accelerator.SynchronizeAsync();
            var minResult = await minOut.CopyToHostAsync<int>();
            if (minResult[0] != 1)
                throw new Exception($"Reduce<MinInt32> expected 1, got {minResult[0]}");

            // --- Sum reduction: sum(1..256) = 32896 ---
            using var sumOut = accelerator.Allocate1D<int>(1);
            accelerator.Reduce<int, global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(
                accelerator.DefaultStream, inputBuf.View, sumOut.View);
            await accelerator.SynchronizeAsync();
            var sumResult = await sumOut.CopyToHostAsync<int>();
            int expectedSum2 = count * (count + 1) / 2; // 32896
            if (sumResult[0] != expectedSum2)
                throw new Exception($"Reduce<AddInt32> expected {expectedSum2}, got {sumResult[0]}");
        });

        // Tests the REAL async ReduceAsync API. Before the 2026-05-29 async/sync fix this
        // was Task.Run(() => sync Reduce) — fake async whose inner sync CopyToCPU THREW on
        // WebGPU (no sync GPU->CPU readback) and returned STALE/zero on Wasm (the reduction
        // kernel was still in flight). It now routes through the core virtual
        // AcceleratorStream/Accelerator.SynchronizeAsync drain + ArrayView.CopyToCPUAsync
        // readback, so it returns correct scalars on every backend. Exercises the full
        // dispatch -> real async drain -> async readback path end to end.
        [TestMethod]
        public async Task ILGPUReduceAsyncTest() => await RunTest(async accelerator =>
        {
            const int count = 256;
            var data = new int[count];
            for (int i = 0; i < count; i++) data[i] = i + 1; // 1..256
            using var inputBuf = accelerator.Allocate1D(data);

            int maxResult = await accelerator.ReduceAsync<
                int, global::ILGPU.Algorithms.ScanReduceOperations.MaxInt32>(
                accelerator.DefaultStream, inputBuf.View.BaseView);
            if (maxResult != 256)
                throw new Exception($"ReduceAsync<MaxInt32> expected 256, got {maxResult}");

            int minResult = await accelerator.ReduceAsync<
                int, global::ILGPU.Algorithms.ScanReduceOperations.MinInt32>(
                accelerator.DefaultStream, inputBuf.View.BaseView);
            if (minResult != 1)
                throw new Exception($"ReduceAsync<MinInt32> expected 1, got {minResult}");

            int sumResult = await accelerator.ReduceAsync<
                int, global::ILGPU.Algorithms.ScanReduceOperations.AddInt32>(
                accelerator.DefaultStream, inputBuf.View.BaseView);
            int expectedSum = count * (count + 1) / 2; // 32896
            if (sumResult != expectedSum)
                throw new Exception($"ReduceAsync<AddInt32> expected {expectedSum}, got {sumResult}");
        });

        // Verifies MemSetToZeroAsync: a kernel fills the buffer with nonzero values
        // (unawaited dispatch), then MemSetToZeroAsync must order AFTER that kernel and
        // zero the buffer. On Wasm the sync MemSetToZero would race the in-flight worker
        // kernel (immediate SAB write bypassing the dispatch queue); the async variant
        // drains first. WebGPU records the clear into the same encoder; desktop is
        // stream-ordered. Skipped on WebGL (MemSet is deferred CPU-side upload — readback
        // reads the GPU/worker side, so this CPU-fill pattern isn't meaningful there).
        [TestMethod]
        public async Task MemSetToZeroAsyncTest() => await RunTest(async accelerator =>
        {
            const int count = 64;
            using var buf = accelerator.Allocate1D<int>(count);
            var fill = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
                (i, v) => v[i] = i + 1);
            fill((Index1D)count, buf.View); // unawaited dispatch
            await buf.View.MemSetToZeroAsync(accelerator.DefaultStream);
            var result = await buf.CopyToHostAsync<int>();
            for (int i = 0; i < count; i++)
                if (result[i] != 0)
                    throw new Exception(
                        $"MemSetToZeroAsync: index {i} = {result[i]}, expected 0 (zero-fill " +
                        "did not order after the kernel)");
        });

        // ILGPUReduceSmallTest removed — the main ILGPUReduceTest covers the same functionality.
        // The small test was a diagnostic that exposed a pre-existing WebGL GLSL codegen bug
        // (_idx undeclared identifier in Reduce kernel shader). Not a Wasm issue.

        // ==================== Reduce<float> ====================
        [TestMethod]
        public async Task ILGPUReduceFloatTest() => await RunTest(async accelerator =>
        {
            const int count = 256;
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = (float)(i + 1); // 1f..256f

            using var inputBuf = accelerator.Allocate1D(data);

            // Max
            using var maxOut = accelerator.Allocate1D<float>(1);
            accelerator.Reduce<float, global::ILGPU.Algorithms.ScanReduceOperations.MaxFloat>(
                accelerator.DefaultStream, inputBuf.View, maxOut.View);
            await accelerator.SynchronizeAsync();
            var maxResult = await maxOut.CopyToHostAsync<float>();
            if (maxResult[0] != 256f)
                throw new Exception($"Reduce<MaxFloat> expected 256, got {maxResult[0]}");

            // Min
            using var minOut = accelerator.Allocate1D<float>(1);
            accelerator.Reduce<float, global::ILGPU.Algorithms.ScanReduceOperations.MinFloat>(
                accelerator.DefaultStream, inputBuf.View, minOut.View);
            await accelerator.SynchronizeAsync();
            var minResult = await minOut.CopyToHostAsync<float>();
            if (minResult[0] != 1f)
                throw new Exception($"Reduce<MinFloat> expected 1, got {minResult[0]}");

            // Sum
            using var sumOut = accelerator.Allocate1D<float>(1);
            accelerator.Reduce<float, global::ILGPU.Algorithms.ScanReduceOperations.AddFloat>(
                accelerator.DefaultStream, inputBuf.View, sumOut.View);
            await accelerator.SynchronizeAsync();
            var sumResult = await sumOut.CopyToHostAsync<float>();
            float expectedSum = count * (count + 1) / 2f; // 32896
            if (Math.Abs(sumResult[0] - expectedSum) > 1f)
                throw new Exception($"Reduce<AddFloat> expected {expectedSum}, got {sumResult[0]}");
        });

        // ==================== Reduce<double> ====================
        [TestMethod]
        public async Task ILGPUReduceDoubleTest() => await RunEmulatedTest(async accelerator =>
        {
            const int count = 256;
            var data = new double[count];
            for (int i = 0; i < count; i++) data[i] = (double)(i + 1);

            using var inputBuf = accelerator.Allocate1D(data);

            // Max
            using var maxOut = accelerator.Allocate1D<double>(1);
            accelerator.Reduce<double, global::ILGPU.Algorithms.ScanReduceOperations.MaxDouble>(
                accelerator.DefaultStream, inputBuf.View, maxOut.View);
            await accelerator.SynchronizeAsync();
            var maxResult = await maxOut.CopyToHostAsync<double>();
            if (maxResult[0] != 256.0)
                throw new Exception($"Reduce<MaxDouble> expected 256, got {maxResult[0]}");

            // Min
            using var minOut = accelerator.Allocate1D<double>(1);
            accelerator.Reduce<double, global::ILGPU.Algorithms.ScanReduceOperations.MinDouble>(
                accelerator.DefaultStream, inputBuf.View, minOut.View);
            await accelerator.SynchronizeAsync();
            var minResult = await minOut.CopyToHostAsync<double>();
            if (minResult[0] != 1.0)
                throw new Exception($"Reduce<MinDouble> expected 1, got {minResult[0]}");

            // Sum
            using var sumOut = accelerator.Allocate1D<double>(1);
            accelerator.Reduce<double, global::ILGPU.Algorithms.ScanReduceOperations.AddDouble>(
                accelerator.DefaultStream, inputBuf.View, sumOut.View);
            await accelerator.SynchronizeAsync();
            var sumResult = await sumOut.CopyToHostAsync<double>();
            double expectedSum = count * (count + 1) / 2.0; // 32896
            if (Math.Abs(sumResult[0] - expectedSum) > 1.0)
                throw new Exception($"Reduce<AddDouble> expected {expectedSum}, got {sumResult[0]}");
        });

        // ==================== Reduce<long> ====================
        [TestMethod]
        public async Task ILGPUReduceLongTest() => await RunEmulatedTest(async accelerator =>
        {
            const int count = 256;
            var data = new long[count];
            for (int i = 0; i < count; i++) data[i] = (long)(i + 1);

            using var inputBuf = accelerator.Allocate1D(data);

            // Max
            using var maxOut = accelerator.Allocate1D<long>(1);
            accelerator.Reduce<long, global::ILGPU.Algorithms.ScanReduceOperations.MaxInt64>(
                accelerator.DefaultStream, inputBuf.View, maxOut.View);
            await accelerator.SynchronizeAsync();
            var maxResult = await maxOut.CopyToHostAsync<long>();
            if (maxResult[0] != 256L)
                throw new Exception($"Reduce<MaxInt64> expected 256, got {maxResult[0]}");

            // Min
            using var minOut = accelerator.Allocate1D<long>(1);
            accelerator.Reduce<long, global::ILGPU.Algorithms.ScanReduceOperations.MinInt64>(
                accelerator.DefaultStream, inputBuf.View, minOut.View);
            await accelerator.SynchronizeAsync();
            var minResult = await minOut.CopyToHostAsync<long>();
            if (minResult[0] != 1L)
                throw new Exception($"Reduce<MinInt64> expected 1, got {minResult[0]}");

            // Sum
            using var sumOut = accelerator.Allocate1D<long>(1);
            accelerator.Reduce<long, global::ILGPU.Algorithms.ScanReduceOperations.AddInt64>(
                accelerator.DefaultStream, inputBuf.View, sumOut.View);
            await accelerator.SynchronizeAsync();
            var sumResult = await sumOut.CopyToHostAsync<long>();
            long expectedSum = count * (count + 1L) / 2; // 32896
            if (sumResult[0] != expectedSum)
                throw new Exception($"Reduce<AddInt64> expected {expectedSum}, got {sumResult[0]}");
        });

        // ==================== Reduce<uint> ====================
        [TestMethod]
        public async Task ILGPUReduceUIntTest() => await RunTest(async accelerator =>
        {
            const int count = 256;
            var data = new uint[count];
            for (int i = 0; i < count; i++) data[i] = (uint)(i + 1);

            using var inputBuf = accelerator.Allocate1D(data);

            // Max
            using var maxOut = accelerator.Allocate1D<uint>(1);
            accelerator.Reduce<uint, global::ILGPU.Algorithms.ScanReduceOperations.MaxUInt32>(
                accelerator.DefaultStream, inputBuf.View, maxOut.View);
            await accelerator.SynchronizeAsync();
            var maxResult = await maxOut.CopyToHostAsync<uint>();
            if (maxResult[0] != 256u)
                throw new Exception($"Reduce<MaxUInt32> expected 256, got {maxResult[0]}");

            // Min
            using var minOut = accelerator.Allocate1D<uint>(1);
            accelerator.Reduce<uint, global::ILGPU.Algorithms.ScanReduceOperations.MinUInt32>(
                accelerator.DefaultStream, inputBuf.View, minOut.View);
            await accelerator.SynchronizeAsync();
            var minResult = await minOut.CopyToHostAsync<uint>();
            if (minResult[0] != 1u)
                throw new Exception($"Reduce<MinUInt32> expected 1, got {minResult[0]}");

            // Sum
            using var sumOut = accelerator.Allocate1D<uint>(1);
            accelerator.Reduce<uint, global::ILGPU.Algorithms.ScanReduceOperations.AddUInt32>(
                accelerator.DefaultStream, inputBuf.View, sumOut.View);
            await accelerator.SynchronizeAsync();
            var sumResult = await sumOut.CopyToHostAsync<uint>();
            uint expectedSum = (uint)(count * (count + 1) / 2); // 32896
            if (sumResult[0] != expectedSum)
                throw new Exception($"Reduce<AddUInt32> expected {expectedSum}, got {sumResult[0]}");
        });

        // ==================== Reduce<ulong> ====================
        [TestMethod]
        public async Task ILGPUReduceULongTest() => await RunEmulatedTest(async accelerator =>
        {
            const int count = 256;
            var data = new ulong[count];
            for (int i = 0; i < count; i++) data[i] = (ulong)(i + 1);

            using var inputBuf = accelerator.Allocate1D(data);

            // Max
            using var maxOut = accelerator.Allocate1D<ulong>(1);
            accelerator.Reduce<ulong, global::ILGPU.Algorithms.ScanReduceOperations.MaxUInt64>(
                accelerator.DefaultStream, inputBuf.View, maxOut.View);
            await accelerator.SynchronizeAsync();
            var maxResult = await maxOut.CopyToHostAsync<ulong>();
            if (maxResult[0] != 256UL)
                throw new Exception($"Reduce<MaxUInt64> expected 256, got {maxResult[0]}");

            // Min
            using var minOut = accelerator.Allocate1D<ulong>(1);
            accelerator.Reduce<ulong, global::ILGPU.Algorithms.ScanReduceOperations.MinUInt64>(
                accelerator.DefaultStream, inputBuf.View, minOut.View);
            await accelerator.SynchronizeAsync();
            var minResult = await minOut.CopyToHostAsync<ulong>();
            if (minResult[0] != 1UL)
                throw new Exception($"Reduce<MinUInt64> expected 1, got {minResult[0]}");

            // Sum
            using var sumOut = accelerator.Allocate1D<ulong>(1);
            accelerator.Reduce<ulong, global::ILGPU.Algorithms.ScanReduceOperations.AddUInt64>(
                accelerator.DefaultStream, inputBuf.View, sumOut.View);
            await accelerator.SynchronizeAsync();
            var sumResult = await sumOut.CopyToHostAsync<ulong>();
            ulong expectedSum = (ulong)(count * (count + 1) / 2); // 32896
            if (sumResult[0] != expectedSum)
                throw new Exception($"Reduce<AddUInt64> expected {expectedSum}, got {sumResult[0]}");
        });

        // ==================== Reduce<Half> ====================
        // Unlocked by f16-emulation-plan Phase 4 (2026-04-22): AddHalf/MaxHalf/MinHalf
        // AtomicApply now uses a CAS loop on the u32 containing the Half bits. Exercises
        // multi-workgroup dispatch (count >> MaxNumThreadsPerGroup) to force the cross-
        // workgroup atomic combine step that was previously throwing.
        // WebGL is exempt - vertex shader TF pipeline has no atomic operations at all.
        [TestMethod]
        public async Task ILGPUReduceHalfTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");

            // count chosen to guarantee multi-workgroup dispatch on every backend
            // (max threads/group is 256 on WebGPU, 1024 elsewhere) and to stay within
            // Half's exact integer range for Max (Half exactly represents integers up
            // to 2048).
            const int count = 2048;
            var data = new global::ILGPU.Half[count];
            for (int i = 0; i < count; i++) data[i] = (global::ILGPU.Half)(float)(i + 1); // 1..2048

            using var inputBuf = accelerator.Allocate1D(data);

            // Max: 1..2048 -> max=2048. Exact in Half (2^11 = 2048).
            using var maxOut = accelerator.Allocate1D<global::ILGPU.Half>(1);
            accelerator.Reduce<global::ILGPU.Half, global::ILGPU.Algorithms.ScanReduceOperations.MaxHalf>(
                accelerator.DefaultStream, inputBuf.View, maxOut.View);
            await accelerator.SynchronizeAsync();
            var maxResult = await maxOut.CopyToHostAsync<global::ILGPU.Half>();
            if ((float)maxResult[0] != (float)count)
                throw new Exception($"Reduce<MaxHalf> expected {count}, got {(float)maxResult[0]}");

            // Min: 1..2048 -> min=1. Exact in Half.
            using var minOut = accelerator.Allocate1D<global::ILGPU.Half>(1);
            accelerator.Reduce<global::ILGPU.Half, global::ILGPU.Algorithms.ScanReduceOperations.MinHalf>(
                accelerator.DefaultStream, inputBuf.View, minOut.View);
            await accelerator.SynchronizeAsync();
            var minResult = await minOut.CopyToHostAsync<global::ILGPU.Half>();
            if ((float)minResult[0] != 1f)
                throw new Exception($"Reduce<MinHalf> expected 1, got {(float)minResult[0]}");

            // Sum: Half precision limits the useful sum size. Use all-ones across
            // count=2048 elements -> exact sum = 2048 (last power of 2 Half can hit).
            // This exercises the AtomicApply AddHalf CAS loop with multi-workgroup
            // contention without running into Half accumulation drift.
            var onesData = new global::ILGPU.Half[count];
            for (int i = 0; i < count; i++) onesData[i] = (global::ILGPU.Half)1f;
            using var onesBuf = accelerator.Allocate1D(onesData);

            using var sumOut = accelerator.Allocate1D<global::ILGPU.Half>(1);
            accelerator.Reduce<global::ILGPU.Half, global::ILGPU.Algorithms.ScanReduceOperations.AddHalf>(
                accelerator.DefaultStream, onesBuf.View, sumOut.View);
            await accelerator.SynchronizeAsync();
            var sumResult = await sumOut.CopyToHostAsync<global::ILGPU.Half>();
            // Allow 0.5% tolerance for Half ULP accumulation near 2048 (ULP(2048)=2)
            float expectedSum = count; // 2048
            float actual = (float)sumResult[0];
            float tolerance = expectedSum * 0.005f;
            if (MathF.Abs(actual - expectedSum) > tolerance)
                throw new Exception($"Reduce<AddHalf> expected ~{expectedSum} (tol {tolerance}), got {actual}");
        });

        // ══════════════════════════════════════════════════════════════
        //  i64 Bitwise Atomic Tests
        //  Verifies Atomic.And/Or/Xor on long work correctly when
        //  multiple threads operate on the same 64-bit value.
        //  On WebGPU: i64 is emulated as vec2<u32>, these use
        //  independent i32 atomics on lo/hi halves.
        // ══════════════════════════════════════════════════════════════

        /// <summary>N threads each clear a different bit. Final value should have all N bits cleared.</summary>
        static void AtomicAndInt64Kernel(Index1D index, ArrayView<long> buffer)
        {
            // Each thread clears its own bit: AND with ~(1L << index)
            long mask = ~(1L << (index % 64));
            Atomic.And(ref buffer[0], mask);
        }

        [TestMethod]
        public async Task AtomicAnd_Int64_MultithreadedBitClear() => await RunTest(async accelerator =>
        {
            // Start with all bits set (0xFFFFFFFFFFFFFFFF = -1L)
            using var buf = accelerator.Allocate1D(new long[] { -1L });
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>>(AtomicAndInt64Kernel);

            // 64 threads, each clearing one bit
            kernel((Index1D)64, buf.View);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<long>();
            if (result[0] != 0L)
                throw new Exception($"AtomicAnd i64 failed. Expected 0 (all bits cleared), got {result[0]:X16}");
        });

        /// <summary>N threads each set a different bit. Final value should have all N bits set.</summary>
        static void AtomicOrInt64Kernel(Index1D index, ArrayView<long> buffer)
        {
            // Each thread sets its own bit: OR with (1L << index)
            long mask = 1L << (index % 64);
            Atomic.Or(ref buffer[0], mask);
        }

        [TestMethod]
        public async Task AtomicOr_Int64_MultithreadedBitSet() => await RunTest(async accelerator =>
        {
            // Start with no bits set
            using var buf = accelerator.Allocate1D(new long[] { 0L });
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>>(AtomicOrInt64Kernel);

            // 64 threads, each setting one bit
            kernel((Index1D)64, buf.View);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<long>();
            if (result[0] != -1L) // -1L = all bits set = 0xFFFFFFFFFFFFFFFF
                throw new Exception($"AtomicOr i64 failed. Expected -1 (all bits set), got {result[0]:X16}");
        });

        /// <summary>N threads each XOR the same bit. Even count = original, odd count = flipped.</summary>
        static void AtomicXorInt64Kernel(Index1D index, ArrayView<long> buffer)
        {
            // All threads XOR bit 0. With 64 threads (even), bit 0 should be unchanged.
            // Threads 0-31 XOR low bits, threads 32-63 XOR high bits for coverage.
            long mask = 1L << (index % 64);
            Atomic.Xor(ref buffer[0], mask);
        }

        [TestMethod]
        public async Task AtomicXor_Int64_MultithreadedBitFlip() => await RunTest(async accelerator =>
        {
            // Start with all bits set. 64 threads each XOR a unique bit once = all bits flipped to 0.
            using var buf = accelerator.Allocate1D(new long[] { -1L });
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>>(AtomicXorInt64Kernel);

            // 64 threads, each flipping one unique bit
            kernel((Index1D)64, buf.View);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<long>();
            if (result[0] != 0L)
                throw new Exception($"AtomicXor i64 failed. Expected 0 (all bits flipped), got {result[0]:X16}");
        });

        /// <summary>Realistic VoxelEngine pattern: multiple threads clearing bits in a shared face mask.</summary>
        static void AtomicAndInt64_FaceMaskKernel(Index1D index, ArrayView<long> masks)
        {
            int maskIdx = index / 32; // 2 masks, 32 threads per mask
            int bit = index % 32;

            // Clear bits 0-31 in each mask (simulating greedy merge consuming faces)
            long clearMask = ~(1L << bit);
            Atomic.And(ref masks[maskIdx], clearMask);
        }

        [TestMethod]
        public async Task AtomicAnd_Int64_FaceMaskPattern() => await RunTest(async accelerator =>
        {
            // 2 face masks, each with all 64 bits set
            // 32 threads per mask clear bits 0-31, leaving bits 32-63 set
            using var masks = accelerator.Allocate1D(new long[] { -1L, -1L });
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>>(AtomicAndInt64_FaceMaskKernel);

            kernel((Index1D)64, masks.View);
            await accelerator.SynchronizeAsync();

            var maskResult = await masks.CopyToHostAsync<long>();
            // Bits 0-31 should be cleared, bits 32-63 should remain set
            long expectedMask = unchecked((long)0xFFFFFFFF00000000L);
            if (maskResult[0] != expectedMask)
                throw new Exception($"FaceMask[0] failed. Expected {expectedMask:X16}, got {maskResult[0]:X16}");
            if (maskResult[1] != expectedMask)
                throw new Exception($"FaceMask[1] failed. Expected {expectedMask:X16}, got {maskResult[1]:X16}");
        });

        // ══════════════════════════════════════════════════════════════
        //  Return-inside-loop test
        //  Verifies that `return` inside a for loop exits the kernel,
        //  not just the loop. This was a WGSL codegen bug where
        //  `return` was transpiled as `break`.
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Kernel: threads 0-31 should write 1, threads 32-63 should write 0.
        /// The `return` inside the for loop must exit the kernel entirely.
        /// If it only breaks the loop, all threads would write 1 (bug).
        /// </summary>
        static void ReturnInsideForLoopKernel(Index1D index, ArrayView<int> output)
        {
            // This loop iterates 4 times. If index >= 32, we return on the first iteration.
            // If return is incorrectly transpiled as break, execution falls through to output[index] = 1.
            for (int i = 0; i < 4; i++)
            {
                if (index >= 32 && i == 0)
                    return; // MUST exit the kernel, not just break the loop
            }
            output[index] = 1;
        }

        [TestMethod]
        public async Task ReturnInsideForLoop_ExitsKernel() => await RunTest(async accelerator =>
        {
            var data = new int[64];
            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(ReturnInsideForLoopKernel);

            kernel((Index1D)64, buf.View);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<int>();

            // Threads 0-31: survive the loop, write 1
            for (int i = 0; i < 32; i++)
                if (result[i] != 1) throw new Exception($"Thread {i} should have written 1, got {result[i]}");

            // Threads 32-63: return inside loop, should NOT write 1
            for (int i = 32; i < 64; i++)
                if (result[i] != 0) throw new Exception($"Thread {i} should have written 0 (return should exit kernel), got {result[i]}");
        });
    }
}
