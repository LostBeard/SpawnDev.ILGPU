using ILGPU;
using ILGPU.Runtime;
using SpawnDev.SpawnJS.Cryptography;
using SpawnDev.UnitTesting;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

namespace SpawnDev.ILGPU.Demo.UnitTests
{
    /// <summary>
    /// WebGPU backend tests. Inherits all shared tests from BackendTestBase
    /// and adds WebGPU-specific tests.
    /// </summary>
    public class WebGPUTests : BackendTestBase
    {
        public WebGPUTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
        protected override string BackendName => "WebGPU";

        protected override async Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
        {
            return await CreateAcceleratorAsync(enableEmulation: true);
        }

        private async Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync(bool enableEmulation)
        {
            var builder = Context.Create()
                .EnableAlgorithms();
            builder.EnableWebGPUAlgorithms();
            await builder.WebGPU();
            var context = builder.ToContext();
            var devices = context.GetWebGPUDevices();
            if (devices.Count == 0)
                throw new UnsupportedTestException("No WebGPU devices found");
            var device = devices[0];
            var options = new WebGPUBackendOptions
            {
                F64Emulation = enableEmulation ? F64EmulationMode.Ozaki : F64EmulationMode.Disabled,
            };
            var accelerator = await device.CreateAcceleratorAsync(context, options);
            return (context, accelerator);
        }

        protected override async Task<(Context context, Accelerator accelerator)> CreateEmulatedAcceleratorAsync()
        {
            return await CreateAcceleratorAsync(enableEmulation: true);
        }

        #region WebGPU-Specific Tests

        /// <summary>
        /// Test WebGPU accelerator basic initialization (uses WebGPU-specific APIs)
        /// </summary>
        // The byte kernels behind an unaligned MemSet / device copy are compiled by CreateAsync (5.4.21). Before, the first
        // unaligned call (both are synchronous APIs) compiled one on the page thread: SpawnDev.Codecs' sync-compile guard
        // saw it at the first unaligned MemSetToZero after every codec had warmed up (Geordi, 2026-11-09).
        [TestMethod]
        public async Task UnalignedMemSetAndCopy_CompileNothingAfterCreate() => await RunTest(async accelerator =>
        {
            var pattern = new byte[64];
            for (int i = 0; i < pattern.Length; i++) pattern[i] = (byte)(i * 7 + 1);
            using var a = accelerator.Allocate1D(pattern);
            using var b = accelerator.Allocate1D(pattern);
            long before = accelerator.CompiledKernelCount;
            a.View.SubView(1, 6).MemSetToZero();
            a.View.SubView(9, 5).MemSet(0x5A);
            b.View.SubView(3, 9).CopyFrom(a.View.SubView(1, 9));
            await accelerator.SynchronizeAsync();
            long compiled = accelerator.CompiledKernelCount - before;
            if (compiled != 0)
                throw new Exception($"{compiled} kernel(s) compiled by an unaligned MemSet / copy after the accelerator was created");
            var wantA = (byte[])pattern.Clone();
            for (int i = 1; i < 7; i++) wantA[i] = 0;
            for (int i = 9; i < 14; i++) wantA[i] = 0x5A;
            var wantB = (byte[])pattern.Clone();
            Array.Copy(wantA, 1, wantB, 3, 9);
            var gotA = await a.View.CopyToHostAsync();
            var gotB = await b.View.CopyToHostAsync();
            for (int i = 0; i < pattern.Length; i++)
                if (gotA[i] != wantA[i] || gotB[i] != wantB[i])
                    throw new Exception($"[{i}] a {gotA[i]} (want {wantA[i]}), b {gotB[i]} (want {wantB[i]})");
        });

        [TestMethod]
        public async Task WebGPUAcceleratorBasicTest()
        {
            var devices = await WebGPU.WebGPUDevice.GetDevicesAsync();
            if (devices.Length == 0)
                throw new UnsupportedTestException("No WebGPU devices found");
            var device = devices[0];
            using var accelerator = await device.CreateAcceleratorAsync();
            if (!accelerator.IsInitialized)
                throw new Exception("WebGPUAccelerator is not initialized");
            using var buffer = accelerator.Allocate<int>(1024);
            if (buffer.Length != 1024)
                throw new Exception("Buffer length mismatch");
        }

