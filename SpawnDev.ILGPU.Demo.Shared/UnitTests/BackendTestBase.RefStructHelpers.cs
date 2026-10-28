using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A struct local passed `ref` through two levels of helpers that are real calls, the inner one writing a buffer and
    // updating the struct (a bool coder's state). Shaped after SpawnDev.Codecs' VP8/VP9 entropy encoders (Geordi
    // 2026-10-28): Vp9BlockCoefEncoderGpu.EmitTreeWalk(ref state, outBuf, consts, long rowBase, token) ->
    // Vp8BoolEncoderGpu.EncodeBool(ref state, outBuf, bit, prob), wrong on Wasm only. Every backend, against the host.
    public abstract partial class BackendTestBase
    {
        public struct RefStructCoderState
        {
            public uint Low;
            public uint Range;
            public int Count;
            public long OutLen;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RefStruct_Put(ref RefStructCoderState s, ArrayView<int> outBuf, int bit, int prob)
        {
            uint split = 1u + (((s.Range - 1u) * (uint)prob) >> 8);
            if (bit != 0) { s.Low += split; s.Range -= split; }
            else s.Range = split;
            while (s.Range < 128u) { s.Range <<= 1; s.Low <<= 1; s.Count++; }
            if (s.Count >= 8)
            {
                if (s.OutLen < outBuf.Length) outBuf[s.OutLen] = (int)(s.Low >> 8) & 0xFF;
                s.OutLen++;
                s.Low &= 0xFF;
                s.Count -= 8;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void RefStruct_Walk(ref RefStructCoderState s, ArrayView<int> outBuf, ArrayView<int> probs, long rowBase, int token)
        {
            // A local of the helper's own next to the caller's state, so the two frames have to stay apart.
            var scratch = new RefStructCoderState { Low = 7, Range = 9, Count = 3, OutLen = 5 };
            for (int b = 0; b < 4; b++)
                RefStruct_Put(ref s, outBuf, (token >> b) & 1, probs[rowBase + b]);
            if (scratch.OutLen + scratch.Count == 8)
                RefStruct_Put(ref s, outBuf, token & 1, probs[rowBase]);
        }

        static void RefStructHelpersKernel(Index1D i, ArrayView<int> tokens, ArrayView<int> probs, ArrayView<int> outBuf, ArrayView<int> lens)
        {
            var s = new RefStructCoderState { Low = 0, Range = 255, Count = 0, OutLen = 0 };
            var view = outBuf.SubView(i * 64, 64);
            for (int t = 0; t < 8; t++)
            {
                int token = tokens[i * 8 + t];
                if (token == 0)
                    RefStruct_Put(ref s, view, 0, 200);
                else
                    RefStruct_Walk(ref s, view, probs, (long)(token % 4) * 4, token);
            }
            lens[i] = (int)s.OutLen;
        }

        [TestMethod]
        public async Task RefStructState_ThroughNestedHelpers_MatchesHost() => await RunTest(async accelerator =>
        {
            const int n = 32;
            var rng = new Random(11);
            var tokens = new int[n * 8];
            for (int k = 0; k < tokens.Length; k++) tokens[k] = rng.Next(3) == 0 ? 0 : rng.Next(1, 16);
            var probs = new int[16];
            for (int k = 0; k < 16; k++) probs[k] = 20 + k * 13;
            using var dTokens = accelerator.Allocate1D(tokens);
            using var dProbs = accelerator.Allocate1D(probs);
            using var dOut = accelerator.Allocate1D<int>(n * 64);
            using var dLens = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(RefStructHelpersKernel)(
                (Index1D)n, dTokens.View, dProbs.View, dOut.View, dLens.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            var gotLens = await dLens.CopyToHostAsync<int>();
            var want = new int[n * 64];
            var wantLens = new int[n];
            for (int i = 0; i < n; i++)
            {
                var s = new RefStructCoderState { Low = 0, Range = 255, Count = 0, OutLen = 0 };
                var view = new ArraySegment<int>(want, i * 64, 64);
                for (int t = 0; t < 8; t++)
                {
                    int token = tokens[i * 8 + t];
                    if (token == 0) HostPut(ref s, view, 0, 200);
                    else
                    {
                        long rowBase = (long)(token % 4) * 4;
                        for (int b = 0; b < 4; b++) HostPut(ref s, view, (token >> b) & 1, probs[rowBase + b]);
                        HostPut(ref s, view, token & 1, probs[rowBase]);
                    }
                }
                wantLens[i] = (int)s.OutLen;
            }
            for (int i = 0; i < n; i++)
            {
                if (gotLens[i] != wantLens[i]) throw new Exception($"thread {i}: {gotLens[i]} bytes, expected {wantLens[i]}");
                for (int k = 0; k < 64; k++)
                    if (got[i * 64 + k] != want[i * 64 + k])
                        throw new Exception($"thread {i}, byte {k}: {got[i * 64 + k]}, expected {want[i * 64 + k]}");
            }

            static void HostPut(ref RefStructCoderState s, ArraySegment<int> outBuf, int bit, int prob)
            {
                uint split = 1u + (((s.Range - 1u) * (uint)prob) >> 8);
                if (bit != 0) { s.Low += split; s.Range -= split; }
                else s.Range = split;
                while (s.Range < 128u) { s.Range <<= 1; s.Low <<= 1; s.Count++; }
                if (s.Count >= 8)
                {
                    if (s.OutLen < outBuf.Count) outBuf[(int)s.OutLen] = (int)(s.Low >> 8) & 0xFF;
                    s.OutLen++;
                    s.Low &= 0xFF;
                    s.Count -= 8;
                }
            }
        });
    }
}
