using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Backends;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.OpenCL;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using ILGPU.Runtime.OpenCL;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Floating-point contraction. .NET rounds every float add / subtract / multiply on its own (RyuJIT never fuses
    // a * b + c), and so do the CPU accelerator, Velocity and Wasm. CUDA and OpenCL let the device compiler contract
    // a multiply and an add into one fused multiply-add, which rounds once: PTX add/sub/mul without a rounding
    // modifier, and OpenCL C's default FP_CONTRACT ON. Found by Geordi (SpawnDev.Codecs FLAC LPC, 2026-11-08: 2 of 26
    // files differed from FFmpeg on CUDA and OpenCL, the same bytes on both). Contraction is now opt-in
    // (Context.Builder.EnableFloatingPointContraction); an explicit Math(F).FusedMultiplyAdd is fused.
    //
    // Inputs: c = -(a * b rounded), so a * b + c is exactly 0 when each operation rounds and the product's rounding
    // error (almost always non-zero) when fused. Every element tells the two apart.
    public abstract partial class BackendTestBase
    {
        const int ContractionCount = 1024;
        const int ContractionDotLength = 16;

        static void PlainMulAddF32Kernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c, ArrayView<float> o) =>
            o[i] = a[i] * b[i] + c[i];

        static void PlainMulAddF64Kernel(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> o) =>
            o[i] = a[i] * b[i] + c[i];

        // The ML inner-loop shape: sum += a * b. One output per thread (WebGL captures one).
        static void PlainDotF32Kernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o)
        {
            float sum = 0f;
            for (int k = 0; k < ContractionDotLength; k++)
                sum += a[i * ContractionDotLength + k] * b[i * ContractionDotLength + k];
            o[i] = sum;
        }

        static void ExplicitFmaF32Kernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c, ArrayView<float> o) =>
            o[i] = MathF.FusedMultiplyAdd(a[i], b[i], c[i]);

        static void ExplicitFmaF64Kernel(Index1D i, ArrayView<double> a, ArrayView<double> b, ArrayView<double> c, ArrayView<double> o) =>
            o[i] = Math.FusedMultiplyAdd(a[i], b[i], c[i]);

        static void MultiplyAddEstimateF32Kernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c, ArrayView<float> o) =>
            o[i] = float.MultiplyAddEstimate(a[i], b[i], c[i]);

        static (float[] a, float[] b, float[] c) ContractionInputsF32(int seed)
        {
            var rng = new Random(seed);
            var a = new float[ContractionCount];
            var b = new float[ContractionCount];
            var c = new float[ContractionCount];
            // [0]: (1 + 2^-12)^2 = 1 + 2^-11 + 2^-24; rounded per operation the result is 0, fused it is 2^-24.
            a[0] = b[0] = 1f + MathF.Pow(2, -12);
            c[0] = -(1f + MathF.Pow(2, -11));
            for (int i = 1; i < ContractionCount; i++)
            {
                a[i] = (float)(rng.NextDouble() * 4 - 2);
                b[i] = (float)(rng.NextDouble() * 4 - 2);
                c[i] = -(a[i] * b[i]);
            }
            return (a, b, c);
        }

        static (double[] a, double[] b, double[] c) ContractionInputsF64(int seed)
        {
            var rng = new Random(seed);
            var a = new double[ContractionCount];
            var b = new double[ContractionCount];
            var c = new double[ContractionCount];
            a[0] = b[0] = 1d + Math.Pow(2, -27);
            c[0] = -(1d + Math.Pow(2, -26));
            for (int i = 1; i < ContractionCount; i++)
            {
                a[i] = rng.NextDouble() * 4 - 2;
                b[i] = rng.NextDouble() * 4 - 2;
                c[i] = -(a[i] * b[i]);
            }
            return (a, b, c);
        }

        // Emulated f64 (WebGPU, WebGL) has a ~48-bit significand, so f64 bit-exactness is a native-f64 property.
        static bool HasNativeF64(Accelerator accelerator) => accelerator is not WebGPUAccelerator and not WebGLAccelerator;

        static void RequireBitExact(float[] got, Func<int, float> expected, string label)
        {
            int bad = 0; string first = "";
            for (int i = 0; i < got.Length; i++)
            {
                float e = expected(i);
                if (BitConverter.SingleToInt32Bits(got[i]) == BitConverter.SingleToInt32Bits(e)) continue;
                if (bad++ < 3) first += $" [{i}] expected {e:R}, got {got[i]:R};";
            }
            if (bad > 0) throw new Exception($"{label}: {bad} of {got.Length} differ:{first}");
        }

        static void RequireBitExact(double[] got, Func<int, double> expected, string label)
        {
            int bad = 0; string first = "";
            for (int i = 0; i < got.Length; i++)
            {
                double e = expected(i);
                if (BitConverter.DoubleToInt64Bits(got[i]) == BitConverter.DoubleToInt64Bits(e)) continue;
                if (bad++ < 3) first += $" [{i}] expected {e:R}, got {got[i]:R};";
            }
            if (bad > 0) throw new Exception($"{label}: {bad} of {got.Length} differ:{first}");
        }

        /// <summary>
        /// a * b + c written as two operations rounds twice on every backend, as in .NET: no implicit contraction
        /// into a fused multiply-add (f32 element-wise, the sum += a * b loop, and f64 on native-f64 backends).
        /// </summary>
        [TestMethod]
        public async Task FloatContraction_PlainMulAdd_RoundsEachOperationLikeDotNet() => await RunTest(async accelerator =>
        {
            var (a, b, c) = ContractionInputsF32(31);
            using (var ab = accelerator.Allocate1D(a))
            using (var bb = accelerator.Allocate1D(b))
            using (var cb = accelerator.Allocate1D(c))
            using (var ob = accelerator.Allocate1D<float>(ContractionCount))
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(PlainMulAddF32Kernel)(
                    (Index1D)ContractionCount, ab.View, bb.View, cb.View, ob.View);
                RequireBitExact(await ob.CopyToHostAsync<float>(), i => a[i] * b[i] + c[i], "f32 a * b + c");
            }

            // sum += a * b over 16 terms; inputs chosen so each partial product's rounding error matters.
            var rng = new Random(33);
            var da = new float[ContractionCount * ContractionDotLength];
            var db = new float[da.Length];
            for (int i = 0; i < da.Length; i++) { da[i] = (float)(rng.NextDouble() * 2 - 1); db[i] = (float)(rng.NextDouble() * 2 - 1); }
            using (var ab = accelerator.Allocate1D(da))
            using (var bb = accelerator.Allocate1D(db))
            using (var ob = accelerator.Allocate1D<float>(ContractionCount))
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(PlainDotF32Kernel)(
                    (Index1D)ContractionCount, ab.View, bb.View, ob.View);
                RequireBitExact(await ob.CopyToHostAsync<float>(), i =>
                {
                    float sum = 0f;
                    for (int k = 0; k < ContractionDotLength; k++)
                        sum += da[i * ContractionDotLength + k] * db[i * ContractionDotLength + k];
                    return sum;
                }, "f32 sum += a * b");
            }

            if (!HasNativeF64(accelerator)) return;
            var (a64, b64, c64) = ContractionInputsF64(32);
            using (var ab = accelerator.Allocate1D(a64))
            using (var bb = accelerator.Allocate1D(b64))
            using (var cb = accelerator.Allocate1D(c64))
            using (var ob = accelerator.Allocate1D<double>(ContractionCount))
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(PlainMulAddF64Kernel)(
                    (Index1D)ContractionCount, ab.View, bb.View, cb.View, ob.View);
                RequireBitExact(await ob.CopyToHostAsync<double>(), i => a64[i] * b64[i] + c64[i], "f64 a * b + c");
            }
        });

        /// <summary>
        /// MathF.FusedMultiplyAdd / Math.FusedMultiplyAdd round once, as .NET's do, on every backend
        /// (f64 on native-f64 backends).
        /// </summary>
        [TestMethod]
        public async Task FloatContraction_ExplicitFusedMultiplyAdd_RoundsOnceLikeDotNet() => await RunTest(async accelerator =>
        {
            var (a, b, c) = ContractionInputsF32(41);
            using (var ab = accelerator.Allocate1D(a))
            using (var bb = accelerator.Allocate1D(b))
            using (var cb = accelerator.Allocate1D(c))
            using (var ob = accelerator.Allocate1D<float>(ContractionCount))
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(ExplicitFmaF32Kernel)(
                    (Index1D)ContractionCount, ab.View, bb.View, cb.View, ob.View);
                RequireBitExact(await ob.CopyToHostAsync<float>(), i => MathF.FusedMultiplyAdd(a[i], b[i], c[i]), "MathF.FusedMultiplyAdd");
            }

            if (!HasNativeF64(accelerator)) return;
            var (a64, b64, c64) = ContractionInputsF64(42);
            using (var ab = accelerator.Allocate1D(a64))
            using (var bb = accelerator.Allocate1D(b64))
            using (var cb = accelerator.Allocate1D(c64))
            using (var ob = accelerator.Allocate1D<double>(ContractionCount))
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>, ArrayView<double>, ArrayView<double>, ArrayView<double>>(ExplicitFmaF64Kernel)(
                    (Index1D)ContractionCount, ab.View, bb.View, cb.View, ob.View);
                RequireBitExact(await ob.CopyToHostAsync<double>(), i => Math.FusedMultiplyAdd(a64[i], b64[i], c64[i]), "Math.FusedMultiplyAdd");
            }
        });

        /// <summary>
        /// float.MultiplyAddEstimate: each result is the fused or the unfused value (both are allowed), and on CUDA and
        /// OpenCL - which have a hardware fma - it is the fused one, emitted as one fma instruction.
        /// </summary>
        [TestMethod]
        public async Task FloatContraction_MultiplyAddEstimate_IsHardwareFmaOrMultiplyAdd() => await RunTest(async accelerator =>
        {
            var (a, b, c) = ContractionInputsF32(51);
            float[] got;
            using (var ab = accelerator.Allocate1D(a))
            using (var bb = accelerator.Allocate1D(b))
            using (var cb = accelerator.Allocate1D(c))
            using (var ob = accelerator.Allocate1D<float>(ContractionCount))
            {
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>, ArrayView<float>>(MultiplyAddEstimateF32Kernel)(
                    (Index1D)ContractionCount, ab.View, bb.View, cb.View, ob.View);
                got = await ob.CopyToHostAsync<float>();
            }
            bool mustFuse = accelerator is CudaAccelerator or CLAccelerator;
            int bad = 0; string first = "";
            for (int i = 0; i < ContractionCount; i++)
            {
                int g = BitConverter.SingleToInt32Bits(got[i]);
                bool fused = g == BitConverter.SingleToInt32Bits(MathF.FusedMultiplyAdd(a[i], b[i], c[i]));
                bool unfused = g == BitConverter.SingleToInt32Bits(a[i] * b[i] + c[i]);
                if (mustFuse ? fused : fused || unfused) continue;
                if (bad++ < 3) first += $" [{i}] got {got[i]:R}, fused {MathF.FusedMultiplyAdd(a[i], b[i], c[i]):R}, unfused {a[i] * b[i] + c[i]:R};";
            }
            if (bad > 0) throw new Exception($"MultiplyAddEstimate ({(mustFuse ? "must be fused" : "fused or unfused")}): {bad} of {ContractionCount} wrong:{first}");

            var method = typeof(BackendTestBase).GetMethod(nameof(MultiplyAddEstimateF32Kernel), BindingFlags.Static | BindingFlags.NonPublic)!;
            var entry = EntryPointDescription.FromImplicitlyGroupedKernel(method);
            if (accelerator is CudaAccelerator cuda)
            {
                string ptx = ((PTXCompiledKernel)cuda.Backend.Compile(entry, default)).PTXAssembly;
                if (!ptx.Contains("fma.rn.f32") || ptx.Contains("mul.rn.f32"))
                    throw new Exception("MultiplyAddEstimate PTX is not one fma.rn.f32:\n" + PtxArithmeticLines(ptx));
            }
            else if (accelerator is CLAccelerator cl)
            {
                string src = ((CLCompiledKernel)cl.Backend.Compile(entry, default)).Source;
                if (!System.Text.RegularExpressions.Regex.IsMatch(src, @"\bfma\s*\("))
                    throw new Exception("MultiplyAddEstimate OpenCL C has no fma call:\n" + src);
            }
        });

        /// <summary>
        /// The generated device code: by default PTX float add/mul carry .rn and OpenCL C sets FP_CONTRACT OFF;
        /// with EnableFloatingPointContraction() PTX has no .rn on them and OpenCL C sets FP_CONTRACT ON.
        /// </summary>
        [TestMethod]
        public async Task FloatContraction_OptIn_ChangesGeneratedCode() => await RunTest(async accelerator =>
        {
            var method = typeof(BackendTestBase).GetMethod(nameof(PlainMulAddF32Kernel), BindingFlags.Static | BindingFlags.NonPublic)!;
            var entry = EntryPointDescription.FromImplicitlyGroupedKernel(method);
            if (accelerator is CudaAccelerator cuda)
            {
                string strict = ((PTXCompiledKernel)cuda.Backend.Compile(entry, default)).PTXAssembly;
                if (!strict.Contains("mul.rn.f32") || !strict.Contains("add.rn.f32") || strict.Contains("mul.f32 ") || strict.Contains("add.f32 "))
                    throw new Exception("default PTX: expected mul.rn.f32 + add.rn.f32 and no bare mul.f32 / add.f32:\n" + PtxArithmeticLines(strict));
                using var ctx = Context.Create(b => b.Cuda().EnableFloatingPointContraction());
                using var acc = ctx.Devices.OfType<CudaDevice>().First(d => d.Name == cuda.Name).CreateCudaAccelerator(ctx);
                string relaxed = ((PTXCompiledKernel)acc.Backend.Compile(entry, default)).PTXAssembly;
                if (relaxed.Contains("mul.rn.f32") || relaxed.Contains("add.rn.f32") || !relaxed.Contains("mul.f32") || !relaxed.Contains("add.f32"))
                    throw new Exception("opt-in PTX: expected bare mul.f32 + add.f32:\n" + PtxArithmeticLines(relaxed));
            }
            else if (accelerator is CLAccelerator cl)
            {
                string strict = ((CLCompiledKernel)cl.Backend.Compile(entry, default)).Source;
                if (!strict.Contains("#pragma OPENCL FP_CONTRACT OFF"))
                    throw new Exception("default OpenCL C has no '#pragma OPENCL FP_CONTRACT OFF'");
                using var ctx = Context.Create(b => b.OpenCL().EnableFloatingPointContraction());
                using var acc = ctx.Devices.OfType<CLDevice>().First(d => d.Name == cl.Name).CreateCLAccelerator(ctx);
                string relaxed = ((CLCompiledKernel)acc.Backend.Compile(entry, default)).Source;
                if (!relaxed.Contains("#pragma OPENCL FP_CONTRACT ON") || relaxed.Contains("FP_CONTRACT OFF"))
                    throw new Exception("opt-in OpenCL C does not set '#pragma OPENCL FP_CONTRACT ON'");
            }
            else
            {
                throw new UnsupportedTestException($"{accelerator.AcceleratorType}: no device compiler that contracts (the flag affects CUDA and OpenCL)");
            }
            await Task.CompletedTask;
        });

        static string PtxArithmeticLines(string ptx) =>
            string.Join("\n", ptx.Split('\n').Where(l => l.Contains(".f32") && (l.Contains("add") || l.Contains("mul") || l.Contains("fma"))).Take(20));
    }
}
