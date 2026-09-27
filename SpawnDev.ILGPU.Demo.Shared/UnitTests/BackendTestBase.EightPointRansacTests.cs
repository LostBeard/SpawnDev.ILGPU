using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// SpawnScene's GPU pair verification kernels (GpuEpipolarRansac), copied verbatim with a Ransac8 prefix: one thread
/// per (pair, hypothesis) draws 8 matches from a counter hash, solves the Hartley-normalised 8-point system in f32
/// by complete-pivot elimination over LocalMemory scratch (with `return false` from inside its loops), enforces
/// rank 2 with a 3x3 Jacobi, and scores every match by Sampson distance; a second kernel picks the best hypothesis.
///
/// Why here: on 2026-09-27 this kernel produced invalid WGSL (view-param helper fn-defs, a LocalMemory cast typed
/// f32) and, once it compiled, exercised the multi-level early exits the structured WGSL/GLSL walkers mis-emitted.
/// A real production kernel of that shape, checked for recall on synthetic two-view geometry and for completing.
/// </summary>
public abstract partial class BackendTestBase
{
    /// <summary>Thread t = (pair, hypothesis): build hypothesis h of the pair and count its inliers (-1: degenerate).</summary>
    static void Ransac8ScoreKernel(Index1D t, ArrayView<float> pts, ArrayView<int> offsets, ArrayView<int> counts,
        ArrayView<int> seeds, int hypotheses, float th2, ArrayView<int> scores)
    {
        int p = t / hypotheses;
        int h = t - p * hypotheses;
        int n = counts[p];
        int off = offsets[p];
        var a = LocalMemory.Allocate<float>(72);
        var perm = LocalMemory.Allocate<int>(9);
        var sample = LocalMemory.Allocate<int>(8);
        var f = LocalMemory.Allocate<float>(9);
        var m = LocalMemory.Allocate<float>(9);
        var v = LocalMemory.Allocate<float>(9);
        int result = -1;
        if (Ransac8Hypothesis(pts, off, n, seeds[p], h, a, perm, sample, f, m, v))
        {
            result = 0;
            for (int i = 0; i < n; i++)
                if (Ransac8Sampson2(f, pts, (off + i) * 4) <= th2) result++;
        }
        scores[t] = result;
    }

    /// <summary>Thread = pair: the first hypothesis with the most inliers, rebuilt and written out (best -1: none).</summary>
    static void Ransac8PickKernel(Index1D p, ArrayView<float> pts, ArrayView<int> offsets, ArrayView<int> counts,
        ArrayView<int> seeds, ArrayView<int> scores, int hypotheses, ArrayView<float> outF, ArrayView<int> outBest)
    {
        int bestH = -1, best = -1;
        int b0 = p * hypotheses;
        for (int h = 0; h < hypotheses; h++)
        {
            int s = scores[b0 + h];
            if (s > best) { best = s; bestH = h; }
        }
        var a = LocalMemory.Allocate<float>(72);
        var perm = LocalMemory.Allocate<int>(9);
        var sample = LocalMemory.Allocate<int>(8);
        var f = LocalMemory.Allocate<float>(9);
        var m = LocalMemory.Allocate<float>(9);
        var v = LocalMemory.Allocate<float>(9);
        if (best < 0 || !Ransac8Hypothesis(pts, offsets[p], counts[p], seeds[p], bestH, a, perm, sample, f, m, v))
            best = -1;
        for (int j = 0; j < 9; j++) outF[p * 9 + j] = best < 0 ? 0f : f[j];
        outBest[p] = best;
    }

    /// <summary>Counter-based hash (lowbias32): sample draws are a pure function of (seed, hypothesis, draw).</summary>
    static uint Ransac8Hash(uint x)
    {
        x ^= x >> 16; x *= 0x7feb352du;
        x ^= x >> 15; x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }

