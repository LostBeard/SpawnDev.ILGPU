using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 24: TensorView-shaped struct-param regression tests.
    //
    // SpawnDev.ILGPU.ML.Tensors.TensorView<T> is a readonly struct that wraps
    // ArrayView1D<T, Stride1D.Dense> Data + five int fields (D0..D3, Rank). It's
    // the canonical "single-view + scalar metadata" struct - blittable, kernel-passable.
    //
    // On 2026-05-24 the ML depth demo went flat-blue after migrating
    // DepthToColormapPalette to a TensorView overload. The diff surfaced two
    // distinct SpawnDev.ILGPU codegen bugs (captured by ML regression test
    // Postprocess_DepthToColormapPalette_TensorView_Matches_Legacy, commit 99e7a17):
    //
    //   (a) WebGPU: in a kernel with TWO TensorView struct params, reading
    //       firstStruct.Data[idx] returns Data[0] for every idx. Writes through the
    //       second struct's Data field at idx work correctly.
    //   (b) Wasm + WebGL: writes to MemoryBuffer2D<int>.View.BaseView silently
    //       zero out even via a plain ArrayView1D kernel (no struct wrapper at all).
    //
    // These tests are scoped to SpawnDev.ILGPU - no dependency on the ML library.
    // The struct shape (one ArrayView1D + 5 ints) mirrors TensorView<T> exactly.
    public abstract partial class BackendTestBase
    {
        #region TensorView-shaped Struct-Param Regression

        /// <summary>Mirrors SpawnDev.ILGPU.ML.Tensors.TensorView&lt;float&gt; field layout.</summary>
        public readonly struct ViewStructF
        {
            public readonly ArrayView1D<float, Stride1D.Dense> Data;
            public readonly int D0, D1, D2, D3, Rank;
            public ViewStructF(ArrayView1D<float, Stride1D.Dense> data, int d0, int d1, int d2, int d3, int rank)
            { Data = data; D0 = d0; D1 = d1; D2 = d2; D3 = d3; Rank = rank; }
        }

        /// <summary>Mirrors SpawnDev.ILGPU.ML.Tensors.TensorView&lt;int&gt; field layout.</summary>
        public readonly struct ViewStructI
        {
            public readonly ArrayView1D<int, Stride1D.Dense> Data;
            public readonly int D0, D1, D2, D3, Rank;
            public ViewStructI(ArrayView1D<int, Stride1D.Dense> data, int d0, int d1, int d2, int d3, int rank)
            { Data = data; D0 = d0; D1 = d1; D2 = d2; D3 = d3; Rank = rank; }
        }

        /// <summary>Mirrors SpawnDev.ILGPU.ML.Tensors.TensorView&lt;Half&gt; field layout.</summary>
        public readonly struct ViewStructHalf
        {
            public readonly ArrayView1D<global::ILGPU.Half, Stride1D.Dense> Data;
            public readonly int D0, D1, D2, D3, Rank;
            public ViewStructHalf(ArrayView1D<global::ILGPU.Half, Stride1D.Dense> data, int d0, int d1, int d2, int d3, int rank)
            { Data = data; D0 = d0; D1 = d1; D2 = d2; D3 = d3; Rank = rank; }

            /// <summary>Mirrors SpawnDev.ILGPU.ML.Tensors.TensorView&lt;Half&gt;.Get2D / Set2D.</summary>
            public global::ILGPU.Half Get2D(int r, int c) => Data[r * D1 + c];
            public void Set2D(int r, int c, global::ILGPU.Half v) => Data[r * D1 + c] = v;
        }

        // Kernel pattern: read from first TensorView-shaped struct, write to second.
        // Mirrors DepthToColormapPaletteTensorViewImpl exactly: idx -> in.Data[idx] -> compute -> out.Data[idx].
        static void TwoViewStruct_ReadFirst_WriteSecond_Kernel(Index1D idx, ViewStructF src, ViewStructI dst)
        {
            // If src.Data[idx] reads src.Data[0] for all idx (WebGPU bug signature),
            // every output element will be the same value. CPU reference reads idx,
            // so the per-index output should be 100 + idx*2.
            float v = src.Data[idx];
            dst.Data[idx] = 100 + (int)(v * 2f);
        }

        // Mirrors the ML DepthToColormapPalette pattern *exactly*: the kernel
        // function unwraps each struct's Data field and forwards both ArrayViews
        // (plus scalar params) into a helper that does the actual indexing.
        // The ML kernel also has trailing scalar params (float, float, int) and
        // branchy logic in the helper; both modelled below to keep the codegen
        // shape as close as possible.
        static void TwoViewStruct_HelperPattern_Kernel(Index1D idx, ViewStructF src, ViewStructI dst,
            float scaleA, float scaleB, int mode)
        {
            TwoViewStruct_HelperPattern_Impl(idx, src.Data, dst.Data, scaleA, scaleB, mode);
        }

        static void TwoViewStruct_HelperPattern_Impl(Index1D idx,
            ArrayView1D<float, Stride1D.Dense> src,
            ArrayView1D<int, Stride1D.Dense> dst,
            float scaleA, float scaleB, int mode)
        {
            float v = src[idx];
            float t = (v - scaleA) / (scaleB - scaleA);
            // Branchy logic so the helper isn't trivially inlined.
            int result;
            if (mode == 0)
                result = (int)(t * 255f);
            else if (mode == 1)
                result = (int)((1f - t) * 255f);
            else
                result = (int)(t * 128f) + 64;
            dst[idx] = 1000 + result;
        }

        /// <summary>
        /// WGSL-capture variant: same as HelperPattern_1DOutput but also installs
        /// a WebGPUBackend.OnShaderCompiled hook to capture the generated WGSL.
        /// The captured shader is embedded in the failure message so we can see
        /// exactly what's wrong with the scalar-slot layout. Skipped on non-WebGPU
        /// backends since the bug is WebGPU-specific.
        /// </summary>
        [TestMethod]
        public async Task TensorViewStructParam_HelperPattern_1DOutput_DumpsWGSL() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType != AcceleratorType.WebGPU)
                throw new UnsupportedTestException("WebGPU-only WGSL capture diagnostic");

            const int N = 16;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i;

            using var srcBuf = accelerator.Allocate1D(src);
            using var dstBuf = accelerator.Allocate1D<int>(N);

            var srcView = new ViewStructF(srcBuf.View, N, 1, 1, 1, 1);
            var dstView = new ViewStructI(dstBuf.View, N, 1, 1, 1, 1);

            // Install WGSL capture hook before kernel load.
            string? capturedWgsl = null;
            string? capturedHelperWgsl = null;
            Action<string, string, global::SpawnDev.ILGPU.WebGPU.Backend.WGSLEntry>? handler = (name, wgsl, info) =>
            {
                if (name.Contains("HelperPattern_Kernel")) capturedWgsl = wgsl;
                else if (name.Contains("HelperPattern_Impl")) capturedHelperWgsl = wgsl;
            };
            global::SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.OnShaderCompiled += handler;
            try
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructF, ViewStructI, float, float, int>(
                    TwoViewStruct_HelperPattern_Kernel);
                kernel((Index1D)N, srcView, dstView, 0f, 15f, 0);
                await accelerator.SynchronizeAsync();
            }
            finally
            {
                global::SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.OnShaderCompiled -= handler;
            }

            var actual = await dstBuf.CopyToHostAsync<int>(0, N);
            int diffs = 0;
            for (int i = 0; i < N; i++)
            {
                float t = (i - 0f) / (15f - 0f);
                int expected = 1000 + (int)(t * 255f);
                if (actual[i] != expected) diffs++;
            }
            if (diffs > 0)
            {
                // Strip i64/f64 emulation library prelude; we only need bindings + kernel body.
                // The dump captures everything from `Kernel:` header to end. Cut to the
                // "@group" first occurrence (binding decls start here in GenerateHeader).
                static string TrimPrelude(string? wgsl)
                {
                    if (string.IsNullOrEmpty(wgsl)) return "(not captured)";
                    var idx = wgsl.IndexOf("@group", StringComparison.Ordinal);
                    if (idx < 0)
                    {
                        // Fall back to last 2500 chars
                        return wgsl.Length > 2500 ? "...EARLY OMITTED...\n" + wgsl.Substring(wgsl.Length - 2500) : wgsl;
                    }
                    // Back up to start of comment block before the @group line
                    int lineStart = wgsl.LastIndexOf("// Param", idx, StringComparison.Ordinal);
                    if (lineStart < 0) lineStart = idx;
                    var trimmed = wgsl.Substring(lineStart);
                    if (trimmed.Length > 8000) trimmed = trimmed.Substring(0, 8000) + "\n... TRUNCATED ...";
                    return trimmed;
                }
                throw new Exception(
                    $"{diffs}/{N} mismatches. actual[0..3]={actual[0]},{actual[1]},{actual[2]},{actual[3]} " +
                    $"expected[0..3]=1000,1017,1034,1051\n" +
                    $"=== KERNEL WGSL (post-prelude) ===\n{TrimPrelude(capturedWgsl)}\n" +
                    $"=== HELPER WGSL (post-prelude) ===\n{TrimPrelude(capturedHelperWgsl)}");
            }
        });

        /// <summary>
        /// Bisect variant A: same helper-pattern kernel as the failing test,
        /// but output buffer is Allocate1D instead of Allocate2DDenseX. Isolates
        /// the 2D-buffer-output factor from the struct-param + helper factors.
        /// </summary>
        [TestMethod]
        public async Task TensorViewStructParam_HelperPattern_1DOutput_Indexes_Correctly() => await RunTest(async accelerator =>
        {
            const int N = 16;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i;

            using var srcBuf = accelerator.Allocate1D(src);
            using var dstBuf = accelerator.Allocate1D<int>(N);

            var srcView = new ViewStructF(srcBuf.View, N, 1, 1, 1, 1);
            var dstView = new ViewStructI(dstBuf.View, N, 1, 1, 1, 1);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructF, ViewStructI, float, float, int>(
                TwoViewStruct_HelperPattern_Kernel);
            kernel((Index1D)N, srcView, dstView, 0f, 15f, 0);
            await accelerator.SynchronizeAsync();

            var actual = await dstBuf.CopyToHostAsync<int>(0, N);

            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                float t = (i - 0f) / (15f - 0f);
                int expected = 1000 + (int)(t * 255f);
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4) msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches. First: {msg}");
        });

        /// <summary>
        /// Bisect variant B: 2D-buffer output AND helper pattern, but source is
        /// Allocate1D-backed (the failing test had this combo). If this passes
        /// while the original fails, the bug requires both source and dest in
        /// specific configurations.
        /// </summary>
        [TestMethod]
        public async Task TensorViewStructParam_HelperPattern_Inline_Indexes_Correctly() => await RunTest(async accelerator =>
        {
            // Same kernel as TwoViewStruct_HelperPattern_Kernel but inlined - no helper call.
            // If this passes while the helper-call test fails, the bug is triggered by the
            // ILGPU IR pattern produced when struct.Data is forwarded to a helper.
            const int N = 16;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i;

            using var srcBuf = accelerator.Allocate1D(src);
            using var dstBuf2D = accelerator.Allocate2DDenseX<int>(new Index2D(N, 1));

            var srcView = new ViewStructF(srcBuf.View, N, 1, 1, 1, 1);
            var dstView = new ViewStructI(dstBuf2D.View.BaseView, N, 1, 1, 1, 1);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructF, ViewStructI, float, float, int>(
                static (Index1D idx, ViewStructF src, ViewStructI dst, float scaleA, float scaleB, int mode) =>
                {
                    float v = src.Data[idx];
                    float t = (v - scaleA) / (scaleB - scaleA);
                    int result;
                    if (mode == 0) result = (int)(t * 255f);
                    else if (mode == 1) result = (int)((1f - t) * 255f);
                    else result = (int)(t * 128f) + 64;
                    dst.Data[idx] = 1000 + result;
                });
            kernel((Index1D)N, srcView, dstView, 0f, 15f, 0);
            await accelerator.SynchronizeAsync();

            using var stage = accelerator.Allocate1D<int>(N);
            stage.View.CopyFrom(dstBuf2D.View.BaseView);
            await accelerator.SynchronizeAsync();
            var actual = await stage.CopyToHostAsync<int>(0, N);

            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                float t = (i - 0f) / (15f - 0f);
                int expected = 1000 + (int)(t * 255f);
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4) msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches. First: {msg}");
        });

        /// <summary>
        /// ML-pattern repro: kernel unwraps struct.Data into a helper function with
        /// trailing scalar params + branches. This mirrors the actual depth kernel
        /// shape and is the candidate for triggering the "Data[idx] reads Data[0]"
        /// WebGPU regression that the ML cross-backend test caught.
        /// </summary>
        [TestMethod]
        public async Task TensorViewStructParam_HelperPattern_Indexes_Correctly() => await RunTest(async accelerator =>
        {
            const int N = 16;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i;

            using var srcBuf = accelerator.Allocate1D(src);
            // Source allocated as 2D MemoryBuffer<int> to mirror the ML depth
            // pipeline exactly - this is what fails on WebGPU+TensorView.
            using var dstBuf2D = accelerator.Allocate2DDenseX<int>(new Index2D(N, 1));

            var srcView = new ViewStructF(srcBuf.View, N, 1, 1, 1, 1);
            var dstView = new ViewStructI(dstBuf2D.View.BaseView, N, 1, 1, 1, 1);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructF, ViewStructI, float, float, int>(
                TwoViewStruct_HelperPattern_Kernel);
            kernel((Index1D)N, srcView, dstView, 0f, 15f, 0);
            await accelerator.SynchronizeAsync();

            using var stage = accelerator.Allocate1D<int>(N);
            stage.View.CopyFrom(dstBuf2D.View.BaseView);
            await accelerator.SynchronizeAsync();
            var actual = await stage.CopyToHostAsync<int>(0, N);

            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                float t = (i - 0f) / (15f - 0f);
                int expected = 1000 + (int)(t * 255f);
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4) msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches. First: {msg}");
        });

        /// <summary>
        /// Same as <see cref="TensorViewStructParam_Half_AddConstant_Indexes_Correctly"/> but
        /// calls Get2D/Set2D like MLTestBase.TensorView_Half_RoundTrip (not raw Data[]).
        /// </summary>
        [TestMethod]
        public async Task TensorViewStructParam_Half_Get2DSet2D_RoundTrip() => await RunTest(async accelerator =>
        {
            const int Rows = 4, Cols = 8;
            const int Count = Rows * Cols;
            var hostIn = new global::ILGPU.Half[Count];
            for (int i = 0; i < Count; i++)
                hostIn[i] = (global::ILGPU.Half)(i * 0.25f);

            using var inBuf = accelerator.Allocate1D(hostIn);
            using var outBuf = accelerator.Allocate1D<global::ILGPU.Half>(Count);

            var inView = new ViewStructHalf(inBuf.View, Rows, Cols, 1, 1, 2);
            var outView = new ViewStructHalf(outBuf.View, Rows, Cols, 1, 1, 2);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructHalf, ViewStructHalf>(
                (Index1D idx, ViewStructHalf inp, ViewStructHalf outp) =>
                {
                    int c = idx % inp.D1;
                    int r = idx / inp.D1;
                    var v = inp.Get2D(r, c);
                    outp.Set2D(r, c, (global::ILGPU.Half)((float)v + 1.5f));
                });
            kernel((Index1D)Count, inView, outView);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<global::ILGPU.Half>(0, Count);
            for (int i = 0; i < Count; i++)
            {
                float expected = (float)hostIn[i] + 1.5f;
                float actual = (float)result[i];
                if (Math.Abs(actual - expected) > 1e-2f)
                    throw new Exception($"Idx {i}: expected {expected} got {actual}");
            }
        });

        /// <summary>
        /// TensorView&lt;Half&gt; body-struct kernel pattern from SpawnDev.ILGPU.ML
        /// (MLTestBase.TensorView_Half_RoundTrip). Regression for Wasm/WebGL zero-output bug.
        /// </summary>
        [TestMethod]
        public async Task TensorViewStructParam_Half_AddConstant_Indexes_Correctly() => await RunTest(async accelerator =>
        {
            const int Rows = 4, Cols = 8;
            const int Count = Rows * Cols;
            var hostIn = new global::ILGPU.Half[Count];
            for (int i = 0; i < Count; i++)
                hostIn[i] = (global::ILGPU.Half)(i * 0.25f);

            using var inBuf = accelerator.Allocate1D(hostIn);
            using var outBuf = accelerator.Allocate1D<global::ILGPU.Half>(Count);

            var inView = new ViewStructHalf(inBuf.View, Rows, Cols, 1, 1, 2);
            var outView = new ViewStructHalf(outBuf.View, Rows, Cols, 1, 1, 2);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructHalf, ViewStructHalf>(
                (Index1D idx, ViewStructHalf inp, ViewStructHalf outp) =>
                {
                    int c = idx % inp.D1;
                    int r = idx / inp.D1;
                    var v = inp.Data[r * inp.D1 + c];
                    outp.Data[r * outp.D1 + c] = (global::ILGPU.Half)((float)v + 1.5f);
                });
            kernel((Index1D)Count, inView, outView);
            await accelerator.SynchronizeAsync();

            var result = await outBuf.CopyToHostAsync<global::ILGPU.Half>(0, Count);
            for (int i = 0; i < Count; i++)
            {
                float expected = (float)hostIn[i] + 1.5f;
                float actual = (float)result[i];
                if (Math.Abs(actual - expected) > 1e-2f)
                    throw new Exception($"Idx {i}: expected {expected} got {actual}");
            }
        });

        /// <summary>
        /// Two TensorView-shaped struct params, kernel reads from the first's ArrayView
        /// and writes to the second's ArrayView - the exact pattern that broke depth
        /// demo on WebGPU. CPU/CUDA/OpenCL must pass (the bug only surfaces on WebGPU).
        /// </summary>
        [TestMethod]
        public async Task TensorViewStructParam_ReadFirst_WriteSecond_Indexes_Correctly() => await RunTest(async accelerator =>
        {
            const int N = 16;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i;

            using var srcBuf = accelerator.Allocate1D(src);
            using var dstBuf = accelerator.Allocate1D<int>(N);

            var srcView = new ViewStructF(srcBuf.View, N, 1, 1, 1, 1);
            var dstView = new ViewStructI(dstBuf.View, N, 1, 1, 1, 1);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructF, ViewStructI>(
                TwoViewStruct_ReadFirst_WriteSecond_Kernel);
            kernel((Index1D)N, srcView, dstView);
            await accelerator.SynchronizeAsync();

            var actual = await dstBuf.CopyToHostAsync<int>(0, N);
            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                int expected = 100 + i * 2;
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4)
                        msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches. First mismatches: {msg}");
        });

        // Source buffer is a MemoryBuffer2D<int>'s BaseView. Mirrors the ML depth
        // pipeline's resultBuf.View.BaseView usage.
        static void Write1DKernel(Index1D idx, ArrayView1D<int, Stride1D.Dense> dst)
        {
            dst[idx] = 1000 + idx;
        }

        /// <summary>
        /// Wasm + WebGL silently-zero bug. Allocate a MemoryBuffer2D&lt;int&gt;,
        /// dispatch a plain ArrayView1D-output kernel against its BaseView, read back.
        /// CPU/CUDA/OpenCL must pass. On Wasm + WebGL the readback is currently all-zero.
        /// </summary>
        [TestMethod]
        public async Task MemoryBuffer2D_Int_BaseView_Writes_Are_Visible() => await RunTest(async accelerator =>
        {
            const int W = 4, H = 4, N = W * H;
            using var buf2D = accelerator.Allocate2DDenseX<int>(new Index2D(W, H));

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>>(
                Write1DKernel);
            kernel((Index1D)N, buf2D.View.BaseView);
            await accelerator.SynchronizeAsync();

            // Stage to 1D for readback (CopyToHostAsync is on MemoryBuffer1D).
            using var stage = accelerator.Allocate1D<int>(N);
            stage.View.CopyFrom(buf2D.View.BaseView);
            await accelerator.SynchronizeAsync();
            var actual = await stage.CopyToHostAsync<int>(0, N);

            int zeros = 0;
            for (int i = 0; i < N; i++) if (actual[i] == 0) zeros++;
            if (zeros == N)
                throw new Exception("MemoryBuffer2D<int>.BaseView writes silently zeroed - kernel didn't materialise.");

            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                int expected = 1000 + i;
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4) msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches. First: {msg}");
        });

        // Mirrors the ML DepthToColormapPalette legacy signature: (Index1D, ArrayView<float> src,
        // ArrayView<int> dst, int count, float minVal, float maxVal, int palette). The trailing
        // scalars don't involve body structs (no TensorView wrapping), but Wasm's ML repro fails
        // with "legacy kernel produced all-zero output" for the 2D-allocated output buffer.
        static void LegacySignatureKernel(Index1D idx,
            ArrayView1D<float, Stride1D.Dense> src,
            ArrayView1D<int, Stride1D.Dense> dst,
            int count, float minVal, float maxVal, int palette)
        {
            // Stop guard mirrors the ML kernel's "if idx >= count" gate.
            if (idx >= count) return;
            float v = src[idx];
            float t = (v - minVal) / (maxVal - minVal);
            int result;
            if (palette == 0) result = (int)(t * 255f);
            else if (palette == 1) result = (int)((1f - t) * 255f);
            else result = (int)(t * 128f) + 64;
            dst[idx] = 2000 + result;
        }

        /// <summary>
        /// Wasm-specific repro of the ML "legacy kernel produced all-zero output" on
        /// 2D-allocated output buffers. Same call shape as ML's
        /// DepthToColormapPalette (no body struct, just trailing scalars). If this fails
        /// on Wasm but passes on every other backend, the bug is in Wasm-side dispatch
        /// of kernels with trailing scalars when the output target is a 2D-allocated
        /// buffer's BaseView.
        /// </summary>
        [TestMethod]
        public async Task LegacySignature_2DOutput_TrailingScalars_Indexes_Correctly() => await RunTest(async accelerator =>
        {
            const int W = 4, H = 4, N = W * H;
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = i;

            using var srcBuf = accelerator.Allocate1D(src);
            using var dstBuf2D = accelerator.Allocate2DDenseX<int>(new Index2D(W, H));

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>,
                int, float, float, int>(LegacySignatureKernel);
            kernel((Index1D)N, srcBuf.View, dstBuf2D.View.BaseView, N, 0f, 15f, 0);
            await accelerator.SynchronizeAsync();

            using var stage = accelerator.Allocate1D<int>(N);
            stage.View.CopyFrom(dstBuf2D.View.BaseView);
            await accelerator.SynchronizeAsync();
            var actual = await stage.CopyToHostAsync<int>(0, N);

            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                float t = (i - 0f) / (15f - 0f);
                int expected = 2000 + (int)(t * 255f);
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4) msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches. First: {msg}");
        });

        /// <summary>
        /// 1D-only sibling of the failing 2D test. Allocates a plain Allocate1D&lt;int&gt;
        /// output buffer, kernel-writes to it, then reads back via CopyFrom→1D-stage→host.
        /// If THIS fails on WebGL, the bug is NOT specific to MemoryBuffer2D — it's that
        /// WebGL's CopyFrom (GPU→GPU) reads from the CPU-side `_backingArray` which is
        /// never synced after a kernel TF write. The 2D-buffer angle would just be a
        /// red herring forced by the need to stage MemoryBuffer2D through a 1D buffer
        /// for CopyToHostAsync (which is MemoryBuffer1D-only).
        /// </summary>
        [TestMethod]
        public async Task CopyFrom_After_KernelWrite_Sees_Kernel_Output() => await RunTest(async accelerator =>
        {
            const int N = 16;
            using var dst = accelerator.Allocate1D<int>(N);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>>(
                Write1DKernel);
            kernel((Index1D)N, dst.View);
            await accelerator.SynchronizeAsync();

            using var stage = accelerator.Allocate1D<int>(N);
            stage.View.CopyFrom(dst.View);
            await accelerator.SynchronizeAsync();
            var actual = await stage.CopyToHostAsync<int>(0, N);

            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                int expected = 1000 + i;
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4) msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches after CopyFrom-from-kernel-output. First: {msg}");
        });

        /// <summary>
        /// Locks in the contract for <c>CopyFromAsync</c> (v4.9.9-local.3+): the async
        /// API auto-drains pending kernel work on Wasm before the GPU-to-GPU copy, so
        /// callers can write <c>kernel(...); await stage.View.CopyFromAsync(src);</c>
        /// without an explicit <c>await accelerator.SynchronizeAsync();</c> in between.
        ///
        /// Why this test matters: the sync <c>CopyFrom</c> sibling above MUST keep its
        /// explicit <c>SynchronizeAsync</c> on Wasm because Blazor WASM single-threaded
        /// main thread cannot block. <c>CopyFromAsync</c> moves the drain inside the
        /// library so async consumer code is backend-agnostic. If anyone weakens the
        /// Wasm drain, this test fires with stale/zero bytes from the still-pending
        /// kernel write.
        /// </summary>
        [TestMethod]
        public async Task CopyFromAsync_After_KernelWrite_NoExplicitSync() => await RunTest(async accelerator =>
        {
            const int N = 16;
            using var dst = accelerator.Allocate1D<int>(N);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<int, Stride1D.Dense>>(
                Write1DKernel);
            kernel((Index1D)N, dst.View);
            // Deliberately NO explicit SynchronizeAsync here - the contract is that
            // CopyFromAsync drains pending work itself on backends that need it (Wasm).

            using var stage = accelerator.Allocate1D<int>(N);
            await stage.View.CopyFromAsync(dst.View);
            var actual = await stage.CopyToHostAsync<int>(0, N);

            int diffs = 0;
            var msg = new System.Text.StringBuilder();
            for (int i = 0; i < N; i++)
            {
                int expected = 1000 + i;
                if (actual[i] != expected)
                {
                    diffs++;
                    if (diffs <= 4) msg.Append($"[{i}] got={actual[i]} expected={expected} ");
                }
            }
            if (diffs > 0)
                throw new Exception(
                    $"{diffs}/{N} mismatches after CopyFromAsync-from-kernel-output without " +
                    $"explicit Synchronize. CopyFromAsync's auto-drain is broken. First: {msg}");
        });

        /// <summary>
        /// Diagnostic: distinguish whether the WebGL "MemoryBuffer2D.BaseView writes are zero"
        /// failure is on the WRITE path (kernel output not reaching the buffer) or the READ
        /// path (CopyFrom from 2D-buffer-BaseView to 1D-stage missing the data). This test
        /// uploads a CPU-known pattern via CopyFromCPU (bypassing the kernel write entirely),
        /// then reads back via CopyFrom→stage→host. If THIS passes, the bug is in WebGL's
        /// Transform Feedback wiring for 2D-buffer BaseView outputs. If THIS fails, the bug
        /// is in CopyFrom from a 2D buffer's BaseView source.
        /// </summary>
        [TestMethod]
        public async Task MemoryBuffer2D_Int_BaseView_CpuUpload_Readback_Works() => await RunTest(async accelerator =>
        {
            const int W = 4, H = 4, N = W * H;
            using var buf2D = accelerator.Allocate2DDenseX<int>(new Index2D(W, H));

            var seed = new int[N];
            for (int i = 0; i < N; i++) seed[i] = 1000 + i;
            buf2D.View.BaseView.CopyFromCPU(seed);
            await accelerator.SynchronizeAsync();

            using var stage = accelerator.Allocate1D<int>(N);
            stage.View.CopyFrom(buf2D.View.BaseView);
            await accelerator.SynchronizeAsync();
            var actual = await stage.CopyToHostAsync<int>(0, N);

            int diffs = 0;
            for (int i = 0; i < N; i++) if (actual[i] != seed[i]) diffs++;
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} mismatches. actual[0..3]={actual[0]},{actual[1]},{actual[2]},{actual[3]} expected={seed[0]},{seed[1]},{seed[2]},{seed[3]}");
        });

        /// <summary>
        /// Float sibling — does the bug care about element type? If MemoryBuffer2D&lt;float&gt;.BaseView
        /// passes everywhere while int fails on Wasm/WebGL, the bug is dtype-specific (sub-word vs
        /// 4-byte int are both 4 bytes, so it's likely not byte-size but type-routing in codegen).
        /// </summary>
        [TestMethod]
        public async Task MemoryBuffer2D_Float_BaseView_Writes_Are_Visible() => await RunTest(async accelerator =>
        {
            const int W = 4, H = 4, N = W * H;
            using var buf2D = accelerator.Allocate2DDenseX<float>(new Index2D(W, H));

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>>(
                static (Index1D idx, ArrayView1D<float, Stride1D.Dense> dst) => dst[idx] = 1000f + idx);
            kernel((Index1D)N, buf2D.View.BaseView);
            await accelerator.SynchronizeAsync();

            using var stage = accelerator.Allocate1D<float>(N);
            stage.View.CopyFrom(buf2D.View.BaseView);
            await accelerator.SynchronizeAsync();
            var actual = await stage.CopyToHostAsync<float>(0, N);

            int zeros = 0;
            for (int i = 0; i < N; i++) if (actual[i] == 0f) zeros++;
            if (zeros == N)
                throw new Exception("MemoryBuffer2D<float>.BaseView writes silently zeroed - kernel didn't materialise.");

            int diffs = 0;
            for (int i = 0; i < N; i++)
                if (MathF.Abs(actual[i] - (1000f + i)) > 1e-4f) diffs++;
            if (diffs > 0)
                throw new Exception($"{diffs}/{N} float mismatches.");
        });

        #endregion

        #region ML-pattern mirror tests (same-assembly ArrayView1D / ViewStructHalf)

        /// <summary>
        /// Two-buffer float ArrayView1D sanity (in + 1.5f -> out). Mirrors the ML
        /// TensorView round-trip pattern with ILGPU's own ArrayView1D so the ILGPU
        /// solution stays free of any SpawnDev.ILGPU.ML reference (that reference broke
        /// the ILGPU GitHub Pages build). The real cross-assembly TensorView&lt;Half&gt;
        /// round-trip lives in MLTestBase.TensorTests.TensorView_Half_RoundTrip.
        /// </summary>
        [TestMethod]
        public async Task ML_ArrayView1D_Float_TwoBuffer_Sanity() => await RunTest(async accelerator =>
        {
            const int Count = 32;
            var host = new float[Count];
            for (int i = 0; i < Count; i++) host[i] = i * 0.25f;
            using var inBuf = accelerator.Allocate1D(host);
            using var outBuf = accelerator.Allocate1D<float>(Count);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>>(
                (Index1D idx, ArrayView1D<float, Stride1D.Dense> inp, ArrayView1D<float, Stride1D.Dense> outp) =>
                {
                    outp[idx] = inp[idx] + 1.5f;
                });
            kernel((Index1D)Count, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<float>(0, Count);
            if (MathF.Abs(result[0] - (host[0] + 1.5f)) > 1e-4f)
                throw new Exception($"float ArrayView1D two-buffer: expected {host[0] + 1.5f} got {result[0]}");
        });

        [TestMethod]
        public async Task ViewStructHalf_DirectIndex_OneBuffer_Write() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            using var buf = accelerator.Allocate1D<global::ILGPU.Half>(4);
            var outView = new ViewStructHalf(buf.View, 4, 1, 1, 1, 1);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ViewStructHalf>(
                (Index1D idx, ViewStructHalf outp) => { outp.Data[idx] = (global::ILGPU.Half)1.5f; });
            kernel((Index1D)4, outView);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<global::ILGPU.Half>(0, 4);
            if (MathF.Abs((float)result[0] - 1.5f) > 1e-2f)
                throw new Exception($"ViewStructHalf direct index: expected 1.5 got {(float)result[0]}");
        });

        [TestMethod]
        public async Task ML_ArrayView1D_Float_OneBuffer_Write() => await RunTest(async accelerator =>
        {
            using var buf = accelerator.Allocate1D<float>(4);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>>(
                (Index1D idx, ArrayView1D<float, Stride1D.Dense> dst) => { dst[idx] = 1.5f; });
            kernel((Index1D)4, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<float>(0, 4);
            if (MathF.Abs(result[0] - 1.5f) > 1e-4f)
                throw new Exception($"float ArrayView1D one-buffer: expected 1.5 got {result[0]}");
        });

        [TestMethod]
        public async Task ML_ArrayView1D_Half_OneBuffer_Write() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            using var buf = accelerator.Allocate1D<global::ILGPU.Half>(4);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<global::ILGPU.Half, Stride1D.Dense>>(
                (Index1D idx, ArrayView1D<global::ILGPU.Half, Stride1D.Dense> dst) =>
                {
                    dst[idx] = (global::ILGPU.Half)1.5f;
                });
            kernel((Index1D)4, buf.View);
            await accelerator.SynchronizeAsync();
            var result = await buf.CopyToHostAsync<global::ILGPU.Half>(0, 4);
            if (MathF.Abs((float)result[0] - 1.5f) > 1e-2f)
                throw new Exception($"ArrayView1D half one-buffer write: expected 1.5 got {(float)result[0]}");
        });

        [TestMethod]
        public async Task ML_ArrayView1D_Half_TwoBuffer_Sanity() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            const int Count = 32;
            var hostHalf = new global::ILGPU.Half[Count];
            for (int i = 0; i < Count; i++)
                hostHalf[i] = (global::ILGPU.Half)(i * 0.25f);
            using var inBuf = accelerator.Allocate1D(hostHalf);
            using var outBuf = accelerator.Allocate1D<global::ILGPU.Half>(Count);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<global::ILGPU.Half, Stride1D.Dense>, ArrayView1D<global::ILGPU.Half, Stride1D.Dense>>(
                (Index1D idx, ArrayView1D<global::ILGPU.Half, Stride1D.Dense> inp, ArrayView1D<global::ILGPU.Half, Stride1D.Dense> outp) =>
                {
                    outp[idx] = inp[idx] + (global::ILGPU.Half)1.5f;
                });
            kernel((Index1D)Count, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var result = await outBuf.CopyToHostAsync<global::ILGPU.Half>(0, Count);
            float actual = (float)result[0];
            float expected = (float)hostHalf[0] + 1.5f;
            if (Math.Abs(actual - expected) > 1e-2f)
                throw new Exception($"ArrayView1D sanity: expected {expected} got {actual}");
        });

        // NOTE: the cross-assembly TensorView<Half> round-trip test that used the real
        // SpawnDev.ILGPU.ML.Tensors.TensorView<T> lives in the ML solution
        // (MLTestBase.TensorTests.TensorView_Half_RoundTrip) - ILGPU must not reference ML
        // (it broke the ILGPU GitHub Pages build). The same-assembly ViewStructHalf mirror
        // tests above cover the struct-param dispatch from the ILGPU side.

        #endregion
    }
}
