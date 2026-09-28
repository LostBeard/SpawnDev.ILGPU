using System;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Per-lane memory accesses that are NOT "4 consecutive elements of a 4-byte type" (2026-09-28).
    // The Wasm SIMD emitter assumed every lane-variant address was unit-stride without checking, so once a
    // worker ran 4+ consecutive items it vectorized these into one v128 load/store at the wrong addresses:
    // an f32 field of a struct array read Key0,Value0,Key1,Value1 as four keys; a scatter through a
    // position table and a stride-2 read hit the wrong slots. Large N (well past 4 items per worker at any
    // worker count) and an odd tail make every backend's SIMD/by-4 path run; each value is checked on the
    // host. The plain elementwise kernel is the control that must stay right (and vectorized on Wasm).
    public abstract partial class BackendTestBase
    {
        const int SimdShapeN = 4099;

        public struct SimdShapePair
        {
            public float Key;
            public int Value;
        }

        static void SimdShapeStructFieldKernel(Index1D i, ArrayView<SimdShapePair> pairs, ArrayView<float> keys) =>
            keys[i] = pairs[i].Key * 2f;

        static void SimdShapeScatterKernel(Index1D i, ArrayView<float> src, ArrayView<int> pos, ArrayView<float> dst) =>
            dst[pos[i]] = src[i] + 1f;

        static void SimdShapeStride2Kernel(Index1D i, ArrayView<float> src, ArrayView<float> dst) =>
            dst[i] = src[i * 2] - src[i * 2 + 1];

        // A block reached two ways (`a || (b && c)`): the Wasm SIMD if-conversion took only the first route
        // into it, so lanes arriving via b && c lost the increment.
        static void SimdShapeShortCircuitKernel(Index1D i, ArrayView<int> q, ArrayView<int> r, ArrayView<int> o)
        {
            int v = q[i];
            if (r[i] > 10 || (r[i] == 10 && (v & 1) == 1)) v++;
            o[i] = v;
        }

        static void SimdShapeElementwiseKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o) =>
            o[i] = a[i] * 2f + b[i];

        [TestMethod]
        public async Task SimdAddressShapes_NonUnitStrideAccesses_Exact() => await RunTest(async accelerator =>
        {
            int n = SimdShapeN;
            var pairs = new SimdShapePair[n];
            var src = new float[2 * n];
            var pos = new int[n];
            for (int i = 0; i < n; i++)
            {
                pairs[i] = new SimdShapePair { Key = i * 0.5f + 1f, Value = 100000 + i };
                pos[i] = (i * 7919) % n;              // a permutation (7919 is prime, coprime to 4099)
            }
            for (int i = 0; i < 2 * n; i++) src[i] = (i % 113) * 0.25f;

            using var pairBuf = accelerator.Allocate1D(pairs);
            using var keyBuf = accelerator.Allocate1D<float>(n);
            using var srcBuf = accelerator.Allocate1D(src);
            using var posBuf = accelerator.Allocate1D(pos);
            using var scatterBuf = accelerator.Allocate1D<float>(n);
            using var strideBuf = accelerator.Allocate1D<float>(n);
            using var elemBuf = accelerator.Allocate1D<float>(n);
            // Separate buffers for the elementwise operands: two SubViews of ONE buffer at an offset that is
            // not 256-byte aligned overlap once WebGPU rounds the binding offset down (it rejects that).
            using var aBuf = accelerator.Allocate1D(src.AsSpan(0, n).ToArray());
            using var bBuf = accelerator.Allocate1D(src.AsSpan(n, n).ToArray());
            var qv = new int[n]; var rv = new int[n];
            for (int i = 0; i < n; i++) { qv[i] = i % 8; rv[i] = 5 + 5 * ((i / 8) % 3); }   // r in {5,10,15}, every q parity
            using var qBuf = accelerator.Allocate1D(qv);
            using var rBuf = accelerator.Allocate1D(rv);
            using var scBuf = accelerator.Allocate1D<int>(n);

            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<SimdShapePair>, ArrayView<float>>(SimdShapeStructFieldKernel)
                (n, pairBuf.View, keyBuf.View);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<int>, ArrayView<float>>(SimdShapeScatterKernel)
                (n, aBuf.View, posBuf.View, scatterBuf.View);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(SimdShapeStride2Kernel)
                (n, srcBuf.View, strideBuf.View);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(SimdShapeElementwiseKernel)
                (n, aBuf.View, bBuf.View, elemBuf.View);
            accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(SimdShapeShortCircuitKernel)
                (n, qBuf.View, rBuf.View, scBuf.View);
            await accelerator.SynchronizeAsync();

            var sc = await scBuf.CopyToHostAsync<int>();
            var keys = await keyBuf.CopyToHostAsync<float>();
            var scattered = await scatterBuf.CopyToHostAsync<float>();
            var strided = await strideBuf.CopyToHostAsync<float>();
            var elem = await elemBuf.CopyToHostAsync<float>();
            // Scatter (a thread storing to an arbitrary computed index) is a documented capability gap on WebGL
            // (transform feedback writes each thread's OWN slot) - the library's own requirement gate says so.
            bool scatter = accelerator.Device.Satisfies(new AcceleratorRequirements { RequiresScatterStores = true });
            var bad = new System.Collections.Generic.Dictionary<string, (int count, string first)>();
            void Bad(string shape, string detail)
            {
                bad[shape] = bad.TryGetValue(shape, out var e) ? (e.count + 1, e.first) : (1, detail);
            }
            for (int i = 0; i < n; i++)
            {
                if (keys[i] != pairs[i].Key * 2f)
                    Bad("struct-field read (8-byte stride)", $"[{i}] = {keys[i]}, expected {pairs[i].Key * 2f}");
                if (scatter && scattered[pos[i]] != src[i] + 1f)
                    Bad("scatter through a position table", $"dst[{pos[i]}] = {scattered[pos[i]]}, expected {src[i] + 1f} (from src[{i}])");
                if (strided[i] != src[i * 2] - src[i * 2 + 1])
                    Bad("stride-2 read", $"[{i}] = {strided[i]}, expected {src[i * 2] - src[i * 2 + 1]}");
                int scWant = qv[i] + ((rv[i] > 10 || (rv[i] == 10 && (qv[i] & 1) == 1)) ? 1 : 0);
                if (sc[i] != scWant)
                    Bad("short-circuit join", $"[{i}] q={qv[i]} r={rv[i]} got {sc[i]}, expected {scWant}");
                if (elem[i] != src[i] * 2f + src[n + i])
                    Bad("elementwise control", $"[{i}] = {elem[i]}, expected {src[i] * 2f + src[n + i]}");
            }
            if (bad.Count > 0)
            {
                var sb = new System.Text.StringBuilder($"{bad.Count} access shape(s) wrong on {accelerator.AcceleratorType}:");
                foreach (var kv in bad) sb.Append($" [{kv.Key}: {kv.Value.count}/{n} wrong, first {kv.Value.first}]");
                throw new Exception(sb.ToString());
            }
        });
    }
}