    /// <summary>Squared Sampson distance of the match at pts[o..o+3] to F (pixels^2), as EpipolarRansac.Sampson2.</summary>
    static float Ransac8Sampson2(ArrayView<float> f, ArrayView<float> pts, int o)
    {
        float x1 = pts[o], y1 = pts[o + 1], x2 = pts[o + 2], y2 = pts[o + 3];
        float fx0 = f[0] * x1 + f[1] * y1 + f[2];
        float fx1 = f[3] * x1 + f[4] * y1 + f[5];
        float fx2 = f[6] * x1 + f[7] * y1 + f[8];
        float ftx0 = f[0] * x2 + f[3] * y2 + f[6];
        float ftx1 = f[1] * x2 + f[4] * y2 + f[7];
        float e = x2 * fx0 + y2 * fx1 + fx2;
        float d = fx0 * fx0 + fx1 * fx1 + ftx0 * ftx0 + ftx1 * ftx1;
        return d <= 1e-30f ? float.MaxValue : e * e / d;
    }

    /// <summary>
    /// Hypothesis h of a pair: 8 distinct matches drawn from the hash, normalised 8-point, rank 2 enforced, F in
    /// pixels written to <paramref name="f"/>. False when the sample or the system is degenerate.
    /// </summary>
    static bool Ransac8Hypothesis(ArrayView<float> pts, int off, int n, int seed, int h,
        ArrayView<float> a, ArrayView<int> perm, ArrayView<int> sample, ArrayView<float> f,
        ArrayView<float> m, ArrayView<float> v)
    {
        // 8 distinct indices by rejection; bounded draws.
        uint baseKey = Ransac8Hash((uint)seed * 0x9E3779B9u + Ransac8Hash((uint)h));
        int got = 0;
        for (int draw = 0; draw < 128 && got < 8; draw++)
        {
            int s = (int)(Ransac8Hash(baseKey + (uint)draw * 0x632BE5ABu) % (uint)n);
            bool dup = false;
            for (int k = 0; k < got; k++) if (sample[k] == s) dup = true;
            if (!dup) { sample[got] = s; got++; }
        }
        if (got < 8) return false;

        // Hartley normalisation per image over the sample.
        float ca0 = 0, ca1 = 0, cb0 = 0, cb1 = 0;
        for (int k = 0; k < 8; k++)
        {
            int o = (off + sample[k]) * 4;
            ca0 += pts[o]; ca1 += pts[o + 1]; cb0 += pts[o + 2]; cb1 += pts[o + 3];
        }
        ca0 *= 0.125f; ca1 *= 0.125f; cb0 *= 0.125f; cb1 *= 0.125f;
        float da = 0, db = 0;
        for (int k = 0; k < 8; k++)
        {
            int o = (off + sample[k]) * 4;
            float ax = pts[o] - ca0, ay = pts[o + 1] - ca1, bx = pts[o + 2] - cb0, by = pts[o + 3] - cb1;
            da += XMath.Sqrt(ax * ax + ay * ay);
            db += XMath.Sqrt(bx * bx + by * by);
        }
        da *= 0.125f; db *= 0.125f;
        if (da < 1e-6f || db < 1e-6f) return false;
        float sa = 1.41421356f / da, sb = 1.41421356f / db;

        // A (8x9): row = [x2x1, x2y1, x2, y2x1, y2y1, y2, x1, y1, 1] in normalised coordinates.
        for (int k = 0; k < 8; k++)
        {
            int o = (off + sample[k]) * 4;
            float x1 = (pts[o] - ca0) * sa, y1 = (pts[o + 1] - ca1) * sa;
            float x2 = (pts[o + 2] - cb0) * sb, y2 = (pts[o + 3] - cb1) * sb;
            int r = k * 9;
            a[r] = x2 * x1; a[r + 1] = x2 * y1; a[r + 2] = x2;
            a[r + 3] = y2 * x1; a[r + 4] = y2 * y1; a[r + 5] = y2;
            a[r + 6] = x1; a[r + 7] = y1; a[r + 8] = 1f;
        }
        for (int c = 0; c < 9; c++) perm[c] = c;

        // Null space by Gaussian elimination with complete pivoting: 8 pivots, the column left over is free.
        for (int r = 0; r < 8; r++)
        {
            int pr = r, pc = r;
            float pv = 0;
            for (int i = r; i < 8; i++)
                for (int c = r; c < 9; c++)
                {
                    float x = XMath.Abs(a[i * 9 + c]);
                    if (x > pv) { pv = x; pr = i; pc = c; }
                }
            if (pv < 1e-7f) return false;   // rank < 8: the sample does not fix F
            if (pr != r)
                for (int c = 0; c < 9; c++) { float tmp = a[r * 9 + c]; a[r * 9 + c] = a[pr * 9 + c]; a[pr * 9 + c] = tmp; }
            if (pc != r)
            {
                for (int i = 0; i < 8; i++) { float tmp = a[i * 9 + r]; a[i * 9 + r] = a[i * 9 + pc]; a[i * 9 + pc] = tmp; }
                int tp = perm[r]; perm[r] = perm[pc]; perm[pc] = tp;
            }
            float inv = 1f / a[r * 9 + r];
            for (int i = r + 1; i < 8; i++)
            {
                float fac = a[i * 9 + r] * inv;
                if (fac == 0f) continue;
                for (int c = r; c < 9; c++) a[i * 9 + c] -= fac * a[r * 9 + c];
            }
        }
        // Back substitution with the free (permuted) column 8 = 1; m holds the solution in permuted order.
        m[8] = 1f;
        for (int r = 7; r >= 0; r--)
        {
            float acc = 0;
            for (int c = r + 1; c < 9; c++) acc += a[r * 9 + c] * m[c];
            m[r] = -acc / a[r * 9 + r];
        }
        float norm = 0;
        for (int c = 0; c < 9; c++) norm += m[c] * m[c];
        norm = XMath.Rsqrt(norm);
        for (int c = 0; c < 9; c++) f[perm[c]] = m[c] * norm;   // fn, normalised F

        // Rank 2: F2 = F (I - v v^T), v = eigenvector of F^T F with the smallest eigenvalue (3x3 cyclic Jacobi).
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                m[r * 3 + c] = f[r] * f[c] + f[3 + r] * f[3 + c] + f[6 + r] * f[6 + c];
        for (int i = 0; i < 9; i++) v[i] = 0f;
        v[0] = 1f; v[4] = 1f; v[8] = 1f;
        for (int sweep = 0; sweep < 12; sweep++)
        {
            float off2 = m[1] * m[1] + m[2] * m[2] + m[5] * m[5];
            if (off2 < 1e-20f) break;
            for (int pq = 0; pq < 3; pq++)
            {
                int pp = pq == 2 ? 1 : 0;
                int qq = pq == 0 ? 1 : 2;
                float apq = m[pp * 3 + qq];
                if (XMath.Abs(apq) < 1e-30f) continue;
                float theta = (m[qq * 3 + qq] - m[pp * 3 + pp]) / (2f * apq);
                float tt = (theta >= 0 ? 1f : -1f) / (XMath.Abs(theta) + XMath.Sqrt(theta * theta + 1f));
                float cs = XMath.Rsqrt(tt * tt + 1f), sn = tt * cs;
                for (int k = 0; k < 3; k++)
                {
                    float akp = m[k * 3 + pp], akq = m[k * 3 + qq];
                    m[k * 3 + pp] = cs * akp - sn * akq; m[k * 3 + qq] = sn * akp + cs * akq;
                }
                for (int k = 0; k < 3; k++)
                {
                    float apk = m[pp * 3 + k], aqk = m[qq * 3 + k];
                    m[pp * 3 + k] = cs * apk - sn * aqk; m[qq * 3 + k] = sn * apk + cs * aqk;
                }
                for (int k = 0; k < 3; k++)
                {
                    float vkp = v[k * 3 + pp], vkq = v[k * 3 + qq];
                    v[k * 3 + pp] = cs * vkp - sn * vkq; v[k * 3 + qq] = sn * vkp + cs * vkq;
                }
            }
        }
        int z = 0;
        if (m[4] < m[0]) z = 1;
        if (m[8] < m[z * 4]) z = 2;
        float z0 = v[z], z1 = v[3 + z], z2 = v[6 + z];
        // m <- F2 = F - (F v) v^T
        for (int r = 0; r < 3; r++)
        {
            float fv = f[r * 3] * z0 + f[r * 3 + 1] * z1 + f[r * 3 + 2] * z2;
            m[r * 3] = f[r * 3] - fv * z0;
            m[r * 3 + 1] = f[r * 3 + 1] - fv * z1;
            m[r * 3 + 2] = f[r * 3 + 2] - fv * z2;
        }
        // Denormalise: F = Tb^T F2 Ta, T = [[s,0,-s cx],[0,s,-s cy],[0,0,1]].
        // F2 Ta: columns 0,1 scale by s_a; column 2 = -s_a (cx col0 + cy col1) + col2.
        for (int r = 0; r < 3; r++)
        {
            float c0 = m[r * 3], c1 = m[r * 3 + 1], c2 = m[r * 3 + 2];
            m[r * 3] = sa * c0;
            m[r * 3 + 1] = sa * c1;
            m[r * 3 + 2] = c2 - sa * (ca0 * c0 + ca1 * c1);
        }
        // Tb^T (F2 Ta): rows 0,1 scale by s_b; row 2 = -s_b (cx row0 + cy row1) + row2.
        for (int c = 0; c < 3; c++)
        {
            float r0 = m[c], r1 = m[3 + c], r2 = m[6 + c];
            f[c] = sb * r0;
            f[3 + c] = sb * r1;
            f[6 + c] = r2 - sb * (cb0 * r0 + cb1 * r1);
        }
        for (int i = 0; i < 9; i++) if (!(XMath.Abs(f[i]) < float.MaxValue)) return false;
        return true;
    }


