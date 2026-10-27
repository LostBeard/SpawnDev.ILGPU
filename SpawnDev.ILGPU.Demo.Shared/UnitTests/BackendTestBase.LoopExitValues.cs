using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Lanes that leave a loop at different iterations, through an early exit whose block uses a value of the loop body.
    // Velocity keeps every value of a loop body in one warp-wide local that each iteration overwrites for every lane; a
    // lane that has left is masked off in the next iterations and a masked load gives it nothing, so the exit block (run
    // after the loop) read what a LATER iteration left there. Found through SpawnDev.Codecs (Geordi 2026-10-27):
    // Vp9FrameEntropyKernel batched two frames per warp, and the end-of-block bit of the frame whose block ended first was
    // coded with the probability loaded for a later coefficient (Vp9KeyframeEncoderGpu_Batch_Unaligned_EqualsSingleFrameEncodes,
    // 4957 bytes instead of 4956). The shapes: the minimal one (one loop, an early break) and Codecs' bool encoder in its
    // token loop (an early return, inside a [NoInlining] step that codes four blocks). Every backend, against a CPU oracle (WebGL refuses the loops' byte writes:
    // one element per store site).
    public abstract partial class BackendTestBase
    {
        public struct LoopExit_BoolState { public uint LowValue; public uint Range; public int Count; public long OutLen; }

        static int LoopExit_Lz8(byte b)
        {
            if (b == 0) return 0;
            if ((b & 0x80) != 0) return 0;
            if ((b & 0x40) != 0) return 1;
            if ((b & 0x20) != 0) return 2;
            if ((b & 0x10) != 0) return 3;
            if ((b & 0x08) != 0) return 4;
            if ((b & 0x04) != 0) return 5;
            if ((b & 0x02) != 0) return 6;
            return 7;
        }

        static void LoopExit_EncodeBool(ref LoopExit_BoolState state, ArrayView<byte> outBuf, int bit, int probability)
        {
            uint split = 1u + (((state.Range - 1u) * (uint)probability) >> 8);
            uint range = split;
            uint lowvalue = state.LowValue;
            int count = state.Count;
            if (bit != 0) { lowvalue += split; range = state.Range - split; }
            int shift = LoopExit_Lz8((byte)range);
            range <<= shift;
            count += shift;
            if (count >= 0)
            {
                int offset = shift - count;
                if ((((ulong)lowvalue) << (offset - 1) & 0x80000000UL) != 0)
                {
                    long x = state.OutLen - 1;
                    if (x >= outBuf.Length) x = outBuf.Length - 1;
                    bool carrying = x >= 0;
                    while (carrying)
                    {
                        if (outBuf[x] == 0xFF) { outBuf[x] = 0; x--; carrying = x >= 0; }
                        else { outBuf[x] = (byte)(outBuf[x] + 1); carrying = false; }
                    }
                }
                if (state.OutLen < outBuf.Length)
                    outBuf[state.OutLen] = (byte)((lowvalue >> (24 - offset)) & 0xFF);
                state.OutLen += 1;
                shift = count;
                lowvalue = (lowvalue << offset) & 0xFFFFFFu;
                count -= 8;
            }
            lowvalue <<= shift;
            state.LowValue = lowvalue;
            state.Range = range;
            state.Count = count;
        }

        static void LoopExit_EncodeBoolNoCarry(ref LoopExit_BoolState state, ArrayView<byte> outBuf, int bit, int probability)
        {
            uint split = 1u + (((state.Range - 1u) * (uint)probability) >> 8);
            uint range = split;
            uint lowvalue = state.LowValue;
            int count = state.Count;
            if (bit != 0) { lowvalue += split; range = state.Range - split; }
            int shift = LoopExit_Lz8((byte)range);
            range <<= shift;
            count += shift;
            if (count >= 0)
            {
                int offset = shift - count;
                if (state.OutLen < outBuf.Length)
                    outBuf[state.OutLen] = (byte)((lowvalue >> (24 - offset)) & 0xFF);
                state.OutLen += 1;
                shift = count;
                lowvalue = (lowvalue << offset) & 0xFFFFFFu;
                count -= 8;
            }
            lowvalue <<= shift;
            state.LowValue = lowvalue;
            state.Range = range;
            state.Count = count;
        }

        static int LoopExit_Block(ref LoopExit_BoolState state, ArrayView<byte> outBuf, ArrayView<int> coefs, ArrayView<byte> probs, int eob, int max)
        {
            int c = 0;
            while (c < max)
            {
                byte m0 = probs[c * 3], m1 = probs[c * 3 + 1], m2 = probs[c * 3 + 2];
                if (c == eob)
                {
                    LoopExit_EncodeBool(ref state, outBuf, 0, m0);
                    return eob;
                }
                LoopExit_EncodeBool(ref state, outBuf, 1, m0);
                LoopExit_EncodeBool(ref state, outBuf, 1, m1);
                int value = coefs[c];
                LoopExit_EncodeBool(ref state, outBuf, 0, m2);
                LoopExit_EncodeBool(ref state, outBuf, value < 0 ? 1 : 0, 128);
                c++;
            }
            return eob;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static void LoopExit_Steps(ArrayView<int> coefs, ArrayView<byte> probs, ArrayView<int> eobs, ArrayView<byte> outBuf, ArrayView<int> st, int f)
        {
            var state = new LoopExit_BoolState { LowValue = 0, Range = 255, Count = -24, OutLen = 0 };
            var fOut = outBuf.SubView(f * 256, 256);
            for (int j = 0; j < 4; j++)
                LoopExit_Block(ref state, fOut, coefs.SubView(f * 64 + j * 16, 16), probs, eobs[f * 4 + j], 16);
            st[f * 4] = (int)state.LowValue; st[f * 4 + 1] = (int)state.Range; st[f * 4 + 2] = state.Count; st[f * 4 + 3] = (int)state.OutLen;
        }

        static void LoopExit_EarlyExitKernel(Index1D idx, ArrayView<byte> probs, ArrayView<byte> outBuf, ArrayView<int> st)
        {
            int f = idx;
            int eob = f == 0 ? 14 : 9;
            var fOut = outBuf.SubView(f * 256, 256);
            var s64 = new LoopExit_BoolState { LowValue = 0, Range = 255, Count = -24, OutLen = 0 };
            int c = 0;
            while (c < 16)
            {
                byte m0 = probs[c * 3];
                if (c == eob)
                {
                    LoopExit_EncodeBoolNoCarry(ref s64, fOut, 0, m0);
                    break;
                }
                LoopExit_EncodeBoolNoCarry(ref s64, fOut, 1, m0);
                c++;
            }
            st[f * 4] = (int)s64.LowValue; st[f * 4 + 1] = (int)s64.Range; st[f * 4 + 2] = s64.Count; st[f * 4 + 3] = (int)s64.OutLen;
        }

        static void LoopExit_TokenKernel(Index1D idx, ArrayView<int> coefs, ArrayView<byte> probs, ArrayView<int> eobs,
            ArrayView<byte> outBuf, ArrayView<int> st) => LoopExit_Steps(coefs, probs, eobs, outBuf, st, idx);

        [TestMethod]
        public async Task LoopExitValues_EarlyExitUsesBodyValue_MatchesOracle() => await RunEmulatedTest(async accelerator =>
        {
            try
            {
                await LoopExitValues_Run(accelerator);
            }
            catch (Exception ex) when (accelerator is SpawnDev.ILGPU.WebGL.WebGLAccelerator)
            {
                // WebGL captures one element per store site per thread: the bool encoder's byte writes from a loop are
                // refused, never wrong
                bool refused = false;
                for (var e = ex; e != null; e = e.InnerException)
                    if (e.Message.Contains("wrote two different elements")) refused = true;
                if (!refused) throw;
            }
        });

        static async Task LoopExitValues_Run(Accelerator accelerator)
        {
            var rng = new Random(3);
            var coefs = new int[128];
            for (int i = 0; i < coefs.Length; i++) coefs[i] = rng.Next(-40, 40);
            var probs = new byte[48];
            for (int i = 0; i < probs.Length; i++) probs[i] = (byte)rng.Next(1, 256);
            int[] eobs = { 16, 14, 16, 9, 16, 16, 3, 16 };
            using var dC = accelerator.Allocate1D(coefs);
            using var dP = accelerator.Allocate1D(probs);
            using var dE = accelerator.Allocate1D(eobs);

            // The minimal shape: lane 0 leaves at 14, lane 1 at 9
            using var dOut1 = accelerator.Allocate1D<byte>(512);
            using var dSt1 = accelerator.Allocate1D<int>(8);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>, ArrayView<int>>(LoopExit_EarlyExitKernel)(
                (Index1D)2, dP.View, dOut1.View, dSt1.View);
            await accelerator.SynchronizeAsync();
            var got1 = await dSt1.CopyToHostAsync<int>();

            // The token loop shape: four blocks per lane, each with its own end of block
            using var dOut2 = accelerator.Allocate1D<byte>(512);
            using var dSt2 = accelerator.Allocate1D<int>(8);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<byte>, ArrayView<int>, ArrayView<byte>, ArrayView<int>>(
                LoopExit_TokenKernel)((Index1D)2, dC.View, dP.View, dE.View, dOut2.View, dSt2.View);
            await accelerator.SynchronizeAsync();
            var got2 = await dSt2.CopyToHostAsync<int>();
            var out2 = await dOut2.CopyToHostAsync<byte>();

            // CPU oracle: the same code as plain C#, lane by lane
            var want1 = new int[8];
            var want2 = new int[8];
            var wantOut2 = new byte[512];
            for (int f = 0; f < 2; f++)
            {
                int eob = f == 0 ? 14 : 9;
                var s = new LoopExit_BoolState { LowValue = 0, Range = 255, Count = -24, OutLen = 0 };
                var buf = new byte[256];
                for (int c = 0; c < 16; c++)
                {
                    byte m0 = probs[c * 3];
                    if (c == eob) { LoopExit_EncodeBoolHost(ref s, buf, 0, m0, carry: false); break; }
                    LoopExit_EncodeBoolHost(ref s, buf, 1, m0, carry: false);
                }
                want1[f * 4] = (int)s.LowValue; want1[f * 4 + 1] = (int)s.Range; want1[f * 4 + 2] = s.Count; want1[f * 4 + 3] = (int)s.OutLen;

                var s2 = new LoopExit_BoolState { LowValue = 0, Range = 255, Count = -24, OutLen = 0 };
                var buf2 = new byte[256];
                for (int j = 0; j < 4; j++)
                {
                    int e = eobs[f * 4 + j];
                    for (int c = 0; c < 16; c++)
                    {
                        byte m0 = probs[c * 3], m1 = probs[c * 3 + 1], m2 = probs[c * 3 + 2];
                        if (c == e) { LoopExit_EncodeBoolHost(ref s2, buf2, 0, m0, carry: true); break; }
                        LoopExit_EncodeBoolHost(ref s2, buf2, 1, m0, carry: true);
                        LoopExit_EncodeBoolHost(ref s2, buf2, 1, m1, carry: true);
                        int value = coefs[f * 64 + j * 16 + c];
                        LoopExit_EncodeBoolHost(ref s2, buf2, 0, m2, carry: true);
                        LoopExit_EncodeBoolHost(ref s2, buf2, value < 0 ? 1 : 0, 128, carry: true);
                    }
                }
                want2[f * 4] = (int)s2.LowValue; want2[f * 4 + 1] = (int)s2.Range; want2[f * 4 + 2] = s2.Count; want2[f * 4 + 3] = (int)s2.OutLen;
                Array.Copy(buf2, 0, wantOut2, f * 256, 256);
            }
            for (int i = 0; i < 8; i++)
            {
                if (got1[i] != want1[i]) throw new Exception($"early break: state[{i}] (lane {i / 4}) = {got1[i]}, expected {want1[i]}");
                if (got2[i] != want2[i]) throw new Exception($"token loop: state[{i}] (lane {i / 4}) = {got2[i]}, expected {want2[i]}");
            }
            for (int i = 0; i < 512; i++)
                if (out2[i] != wantOut2[i]) throw new Exception($"token loop: out[{i}] = {out2[i]}, expected {wantOut2[i]}");
        }

        static void LoopExit_EncodeBoolHost(ref LoopExit_BoolState state, byte[] outBuf, int bit, int probability, bool carry)
        {
            uint split = 1u + (((state.Range - 1u) * (uint)probability) >> 8);
            uint range = split;
            uint lowvalue = state.LowValue;
            int count = state.Count;
            if (bit != 0) { lowvalue += split; range = state.Range - split; }
            int shift = (byte)range == 0 ? 0 : System.Numerics.BitOperations.LeadingZeroCount((uint)(byte)range) - 24;
            range <<= shift;
            count += shift;
            if (count >= 0)
            {
                int offset = shift - count;
                if (carry && ((((ulong)lowvalue) << (offset - 1) & 0x80000000UL) != 0))
                {
                    long x = state.OutLen - 1;
                    if (x >= outBuf.Length) x = outBuf.Length - 1;
                    while (x >= 0 && outBuf[x] == 0xFF) { outBuf[x] = 0; x--; }
                    if (x >= 0) outBuf[x]++;
                }
                if (state.OutLen < outBuf.Length) outBuf[state.OutLen] = (byte)((lowvalue >> (24 - offset)) & 0xFF);
                state.OutLen += 1;
                shift = count;
                lowvalue = (lowvalue << offset) & 0xFFFFFFu;
                count -= 8;
            }
            lowvalue <<= shift;
            state.LowValue = lowvalue; state.Range = range; state.Count = count;
        }
    }
}
