using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WebGPU scalar packing (2026-10-01): WebGPUAccelerator.PackScalarsInto replaced the per-dispatch packer, which
    // allocated a byte[] per value and string-tested every scalar's type name. The old code is kept verbatim as
    // PackScalarsLegacy, and WebGPUBackend.VerifyScalarPacking packs every dispatch BOTH ways and throws on any byte
    // difference. This test runs a kernel with every common packed scalar kind - int, uint, float, long, ulong, byte -
    // and a SUB-VIEW (its element offset is packed too), with the verify switch ON, and checks the values the
    // kernel actually received. On the other backends it is a plain scalar round trip.
    public abstract partial class BackendTestBase
    {
        private static void BoolScalarKernel(Index1D i, ArrayView<int> output, bool flag, int whenTrue)
        {
            output[i] = flag ? whenTrue : -whenTrue;
        }

        /// <summary>
        /// A <c>bool</c> kernel SCALAR parameter, true and false, must reach the kernel as the value passed. FOUND 2026-10-01
        /// (ScalarPacking_AllKinds_VerifiedAgainstLegacy with a bool): WebGPU emitted <c>bitcast&lt;bool&gt;(u32)</c>, which
        /// fails WGSL validation (fixed in WGSLCodeGenerator.BitcastFromU32); Wasm received false for true (silently
        /// wrong); WebGL ("'bool' : illegal type for precision qualifier") and OpenCL (CL_BUILD_PROGRAM_FAILURE) failed to
        /// compile; CPU and CUDA rejected it ("Type 'System.Boolean' is not blittable").
        /// </summary>
        [TestMethod]
        public async Task BoolScalarParam_TrueAndFalse_Arrive() => await RunTest(async accelerator =>
        {
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, bool, int>(BoolScalarKernel);
            using var buf = accelerator.Allocate1D<int>(4);
            foreach (bool flag in new[] { true, false, true })
            {
                kernel((Index1D)4, buf.View, flag, 7);
                await accelerator.SynchronizeAsync();
                var got = await buf.CopyToHostAsync<int>(0, 4);
                for (int k = 0; k < 4; k++)
                    if (got[k] != (flag ? 7 : -7))
                        throw new Exception($"bool scalar {flag}: kernel saw {(got[k] == 7 ? "true" : got[k] == -7 ? "false" : got[k].ToString())}");
            }
        });

        private static void AllScalarKindsKernel(Index1D i, ArrayView<int> outI, ArrayView<float> outF,
            int a, uint b, float c, long d, ulong e, byte f)
        {
            if (i != 0) return;
            outI[0] = a;
            outI[1] = (int)b;
            outI[2] = (int)(d & 0xFFFFFFFFL);
            outI[3] = (int)(d >> 32);
            outI[4] = (int)(e & 0xFFFFFFFFUL);
            outI[5] = (int)(e >> 32);
            outI[6] = f;
            outI[7] = 0x5A5A;   // sentinel: the window's last slot is written by THIS kernel
            outF[0] = c;
        }

        [TestMethod]
        public async Task ScalarPacking_AllKinds_VerifiedAgainstLegacy() => await RunTest(async accelerator =>
        {
            bool prev = WebGPUBackend.VerifyScalarPacking;
            WebGPUBackend.VerifyScalarPacking = true;
            try
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>,
                    int, uint, float, long, ulong, byte>(AllScalarKindsKernel);
                using var iBuf = accelerator.Allocate1D<int>(64 + 8);
                using var fBuf = accelerator.Allocate1D<float>(4);
                var cases = new (int a, uint b, float c, long d, ulong e, byte f)[]
                {
                    (-7, 0xFEDCBA98u, -1.5f, -0x123456789ABCL, 0xF0E1D2C3B4A59687UL, 0xA5),
                    (int.MaxValue, 1u, float.Epsilon, long.MinValue, ulong.MaxValue, 0),
                };
                foreach (var cs in cases)
                {
                    // A sub-view at element 64 (256 bytes in, so its offset word is non-zero on WebGPU).
                    var outI = iBuf.View.SubView(64, 8);
                    kernel((Index1D)1, outI, fBuf.View, cs.a, cs.b, cs.c, cs.d, cs.e, cs.f);
                    await accelerator.SynchronizeAsync();
                    var gotI = await iBuf.CopyToHostAsync<int>(64, 8);
                    var gotF = await fBuf.CopyToHostAsync<float>(0, 1);
                    var want = new[]
                    {
                        cs.a, (int)cs.b, (int)(cs.d & 0xFFFFFFFFL), (int)(cs.d >> 32),
                        (int)(cs.e & 0xFFFFFFFFUL), (int)(cs.e >> 32), cs.f, 0x5A5A,
                    };
                    for (int k = 0; k < want.Length; k++)
                        if (gotI[k] != want[k])
                            throw new Exception($"scalar {k}: kernel received {gotI[k]} (0x{gotI[k]:X8}), expected {want[k]} (0x{want[k]:X8})");
                    if (BitConverter.SingleToInt32Bits(gotF[0]) != BitConverter.SingleToInt32Bits(cs.c))
                        throw new Exception($"float scalar: kernel received {gotF[0]}, expected {cs.c}");
                }
            }
            finally
            {
                WebGPUBackend.VerifyScalarPacking = prev;
            }
        });
    }
}
