using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A thread reads back what it wrote. WebGL writes outputs through transform feedback and reads every buffer from the
    // texture uploaded before the dispatch, so a load of an element the thread had already stored read the OLD contents:
    // SpawnDev.Codecs Vp9ForwardWht4x4Gpu.Transform (pass 1 writes the output, pass 2 reads it back) came out 0 where the
    // CPU gave 1600. The GLSL generator now forwards a thread's own stores to its later loads. The buffers start with
    // nonzero contents, so a stale read cannot pass by accident. Every backend, against a CPU oracle.
    public abstract partial class BackendTestBase
    {
        // The Vp9ForwardWht4x4Gpu shape: an in-place two-pass 4x4 transform, one thread per block, the output buffer
        // is the intermediate.
        static void StoreForward_TwoPassKernel(Index1D block, ArrayView<short> input, ArrayView<int> output)
        {
            long b = (long)(int)block * 16;
            for (int i = 0; i < 4; i++)
            {
                long a1 = input[b + i], b1 = input[b + i + 4], c1 = input[b + i + 8], d1 = input[b + i + 12];
                a1 += b1; d1 -= c1;
                long e1 = (a1 - d1) >> 1;
                b1 = e1 - b1; c1 = e1 - c1; a1 -= c1; d1 += b1;
                output[b + i] = (int)a1; output[b + i + 4] = (int)c1; output[b + i + 8] = (int)d1; output[b + i + 12] = (int)b1;
            }
            for (int i = 0; i < 4; i++)
            {
                long a1 = output[b + i * 4], b1 = output[b + i * 4 + 1], c1 = output[b + i * 4 + 2], d1 = output[b + i * 4 + 3];
                a1 += b1; d1 -= c1;
                long e1 = (a1 - d1) >> 1;
                b1 = e1 - b1; c1 = e1 - c1; a1 -= c1; d1 += b1;
                output[b + i * 4] = (int)(a1 * 4); output[b + i * 4 + 1] = (int)(c1 * 4);
                output[b + i * 4 + 2] = (int)(d1 * 4); output[b + i * 4 + 3] = (int)(b1 * 4);
            }
        }

        static void StoreForward_TwoPassOracle(short[] input, int[] output, int block)
        {
            int b = block * 16;
            for (int i = 0; i < 4; i++)
            {
                long a1 = input[b + i], b1 = input[b + i + 4], c1 = input[b + i + 8], d1 = input[b + i + 12];
                a1 += b1; d1 -= c1;
                long e1 = (a1 - d1) >> 1;
                b1 = e1 - b1; c1 = e1 - c1; a1 -= c1; d1 += b1;
                output[b + i] = (int)a1; output[b + i + 4] = (int)c1; output[b + i + 8] = (int)d1; output[b + i + 12] = (int)b1;
            }
            for (int i = 0; i < 4; i++)
            {
                long a1 = output[b + i * 4], b1 = output[b + i * 4 + 1], c1 = output[b + i * 4 + 2], d1 = output[b + i * 4 + 3];
                a1 += b1; d1 -= c1;
                long e1 = (a1 - d1) >> 1;
                b1 = e1 - b1; c1 = e1 - c1; a1 -= c1; d1 += b1;
                output[b + i * 4] = (int)(a1 * 4); output[b + i * 4 + 1] = (int)(c1 * 4);
                output[b + i * 4 + 2] = (int)(d1 * 4); output[b + i * 4 + 3] = (int)(b1 * 4);
            }
        }

        [TestMethod]
        public async Task StoreForward_InPlaceTwoPass_ReadsOwnWrites() => await RunEmulatedTest(async accelerator =>
        {
            const int blocks = 8;
            var input = new short[blocks * 16];
            var rng = new Random(17);
            for (int i = 0; i < input.Length; i++) input[i] = (short)rng.Next(-255, 256);
            var initial = new int[blocks * 16];
            for (int i = 0; i < initial.Length; i++) initial[i] = -1000 - i;
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D(initial);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>>(StoreForward_TwoPassKernel)(
                (Index1D)blocks, dIn.View, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            var want = (int[])initial.Clone();
            for (int b = 0; b < blocks; b++) StoreForward_TwoPassOracle(input, want, b);
            for (int i = 0; i < want.Length; i++)
                if (got[i] != want[i]) throw new Exception($"output[{i}] = {got[i]}, expected {want[i]}");
        });

        // `acc[i] += x` in a loop with a run-time trip count: one store site, read back on every iteration.
        static void StoreForward_LoopAccumulateKernel(Index1D idx, ArrayView<int> acc, ArrayView<int> values, int count)
        {
            acc[idx] = 3;
            for (int k = 0; k < count; k++)
                acc[idx] += values[idx * count + k];
        }

        [TestMethod]
        public async Task StoreForward_LoopAccumulate_ReadsOwnWrites() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 32, count = 5;
            var values = new int[n * count];
            for (int i = 0; i < values.Length; i++) values[i] = i * 7 - 50;
            var initial = new int[n];
            for (int i = 0; i < n; i++) initial[i] = 1000 + i;
            using var dAcc = accelerator.Allocate1D(initial);
            using var dValues = accelerator.Allocate1D(values);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(StoreForward_LoopAccumulateKernel)(
                (Index1D)n, dAcc.View, dValues.View, count);
            await accelerator.SynchronizeAsync();
            var got = await dAcc.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int want = 3;
                for (int k = 0; k < count; k++) want += values[i * count + k];
                if (got[i] != want) throw new Exception($"acc[{i}] = {got[i]}, expected {want}");
            }
        });

        // A conditional store read back (taken: the stored value; not taken: the buffer's own contents), and the
        // element types whose stored form differs from the register: a short wraps to 16 bits, a long is two words,
        // a float keeps its bits.
        static void StoreForward_TypesKernel(Index1D idx, ArrayView<int> cond, ArrayView<short> s, ArrayView<long> l,
            ArrayView<float> f, ArrayView<long> result)
        {
            if (cond[idx] > 0)
            {
                s[idx] = (short)(idx * 3000 + 1);
                l[idx] = (long)(int)idx * 5000000000L - 7;
                f[idx] = idx * 0.25f + 0.5f;
            }
            result[idx] = s[idx] + l[idx] + (long)(f[idx] * 4);
        }

        [TestMethod]
        public async Task StoreForward_ConditionalAndTypes_ReadsOwnWrites() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 48;
            var cond = new int[n];
            var s = new short[n];
            var l = new long[n];
            var f = new float[n];
            for (int i = 0; i < n; i++)
            {
                cond[i] = i % 3 == 1 ? 0 : 1;
                s[i] = (short)(-100 - i);
                l[i] = -(1L << 40) - i;
                f[i] = -2.5f - i;
            }
            using var dCond = accelerator.Allocate1D(cond);
            using var dS = accelerator.Allocate1D(s);
            using var dL = accelerator.Allocate1D(l);
            using var dF = accelerator.Allocate1D(f);
            using var dResult = accelerator.Allocate1D<long>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<short>, ArrayView<long>, ArrayView<float>, ArrayView<long>>(
                StoreForward_TypesKernel)((Index1D)n, dCond.View, dS.View, dL.View, dF.View, dResult.View);
            await accelerator.SynchronizeAsync();
            var got = await dResult.CopyToHostAsync<long>();
            for (int i = 0; i < n; i++)
            {
                short si = s[i];
                long li = l[i];
                float fi = f[i];
                if (cond[i] > 0)
                {
                    si = (short)(i * 3000 + 1);
                    li = (long)i * 5000000000L - 7;
                    fi = i * 0.25f + 0.5f;
                }
                long want = si + li + (long)(fi * 4);
                if (got[i] != want) throw new Exception($"result[{i}] (cond {cond[i]}) = {got[i]}, expected {want}");
            }
        });
    }
}
