using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // SpawnDev.ILGPU.ML's PadKernel (2026-09-30): a rank loop whose body breaks from inside an if / else-if / else chain
    // HUNG the GPU on WebGPU (DXGI_ERROR_DEVICE_HUNG, whole lane lost) - style-mosaic's first node, [1,3,224,224]
    // reflect-padded to [1,3,232,232]. The kernel is copied verbatim from ML so this repo owns the regression.
    public abstract partial class BackendTestBase
    {
        private static void LoopBreakInElseChainPadKernel(Index1D idx, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<int, Stride1D.Dense> p, float constantValue)
        {
            int rank = p[0];
            int mode = p[1]; // 0=constant, 1=edge, 2=reflect
            int remaining = idx;
            int srcIdx = 0;
            bool outOfBounds = false;
            for (int d = 0; d < rank; d++)
            {
                int outStride = p[2 + 4 * rank + d];
                int coord = remaining / outStride;
                remaining = remaining % outStride;
                int inDim = p[2 + d];
                int padBefore = p[2 + rank + d];
                int inStride = p[2 + 3 * rank + d];
                int srcCoord = coord - padBefore;
                if (srcCoord < 0 || srcCoord >= inDim)
                {
                    if (mode == 0) { outOfBounds = true; break; }
                    else if (mode == 1) srcCoord = srcCoord < 0 ? 0 : inDim - 1;
                    else
                    {
                        if (srcCoord < 0) srcCoord = -srcCoord;
                        if (srcCoord >= inDim) srcCoord = 2 * (inDim - 1) - srcCoord;
                    }
                }
                srcIdx += srcCoord * inStride;
            }
            output[idx] = outOfBounds ? constantValue : input[srcIdx];
        }

        [TestMethod(Timeout = 60000)]
        public async Task LoopBreakInElseChain_Pad_AllModes() => await RunTest(async accelerator =>
        {
            int[] shape = { 1, 3, 224, 224 }, pads = { 0, 0, 4, 4, 0, 0, 4, 4 };
            const int rank = 4, n = 3 * 224 * 224, h2 = 232, no = 3 * h2 * h2;
            var outShape = new int[rank];
            for (int i = 0; i < rank; i++) outShape[i] = shape[i] + pads[i] + pads[i + rank];
            var inStrides = new int[rank]; var outStrides = new int[rank];
            inStrides[rank - 1] = outStrides[rank - 1] = 1;
            for (int i = rank - 2; i >= 0; i--) { inStrides[i] = inStrides[i + 1] * shape[i + 1]; outStrides[i] = outStrides[i + 1] * outShape[i + 1]; }
            var x = new float[n];
            for (int i = 0; i < n; i++) x[i] = (i * 37 % 1009) * 0.5f;
            using var inBuf = accelerator.Allocate1D(x);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<int, Stride1D.Dense>, float>(LoopBreakInElseChainPadKernel);
            foreach (int mode in new[] { 2, 1, 0 })
            {
                var pr = new int[2 + 5 * rank];
                pr[0] = rank; pr[1] = mode;
                for (int i = 0; i < rank; i++) { pr[2 + i] = shape[i]; pr[2 + 3 * rank + i] = inStrides[i]; pr[2 + 4 * rank + i] = outStrides[i]; }
                for (int i = 0; i < 2 * rank; i++) pr[2 + rank + i] = pads[i];
                using var pBuf = accelerator.Allocate1D(pr);
                using var outBuf = accelerator.Allocate1D<float>(no);
                kernel(no, inBuf.View, outBuf.View, pBuf.View, -7f);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync<float>();
                int bad = 0; string first = "";
                for (int c = 0; c < 3; c++)
                    for (int h = 0; h < h2; h++)
                        for (int w = 0; w < h2; w++)
                        {
                            int sh = h - 4, sw = w - 4;
                            float want;
                            if (mode == 0 && (sh < 0 || sh >= 224 || sw < 0 || sw >= 224)) want = -7f;
                            else want = x[(c * 224 + PadSrc(sh, 224, mode)) * 224 + PadSrc(sw, 224, mode)];
                            float g = got[(c * h2 + h) * h2 + w];
                            if (g != want && bad++ == 0) first = $"[{c},{h},{w}] = {g}, expected {want}";
                        }
                if (bad != 0) throw new Exception($"mode {mode}: {bad} of {no} wrong; first {first}");
            }
        });

        private static int PadSrc(int s, int dim, int mode)
        {
            if (s >= 0 && s < dim) return s;
            if (mode == 1) return s < 0 ? 0 : dim - 1;
            if (s < 0) s = -s;
            if (s >= dim) s = 2 * (dim - 1) - s;
            return s;
        }
    }
}
