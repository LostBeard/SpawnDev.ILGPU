using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-23 (SpawnDev.Codecs, found by Geordi's Tint compile screen of all 373 Codecs kernels): `.Length` of a
    // SubView reached the WebGPU generator as GetViewLength(SubViewValue). The IR builder folded the length of a
    // NewView but not of a SubView, and the WGSL generator aliases a sub-view to its SOURCE view (`let v_N = v_src;`),
    // so the kernel path could not resolve it to a parameter and fell back to `arrayLength(&v_N)`: invalid WGSL
    // (Tint: "cannot take the address of 'let v_N'"; naga accepted it) and, had it compiled, the SOURCE buffer's
    // length. 9 Codecs kernels (Vp8BoolEncoderGpu, Vp8/Vp9 frame entropy/assemble/setup, Vp9 keyframe decode) pass
    // `outBuf.SubView(outBase, stride)` to helpers that bound their writes by `view.Length`. Fix: the IR builder folds
    // GetViewLength(SubViewValue) to the sub-view's own length operand, on every backend.
    public abstract partial class BackendTestBase
    {
        const int SubViewLengthStride = 37; // odd, so stream boundaries fall inside u32 words

        static int SubViewLengthOf(ArrayView<byte> view) => (int)view.Length;

        // One store per output buffer per thread, at the thread's own slot (runs on WebGL too).
        static void SubViewLengthKernel(Index1D i, ArrayView<byte> bytes, ArrayView<int> ints,
            ArrayView<int> lenByte, ArrayView<int> lenInt, ArrayView<int> lenNested)
        {
            int t = i;
            var byteSub = bytes.SubView((long)t * SubViewLengthStride, SubViewLengthStride - (t % 5));
            lenByte[t] = SubViewLengthOf(byteSub);

            var intSub = ints.SubView(t, ints.IntLength - t - (t % 3));
            lenInt[t] = intSub.IntLength;

            var nested = bytes.SubView((long)t * SubViewLengthStride, SubViewLengthStride)
                .SubView(1 + (t % 4), SubViewLengthStride - 8 - (t % 4));
            lenNested[t] = (int)nested.Length;
        }

        // The Codecs shape: each thread writes its own stream through a SubView, bounded by that view's Length.
        static void SubViewLengthBoundedWrite(ArrayView<byte> view, int stream)
        {
            for (int k = 0; k < SubViewLengthStride + 8; k++)
            {
                if (k < view.Length)
                    view[k] = (byte)(stream * 7 + k);
            }
        }

        static void SubViewLengthBoundedWriteKernel(Index1D i, ArrayView<byte> outBuf, int streamCount)
        {
            int t = i;
            if (t >= streamCount) return;
            SubViewLengthBoundedWrite(outBuf.SubView((long)t * SubViewLengthStride, SubViewLengthStride), t);
        }

        [TestMethod]
        public async Task SubViewLength_ReturnsSubViewLength() => await RunTest(async accelerator =>
        {
            const int n = 300;
            using var bytes = accelerator.Allocate1D<byte>(n * SubViewLengthStride);
            using var ints = accelerator.Allocate1D<int>(n + 5);
            using var lenByte = accelerator.Allocate1D<int>(n);
            using var lenInt = accelerator.Allocate1D<int>(n);
            using var lenNested = accelerator.Allocate1D<int>(n);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>,
                ArrayView<int>, ArrayView<int>, ArrayView<int>>(SubViewLengthKernel);
            kernel(n, bytes.View, ints.View, lenByte.View, lenInt.View, lenNested.View);
            await accelerator.SynchronizeAsync();

            var gotByte = await lenByte.CopyToHostAsync();
            var gotInt = await lenInt.CopyToHostAsync();
            var gotNested = await lenNested.CopyToHostAsync();
            for (int t = 0; t < n; t++)
            {
                int wantByte = SubViewLengthStride - t % 5;
                int wantInt = n + 5 - t - t % 3;
                int wantNested = SubViewLengthStride - 8 - t % 4;
                if (gotByte[t] != wantByte)
                    throw new Exception($"byte SubView.Length on {BackendName}, thread {t}: got {gotByte[t]}, expected {wantByte}");
                if (gotInt[t] != wantInt)
                    throw new Exception($"int SubView.IntLength on {BackendName}, thread {t}: got {gotInt[t]}, expected {wantInt}");
                if (gotNested[t] != wantNested)
                    throw new Exception($"SubView-of-SubView Length on {BackendName}, thread {t}: got {gotNested[t]}, expected {wantNested}");
            }
            Console.WriteLine($"[SubViewLength] byte, int and nested SubView lengths on {BackendName} ✓");
        });

        [TestMethod]
        public async Task SubViewLength_BoundsPerStreamWrites() => await RunTest(async accelerator =>
        {
            // WebGL writes one output element per thread at its own slot; each thread here writes a whole stream.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: in-kernel multi-store per thread is not supported.");

            const int streams = 64;
            const int guard = 16;
            var init = new byte[streams * SubViewLengthStride + guard];
            Array.Fill(init, (byte)0xEE);
            using var outBuf = accelerator.Allocate1D(init);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, int>(SubViewLengthBoundedWriteKernel);
            kernel(streams, outBuf.View, streams);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync();

            // A Length that reported the whole buffer would let every stream spill 8 bytes into the next one and the
            // last stream into the guard.
            for (int j = 0; j < got.Length; j++)
            {
                byte want = j < streams * SubViewLengthStride
                    ? (byte)(j / SubViewLengthStride * 7 + j % SubViewLengthStride)
                    : (byte)0xEE;
                if (got[j] != want)
                    throw new Exception($"SubView-bounded write on {BackendName}, byte {j} (stream {j / SubViewLengthStride}): got {got[j]}, expected {want}");
            }
            Console.WriteLine($"[SubViewLength] {streams} streams bounded by SubView.Length on {BackendName} ✓");
        });
    }
}