        /// <summary>
        /// Test WebGPU buffer transfer (uses WebGPU-specific APIs)
        /// </summary>
        [TestMethod]
        public async Task WebGPUBufferTransferTest()
        {
            var device = await WebGPU.WebGPUDevice.GetDefaultDeviceAsync();
            if (device == null)
                throw new UnsupportedTestException("No WebGPU devices found");
            using var accelerator = await device.CreateAcceleratorAsync();
            int length = 128;
            var data = Enumerable.Range(0, length).ToArray();
            using var buffer = accelerator.Allocate(data);
            var readBack = await buffer.CopyToHostAsync();
            if (readBack.Length != length)
                throw new Exception($"Readback length mismatch");
            for (int i = 0; i < length; i++)
                if (readBack[i] != data[i])
                    throw new Exception($"Data mismatch at index {i}");
        }

        /// <summary>
        /// Test double precision with explicit F64 emulation options (WebGPU-specific API test)
        /// </summary>
        [TestMethod]
        public async Task WebGPUDoublePrecisionVerifyTest()
        {
            // F64 emulation is now enabled by default, so this test validates
            // that explicitly passing the default options still works
            var builder = Context.Create();
            await builder.WebGPU();
            using var context = builder.ToContext();
            var device = context.GetWebGPUDevices()[0];
            var options = new WebGPUBackendOptions
            {
                F64Emulation = F64EmulationMode.Ozaki,
            };
            using var accelerator = await device.CreateAcceleratorAsync(context, options);

            var data = new double[] { 0.1, 0.2, 0.3, 0.1 + 0.2, 1.0 / 3.0, 2.0 / 3.0 };
            int len = data.Length;
            using var buf = accelerator.Allocate1D(data);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>>(DoublePrecisionVerifyKernel);
            kernel((Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<double>();
            for (int i = 0; i < len; i++)
            {
                double expected = data[i];
                double tolerance = Math.Max(Math.Abs(expected * 0.02), 1e-5);
                if (Math.Abs(result[i] - expected) > tolerance)
                    throw new Exception($"Double precision verify failed at {i}. Expected {expected}, got {result[i]}");
            }
        }


        /// <summary>
        /// Test dynamic shared memory with F64 using WebGPU-specific API
        /// </summary>
        [TestMethod]
        public async Task WebGPUDynamicSharedF64Test()
        {
            // VerboseLogging disabled (was flooding console)
            // F64 emulation is enabled by default
            var builder = Context.Create();
            await builder.WebGPU();
            using var context = builder.ToContext();
            var device = context.GetWebGPUDevices()[0];
            using var accelerator = await device.CreateAcceleratorAsync(context);

            int len = 64;
            var data = new double[len];
            for (int i = 0; i < len; i++) data[i] = i * 1.5;

            using var buf = accelerator.Allocate1D(data);
            var kernel = accelerator.LoadStreamKernel<Index1D, ArrayView<double>>(DynamicSharedF64Kernel);
            kernel(new KernelConfig(1, len, SharedMemoryConfig.RequestDynamic<double>(len)), (Index1D)len, buf.View);
            await accelerator.SynchronizeAsync();

            var result = await buf.CopyToHostAsync<double>();
            for (int i = 0; i < len; i++)
            {
                double expected = (63 - i) * 1.5;
                double tolerance = Math.Max(Math.Abs(expected * 0.01), 1e-6);
                if (Math.Abs(result[i] - expected) > tolerance)
                    throw new Exception($"Dynamic shared F64 failed at {i}. Expected {expected}, got {result[i]}");
            }
        }

        /// <summary>
        /// Test that f32-only kernels work correctly even when f64 emulation is enabled.
        /// This verifies the emulation library doesn't break f32 shaders.
        /// Regression test for the Rendusa DepthEstimationService issue.
        /// </summary>
        [TestMethod]
        public async Task WebGPUF32WithEmulationTest()
        {
            // Force-enable emulation on an f32-only kernel
            var (context, accelerator) = await CreateAcceleratorAsync(enableEmulation: true);
            try
            {
                int length = 64;
                var a = Enumerable.Range(0, length).Select(i => (float)i).ToArray();
                var b = Enumerable.Range(0, length).Select(i => (float)(i * 2)).ToArray();
                var expected = a.Zip(b, (x, y) => x + y).ToArray();

                using var bufA = accelerator.Allocate1D(a);
                using var bufB = accelerator.Allocate1D(b);
                using var bufC = accelerator.Allocate1D<float>(length);

                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                    static (Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c) => { c[index] = a[index] + b[index]; });
                kernel((Index1D)length, bufA.View, bufB.View, bufC.View);
                await accelerator.SynchronizeAsync();

                var result = await bufC.CopyToHostAsync<float>();
                for (int i = 0; i < length; i++)
                {
                    if (Math.Abs(result[i] - expected[i]) > 0.001f)
                        throw new Exception($"VectorAdd with emulation failed at index {i}. Expected {expected[i]}, got {result[i]}");
                }
            }
            finally
            {
                accelerator.Dispose();
                context.Dispose();
            }
        }

        /// <summary>
        /// Replicates the Rendusa ReduceMinMaxKernel pattern:
        /// SharedMemory, Group.Barrier, tree reduction, Math.Min/Max, explicitly grouped.
        /// Runs with emulation enabled to verify compatibility.
        /// </summary>
        [TestMethod]
        public async Task RendusaReduceMinMaxTest()
        {
            var (context, accelerator) = await CreateAcceleratorAsync(enableEmulation: true);
            try
            {
                // WebGPU bakes @workgroup_size(64) into WGSL — must match at dispatch time
                const int groupSize = 64;
                int length = 256;
                int numGroups = (length + groupSize - 1) / groupSize; // 4 groups
                var input = Enumerable.Range(0, length).Select(i => (float)i).ToArray();
                // Output: [min, max] per workgroup → 2 * numGroups values
                var output = new float[numGroups * 2];

                using var bufInput = accelerator.Allocate1D(input);
                using var bufOutput = accelerator.Allocate1D(output);

                var kernel = accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, int>(RendusaReduceMinMaxKernel);
                kernel(new KernelConfig(numGroups, groupSize), bufInput.View, bufOutput.View, length);
                await accelerator.SynchronizeAsync();

                var result = await bufOutput.CopyToHostAsync<float>();
                // Reduce per-group results on CPU
                float globalMin = float.MaxValue, globalMax = float.MinValue;
                for (int g = 0; g < numGroups; g++)
                {
                    globalMin = Math.Min(globalMin, result[g * 2]);
                    globalMax = Math.Max(globalMax, result[g * 2 + 1]);
                }
                if (Math.Abs(globalMin - 0.0f) > 0.001f)
                    throw new Exception($"ReduceMinMax min failed. Expected 0, got {globalMin}");
                if (Math.Abs(globalMax - 255.0f) > 0.001f)
                    throw new Exception($"ReduceMinMax max failed. Expected 255, got {globalMax}");
            }
            finally
            {
                accelerator.Dispose();
                context.Dispose();
            }
        }

        /// <summary>
        /// Replicates the Rendusa NormalizeSmoothKernel pattern:
        /// Scalar float params, control flow (if/else), Math.Min/Max for clamping.
        /// Runs with emulation enabled to verify compatibility.
        /// </summary>
        [TestMethod]
        public async Task RendusaNormalizeSmoothTest()
        {
            var (context, accelerator) = await CreateAcceleratorAsync(enableEmulation: true);
            try
            {
                int length = 64;
                var input = Enumerable.Range(0, length).Select(i => (float)i).ToArray();
                var smoothed = new float[length]; // will be seeded

                float dMin = 0f;
                float invRange = 1f / 63f; // normalize to [0,1]
                float alpha = 0.5f;
                int seedMode = 1; // seed mode = first frame

                using var bufInput = accelerator.Allocate1D(input);
                using var bufSmoothed = accelerator.Allocate1D(smoothed);

                var kernel = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D, ArrayView<float>, ArrayView<float>,
                    float, float, float, int>(RendusaNormalizeSmoothKernel);
                kernel((Index1D)length, bufInput.View, bufSmoothed.View,
                    dMin, invRange, alpha, seedMode);
                await accelerator.SynchronizeAsync();

                var result = await bufSmoothed.CopyToHostAsync<float>();
                for (int i = 0; i < length; i++)
                {
                    float expected = Math.Min(Math.Max((input[i] - dMin) * invRange, 0f), 1f);
                    if (Math.Abs(result[i] - expected) > 0.001f)
                        throw new Exception($"NormalizeSmooth failed at {i}. Expected {expected}, got {result[i]}");
                }
            }
            finally
            {
                accelerator.Dispose();
                context.Dispose();
            }
        }

        #region Rendusa-Pattern Kernels

        static void RendusaReduceMinMaxKernel(
            ArrayView<float> input,
            ArrayView<float> output,
            int length)
        {
            int index = Grid.GlobalIndex.X;
            float localMin = index < length ? input[index] : 1e38f;
            float localMax = index < length ? input[index] : -1e38f;

            // Must match @workgroup_size(64) — WebGPU bakes this at compile time
            var sharedMin = SharedMemory.Allocate<float>(64);
            var sharedMax = SharedMemory.Allocate<float>(64);

            int lid = Group.IdxX;
            sharedMin[lid] = localMin;
            sharedMax[lid] = localMax;
            Group.Barrier();

            for (int stride = Group.DimX / 2; stride > 0; stride /= 2)
            {
                if (lid < stride)
                {
                    sharedMin[lid] = Math.Min(sharedMin[lid], sharedMin[lid + stride]);
                    sharedMax[lid] = Math.Max(sharedMax[lid], sharedMax[lid + stride]);
                }
                Group.Barrier();
            }

            if (lid == 0)
            {
                int groupIdx = Grid.IdxX;
                output[groupIdx * 2] = sharedMin[0];
                output[groupIdx * 2 + 1] = sharedMax[0];
            }
        }

        static void RendusaNormalizeSmoothKernel(
            Index1D index,
            ArrayView<float> input,
            ArrayView<float> smoothed,
            float dMin,
            float invRange,
            float alpha,
            int seedMode)
        {
            float normalized = (input[index] - dMin) * invRange;
            float blended;
            if (seedMode != 0)
            {
                blended = normalized;
            }
            else
            {
                blended = alpha * normalized + (1f - alpha) * smoothed[index];
            }
            smoothed[index] = Math.Min(Math.Max(blended, 0f), 1f);
        }

        /// <summary>
        /// An invalid shader must fail the READBACK when nothing synchronizes before it. The browser only warns about an invalid
        /// pipeline ("[Invalid ComputePipeline] is invalid due to a previous error"), the launch returns and the kernel does
        /// nothing; shader errors surfaced only in SynchronizeAsync, so a caller whose readback is its fence got the buffer's OLD
        /// contents back as the result (SpawnDev.ILGPU.ML face decode, Tuvok 2026-11-01). The shader is the construct that hit
        /// it: a plain i32 store into an array&lt;atomic&lt;i32&gt;&gt;. A second readback must then work (the staging buffer was unmapped).
        /// </summary>
        [TestMethod]
        public async Task WebGPU_InvalidShader_FailsTheReadbackWithoutASync()
        {
            var (context, accelerator) = await CreateAcceleratorAsync(enableEmulation: false);
            try
            {
                var native = ((WebGPUAccelerator)accelerator).NativeAccelerator;
                const string wgsl = @"
@group(0) @binding(0) var<storage, read_write> buf : array<atomic<i32>>;
@compute @workgroup_size(1)
fn main() {
    let p = &buf[1];
    *p = 5;
}";
                using var buffer = native.Allocate(new int[] { 11, 22, 33, 44 });
                using var shader = native.CreateComputeShader(wgsl, "main", label: "InvalidOnPurpose");
                shader.SetBuffer(0, buffer);
                shader.Dispatch(1);
                InvalidOperationException? caught = null;
                try { await buffer.CopyToHostAsync(); }
                catch (InvalidOperationException ex) { caught = ex; }
                if (caught == null)
                    throw new Exception("the readback after an invalid shader returned data instead of throwing (a silent no-op kernel)");
                // The shader's validation error, or the follow-on submit error it causes ("[Invalid CommandBuffer] is invalid due to
                // a previous error"): the browser does not order them, and under load (the 5.4.22 release sweep) the follow-on came
                // first. This accelerator dispatches nothing else, so either one is this shader's failure.
                if (!caught.Message.Contains("failed validation") && !caught.Message.Contains("InvalidOnPurpose")
                    && !caught.Message.Contains("due to a previous error"))
                    throw new Exception($"the readback threw, but not about the invalid shader: {caught.Message}");
                // The error is consumed, and the staging buffer is usable again. The shader causes TWO browser errors (its
                // validation error and the follow-on submit error) whose order and timing the browser does not fix: when the
                // second arrives after the first readback threw, the next readback reports it (each error is reported once;
                // seen in the 5.4.23 WebGPU lane under load). A third readback must then succeed.
                int[] again;
                try { again = await buffer.CopyToHostAsync(); }
                catch (InvalidOperationException followOn) when (followOn.Message.Contains("due to a previous error"))
                {
                    again = await buffer.CopyToHostAsync();
                }
                if (again.Length != 4 || again[0] != 11 || again[3] != 44)
                    throw new Exception($"second readback = [{string.Join(", ", again)}], expected [11, 22, 33, 44]");
            }
            finally
            {
                accelerator.Dispose();
                context.Dispose();
            }
        }

        #endregion

        /// <summary>
        /// Verifies the Interop.SizeOf(Type) fix for closed generic types.
        /// Previously, calling Interop.SizeOf with a closed generic type (e.g. a struct
        /// instantiated with type parameters) would throw Argument_NeedNonGenericType
        /// because MakeGenericMethod does not accept a closed generic as a type argument.
        /// The fix adds a Marshal.SizeOf fallback for generic types.
        /// This test does NOT run a GPU kernel — it verifies the reflection fix directly.
        /// </summary>
        [TestMethod]
        public Task InteropSizeOfClosedGenericTest()
        {
            // A simple closed generic struct to test with
            // (mimics what ReductionImplementation<T,TStride,TReduction> looks like to the runtime)
            var closedGenericType = typeof(System.Collections.Generic.KeyValuePair<int, float>);

            // This should NOT throw Argument_NeedNonGenericType after the fix
            int size = Interop.SizeOf(closedGenericType);

            // KeyValuePair<int, float> = 4 (int) + 4 (float) = 8 bytes
            if (size != 8)
                throw new Exception($"Interop.SizeOf(KeyValuePair<int,float>) returned {size}, expected 8");

            // Also verify a non-generic type still works
            int intSize = Interop.SizeOf(typeof(int));
            if (intSize != 4)
                throw new Exception($"Interop.SizeOf(int) returned {intSize}, expected 4");

            return Task.CompletedTask;
        }

        #endregion

    }

