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
    }
}
