using ILGPU;
using ILGPU.Runtime;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 17: Browser buffer operations - CopyFromJS, public Buffer property,
    // GPU-to-GPU buffer access for zero-copy rendering pipelines.
    public abstract partial class BackendTestBase
    {
        /// <summary>
        /// Verify CopyFromJS with TypedArray writes data correctly to GPU buffer.
        /// Writes via JS, reads back via CopyToHostAsync, compares with expected values.
        /// </summary>
        [TestMethod]
        public async Task CopyFromJS_TypedArray_WritesCorrectDataTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("CopyFromJS only available on browser backends");

            var testData = new float[] { 1.0f, 2.5f, 3.14f, 42.0f, -7.7f, 0f, 100f, 255f };
            using var buffer = accelerator.Allocate1D<float>(testData.Length);

            // Write via JS TypedArray
            var browserBuffer = buffer.Buffer as IBrowserMemoryBuffer;
            if (browserBuffer == null)
                throw new Exception("Buffer does not implement IBrowserMemoryBuffer");

            using var jsArray = new Float32Array(testData);
            browserBuffer.CopyFromJS(jsArray);

            // Read back and verify
            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (Math.Abs(result[i] - testData[i]) > 0.001f)
                    throw new Exception($"Mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        /// <summary>
        /// Fail-loud guard (ALL browser backends): a host->GPU CopyFromCPU larger than
        /// BrowserBufferPolicy.StrictHostCopyMaxBytes must THROW - bulk browser data (model weights) must
        /// stream JS-side via CopyFromStreamAsync/CopyFromJS over an IJSReadStream, never through the
        /// single-threaded .NET heap (the same main-thread tax on WebGPU, WebGL AND Wasm). A sub-threshold
        /// copy must still succeed (tiny .NET-origin constants are allowed); with the guard off the same
        /// bulk copy succeeds (proving the throw is the guard, not a real transfer error). Byte-level
        /// round-trip correctness of the zero-copy CopyFromCPU path is covered per-backend by the
        /// *_RoundTrip_NoKernel_Test family above. Captain directive 2026-07-05.
        /// </summary>
        [TestMethod]
        public async Task StrictHostCopyGuard_ThrowsOnBulkCopyTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("BrowserBufferPolicy host-copy guard applies to browser backends only");

            var big = new float[512];   // 2048 bytes
            for (int i = 0; i < big.Length; i++) big[i] = i;
            var small = new float[4];   // 16 bytes
            for (int i = 0; i < small.Length; i++) small[i] = i;

            long prev = BrowserBufferPolicy.StrictHostCopyMaxBytes;
            BrowserBufferPolicy.StrictHostCopyMaxBytes = 1024;
            try
            {
                using var buf = accelerator.Allocate1D<float>(big.Length);

                // Sub-threshold copy (16 bytes <= 1024) is still allowed.
                buf.View.SubView(0, small.Length).CopyFromCPU(small);

                // Bulk copy (2048 bytes > 1024) must trip the fail-loud guard.
                bool threw = false;
                try { buf.CopyFromCPU(big); }
                catch (InvalidOperationException ex) when (ex.Message.Contains("StrictHostCopyMaxBytes"))
                { threw = true; }
                if (!threw)
                    throw new Exception("BrowserBufferPolicy guard did NOT throw on a bulk host->GPU CopyFromCPU.");

                // Guard OFF: the same bulk copy must NOT throw (proves the throw was the guard, not a real error).
                BrowserBufferPolicy.StrictHostCopyMaxBytes = -1;
                buf.CopyFromCPU(big);
            }
            finally { BrowserBufferPolicy.StrictHostCopyMaxBytes = prev; }
        });

        /// <summary>
        /// Verify CopyFromJS with ArrayBuffer writes data correctly.
        /// </summary>
        [TestMethod]
        public async Task CopyFromJS_ArrayBuffer_WritesCorrectDataTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("CopyFromJS only available on browser backends");

            var testData = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };
            using var buffer = accelerator.Allocate1D<byte>(testData.Length);

            var browserBuffer = buffer.Buffer as IBrowserMemoryBuffer;
            if (browserBuffer == null)
                throw new Exception("Buffer does not implement IBrowserMemoryBuffer");

            using var jsArray = new Uint8Array(testData);
            using var arrayBuffer = jsArray.Buffer;
            browserBuffer.CopyFromJS(arrayBuffer);

            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (result[i] != testData[i])
                    throw new Exception($"Mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        /// <summary>
        /// Verify CopyFromJS data can be processed by a kernel and produces correct results.
        /// End-to-end: JS data -> GPU buffer -> kernel -> GPU buffer -> CPU readback.
        /// </summary>
        [TestMethod]
        public async Task CopyFromJS_KernelProcessing_EndToEndTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("CopyFromJS only available on browser backends");

            var inputData = new float[] { 1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f };
            using var input = accelerator.Allocate1D<float>(inputData.Length);
            using var output = accelerator.Allocate1D<float>(inputData.Length);

            // Load input via CopyFromJS
            var browserBuffer = input.Buffer as IBrowserMemoryBuffer;
            if (browserBuffer == null)
                throw new Exception("Buffer does not implement IBrowserMemoryBuffer");

            using var jsArray = new Float32Array(inputData);
            browserBuffer.CopyFromJS(jsArray);

            // Run kernel that doubles values
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                (Index1D idx, ArrayView<float> src, ArrayView<float> dst) => { dst[idx] = src[idx] * 2f; });
            kernel(inputData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            // Verify
            var result = await output.CopyToHostAsync();
            for (int i = 0; i < inputData.Length; i++)
            {
                float expected = inputData[i] * 2f;
                if (Math.Abs(result[i] - expected) > 0.001f)
                    throw new Exception($"Mismatch at [{i}]: expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Verify MemoryBuffer.Buffer property is publicly accessible and returns
        /// the correct backend-specific buffer type.
        /// </summary>
        [TestMethod]
        public async Task Buffer_Property_IsAccessibleTest() => await RunTest(async accelerator =>
        {
            using var buffer = accelerator.Allocate1D<float>(256);

            // This would have failed when Buffer was protected
            var memBuffer = buffer.Buffer;
            if (memBuffer == null)
                throw new Exception("Buffer property returned null");

            // On browser backends, should be a WebGPUMemoryBuffer, WebGLMemoryBuffer, or WasmMemoryBuffer
            if (accelerator.AcceleratorType == AcceleratorType.WebGPU)
            {
                if (memBuffer is not WebGPUMemoryBuffer webGpuMem)
                    throw new Exception($"Expected WebGPUMemoryBuffer, got {memBuffer.GetType().Name}");
                if (webGpuMem.NativeBuffer == null)
                    throw new Exception("WebGPUMemoryBuffer.NativeBuffer is null");
                if (webGpuMem.NativeBuffer.NativeBuffer == null)
                    throw new Exception("WebGPUBuffer.NativeBuffer (GPUBuffer) is null");
            }

            await Task.CompletedTask;
        });

        // ==================== Sub-Word Buffer Access Tests ====================

        static void Int16DoubleKernel(Index1D idx, ArrayView<short> src, ArrayView<int> dst)
        {
            dst[idx] = (int)src[idx] * 2;
        }

        /// <summary>
        /// Basic round-trip: CopyFromCPU short[] -> CopyToHostAsync short[] (no kernel).
        /// Verifies buffer I/O works for short type.
        /// </summary>
        [TestMethod]
        public async Task Int16_RoundTrip_NoKernel_Test() => await RunTest(async accelerator =>
        {
            var testData = new short[] { 1, -2, 300, -400, 32767, -32768, 0, 42 };
            using var buffer = accelerator.Allocate1D<short>(testData.Length);
            buffer.CopyFromCPU(testData);
            await accelerator.SynchronizeAsync();
            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (result[i] != testData[i])
                    throw new Exception($"Short round-trip mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        static void Int16WriteKernel(Index1D idx, ArrayView<short> dst, ArrayView<int> src)
        {
            dst[idx] = (short)src[idx];
        }

        /// <summary>
        /// Verify ArrayView&lt;short&gt; buffer read works correctly on all backends.
        /// Writes short[] via CopyFromCPU, kernel reads as short, outputs to int buffer.
        /// </summary>
        [TestMethod]
        public async Task Int16_BufferRead_Test() => await RunTest(async accelerator =>
        {
            var testData = new short[] { 1, -2, 300, -400, 32767, -32768, 0, 42 };
            using var input = accelerator.Allocate1D<short>(testData.Length);
            using var output = accelerator.Allocate1D<int>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(Int16DoubleKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                int expected = (int)testData[i] * 2;
                if (result[i] != expected)
                    throw new Exception($"Int16 read mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        /// <summary>
        /// Verify ArrayView&lt;short&gt; buffer write works correctly.
        /// Kernel writes short values to a short buffer, read back and verify.
        /// </summary>
        [TestMethod]
        public async Task Int16_BufferWrite_Test() => await RunTest(async accelerator =>
        {
            var srcData = new int[] { 1, -2, 300, -400, 32767, -32768, 0, 42 };
            using var src = accelerator.Allocate1D<int>(srcData.Length);
            using var dst = accelerator.Allocate1D<short>(srcData.Length);

            src.CopyFromCPU(srcData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(Int16WriteKernel);
            kernel(srcData.Length, dst.View, src.View);
            await accelerator.SynchronizeAsync();

            var result = await dst.CopyToHostAsync();
            for (int i = 0; i < srcData.Length; i++)
            {
                short expected = (short)srcData[i];
                if (result[i] != expected)
                    throw new Exception($"Int16 write mismatch at [{i}]: expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Verify ArrayView&lt;Half&gt; buffer read works correctly.
        /// Writes Half[] via CopyFromCPU, kernel reads as Half, converts to float.
        /// </summary>
        // Named static method avoids lambda issues with ILGPU Frontend's Half detection
        static void Float16ReadKernel(Index1D idx, ArrayView<global::ILGPU.Half> src, ArrayView<float> dst)
        {
            dst[idx] = (float)src[idx]; // implicit Half -> float via IR Convert node
        }

        [TestMethod]
        public async Task Float16_BufferRead_Test() => await RunTest(async accelerator =>
        {
            var testFloats = new float[] { 1.0f, -2.5f, 0.0f, 3.14f, 100.0f, -0.5f, 0.001f, 65504.0f };
            var testData = testFloats.Select(f => (global::ILGPU.Half)f).ToArray();
            using var input = accelerator.Allocate1D<global::ILGPU.Half>(testData.Length);
            using var output = accelerator.Allocate1D<float>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<float>>(Float16ReadKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                float expected = (float)testData[i];
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"Float16 read mismatch at [{i}]: expected {expected}, got {result[i]}");
            }
        });

        // ==================== UInt16 Sub-Word Buffer Access Tests ====================

        /// <summary>
        /// Basic round-trip: CopyFromCPU ushort[] -> CopyToHostAsync ushort[] (no kernel).
        /// Verifies buffer I/O works for ushort type.
        /// </summary>
        [TestMethod]
        public async Task UInt16_RoundTrip_NoKernel_Test() => await RunTest(async accelerator =>
        {
            var testData = new ushort[] { 0, 1, 255, 256, 32767, 32768, 65534, 65535 };
            using var buffer = accelerator.Allocate1D<ushort>(testData.Length);
            buffer.CopyFromCPU(testData);
            await accelerator.SynchronizeAsync();
            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (result[i] != testData[i])
                    throw new Exception($"UInt16 round-trip mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        static void UInt16DoubleKernel(Index1D idx, ArrayView<ushort> src, ArrayView<int> dst)
        {
            dst[idx] = (int)src[idx] * 2;
        }

        /// <summary>
        /// Verify ArrayView&lt;ushort&gt; buffer read works correctly on all backends.
        /// Writes ushort[] via CopyFromCPU, kernel reads as ushort, multiplies by 2, outputs to int buffer.
        /// </summary>
        [TestMethod]
        public async Task UInt16_BufferRead_Test() => await RunTest(async accelerator =>
        {
            var testData = new ushort[] { 0, 1, 255, 256, 32767, 32768, 65534, 65535 };
            using var input = accelerator.Allocate1D<ushort>(testData.Length);
            using var output = accelerator.Allocate1D<int>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ushort>, ArrayView<int>>(UInt16DoubleKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                int expected = (int)testData[i] * 2;
                if (result[i] != expected)
                    throw new Exception($"UInt16 read mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        static void UInt16WriteKernel(Index1D idx, ArrayView<ushort> dst, ArrayView<int> src)
        {
            dst[idx] = (ushort)src[idx];
        }

        /// <summary>
        /// Verify ArrayView&lt;ushort&gt; buffer write works correctly.
        /// Kernel writes ushort values to a ushort buffer, read back and verify.
        /// </summary>
        [TestMethod]
        public async Task UInt16_BufferWrite_Test() => await RunTest(async accelerator =>
        {
            var srcData = new int[] { 0, 1, 255, 256, 32767, 32768, 65534, 65535 };
            using var src = accelerator.Allocate1D<int>(srcData.Length);
            using var dst = accelerator.Allocate1D<ushort>(srcData.Length);

            src.CopyFromCPU(srcData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ushort>, ArrayView<int>>(UInt16WriteKernel);
            kernel(srcData.Length, dst.View, src.View);
            await accelerator.SynchronizeAsync();

            var result = await dst.CopyToHostAsync();
            for (int i = 0; i < srcData.Length; i++)
            {
                ushort expected = (ushort)srcData[i];
                if (result[i] != expected)
                    throw new Exception($"UInt16 write mismatch at [{i}]: expected {expected}, got {result[i]}");
            }
        });

        static void UInt16EndToEndKernel(Index1D idx, ArrayView<ushort> input, ArrayView<ushort> output)
        {
            output[idx] = (ushort)(input[idx] + (ushort)10);
        }

        /// <summary>
        /// End-to-end: write ushort data, kernel reads AND writes ushort buffers.
        /// Tests both sub-word Load and sub-word Store in one kernel.
        /// </summary>
        [TestMethod]
        public async Task UInt16_EndToEnd_ReadWrite_Test() => await RunTest(async accelerator =>
        {
            var testData = new ushort[] { 0, 1, 100, 32767, 65525, 42, 0, 255 };
            using var input = accelerator.Allocate1D<ushort>(testData.Length);
            using var output = accelerator.Allocate1D<ushort>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ushort>, ArrayView<ushort>>(UInt16EndToEndKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                ushort expected = (ushort)(testData[i] + 10);
                if (result[i] != expected)
                    throw new Exception($"UInt16 end-to-end mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        // ==================== Additional Float16 Sub-Word Buffer Access Tests ====================

        static void Float16WriteKernel(Index1D idx, ArrayView<global::ILGPU.Half> dst, ArrayView<float> src)
        {
            dst[idx] = (global::ILGPU.Half)src[idx];
        }

        /// <summary>
        /// Verify ArrayView&lt;Half&gt; buffer write works correctly.
        /// Kernel converts float to Half and stores in Half buffer. Read back and compare as float.
        /// </summary>
        [TestMethod]
        public async Task Float16_BufferWrite_Test() => await RunTest(async accelerator =>
        {
            var srcData = new float[] { 1.0f, -2.5f, 0.0f, 3.14f, 100.0f, -0.5f, 0.001f, 65504.0f };
            using var src = accelerator.Allocate1D<float>(srcData.Length);
            using var dst = accelerator.Allocate1D<global::ILGPU.Half>(srcData.Length);

            src.CopyFromCPU(srcData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<float>>(Float16WriteKernel);
            kernel(srcData.Length, dst.View, src.View);
            await accelerator.SynchronizeAsync();

            var result = await dst.CopyToHostAsync();
            for (int i = 0; i < srcData.Length; i++)
            {
                float expected = (float)(global::ILGPU.Half)srcData[i];
                float actual = (float)result[i];
                if (MathF.Abs(actual - expected) > 0.01f)
                    throw new Exception($"Float16 write mismatch at [{i}]: expected {expected}, got {actual}");
            }
        });

        static void Float16EndToEndKernel(Index1D idx, ArrayView<global::ILGPU.Half> input, ArrayView<global::ILGPU.Half> output)
        {
            output[idx] = (global::ILGPU.Half)((float)input[idx] * 2.0f);
        }

        /// <summary>
        /// End-to-end: write Half data, kernel reads Half, multiplies by 2 via float, writes Half.
        /// Tests both sub-word Load and sub-word Store for Half in one kernel.
        /// </summary>
        [TestMethod]
        public async Task Float16_EndToEnd_ReadWrite_Test() => await RunTest(async accelerator =>
        {
            var testFloats = new float[] { 1.0f, -2.5f, 0.0f, 3.14f };
            var testData = testFloats.Select(f => (global::ILGPU.Half)f).ToArray();
            using var input = accelerator.Allocate1D<global::ILGPU.Half>(testData.Length);
            using var output = accelerator.Allocate1D<global::ILGPU.Half>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>>(Float16EndToEndKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                float expected = (float)testData[i] * 2.0f;
                float actual = (float)result[i];
                if (MathF.Abs(actual - expected) > 0.05f)
                    throw new Exception($"Float16 end-to-end mismatch at [{i}]: expected {expected}, got {actual}. Input was {(float)testData[i]}");
            }
        });

        static void Int16EndToEndKernel(Index1D idx, ArrayView<short> input, ArrayView<short> output)
        {
            output[idx] = (short)(input[idx] + (short)10);
        }

        /// <summary>
        /// End-to-end: write short data, kernel reads AND writes short buffers.
        /// Tests both sub-word Load and sub-word Store in one kernel.
        /// </summary>
        [TestMethod]
        public async Task Int16_EndToEnd_ReadWrite_Test() => await RunTest(async accelerator =>
        {
            var testData = new short[] { 0, 1, -1, 100, -100, 32757, -32758, 42 };
            using var input = accelerator.Allocate1D<short>(testData.Length);
            using var output = accelerator.Allocate1D<short>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<short>>(Int16EndToEndKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                short expected = (short)(testData[i] + 10);
                if (result[i] != expected)
                    throw new Exception($"Int16 end-to-end mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        // ==================== Int8 Sub-Word Buffer Access Tests ====================

        /// <summary>
        /// Basic round-trip: CopyFromCPU sbyte[] -> CopyToHostAsync sbyte[] (no kernel).
        /// Verifies buffer I/O works for sbyte type.
        /// </summary>
        [TestMethod]
        public async Task Int8_RoundTrip_NoKernel_Test() => await RunTest(async accelerator =>
        {
            var testData = new sbyte[] { 0, 1, -1, 127, -128, 42, -42, 100 };
            using var buffer = accelerator.Allocate1D<sbyte>(testData.Length);
            buffer.CopyFromCPU(testData);
            await accelerator.SynchronizeAsync();
            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (result[i] != testData[i])
                    throw new Exception($"Int8 round-trip mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        static void Int8DoubleKernel(Index1D idx, ArrayView<sbyte> src, ArrayView<int> dst)
        {
            dst[idx] = (int)src[idx] * 2;
        }

        /// <summary>
        /// Verify ArrayView&lt;sbyte&gt; buffer read works correctly on all backends.
        /// Writes sbyte[] via CopyFromCPU, kernel reads as sbyte, multiplies by 2, outputs to int buffer.
        /// </summary>
        [TestMethod]
        public async Task Int8_BufferRead_Test() => await RunTest(async accelerator =>
        {
            var testData = new sbyte[] { 0, 1, -1, 127, -128, 42, -42, 100 };
            using var input = accelerator.Allocate1D<sbyte>(testData.Length);
            using var output = accelerator.Allocate1D<int>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<sbyte>, ArrayView<int>>(Int8DoubleKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                int expected = (int)testData[i] * 2;
                if (result[i] != expected)
                    throw new Exception($"Int8 read mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        static void Int8WriteKernel(Index1D idx, ArrayView<sbyte> dst, ArrayView<int> src)
        {
            dst[idx] = (sbyte)src[idx];
        }

        /// <summary>
        /// Verify ArrayView&lt;sbyte&gt; buffer write works correctly.
        /// Kernel writes sbyte values to a sbyte buffer, read back and verify.
        /// </summary>
        [TestMethod]
        public async Task Int8_BufferWrite_Test() => await RunTest(async accelerator =>
        {
            var srcData = new int[] { 1, -2, 100, -100, 127, -128, 0, 42 };
            using var src = accelerator.Allocate1D<int>(srcData.Length);
            using var dst = accelerator.Allocate1D<sbyte>(srcData.Length);

            src.CopyFromCPU(srcData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<sbyte>, ArrayView<int>>(Int8WriteKernel);
            kernel(srcData.Length, dst.View, src.View);
            await accelerator.SynchronizeAsync();

            var result = await dst.CopyToHostAsync();
            for (int i = 0; i < srcData.Length; i++)
            {
                sbyte expected = (sbyte)srcData[i];
                if (result[i] != expected)
                    throw new Exception($"Int8 write mismatch at [{i}]: expected {expected}, got {result[i]}");
            }
        });

        static void Int8EndToEndKernel(Index1D idx, ArrayView<sbyte> input, ArrayView<sbyte> output)
        {
            output[idx] = (sbyte)(input[idx] + (sbyte)5);
        }

        /// <summary>
        /// End-to-end: write sbyte data, kernel reads AND writes sbyte buffers.
        /// Tests both sub-word Load and sub-word Store in one kernel.
        /// </summary>
        [TestMethod]
        public async Task Int8_EndToEnd_ReadWrite_Test() => await RunTest(async accelerator =>
        {
            var testData = new sbyte[] { 0, 1, -1, 50, -50, 122, -123, 42 };
            using var input = accelerator.Allocate1D<sbyte>(testData.Length);
            using var output = accelerator.Allocate1D<sbyte>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<sbyte>, ArrayView<sbyte>>(Int8EndToEndKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                sbyte expected = (sbyte)(testData[i] + 5);
                if (result[i] != expected)
                    throw new Exception($"Int8 end-to-end mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        // ==================== UInt8 Sub-Word Buffer Access Tests ====================

        /// <summary>
        /// Basic round-trip: CopyFromCPU byte[] -> CopyToHostAsync byte[] (no kernel).
        /// Verifies buffer I/O works for byte type.
        /// </summary>
        [TestMethod]
        public async Task UInt8_RoundTrip_NoKernel_Test() => await RunTest(async accelerator =>
        {
            var testData = new byte[] { 0, 1, 127, 128, 254, 255, 42, 100 };
            using var buffer = accelerator.Allocate1D<byte>(testData.Length);
            buffer.CopyFromCPU(testData);
            await accelerator.SynchronizeAsync();
            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (result[i] != testData[i])
                    throw new Exception($"UInt8 round-trip mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        static void UInt8DoubleKernel(Index1D idx, ArrayView<byte> src, ArrayView<int> dst)
        {
            dst[idx] = (int)src[idx] * 2;
        }

        /// <summary>
        /// Verify ArrayView&lt;byte&gt; buffer read works correctly on all backends.
        /// Writes byte[] via CopyFromCPU, kernel reads as byte, multiplies by 2, outputs to int buffer.
        /// </summary>
        [TestMethod]
        public async Task UInt8_BufferRead_Test() => await RunTest(async accelerator =>
        {
            var testData = new byte[] { 0, 1, 127, 128, 254, 255, 42, 100 };
            using var input = accelerator.Allocate1D<byte>(testData.Length);
            using var output = accelerator.Allocate1D<int>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>>(UInt8DoubleKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                int expected = (int)testData[i] * 2;
                if (result[i] != expected)
                    throw new Exception($"UInt8 read mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        static void UInt8WriteKernel(Index1D idx, ArrayView<byte> dst, ArrayView<int> src)
        {
            dst[idx] = (byte)src[idx];
        }

        /// <summary>
        /// Verify ArrayView&lt;byte&gt; buffer write works correctly.
        /// Kernel writes byte values to a byte buffer, read back and verify.
        /// </summary>
        [TestMethod]
        public async Task UInt8_BufferWrite_Test() => await RunTest(async accelerator =>
        {
            var srcData = new int[] { 0, 1, 127, 128, 254, 255, 42, 100 };
            using var src = accelerator.Allocate1D<int>(srcData.Length);
            using var dst = accelerator.Allocate1D<byte>(srcData.Length);

            src.CopyFromCPU(srcData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>>(UInt8WriteKernel);
            kernel(srcData.Length, dst.View, src.View);
            await accelerator.SynchronizeAsync();

            var result = await dst.CopyToHostAsync();
            for (int i = 0; i < srcData.Length; i++)
            {
                byte expected = (byte)srcData[i];
                if (result[i] != expected)
                    throw new Exception($"UInt8 write mismatch at [{i}]: expected {expected}, got {result[i]}");
            }
        });

        static void UInt8EndToEndKernel(Index1D idx, ArrayView<byte> input, ArrayView<byte> output)
        {
            output[idx] = (byte)(input[idx] + (byte)5);
        }

        /// <summary>
        /// End-to-end: write byte data, kernel reads AND writes byte buffers.
        /// Tests both sub-word Load and sub-word Store in one kernel.
        /// </summary>
        [TestMethod]
        public async Task UInt8_EndToEnd_ReadWrite_Test() => await RunTest(async accelerator =>
        {
            var testData = new byte[] { 0, 1, 100, 127, 250, 42, 0, 200 };
            using var input = accelerator.Allocate1D<byte>(testData.Length);
            using var output = accelerator.Allocate1D<byte>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>>(UInt8EndToEndKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                byte expected = (byte)(testData[i] + 5);
                if (result[i] != expected)
                    throw new Exception($"UInt8 end-to-end mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {testData[i]}");
            }
        });

        // ==================== Half Intrinsics Tests ====================

        static void HalfAbsKernel(Index1D idx, ArrayView<global::ILGPU.Half> src, ArrayView<global::ILGPU.Half> dst)
        {
            dst[idx] = global::ILGPU.Half.Abs(src[idx]);
        }

        /// <summary>
        /// Verify Half.Abs intrinsic works correctly on all backends.
        /// Writes Half values (including negatives), kernel applies Abs, verifies against CPU reference.
        /// </summary>
        [TestMethod]
        public async Task Half_Abs_Test() => await RunTest(async accelerator =>
        {
            var testFloats = new float[] { 1.0f, -2.5f, 0.0f, -100.0f };
            var testData = testFloats.Select(f => (global::ILGPU.Half)f).ToArray();
            using var input = accelerator.Allocate1D<global::ILGPU.Half>(testData.Length);
            using var output = accelerator.Allocate1D<global::ILGPU.Half>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>>(HalfAbsKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                float expected = MathF.Abs(testFloats[i]);
                float actual = (float)result[i];
                // Half precision tolerance
                if (MathF.Abs(actual - expected) > 0.1f)
                    throw new Exception($"Half Abs mismatch at [{i}]: expected {expected}, got {actual}. Input was {testFloats[i]}");
            }
        });

        static void HalfMinMaxKernel(Index1D idx, ArrayView<global::ILGPU.Half> a, ArrayView<global::ILGPU.Half> b, ArrayView<float> minOut, ArrayView<float> maxOut)
        {
            var va = (float)a[idx];
            var vb = (float)b[idx];
            minOut[idx] = va < vb ? va : vb;
            maxOut[idx] = va > vb ? va : vb;
        }

        /// <summary>
        /// Verify Half min/max operations via float comparison on all backends.
        /// Reads two Half arrays, computes min and max via float cast, writes to float outputs.
        /// </summary>
        [TestMethod]
        public async Task Half_MinMax_Test() => await RunTest(async accelerator =>
        {
            var aFloats = new float[] { 1f, 3f, -2f, 0f };
            var bFloats = new float[] { 2f, 1f, -1f, 0f };
            var aData = aFloats.Select(f => (global::ILGPU.Half)f).ToArray();
            var bData = bFloats.Select(f => (global::ILGPU.Half)f).ToArray();
            int length = aData.Length;

            using var aBuffer = accelerator.Allocate1D<global::ILGPU.Half>(length);
            using var bBuffer = accelerator.Allocate1D<global::ILGPU.Half>(length);
            using var minBuffer = accelerator.Allocate1D<float>(length);
            using var maxBuffer = accelerator.Allocate1D<float>(length);

            aBuffer.CopyFromCPU(aData);
            bBuffer.CopyFromCPU(bData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<global::ILGPU.Half>, ArrayView<float>, ArrayView<float>>(HalfMinMaxKernel);
            kernel(length, aBuffer.View, bBuffer.View, minBuffer.View, maxBuffer.View);
            await accelerator.SynchronizeAsync();

            var minResult = await minBuffer.CopyToHostAsync();
            var maxResult = await maxBuffer.CopyToHostAsync();
            for (int i = 0; i < length; i++)
            {
                float expectedMin = MathF.Min(aFloats[i], bFloats[i]);
                float expectedMax = MathF.Max(aFloats[i], bFloats[i]);
                if (MathF.Abs(minResult[i] - expectedMin) > 0.1f)
                    throw new Exception($"Half Min mismatch at [{i}]: expected {expectedMin}, got {minResult[i]}. a={aFloats[i]}, b={bFloats[i]}");
                if (MathF.Abs(maxResult[i] - expectedMax) > 0.1f)
                    throw new Exception($"Half Max mismatch at [{i}]: expected {expectedMax}, got {maxResult[i]}. a={aFloats[i]}, b={bFloats[i]}");
            }
        });

        // ==================== Half Clamp Test ====================

        static void HalfClampKernel(Index1D idx, ArrayView<global::ILGPU.Half> input, ArrayView<float> output)
        {
            float val = (float)input[idx];
            output[idx] = IntrinsicMath.Clamp(val, -1.0f, 1.0f);
        }

        [TestMethod]
        public async Task Half_Clamp_Test() => await RunTest(async accelerator =>
        {
            var testFloats = new float[] { 0.5f, -0.5f, 2.0f, -3.0f, 0.0f, 1.0f, -1.0f, 0.001f };
            var testData = testFloats.Select(f => (global::ILGPU.Half)f).ToArray();
            using var input = accelerator.Allocate1D<global::ILGPU.Half>(testData.Length);
            using var output = accelerator.Allocate1D<float>(testData.Length);

            input.CopyFromCPU(testData);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<global::ILGPU.Half>, ArrayView<float>>(HalfClampKernel);
            kernel(testData.Length, input.View, output.View);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                float val = (float)testData[i];
                float expected = val < -1.0f ? -1.0f : (val > 1.0f ? 1.0f : val);
                if (MathF.Abs(result[i] - expected) > 0.01f)
                    throw new Exception($"Half Clamp mismatch at [{i}]: expected {expected}, got {result[i]}. Input was {val}");
            }
        });

        // ==================== Float16 RoundTrip Test ====================

        [TestMethod]
        public async Task Float16_RoundTrip_NoKernel_Test() => await RunTest(async accelerator =>
        {
            var testFloats = new float[] { 1.0f, -2.5f, 0.0f, 3.14f, 100.0f, -0.5f, 0.001f, 65504.0f };
            var testData = testFloats.Select(f => (global::ILGPU.Half)f).ToArray();
            using var buffer = accelerator.Allocate1D<global::ILGPU.Half>(testData.Length);
            buffer.CopyFromCPU(testData);
            await accelerator.SynchronizeAsync();
            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                float expected = (float)testData[i];
                float actual = (float)result[i];
                if (MathF.Abs(actual - expected) > 0.01f)
                    throw new Exception($"Float16 round-trip mismatch at [{i}]: expected {expected}, got {actual}");
            }
        });

        // ==================== CopyFromJS Sub-Word Tests ====================

        [TestMethod]
        public async Task CopyFromJS_Int16_WritesCorrectDataTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("CopyFromJS only available on browser backends");

            var testData = new short[] { 1, -2, 300, -400, 32767, -32768, 0, 42 };
            using var buffer = accelerator.Allocate1D<short>(testData.Length);

            var browserBuffer = buffer.Buffer as IBrowserMemoryBuffer;
            if (browserBuffer == null)
                throw new Exception("Buffer does not implement IBrowserMemoryBuffer");

            using var jsArray = new Int16Array(testData);
            browserBuffer.CopyFromJS(jsArray);

            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (result[i] != testData[i])
                    throw new Exception($"CopyFromJS Int16 mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        [TestMethod]
        public async Task CopyFromJS_UInt8_WritesCorrectDataTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("CopyFromJS only available on browser backends");

            var testData = new byte[] { 0, 1, 127, 128, 254, 255, 42, 100 };
            using var buffer = accelerator.Allocate1D<byte>(testData.Length);

            var browserBuffer = buffer.Buffer as IBrowserMemoryBuffer;
            if (browserBuffer == null)
                throw new Exception("Buffer does not implement IBrowserMemoryBuffer");

            using var jsArray = new Uint8Array(testData);
            browserBuffer.CopyFromJS(jsArray);

            var result = await buffer.CopyToHostAsync();
            for (int i = 0; i < testData.Length; i++)
            {
                if (result[i] != testData[i])
                    throw new Exception($"CopyFromJS UInt8 mismatch at [{i}]: expected {testData[i]}, got {result[i]}");
            }
        });

        // ==================== Many-Binding Tests ====================
        // These tests verify that kernels with many ArrayView parameters work
        // within WebGPU's maxStorageBuffersPerShaderStage limit.
        // AubsCraft's HeightmapMeshKernel has 11 ArrayView params + 1 scalar buffer = 12 bindings,
        // which exceeds Chrome's typical limit of 10.

        /// <summary>
        /// Kernel with 8 ArrayView params (all int) + 2 scalars = 9 bindings (under limit).
        /// Should pass on all backends.
        /// </summary>
        static void ManyViewsKernel_8Views(
            Index1D index,
            ArrayView<int> a, ArrayView<int> b, ArrayView<int> c, ArrayView<int> d,
            ArrayView<float> e, ArrayView<float> f, ArrayView<float> g,
            ArrayView<int> output,
            int scalarA, int scalarB)
        {
            output[index] = a[index] + b[index] + c[index] + d[index] + scalarA + scalarB;
        }

        [TestMethod]
        public async Task ManyViews_8Views_UnderLimit_Test() => await RunTest(async accelerator =>
        {
            int size = 64;
            var data = Enumerable.Range(1, size).ToArray();
            using var a = accelerator.Allocate1D<int>(size);
            using var b = accelerator.Allocate1D<int>(size);
            using var c = accelerator.Allocate1D<int>(size);
            using var d = accelerator.Allocate1D<int>(size);
            using var e = accelerator.Allocate1D<float>(size);
            using var f = accelerator.Allocate1D<float>(size);
            using var g = accelerator.Allocate1D<float>(size);
            using var output = accelerator.Allocate1D<int>(size);
            a.CopyFromCPU(data);
            b.CopyFromCPU(data);
            c.CopyFromCPU(data);
            d.CopyFromCPU(data);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>,
                ArrayView<float>, ArrayView<float>, ArrayView<float>,
                ArrayView<int>, int, int>(ManyViewsKernel_8Views);
            kernel(size, a.View, b.View, c.View, d.View, e.View, f.View, g.View, output.View, 10, 20);
            await accelerator.SynchronizeAsync();

            var result = await output.CopyToHostAsync();
            for (int i = 0; i < size; i++)
            {
                int expected = data[i] * 4 + 30; // a+b+c+d + 10 + 20
                if (result[i] != expected)
                    throw new Exception($"8-view kernel mismatch at [{i}]: expected {expected}, got {result[i]}");
            }
        });

        /// <summary>
        /// Kernel with 11 ArrayView params (including short) + 2 scalars.
        /// This matches AubsCraft HeightmapMeshKernel's binding pattern:
        /// 11 storage buffers + 1 scalar buffer = 12 total bindings.
        /// Tests whether the backend handles exceeding maxStorageBuffersPerShaderStage.
        /// </summary>
        static void ManyViewsKernel_11Views_WithShort(
            Index1D index,
            ArrayView<int> heights,
            ArrayView<short> blockIds,
            ArrayView<float> paletteColors,
            ArrayView<float> atlasUVs,
            ArrayView<float> blockFlags,
            ArrayView<int> seabedHeights,
            ArrayView<short> seabedBlockIds,
            ArrayView<float> opaqueVerts,
            ArrayView<int> opaqueCounter,
            ArrayView<float> waterVerts,
            ArrayView<int> waterCounter,
            int chunkX, int chunkZ)
        {
            int blockId = blockIds[index];
            if (blockId == 0) return;

            float wx = chunkX * 16 + (index % 16);
            float wz = chunkZ * 16 + (index / 16);
            float wy = heights[index];
            float cr = paletteColors[blockId * 3];

            int oo = Atomic.Add(ref opaqueCounter[0], 3);
            opaqueVerts[oo] = wx;
            opaqueVerts[oo + 1] = wy;
            opaqueVerts[oo + 2] = cr;

            int sbId = seabedBlockIds[index];
            if (sbId > 0)
            {
                float sbwy = seabedHeights[index];
                int wo = Atomic.Add(ref waterCounter[0], 2);
                waterVerts[wo] = sbwy;
                waterVerts[wo + 1] = blockFlags[blockId];
            }
        }

        /// <summary>
        /// Negative test: verifies that ILGPU throws a clear error when a kernel
        /// exceeds the maxStorageBuffersPerShaderStage binding limit on WebGPU.
        /// This kernel intentionally uses 11 ArrayViews + 1 scalar = 12 bindings,
        /// exceeding the 10-binding limit. The production fix is struct packing
        /// (see AubsCraft's HeightmapMeshKernel which packs 12 down to 6).
        /// On non-WebGPU backends (CPU, CUDA, OpenCL, Wasm), the kernel runs
        /// normally since they have no binding limit.
        /// </summary>
        [TestMethod]
        public async Task ManyViews_11Views_HeightmapPattern_Test() => await RunTest(async accelerator =>
        {
            int size = 256; // 16x16 chunk
            int paletteSize = 16;

            // Setup test data
            var heights = new int[size];
            var blockIds = new short[size];
            var seabedHeights = new int[size];
            var seabedBlockIds = new short[size];
            var paletteColors = new float[paletteSize * 3];
            var atlasUVs = new float[paletteSize * 4];
            var blockFlags = new float[paletteSize];

            for (int i = 0; i < size; i++)
            {
                heights[i] = 64 + (i % 16);
                blockIds[i] = (short)(1 + (i % (paletteSize - 1))); // 1 to paletteSize-1
                seabedHeights[i] = 32 + (i % 8);
                seabedBlockIds[i] = (short)(1 + (i % 3));
            }
            for (int i = 0; i < paletteSize; i++)
            {
                paletteColors[i * 3] = i * 0.1f;
                paletteColors[i * 3 + 1] = i * 0.2f;
                paletteColors[i * 3 + 2] = i * 0.3f;
                atlasUVs[i * 4] = 0f;
                atlasUVs[i * 4 + 1] = 0f;
                atlasUVs[i * 4 + 2] = 1f;
                atlasUVs[i * 4 + 3] = 1f;
                blockFlags[i] = 0f;
            }

            using var bufHeights = accelerator.Allocate1D<int>(size);
            using var bufBlockIds = accelerator.Allocate1D<short>(size);
            using var bufPaletteColors = accelerator.Allocate1D<float>(paletteSize * 3);
            using var bufAtlasUVs = accelerator.Allocate1D<float>(paletteSize * 4);
            using var bufBlockFlags = accelerator.Allocate1D<float>(paletteSize);
            using var bufSeabedHeights = accelerator.Allocate1D<int>(size);
            using var bufSeabedBlockIds = accelerator.Allocate1D<short>(size);
            using var bufOpaqueVerts = accelerator.Allocate1D<float>(size * 3);
            using var bufOpaqueCounter = accelerator.Allocate1D<int>(1);
            using var bufWaterVerts = accelerator.Allocate1D<float>(size * 2);
            using var bufWaterCounter = accelerator.Allocate1D<int>(1);

            bufHeights.CopyFromCPU(heights);
            bufBlockIds.CopyFromCPU(blockIds);
            bufPaletteColors.CopyFromCPU(paletteColors);
            bufAtlasUVs.CopyFromCPU(atlasUVs);
            bufBlockFlags.CopyFromCPU(blockFlags);
            bufSeabedHeights.CopyFromCPU(seabedHeights);
            bufSeabedBlockIds.CopyFromCPU(seabedBlockIds);
            bufOpaqueCounter.CopyFromCPU(new int[] { 0 });
            bufWaterCounter.CopyFromCPU(new int[] { 0 });

            // This kernel intentionally uses 11 ArrayViews + 1 scalar = 12 bindings.
            // On WebGPU this exceeds maxStorageBuffersPerShaderStage (10) and should
            // throw InvalidOperationException at load or dispatch time. On other backends
            // it runs normally.
            try
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D,
                    ArrayView<int>, ArrayView<short>, ArrayView<float>, ArrayView<float>,
                    ArrayView<float>, ArrayView<int>, ArrayView<short>,
                    ArrayView<float>, ArrayView<int>,
                    ArrayView<float>, ArrayView<int>,
                    int, int>(ManyViewsKernel_11Views_WithShort);

                kernel(size,
                    bufHeights.View, bufBlockIds.View,
                    bufPaletteColors.View, bufAtlasUVs.View, bufBlockFlags.View,
                    bufSeabedHeights.View, bufSeabedBlockIds.View,
                    bufOpaqueVerts.View, bufOpaqueCounter.View,
                    bufWaterVerts.View, bufWaterCounter.View,
                    0, 0);
                await accelerator.SynchronizeAsync();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("storage buffer binding"))
            {
                // WebGPU correctly rejected the kernel - binding limit enforced. PASS.
                return;
            }

            // Non-WebGPU backends: kernel ran, verify output
            var opaqueCount = await bufOpaqueCounter.CopyToHostAsync();
            var waterCount = await bufWaterCounter.CopyToHostAsync();

            if (opaqueCount[0] != size * 3)
                throw new Exception($"Opaque counter: expected {size * 3}, got {opaqueCount[0]}");
            if (waterCount[0] != size * 2)
                throw new Exception($"Water counter: expected {size * 2}, got {waterCount[0]}");

            // Verify vertex colors are valid palette values.
            // Atomic.Add slot allocation means ANY thread can get slot 0, so we check
            // that the color at opaqueVerts[2] matches paletteColors[blockId * 3] for
            // SOME valid blockId, not specifically thread 0's blockId.
            var opaqueVerts = await bufOpaqueVerts.CopyToHostAsync();
            float actualCr = opaqueVerts[2];
            bool foundMatch = false;
            for (int i = 0; i < size; i++)
            {
                float candidateCr = paletteColors[blockIds[i] * 3];
                if (Math.Abs(actualCr - candidateCr) < 0.001f)
                {
                    foundMatch = true;
                    break;
                }
            }
            if (!foundMatch)
                throw new Exception($"Opaque vertex color {actualCr} does not match any valid palette color");
        });

        // ==================== Index3D + ArrayView<short> BindGroupLayout Regression Tests ====================
        // Repros Data's VoxelEngine SDF/DMC WebGPU bug (2026-04-16). CPU/OpenCL/CUDA pass, WebGPU fails
        // with "[Invalid BindGroupLayout (unlabeled)] is invalid" at CreateBindGroup time.
        // Common factors: ArrayView<short> + Index3D + multiple buffers/scalars.

        // Mirrors ModifySdfSphereKernel - Index3D + i16 view + 8 float + 3 int scalars
        static void Repro_Index3D_Short_ManyScalarsKernel(
            Index3D index,
            ArrayView<short> output,
            float a, float b, float c,
            float d,
            int mode,
            float e, float f, float g,
            float h,
            int size)
        {
            int x = index.X;
            int y = index.Y;
            int z = index.Z;
            if (x >= size || y >= size || z >= size) return;

            int idx = x + z * size + y * size * size;
            float v = a + b + c + d + e + f + g + h + mode;
            output[idx] = (short)(v);
        }

        [TestMethod]
        public async Task ShortBuffer_Index3D_ManyScalars_DispatchSucceedsTest() => await RunTest(async accelerator =>
        {
            const int size = 4;
            using var buf = accelerator.Allocate1D<short>(size * size * size);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index3D, ArrayView<short>,
                float, float, float, float, int,
                float, float, float, float, int>(Repro_Index3D_Short_ManyScalarsKernel);

            kernel(new Index3D(size, size, size),
                buf.View,
                1f, 2f, 3f, 4f, 0,
                5f, 6f, 7f, 8f, size);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync();
            short expected = (short)(1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 0);
            for (int i = 0; i < result.Length; i++)
                if (result[i] != expected)
                    throw new Exception($"[{i}] expected {expected}, got {result[i]}");
        });

        // Mirrors ClassifyActiveCellsKernel - Index3D + i16 read + 2x i32 write + 1 int scalar
        static void Repro_Index3D_Short_MultiBufferKernel(
            Index3D index,
            ArrayView<short> sdfInput,
            ArrayView<int> intOutput1,
            ArrayView<int> intOutput2,
            int size)
        {
            int x = index.X;
            int y = index.Y;
            int z = index.Z;
            if (x >= size || y >= size || z >= size) return;

            int idx = x + z * size + y * size * size;
            short v = sdfInput[idx];
            intOutput1[idx] = v;
            intOutput2[idx] = v > 0 ? 1 : 0;
        }

        [TestMethod]
        public async Task ShortBuffer_Index3D_MultiBuffer_DispatchSucceedsTest() => await RunTest(async accelerator =>
        {
            const int size = 4;
            int total = size * size * size;
            var sdfData = new short[total];
            for (int i = 0; i < total; i++) sdfData[i] = (short)((i % 2 == 0) ? 10 : -10);

            using var sdfBuf = accelerator.Allocate1D<short>(total);
            sdfBuf.CopyFromCPU(sdfData);
            using var intBuf1 = accelerator.Allocate1D<int>(total);
            using var intBuf2 = accelerator.Allocate1D<int>(total);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index3D, ArrayView<short>, ArrayView<int>, ArrayView<int>, int>(
                Repro_Index3D_Short_MultiBufferKernel);

            kernel(new Index3D(size, size, size), sdfBuf.View, intBuf1.View, intBuf2.View, size);
            await accelerator.SynchronizeAsync();

            var out1 = await intBuf1.CopyToHostAsync();
            var out2 = await intBuf2.CopyToHostAsync();
            for (int i = 0; i < total; i++)
            {
                int expected1 = sdfData[i];
                int expected2 = sdfData[i] > 0 ? 1 : 0;
                if (out1[i] != expected1)
                    throw new Exception($"out1[{i}] expected {expected1}, got {out1[i]}");
                if (out2[i] != expected2)
                    throw new Exception($"out2[{i}] expected {expected2}, got {out2[i]}");
            }
        });

        // Mirrors GenerateDualVerticesKernel - Index1D + mixed i32/i16/i32/f32/f32 views + many scalars
        static void Repro_Index1D_MixedTypesKernel(
            Index1D idx,
            ArrayView<int> activeIds,
            ArrayView<short> sdfInput,
            ArrayView<int> caseInput,
            ArrayView<float> positionOut,
            ArrayView<float> normalOut,
            int size,
            float a, float b, float c, float d,
            int activeCount)
        {
            int i = idx.X;
            if (i >= activeCount) return;
            int cellId = activeIds[i];
            short s = sdfInput[cellId % (int)sdfInput.IntLength];
            int cs = caseInput[cellId % (int)caseInput.IntLength];

            positionOut[i * 3 + 0] = a + s;
            positionOut[i * 3 + 1] = b + cs;
            positionOut[i * 3 + 2] = c;
            normalOut[i * 3 + 0] = d;
            normalOut[i * 3 + 1] = 0f;
            normalOut[i * 3 + 2] = 1f;
        }

        [TestMethod]
        public async Task ShortBuffer_Index1D_MixedTypes_DispatchSucceedsTest() => await RunTest(async accelerator =>
        {
            const int activeCount = 8;
            const int size = 4;

            var activeIdsData = Enumerable.Range(0, activeCount).ToArray();
            var sdfData = Enumerable.Range(0, 16).Select(i => (short)i).ToArray();
            var caseData = Enumerable.Range(0, 16).ToArray();

            using var activeIds = accelerator.Allocate1D<int>(activeCount);
            activeIds.CopyFromCPU(activeIdsData);
            using var sdfBuf = accelerator.Allocate1D<short>(16);
            sdfBuf.CopyFromCPU(sdfData);
            using var caseBuf = accelerator.Allocate1D<int>(16);
            caseBuf.CopyFromCPU(caseData);
            using var posBuf = accelerator.Allocate1D<float>(activeCount * 3);
            using var normBuf = accelerator.Allocate1D<float>(activeCount * 3);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<short>, ArrayView<int>,
                ArrayView<float>, ArrayView<float>,
                int, float, float, float, float, int>(Repro_Index1D_MixedTypesKernel);

            kernel(new Index1D(activeCount),
                activeIds.View, sdfBuf.View, caseBuf.View, posBuf.View, normBuf.View,
                size, 1f, 2f, 3f, 4f, activeCount);
            await accelerator.SynchronizeAsync();

            var pos = await posBuf.CopyToHostAsync();
            var norm = await normBuf.CopyToHostAsync();
            for (int i = 0; i < activeCount; i++)
            {
                float expX = 1f + i;
                float expY = 2f + i;
                float expZ = 3f;
                if (MathF.Abs(pos[i * 3 + 0] - expX) > 0.001f)
                    throw new Exception($"pos[{i}].x expected {expX}, got {pos[i * 3 + 0]}");
                if (MathF.Abs(pos[i * 3 + 1] - expY) > 0.001f)
                    throw new Exception($"pos[{i}].y expected {expY}, got {pos[i * 3 + 1]}");
                if (MathF.Abs(pos[i * 3 + 2] - expZ) > 0.001f)
                    throw new Exception($"pos[{i}].z expected {expZ}, got {pos[i * 3 + 2]}");
                if (MathF.Abs(norm[i * 3 + 0] - 4f) > 0.001f)
                    throw new Exception($"norm[{i}].x expected 4, got {norm[i * 3 + 0]}");
                if (MathF.Abs(norm[i * 3 + 2] - 1f) > 0.001f)
                    throw new Exception($"norm[{i}].z expected 1, got {norm[i * 3 + 2]}");
            }
        });
    }
}
