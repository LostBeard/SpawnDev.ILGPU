using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // AsAligned16() on a 16-byte struct-of-4 view. On PTX this emits a 128-bit ld.v4.b32; on the
    // browser backends the view is a compile-time alignment hint (pass-through). This guards the WGSL
    // AsAligned lowering, which previously mis-declared the view result with its ELEMENT type
    // (`var v : struct_403;`) and then indexed it -> invalid WGSL that Dawn rejected (latent: no
    // production kernel used AsAligned16). The fix aliases the result to the source view (like SubView).
    public abstract partial class BackendTestBase
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct AsAlignedF4 { public float A, B, C, D; }

        static void AsAlignedStructSumKernel(Index1D i, ArrayView<AsAlignedF4> w, ArrayView<float> o)
        {
            var v = w.AsAligned16()[i];
            o[i] = v.A + v.B + v.C + v.D;
        }

        /// <summary>
        /// AsAligned16() struct-of-4 load must produce correct results on every backend (and valid,
        /// Dawn-accepted WGSL on WebGPU). Regression guard for the AsAligned WGSL lowering.
        /// </summary>
        [TestMethod]
        public async Task AsAligned16_StructOf4_SumsCorrectly() => await RunTest(async accelerator =>
        {
            // NOTE: runs on ALL 6 backends now, incl. WebGL. WebGL has no native struct storage, so an
            // AsAligned16'd struct-of-4 element loads via per-field texelFetch + struct assembly (element
            // i -> texels 4i+0..3); the AsAligned node is traced through to the param by the GLSL
            // resolvers (was falling to the generic-LEA fallback -> invalid GLSL; fixed 2026-07-02).
            const int n = 512;
            var data = new AsAlignedF4[n];
            var expected = new float[n];
            for (int i = 0; i < n; i++)
            {
                data[i] = new AsAlignedF4 { A = i * 0.5f, B = i + 1.0f, C = -i * 0.25f, D = 3.0f };
                expected[i] = data[i].A + data[i].B + data[i].C + data[i].D;
            }

            using var w = accelerator.Allocate1D(data);
            using var o = accelerator.Allocate1D<float>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<AsAlignedF4>, ArrayView<float>>(AsAlignedStructSumKernel);
            kernel((Index1D)n, w.View, o.View);
            await accelerator.SynchronizeAsync();

            var got = await o.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (MathF.Abs(got[i] - expected[i]) > 1e-3f)
                    throw new Exception($"AsAligned16 struct sum mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });
    }
}
