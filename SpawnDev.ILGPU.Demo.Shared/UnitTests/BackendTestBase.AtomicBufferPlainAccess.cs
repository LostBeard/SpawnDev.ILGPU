using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A uint or float buffer the kernel updates with Atomic.* AND reads / writes with plain loads and stores. WebGPU
    // declares such a buffer array<atomic<u32>> (WGSL has no atomic<f32>, and unsigned atomics use u32) while the
    // kernel's uint and float values are i32 / f32, and the plain accesses did not bitcast: `atomicStore(p, 0)` was
    // `atomicStore(ptr<storage, atomic<u32>>, i32)`, which Tint rejects, so the pipeline was invalid. Found through
    // SpawnDev.Codecs (Geordi 2026-10-27): the zlib lanes emitter zeroes its bit words, ORs codes into them and reads
    // bytes back. Shapes: zero, Atomic.Or, plain read (uint); plain store, Atomic.Add, plain read (float).
    public abstract partial class BackendTestBase
    {
        static void AtomicPlain_UIntWordsKernel(Index1D i, ArrayView<uint> words, ArrayView<int> o, long wordsAt)
        {
            for (int k = 0; k < 4; k++) words[wordsAt + i * 4 + k] = 0;
            uint v = (uint)i * 2654435761u;
            Atomic.Or(ref words[wordsAt + (i * 4 + 1)], v << (i & 7));
            Atomic.Or(ref words[wordsAt + (i * 4 + 1)], 1u);
            o[i] = (int)(words[wordsAt + (i * 4 + 1)] >> (8 * (i & 3)));
        }

        static void AtomicPlain_FloatKernel(Index1D i, ArrayView<float> f, ArrayView<float> o)
        {
            f[i * 2] = 0.25f * i;
            f[i * 2 + 1] = 1.0f;
            Atomic.Add(ref f[i * 2 + 1], 1.5f);
            o[i] = f[i * 2 + 1] + f[i * 2];
        }

        [TestMethod]
        public async Task AtomicBuffer_PlainStoreAndLoad_MatchesOracle() => await RunTest(async accelerator =>
        {
            const int n = 1000;
            const long at = 3;
            var init = new uint[at + n * 4];
            for (int k = 0; k < init.Length; k++) init[k] = 0xDEADBEEFu;
            using var dWords = accelerator.Allocate1D(init);
            using var dO = accelerator.Allocate1D<int>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<int>, long>(AtomicPlain_UIntWordsKernel)(
                (Index1D)n, dWords.View, dO.View, at);
            using var dF = accelerator.Allocate1D<float>(n * 2);
            using var dFo = accelerator.Allocate1D<float>(n);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(AtomicPlain_FloatKernel)(
                (Index1D)n, dF.View, dFo.View);
            await accelerator.SynchronizeAsync();
            var words = await dWords.CopyToHostAsync<uint>();
            var o = await dO.CopyToHostAsync<int>();
            var fo = await dFo.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                uint w = (((uint)i * 2654435761u) << (i & 7)) | 1u;
                int want = (int)(w >> (8 * (i & 3)));
                if (o[i] != want) throw new Exception($"uint read {i} = {o[i]}, expected {want}");
                for (int k = 0; k < 4; k++)
                {
                    uint wantWord = k == 1 ? w : 0u;
                    if (words[at + i * 4 + k] != wantWord) throw new Exception($"uint word {i * 4 + k} = 0x{words[at + i * 4 + k]:X8}, expected 0x{wantWord:X8}");
                }
                float wantF = 2.5f + 0.25f * i;
                if (fo[i] != wantF) throw new Exception($"float {i} = {fo[i]}, expected {wantF}");
            }
            if (words[0] != 0xDEADBEEFu || words[2] != 0xDEADBEEFu) throw new Exception("wrote before the view offset");
        });
    }
}