    /// <summary>Pinhole projection of p for a camera at c looking at the origin-ish target (row-major R).</summary>
    static bool Ransac8Project(double[] r, double[] c, double f, double[] p, out float u, out float v)
    {
        double dx = p[0] - c[0], dy = p[1] - c[1], dz = p[2] - c[2];
        double x = r[0] * dx + r[1] * dy + r[2] * dz, y = r[3] * dx + r[4] * dy + r[5] * dz, z = r[6] * dx + r[7] * dy + r[8] * dz;
        u = 0; v = 0;
        if (z < 0.1) return false;
        u = (float)(f * x / z + 489.5); v = (float)(f * y / z + 273.0);
        return u >= 0 && v >= 0 && u < 979 && v < 546;
    }

    static double[] Ransac8LookAt(double[] c, double[] target)
    {
        double fx = target[0] - c[0], fy = target[1] - c[1], fz = target[2] - c[2];
        double fl = Math.Sqrt(fx * fx + fy * fy + fz * fz); fx /= fl; fy /= fl; fz /= fl;
        // right = forward x up(0,1,0)... using down = +y image axis convention
        double rx = -fz, ry = 0, rz = fx; double rl = Math.Sqrt(rx * rx + rz * rz); rx /= rl; rz /= rl;
        double dx = fy * rz - fz * ry, dy = fz * rx - fx * rz, dz = fx * ry - fy * rx;   // down = forward x right
        return new[] { rx, ry, rz, dx, dy, dz, fx, fy, fz };
    }

