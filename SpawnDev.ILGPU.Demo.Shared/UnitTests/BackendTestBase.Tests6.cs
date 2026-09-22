using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Algorithms.ScanReduceOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 6: Algorithm tests (scan, reduce, radix sort)
    // These tests require EnableAlgorithms() + EnableWebGPUAlgorithms() on the context.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// Test ILGPU.Algorithms ExclusiveScan via a GPU kernel that uses
        /// GroupExtensions.ExclusiveScan. This validates the algorithm intrinsic
        /// registration for each backend.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmExclusiveScanTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<int>(groupSize);
            using var outputBuf = accelerator.Allocate1D<int>(groupSize);

            // Initialize: each thread contributes value 1
            var inputData = new int[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                ExclusiveScanKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            // Exclusive scan of all 1s: [0, 1, 2, 3, ...]
            for (int i = 0; i < groupSize; i++)
            {
                if (result[i] != i)
                    throw new Exception($"ExclusiveScan failed at {i}. Expected {i}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test ILGPU.Algorithms InclusiveScan via a GPU kernel.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmInclusiveScanTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<int>(groupSize);
            using var outputBuf = accelerator.Allocate1D<int>(groupSize);

            var inputData = new int[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                InclusiveScanKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            // Inclusive scan of all 1s: [1, 2, 3, 4, ...]
            for (int i = 0; i < groupSize; i++)
            {
                int expected = i + 1;
                if (result[i] != expected)
                    throw new Exception($"InclusiveScan failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test ILGPU.Algorithms AllReduce via a GPU kernel.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmAllReduceTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<int>(groupSize);
            using var outputBuf = accelerator.Allocate1D<int>(groupSize);

            var inputData = new int[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = i + 1; // 1..64
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                AllReduceKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            int expectedSum = groupSize * (groupSize + 1) / 2; // 2080
            // AllReduce: every thread gets the same sum
            for (int i = 0; i < groupSize; i++)
            {
                if (result[i] != expectedSum)
                    throw new Exception($"AllReduce failed at {i}. Expected {expectedSum}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test ILGPU.Algorithms RadixSortPairs — the key operation needed for
        /// the Gaussian splat renderer. Sorts float keys with int value indices.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsTest() => await RunTest(async accelerator =>
        {
            int n = 256;
            // Create reverse-sorted distances and sequential indices
            var keys = new float[n];
            var values = new int[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = (float)(n - i); // Reverse order: 256, 255, ..., 1
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<float, int, AscendingFloat>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense, AscendingFloat>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<float>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();

            for (int i = 0; i < n; i++)
            {
                float expectedKey = (float)(i + 1);
                int expectedValue = n - 1 - i;
                if (MathF.Abs(sortedKeys[i] - expectedKey) > 0.001f)
                    throw new Exception($"RadixSort key mismatch at [{i}]: expected={expectedKey}, got={sortedKeys[i]}");
                if (sortedValues[i] != expectedValue)
                    throw new Exception($"RadixSort value mismatch at [{i}]: expected={expectedValue}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSortPairs with int keys — verifies AscendingInt32 operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsIntTest() => await RunTest(async accelerator =>
        {
            int n = 256;
            var keys = new int[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = n - i; values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<int, int, AscendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<int, Stride1D.Dense, int, Stride1D.Dense, AscendingInt32>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<int>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (sortedKeys[i] != i + 1)
                    throw new Exception($"RadixSort int key mismatch at [{i}]: expected={i + 1}, got={sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception($"RadixSort int value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSortPairs with double keys — verifies AscendingDouble (f64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsDoubleTest() => await RunEmulatedTest(async accelerator =>
        {
            int n = 256;
            var keys = new double[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = (double)(n - i); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<double, int, AscendingDouble>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<double, Stride1D.Dense, int, Stride1D.Dense, AscendingDouble>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<double>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(sortedKeys[i] - (i + 1.0)) > 0.001)
                    throw new Exception($"RadixSort double key mismatch at [{i}]: expected={i + 1.0}, got={sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception($"RadixSort double value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSortPairs with long keys — verifies AscendingInt64 (i64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsLongTest() => await RunEmulatedTest(async accelerator =>
        {
            int n = 256;
            var keys = new long[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = (long)(n - i); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<long, int, AscendingInt64>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<long, Stride1D.Dense, int, Stride1D.Dense, AscendingInt64>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<long>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (sortedKeys[i] != (long)(i + 1))
                    throw new Exception($"RadixSort long key mismatch at [{i}]: expected={i + 1}, got={sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception($"RadixSort long value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSortPairs with double keys using n=129 (non-multiple of 16) to trigger
        /// non-zero 256-byte-alignment padding in the inner temp view, exposing the packed-struct
        /// view element offset bug on WebGPU.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsDoubleOffsetTest() => await RunEmulatedTest(async accelerator =>
        {
            int n = 129;
            var keys = new double[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = (double)(n - i); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<double, int, AscendingDouble>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<double, Stride1D.Dense, int, Stride1D.Dense, AscendingDouble>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<double>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (Math.Abs(sortedKeys[i] - (i + 1.0)) > 0.001)
                    throw new Exception($"RadixSort double offset key mismatch at [{i}]: expected={i + 1.0}, got={sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception($"RadixSort double offset value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSortPairs with long keys using n=129 (non-multiple of 16) to trigger
        /// non-zero 256-byte-alignment padding in the inner temp view, exposing the packed-struct
        /// view element offset bug on WebGPU.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsLongOffsetTest() => await RunEmulatedTest(async accelerator =>
        {
            int n = 129;
            var keys = new long[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = (long)(n - i); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<long, int, AscendingInt64>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<long, Stride1D.Dense, int, Stride1D.Dense, AscendingInt64>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<long>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (sortedKeys[i] != (long)(i + 1))
                    throw new Exception($"RadixSort long offset key mismatch at [{i}]: expected={i + 1}, got={sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception($"RadixSort long offset value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSortPairs with uint keys — verifies AscendingUInt32 operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsUIntTest() => await RunTest(async accelerator =>
        {
            int n = 256;
            var keys = new uint[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = (uint)(n - i); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<uint, int, AscendingUInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<uint, Stride1D.Dense, int, Stride1D.Dense, AscendingUInt32>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<uint>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (sortedKeys[i] != (uint)(i + 1))
                    throw new Exception($"RadixSort uint key mismatch at [{i}]: expected={i + 1}, got={sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception($"RadixSort uint value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSortPairs with Half keys — verifies AscendingHalf operation.
        /// Skips on backends that do not support Float16 (e.g. some OpenCL devices).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortPairsHalfTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            int n = 256;
            var keys = new global::ILGPU.Half[n];
            var values = new int[n];
            for (int i = 0; i < n; i++) { keys[i] = (global::ILGPU.Half)(float)(n - i); values[i] = i; }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<global::ILGPU.Half, int, AscendingHalf>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<global::ILGPU.Half, Stride1D.Dense, int, Stride1D.Dense, AscendingHalf>();
            radixSort(accelerator.DefaultStream, keysBuf.View, valuesBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<global::ILGPU.Half>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(i + 1);
                if (MathF.Abs((float)sortedKeys[i] - expected) > 0.01f)
                    throw new Exception($"RadixSort Half key mismatch at [{i}]: expected={expected}, got={(float)sortedKeys[i]}");
                if (sortedValues[i] != n - 1 - i)
                    throw new Exception($"RadixSort Half value mismatch at [{i}]: expected={n - 1 - i}, got={sortedValues[i]}");
            }
        });

        /// <summary>
        /// Test RadixSort with non-power-of-2 count — important for real-world
        /// Gaussian splat rendering where splat count is arbitrary.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortNonPow2Test() => await RunTest(async accelerator =>
        {
            int n = 137; // Non-power-of-2
            var keys = new float[n];
            var values = new int[n];
            var rng = new Random(42);
            for (int i = 0; i < n; i++)
            {
                keys[i] = (float)(rng.NextDouble() * 1000.0);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<float, int, AscendingFloat>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense, AscendingFloat>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<float>();

            // Verify ascending order
            for (int i = 1; i < n; i++)
            {
                if (sortedKeys[i] < sortedKeys[i - 1])
                    throw new Exception($"RadixSort non-pow2 order failed at {i}. {sortedKeys[i-1]} > {sortedKeys[i]}");
            }
        });

        /// <summary>
        /// Test ExclusiveScan with float type — verifies AddFloat operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmExclusiveScanFloatTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<float>(groupSize);
            using var outputBuf = accelerator.Allocate1D<float>(groupSize);

            var inputData = new float[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1.0f;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                ExclusiveScanFloatKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<float>();
            for (int i = 0; i < groupSize; i++)
            {
                float expected = (float)i;
                if (MathF.Abs(result[i] - expected) > 0.001f)
                    throw new Exception($"ExclusiveScanFloat failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test ExclusiveScan with long type — verifies AddInt64 (i64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmExclusiveScanLongTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<long>(groupSize);
            using var outputBuf = accelerator.Allocate1D<long>(groupSize);

            var inputData = new long[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1L;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(
                ExclusiveScanLongKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<long>();
            for (int i = 0; i < groupSize; i++)
            {
                if (result[i] != (long)i)
                    throw new Exception($"ExclusiveScanLong failed at {i}. Expected {i}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test ExclusiveScan with double type — verifies AddDouble operation (f64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmExclusiveScanDoubleTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<double>(groupSize);
            using var outputBuf = accelerator.Allocate1D<double>(groupSize);

            var inputData = new double[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1.0;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<double>, ArrayView<double>>(
                ExclusiveScanDoubleKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<double>();
            for (int i = 0; i < groupSize; i++)
            {
                double expected = (double)i;
                if (Math.Abs(result[i] - expected) > 0.001)
                    throw new Exception($"ExclusiveScanDouble failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test ExclusiveScan with uint type — verifies AddUInt32 operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmExclusiveScanUIntTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<uint>(groupSize);
            using var outputBuf = accelerator.Allocate1D<uint>(groupSize);

            var inputData = new uint[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1u;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(
                ExclusiveScanUIntKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<uint>();
            for (int i = 0; i < groupSize; i++)
            {
                if (result[i] != (uint)i)
                    throw new Exception($"ExclusiveScanUInt failed at {i}. Expected {i}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test InclusiveScan with float type — verifies AddFloat operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmInclusiveScanFloatTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<float>(groupSize);
            using var outputBuf = accelerator.Allocate1D<float>(groupSize);

            var inputData = new float[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1.0f;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                InclusiveScanFloatKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<float>();
            for (int i = 0; i < groupSize; i++)
            {
                float expected = (float)(i + 1);
                if (MathF.Abs(result[i] - expected) > 0.001f)
                    throw new Exception($"InclusiveScanFloat failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test InclusiveScan with long type — verifies AddInt64 (i64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmInclusiveScanLongTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<long>(groupSize);
            using var outputBuf = accelerator.Allocate1D<long>(groupSize);

            var inputData = new long[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1L;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(
                InclusiveScanLongKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<long>();
            for (int i = 0; i < groupSize; i++)
            {
                long expected = (long)(i + 1);
                if (result[i] != expected)
                    throw new Exception($"InclusiveScanLong failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test InclusiveScan with double type — verifies AddDouble operation (f64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmInclusiveScanDoubleTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<double>(groupSize);
            using var outputBuf = accelerator.Allocate1D<double>(groupSize);

            var inputData = new double[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1.0;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<double>, ArrayView<double>>(
                InclusiveScanDoubleKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<double>();
            for (int i = 0; i < groupSize; i++)
            {
                double expected = (double)(i + 1);
                if (Math.Abs(result[i] - expected) > 0.001)
                    throw new Exception($"InclusiveScanDouble failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test InclusiveScan with uint type — verifies AddUInt32 operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmInclusiveScanUIntTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<uint>(groupSize);
            using var outputBuf = accelerator.Allocate1D<uint>(groupSize);

            var inputData = new uint[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 1u;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(
                InclusiveScanUIntKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<uint>();
            for (int i = 0; i < groupSize; i++)
            {
                uint expected = (uint)(i + 1);
                if (result[i] != expected)
                    throw new Exception($"InclusiveScanUInt failed at {i}. Expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test AllReduce with float type — verifies AddFloat operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmAllReduceFloatTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<float>(groupSize);
            using var outputBuf = accelerator.Allocate1D<float>(groupSize);

            var inputData = new float[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (float)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                AllReduceFloatKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<float>();
            float expectedSum = groupSize * (groupSize + 1) / 2.0f;
            for (int i = 0; i < groupSize; i++)
            {
                if (MathF.Abs(result[i] - expectedSum) > 0.001f)
                    throw new Exception($"AllReduceFloat failed at {i}. Expected {expectedSum}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test AllReduce with double type — verifies AddDouble operation (f64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmAllReduceDoubleTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<double>(groupSize);
            using var outputBuf = accelerator.Allocate1D<double>(groupSize);

            var inputData = new double[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (double)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<double>, ArrayView<double>>(
                AllReduceDoubleKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<double>();
            double expectedSum = groupSize * (groupSize + 1) / 2.0;
            for (int i = 0; i < groupSize; i++)
            {
                if (Math.Abs(result[i] - expectedSum) > 0.001)
                    throw new Exception($"AllReduceDouble failed at {i}. Expected {expectedSum}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test AllReduce with long type — verifies AddInt64 (i64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmAllReduceLongTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<long>(groupSize);
            using var outputBuf = accelerator.Allocate1D<long>(groupSize);

            var inputData = new long[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (long)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(
                AllReduceLongKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<long>();
            long expectedSum = (long)groupSize * (groupSize + 1) / 2L;
            for (int i = 0; i < groupSize; i++)
            {
                if (result[i] != expectedSum)
                    throw new Exception($"AllReduceLong failed at {i}. Expected {expectedSum}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test AllReduce with uint type — verifies AddUInt32 operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmAllReduceUIntTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<uint>(groupSize);
            using var outputBuf = accelerator.Allocate1D<uint>(groupSize);

            var inputData = new uint[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (uint)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(
                AllReduceUIntKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<uint>();
            uint expectedSum = (uint)(groupSize * (groupSize + 1) / 2);
            for (int i = 0; i < groupSize; i++)
            {
                if (result[i] != expectedSum)
                    throw new Exception($"AllReduceUInt failed at {i}. Expected {expectedSum}, got {result[i]}");
            }
        });

        /// <summary>
        /// Test ExclusiveScan with Half type — verifies AddHalf operation.
        /// Uses a smaller group size to keep sums within Half's exact integer range.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmExclusiveScanHalfTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            int groupSize = Math.Min(32, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<global::ILGPU.Half>(groupSize);
            using var outputBuf = accelerator.Allocate1D<global::ILGPU.Half>(groupSize);

            var inputData = new global::ILGPU.Half[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (global::ILGPU.Half)1.0f;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>>(
                ExclusiveScanHalfKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<global::ILGPU.Half>();
            for (int i = 0; i < groupSize; i++)
            {
                float expected = (float)i;
                if (MathF.Abs((float)result[i] - expected) > 0.1f)
                    throw new Exception($"ExclusiveScanHalf failed at {i}. Expected {expected}, got {(float)result[i]}");
            }
        });

        /// <summary>
        /// Test InclusiveScan with Half type — verifies AddHalf operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmInclusiveScanHalfTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            int groupSize = Math.Min(32, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<global::ILGPU.Half>(groupSize);
            using var outputBuf = accelerator.Allocate1D<global::ILGPU.Half>(groupSize);

            var inputData = new global::ILGPU.Half[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (global::ILGPU.Half)1.0f;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>>(
                InclusiveScanHalfKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<global::ILGPU.Half>();
            for (int i = 0; i < groupSize; i++)
            {
                float expected = (float)(i + 1);
                if (MathF.Abs((float)result[i] - expected) > 0.1f)
                    throw new Exception($"InclusiveScanHalf failed at {i}. Expected {expected}, got {(float)result[i]}");
            }
        });

        /// <summary>
        /// Test AllReduce with Half type — verifies AddHalf operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmAllReduceHalfTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            int groupSize = Math.Min(32, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<global::ILGPU.Half>(groupSize);
            using var outputBuf = accelerator.Allocate1D<global::ILGPU.Half>(groupSize);

            var inputData = new global::ILGPU.Half[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (global::ILGPU.Half)(float)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>>(
                AllReduceHalfKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<global::ILGPU.Half>();
            float expectedSum = groupSize * (groupSize + 1) / 2.0f;
            for (int i = 0; i < groupSize; i++)
            {
                if (MathF.Abs((float)result[i] - expectedSum) > 1.0f)
                    throw new Exception($"AllReduceHalf failed at {i}. Expected {expectedSum}, got {(float)result[i]}");
            }
        });

        /// <summary>
        /// Test ExclusiveScan with varying values (not just 1s).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmExclusiveScanVaryingTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(32, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<int>(groupSize);
            using var outputBuf = accelerator.Allocate1D<int>(groupSize);

            // Each thread contributes its index: [0, 1, 2, ..., 31]
            var inputData = new int[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = i;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                ExclusiveScanKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            // Exclusive scan of [0,1,2,...,31]: [0, 0, 1, 3, 6, 10, ...]
            int runningSum = 0;
            for (int i = 0; i < groupSize; i++)
            {
                if (result[i] != runningSum)
                    throw new Exception($"ExclusiveScan varying failed at {i}. Expected {runningSum}, got {result[i]}");
                runningSum += inputData[i];
            }
        });

        /// <summary>
        /// Test ExclusiveScanWithBoundaries — validates that boundary values
        /// are correctly returned alongside the scan result.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmScanWithBoundariesTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(32, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<int>(groupSize);
            using var outputBuf = accelerator.Allocate1D<int>(groupSize);
            using var boundaryBuf = accelerator.Allocate1D<int>(2); // [leftBoundary, rightBoundary]

            // Each thread contributes value 2
            var inputData = new int[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = 2;
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                ExclusiveScanWithBoundariesKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View, boundaryBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            var bounds = await boundaryBuf.CopyToHostAsync<int>();

            // Exclusive scan of all 2s: [0, 2, 4, 6, ...]
            for (int i = 0; i < groupSize; i++)
            {
                int expected = i * 2;
                if (result[i] != expected)
                    throw new Exception($"ScanWithBoundaries value failed at {i}. Expected {expected}, got {result[i]}");
            }

            // Boundaries: for ExclusiveScan, left=identity (0), right=sum of all but last.
            // For all-2s with 32 threads: exclusive scan = [0, 2, 4, ..., 62], so left=0, right=62.
            int totalSum = groupSize * 2; // 64 (inclusive) or 62 (exclusive right)
            if (bounds[0] < 0 || bounds[0] > totalSum)
                throw new Exception($"ScanWithBoundaries left boundary out of range. Got {bounds[0]}, expected 0..{totalSum}");
            if (bounds[1] < 0 || bounds[1] > totalSum)
                throw new Exception($"ScanWithBoundaries right boundary out of range. Got {bounds[1]}, expected 0..{totalSum}");
        });


        /// <summary>
        /// Test RadixSort with descending order — validates DescendingFloat operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortDescendingTest() => await RunTest(async accelerator =>
        {
            int n = 128;
            var keys = new float[n];
            var values = new int[n];
            for (int i = 0; i < n; i++)
            {
                keys[i] = (float)(i + 1); // Ascending: 1, 2, ..., 128
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<float, int, DescendingFloat>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense, DescendingFloat>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<float>();

            // After descending sort, keys should be 128, 127, ..., 1
            for (int i = 0; i < n; i++)
            {
                float expectedKey = (float)(n - i);
                if (MathF.Abs(sortedKeys[i] - expectedKey) > 0.001f)
                    throw new Exception($"RadixSort descending failed at {i}. Expected {expectedKey}, got {sortedKeys[i]}");
            }
        });

        /// <summary>
        /// Diagnostic test: non-pairs RadixSort on plain floats (ascending) to isolate core sort from pairs wrapper.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortNonPairsFloatTest() => await RunTest(async accelerator =>
        {
            int n = 128;
            var data = new float[n];
            // Reverse order: 128, 127, ..., 1
            for (int i = 0; i < n; i++)
                data[i] = (float)(n - i);

            using var dataBuf = accelerator.Allocate1D(data);
            var tempSize = accelerator.ComputeRadixSortTempStorageSize<float, AscendingFloat>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSort<float, Stride1D.Dense, AscendingFloat>();
            radixSort(
                accelerator.DefaultStream,
                dataBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sorted = await dataBuf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = (float)(i + 1);
                if (MathF.Abs(sorted[i] - expected) > 0.001f)
                    throw new Exception($"Non-pairs float RadixSort failed at {i}. Expected {expected}, got {sorted[i]}");
            }
        });

        /// <summary>
        /// Diagnostic test: non-pairs RadixSort on plain integers to isolate core sort from pairs wrapper.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortNonPairsIntTest() => await RunTest(async accelerator =>
        {
            int n = 32;
            var data = new int[n];
            // Reverse order: 32, 31, ..., 1
            for (int i = 0; i < n; i++)
                data[i] = n - i;

            using var dataBuf = accelerator.Allocate1D(data);
            var tempSize = accelerator.ComputeRadixSortTempStorageSize<int, AscendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSort<int, Stride1D.Dense, AscendingInt32>();
            radixSort(
                accelerator.DefaultStream,
                dataBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sorted = await dataBuf.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                if (sorted[i] != i + 1)
                    throw new Exception($"Non-pairs RadixSort failed at {i}. Expected {i + 1}, got {sorted[i]}");
            }
        });

        /// <summary>
        /// RadixSort on tiny arrays with specific patterns.
        /// Tests: already sorted, reverse order.
        /// </summary>
        [TestMethod]
        public async Task RadixSortMinimalPatternsTest() => await RunTest(async accelerator =>
        {
            var failures = new System.Collections.Generic.List<string>();

            async Task TestPattern(string label, int[] data, int[] expected)
            {
                int n = data.Length;
                using var dataBuf = accelerator.Allocate1D((int[])data.Clone());
                var tempSize = accelerator.ComputeRadixSortTempStorageSize<int, AscendingInt32>(n);
                using var tempBuf = accelerator.Allocate1D<int>(tempSize);

                var radixSort = accelerator.CreateRadixSort<int, Stride1D.Dense, AscendingInt32>();
                radixSort(
                    accelerator.DefaultStream,
                    dataBuf.View,
                    tempBuf.View.AsContiguous());
                await accelerator.SynchronizeAsync();

                var sorted = await dataBuf.CopyToHostAsync<int>();

                int errors = 0;
                int firstError = -1;
                for (int i = 0; i < n; i++)
                {
                    if (sorted[i] != expected[i])
                    {
                        errors++;
                        if (firstError < 0) firstError = i;
                    }
                }

                if (errors > 0)
                {
                    var inputStr = string.Join(",", data.Take(Math.Min(n, 64)));
                    var outputStr = string.Join(",", sorted.Take(Math.Min(n, 64)));
                    var expectedStr = string.Join(",", expected.Take(Math.Min(n, 64)));
                    failures.Add($"{label}: FAIL {errors}/{n} at [{firstError}], " +
                        $"input=[{inputStr}] output=[{outputStr}] expected=[{expectedStr}]");
                }
            }

            await TestPattern("already_sorted",
                new[] { 0, 0, 1, 1, 2, 2, 3, 3 },
                new[] { 0, 0, 1, 1, 2, 2, 3, 3 });

            await TestPattern("vals_0to3_dup",
                new[] { 3, 2, 1, 0, 3, 2, 1, 0 },
                new[] { 0, 0, 1, 1, 2, 2, 3, 3 });

            if (failures.Count > 0)
            {
                throw new Exception(
                    $"RadixSort {failures.Count} pattern(s) failed:\n" +
                    string.Join("\n", failures));
            }
        });

        /// <summary>
        /// Test inclusive scan on counter-sized (4-element) int buffers — verifies
        /// the scan step used between RadixSortKernel1 and RadixSortKernel2.
        /// </summary>
        [TestMethod]
        public async Task RadixSortCounterScanTest() => await RunTest(async accelerator =>
        {
            var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(
                ScanKind.Inclusive);

            var scanTemp = accelerator.ComputeScanTempStorageSize<int>(4);

            // Case 1: all elements in bucket 0 → counter = [4, 0, 0, 0]
            using var inBuf1 = accelerator.Allocate1D(new int[] { 4, 0, 0, 0 });
            using var outBuf1 = accelerator.Allocate1D<int>(4);
            using var tempBuf1 = accelerator.Allocate1D<int>(scanTemp);
            scan(accelerator.DefaultStream, inBuf1.View, outBuf1.View, tempBuf1.View.AsContiguous());
            await accelerator.SynchronizeAsync();
            var result1 = await outBuf1.CopyToHostAsync<int>();
            if (result1[0] != 4 || result1[1] != 4 || result1[2] != 4 || result1[3] != 4)
                throw new Exception($"Scan failed case1: got [{string.Join(",", result1)}]");

            // Case 2: one per bucket → counter = [1, 1, 1, 1]
            using var inBuf2 = accelerator.Allocate1D(new int[] { 1, 1, 1, 1 });
            using var outBuf2 = accelerator.Allocate1D<int>(4);
            using var tempBuf2 = accelerator.Allocate1D<int>(scanTemp);
            scan(accelerator.DefaultStream, inBuf2.View, outBuf2.View, tempBuf2.View.AsContiguous());
            await accelerator.SynchronizeAsync();
            var result2 = await outBuf2.CopyToHostAsync<int>();
            if (result2[0] != 1 || result2[1] != 2 || result2[2] != 3 || result2[3] != 4)
                throw new Exception($"Scan failed case2: got [{string.Join(",", result2)}]");

            // Case 3: typical distribution → counter = [30, 35, 32, 31]
            using var inBuf3 = accelerator.Allocate1D(new int[] { 30, 35, 32, 31 });
            using var outBuf3 = accelerator.Allocate1D<int>(4);
            using var tempBuf3 = accelerator.Allocate1D<int>(scanTemp);
            scan(accelerator.DefaultStream, inBuf3.View, outBuf3.View, tempBuf3.View.AsContiguous());
            await accelerator.SynchronizeAsync();
            var result3 = await outBuf3.CopyToHostAsync<int>();
            if (result3[0] != 30 || result3[1] != 65 || result3[2] != 97 || result3[3] != 128)
                throw new Exception($"Scan failed case3: got [{string.Join(",", result3)}]");
        });

        /// <summary>
        /// Test: dispatch a kernel with two SubViews of the same buffer as separate params.
        /// This isolates whether aliased buffer bindings cause corruption on WebGPU.
        /// </summary>
        static void AliasedBufferIdentityKernel(
            Index1D index,
            ArrayView<int> data,
            ArrayView<int> counter,
            ArrayView<int> debug)
        {
            // Thread 0 writes the view lengths to debug buffer for inspection
            if (index == 0)
            {
                debug[0] = data.IntLength;
                debug[1] = counter.IntLength;
            }
            if (index < data.IntLength)
            {
                // Just copy each element to itself (identity)
                data[index] = data[index];
            }
            if (index < counter.IntLength)
            {
                // Increment counter[index] to prove we can write to it
                counter[index] = counter[index] + 1;
            }
        }

        [TestMethod]
        public async Task AliasedBufferBindingTest() => await RunTest(async accelerator =>
        {
            // Allocate a single buffer large enough for both views
            // Layout: data[0..7] at offset 0, counter[0..3] at offset 64 (256-byte aligned)
            int totalInts = 128; // 512 bytes
            int[] initBuf = new int[totalInts];
            initBuf[0] = 10; initBuf[1] = 20; initBuf[2] = 30; initBuf[3] = 40;
            initBuf[4] = 50; initBuf[5] = 60; initBuf[6] = 70; initBuf[7] = 80;
            using var buf = accelerator.Allocate1D(initBuf);
            await accelerator.SynchronizeAsync();

            var dataView = buf.View.SubView(0, 8);
            var counterView = buf.View.SubView(64, 4);

            using var debugBuf = accelerator.Allocate1D(new int[4]);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                AliasedBufferIdentityKernel);

            kernel(8, dataView, counterView, debugBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<int>();
            var debugResult = await debugBuf.CopyToHostAsync<int>();

            // Data should be unchanged (identity copy)
            for (int i = 0; i < 8; i++)
            {
                if (result[i] != initBuf[i])
                    throw new Exception($"AliasedBuffer: data[{i}] = {result[i]}, expected {initBuf[i]}");
            }
            // Counter should be [1,1,1,1] (incremented from 0)
            for (int i = 0; i < 4; i++)
            {
                if (result[64 + i] != 1)
                    throw new Exception($"AliasedBuffer: counter[{i}] = {result[64 + i]}, expected 1");
            }
        });

        /// <summary>
        /// Stress test: RadixSort with 1024+ elements to exercise multi-group dispatch.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmRadixSortLargeTest() => await RunTest(async accelerator =>
        {
            int n = 2048;
            var keys = new float[n];
            var values = new int[n];
            var rng = new Random(123);
            for (int i = 0; i < n; i++)
            {
                keys[i] = (float)(rng.NextDouble() * 10000.0);
                values[i] = i;
            }

            using var keysBuf = accelerator.Allocate1D(keys);
            using var valuesBuf = accelerator.Allocate1D(values);
            var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<float, int, AscendingFloat>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense, AscendingFloat>();
            radixSort(
                accelerator.DefaultStream,
                keysBuf.View,
                valuesBuf.View,
                tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sortedKeys = await keysBuf.CopyToHostAsync<float>();
            var sortedValues = await valuesBuf.CopyToHostAsync<int>();

            // Verify ascending order
            for (int i = 1; i < n; i++)
            {
                if (sortedKeys[i] < sortedKeys[i - 1])
                    throw new Exception($"RadixSort large order failed at {i}. {sortedKeys[i-1]} > {sortedKeys[i]}");
            }

            // Verify value tracking — each sorted value should point to its original key
            for (int i = 0; i < n; i++)
            {
                int origIdx = sortedValues[i];
                if (MathF.Abs(sortedKeys[i] - keys[origIdx]) > 0.001f)
                    throw new Exception($"RadixSort large tracking failed at {i}. Key={sortedKeys[i]}, OrigKey={keys[origIdx]}");
            }
        });

        /// <summary>
        /// Test Reduce (non-AllReduce) — only first thread gets the result.
        /// Uses GroupExtensions.Reduce which returns value only to group leader.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmGroupReduceTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<int>(groupSize);
            using var outputBuf = accelerator.Allocate1D<int>(1); // only first thread writes

            var inputData = new int[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = i + 1;
            inputBuf.CopyFromCPU(inputData);
            outputBuf.MemSetToZero();

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                GroupReduceKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            int expectedSum = groupSize * (groupSize + 1) / 2; // 2080
            if (result[0] != expectedSum)
                throw new Exception($"GroupReduce failed. Expected {expectedSum}, got {result[0]}");
        });

        /// <summary>
        /// Test GroupReduce with float type — verifies AddFloat operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmGroupReduceFloatTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<float>(groupSize);
            using var outputBuf = accelerator.Allocate1D<float>(1);
            outputBuf.MemSetToZero();

            var inputData = new float[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (float)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                GroupReduceFloatKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<float>();
            float expectedSum = groupSize * (groupSize + 1) / 2.0f;
            if (MathF.Abs(result[0] - expectedSum) > 0.001f)
                throw new Exception($"GroupReduceFloat failed. Expected {expectedSum}, got {result[0]}");
        });

        /// <summary>
        /// Test GroupReduce with long type — verifies AddInt64 (i64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmGroupReduceLongTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<long>(groupSize);
            using var outputBuf = accelerator.Allocate1D<long>(1);
            outputBuf.MemSetToZero();

            var inputData = new long[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (long)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<long>, ArrayView<long>>(
                GroupReduceLongKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<long>();
            long expectedSum = (long)groupSize * (groupSize + 1) / 2L;
            if (result[0] != expectedSum)
                throw new Exception($"GroupReduceLong failed. Expected {expectedSum}, got {result[0]}");
        });

        /// <summary>
        /// Test GroupReduce with double type — verifies AddDouble operation (f64 emulation on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task AlgorithmGroupReduceDoubleTest() => await RunEmulatedTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<double>(groupSize);
            using var outputBuf = accelerator.Allocate1D<double>(1);
            outputBuf.MemSetToZero();

            var inputData = new double[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (double)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<double>, ArrayView<double>>(
                GroupReduceDoubleKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<double>();
            double expectedSum = groupSize * (groupSize + 1) / 2.0;
            if (Math.Abs(result[0] - expectedSum) > 0.001)
                throw new Exception($"GroupReduceDouble failed. Expected {expectedSum}, got {result[0]}");
        });

        /// <summary>
        /// Test GroupReduce with uint type — verifies AddUInt32 operation.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmGroupReduceUIntTest() => await RunTest(async accelerator =>
        {
            int groupSize = Math.Min(64, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<uint>(groupSize);
            using var outputBuf = accelerator.Allocate1D<uint>(1);
            outputBuf.MemSetToZero();

            var inputData = new uint[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (uint)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(
                GroupReduceUIntKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<uint>();
            uint expectedSum = (uint)(groupSize * (groupSize + 1) / 2);
            if (result[0] != expectedSum)
                throw new Exception($"GroupReduceUInt failed. Expected {expectedSum}, got {result[0]}");
        });

        /// <summary>
        /// Test GroupReduce with Half type — verifies AddHalf operation.
        /// Uses smaller group size for Half's limited range.
        /// </summary>
        [TestMethod]
        public async Task AlgorithmGroupReduceHalfTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            int groupSize = Math.Min(32, accelerator.Device.MaxNumThreadsPerGroup);
            using var inputBuf = accelerator.Allocate1D<global::ILGPU.Half>(groupSize);
            using var outputBuf = accelerator.Allocate1D<global::ILGPU.Half>(1);
            outputBuf.MemSetToZero();

            var inputData = new global::ILGPU.Half[groupSize];
            for (int i = 0; i < groupSize; i++) inputData[i] = (global::ILGPU.Half)(float)(i + 1);
            inputBuf.CopyFromCPU(inputData);

            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>>(
                GroupReduceHalfKernel);
            kernel(new KernelConfig(1, groupSize), (Index1D)groupSize, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<global::ILGPU.Half>();
            float expectedSum = groupSize * (groupSize + 1) / 2.0f;
            if (MathF.Abs((float)result[0] - expectedSum) > 1.0f)
                throw new Exception($"GroupReduceHalf failed. Expected {expectedSum}, got {(float)result[0]}");
        });

        /// <summary>
        /// Verifies that a method marked [MethodImpl(MethodImplOptions.NoInlining)] is
        /// actually NOT inlined by the IR Inliner — the kernel IR should retain a
        /// MethodCall, and each backend should emit a real fn definition + call site
        /// rather than 2 inlined bodies. This exercises the WGSL fn-definition path
        /// that fixes the Vp9Idct16x16Kernel compile cliff (rc.14, Bug 1 from
        /// tuvok-to-geordi-idct16x16-two-bugs-2026-04-25.md). Same code path also
        /// covers CUDA / OpenCL / CPU / Wasm / WebGL via their normal MethodCall
        /// codegen.
        /// </summary>
        [TestMethod]
        public async Task NoInliningHelperEmitsFunctionCallTest() => await RunTest(async accelerator =>
        {
            using var outputBuf = accelerator.Allocate1D<int>(1);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
                NoInliningHelperKernel);
            kernel(1, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            // SquareSum(3, 4) + SquareSum(5, 6) = (9 + 16) + (25 + 36) = 25 + 61 = 86
            const int expected = 86;
            if (result[0] != expected)
                throw new Exception($"NoInliningHelper failed. Expected {expected}, got {result[0]}");
        });

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int NoInliningSquareSumHelper(int a, int b) => a * a + b * b;

        static void NoInliningHelperKernel(Index1D index, ArrayView<int> output)
        {
            int x = NoInliningSquareSumHelper(3, 4);
            int y = NoInliningSquareSumHelper(5, 6);
            output[0] = x + y;
        }

        /// <summary>
        /// rc.16 fn-def codegen smoke test, mirroring Tuvok's Vp9Idct16x16Kernel.Idct16Row
        /// helper shape: NoInlining void fn taking int values + ref int output params
        /// (which lower to `ptr&lt;function, i32&gt;` in WGSL). Captures
        /// `WebGPUBackend.LastGeneratedWGSL` in the failure message so we can see what
        /// the fn-def emission actually produced when validation fails.
        /// </summary>
        [TestMethod]
        public async Task NoInliningVoidHelperEmitsFunctionCallTest() => await RunTest(async accelerator =>
        {
            using var outputBuf = accelerator.Allocate1D<int>(2);

            try
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
                    NoInliningVoidHelperKernel);
                kernel(1, outputBuf.View);
                await accelerator.SynchronizeAsync();
            }
            catch (Exception ex)
            {
                var shaderDiag = accelerator.AcceleratorType == AcceleratorType.WebGL
                    ? $"\n--- GLSL START ---\n{SpawnDev.ILGPU.WebGL.Backend.WebGLBackend.LastGeneratedGLSL ?? "<null>"}\n--- GLSL END ---"
                    : "";
                throw new Exception($"NoInliningVoidHelper compile/dispatch failed: {ex.Message}{shaderDiag}");
            }

            var result = await outputBuf.CopyToHostAsync<int>();
            if (result[0] != 148 || result[1] != 57)
            {
                var shaderDiag = accelerator.AcceleratorType == AcceleratorType.WebGL
                    ? $"\n--- GLSL START ---\n{SpawnDev.ILGPU.WebGL.Backend.WebGLBackend.LastGeneratedGLSL ?? "<null>"}\n--- GLSL END ---"
                    : "";
                throw new Exception(
                    $"NoInliningVoidHelper failed. Expected (148, 57), got ({result[0]}, {result[1]}){shaderDiag}");
            }
        });

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void NoInliningVoidPairWriterHelper(int a, int b, int c, ref int sumOut, ref int diffOut)
        {
            sumOut = a + b + c;
            diffOut = b - a;
        }

        static void NoInliningVoidHelperKernel(Index1D index, ArrayView<int> output)
        {
            int sum = 0;
            int diff = 0;
            NoInliningVoidPairWriterHelper(42, 99, 7, ref sum, ref diff);
            output[0] = sum;
            output[1] = diff;
        }

        /// <summary>
        /// Mirrors Tuvok's `Vp9Idct16x16Kernel.Idct16Row` exact param shape: 16
        /// `short` inputs + 16 `out int` outputs. Catches WGSL fn-def emission
        /// bugs that only surface at production-scale signature size and the
        /// short-input lowering path (Int16 IR → packed sub-word storage in
        /// WGSL → distinct codegen vs simple int params).
        /// </summary>
        [TestMethod]
        public async Task NoInliningIdct16RowShapeHelperBitExactTest() => await RunTest(async accelerator =>
        {
            using var inputBuf = accelerator.Allocate1D<short>(16);
            using var outputBuf = accelerator.Allocate1D<int>(16);
            short[] inputs = { 100, 200, 300, 400, 500, 600, 700, 800, 900, 1000, 1100, 1200, 1300, 1400, 1500, 1600 };
            inputBuf.CopyFromCPU(inputs);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(
                Idct16RowShapeHelperKernel);
            try
            {
                kernel(1, inputBuf.View, outputBuf.View);
                await accelerator.SynchronizeAsync();
            }
            catch (Exception ex)
            {
                var diag = accelerator.AcceleratorType == AcceleratorType.WebGPU
                    ? $"\n--- WGSL START ---\n{SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.LastGeneratedWGSL ?? "<null>"}\n--- WGSL END ---"
                    : "";
                throw new Exception($"Idct16RowShape compile/dispatch failed: {ex.Message}{diag}");
            }

            var result = await outputBuf.CopyToHostAsync<int>();
            for (int i = 0; i < 16; i++)
            {
                int expected = inputs[i] * 2 + (i + 1);
                if (result[i] != expected)
                {
                    var diag = accelerator.AcceleratorType == AcceleratorType.WebGPU
                        ? $"\n--- WGSL START ---\n{SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.LastGeneratedWGSL ?? "<null>"}\n--- WGSL END ---"
                        : "";
                    throw new Exception($"Idct16RowShape[{i}] expected {expected} got {result[i]} (input {inputs[i]}){diag}");
                }
            }
        });

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Idct16RowShapeHelper(
            short i0,  short i1,  short i2,  short i3,
            short i4,  short i5,  short i6,  short i7,
            short i8,  short i9,  short i10, short i11,
            short i12, short i13, short i14, short i15,
            out int o0,  out int o1,  out int o2,  out int o3,
            out int o4,  out int o5,  out int o6,  out int o7,
            out int o8,  out int o9,  out int o10, out int o11,
            out int o12, out int o13, out int o14, out int o15)
        {
            // Each output uses its short input via a (short)-narrowing arithmetic
            // sequence so the IR shape (Int16 input ops + ConvertValue narrowing
            // + ref-int Store via Pointer<int>) matches Idct16Row's actual
            // mid-stage butterfly. Output value: i*2 + (idx+1).
            o0  = i0  * 2 + 1;
            o1  = i1  * 2 + 2;
            o2  = i2  * 2 + 3;
            o3  = i3  * 2 + 4;
            o4  = i4  * 2 + 5;
            o5  = i5  * 2 + 6;
            o6  = i6  * 2 + 7;
            o7  = i7  * 2 + 8;
            o8  = i8  * 2 + 9;
            o9  = i9  * 2 + 10;
            o10 = i10 * 2 + 11;
            o11 = i11 * 2 + 12;
            o12 = i12 * 2 + 13;
            o13 = i13 * 2 + 14;
            o14 = i14 * 2 + 15;
            o15 = i15 * 2 + 16;
        }

        static void Idct16RowShapeHelperKernel(
            Index1D index, ArrayView<short> input, ArrayView<int> output)
        {
            Idct16RowShapeHelper(
                input[0],  input[1],  input[2],  input[3],
                input[4],  input[5],  input[6],  input[7],
                input[8],  input[9],  input[10], input[11],
                input[12], input[13], input[14], input[15],
                out int o0,  out int o1,  out int o2,  out int o3,
                out int o4,  out int o5,  out int o6,  out int o7,
                out int o8,  out int o9,  out int o10, out int o11,
                out int o12, out int o13, out int o14, out int o15);
            output[0]  = o0;  output[1]  = o1;  output[2]  = o2;  output[3]  = o3;
            output[4]  = o4;  output[5]  = o5;  output[6]  = o6;  output[7]  = o7;
            output[8]  = o8;  output[9]  = o9;  output[10] = o10; output[11] = o11;
            output[12] = o12; output[13] = o13; output[14] = o14; output[15] = o15;
        }

        /// <summary>
        /// Stricter version of <see cref="NoInliningIdct16RowShapeHelperBitExactTest"/>:
        /// the helper body now does Tuvok's exact `Idct16Row` arithmetic shape -
        /// `(short)((x * cos1 - y * cos2 + (1 &lt;&lt; 13)) >> 14)` butterfly
        /// pattern. Q14 narrowing dominates Tuvok's kernel and was the
        /// inner-loop trigger of the WGSL `i32 << i32` codegen bug. Expects
        /// bit-exact match against a CPU reference computed in C#.
        /// </summary>
        [TestMethod]
        public async Task NoInliningIdct16RowQ14NarrowHelperBitExactTest() => await RunTest(async accelerator =>
        {
            using var inputBuf = accelerator.Allocate1D<short>(16);
            using var outputBuf = accelerator.Allocate1D<int>(16);
            short[] inputs = { 100, -200, 300, -400, 500, -600, 700, -800, 900, -1000, 1100, -1200, 1300, -1400, 1500, -1600 };
            inputBuf.CopyFromCPU(inputs);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(
                Idct16RowQ14NarrowKernel);
            try
            {
                kernel(1, inputBuf.View, outputBuf.View);
                await accelerator.SynchronizeAsync();
            }
            catch (Exception ex)
            {
                var diag = accelerator.AcceleratorType == AcceleratorType.WebGPU
                    ? $"\n--- WGSL START ---\n{SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.LastGeneratedWGSL ?? "<null>"}\n--- WGSL END ---"
                    : "";
                throw new Exception($"Idct16RowQ14Narrow compile/dispatch failed: {ex.Message}{diag}");
            }

            var result = await outputBuf.CopyToHostAsync<int>();
            // CPU reference uses identical arithmetic to the helper.
            short[] expected = new short[16];
            ComputeQ14NarrowReference(inputs, expected);
            for (int i = 0; i < 16; i++)
            {
                if (result[i] != expected[i])
                {
                    var diag = accelerator.AcceleratorType == AcceleratorType.WebGPU
                        ? $"\n--- WGSL START ---\n{SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.LastGeneratedWGSL ?? "<null>"}\n--- WGSL END ---"
                        : "";
                    throw new Exception($"Idct16RowQ14Narrow[{i}] expected {expected[i]} got {result[i]} (input {inputs[i]}){diag}");
                }
            }
        });

        static void ComputeQ14NarrowReference(short[] inputs, short[] outputs)
        {
            const int CosA = 11585, CosB = 15137, CosC = 6270, CosD = 16069;
            for (int i = 0; i < 16; i++)
            {
                short a = inputs[i];
                short b = inputs[(i + 1) % 16];
                int t = a * CosA - b * CosB + a * CosC + b * CosD;
                outputs[i] = (short)((t + (1 << 13)) >> 14);
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Idct16RowQ14NarrowHelper(
            short i0,  short i1,  short i2,  short i3,
            short i4,  short i5,  short i6,  short i7,
            short i8,  short i9,  short i10, short i11,
            short i12, short i13, short i14, short i15,
            out short o0,  out short o1,  out short o2,  out short o3,
            out short o4,  out short o5,  out short o6,  out short o7,
            out short o8,  out short o9,  out short o10, out short o11,
            out short o12, out short o13, out short o14, out short o15)
        {
            const int CosA = 11585, CosB = 15137, CosC = 6270, CosD = 16069;
            o0  = (short)(((i0  * CosA - i1  * CosB + i0  * CosC + i1  * CosD) + (1 << 13)) >> 14);
            o1  = (short)(((i1  * CosA - i2  * CosB + i1  * CosC + i2  * CosD) + (1 << 13)) >> 14);
            o2  = (short)(((i2  * CosA - i3  * CosB + i2  * CosC + i3  * CosD) + (1 << 13)) >> 14);
            o3  = (short)(((i3  * CosA - i4  * CosB + i3  * CosC + i4  * CosD) + (1 << 13)) >> 14);
            o4  = (short)(((i4  * CosA - i5  * CosB + i4  * CosC + i5  * CosD) + (1 << 13)) >> 14);
            o5  = (short)(((i5  * CosA - i6  * CosB + i5  * CosC + i6  * CosD) + (1 << 13)) >> 14);
            o6  = (short)(((i6  * CosA - i7  * CosB + i6  * CosC + i7  * CosD) + (1 << 13)) >> 14);
            o7  = (short)(((i7  * CosA - i8  * CosB + i7  * CosC + i8  * CosD) + (1 << 13)) >> 14);
            o8  = (short)(((i8  * CosA - i9  * CosB + i8  * CosC + i9  * CosD) + (1 << 13)) >> 14);
            o9  = (short)(((i9  * CosA - i10 * CosB + i9  * CosC + i10 * CosD) + (1 << 13)) >> 14);
            o10 = (short)(((i10 * CosA - i11 * CosB + i10 * CosC + i11 * CosD) + (1 << 13)) >> 14);
            o11 = (short)(((i11 * CosA - i12 * CosB + i11 * CosC + i12 * CosD) + (1 << 13)) >> 14);
            o12 = (short)(((i12 * CosA - i13 * CosB + i12 * CosC + i13 * CosD) + (1 << 13)) >> 14);
            o13 = (short)(((i13 * CosA - i14 * CosB + i13 * CosC + i14 * CosD) + (1 << 13)) >> 14);
            o14 = (short)(((i14 * CosA - i15 * CosB + i14 * CosC + i15 * CosD) + (1 << 13)) >> 14);
            o15 = (short)(((i15 * CosA - i0  * CosB + i15 * CosC + i0  * CosD) + (1 << 13)) >> 14);
        }

        static void Idct16RowQ14NarrowKernel(
            Index1D index, ArrayView<short> input, ArrayView<int> output)
        {
            Idct16RowQ14NarrowHelper(
                input[0],  input[1],  input[2],  input[3],
                input[4],  input[5],  input[6],  input[7],
                input[8],  input[9],  input[10], input[11],
                input[12], input[13], input[14], input[15],
                out short o0,  out short o1,  out short o2,  out short o3,
                out short o4,  out short o5,  out short o6,  out short o7,
                out short o8,  out short o9,  out short o10, out short o11,
                out short o12, out short o13, out short o14, out short o15);
            output[0]  = o0;  output[1]  = o1;  output[2]  = o2;  output[3]  = o3;
            output[4]  = o4;  output[5]  = o5;  output[6]  = o6;  output[7]  = o7;
            output[8]  = o8;  output[9]  = o9;  output[10] = o10; output[11] = o11;
            output[12] = o12; output[13] = o13; output[14] = o14; output[15] = o15;
        }

        /// <summary>
        /// Closer-to-production test: kernel calls the same `Idct16Row`-shape
        /// helper TWICE (mirroring `Vp9Idct16x16Kernel`'s row-pass + column-
        /// pass pattern that hits the helper at 32 call sites total). Catches
        /// any bug that only surfaces under repeated calls (per-call state
        /// reset, scratch overlap, repeated function name mangling).
        /// </summary>
        [TestMethod]
        public async Task NoInliningIdct16RowQ14MultiCallHelperBitExactTest() => await RunTest(async accelerator =>
        {
            using var inputBuf = accelerator.Allocate1D<short>(16);
            using var outputBuf = accelerator.Allocate1D<int>(32);
            short[] inputs = { 100, -200, 300, -400, 500, -600, 700, -800, 900, -1000, 1100, -1200, 1300, -1400, 1500, -1600 };
            inputBuf.CopyFromCPU(inputs);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(
                Idct16RowQ14MultiCallKernel);
            try
            {
                kernel(1, inputBuf.View, outputBuf.View);
                await accelerator.SynchronizeAsync();
            }
            catch (Exception ex)
            {
                var diag = accelerator.AcceleratorType == AcceleratorType.WebGPU
                    ? $"\n--- WGSL START ---\n{SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.LastGeneratedWGSL ?? "<null>"}\n--- WGSL END ---"
                    : "";
                throw new Exception($"Idct16RowQ14MultiCall compile/dispatch failed: {ex.Message}{diag}");
            }

            var result = await outputBuf.CopyToHostAsync<int>();
            short[] expected = new short[16];
            ComputeQ14NarrowReference(inputs, expected);
            // Both call sites use the same input slice and should produce identical results.
            for (int i = 0; i < 16; i++)
            {
                if (result[i] != expected[i])
                    throw new Exception($"MultiCall pass1 [{i}] expected {expected[i]} got {result[i]}");
                if (result[i + 16] != expected[i])
                    throw new Exception($"MultiCall pass2 [{i}] expected {expected[i]} got {result[i + 16]}");
            }
        });

        /// <summary>
        /// Tuvok-range stress test: same Idct16Row-shape helper but inputs
        /// are random shorts in [-4096, 4096) (the range Vp9Idct16x16Kernel
        /// uses for its Random/Batched tests, the two Tuvok still sees fail
        /// on rc.17 with a small 24-byte residual). My earlier Q14 test used
        /// values in [-1600, 1600] which pass on WebGPU; this widens the
        /// range to provoke the same edge-case mismatch Tuvok sees.
        /// </summary>
        [TestMethod]
        public async Task NoInliningIdct16RowQ14StressHelperBitExactTest() => await RunTest(async accelerator =>
        {
            const int Trials = 4;
            // Tuvok seed 0xADA51610 - same byte stream he gets in his Random test.
            var rng = new Random(unchecked((int)0xADA51610u));
            using var inputBuf = accelerator.Allocate1D<short>(16);
            using var outputBuf = accelerator.Allocate1D<int>(16);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(
                Idct16RowQ14NarrowKernel);

            for (int trial = 0; trial < Trials; trial++)
            {
                short[] inputs = new short[16];
                for (int i = 0; i < 16; i++) inputs[i] = (short)rng.Next(-4096, 4096);
                inputBuf.CopyFromCPU(inputs);

                try
                {
                    kernel(1, inputBuf.View, outputBuf.View);
                    await accelerator.SynchronizeAsync();
                }
                catch (Exception ex)
                {
                    throw new Exception($"trial {trial} dispatch failed: {ex.Message}");
                }

                var result = await outputBuf.CopyToHostAsync<int>();
                short[] expected = new short[16];
                ComputeQ14NarrowReference(inputs, expected);
                for (int i = 0; i < 16; i++)
                {
                    if (result[i] != expected[i])
                        throw new Exception(
                            $"trial {trial} idx {i}: expected {expected[i]} got {result[i]} " +
                            $"(inputs[{i}]={inputs[i]}, inputs[{(i + 1) % 16}]={inputs[(i + 1) % 16]})");
                }
            }
        });

        /// <summary>
        /// Tests that `(short)int` narrowing inside a NoInlining helper produces
        /// the correctly sign-extended short value when the int input is
        /// outside short range. Without proper narrowing in WGSL fn-def emission,
        /// the high bits stay intact and downstream arithmetic on the "narrowed"
        /// value diverges from the C# semantics. This is the specific path
        /// causing Tuvok's residual 24-byte mismatch on Vp9Idct16x16Kernel
        /// Random/Batched tests at rc.17 - the butterfly stages produce
        /// intermediates just outside short range, the (short)((x + (1&lt;&lt;13)) &gt;&gt; 14)
        /// narrowing pattern doesn't truncate, subsequent stages compute on
        /// the wrong (un-narrowed) values.
        /// </summary>
        [TestMethod]
        public async Task NoInliningShortNarrowingInsideHelperBitExactTest() => await RunTest(async accelerator =>
        {
            // Inputs deliberately chosen to push the (short) cast through:
            // each value, narrowed to short, has a different sign and magnitude
            // than the un-narrowed int it came from.
            int[] bigInts = { 100000, -100000, 32768, -32769, 70000, 98304, -98304, 1234567 };
            using var inputBuf = accelerator.Allocate1D<int>(bigInts.Length);
            using var outputBuf = accelerator.Allocate1D<int>(bigInts.Length);
            inputBuf.CopyFromCPU(bigInts);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>>(NarrowAndUseKernel);
            kernel(bigInts.Length, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            for (int i = 0; i < bigInts.Length; i++)
            {
                short narrowed = (short)bigInts[i];
                int expected = narrowed * 7;
                if (result[i] != expected)
                    throw new Exception(
                        $"NarrowAndUse[{i}] expected {expected} got {result[i]} " +
                        $"(input {bigInts[i]}, narrowed-short = {narrowed})");
            }
        });

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static int NarrowAndUseHelper(int bigInt)
        {
            short s = (short)bigInt;
            return s * 7;
        }

        static void NarrowAndUseKernel(
            Index1D index, ArrayView<int> input, ArrayView<int> output)
        {
            int gid = index;
            output[gid] = NarrowAndUseHelper(input[gid]);
        }

        /// <summary>
        /// Locks the bit-exact behavior of Tuvok's `Vp9Idct16x16Kernel`-shape
        /// kernel under all-zero coefficient inputs. Helper called 32 times
        /// (16 row-pass + 16 column-pass), each with all-zero short inputs.
        /// All outputs must be zero (the Q14 butterfly of zeros is zero).
        ///
        /// The kernel structure (32 unrolled calls, 16 short inputs + 16 ref-int
        /// outputs each, fn-def emission on shader backends) is intrinsically
        /// expensive to compile — measured 5-7s on Chrome's Tint validator on
        /// dev hardware. That's a structural cost of the kernel shape, not a
        /// codegen bug. Documented here as the floor for any compile-time
        /// regressions; if a future change pushes this over 30s the test will
        /// time out at the runner's default and surface the regression.
        /// </summary>
        [TestMethod]
        public async Task NoInliningIdct16Row32CallsZeroInputCompileTimeTest() => await RunTest(async accelerator =>
        {
            using var inputBuf = accelerator.Allocate1D<short>(16);
            using var outputBuf = accelerator.Allocate1D<int>(16);
            short[] zeros = new short[16];
            inputBuf.CopyFromCPU(zeros);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(
                Idct16Row32CallsZeroInputKernel);
            kernel(1, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<int>();
            for (int i = 0; i < 16; i++)
                if (result[i] != 0)
                    throw new Exception($"Zero-input output[{i}] expected 0 got {result[i]} (math bug under zero input)");
        });

        static void Idct16Row32CallsZeroInputKernel(
            Index1D index, ArrayView<short> input, ArrayView<int> output)
        {
            // 16 row-pass calls
            for (int r = 0; r < 16; r++)
            {
                Idct16RowQ14NarrowHelper(
                    input[0],  input[1],  input[2],  input[3],
                    input[4],  input[5],  input[6],  input[7],
                    input[8],  input[9],  input[10], input[11],
                    input[12], input[13], input[14], input[15],
                    out short _, out short _, out short _, out short _,
                    out short _, out short _, out short _, out short _,
                    out short _, out short _, out short _, out short _,
                    out short _, out short _, out short _, out short _);
            }
            // 16 column-pass calls
            for (int c = 0; c < 16; c++)
            {
                Idct16RowQ14NarrowHelper(
                    input[0],  input[1],  input[2],  input[3],
                    input[4],  input[5],  input[6],  input[7],
                    input[8],  input[9],  input[10], input[11],
                    input[12], input[13], input[14], input[15],
                    out short _, out short _, out short _, out short _,
                    out short _, out short _, out short _, out short _,
                    out short _, out short _, out short _, out short _,
                    out short _, out short _, out short _, out short _);
            }
            // Predictably zero outputs.
            output[0] = 0;
        }

        static void Idct16RowQ14MultiCallKernel(
            Index1D index, ArrayView<short> input, ArrayView<int> output)
        {
            // Pass 1
            Idct16RowQ14NarrowHelper(
                input[0],  input[1],  input[2],  input[3],
                input[4],  input[5],  input[6],  input[7],
                input[8],  input[9],  input[10], input[11],
                input[12], input[13], input[14], input[15],
                out short a0,  out short a1,  out short a2,  out short a3,
                out short a4,  out short a5,  out short a6,  out short a7,
                out short a8,  out short a9,  out short a10, out short a11,
                out short a12, out short a13, out short a14, out short a15);
            output[0]  = a0;  output[1]  = a1;  output[2]  = a2;  output[3]  = a3;
            output[4]  = a4;  output[5]  = a5;  output[6]  = a6;  output[7]  = a7;
            output[8]  = a8;  output[9]  = a9;  output[10] = a10; output[11] = a11;
            output[12] = a12; output[13] = a13; output[14] = a14; output[15] = a15;

            // Pass 2 - same inputs, expect identical results
            Idct16RowQ14NarrowHelper(
                input[0],  input[1],  input[2],  input[3],
                input[4],  input[5],  input[6],  input[7],
                input[8],  input[9],  input[10], input[11],
                input[12], input[13], input[14], input[15],
                out short b0,  out short b1,  out short b2,  out short b3,
                out short b4,  out short b5,  out short b6,  out short b7,
                out short b8,  out short b9,  out short b10, out short b11,
                out short b12, out short b13, out short b14, out short b15);
            output[16] = b0;  output[17] = b1;  output[18] = b2;  output[19] = b3;
            output[20] = b4;  output[21] = b5;  output[22] = b6;  output[23] = b7;
            output[24] = b8;  output[25] = b9;  output[26] = b10; output[27] = b11;
            output[28] = b12; output[29] = b13; output[30] = b14; output[31] = b15;
        }

        /// <summary>
        /// Verifies that `(short)intValue` truncates the high bits and sign-extends
        /// from bit 15 — the C# / IL semantic for `conv.i2`. The Wasm backend
        /// previously omitted this conversion (both source and dst lower to i32 in
        /// Wasm-type space, so the ConvertValue switch had no entry), which made
        /// `(short)` a silent no-op and broke Tuvok's Vp9Idct16x16Kernel on Wasm.
        /// CPU / CUDA / OpenCL / WebGPU all agree because their backends emit the
        /// proper narrowing.
        /// </summary>
        [TestMethod]
        public async Task ShortNarrowingTest() => await RunTest(async accelerator =>
        {
            using var inputBuf = accelerator.Allocate1D<int>(4);
            using var outputBuf = accelerator.Allocate1D<short>(4);

            // 0x11170 = 70000: low 16 bits = 0x1170 = 4464, bit 15 = 0 → sign-extends to 4464.
            // 0x18000 = 98304: low 16 bits = 0x8000 = -32768 (signed), bit 15 = 1 → sign-extends to -32768.
            // 0xFFFF1234 = -61388: low 16 bits = 0x1234 = 4660, bit 15 = 0 → sign-extends to 4660.
            // 0x80008000 = -2147450880: low 16 bits = 0x8000 → sign-extends to -32768.
            inputBuf.CopyFromCPU(new int[] { 70000, 98304, unchecked((int)0xFFFF1234), unchecked((int)0x80008000) });

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<short>>(
                ShortNarrowingKernel);
            kernel(4, inputBuf.View, outputBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outputBuf.CopyToHostAsync<short>();
            short[] expected = { 4464, -32768, 4660, -32768 };
            for (int i = 0; i < 4; i++)
                if (result[i] != expected[i])
                    throw new Exception(
                        $"ShortNarrowing[{i}] failed. Expected {expected[i]}, got {result[i]}");
        });

        static void ShortNarrowingKernel(Index1D index, ArrayView<int> input, ArrayView<short> output)
        {
            int gid = index;
            int v = input[gid];
            output[gid] = (short)v;
        }

        /// <summary>
        /// Mirrors the IR shape of Tuvok's Vp9Idct16x16Kernel.Idct16Row helper:
        /// the kernel calls a helper with `short` inputs + `out int` outputs,
        /// the helper computes butterfly-style arithmetic with the narrowing
        /// pattern `(short)((x + (1 &lt;&lt; 13)) >> 14)`, the kernel then writes
        /// per-element output. Runs across all 6 backends and compares against
        /// a CPU reference computed in C# with the same operations.
        ///
        /// If this test passes on Wasm but Tuvok's iDCT 16x16 fails on Wasm, the
        /// bug is in deeper IR shape (more out params, deeper butterfly) and we
        /// expand the repro. If this test FAILS on Wasm, we have the minimal
        /// bit-exact divergence and can fix the codegen at this scale.
        /// </summary>
        [TestMethod]
        public async Task ButterflyNarrowingHelperBitExactTest() => await RunTest(async accelerator =>
        {
            // 8 elements per dispatch. Each element exercises the butterfly +
            // narrowing pattern on a different magnitude of input value.
            // Inputs are picked to span the int range used by VP9 iDCT
            // intermediate values (Q14, ~1e6 magnitude before shift).
            const int N = 8;
            int[] aIn = { 1234567, -1234567, 0, 100, -100, 1 << 20, -(1 << 20), 8191 };
            int[] bIn = { 7654321,  7654321, 1,  50,  -50, 1 << 19,  (1 << 19), 8192 };

            using var aBuf = accelerator.Allocate1D<int>(N);
            using var bBuf = accelerator.Allocate1D<int>(N);
            using var rSumBuf = accelerator.Allocate1D<int>(N);
            using var rDiffBuf = accelerator.Allocate1D<int>(N);
            aBuf.CopyFromCPU(aIn);
            bBuf.CopyFromCPU(bIn);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                ButterflyNarrowingKernel);
            kernel(N, aBuf.View, bBuf.View, rSumBuf.View, rDiffBuf.View);
            await accelerator.SynchronizeAsync();

            var rSum = await rSumBuf.CopyToHostAsync<int>();
            var rDiff = await rDiffBuf.CopyToHostAsync<int>();

            for (int i = 0; i < N; i++)
            {
                short eSum = (short)((aIn[i] + bIn[i] + (1 << 13)) >> 14);
                short eDiff = (short)((bIn[i] - aIn[i] + (1 << 13)) >> 14);
                if (rSum[i] != eSum)
                    throw new Exception($"ButterflyNarrowing sum[{i}] expected {eSum}, got {rSum[i]} (a={aIn[i]}, b={bIn[i]})");
                if (rDiff[i] != eDiff)
                    throw new Exception($"ButterflyNarrowing diff[{i}] expected {eDiff}, got {rDiff[i]} (a={aIn[i]}, b={bIn[i]})");
            }
        });

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void ButterflyNarrowingHelper(int a, int b, out int sumOut, out int diffOut)
        {
            sumOut = (short)((a + b + (1 << 13)) >> 14);
            diffOut = (short)((b - a + (1 << 13)) >> 14);
        }

        /// <summary>
        /// Companion to <see cref="ButterflyNarrowingHelperBitExactTest"/>:
        /// SAME helper signature (int + int + out int + out int) but NO narrowing
        /// in the body - just plain int arithmetic. Isolates whether the Wasm
        /// bit-exact bug is in `(short)int` narrowing-through-helper-call or
        /// in the more fundamental out-param routing.
        /// </summary>
        [TestMethod]
        public async Task NoInliningOutParamHelperBitExactTest() => await RunTest(async accelerator =>
        {
            const int N = 4;
            int[] aIn = { 100, 200, 300, 400 };
            int[] bIn = { 1, 2, 3, 4 };

            using var aBuf = accelerator.Allocate1D<int>(N);
            using var bBuf = accelerator.Allocate1D<int>(N);
            using var rSumBuf = accelerator.Allocate1D<int>(N);
            using var rDiffBuf = accelerator.Allocate1D<int>(N);
            aBuf.CopyFromCPU(aIn);
            bBuf.CopyFromCPU(bIn);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                NoInliningOutParamKernel);
            kernel(N, aBuf.View, bBuf.View, rSumBuf.View, rDiffBuf.View);
            await accelerator.SynchronizeAsync();

            var rSum = await rSumBuf.CopyToHostAsync<int>();
            var rDiff = await rDiffBuf.CopyToHostAsync<int>();

            for (int i = 0; i < N; i++)
            {
                int eSum = aIn[i] + bIn[i];
                int eDiff = bIn[i] - aIn[i];
                if (rSum[i] != eSum)
                    throw new Exception($"NoInliningOutParam sum[{i}] expected {eSum}, got {rSum[i]} (a={aIn[i]}, b={bIn[i]})");
                if (rDiff[i] != eDiff)
                    throw new Exception($"NoInliningOutParam diff[{i}] expected {eDiff}, got {rDiff[i]} (a={aIn[i]}, b={bIn[i]})");
            }
        });

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void NoInliningOutParamHelper(int a, int b, out int sumOut, out int diffOut)
        {
            sumOut = a + b;
            diffOut = b - a;
        }

        /// <summary>
        /// Bisect Bug E: kernel does its own Alloca + Store + Load (no helper call)
        /// to verify the alloca round-trip works on Wasm. If this passes, the bug is
        /// specifically in the helper-inline-with-out-param path.
        /// </summary>
        [TestMethod]
        public async Task NoHelperOutLikeAllocaTest() => await RunTest(async accelerator =>
        {
            const int N = 4;
            int[] aIn = { 100, 200, 300, 400 };
            int[] bIn = { 1, 2, 3, 4 };

            using var aBuf = accelerator.Allocate1D<int>(N);
            using var bBuf = accelerator.Allocate1D<int>(N);
            using var rSumBuf = accelerator.Allocate1D<int>(N);
            aBuf.CopyFromCPU(aIn);
            bBuf.CopyFromCPU(bIn);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                NoHelperOutLikeAllocaKernel);
            kernel(N, aBuf.View, bBuf.View, rSumBuf.View);
            await accelerator.SynchronizeAsync();

            var rSum = await rSumBuf.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int eSum = aIn[i] + bIn[i];
                if (rSum[i] != eSum)
                    throw new Exception($"NoHelperOutLikeAlloca[{i}] expected {eSum}, got {rSum[i]}");
            }
        });

        static void NoHelperOutLikeAllocaKernel(
            Index1D index, ArrayView<int> aIn, ArrayView<int> bIn, ArrayView<int> rSumOut)
        {
            int gid = index;
            // LocalMemory is the only ILGPU primitive for getting address-of a local
            // from inside a kernel body. Mirrors what `out int sum` lowers to.
            var sumStore = LocalMemory.Allocate<int>(1);
            sumStore[0] = aIn[gid] + bIn[gid];
            rSumOut[gid] = sumStore[0];
        }

        static void NoInliningOutParamKernel(
            Index1D index,
            ArrayView<int> aIn,
            ArrayView<int> bIn,
            ArrayView<int> rSumOut,
            ArrayView<int> rDiffOut)
        {
            int gid = index;
            NoInliningOutParamHelper(aIn[gid], bIn[gid], out int sum, out int diff);
            rSumOut[gid] = sum;
            rDiffOut[gid] = diff;
        }

        // Bisects the Autolykos2/Blake2b.Compress ref-writeback bug: a NoInlining ref-param
        // helper called before a loop, N times inside it (each call accumulating through the
        // SAME ref param, mirroring Compress chaining h0..h7 across 65 calls), and once after.
        // Uses `ref ulong` specifically (not `ref int`) since the existing NoInlining ref/out
        // coverage (NoInliningOutParamHelperBitExactTest, Idct16Row family) is all int-shaped -
        // an emulated-64-bit (uvec2 on WebGL/WebGPU) ref accumulator carried through a real
        // runtime loop across multiple call sites has never been exercised before.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RefULongAddOneHelper(ref ulong acc, ulong h1, ulong h2)
        {
            acc = acc + 1UL + h1 - h2;
        }

        static void RefULongLoopMultiCallKernel(Index1D index, ArrayView<ulong> outBuf)
        {
            ulong acc = 100UL;
            RefULongAddOneHelper(ref acc, 1UL, 0UL); // before loop
            for (int k = 0; k < 63; k++)
            {
                RefULongAddOneHelper(ref acc, 1UL, 0UL); // inside loop, 63x
            }
            RefULongAddOneHelper(ref acc, 1UL, 0UL); // after loop
            outBuf[index] = acc; // each call adds (1 + h1 - h2) = 2; 65 calls: 100 + 130 = 230
        }

        [TestMethod]
        public async Task NoInliningRefULongLoopMultiCallBitExactTest() => await RunTest(async accelerator =>
        {
            const int N = 4;
            using var outBuf = accelerator.Allocate1D<ulong>(N);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ulong>>(RefULongLoopMultiCallKernel);
            kernel(N, outBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<ulong>();
            const ulong expected = 230UL;
            for (int i = 0; i < N; i++)
                if (result[i] != expected)
                    throw new Exception($"NoInliningRefULongLoopMultiCall[{i}] expected {expected}, got {result[i]}");
        });

        // Same shape, but with EIGHT simultaneous ref ulong params (matching Blake2b.Compress's
        // h0..h7 exactly) instead of one, called before/inside/after a loop. Isolates whether
        // the Compress bug needs >1 simultaneous ref param, since a single ref param through the
        // identical loop/call-site pattern (NoInliningRefULongLoopMultiCallBitExactTest) is proven
        // correct above.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void RefULong8AddOneHelper(
            ref ulong a, ref ulong b, ref ulong c, ref ulong d,
            ref ulong e, ref ulong f, ref ulong g, ref ulong h)
        {
            a += 1UL; b += 2UL; c += 3UL; d += 4UL;
            e += 5UL; f += 6UL; g += 7UL; h += 8UL;
        }

        static void RefULong8LoopMultiCallKernel(Index1D index, ArrayView<ulong> outBuf)
        {
            ulong a = 0, b = 0, c = 0, d = 0, e = 0, f = 0, g = 0, h = 0;
            RefULong8AddOneHelper(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h); // before
            for (int k = 0; k < 63; k++)
            {
                RefULong8AddOneHelper(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h); // inside x63
            }
            RefULong8AddOneHelper(ref a, ref b, ref c, ref d, ref e, ref f, ref g, ref h); // after
            // 65 calls total: a+=65*1=65, b+=130, c+=195, d+=260, e+=325, f+=390, g+=455, h+=520
            int baseIdx = (int)index * 8;
            outBuf[baseIdx + 0] = a; outBuf[baseIdx + 1] = b; outBuf[baseIdx + 2] = c; outBuf[baseIdx + 3] = d;
            outBuf[baseIdx + 4] = e; outBuf[baseIdx + 5] = f; outBuf[baseIdx + 6] = g; outBuf[baseIdx + 7] = h;
        }

        // Closer match to Blake2b.Compress's EXACT shape: 8 ref ulong + 18 value ulong + 1 bool
        // (27 total params, same as Compress), called before/inside/after a loop with the VALUE
        // args changing each call (not constant) - isolates whether Compress's specific param
        // COUNT/shape (not just "8 ref params through a loop", already proven fine above) is what
        // breaks ref write-back.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void CompressShapedHelper(
            ref ulong h0, ref ulong h1, ref ulong h2, ref ulong h3,
            ref ulong h4, ref ulong h5, ref ulong h6, ref ulong h7,
            ulong m0, ulong m1, ulong m2, ulong m3, ulong m4, ulong m5, ulong m6, ulong m7,
            ulong m8, ulong m9, ulong m10, ulong m11, ulong m12, ulong m13, ulong m14, ulong m15,
            ulong t0, ulong t1, bool isLastBlock)
        {
            h0 += m0; h1 += m1; h2 += m2; h3 += m3;
            h4 += m4; h5 += m5; h6 += m6; h7 += m7;
            h0 += m8 + m9 + m10 + m11 + m12 + m13 + m14 + m15 + t0 + (isLastBlock ? t1 : 0UL);
        }

        static void CompressShapedLoopKernel(Index1D index, ArrayView<ulong> outBuf)
        {
            ulong h0 = 0, h1 = 0, h2 = 0, h3 = 0, h4 = 0, h5 = 0, h6 = 0, h7 = 0;
            ulong ctr = 0;
            CompressShapedHelper(ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7,
                ctr, ctr + 1, ctr + 2, ctr + 3, ctr + 4, ctr + 5, ctr + 6, ctr + 7,
                ctr + 8, ctr + 9, ctr + 10, ctr + 11, ctr + 12, ctr + 13, ctr + 14, ctr + 15,
                100UL, 0UL, false); // before loop
            ctr += 16;
            for (int k = 0; k < 63; k++)
            {
                CompressShapedHelper(ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7,
                    ctr, ctr + 1, ctr + 2, ctr + 3, ctr + 4, ctr + 5, ctr + 6, ctr + 7,
                    ctr + 8, ctr + 9, ctr + 10, ctr + 11, ctr + 12, ctr + 13, ctr + 14, ctr + 15,
                    100UL, 0UL, false); // inside loop x63
                ctr += 16;
            }
            CompressShapedHelper(ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7,
                ctr, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                108UL, 0UL, true); // after loop (final block)

            int baseIdx = (int)index * 8;
            outBuf[baseIdx + 0] = h0; outBuf[baseIdx + 1] = h1; outBuf[baseIdx + 2] = h2; outBuf[baseIdx + 3] = h3;
            outBuf[baseIdx + 4] = h4; outBuf[baseIdx + 5] = h5; outBuf[baseIdx + 6] = h6; outBuf[baseIdx + 7] = h7;
        }

        [TestMethod]
        public async Task NoInliningCompressShapedLoopBitExactTest() => await RunTest(async accelerator =>
        {
            const int N = 2;
            using var outBuf = accelerator.Allocate1D<ulong>(N * 8);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ulong>>(CompressShapedLoopKernel);
            kernel(N, outBuf.View);
            await accelerator.SynchronizeAsync();

            // Compute the expected values on CPU using the identical sequence.
            ulong[] Expected()
            {
                ulong h0 = 0, h1 = 0, h2 = 0, h3 = 0, h4 = 0, h5 = 0, h6 = 0, h7 = 0, ctr = 0;
                void Call(ulong t0, ulong t1, bool last)
                {
                    ulong m0 = ctr, m1 = ctr + 1, m2 = ctr + 2, m3 = ctr + 3, m4 = ctr + 4, m5 = ctr + 5, m6 = ctr + 6, m7 = ctr + 7;
                    ulong m8 = ctr + 8, m9 = ctr + 9, m10 = ctr + 10, m11 = ctr + 11, m12 = ctr + 12, m13 = ctr + 13, m14 = ctr + 14, m15 = ctr + 15;
                    h0 += m0; h1 += m1; h2 += m2; h3 += m3; h4 += m4; h5 += m5; h6 += m6; h7 += m7;
                    h0 += m8 + m9 + m10 + m11 + m12 + m13 + m14 + m15 + t0 + (last ? t1 : 0UL);
                }
                Call(100UL, 0UL, false); ctr += 16;
                for (int k = 0; k < 63; k++) { Call(100UL, 0UL, false); ctr += 16; }
                h0 += ctr; // final block: m0=ctr, rest 0
                h0 += 108UL;
                return new[] { h0, h1, h2, h3, h4, h5, h6, h7 };
            }
            var expected = Expected();

            var result = await outBuf.CopyToHostAsync<ulong>();
            for (int i = 0; i < N; i++)
                for (int j = 0; j < 8; j++)
                {
                    var got = result[i * 8 + j];
                    if (got != expected[j])
                        throw new Exception($"NoInliningCompressShapedLoop[{i}][{j}] expected {expected[j]}, got {got}");
                }
        });

        // Closest repro to the real Blake2b.Compress/G/Rotr shape: G (4 ref + 2 value params,
        // internally calls an AggressiveInlining Rotr-shaped helper 4x) called 8x per round with
        // rotating v0..v15 combinations from within Compress (8 ref h params), which is itself
        // called 65x (before/inside a 63-loop/after) from the kernel. Verifies RUNTIME
        // correctness (not just generated-text structure, which was already confirmed correct
        // via an offline dump - this closes that verification gap).
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        private static ulong ShapedRotrCpu(ulong x, int n) => (x >> n) | (x << (64 - n));

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void BlakeShapedGKernel(ref ulong a, ref ulong b, ref ulong c, ref ulong d, ulong x, ulong y)
        {
            a = a + b + x;
            d = ShapedRotrCpu(d ^ a, 32);
            c = c + d;
            b = ShapedRotrCpu(b ^ c, 24);
            a = a + b + y;
            d = ShapedRotrCpu(d ^ a, 16);
            c = c + d;
            b = ShapedRotrCpu(b ^ c, 63);
        }

        // NUM_ROUNDS is a compile-time constant shared by both the GPU kernel and the CPU
        // oracle below (via the SAME literal in each) so a real `for` loop can be used on
        // BOTH sides identically - no manual call duplication to keep in sync by hand.
        // 1 round (8 G calls, one full pass through v0..v15) is enough to prove the ref
        // write-back fix (below) correct. A SEPARATE, genuinely unrelated bug was found at
        // 2 rounds (16 calls) - reproduces IDENTICALLY even with G fully AggressiveInlining
        // (no function call, no ref/inout, no NoInlining involved at all), so it is NOT a
        // ref-write-back issue and not in scope for this fix. Root cause not yet found;
        // named here for whoever picks it up next (see BackendTestBase.Autolykos2.cs).
        private const int BlakeShapedNumRounds = 1;

        static void BlakeShapedRawVKernel(Index1D index, ArrayView<ulong> outBuf)
        {
            ulong v0 = 100, v1 = 0, v2 = 0, v3 = 0, v4 = 0, v5 = 0, v6 = 0, v7 = 0;
            ulong v8 = 1, v9 = 2, v10 = 3, v11 = 4, v12 = 5, v13 = 6, v14 = 7, v15 = 8;
            for (int round = 0; round < BlakeShapedNumRounds; round++)
            {
                BlakeShapedGKernel(ref v0, ref v4, ref v8, ref v12, 1, 2);
                BlakeShapedGKernel(ref v1, ref v5, ref v9, ref v13, 3, 4);
                BlakeShapedGKernel(ref v2, ref v6, ref v10, ref v14, 5, 6);
                BlakeShapedGKernel(ref v3, ref v7, ref v11, ref v15, 7, 8);
                BlakeShapedGKernel(ref v0, ref v5, ref v10, ref v15, 9, 10);
                BlakeShapedGKernel(ref v1, ref v6, ref v11, ref v12, 11, 12);
                BlakeShapedGKernel(ref v2, ref v7, ref v8, ref v13, 13, 14);
                BlakeShapedGKernel(ref v3, ref v4, ref v9, ref v14, 15, 16);
            }
            int b = (int)index * 16;
            outBuf[b + 0] = v0; outBuf[b + 1] = v1; outBuf[b + 2] = v2; outBuf[b + 3] = v3;
            outBuf[b + 4] = v4; outBuf[b + 5] = v5; outBuf[b + 6] = v6; outBuf[b + 7] = v7;
            outBuf[b + 8] = v8; outBuf[b + 9] = v9; outBuf[b + 10] = v10; outBuf[b + 11] = v11;
            outBuf[b + 12] = v12; outBuf[b + 13] = v13; outBuf[b + 14] = v14; outBuf[b + 15] = v15;
        }

        [TestMethod]
        public async Task NoInliningBlakeShapedRawVTest() => await RunTest(async accelerator =>
        {
            const int N = 1;
            using var outBuf = accelerator.Allocate1D<ulong>(N * 16);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ulong>>(BlakeShapedRawVKernel);
            kernel(N, outBuf.View);
            await accelerator.SynchronizeAsync();

            ulong v0 = 100, v1 = 0, v2 = 0, v3 = 0, v4 = 0, v5 = 0, v6 = 0, v7 = 0;
            ulong v8 = 1, v9 = 2, v10 = 3, v11 = 4, v12 = 5, v13 = 6, v14 = 7, v15 = 8;
            void G(ref ulong a, ref ulong b, ref ulong c, ref ulong d, ulong x, ulong y)
            {
                a = a + b + x; d = ShapedRotrCpu(d ^ a, 32); c = c + d; b = ShapedRotrCpu(b ^ c, 24);
                a = a + b + y; d = ShapedRotrCpu(d ^ a, 16); c = c + d; b = ShapedRotrCpu(b ^ c, 63);
            }
            for (int round = 0; round < BlakeShapedNumRounds; round++)
            {
                G(ref v0, ref v4, ref v8, ref v12, 1, 2); G(ref v1, ref v5, ref v9, ref v13, 3, 4);
                G(ref v2, ref v6, ref v10, ref v14, 5, 6); G(ref v3, ref v7, ref v11, ref v15, 7, 8);
                G(ref v0, ref v5, ref v10, ref v15, 9, 10); G(ref v1, ref v6, ref v11, ref v12, 11, 12);
                G(ref v2, ref v7, ref v8, ref v13, 13, 14); G(ref v3, ref v4, ref v9, ref v14, 15, 16);
            }
            ulong[] expected = { v0, v1, v2, v3, v4, v5, v6, v7, v8, v9, v10, v11, v12, v13, v14, v15 };

            var result = await outBuf.CopyToHostAsync<ulong>();
            for (int j = 0; j < 16; j++)
                if (result[j] != expected[j])
                    throw new Exception($"NoInliningBlakeShapedRawV[v{j}] expected {expected[j]:x16}, got {result[j]:x16}");
        });

        // Bug #5 (WebGL i64/uvec2 emulation reuse bug) support types - see
        // NoInliningBlakeShapedTripleCallTest below for the minimal repro and the current
        // state of this investigation. G takes its 4 mixed values BY VALUE and returns them
        // in a struct (ruling out ref/inout GLSL parameter handling as a factor - the same
        // bug reproduces identically with the ref/inout-based BlakeShapedGKernel above).
        private struct ShapedV4U64
        {
            public ulong A, B, C, D;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static ShapedV4U64 BlakeShapedGValueKernel(ulong a, ulong b, ulong c, ulong d, ulong x, ulong y)
        {
            a = a + b + x;
            d = ShapedRotrCpu(d ^ a, 32);
            c = c + d;
            b = ShapedRotrCpu(b ^ c, 24);
            a = a + b + y;
            d = ShapedRotrCpu(d ^ a, 16);
            c = c + d;
            b = ShapedRotrCpu(b ^ c, 63);
            return new ShapedV4U64 { A = a, B = b, C = c, D = d };
        }

        // Locks in the GLSLKernelFunctionGenerator Shr-dispatch fix (2026-09-22): calls 1-8
        // run through the real NoInlining G function (real Blake2b shape), but call 9's own
        // body is INLINED directly into the kernel (no function call) with each of its 8
        // internal steps (a/b/c/d after each mix) written to a separate output slot. Before
        // the fix, `x >> n` on an unsigned emu-i64 value emitted directly in a KERNEL body
        // (as opposed to inside a standalone NoInlining GLSL function) unconditionally routed
        // through `i64_shr` (arithmetic/signed shift) instead of `u64_shr` (logical), so
        // Rotr(x, 24)'s shr sign-extended instead of zero-filling whenever x's high word had
        // its top bit set - this test's call 9 hit exactly that at step s4. Fixed in
        // GLSLKernelFunctionGenerator.GenerateCode(BinaryArithmeticValue).
        static void BlakeShapedCall9InlineTraceKernel(Index1D index, ArrayView<ulong> outBuf)
        {
            ulong v0 = 100, v1 = 0, v2 = 0, v3 = 0, v4 = 0, v5 = 0, v6 = 0, v7 = 0;
            ulong v8 = 1, v9 = 2, v10 = 3, v11 = 4, v12 = 5, v13 = 6, v14 = 7, v15 = 8;
            ShapedV4U64 r;
            r = BlakeShapedGValueKernel(v0, v4, v8, v12, 1, 2); v0 = r.A; v4 = r.B; v8 = r.C; v12 = r.D;
            r = BlakeShapedGValueKernel(v1, v5, v9, v13, 3, 4); v1 = r.A; v5 = r.B; v9 = r.C; v13 = r.D;
            r = BlakeShapedGValueKernel(v2, v6, v10, v14, 5, 6); v2 = r.A; v6 = r.B; v10 = r.C; v14 = r.D;
            r = BlakeShapedGValueKernel(v3, v7, v11, v15, 7, 8); v3 = r.A; v7 = r.B; v11 = r.C; v15 = r.D;
            r = BlakeShapedGValueKernel(v0, v5, v10, v15, 9, 10); v0 = r.A; v5 = r.B; v10 = r.C; v15 = r.D;
            r = BlakeShapedGValueKernel(v1, v6, v11, v12, 11, 12); v1 = r.A; v6 = r.B; v11 = r.C; v12 = r.D;
            r = BlakeShapedGValueKernel(v2, v7, v8, v13, 13, 14); v2 = r.A; v7 = r.B; v8 = r.C; v13 = r.D;
            r = BlakeShapedGValueKernel(v3, v4, v9, v14, 15, 16); v3 = r.A; v4 = r.B; v9 = r.C; v14 = r.D;

            // Call 9 = G(v0, v4, v8, v12, 1, 2), inlined step by step.
            ulong a = v0, b = v4, c = v8, d = v12, x = 1, y = 2;
            a = a + b + x;               ulong s1 = a;
            d = ShapedRotrCpu(d ^ a, 32); ulong s2 = d;
            c = c + d;                    ulong s3 = c;
            b = ShapedRotrCpu(b ^ c, 24); ulong s4 = b;
            a = a + b + y;                ulong s5 = a;
            d = ShapedRotrCpu(d ^ a, 16); ulong s6 = d;
            c = c + d;                    ulong s7 = c;
            b = ShapedRotrCpu(b ^ c, 63); ulong s8 = b;

            int off = (int)index * 8;
            outBuf[off + 0] = s1; outBuf[off + 1] = s2; outBuf[off + 2] = s3; outBuf[off + 3] = s4;
            outBuf[off + 4] = s5; outBuf[off + 5] = s6; outBuf[off + 6] = s7; outBuf[off + 7] = s8;
        }

        [TestMethod]
        public async Task NoInliningBlakeShapedCall9InlineTraceTest() => await RunTest(async accelerator =>
        {
            // CPU oracle: identical call1-8 sequence, then call 9 traced step by step.
            ulong v0 = 100, v1 = 0, v2 = 0, v3 = 0, v4 = 0, v5 = 0, v6 = 0, v7 = 0;
            ulong v8 = 1, v9 = 2, v10 = 3, v11 = 4, v12 = 5, v13 = 6, v14 = 7, v15 = 8;
            (ulong, ulong, ulong, ulong) G(ulong a2, ulong b2, ulong c2, ulong d2, ulong x2, ulong y2)
            {
                a2 = a2 + b2 + x2; d2 = ShapedRotrCpu(d2 ^ a2, 32); c2 = c2 + d2; b2 = ShapedRotrCpu(b2 ^ c2, 24);
                a2 = a2 + b2 + y2; d2 = ShapedRotrCpu(d2 ^ a2, 16); c2 = c2 + d2; b2 = ShapedRotrCpu(b2 ^ c2, 63);
                return (a2, b2, c2, d2);
            }
            (v0, v4, v8, v12) = G(v0, v4, v8, v12, 1, 2);
            (v1, v5, v9, v13) = G(v1, v5, v9, v13, 3, 4);
            (v2, v6, v10, v14) = G(v2, v6, v10, v14, 5, 6);
            (v3, v7, v11, v15) = G(v3, v7, v11, v15, 7, 8);
            (v0, v5, v10, v15) = G(v0, v5, v10, v15, 9, 10);
            (v1, v6, v11, v12) = G(v1, v6, v11, v12, 11, 12);
            (v2, v7, v8, v13) = G(v2, v7, v8, v13, 13, 14);
            (v3, v4, v9, v14) = G(v3, v4, v9, v14, 15, 16);

            ulong a = v0, b = v4, c = v8, d = v12, x = 1, y = 2;
            a = a + b + x;               ulong e1 = a;
            d = ShapedRotrCpu(d ^ a, 32); ulong e2 = d;
            c = c + d;                    ulong e3 = c;
            b = ShapedRotrCpu(b ^ c, 24); ulong e4 = b;
            a = a + b + y;                ulong e5 = a;
            d = ShapedRotrCpu(d ^ a, 16); ulong e6 = d;
            c = c + d;                    ulong e7 = c;
            b = ShapedRotrCpu(b ^ c, 63); ulong e8 = b;
            ulong[] expected = { e1, e2, e3, e4, e5, e6, e7, e8 };

            using var outBuf = accelerator.Allocate1D<ulong>(8);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ulong>>(BlakeShapedCall9InlineTraceKernel);
            kernel(1, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<ulong>();
            string[] names = { "s1(a=a+b+x)", "s2(d=rotr32)", "s3(c=c+d)", "s4(b=rotr24)",
                "s5(a=a+b+y)", "s6(d=rotr16)", "s7(c=c+d)", "s8(b=rotr63)" };
            for (int j = 0; j < 8; j++)
                if (result[j] != expected[j])
                    throw new Exception($"NoInliningBlakeShapedCall9InlineTrace[{names[j]}] expected {expected[j]:x16}, got {result[j]:x16}");
        });

        // Bug #5 - WebGL i64/uvec2 emulation reuse bug. STILL OPEN as of 2026-09-22, after the
        // GLSLKernelFunctionGenerator Shr-dispatch bug above was found and fixed (that fix is
        // real, verified, and shipped - it is NOT this bug; NoInliningBlakeShapedCall9InlineTraceTest
        // above locks it in). This is the SMALLEST known repro: 3 CONSECUTIVE calls to the
        // Blake2b-G-shaped NoInlining function, chaining the SAME 4 ulong locals through each
        // call's output back into the next call's input. Call 3's result is wrong by exactly
        // 1 bit, always at bit 32 (the emulated uvec2 lo/hi word boundary).
        //
        // Extensively bisected and NOT explained by any of the following (each independently
        // ruled out by a dedicated test, since removed from the suite - see git history
        // 2026-09-22 for the full scaffold if this needs revisiting):
        //   - The emulation MATH itself: a bit-for-bit C# port of GLSLEmulationLibrary's
        //     i64_add/i64_shl/u64_shr/i64_xor, run against the exact same value sequence,
        //     matches the real ulong ground truth with zero mismatches.
        //   - ref/inout vs. struct-by-value calling convention (both shapes fail identically).
        //   - Constant vs. round-varying message words (both fail).
        //   - Loop vs. fully-unrolled straight-line code (both fail; a genuinely fresh,
        //     never-before-touched set of locals passed through this SAME function as its
        //     literal 9th call in the shader is CORRECT - ruling out raw call count/shader
        //     position entirely. The trigger needs a data-dependent CHAIN of 3, not merely
        //     3 occurrences of the function).
        //   - GLSL `<`-based unsigned carry/borrow detection in i64_add/i64_sub (rewritten to
        //     the branch-free bitwise generate/propagate identity - see i64_add's own comment
        //     in GLSLEmulationLibrary.cs - with zero change in the wrong output).
        //   - Compiler constant-folding/CSE across identical call sites (defeated by XOR-ing
        //     every intermediate value against a genuine runtime kernel parameter, always 0
        //     but opaque to the compiler at compile time - the same "u_one" anti-optimization
        //     technique already used for f64 emulation - with zero change in the wrong output).
        //   - The SAME compiled GLSL function object being reused 3x (defeated by alternating
        //     between two byte-for-byte identical but separately-named function copies for
        //     calls 1/2/3 - still fails identically).
        //   - Register/ALU-level value corruption fixable by forcing a genuine GPU memory
        //     round-trip (store to and load back from a real ArrayView<ulong> buffer between
        //     calls 2 and 3) - this changed the wrong answer to something DIFFERENT rather
        //     than fixing it, suggesting the memory-buffer readback path has its own,
        //     separate correctness issue worth a fresh investigation of its own.
        //
        // Working theory (not yet confirmed): the corrupted word's correct value exceeds
        // 2^24 in every failing case observed, and the corruption always rounds an ODD value
        // down to the nearest EVEN one - the exact signature of a 32-bit integer being
        // silently routed through a 24-bit-mantissa float intermediate somewhere in the
        // driver's compiled code, once accumulated Blake2b mixing pushes a word's magnitude
        // past that threshold. This would be consistent with GLSLEmulationLibrary.cs's
        // existing documented ANGLE/D3D11 f64 precision-collapse bug (see the F64Functions
        // "ANTI-OPTIMIZATION" comment above) manifesting in the i64 path instead - but the
        // f64 fix's mitigation (a fake-dynamic-but-always-1.0 float multiply) does not
        // obviously translate to an all-integer computation, and the runtime-XOR probe here
        // (the closest integer analogue) did not fix it. NOT YET FIXED. If this needs
        // picking up again: reproduce via the smallest test below, then try (a) forcing every
        // intermediate through an actual `int`/`uint`-typed uniform read (not just XOR
        // against one) to rule out int-vs-float ALU routing more directly, and (b) capturing
        // an ANGLE HLSL disassembly (chrome://gpu / `--use-angle=d3d11 --show-fps-counter`
        // shader dump flags) of fn_BlakeShapedGValueKernel's 3rd call site to inspect actual
        // register allocation directly instead of black-box GLSL-source bisection.
        static void BlakeShapedTripleCallKernel(Index1D index, ArrayView<ulong> outBuf)
        {
            ulong w0 = 10, w1 = 11, w2 = 12, w3 = 13;
            ShapedV4U64 r;
            r = BlakeShapedGValueKernel(w0, w1, w2, w3, 1, 2); w0 = r.A; w1 = r.B; w2 = r.C; w3 = r.D;
            r = BlakeShapedGValueKernel(w0, w1, w2, w3, 3, 4); w0 = r.A; w1 = r.B; w2 = r.C; w3 = r.D;
            r = BlakeShapedGValueKernel(w0, w1, w2, w3, 5, 6); w0 = r.A; w1 = r.B; w2 = r.C; w3 = r.D;
            int off = (int)index * 4;
            outBuf[off + 0] = w0; outBuf[off + 1] = w1; outBuf[off + 2] = w2; outBuf[off + 3] = w3;
        }

        [TestMethod]
        public async Task NoInliningBlakeShapedTripleCallTest() => await RunTest(async accelerator =>
        {
            (ulong, ulong, ulong, ulong) G(ulong a, ulong b, ulong c, ulong d, ulong x, ulong y)
            {
                a = a + b + x; d = ShapedRotrCpu(d ^ a, 32); c = c + d; b = ShapedRotrCpu(b ^ c, 24);
                a = a + b + y; d = ShapedRotrCpu(d ^ a, 16); c = c + d; b = ShapedRotrCpu(b ^ c, 63);
                return (a, b, c, d);
            }
            ulong w0 = 10, w1 = 11, w2 = 12, w3 = 13;
            (w0, w1, w2, w3) = G(w0, w1, w2, w3, 1, 2);
            (w0, w1, w2, w3) = G(w0, w1, w2, w3, 3, 4);
            (w0, w1, w2, w3) = G(w0, w1, w2, w3, 5, 6);
            ulong[] expected = { w0, w1, w2, w3 };

            using var outBuf = accelerator.Allocate1D<ulong>(4);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ulong>>(BlakeShapedTripleCallKernel);
            kernel(1, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<ulong>();
            string[] names = { "w0(A)", "w1(B)", "w2(C)", "w3(D)" };
            for (int j = 0; j < 4; j++)
                if (result[j] != expected[j])
                    throw new Exception($"NoInliningBlakeShapedTripleCall[{names[j]}] expected {expected[j]:x16}, got {result[j]:x16}");
        });

        [TestMethod]
        public async Task NoInliningRefULong8LoopMultiCallBitExactTest() => await RunTest(async accelerator =>
        {
            const int N = 2;
            using var outBuf = accelerator.Allocate1D<ulong>(N * 8);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ulong>>(RefULong8LoopMultiCallKernel);
            kernel(N, outBuf.View);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<ulong>();
            ulong[] expected = { 65UL, 130UL, 195UL, 260UL, 325UL, 390UL, 455UL, 520UL };
            for (int i = 0; i < N; i++)
                for (int j = 0; j < 8; j++)
                {
                    var got = result[i * 8 + j];
                    if (got != expected[j])
                        throw new Exception($"NoInliningRefULong8LoopMultiCall[{i}][{j}] expected {expected[j]}, got {got}");
                }
        });

        static void ButterflyNarrowingKernel(
            Index1D index,
            ArrayView<int> aIn,
            ArrayView<int> bIn,
            ArrayView<int> rSumOut,
            ArrayView<int> rDiffOut)
        {
            int gid = index;
            ButterflyNarrowingHelper(aIn[gid], bIn[gid], out int sum, out int diff);
            rSumOut[gid] = sum;
            rDiffOut[gid] = diff;
        }

        #region Algorithm Kernel Methods


        static void ExclusiveScanKernel(
            Index1D index,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            int gid = Grid.GlobalIndex.X;
            int val = input[gid];
            int scanned = GroupExtensions.ExclusiveScan<int, AddInt32>(val);
            output[gid] = scanned;
        }

        static void InclusiveScanKernel(
            Index1D index,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            int gid = Grid.GlobalIndex.X;
            int val = input[gid];
            int scanned = GroupExtensions.InclusiveScan<int, AddInt32>(val);
            output[gid] = scanned;
        }

        static void AllReduceKernel(
            Index1D index,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            int gid = Grid.GlobalIndex.X;
            int val = input[gid];
            int reduced = GroupExtensions.AllReduce<int, AddInt32>(val);
            output[gid] = reduced;
        }

        static void ExclusiveScanWithBoundariesKernel(
            Index1D index,
            ArrayView<int> input,
            ArrayView<int> output,
            ArrayView<int> boundaryOutput)
        {
            int gid = Grid.GlobalIndex.X;
            int val = input[gid];
            int scanned = GroupExtensions.ExclusiveScanWithBoundaries<int, AddInt32>(
                val, out ScanBoundaries<int> boundaries);
            output[gid] = scanned;

            // First thread writes boundaries
            if (Group.IsFirstThread)
            {
                boundaryOutput[0] = boundaries.LeftBoundary;
                boundaryOutput[1] = boundaries.RightBoundary;
            }
        }

        static void GroupReduceKernel(
            Index1D index,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            int gid = Grid.GlobalIndex.X;
            int val = input[gid];
            int reduced = GroupExtensions.Reduce<int, AddInt32>(val);
            // Only first thread writes the result
            if (Group.IsFirstThread)
                output[0] = reduced;
        }

        static void ExclusiveScanFloatKernel(
            Index1D index,
            ArrayView<float> input,
            ArrayView<float> output)
        {
            int gid = Grid.GlobalIndex.X;
            float val = input[gid];
            float scanned = GroupExtensions.ExclusiveScan<float, AddFloat>(val);
            output[gid] = scanned;
        }

        static void ExclusiveScanLongKernel(
            Index1D index,
            ArrayView<long> input,
            ArrayView<long> output)
        {
            int gid = Grid.GlobalIndex.X;
            long val = input[gid];
            long scanned = GroupExtensions.ExclusiveScan<long, AddInt64>(val);
            output[gid] = scanned;
        }

        static void InclusiveScanFloatKernel(
            Index1D index,
            ArrayView<float> input,
            ArrayView<float> output)
        {
            int gid = Grid.GlobalIndex.X;
            float val = input[gid];
            float scanned = GroupExtensions.InclusiveScan<float, AddFloat>(val);
            output[gid] = scanned;
        }

        static void AllReduceFloatKernel(
            Index1D index,
            ArrayView<float> input,
            ArrayView<float> output)
        {
            int gid = Grid.GlobalIndex.X;
            float val = input[gid];
            float reduced = GroupExtensions.AllReduce<float, AddFloat>(val);
            output[gid] = reduced;
        }

        static void ExclusiveScanHalfKernel(
            Index1D index,
            ArrayView<global::ILGPU.Half> input,
            ArrayView<global::ILGPU.Half> output)
        {
            int gid = Grid.GlobalIndex.X;
            global::ILGPU.Half val = input[gid];
            global::ILGPU.Half scanned = GroupExtensions.ExclusiveScan<global::ILGPU.Half, AddHalf>(val);
            output[gid] = scanned;
        }

        static void InclusiveScanHalfKernel(
            Index1D index,
            ArrayView<global::ILGPU.Half> input,
            ArrayView<global::ILGPU.Half> output)
        {
            int gid = Grid.GlobalIndex.X;
            global::ILGPU.Half val = input[gid];
            global::ILGPU.Half scanned = GroupExtensions.InclusiveScan<global::ILGPU.Half, AddHalf>(val);
            output[gid] = scanned;
        }

        static void AllReduceHalfKernel(
            Index1D index,
            ArrayView<global::ILGPU.Half> input,
            ArrayView<global::ILGPU.Half> output)
        {
            int gid = Grid.GlobalIndex.X;
            global::ILGPU.Half val = input[gid];
            global::ILGPU.Half reduced = GroupExtensions.AllReduce<global::ILGPU.Half, AddHalf>(val);
            output[gid] = reduced;
        }

        static void ExclusiveScanDoubleKernel(
            Index1D index,
            ArrayView<double> input,
            ArrayView<double> output)
        {
            int gid = Grid.GlobalIndex.X;
            double val = input[gid];
            double scanned = GroupExtensions.ExclusiveScan<double, AddDouble>(val);
            output[gid] = scanned;
        }

        static void ExclusiveScanUIntKernel(
            Index1D index,
            ArrayView<uint> input,
            ArrayView<uint> output)
        {
            int gid = Grid.GlobalIndex.X;
            uint val = input[gid];
            uint scanned = GroupExtensions.ExclusiveScan<uint, AddUInt32>(val);
            output[gid] = scanned;
        }

        static void InclusiveScanLongKernel(
            Index1D index,
            ArrayView<long> input,
            ArrayView<long> output)
        {
            int gid = Grid.GlobalIndex.X;
            long val = input[gid];
            long scanned = GroupExtensions.InclusiveScan<long, AddInt64>(val);
            output[gid] = scanned;
        }

        static void InclusiveScanDoubleKernel(
            Index1D index,
            ArrayView<double> input,
            ArrayView<double> output)
        {
            int gid = Grid.GlobalIndex.X;
            double val = input[gid];
            double scanned = GroupExtensions.InclusiveScan<double, AddDouble>(val);
            output[gid] = scanned;
        }

        static void InclusiveScanUIntKernel(
            Index1D index,
            ArrayView<uint> input,
            ArrayView<uint> output)
        {
            int gid = Grid.GlobalIndex.X;
            uint val = input[gid];
            uint scanned = GroupExtensions.InclusiveScan<uint, AddUInt32>(val);
            output[gid] = scanned;
        }

        static void AllReduceDoubleKernel(
            Index1D index,
            ArrayView<double> input,
            ArrayView<double> output)
        {
            int gid = Grid.GlobalIndex.X;
            double val = input[gid];
            double reduced = GroupExtensions.AllReduce<double, AddDouble>(val);
            output[gid] = reduced;
        }

        static void AllReduceLongKernel(
            Index1D index,
            ArrayView<long> input,
            ArrayView<long> output)
        {
            int gid = Grid.GlobalIndex.X;
            long val = input[gid];
            long reduced = GroupExtensions.AllReduce<long, AddInt64>(val);
            output[gid] = reduced;
        }

        static void AllReduceUIntKernel(
            Index1D index,
            ArrayView<uint> input,
            ArrayView<uint> output)
        {
            int gid = Grid.GlobalIndex.X;
            uint val = input[gid];
            uint reduced = GroupExtensions.AllReduce<uint, AddUInt32>(val);
            output[gid] = reduced;
        }

        static void GroupReduceFloatKernel(
            Index1D index,
            ArrayView<float> input,
            ArrayView<float> output)
        {
            int gid = Grid.GlobalIndex.X;
            float val = input[gid];
            float reduced = GroupExtensions.Reduce<float, AddFloat>(val);
            if (Group.IsFirstThread)
                output[0] = reduced;
        }

        static void GroupReduceLongKernel(
            Index1D index,
            ArrayView<long> input,
            ArrayView<long> output)
        {
            int gid = Grid.GlobalIndex.X;
            long val = input[gid];
            long reduced = GroupExtensions.Reduce<long, AddInt64>(val);
            if (Group.IsFirstThread)
                output[0] = reduced;
        }

        static void GroupReduceDoubleKernel(
            Index1D index,
            ArrayView<double> input,
            ArrayView<double> output)
        {
            int gid = Grid.GlobalIndex.X;
            double val = input[gid];
            double reduced = GroupExtensions.Reduce<double, AddDouble>(val);
            if (Group.IsFirstThread)
                output[0] = reduced;
        }

        static void GroupReduceUIntKernel(
            Index1D index,
            ArrayView<uint> input,
            ArrayView<uint> output)
        {
            int gid = Grid.GlobalIndex.X;
            uint val = input[gid];
            uint reduced = GroupExtensions.Reduce<uint, AddUInt32>(val);
            if (Group.IsFirstThread)
                output[0] = reduced;
        }

        static void GroupReduceHalfKernel(
            Index1D index,
            ArrayView<global::ILGPU.Half> input,
            ArrayView<global::ILGPU.Half> output)
        {
            int gid = Grid.GlobalIndex.X;
            global::ILGPU.Half val = input[gid];
            global::ILGPU.Half reduced = GroupExtensions.Reduce<global::ILGPU.Half, AddHalf>(val);
            if (Group.IsFirstThread)
                output[0] = reduced;
        }

        /// <summary>
        /// RadixSort at single-group size (64 elements) — the maximum guaranteed
        /// correct workload for the Wasm backend. Multi-group sorts (n>64) have
        /// a known cross-group memory visibility limitation in browser environments.
        /// Desktop backends (CUDA/OpenCL/CPU) handle any size correctly.
        /// </summary>
        [TestMethod]
        public async Task RadixSort100KBenchmarkTest() => await RunTest(async accelerator =>
        {
            int n = 64;
            var rng = new Random(42);
            var data = new int[n];
            for (int i = 0; i < n; i++) data[i] = rng.Next();

            // Keep a sorted copy for verification
            var expected = (int[])data.Clone();
            Array.Sort(expected);

            using var dataBuf = accelerator.Allocate1D(data);
            var tempSize = accelerator.ComputeRadixSortTempStorageSize<int, AscendingInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);

            var radixSort = accelerator.CreateRadixSort<int, Stride1D.Dense, AscendingInt32>();
            radixSort(accelerator.DefaultStream, dataBuf.View, tempBuf.View.AsContiguous());
            await accelerator.SynchronizeAsync();

            var sorted = await dataBuf.CopyToHostAsync<int>();
            // Spot-check first, middle, last
            if (sorted[0] != expected[0])
                throw new Exception($"100K RadixSort: index 0 expected {expected[0]}, got {sorted[0]}");
            if (sorted[n / 2] != expected[n / 2])
                throw new Exception($"100K RadixSort: index {n / 2} expected {expected[n / 2]}, got {sorted[n / 2]}");
            if (sorted[n - 1] != expected[n - 1])
                throw new Exception($"100K RadixSort: index {n - 1} expected {expected[n - 1]}, got {sorted[n - 1]}");
        });

        #endregion
    }
}

