using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // XOR of two bools. WGSL has no `^` on bool (it is `!=`); the WebGPU generator emitted `a ^ b`, an invalid shader
    // (naga: "ExclusiveOr can't work with Bool"), found in SpawnDev.Codecs' VP9 ParseBlock (2026-11-09). Both forms: the
    // bool result branched on / stored as a bool, and the bool XOR widened to an int.
    public abstract partial class BackendTestBase
    {
        static void BoolXorKernel(Index1D i, ArrayView<int> data, ArrayView<int> output)
        {
            bool a = data[i] > 3;
            bool b = data[i + 1] < 7;
            bool c = (data[i] & 1) != 0;
            bool x = a ^ b;
            int widened = (b ^ c) ? 1 : 0;
            output[i] = (x ? 100 : 0) + widened * 10 + ((x ^ c) ? 1 : 0);
        }

        [TestMethod]
        public async Task BoolXor_TwoComparisons_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 64;
            var data = new int[n + 1];
            for (int k = 0; k < data.Length; k++) data[k] = (k * 5 + 3) % 11;
            using var dData = accelerator.Allocate1D(data);
            using var dOut = accelerator.Allocate1D<int>(n);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(BoolXorKernel);
            kernel((Index1D)n, dData.View, dOut.View);
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                bool a = data[i] > 3, b = data[i + 1] < 7, c = (data[i] & 1) != 0;
                bool x = a ^ b;
                int want = (x ? 100 : 0) + ((b ^ c) ? 1 : 0) * 10 + ((x ^ c) ? 1 : 0);
                if (got[i] != want) throw new Exception($"output[{i}] = {got[i]}, expected {want}");
            }
        });
    }
}
