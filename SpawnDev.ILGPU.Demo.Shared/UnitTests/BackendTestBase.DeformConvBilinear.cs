using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // SpawnDev.ILGPU.ML's DeformConvImpl (ONNX DeformConv: three nested loops, a bilinear gather of four input corners per tap)
    // was wrong on WebGL whenever a "+1" corner carried weight - any fractional offset - and right everywhere else (Tuvok,
    // ML 5.8.7 release sweep, 2026-11-08). The generated GLSL reads correctly line by line, so this copy of the kernel takes a
    // PROBE: each thread outputs one intermediate value of one tap, compared with the CPU accelerator running the same kernel,
    // so the first wrong probe names the step a backend gets wrong.
    public abstract partial class BackendTestBase
    {
        // params: [inC, H, W, outC, outH, outW, kH, kW, sH, sW, pTop, pLeft, groups, offsetGroups, dH, dW, hasMask, hasBias,
        //          probeWhat, probeTap]
        static void DeformConvProbeKernel(Index1D idx, ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> weights, ArrayView1D<float, Stride1D.Dense> offsets,
            ArrayView1D<float, Stride1D.Dense> mask, ArrayView1D<float, Stride1D.Dense> bias,
            ArrayView1D<float, Stride1D.Dense> output, ArrayView1D<float, Stride1D.Dense> paramsArr)
        {
            int inC = (int)paramsArr[0]; int H = (int)paramsArr[1]; int W = (int)paramsArr[2];
            int outC = (int)paramsArr[3]; int outH = (int)paramsArr[4]; int outW = (int)paramsArr[5];
            int kH = (int)paramsArr[6]; int kW = (int)paramsArr[7];
            int sH = (int)paramsArr[8]; int sW = (int)paramsArr[9];
            int pH = (int)paramsArr[10]; int pW = (int)paramsArr[11];
            int groups = (int)paramsArr[12]; int offsetGroups = (int)paramsArr[13];
            int dH = (int)paramsArr[14]; int dW = (int)paramsArr[15];
            bool hasMask = paramsArr[16] != 0f; bool hasBias = paramsArr[17] != 0f;
            int probeWhat = (int)paramsArr[18]; int probeTap = (int)paramsArr[19];
            int tmp = idx;
            int ow = tmp % outW; tmp /= outW;
            int oh = tmp % outH; tmp /= outH;
            int oc = tmp % outC; int n = tmp / outC;
            int cPerGroup = inC / groups;
            int ocPerGroup = outC / groups;
            int g = oc / ocPerGroup;
            int cPerOffGrp = inC / offsetGroups;
            int hw = outH * outW;
            int kHW = kH * kW;
            float sum = 0f;
            float probe = 0f;
            int icEnd = (g + 1) * cPerGroup;
            for (int ic = g * cPerGroup; ic < icEnd; ic++)
            {
                int offGrp = ic / cPerOffGrp;
                int chOff = (n * inC + ic) * H;
                for (int kh = 0; kh < kH; kh++)
                {
                    for (int kw = 0; kw < kW; kw++)
                    {
                        int k = kh * kW + kw;
                        int offBase = ((n * offsetGroups + offGrp) * kHW + k) * 2 * hw + oh * outW + ow;
                        float fy = oh * sH - pH + kh * dH + offsets[offBase];
                        float fx = ow * sW - pW + kw * dW + offsets[offBase + hw];
                        float floorY = MathF.Floor(fy), floorX = MathF.Floor(fx);
                        int y0 = (int)floorY; int x0 = (int)floorX;
                        int y1 = y0 + 1; int x1 = x0 + 1;
                        float ty = fy - floorY; float tx = fx - floorX;
                        float fy0 = (y0 >= 0 && y0 < H) ? 1f : 0f, fy1 = (y1 >= 0 && y1 < H) ? 1f : 0f;
                        float fx0 = (x0 >= 0 && x0 < W) ? 1f : 0f, fx1 = (x1 >= 0 && x1 < W) ? 1f : 0f;
                        int cy0 = Math.Min(Math.Max(y0, 0), H - 1), cy1 = Math.Min(Math.Max(y1, 0), H - 1);
                        int cx0 = Math.Min(Math.Max(x0, 0), W - 1), cx1 = Math.Min(Math.Max(x1, 0), W - 1);
                        float raw10 = input[(chOff + cy1) * W + cx0];
                        float v00 = input[(chOff + cy0) * W + cx0] * (fy0 * fx0);
                        float v01 = input[(chOff + cy0) * W + cx1] * (fy0 * fx1);
                        float v10 = raw10 * (fy1 * fx0);
                        float v11 = input[(chOff + cy1) * W + cx1] * (fy1 * fx1);
                        float interp = v00 * (1f - ty) * (1f - tx) + v01 * (1f - ty) * tx
                            + v10 * ty * (1f - tx) + v11 * ty * tx;
                        if (hasMask) interp *= mask[((n * offsetGroups + offGrp) * kHW + k) * hw + oh * outW + ow];
                        int wIdx = ((oc * cPerGroup + (ic - g * cPerGroup)) * kH + kh) * kW + kw;
                        sum += interp * weights[wIdx];
                        if (ic == g * cPerGroup && k == probeTap)
                        {
                            probe = probeWhat switch
                            {
                                1 => fy,
                                2 => ty,
                                3 => (float)y1,
                                4 => fy1,
                                5 => (float)cy1,
                                6 => raw10,
                                7 => v10,
                                8 => interp,
                                9 => fx,
                                10 => tx,
                                11 => v00,
                                _ => sum,
                            };
                        }
                    }
                }
            }
            if (hasBias) sum += bias[oc];
            output[idx] = probeWhat == 0 ? sum : probe;
        }

        static readonly string[] DeformProbeNames =
            { "sum", "fy", "ty", "y1", "fy1", "cy1", "raw input[cy1][cx0]", "v10", "interp", "fx", "tx", "v00" };

        /// <summary>
        /// A DeformConv-shaped kernel (three nested loops, four bilinear corner loads a tap) with fractional offsets
        /// matches the CPU accelerator on every backend - the final sum and each intermediate value of every tap.
        /// </summary>
        [TestMethod]
        public async Task DeformConv_FractionalOffsets_BilinearCorners_MatchCpu() => await RunTest(async accelerator =>
        {
            const int H = 5, W = 5, K = 3, outC = 2, inC = 2;
            int outH = H, outW = W, hw = outH * outW, outLen = outC * hw;
            var rng = new Random(7);
            var x = new float[inC * H * W];
            for (int i = 0; i < x.Length; i++) x[i] = (float)(rng.NextDouble() * 4 - 2);
            var wt = new float[outC * inC * K * K];
            for (int i = 0; i < wt.Length; i++) wt[i] = (float)(rng.NextDouble() * 2 - 1);
            // Offsets [1, 2 * K * K, outH, outW]: dy = 0.5 everywhere, dx = 0 (Tuvok's first failing configuration),
            // then random fractional dy and dx.
            var offA = new float[2 * K * K * hw];
            for (int t = 0; t < K * K; t++) for (int p = 0; p < hw; p++) offA[(t * 2) * hw + p] = 0.5f;
            var offB = new float[2 * K * K * hw];
            for (int i = 0; i < offB.Length; i++) offB[i] = (float)(rng.NextDouble() * 1.6 - 0.8);

            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);

            async Task<float[]> Run(Accelerator acc, float[] off, int probeWhat, int probeTap)
            {
                var prm = new float[] { inC, H, W, outC, outH, outW, K, K, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, probeWhat, probeTap };
                using var xb = acc.Allocate1D(x);
                using var wb = acc.Allocate1D(wt);
                using var ob = acc.Allocate1D(off);
                using var mb = acc.Allocate1D(new float[] { 0f });
                using var bb = acc.Allocate1D(new float[] { 0f, 0f });
                using var yb = acc.Allocate1D<float>(outLen);
                using var pb = acc.Allocate1D(prm);
                var kernel = acc.LoadAutoGroupedStreamKernel<Index1D,
                    ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                    ArrayView1D<float, Stride1D.Dense>>(DeformConvProbeKernel);
                kernel((Index1D)outLen, xb.View, wb.View, ob.View, mb.View, bb.View, yb.View, pb.View);
                await acc.SynchronizeAsync();
                return await yb.CopyToHostAsync<float>();
            }

            var failures = new System.Collections.Generic.List<string>();
            foreach (var (label, off) in new[] { ("dy=0.5 dx=0", offA), ("random fractional", offB) })
            {
                for (int what = 0; what < DeformProbeNames.Length; what++)
                {
                    foreach (int tap in what == 0 ? new[] { 0 } : new[] { 0, 4, 8 })
                    {
                        var got = await Run(accelerator, off, what, tap);
                        var want = await Run(cpu, off, what, tap);
                        int bad = 0; string first = "";
                        for (int i = 0; i < outLen; i++)
                        {
                            float e = want[i];
                            if (MathF.Abs(got[i] - e) <= MathF.Abs(e) * 1e-5f + 1e-5f) continue;
                            if (bad++ == 0) first = $"[{i}] cpu {e:R} got {got[i]:R}";
                        }
                        if (bad > 0) failures.Add($"{label}, {DeformProbeNames[what]} tap {tap}: {bad}/{outLen} differ, first {first}");
                    }
                }
            }
            if (failures.Count > 0)
            {
                // The GLSL the WebGL accelerator actually compiled, around each floor().
                string glsl = "";
                if (accelerator is global::SpawnDev.ILGPU.WebGL.WebGLAccelerator && global::SpawnDev.ILGPU.WebGL.WebGLAccelerator.LastGeneratedGLSL is string src)
                {
                    var lines = src.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                        if (lines[i].Contains("floor"))
                            glsl += "\n  GLSL " + string.Join("\n  GLSL ", lines[Math.Max(0, i - 3)..Math.Min(lines.Length, i + 9)]);
                }
                throw new Exception($"{failures.Count} probes differ from the CPU accelerator:\n  " + string.Join("\n  ", failures) + glsl);
            }
        });
    }
}
