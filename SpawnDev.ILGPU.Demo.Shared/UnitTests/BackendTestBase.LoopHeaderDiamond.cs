using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-22 (SpawnDev.Codecs Vp8CoefBlockDecoderGpu, found by Geordi): a `while (true)` whose body STARTS with an
    // if/else that rejoins - `if (pos >= len) { b = 0; } else { b = data[pos++]; }` followed by a tail both arms share -
    // made the WebGPU structurizer emit each arm of the loop header's branch as an independent subtree. The arm walked
    // first took the whole shared tail; the other arm ended the loop body without it, so the next iteration took the same
    // path with unchanged state: an infinite loop, which Chrome reports as DXGI_ERROR_DEVICE_HUNG. The same shape is in
    // Vp8 BoolDecoder.DecodeBool (`if (state.Count < 0) Fill(...)`), Opus range decoder normalisation and Vp9. Fix:
    // ComputeIfMergeNode / EmitIfElseArms - the header branch is emitted like any other if/else (arms up to the merge),
    // then the merge once.
    public abstract partial class BackendTestBase
    {
        // Mirrors a bitstream reader refill: bytes past `len` read as zero, the tail packs the byte and advances.
        private static void LoopHeaderDiamondKernel(Index1D i, ArrayView<int> data, ArrayView<int> lens, ArrayView<int> result)
        {
            int len = lens[i];
            int pos = 0;
            int count = 0;
            int value = 0;
            while (true)
            {
                int b;
                int next;
                if (pos >= len)
                {
                    b = 0;
                    next = pos;
                }
                else
                {
                    b = data[pos] & 0xFF;
                    next = pos + 1;
                }
                value |= b << count;
                count += 8;
                pos = next;
                if (count > 24)
                    break;
            }
            result[i * 3] = value;
            result[i * 3 + 1] = pos;
            result[i * 3 + 2] = count;
        }

        [TestMethod]
        public async Task LoopHeaderDiamond_SharedTailRunsOnBothArms_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // WebGL writes one output element per thread at its own slot; this kernel stores three.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: in-kernel multi-store per thread is not supported.");

            // Hostile lengths: 4 = every iteration reads a byte, 3 = three reads then one padding iteration,
            // 0 = padding only, 1 = one read then padding. Bytes have the high bit set so a lost shift or a lost OR
            // changes the value.
            var data = new int[] { 0x81, 0x7F, 0xC3, 0xFF };
            var lens = new int[] { 4, 3, 0, 1 };

            var expected = new int[lens.Length * 3];
            for (int t = 0; t < lens.Length; t++)
            {
                int pos = 0, count = 0, value = 0;
                while (true)
                {
                    int b = 0, next = pos;
                    if (pos < lens[t]) { b = data[pos] & 0xFF; next = pos + 1; }
                    value |= b << count;
                    count += 8;
                    pos = next;
                    if (count > 24) break;
                }
                expected[t * 3] = value;
                expected[t * 3 + 1] = pos;
                expected[t * 3 + 2] = count;
            }

            using var dataBuf = accelerator.Allocate1D(data);
            using var lensBuf = accelerator.Allocate1D(lens);
            using var resultBuf = accelerator.Allocate1D(new int[lens.Length * 3]);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                LoopHeaderDiamondKernel);
            kernel(lens.Length, dataBuf.View, lensBuf.View, resultBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await resultBuf.CopyToHostAsync<int>();

            for (int k = 0; k < expected.Length; k++)
                if (got[k] != expected[k])
                    throw new Exception($"LoopHeaderDiamond mismatch at result[{k}] (thread {k / 3}, field {k % 3}): expected 0x{expected[k]:X}, got 0x{got[k]:X} (all: {string.Join(",", got)}).");
        });
    }
}
