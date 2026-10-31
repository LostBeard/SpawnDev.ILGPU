using ILGPU;
using ILGPU.Backends.EntryPoints;
using ILGPU.Backends.PTX;
using ILGPU.Runtime;
using ILGPU.Runtime.Cuda;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;
using System.Diagnostics;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // IntrinsicMath.Dot4I8Packed / Dot4U8Packed: the dot product of four packed 8-bit integers plus an accumulator,
    // wrapping on 32-bit overflow. One dp4a on CUDA sm_61+, dot4I8Packed / dot4U8Packed on WebGPU with the
    // packed_4x8_integer_dot_product WGSL language feature, the managed four-term body everywhere else; all must give
    // the same bits (Tuvok's 8-bit attention for the browser, ML plan item 4).
    public abstract partial class BackendTestBase
    {
        static void PackedDotI8Kernel(Index1D i, ArrayView<uint> a, ArrayView<uint> b, ArrayView<int> acc, ArrayView<int> o) =>
            o[i] = IntrinsicMath.Dot4I8Packed(a[i], b[i], acc[i]);

        static void PackedDotU8Kernel(Index1D i, ArrayView<uint> a, ArrayView<uint> b, ArrayView<uint> acc, ArrayView<uint> o) =>
            o[i] = IntrinsicMath.Dot4U8Packed(a[i], b[i], acc[i]);

        // Independent host oracle (sbyte / byte casts; the library body uses shifts only)
        static int HostDot4I8(uint a, uint b, int acc)
        {
            unchecked
            {
                int s = acc;
                for (int k = 0; k < 32; k += 8)
                    s += (sbyte)(a >> k) * (sbyte)(b >> k);
                return s;
            }
        }

        static uint HostDot4U8(uint a, uint b, uint acc)
        {
            unchecked
            {
                uint s = acc;
                for (int k = 0; k < 32; k += 8)
                    s += (byte)(a >> k) * (uint)(byte)(b >> k);
                return s;
            }
        }

        // Edge bytes (-128, 127, -1, 0, 255) and accumulators that overflow both ways, then random words
        static (uint[] a, uint[] b, int[] acc) PackedDotInputs(int n)
        {
            uint[] edgeWords = { 0u, 0x80808080u, 0x7F7F7F7Fu, 0xFFFFFFFFu, 0x01020304u, 0x807F01FFu, 0x00000080u, 0x80000000u };
            int[] edgeAcc = { 0, 1, -1, int.MaxValue, int.MinValue, int.MaxValue - 100, int.MinValue + 100 };
            var a = new uint[n];
            var b = new uint[n];
            var acc = new int[n];
            var rng = new Random(4242);
            int e = 0;
            for (int x = 0; x < edgeWords.Length; x++)
                for (int y = 0; y < edgeWords.Length; y++)
                    for (int z = 0; z < edgeAcc.Length && e < n; z++, e++)
                    {
                        a[e] = edgeWords[x];
                        b[e] = edgeWords[y];
                        acc[e] = edgeAcc[z];
                    }
            for (int i = e; i < n; i++)
            {
                a[i] = (uint)rng.Next() ^ ((uint)rng.Next() << 16);
                b[i] = (uint)rng.Next() ^ ((uint)rng.Next() << 16);
                acc[i] = rng.Next(4) == 0 ? (rng.Next(2) == 0 ? int.MaxValue - rng.Next(70000) : int.MinValue + rng.Next(70000)) : rng.Next() - (1 << 30);
            }
            return (a, b, acc);
        }

        async Task CheckPackedDot(Accelerator accelerator, string label)
        {
            const int n = 4096;
            var (a, b, acc) = PackedDotInputs(n);
            var uacc = new uint[n];
            for (int i = 0; i < n; i++) uacc[i] = unchecked((uint)acc[i]);
            using var aBuf = accelerator.Allocate1D(a);
            using var bBuf = accelerator.Allocate1D(b);
            using var accBuf = accelerator.Allocate1D(acc);
            using var uaccBuf = accelerator.Allocate1D(uacc);
            using var oBuf = accelerator.Allocate1D<int>(n);
            using var uoBuf = accelerator.Allocate1D<uint>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>, ArrayView<int>, ArrayView<int>>(PackedDotI8Kernel)(
                n, aBuf.View, bBuf.View, accBuf.View, oBuf.View);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>, ArrayView<uint>, ArrayView<uint>>(PackedDotU8Kernel)(
                n, aBuf.View, bBuf.View, uaccBuf.View, uoBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await oBuf.CopyToHostAsync<int>();
            var ugot = await uoBuf.CopyToHostAsync<uint>();
            for (int i = 0; i < n; i++)
            {
                int want = HostDot4I8(a[i], b[i], acc[i]);
                if (got[i] != want)
                    throw new Exception($"Dot4I8Packed on {BackendName} ({label}), element {i}: a=0x{a[i]:X8} b=0x{b[i]:X8} acc={acc[i]}: got {got[i]}, expected {want}");
                uint uwant = HostDot4U8(a[i], b[i], uacc[i]);
                if (ugot[i] != uwant)
                    throw new Exception($"Dot4U8Packed on {BackendName} ({label}), element {i}: a=0x{a[i]:X8} b=0x{b[i]:X8} acc={uacc[i]}: got {ugot[i]}, expected {uwant}");
            }
        }

        [TestMethod]
        public async Task PackedDot_Dot4I8AndU8_MatchHost() => await RunTest(async accelerator =>
        {
            await CheckPackedDot(accelerator, accelerator.Capabilities.PackedInt8DotProduct ? "native" : "four-term");
            Console.WriteLine($"[PackedDot] {BackendName}: native={accelerator.Capabilities.PackedInt8DotProduct}, 4096 x I8 + U8 incl. overflow edges ✓");
        });

        // The capability says what runs, and the generated code agrees: CUDA sm_61+ -> dp4a in the PTX; WebGPU -> dot4I8Packed
        // in the WGSL when the language feature is there, the expansion when it is not (also forced, same results).
        [TestMethod]
        public async Task PackedDot_CapabilityMatchesGeneratedCode() => await RunTest(async accelerator =>
        {
            bool native = accelerator.Capabilities.PackedInt8DotProduct;
            if (accelerator is CudaAccelerator cuda)
            {
                bool want = cuda.Architecture >= CudaArchitecture.SM_61;
                if (native != want)
                    throw new Exception($"CUDA {cuda.Architecture}: PackedInt8DotProduct={native}, expected {want}");
                var method = typeof(BackendTestBase).GetMethod(nameof(PackedDotI8Kernel), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
                var ptx = ((PTXCompiledKernel)accelerator.CompileKernel(EntryPointDescription.FromImplicitlyGroupedKernel(method))).PTXAssembly;
                if (ptx.Contains("dp4a.s32.s32") != native)
                    throw new Exception($"CUDA {cuda.Architecture}: PTX {(native ? "has no" : "has a")} dp4a.s32.s32 with PackedInt8DotProduct={native}");
            }
            else if (accelerator is WebGPUAccelerator wgpu)
            {
                if (native != wgpu.Backend.HasPackedInt8Dot)
                    throw new Exception($"WebGPU: PackedInt8DotProduct={native} but the backend says HasPackedInt8Dot={wgpu.Backend.HasPackedInt8Dot}");
                var wgsl = CompileWgsl(accelerator);
                if (wgsl.Contains("dot4I8Packed(") != native)
                    throw new Exception($"WebGPU: WGSL {(native ? "has no" : "has a")} dot4I8Packed with PackedInt8DotProduct={native}");
                if (native)
                {
                    // the expansion on the same device: no builtin, same results
                    WebGPUBackend.ForceEmulatedPackedInt8Dot = true;
                    try
                    {
                        accelerator.ClearCache(ClearCacheMode.Everything);
                        if (CompileWgsl(accelerator).Contains("dot4I8Packed("))
                            throw new Exception("WebGPU: ForceEmulatedPackedInt8Dot still emitted dot4I8Packed");
                        await CheckPackedDot(accelerator, "forced expansion");
                    }
                    finally
                    {
                        WebGPUBackend.ForceEmulatedPackedInt8Dot = false;
                        accelerator.ClearCache(ClearCacheMode.Everything);
                    }
                }
            }
            else if (native)
            {
                throw new Exception($"{BackendName}: PackedInt8DotProduct=true, but only CUDA sm_61+ and WebGPU have a native path");
            }
            Console.WriteLine($"[PackedDot] {BackendName}: PackedInt8DotProduct={native}, generated code agrees ✓");

            static string CompileWgsl(Accelerator accelerator)
            {
                string? wgsl = null;
                Action<string, string, WGSLEntry> handler = (name, source, info) =>
                {
                    if (name.Contains(nameof(PackedDotI8Kernel), StringComparison.Ordinal)) wgsl = source;
                };
                accelerator.ClearCache(ClearCacheMode.Everything);
                bool artifactCacheWas = SpawnDev.ILGPU.ShaderArtifactCache.Enabled;
                SpawnDev.ILGPU.ShaderArtifactCache.Enabled = false;
                WebGPUBackend.OnShaderCompiled += handler;
                try
                {
                    _ = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>, ArrayView<int>, ArrayView<int>>(PackedDotI8Kernel);
                }
                finally
                {
                    WebGPUBackend.OnShaderCompiled -= handler;
                    SpawnDev.ILGPU.ShaderArtifactCache.Enabled = artifactCacheWas;
                }
                return wgsl ?? throw new Exception($"Did not capture {nameof(PackedDotI8Kernel)}'s WGSL via OnShaderCompiled.");
            }
        });

        // 4 packed dots (16 int8 MACs) per iteration into 4 independent accumulators, so neither a dependent chain nor
        // the loop bookkeeping hides the multiply-add throughput
        static void PackedDotBenchKernel(Index1D i, ArrayView<uint> a, ArrayView<int> o, int iterations)
        {
            uint x = a[i];
            uint y0 = x * 2654435761u, y1 = y0 ^ 0x5A5A5A5Au, y2 = y0 + 0x01010101u, y3 = ~y0;
            int acc0 = 0, acc1 = 0, acc2 = 0, acc3 = 0;
            for (int k = 0; k < iterations; k++)
            {
                acc0 = IntrinsicMath.Dot4I8Packed(x, y0, acc0);
                acc1 = IntrinsicMath.Dot4I8Packed(x, y1, acc1);
                acc2 = IntrinsicMath.Dot4I8Packed(x, y2, acc2);
                acc3 = IntrinsicMath.Dot4I8Packed(x, y3, acc3);
                x += 0x01030507u;
            }
            o[i] = acc0 ^ acc1 ^ acc2 ^ acc3;
        }

        // The same 16 multiply-adds per iteration in f32 (the attention path the int8 one would replace)
        static void F32Dot4BenchKernel(Index1D i, ArrayView<uint> a, ArrayView<int> o, int iterations)
        {
            float x0 = a[i] & 0xFF, x1 = x0 + 1f, x2 = x0 + 2f, x3 = x0 + 3f;
            float acc0 = 0f, acc1 = 0f, acc2 = 0f, acc3 = 0f;
            for (int k = 0; k < iterations; k++)
            {
                acc0 += x0 * 0.5f + x1 * 0.25f + x2 * 0.125f + x3 * 0.0625f;
                acc1 += x0 * 0.75f + x1 * 0.375f + x2 * 0.1875f + x3 * 0.09375f;
                acc2 += x0 * 1.5f + x1 * 1.25f + x2 * 1.125f + x3 * 1.0625f;
                acc3 += x0 * 2.5f + x1 * 2.25f + x2 * 2.125f + x3 * 2.0625f;
                x0 += 1f; x1 += 1f; x2 += 1f; x3 += 1f;
            }
            o[i] = (int)(acc0 + acc1 + acc2 + acc3);
        }

        // WebGPU micro-benchmark (Tuvok's (c)): dot4I8Packed vs its expansion vs 4 f32 multiply-adds, same loop shape.
        // Reports numbers; fails only if the two int8 paths disagree.
        [TestMethod(Timeout = 600000)]
        public async Task PackedDot_WebGPU_NativeVsExpansionBenchmark() => await RunTest(async accelerator =>
        {
            if (accelerator is not WebGPUAccelerator wgpu)
                throw new UnsupportedTestException("WebGPU-only micro-benchmark (native packed dot vs its WGSL expansion).");
            const int n = 1 << 20, iterations = 1024, reps = 5;
            var a = new uint[n];
            var rng = new Random(7);
            for (int i = 0; i < n; i++) a[i] = (uint)rng.Next() ^ ((uint)rng.Next() << 16);
            using var aBuf = accelerator.Allocate1D(a);
            using var oBuf = accelerator.Allocate1D<int>(n);

            async Task<(double ms, int[] result)> Time(Action<Index1D, ArrayView<uint>, ArrayView<int>, int> kernelMethod)
            {
                var k = accelerator.LoadAutoGroupedStreamKernel(kernelMethod);
                k(n, aBuf.View, oBuf.View, iterations); // compile + warm up
                await accelerator.SynchronizeAsync();
                double best = double.MaxValue;
                for (int r = 0; r < reps; r++)
                {
                    var sw = Stopwatch.StartNew();
                    k(n, aBuf.View, oBuf.View, iterations);
                    await accelerator.SynchronizeAsync();
                    best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
                }
                return (best, await oBuf.CopyToHostAsync<int>());
            }

            bool hasNative = wgpu.Backend.HasPackedInt8Dot;
            var (f32Ms, _) = await Time(F32Dot4BenchKernel);
            var (nativeMs, nativeResult) = hasNative ? await Time(PackedDotBenchKernel) : (double.NaN, null!);
            WebGPUBackend.ForceEmulatedPackedInt8Dot = true;
            double expansionMs;
            int[] expansionResult;
            try
            {
                accelerator.ClearCache(ClearCacheMode.Everything);
                bool artifactCacheWas = SpawnDev.ILGPU.ShaderArtifactCache.Enabled;
                SpawnDev.ILGPU.ShaderArtifactCache.Enabled = false;
                try { (expansionMs, expansionResult) = await Time(PackedDotBenchKernel); }
                finally { SpawnDev.ILGPU.ShaderArtifactCache.Enabled = artifactCacheWas; }
            }
            finally
            {
                WebGPUBackend.ForceEmulatedPackedInt8Dot = false;
                accelerator.ClearCache(ClearCacheMode.Everything);
            }
            if (hasNative)
                for (int i = 0; i < n; i++)
                    if (nativeResult[i] != expansionResult[i])
                        throw new Exception($"dot4I8Packed and its expansion disagree at {i}: {nativeResult[i]} vs {expansionResult[i]}");
            double gmac(double ms) => n * (double)iterations * 16 / (ms * 1e6);
            Console.WriteLine($"[PackedDot] WebGPU {n} threads x {iterations} iterations x 16 MACs: " +
                $"dot4I8Packed {(hasNative ? $"{nativeMs:F2} ms ({gmac(nativeMs):F0} GMAC/s)" : "n/a (no language feature)")}, " +
                $"expansion {expansionMs:F2} ms ({gmac(expansionMs):F0} GMAC/s), f32 {f32Ms:F2} ms ({gmac(f32Ms):F0} GMAC/s)");
        });
    }
}