    [TestMethod]
    public async Task HelperCodegen_EightPointRansac_SpawnSceneKernel() => await RunTest(async accelerator =>
    {
        const int hypotheses = 1000;
        const float th2 = 4f;   // 2 px
        var rng = new Random(20260927);
        var cams = new List<(double[] R, double[] C)>();
        for (int i = 0; i < 6; i++)
        {
            double a = i * 0.18;
            var c = new[] { 8 * Math.Sin(a), 1.0, 8 * Math.Cos(a) };
            cams.Add((Ransac8LookAt(c, new[] { 0.0, 0.5, 0.0 }), c));
        }
        var pts = new List<float>(); var offsets = new List<int>(); var counts = new List<int>(); var seeds = new List<int>();
        var truth = new List<int>(); var isNoise = new List<bool>();
        int pairIndex = 0;
        for (int i = 0; i < cams.Count; i++)
            for (int j = i + 1; j < cams.Count; j++)
                foreach (double outFrac in new[] { 0.1, 0.4 })
                {
                    int inl = 40 + rng.Next(160), outl = (int)(inl * outFrac / (1 - outFrac));
                    offsets.Add(pts.Count / 4);
                    int n = 0;
                    while (n < inl)
                    {
                        var p = new[] { rng.NextDouble() * 6 - 3, rng.NextDouble() * 3 - 1, rng.NextDouble() * 3 - 1.5 };
                        if (!Ransac8Project(cams[i].R, cams[i].C, 800, p, out var u1, out var v1)) continue;
                        if (!Ransac8Project(cams[j].R, cams[j].C, 800, p, out var u2, out var v2)) continue;
                        pts.Add(u1 + (float)(rng.NextDouble() - 0.5)); pts.Add(v1 + (float)(rng.NextDouble() - 0.5));
                        pts.Add(u2 + (float)(rng.NextDouble() - 0.5)); pts.Add(v2 + (float)(rng.NextDouble() - 0.5));
                        n++;
                    }
                    for (int k = 0; k < outl; k++)
                    {
                        pts.Add((float)(rng.NextDouble() * 979)); pts.Add((float)(rng.NextDouble() * 546));
                        pts.Add((float)(rng.NextDouble() * 979)); pts.Add((float)(rng.NextDouble() * 546));
                    }
                    counts.Add(inl + outl); seeds.Add(pairIndex++ * 7919); truth.Add(inl); isNoise.Add(false);
                }
        for (int k = 0; k < 8; k++)   // pure noise at SpawnScene's measured floor: must not verify
        {
            offsets.Add(pts.Count / 4);
            for (int m = 0; m < 34 * 4; m++) pts.Add((float)(rng.NextDouble() * (m % 2 == 0 ? 979 : 546)));
            counts.Add(34); seeds.Add(pairIndex++ * 7919); truth.Add(0); isNoise.Add(true);
        }
        int pairCount = counts.Count;

        using var dPts = accelerator.Allocate1D(pts.ToArray());
        using var dOffsets = accelerator.Allocate1D(offsets.ToArray());
        using var dCounts = accelerator.Allocate1D(counts.ToArray());
        using var dSeeds = accelerator.Allocate1D(seeds.ToArray());
        using var dScores = accelerator.Allocate1D<int>((long)pairCount * hypotheses);
        using var dF = accelerator.Allocate1D<float>(pairCount * 9);
        using var dBest = accelerator.Allocate1D<int>(pairCount);
        var score = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<int>, ArrayView<int>,
            ArrayView<int>, int, float, ArrayView<int>>(Ransac8ScoreKernel);
        var pick = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<int>, ArrayView<int>,
            ArrayView<int>, ArrayView<int>, int, ArrayView<float>, ArrayView<int>>(Ransac8PickKernel);
        // Batched exactly as SpawnScene's GpuEpipolarRansac dispatches it: consecutive pairs per batch through
        // SubViews with NONZERO offsets, a different thread count per batch, one completed submission each.
        int[] batchSizes = { 5, 11, 3, 17 };
        int start = 0, bi = 0;
        while (start < pairCount)
        {
            int count = Math.Min(batchSizes[bi++ % batchSizes.Length], pairCount - start);
            score((Index1D)(count * hypotheses), dPts.View, dOffsets.View.SubView(start, count), dCounts.View.SubView(start, count),
                dSeeds.View.SubView(start, count), hypotheses, th2, dScores.View);
            pick((Index1D)count, dPts.View, dOffsets.View.SubView(start, count), dCounts.View.SubView(start, count),
                dSeeds.View.SubView(start, count), dScores.View, hypotheses, dF.View.SubView(start * 9L, count * 9L),
                dBest.View.SubView(start, count));
            await accelerator.SynchronizeAsync();
            start += count;
        }
        var best = await dBest.CopyToHostAsync<int>();
        for (int p = 0; p < pairCount; p++)
        {
            if (isNoise[p])
            {
                if (best[p] >= 15) throw new Exception($"noise pair {p}: best hypothesis has {best[p]} inliers (chance matches must not verify)");
            }
            else if (!(best[p] >= truth[p] * 0.9))
                throw new Exception($"pair {p}: best hypothesis {best[p]} inliers of {counts[p]}, expected >= 90% of {truth[p]} true");
        }
    });
}
