using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-23 (SpawnDev.Codecs, found by Geordi): three WGSL code generation holes that made shader creation fail, so
    // every dispatch of the kernel errored. All three were already present in 5.4.2; 5.4.2 failed earlier in the same
    // kernels on undeclared names (fixed in 5.4.3), which hid them.
    // 1. A view used by an Atomic.* op is declared array<atomic<T>>. A plain read or write of it through a pointer
    //    used in another block went through the "cross-block" inline expression without atomicLoad / atomicStore:
    //    "cannot assign 'atomic<i32>' to 'i32'" (ExactPaletteBuilder.InsertKernel).
    // 2. An element of a SUB-VIEW of a byte view was addressed with `&source[offset]`, indexing the packed
    //    array<atomic<u32>> words with a byte index and reading them as plain values: "cannot assign 'atomic<u32>' to
    //    'i32'" (VP8/VP9/AV1 encoders, VP8 decoder). The sub-view's element address also ignored the view offset of
    //    a kernel argument that is itself a sub-view.
    // 3. A hoisted struct variable was declared `var v : struct_N = 0;` ("expected struct_N, got AbstractInt",
    //    WebPLossyEncodeRowKernel: a StructureValue of Struct<Int64 x5, Int32> used in another block).
    // Red-checks (offline, WGSL of the b0991d7 tree vs the fixed tree): test 1's kernel reads its atomic buffer plainly
    // on b0991d7 (Tint rejects it; naga 29 does not), test 2's two kernels fail naga on b0991d7. The two struct tests
    // below do NOT reproduce hole 3 - four shapes (loop phi, helper return, `ref` + whole copy, WebPModeScore layout)
    // all stayed split into fields; they remain as correctness coverage. Hole 3 is guarded by the SpawnDev.Codecs
    // WebPLossyEncodeRowKernel (naga: fails on b0991d7, passes fixed) until a small kernel that keeps a whole
    // StructureValue across blocks is found.
    public abstract partial class BackendTestBase
    {
        // ---- 1. atomic + plain access across blocks ----

        // The control flow of SpawnDev.Codecs ExactPaletteBuilder.InsertKernel: state[1] is read before the probe loop and
        // again in the loop's else-arm, and Atomic.Add'ed in another arm, so its element address crosses blocks.
        private const int AtomicPlainSlots = 64;

        private static void AtomicPlainMixKernel(Index1D i, ArrayView<int> keys, ArrayView<int> table, ArrayView<int> state)
        {
            int limit = state[0];
            int key = keys[i];
            bool room = state[1] <= limit;
            bool wanted = (key != 0) & room;
            if (wanted)
            {
                int slot = (int)(((uint)key * 2654435761u) >> 26);
                int step = 0;
                bool done = false;
                bool go = (!done) & (step < AtomicPlainSlots);
                while (go)
                {
                    int seen = Atomic.CompareExchange(ref table[slot], 0, key);
                    if (seen == 0)
                    {
                        Atomic.Add(ref state[1], 1);
                        done = true;
                    }
                    else if (seen == key)
                    {
                        done = true;
                    }
                    else
                    {
                        slot = (slot + 1) & (AtomicPlainSlots - 1);
                        step++;
                        if (state[1] > limit) done = true;
                    }
                    go = (!done) & (step < AtomicPlainSlots);
                }
                if (!done) state[2] = 1;
            }
        }

        [TestMethod]
        public async Task WgslCodegen_AtomicBufferReadPlainlyAcrossBlocks_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: no atomics.");

            var keys = new int[] { 5, 9, 5, 12, 33, 9, 70, 12, 41, 5, 88, 33 };
            int distinct = 0;
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var k in keys) if (seen.Add(k)) distinct++;

            // A generous limit: every distinct key lands exactly once, no thread gives up, so the result is deterministic.
            const int limit = 64;
            using var keyBuf = accelerator.Allocate1D(keys);
            using var tableBuf = accelerator.Allocate1D(new int[AtomicPlainSlots]);
            using var stateBuf = accelerator.Allocate1D(new int[] { limit, 0, 0 });
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(AtomicPlainMixKernel);
            kernel(keys.Length, keyBuf.View, tableBuf.View, stateBuf.View);
            await accelerator.SynchronizeAsync();
            var state = await stateBuf.CopyToHostAsync<int>();
            var table = await tableBuf.CopyToHostAsync<int>();
            int stored = 0;
            foreach (var v in table) if (v != 0) { stored++; if (!seen.Contains(v)) throw new Exception($"table holds {v}, which is not a key."); }
            if (state[1] != distinct || state[2] != 0 || stored != distinct)
                throw new Exception($"distinct={state[1]} overflow={state[2]} stored={stored}, expected {distinct} / 0 / {distinct}.");
        });

        // ---- 2. elements of a sub-view of a byte view ----

        private static void ByteSubViewReadKernel(Index1D i, ArrayView<byte> data, ArrayView<int> output, int off)
        {
            var sub = data.SubView(off, 8);
            int s = 0;
            for (int k = 0; k <= i; k++)
                s += sub[k];
            output[i] = s * 256 + sub[7 - i];
        }

        private static void ByteSubViewWriteKernel(Index1D i, ArrayView<byte> data, int off)
        {
            var sub = data.SubView(off, 8);
            int idx = i;
            if (sub[idx] != 0)
                sub[idx] = (byte)(sub[idx] * 3 + idx);
        }

        [TestMethod]
        public async Task WgslCodegen_ByteSubViewElements_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: in-kernel scatter stores are not supported.");

            var bytes = new byte[48];
            for (int k = 0; k < bytes.Length; k++) bytes[k] = (byte)(k * 29 + 3);
            // The kernel argument is itself a sub-view at an odd byte offset (3), and the kernel cuts a second one (+6).
            const int argOff = 3, kernelOff = 6, b = argOff + kernelOff;
            using var buf = accelerator.Allocate1D(bytes);
            using var outBuf = accelerator.Allocate1D(new int[8]);
            var read = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<int>, int>(ByteSubViewReadKernel);
            var write = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, int>(ByteSubViewWriteKernel);
            var arg = buf.View.SubView(argOff, 40);
            read(8, arg, outBuf.View, kernelOff);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < 8; i++)
            {
                int s = 0;
                for (int k = 0; k <= i; k++) s += bytes[b + k];
                int want = s * 256 + bytes[b + 7 - i];
                if (got[i] != want)
                    throw new Exception($"byte sub-view read: output[{i}] = {got[i]}, expected {want}.");
            }

            write(8, arg, kernelOff);
            await accelerator.SynchronizeAsync();
            var after = await buf.View.CopyToHostAsync();
            var wantBytes = (byte[])bytes.Clone();
            for (int i = 0; i < 8; i++)
                if (wantBytes[b + i] != 0) wantBytes[b + i] = (byte)(wantBytes[b + i] * 3 + i);
            for (int k = 0; k < bytes.Length; k++)
                if (after[k] != wantBytes[k])
                    throw new Exception($"byte sub-view write: byte {k} is {after[k]}, expected {wantBytes[k]}.");
        });

        // ---- 3. a struct value live across a loop ----

        // The shape of SpawnDev.Codecs' WebPModeScore in WebPLossyEncodeRowKernel: a struct with 64-bit fields, a best-so-far
        // copy kept through a loop and replaced whole.
        // The layout of WebPModeScore (Struct<Int64 x5, Int32>): the codegen hoisted a StructureValue of this type.
        public struct HoistScore
        {
            public long D, SD, H, R, Score;
            public uint Nz;

            // Like WebPModeScore.Init / SetRD: instance methods that mutate the struct through `this`.
            public void Init() { D = 0; SD = 0; R = 0; H = 0; Nz = 0; Score = 0x7fffffffffffffL; }
            public void SetRD(int lambda) => Score = (R + H) * lambda + 256L * (D + SD);
        }

        // Like WebPLossyEncodeRowKernel.PickBestIntra4: the score travels by `ref` and is replaced WHOLE inside a loop.
        private static void PickBestScore(ArrayView<int> input, int thread, ref HoistScore rd)
        {
            var rdBest = new HoistScore();
            rdBest.Init();
            for (int k = 0; k < input.IntLength; k++)
            {
                var cur = new HoistScore();
                cur.Init();
                cur.D = input[k] * (long)(thread + 1);
                cur.R = k;
                cur.H = 3;
                cur.Nz = (uint)(input[k] & 7);
                cur.SetRD(1000);
                if (cur.Score < rdBest.Score)
                    rdBest = cur;
            }
            if (rdBest.Score < rd.Score)
                rd = rdBest;
        }

        private static void StructAcrossLoopKernel(Index1D i, ArrayView<int> input, ArrayView<long> output)
        {
            int thread = i;
            HoistScore best = default;
            best.Init();
            PickBestScore(input, thread, ref best);
            output[thread * 4] = best.D;
            output[thread * 4 + 1] = best.R;
            output[thread * 4 + 2] = best.Score;
            output[thread * 4 + 3] = best.Nz;
        }

        // A whole struct value returned by a non-inlined helper (a call result is one IR value, not split into fields)
        // and used in later blocks: the struct value itself crosses blocks and is hoisted.
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static HoistScore MakeHoistScore(int value, int thread)
            => new HoistScore { D = value * 3L, R = thread, Score = value * 1000L, Nz = (uint)thread };

        private static void StructAcrossBlocksKernel(Index1D i, ArrayView<int> input, ArrayView<HoistScore> output)
        {
            int thread = i;
            var s = MakeHoistScore(input[thread], thread);
            for (int k = 0; k < input.IntLength; k++)
            {
                if (input[k] > input[thread])
                {
                    output[thread] = s;
                    return;
                }
            }
            s.R = -1;
            output[thread] = s;
        }

        [TestMethod]
        public async Task WgslCodegen_StructValueAcrossBlocks_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: struct output views are not supported.");

            var input = new int[] { 40, 13, 75, 22, 9, 31 };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D(new HoistScore[input.Length]);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<HoistScore>>(StructAcrossBlocksKernel);
            kernel(input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<HoistScore>();
            for (int t = 0; t < input.Length; t++)
            {
                bool larger = false;
                foreach (var v in input) if (v > input[t]) larger = true;
                long r = larger ? t : -1;
                if (got[t].D != input[t] * 3L || got[t].R != r || got[t].Score != input[t] * 1000L || got[t].Nz != (uint)t)
                    throw new Exception($"struct across blocks: thread {t} got ({got[t].D}, {got[t].R}, {got[t].Score}, {got[t].Nz}), expected ({input[t] * 3L}, {r}, {input[t] * 1000L}, {t}).");
            }
        });

        [TestMethod]
        public async Task WgslCodegen_StructLiveAcrossLoop_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: in-kernel multi-store per thread is not supported.");

            var input = new int[] { 40, 13, 75, 22, 9, 31, 60 };
            const int N = 6;
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D(new long[N * 4]);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<long>>(StructAcrossLoopKernel);
            kernel(N, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<long>();
            for (int i = 0; i < N; i++)
            {
                long bd = 0, br = 0, bs = 0x7fffffffffffffL; uint bnz = 0;
                for (int k = 0; k < input.Length; k++)
                {
                    long d = input[k] * (long)(i + 1), r = k, s = (r + 3) * 1000L + 256L * d;
                    if (s < bs) { bd = d; br = r; bs = s; bnz = (uint)(input[k] & 7); }
                }
                if (got[i * 4] != bd || got[i * 4 + 1] != br || got[i * 4 + 2] != bs || got[i * 4 + 3] != bnz)
                    throw new Exception($"struct across loop: thread {i} got ({got[i * 4]}, {got[i * 4 + 1]}, {got[i * 4 + 2]}, {got[i * 4 + 3]}), expected ({bd}, {br}, {bs}, {bnz}).");
            }
        });
    }
}
