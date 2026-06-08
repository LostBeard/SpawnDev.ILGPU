using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // WebGL GPGPU scatter primitive (render-points-to-texture). WebGL2 transform feedback is
    // gather-only, so dst[destIndex[i]] = src[i] is done by rasterizing one GL_POINT per element at
    // the destination texel and writing the value in the fragment shader (glWorker.js handleScatter,
    // WebGLAccelerator.Scatter). This is the building block for a WebGL multi-pass RadixSort. The
    // result lives in the destination GPU texture (zero-copy); CopyToHostAsync refreshes lazily.
    // WebGL-only: on other backends the WebGLAccelerator cast is null -> the test self-skips.
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task WebGLScatter_ReversePermutation_Correct() => await RunTest(async accelerator =>
        {
            var webgl = accelerator as global::SpawnDev.ILGPU.WebGL.WebGLAccelerator;
            if (webgl == null)
                throw new UnsupportedTestException(
                    "Scatter is a WebGL-specific primitive (render-points-to-texture); other backends scatter natively.");

            const int n = 16;
            var src = new int[n];
            var dest = new int[n];
            for (int i = 0; i < n; i++)
            {
                src[i] = i * 10 + 1;     // distinct, non-zero values
                dest[i] = n - 1 - i;     // reverse permutation
            }

            using var srcBuf = accelerator.Allocate1D(src);
            using var destBuf = accelerator.Allocate1D(dest);
            using var dstBuf = accelerator.Allocate1D<int>(n);

            // dst[dest[i]] = src[i]
            webgl.Scatter(dstBuf.View, srcBuf.View, destBuf.View, n);

            var result = await dstBuf.CopyToHostAsync<int>();

            // dst[n-1-i] = i*10+1  =>  dst[j] = (n-1-j)*10+1
            for (int j = 0; j < n; j++)
            {
                int expected = (n - 1 - j) * 10 + 1;
                if (result[j] != expected)
                    throw new Exception(
                        $"WebGL scatter wrong at [{j}]: expected {expected}, got {result[j]}. " +
                        $"result=[{string.Join(",", result)}] (render-points-to-texture scatter bug).");
            }
        });

        // The R32UI scatter program (usampler / uvec4) is exercised by RadixSort<uint>; verify it
        // scatters HIGH-BIT uint values (>2^23, >2^31) without truncation — a uint-scatter truncation
        // would make a uint radix sort order by low bits only.
        [TestMethod]
        public async Task WebGLScatter_UintHighBits_Correct() => await RunTest(async accelerator =>
        {
            var webgl = accelerator as global::SpawnDev.ILGPU.WebGL.WebGLAccelerator;
            if (webgl == null)
                throw new UnsupportedTestException("Scatter is a WebGL-specific primitive.");

            uint[] src = { 256u, 0x100u, 0xFF00u, 0x01000000u, 0x80000000u, 0xFFFFFFFFu, 1u, 0u };
            int n = src.Length;
            var dest = new int[n];
            for (int i = 0; i < n; i++) dest[i] = n - 1 - i; // reverse

            using var srcBuf = accelerator.Allocate1D(src);
            using var destBuf = accelerator.Allocate1D(dest);
            using var dstBuf = accelerator.Allocate1D<uint>(n);

            webgl.Scatter(dstBuf.View, srcBuf.View, destBuf.View, n, "uint");
            var result = await dstBuf.CopyToHostAsync<uint>();

            for (int j = 0; j < n; j++)
            {
                uint expected = src[n - 1 - j];
                if (result[j] != expected)
                    throw new Exception(
                        $"WebGL uint scatter wrong at [{j}]: expected 0x{expected:X} got 0x{result[j]:X}. " +
                        $"(R32UI scatter truncation -> uint radix sorts by low bits.)");
            }
        });

        // Multi-texel scatter (componentsPerElement=2): a long/double is i64/f64-emulated as two texels
        // per element [lo,hi]. Scatter must move BOTH texels to dest*2 and dest*2+1. Building block for
        // 64-bit-key RadixSort (task #10). Verify a full 64-bit reverse permutation, incl. hi-word bits.
        [TestMethod]
        public async Task WebGLScatter_Int64MultiTexel_Correct() => await RunTest(async accelerator =>
        {
            var webgl = accelerator as global::SpawnDev.ILGPU.WebGL.WebGLAccelerator;
            if (webgl == null)
                throw new UnsupportedTestException("Scatter is a WebGL-specific primitive.");

            long[] src =
            {
                0x1122334455667788L, 256L, -1L, long.MinValue, long.MaxValue,
                1L, 0L, unchecked((long)0xABCDEF0123456789L),
            };
            int n = src.Length;
            var dest = new int[n];
            for (int i = 0; i < n; i++) dest[i] = n - 1 - i; // reverse

            using var srcBuf = accelerator.Allocate1D(src);
            using var destBuf = accelerator.Allocate1D(dest);
            using var dstBuf = accelerator.Allocate1D<long>(n);

            // long is stored as 2 uint texels (lo,hi); cpe=2, type "uint" (R32UI).
            webgl.Scatter(dstBuf.View, srcBuf.View, destBuf.View, n, "uint", 2);
            var result = await dstBuf.CopyToHostAsync<long>();

            for (int j = 0; j < n; j++)
            {
                long expected = src[n - 1 - j];
                if (result[j] != expected)
                    throw new Exception(
                        $"WebGL int64 multi-texel scatter wrong at [{j}]: expected 0x{expected:X16} " +
                        $"got 0x{result[j]:X16} (cpe=2 lo/hi texel mapping bug).");
            }
        });
    }
}