    /// <summary>
    /// Runs the full WebGPU test suite with <see cref="WebGPUBackendOptions.ForceDisableSubgroups"/> = true.
    /// This exercises the workgroup shared-memory shuffle emulation path, allowing developers to verify
    /// their ILGPU kernels work correctly on both the native-subgroup and emulation paths — even on
    /// hardware that natively supports subgroups.
    /// </summary>
    public class WebGPUNoSubgroupsTests : BackendTestBase
    {
        public WebGPUNoSubgroupsTests(IPortableCrypto crypto, SpawnDev.WebTorrent.WebTorrentClient webTorrentClient) : base(crypto, webTorrentClient) { }
        protected override string BackendName => "WebGPU (No Subgroups)";

        protected override void RequireFeature(Accelerator accelerator, string featureName, string? reason = null)
        {
            // Subgroups are force-disabled in this test class - skip tests that need the hardware feature.
            // "subgroup_shuffle" is NOT skipped: Warp.Shuffle has a shared-memory emulation on this path and
            // a device with no subgroups (Intel HD 620 on Chrome 155) takes it in the WebGPUTests lane.
            if (featureName is "subgroups")
                throw new UnsupportedTestException(reason ?? $"Feature '{featureName}' not available (subgroups force-disabled)");
        }

        protected override async Task<(Context context, Accelerator accelerator)> CreateAcceleratorAsync()
        {
            var builder = Context.Create()
                .EnableAlgorithms();
            builder.EnableWebGPUAlgorithms();
            await builder.WebGPU();
            var context = builder.ToContext();
            var devices = context.GetWebGPUDevices();
            if (devices.Count == 0)
                throw new UnsupportedTestException("No WebGPU devices found");
            var device = devices[0];
            var options = new WebGPUBackendOptions
            {
                ForceDisableSubgroups = true   // Force shared-memory emulation path
            };
            var accelerator = await device.CreateAcceleratorAsync(context, options);
            return (context, accelerator);
        }
    }
}
