using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Bisect for DeformConv_FractionalOffsets_BilinearCorners_MatchCpu on WebGL: ty = fy - floor(fy) came out 0 there while fy
    // itself was right, with correct GLSL (v = floor(x); t = x - v). V1 keeps the loop nest and floors by hand; V2 keeps floor()
    // but drops the loops. Each writes ty of tap 0 for every output element.
    public abstract partial class BackendTestBase
    {
        static float DeformManualFloor(float x)
        {
            int t = (int)x;
            return x < t ? t - 1 : t;
        }

        // V1: the loop nest, manual floor.
        static void DeformBisectLoopsManualFloor(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int outW = (int)paramsArr[0], outH = (int)paramsArr[1], kH = (int)paramsArr[2], kW = (int)paramsArr[3], icEnd = (int)paramsArr[4];
            int ow = idx % outW, oh = (idx / outW) % outH, hw = outW * outH;
            float probe = 0f;
            for (int ic = 0; ic < icEnd; ic++)
                for (int kh = 0; kh < kH; kh++)
                    for (int kw = 0; kw < kW; kw++)
                    {
                        int k = kh * kW + kw;
                        float fy = oh - 1 + kh + offsets[k * 2 * hw + oh * outW + ow];
                        float floorY = DeformManualFloor(fy);
                        float ty = fy - floorY;
                        if (ic == 0 && k == 0) probe = ty;
                    }
            output[idx] = probe;
        }

        // V1b: the loop nest, MathF.Floor (the failing form, minimized).
        static void DeformBisectLoopsFloor(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int outW = (int)paramsArr[0], outH = (int)paramsArr[1], kH = (int)paramsArr[2], kW = (int)paramsArr[3], icEnd = (int)paramsArr[4];
            int ow = idx % outW, oh = (idx / outW) % outH, hw = outW * outH;
            float probe = 0f;
            for (int ic = 0; ic < icEnd; ic++)
                for (int kh = 0; kh < kH; kh++)
                    for (int kw = 0; kw < kW; kw++)
                    {
                        int k = kh * kW + kw;
                        float fy = oh - 1 + kh + offsets[k * 2 * hw + oh * outW + ow];
                        float floorY = MathF.Floor(fy);
                        float ty = fy - floorY;
                        if (ic == 0 && k == 0) probe = ty;
                    }
            output[idx] = probe;
        }

        // V2: no loops, MathF.Floor.
        static void DeformBisectNoLoopsFloor(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int outW = (int)paramsArr[0], outH = (int)paramsArr[1];
            int ow = idx % outW, oh = (idx / outW) % outH;
            float fy = oh - 1 + offsets[oh * outW + ow];
            float floorY = MathF.Floor(fy);
            output[idx] = fy - floorY;
        }

        // Structure variants: where does a value assigned under a condition inside a loop nest get lost?
        static void BisectOneLoopCond(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets, ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int n = (int)paramsArr[2] * (int)paramsArr[3];
            float probe = 0f;
            for (int k = 0; k < n; k++) { float v = offsets[k] + idx; if (k == 0) probe = v; }
            output[idx] = probe;
        }
        static void BisectTwoLoopsCond(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets, ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int kH = (int)paramsArr[2], kW = (int)paramsArr[3];
            float probe = 0f;
            for (int kh = 0; kh < kH; kh++) for (int kw = 0; kw < kW; kw++) { float v = offsets[kh * kW + kw] + idx; if (kh * kW + kw == 0) probe = v; }
            output[idx] = probe;
        }
        static void BisectThreeLoopsOneCond(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets, ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int kH = (int)paramsArr[2], kW = (int)paramsArr[3], icEnd = (int)paramsArr[4];
            float probe = 0f;
            for (int ic = 0; ic < icEnd; ic++) for (int kh = 0; kh < kH; kh++) for (int kw = 0; kw < kW; kw++) { float v = offsets[kh * kW + kw] + idx; if (kh * kW + kw == 0) probe = v; }
            output[idx] = probe;
        }
        static void BisectThreeLoopsTwoConds(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets, ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int kH = (int)paramsArr[2], kW = (int)paramsArr[3], icEnd = (int)paramsArr[4];
            float probe = 0f;
            for (int ic = 0; ic < icEnd; ic++) for (int kh = 0; kh < kH; kh++) for (int kw = 0; kw < kW; kw++) { float v = offsets[kh * kW + kw] + idx; if (ic == 0 && kh * kW + kw == 0) probe = v; }
            output[idx] = probe;
        }
        static void BisectThreeLoopsArithmetic(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets, ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int kH = (int)paramsArr[2], kW = (int)paramsArr[3], icEnd = (int)paramsArr[4];
            float probe = 0f;
            for (int ic = 0; ic < icEnd; ic++) for (int kh = 0; kh < kH; kh++) for (int kw = 0; kw < kW; kw++) { float v = offsets[kh * kW + kw] + idx; probe += v * (1 - Math.Min(1, ic + kh * kW + kw)); }
            output[idx] = probe;
        }

        // V1 decomposed: the same loop nest and fy, probing (paramsArr[5]) 0 = raw offsets load, 1 = fy, 2 = MathF.Floor(fy),
        // 3 = fy - MathF.Floor(fy), 4 = (float)(int)fy.
        static void BisectV1Decomposed(Index1D idx, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int outW = (int)paramsArr[0], outH = (int)paramsArr[1], kH = (int)paramsArr[2], kW = (int)paramsArr[3], icEnd = (int)paramsArr[4];
            int what = (int)paramsArr[5];
            int ow = idx % outW, oh = (idx / outW) % outH, hw = outW * outH;
            float probe = 0f;
            for (int ic = 0; ic < icEnd; ic++)
                for (int kh = 0; kh < kH; kh++)
                    for (int kw = 0; kw < kW; kw++)
                    {
                        int k = kh * kW + kw;
                        float raw = offsets[k * 2 * hw + oh * outW + ow];
                        float fy = oh - 1 + kh + raw;
                        float fl = MathF.Floor(fy);
                        float value = what == 0 ? raw : what == 1 ? fy : what == 2 ? fl : what == 3 ? fy - fl : (float)(int)fy;
                        if (ic == 0 && k == 0) probe = value;
                    }
            output[idx] = probe;
        }

        [TestMethod]
        public async Task DeformConv_Bisect_V1Decomposed_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int outW = 5, outH = 5, K = 3, hw = outW * outH;
            var off = new float[2 * K * K * hw];
            for (int t = 0; t < K * K; t++) for (int p = 0; p < hw; p++) off[(t * 2) * hw + p] = 0.5f;
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            async Task<float[]> Run(Accelerator acc, int what)
            {
                using var ob = acc.Allocate1D(off);
                using var yb = acc.Allocate1D<float>(hw);
                using var pb = acc.Allocate1D(new float[] { outW, outH, K, K, 2, what });
                var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>>(BisectV1Decomposed);
                kernel((Index1D)hw, ob.View, yb.View, pb.View);
                await acc.SynchronizeAsync();
                return await yb.CopyToHostAsync<float>();
            }
            var names = new[] { "raw offsets load", "fy", "floor(fy)", "fy - floor(fy)", "(float)(int)fy" };
            string results = ""; bool anyBad = false;
            for (int what = 0; what < names.Length; what++)
            {
                var got = await Run(accelerator, what);
                var want = await Run(cpu, what);
                int bad = 0; string first = "";
                for (int i = 0; i < hw; i++) if (got[i] != want[i] && bad++ == 0) first = $" [{i}] got {got[i]:R} cpu {want[i]:R}";
                results += $"\n  {names[what]}: {bad}/{hw} differ{first}";
                anyBad |= bad > 0;
            }
            if (anyBad) throw new Exception("V1 decomposed:" + results);
        });

        [TestMethod]
        public async Task DeformConv_Bisect_FloorInLoopNest_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int outW = 5, outH = 5, K = 3, hw = outW * outH;
            var off = new float[2 * K * K * hw];
            for (int t = 0; t < K * K; t++) for (int p = 0; p < hw; p++) off[(t * 2) * hw + p] = 0.5f;
            var prm = new float[] { outW, outH, K, K, 2 };
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);

            async Task<float[]> Run(Accelerator acc, Action<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>> body)
            {
                using var ob = acc.Allocate1D(off);
                using var yb = acc.Allocate1D<float>(hw);
                using var pb = acc.Allocate1D(prm);
                var kernel = acc.LoadAutoGroupedStreamKernel<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>>(body);
                kernel((Index1D)hw, ob.View, yb.View, pb.View);
                await acc.SynchronizeAsync();
                return await yb.CopyToHostAsync<float>();
            }

            var results = "";
            bool anyBad = false;
            foreach (var (name, body) in new (string, Action<Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>>)[]
                { ("V1 loops + manual floor", DeformBisectLoopsManualFloor), ("V1b loops + MathF.Floor", DeformBisectLoopsFloor), ("V2 no loops + MathF.Floor", DeformBisectNoLoopsFloor),
                  ("1 loop, if (k == 0)", BisectOneLoopCond), ("2 loops, if (k == 0)", BisectTwoLoopsCond), ("3 loops, if (k == 0)", BisectThreeLoopsOneCond),
                  ("3 loops, if (ic == 0 && k == 0)", BisectThreeLoopsTwoConds), ("3 loops, arithmetic select", BisectThreeLoopsArithmetic) })
            {
                var got = await Run(accelerator, body);
                var want = await Run(cpu, body);
                int bad = 0;
                for (int i = 0; i < hw; i++) if (got[i] != want[i]) bad++;
                results += $"\n  {name}: {bad}/{hw} differ (got[0] {got[0]:R}, cpu {want[0]:R})";
                anyBad |= bad > 0;
            }
            if (anyBad) throw new Exception("floor bisect:" + results);
        });
    }
}
