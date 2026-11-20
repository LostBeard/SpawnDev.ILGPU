using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part: buffers bigger than one texture row. WebGL keeps a buffer in a 2D texture, a fixed-width tile layout (u_paramN_tileW).
    // The width was a constant 16384 in glWorker.js; on a device whose MAX_TEXTURE_SIZE is lower, every buffer over that many
    // texels got an illegal texture (INVALID_VALUE) and kernels could not read it: Chrome with --disable-gpu (SwiftShader,
    // MAX_TEXTURE_SIZE 8192), SpawnGen's SD-Turbo graph (Lore, 2026-11-20). The width is the device's limit now.
    // On a GPU this passes either way; run it on the software path with PMT_SOFTWARE_GL=1 PMT_FILTER=WebGLTests.WebGL_Buffer.
    public abstract partial class BackendTestBase
    {
        static void TextureTiling_Kernel(Index1D i, ArrayView<int> input, ArrayView<int> output)
        {
            output[i] = input[i] * 3 + 1;
        }

        [TestMethod(Timeout = 180000)]
        public async Task WebGL_BufferWiderThanATextureRow_EveryElementRoundTrips() => await RunTest(async accelerator =>
        {
            const int n = 3_000_000;            // 366 rows of 8192 texels, 184 of 16384
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = i ^ 0x5A5A5;
            using var a = accelerator.Allocate1D(input);
            using var b = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(TextureTiling_Kernel);
            kernel(n, a.View, b.View);
            await accelerator.SynchronizeAsync();
            var got = await b.CopyToHostAsync();
            for (int i = 0; i < n; i++)
                if (got[i] != input[i] * 3 + 1)
                    throw new Exception($"{accelerator.AcceleratorType}: element {i} is {got[i]}, expected {input[i] * 3 + 1}");
        });
    }
}
