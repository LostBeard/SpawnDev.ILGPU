using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Barriers under a condition read from a VIEW at a group-uniform address (5.4.47). WGSL's uniformity analysis treats every
    // load from read_write storage as non-uniform, and ILGPU binds views read_write, so `if (n < N) { ... Group.Barrier() ... }`
    // with N = p[2] did not compile on WebGPU ("'workgroupBarrier' must only be called from uniform control flow"): ILGPU.ML's
    // Q4_K GEMV has been gated off WebGPU for it since 2026-10-19. The generator now takes such a condition from invocation 0
    // through workgroupUniformLoad (WGSLKernelFunctionGenerator.BeginUniformIf). Kernels in the GEMV's shape, on every backend
    // with barriers and shared memory.
    public abstract partial class BackendTestBase
    {
        const int UniformViewGroupSize = 64;

        // one group per output column: the `if (n < N)` around a block loop with two barriers, then a tree reduction
        static void UniformViewCondition_GemvShape(ArrayView<float> input, ArrayView<float> w, ArrayView<float> output, ArrayView<int> p)
        {
            int K = p[1], N = p[2];
            int n = Grid.IdxX, tid = Group.IdxX;
            var sh = SharedMemory.Allocate<float>(UniformViewGroupSize);
            var scale = SharedMemory.Allocate<float>(8);
            float partial = 0f;
            if (n < N)
            {
                int blocks = K / 256;
                for (int blk = 0; blk < blocks; blk++)
                {
                    if (tid < 8) scale[tid] = 1f + 0.125f * ((n + blk + tid) % 5);
                    Group.Barrier();
                    for (int s = 0; s < 256 / UniformViewGroupSize; s++)
                    {
                        int k = blk * 256 + tid + s * UniformViewGroupSize;
                        partial += input[k] * w[n * K + k] * scale[(k >> 5) & 7];
                    }
                    Group.Barrier();
                }
            }
            sh[tid] = partial;
            Group.Barrier();
            for (int stride = UniformViewGroupSize / 2; stride > 0; stride >>= 1)
            {
                if (tid < stride) sh[tid] += sh[tid + stride];
                Group.Barrier();
            }
            if (tid == 0 && n < N) output[n] = sh[0];
        }

        // the guard-clause form: the whole group returns before the barriers when its column is past N
        static void UniformViewCondition_Guard(ArrayView<float> input, ArrayView<float> output, ArrayView<int> p)
        {
            int N = p[2];
            int n = Grid.IdxX, tid = Group.IdxX;
            var sh = SharedMemory.Allocate<float>(UniformViewGroupSize);
            if (n >= N) return;
            sh[tid] = input[n * UniformViewGroupSize + tid];
            Group.Barrier();
            for (int stride = UniformViewGroupSize / 2; stride > 0; stride >>= 1)
            {
                if (tid < stride) sh[tid] += sh[tid + stride];
                Group.Barrier();
            }
            if (tid == 0) output[n] = sh[0];
        }

        // a loop that leaves only through `return` (a C# while (true) with returns inside): the function's trailing `return;`
        // after it was unreachable, and Chrome logged "warning: code is unreachable" for it (TRELLIS.2 DeviceRemesher.Link)
        static void ReturnOnlyLoopKernel(Index1D i, ArrayView<int> o)
        {
            int x = i;
            while (true)
            {
                x = (x * 7 + 3) & 1023;
                if ((x & 15) == 5) { o[i] = x; return; }
                if (x == 0) { o[i] = -1; return; }
            }
        }

        // Generated WGSL (5.4.47): one line ending (no '\r': the emulation library's CRLF literals left lone CRs, which Tint
        // counts as line breaks) and no unreachable trailing `return;`. And the kernel still computes the right values.
        [TestMethod(Timeout = 120000)]
        public async Task WebGPU_GeneratedWgsl_NoCarriageReturn_NoTrailingReturn() => await RunTest(async accelerator =>
        {
            if (accelerator is not SpawnDev.ILGPU.WebGPU.WebGPUAccelerator webgpu)
                throw new UnsupportedTestException("WebGPU-only (WGSL text)");
            bool savedCache = ShaderArtifactCache.Enabled;
            ShaderArtifactCache.Enabled = false;
            try
            {
                var wgsl = ((SpawnDev.ILGPU.WebGPU.Backend.WebGPUCompiledKernel)webgpu.Backend.Compile(
                    global::ILGPU.Backends.EntryPoints.EntryPointDescription.FromImplicitlyGroupedKernel(((Action<Index1D, ArrayView<int>>)ReturnOnlyLoopKernel).Method),
                    KernelSpecialization.Empty)).WGSLSource;
                if (wgsl.Contains('\r')) throw new Exception($"the WGSL contains {wgsl.Count(c => c == '\r')} carriage returns");
                if (wgsl.Contains("\n    return;\n}")) throw new Exception("the WGSL ends a function with a `return;` right before its closing brace");
            }
            finally { ShaderArtifactCache.Enabled = savedCache; }
            const int n = 256;
            using var buf = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(ReturnOnlyLoopKernel)(n, buf.View);
            var got = await buf.CopyToHostAsync();
            for (int i = 0; i < n; i++)
            {
                int x = i, want;
                while (true)
                {
                    x = (x * 7 + 3) & 1023;
                    if ((x & 15) == 5) { want = x; break; }
                    if (x == 0) { want = -1; break; }
                }
                if (got[i] != want) throw new Exception($"element {i} is {got[i]}, expected {want}");
            }
        });

        [TestMethod(Timeout = 120000)]
        public async Task Barrier_UnderConditionReadFromAView_CompilesAndRuns() => await RunTest(async accelerator =>
        {
            if (!accelerator.Device.Satisfies(new AcceleratorRequirements { RequiresBarriers = true, RequiresSharedMemory = true }))
                throw new UnsupportedTestException("needs barriers and shared memory");
            const int K = 512, N = 5, groups = 7;   // more groups than columns: groups n >= N skip the work
            var rng = new Random(23);
            var input = new float[K]; for (int i = 0; i < K; i++) input[i] = rng.Next(-8, 9) * 0.25f;
            var w = new float[N * K]; for (int i = 0; i < w.Length; i++) w[i] = rng.Next(-8, 9) * 0.125f;
            var expected = new float[groups];
            for (int n = 0; n < N; n++)
            {
                // the same summation order as the kernel: per-thread partials, then the tree reduction
                var partials = new float[UniformViewGroupSize];
                for (int tid = 0; tid < UniformViewGroupSize; tid++)
                    for (int blk = 0; blk < K / 256; blk++)
                        for (int s = 0; s < 256 / UniformViewGroupSize; s++)
                        {
                            int k = blk * 256 + tid + s * UniformViewGroupSize;
                            int sub = (k >> 5) & 7;
                            float scale = 1f + 0.125f * ((n + blk + sub) % 5);
                            partials[tid] += input[k] * w[n * K + k] * scale;
                        }
                for (int stride = UniformViewGroupSize / 2; stride > 0; stride >>= 1)
                    for (int tid = 0; tid < stride; tid++) partials[tid] += partials[tid + stride];
                expected[n] = partials[0];
            }
            using var inBuf = accelerator.Allocate1D(input);
            using var wBuf = accelerator.Allocate1D(w);
            using var outBuf = accelerator.Allocate1D<float>(groups);
            using var pBuf = accelerator.Allocate1D(new[] { 0, K, N });
            outBuf.MemSetToZero();
            accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<int>>(UniformViewCondition_GemvShape)
                (new KernelConfig(groups, UniformViewGroupSize), inBuf.View, wBuf.View, outBuf.View, pBuf.View);
            var got = await outBuf.CopyToHostAsync();
            for (int n = 0; n < groups; n++)
                if (got[n] != expected[n]) throw new Exception($"GEMV shape: output[{n}] = {got[n]}, expected {expected[n]}");

            // guard clause
            var gIn = new float[groups * UniformViewGroupSize]; for (int i = 0; i < gIn.Length; i++) gIn[i] = rng.Next(-8, 9);
            var gExp = new float[groups];
            for (int n = 0; n < N; n++) { float s = 0; for (int t = 0; t < UniformViewGroupSize; t++) s += gIn[n * UniformViewGroupSize + t]; gExp[n] = s; }
            using var gInBuf = accelerator.Allocate1D(gIn);
            using var gOutBuf = accelerator.Allocate1D<float>(groups);
            gOutBuf.MemSetToZero();
            accelerator.LoadStreamKernel<ArrayView<float>, ArrayView<float>, ArrayView<int>>(UniformViewCondition_Guard)
                (new KernelConfig(groups, UniformViewGroupSize), gInBuf.View, gOutBuf.View, pBuf.View);
            var gGot = await gOutBuf.CopyToHostAsync();
            for (int n = 0; n < groups; n++)
                if (gGot[n] != gExp[n]) throw new Exception($"guard: output[{n}] = {gGot[n]}, expected {gExp[n]}");
            Console.WriteLine($"[report] {accelerator.AcceleratorType} barrier under a view-read condition: GEMV shape {N} of {groups} columns + guard clause right");
        });
    }
}
