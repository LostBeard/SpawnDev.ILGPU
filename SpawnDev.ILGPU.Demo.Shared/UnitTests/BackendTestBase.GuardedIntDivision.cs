using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-26 (SpawnDev.Codecs ShuffleFilterGpu, found by Geordi): an integer division behind a guard ran
    // unconditionally and Wasm trapped "divide by zero" (i32.div_s / rem_s trap on a zero divisor and on MinValue / -1;
    // GPU backends return a value the program then discards, so only Wasm failed). Two passes moved it:
    // - IfConversion turned the small guarded block into selects (ShuffleKernel: `j = o / elements`, elements == 0).
    // - LoopInvariantCodeMotion moved a loop-invariant `num / den` to the loop entry, which also runs when the loop runs
    //   zero times or skips the division's block.
    // Fix: BinaryArithmeticValue.CanTrap; neither pass moves such a value onto a path the source did not take.
    public abstract partial class BackendTestBase
    {
        // SpawnDev.Codecs ShuffleFilterGpu.ShuffleKernel (c-blosc byte shuffle), unchanged.
        private static void GuardedDivShuffleKernel(Index1D idx, ArrayView<byte> src, ArrayView<byte> dst, int offset, int length, int elementSize)
        {
            int o = offset + idx.X;
            if (o >= length) return;
            int elements = length / elementSize;
            int s = o;
            if (o < elements * elementSize)
            {
                int j = o / elements;
                int i = o - j * elements;
                s = i * elementSize + j;
            }
            dst[o] = src[s];
        }

        // Guards that skip a zero divisor and a signed MinValue / -1 (both trap in Wasm), no loop.
        private static void GuardedDivPairKernel(Index1D i, ArrayView<int> nums, ArrayView<int> dens, ArrayView<int> outQ, ArrayView<int> outR)
        {
            int n = nums[i];
            int d = dens[i];
            int q = n;
            int r = 0;
            if (d != 0 && d != -1)
            {
                q = n / d;
                r = n % d;
            }
            outQ[i] = q;
            outR[i] = r ^ q;
        }

        // Loop-invariant division in a loop that runs zero times exactly when the divisor is zero.
        private static void GuardedDivZeroTripLoopKernel(Index1D i, ArrayView<int> counts, ArrayView<int> nums, ArrayView<int> dens, ArrayView<int> output)
        {
            int count = counts[i];
            int num = nums[i];
            int den = dens[i];
            int acc = i;
            for (int k = 0; k < count; k++)
                acc += (num / den) ^ k;
            output[i] = acc;
        }

        // Loop-invariant remainder behind a guard inside the loop body (the loop runs, the division must not).
        private static void GuardedDivInLoopBodyKernel(Index1D i, ArrayView<int> counts, ArrayView<int> nums, ArrayView<int> dens, ArrayView<int> output)
        {
            int count = counts[i];
            int num = nums[i];
            int den = dens[i];
            int acc = i;
            for (int k = 0; k < count; k++)
            {
                if (den != 0)
                    acc += (num % den) * k;
                else
                    acc += k;
            }
            output[i] = acc;
        }

        private static byte[] GuardedDivShuffleOracle(byte[] src, int length, int elementSize)
        {
            var dst = new byte[length];
            int elements = length / elementSize;
            for (int o = 0; o < length; o++)
            {
                int s = o;
                if (o < elements * elementSize)
                {
                    int j = o / elements;
                    s = (o - j * elements) * elementSize + j;
                }
                dst[o] = src[s];
            }
            return dst;
        }

        [TestMethod]
        public async Task GuardedIntDivision_ShuffleKernel_LengthBelowElementSize_MatchesOracle() => await RunTest(async accelerator =>
        {
            // WebGL writes one output element per thread at its own slot: dst[o] with offset 0 is exactly that.
            // Every Codecs fixture shape with length < elementSize (elements == 0: the guard skips o / elements), the
            // boundary shapes, and a 1 MiB buffer (production chunk size) with a remainder tail.
            var cases = new (int Length, int ElementSize)[]
            {
                (1, 2), (1, 4), (3, 4), (7, 8), (15, 16), (1, 16),
                (2, 2), (17, 16), (31, 4), (1000, 3), (1 << 20, 4), ((1 << 20) + 13, 8),
            };
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, int, int, int>(
                GuardedDivShuffleKernel);
            foreach (var (length, elementSize) in cases)
            {
                var src = new byte[length];
                for (int k = 0; k < length; k++) src[k] = (byte)(k * 131 + 7);
                using var srcBuf = accelerator.Allocate1D(src);
                using var dstBuf = accelerator.Allocate1D(new byte[length]);
                kernel(length, srcBuf.View, dstBuf.View, 0, length, elementSize);
                await accelerator.SynchronizeAsync();
                var got = await dstBuf.CopyToHostAsync<byte>();
                var want = GuardedDivShuffleOracle(src, length, elementSize);
                for (int k = 0; k < length; k++)
                {
                    if (got[k] != want[k])
                        throw new Exception($"GuardedIntDivision shuffle length={length} elementSize={elementSize}: byte {k} expected {want[k]}, got {got[k]}.");
                }
            }
        });

        [TestMethod]
        public async Task GuardedIntDivision_ZeroAndMinusOneDivisors_MatchesOracle() => await RunTest(async accelerator =>
        {
            // WebGL: one store per thread into each of two output buffers, own slot: fine.
            const int N = 4096;
            var nums = new int[N];
            var dens = new int[N];
            for (int k = 0; k < N; k++)
            {
                // Hostile operands: every fourth divisor is 0, every fourth is -1 against int.MinValue, the rest mixed signs.
                nums[k] = (k & 3) == 1 ? int.MinValue : (k * 7919 - 9000000) ^ (k << 11);
                dens[k] = (k & 3) switch { 0 => 0, 1 => -1, 2 => (k % 97) - 48 | 1, _ => (k % 13) + 2 };
            }
            using var numBuf = accelerator.Allocate1D(nums);
            using var denBuf = accelerator.Allocate1D(dens);
            using var qBuf = accelerator.Allocate1D(new int[N]);
            using var rBuf = accelerator.Allocate1D(new int[N]);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                GuardedDivPairKernel);
            kernel(N, numBuf.View, denBuf.View, qBuf.View, rBuf.View);
            await accelerator.SynchronizeAsync();
            var q = await qBuf.CopyToHostAsync<int>();
            var r = await rBuf.CopyToHostAsync<int>();
            for (int k = 0; k < N; k++)
            {
                int n = nums[k], d = dens[k];
                int wantQ = n, wantR = 0;
                if (d != 0 && d != -1) { wantQ = n / d; wantR = n % d; }
                if (q[k] != wantQ || r[k] != (wantR ^ wantQ))
                    throw new Exception($"GuardedIntDivision pair {n} / {d} at {k}: expected q={wantQ} r^q={wantR ^ wantQ}, got q={q[k]} r^q={r[k]}.");
            }
        });

        [TestMethod]
        public async Task GuardedIntDivision_LoopInvariantDivisor_NotHoistedPastGuard_MatchesOracle() => await RunTest(async accelerator =>
        {
            const int N = 4096;
            var counts = new int[N];
            var nums = new int[N];
            var dens = new int[N];
            var bodyDens = new int[N];
            for (int k = 0; k < N; k++)
            {
                counts[k] = k % 5;
                nums[k] = k * 7919 - 1000000;
                // Zero-trip loop: the divisor is 0 exactly when the loop runs zero times.
                dens[k] = counts[k] == 0 ? 0 : (k % 11) - 5 | 1;
                // Guarded body: the loop runs, the guard skips the zero divisor.
                bodyDens[k] = (k % 3) == 0 ? 0 : (k % 9) + 1;
            }
            using var countBuf = accelerator.Allocate1D(counts);
            using var numBuf = accelerator.Allocate1D(nums);
            using var denBuf = accelerator.Allocate1D(dens);
            using var bodyDenBuf = accelerator.Allocate1D(bodyDens);
            using var zeroTripOut = accelerator.Allocate1D(new int[N]);
            using var bodyOut = accelerator.Allocate1D(new int[N]);
            var zeroTrip = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                GuardedDivZeroTripLoopKernel);
            var inBody = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(
                GuardedDivInLoopBodyKernel);
            zeroTrip(N, countBuf.View, numBuf.View, denBuf.View, zeroTripOut.View);
            inBody(N, countBuf.View, numBuf.View, bodyDenBuf.View, bodyOut.View);
            await accelerator.SynchronizeAsync();
            var gotZeroTrip = await zeroTripOut.CopyToHostAsync<int>();
            var gotBody = await bodyOut.CopyToHostAsync<int>();
            for (int k = 0; k < N; k++)
            {
                int wantZeroTrip = k, wantBody = k;
                for (int j = 0; j < counts[k]; j++)
                {
                    wantZeroTrip += (nums[k] / dens[k]) ^ j;
                    wantBody += bodyDens[k] != 0 ? (nums[k] % bodyDens[k]) * j : j;
                }
                if (gotZeroTrip[k] != wantZeroTrip)
                    throw new Exception($"GuardedIntDivision zero-trip loop at {k} (count {counts[k]}, den {dens[k]}): expected {wantZeroTrip}, got {gotZeroTrip[k]}.");
                if (gotBody[k] != wantBody)
                    throw new Exception($"GuardedIntDivision guarded loop body at {k} (count {counts[k]}, den {bodyDens[k]}): expected {wantBody}, got {gotBody[k]}.");
            }
        });
    }
}
