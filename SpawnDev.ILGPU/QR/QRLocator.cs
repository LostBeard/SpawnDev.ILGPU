namespace SpawnDev.ILGPU.QR;

/// <summary>A finder (or alignment) pattern candidate found in the binary image.</summary>
/// <param name="X">Centre, image pixels.</param>
/// <param name="Y">Centre, image pixels.</param>
/// <param name="ModuleSize">Measured module size along the scan line, pixels.</param>
/// <param name="Count">How many independent confirmations were merged into this candidate. A real pattern is confirmed on
/// several rows; a few data modules that merely look like one are confirmed once.</param>
internal sealed record FinderPattern(double X, double Y, double ModuleSize, int Count = 1)
{
    /// <summary>The running average of this candidate and one more confirmation.</summary>
    public FinderPattern Combine(double x, double y, double size)
    {
        int n = Count + 1;
        return new FinderPattern((Count * X + x) / n, (Count * Y + y) / n, (Count * ModuleSize + size) / n, n);
    }

    /// <summary>Whether a new confirmation is the same pattern: close in position and of similar size.</summary>
    public bool AboutEquals(double size, double y, double x)
    {
        if (Math.Abs(y - Y) > size || Math.Abs(x - X) > size) return false;
        return Math.Abs(size - ModuleSize) <= Math.Max(1.0, 0.5 * ModuleSize);
    }
}

/// <summary>
/// Locates a QR code in a binary image: the three finder patterns, the module size, the alignment pattern, and the
/// perspective transform from module coordinates to image pixels. The finder search is rotation-invariant (a 1:1:3:1:1
/// run ratio holds along any line through the centre of concentric squares) and the sampling transform is a full
/// projective mapping, so a tilted or obliquely viewed code reads.
/// </summary>
internal static class QRLocator
{
    #region Finder patterns

    /// <summary>
    /// Every finder pattern in the image, found by scanning rows for dark-light-dark-light-dark runs in the ratio
    /// 1:1:3:1:1 and confirming each hit by walking the vertical, horizontal and diagonal through its centre.
    /// </summary>
    public static List<FinderPattern> FindFinders(bool[] binary, int width, int height)
    {
        var found = new List<FinderPattern>();
        var counts = new int[5];
        for (int y = 0; y < height; y++)
        {
            Array.Clear(counts);
            int state = 0; // even = counting a dark run, odd = a light run
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                if (binary[row + x])
                {
                    if ((state & 1) == 1) state++;
                    counts[state]++;
                }
                else if ((state & 1) == 0)
                {
                    if (state == 4)
                    {
                        if (LooksLikeFinder(counts) && ConfirmFinder(binary, width, height, counts, y, x, found))
                        {
                            Array.Clear(counts);
                            state = 0;
                        }
                        else
                        {
                            ShiftCounts(counts);
                            state = 3;
                        }
                    }
                    else
                    {
                        counts[++state]++;
                    }
                }
                else
                {
                    counts[state]++;
                }
            }
            // A pattern that ends exactly at the right edge of the row.
            if (state == 4 && LooksLikeFinder(counts))
                ConfirmFinder(binary, width, height, counts, y, width, found);
        }
        return found;
    }

    /// <summary>Drop the first two runs: the pattern restarts at what was the second dark run.</summary>
    private static void ShiftCounts(int[] counts)
    {
        counts[0] = counts[2];
        counts[1] = counts[3];
        counts[2] = counts[4];
        counts[3] = 1;
        counts[4] = 0;
    }

    /// <summary>Five runs in the ratio 1:1:3:1:1, each within half a module of its ideal.</summary>
    private static bool LooksLikeFinder(int[] counts)
    {
        int total = 0;
        for (int i = 0; i < 5; i++)
        {
            if (counts[i] == 0) return false;
            total += counts[i];
        }
        if (total < 7) return false;
        double module = total / 7.0;
        double variance = module / 2.0;
        return Math.Abs(module - counts[0]) < variance
            && Math.Abs(module - counts[1]) < variance
            && Math.Abs(3.0 * module - counts[2]) < 3.0 * variance
            && Math.Abs(module - counts[3]) < variance
            && Math.Abs(module - counts[4]) < variance;
    }

    /// <summary>
    /// A row hit becomes a candidate only if the vertical, then the horizontal, then the diagonal through the refined centre
    /// show the same pattern. This is what separates a real finder from data modules that happen to look like one on a row.
    /// </summary>
    private static bool ConfirmFinder(bool[] binary, int width, int height, int[] counts, int y, int endX, List<FinderPattern> found)
    {
        int total = counts[0] + counts[1] + counts[2] + counts[3] + counts[4];
        double centerX = endX - counts[4] - counts[3] - counts[2] / 2.0;
        double centerY = CrossCheck(binary, width, height, (int)centerX, y, 0, 1, counts[2], total, true, LooksLikeFinder);
        if (double.IsNaN(centerY)) return false;
        centerX = CrossCheck(binary, width, height, (int)centerX, (int)centerY, 1, 0, counts[2], total, true, LooksLikeFinder);
        if (double.IsNaN(centerX)) return false;
        if (double.IsNaN(CrossCheck(binary, width, height, (int)centerX, (int)centerY, 1, 1, counts[2], total, false, LooksLikeFinder)))
            return false;

        double size = total / 7.0;
        int index = found.FindIndex(f => f.AboutEquals(size, centerY, centerX));
        if (index >= 0) found[index] = found[index].Combine(centerX, centerY, size);
        else found.Add(new FinderPattern(centerX, centerY, size));
        return true;
    }

    /// <summary>
    /// Walk both ways along a line from a dark start pixel counting dark, light, dark runs outward (five runs in all) and test
    /// them. Returns the refined centre coordinate along the line's axis (x for horizontal, y for vertical), or NaN.
    /// </summary>
    private static double CrossCheck(bool[] bin, int w, int h, int x, int y, int dx, int dy, int maxCount, int originalTotal,
        bool checkTotal, Func<int[], bool> matches)
    {
        bool InBounds(int px, int py) => px >= 0 && px < w && py >= 0 && py < h;
        if (!InBounds(x, y) || !bin[y * w + x]) return double.NaN;
        var c = new int[5];

        int cx = x, cy = y;
        while (InBounds(cx, cy) && bin[cy * w + cx]) { c[2]++; cx -= dx; cy -= dy; }
        if (!InBounds(cx, cy)) return double.NaN;
        while (InBounds(cx, cy) && !bin[cy * w + cx] && c[1] <= maxCount) { c[1]++; cx -= dx; cy -= dy; }
        if (!InBounds(cx, cy) || c[1] > maxCount) return double.NaN;
        while (InBounds(cx, cy) && bin[cy * w + cx] && c[0] <= maxCount) { c[0]++; cx -= dx; cy -= dy; }
        if (c[0] > maxCount) return double.NaN;

        cx = x + dx;
        cy = y + dy;
        int forward = 1; // the exclusive end of what has been counted, in steps from the start pixel
        while (InBounds(cx, cy) && bin[cy * w + cx]) { c[2]++; cx += dx; cy += dy; forward++; }
        if (!InBounds(cx, cy)) return double.NaN;
        while (InBounds(cx, cy) && !bin[cy * w + cx] && c[3] < maxCount) { c[3]++; cx += dx; cy += dy; forward++; }
        if (!InBounds(cx, cy) || c[3] >= maxCount) return double.NaN;
        while (InBounds(cx, cy) && bin[cy * w + cx] && c[4] < maxCount) { c[4]++; cx += dx; cy += dy; forward++; }
        if (c[4] >= maxCount) return double.NaN;

        if (checkTotal)
        {
            // The two directions measure the same pattern, so their totals should agree, but a code seen at an angle is
            // foreshortened along one axis only: allow up to 3x either way (a tilt of about 70 degrees) and leave the
            // run RATIOS, which survive any affine squeeze, to reject look-alikes.
            int total = c[0] + c[1] + c[2] + c[3] + c[4];
            if (total > 3 * originalTotal || 3 * total < originalTotal) return double.NaN;
        }
        if (!matches(c)) return double.NaN;

        double centreOffset = forward - c[4] - c[3] - c[2] / 2.0;
        return (dx != 0 ? x : y) + centreOffset;
    }

    #endregion

    #region Choosing the three finders

    /// <summary>
    /// The best triples of finder candidates, most plausible first, each ordered top-left, top-right, bottom-left. A real
    /// triple has a right angle at the top-left, equal legs and equal module sizes; a look-alike usually breaks one of them.
    /// </summary>
    public static List<(FinderPattern TL, FinderPattern TR, FinderPattern BL)> RankTriples(List<FinderPattern> finders)
    {
        var pool = finders.Where(f => f.Count >= 2).ToList();
        if (pool.Count < 3) pool = finders.ToList();
        var result = new List<(double Score, FinderPattern TL, FinderPattern TR, FinderPattern BL)>();
        if (pool.Count < 3) return new();
        pool = pool.OrderByDescending(f => f.Count).Take(8).ToList();

        for (int i = 0; i < pool.Count; i++)
            for (int j = i + 1; j < pool.Count; j++)
                for (int k = j + 1; k < pool.Count; k++)
                {
                    var pts = new[] { pool[i], pool[j], pool[k] };
                    double bestScore = double.MaxValue;
                    FinderPattern? bestTL = null, bestTR = null, bestBL = null;
                    for (int v = 0; v < 3; v++)
                    {
                        var a = pts[v];
                        var b = pts[(v + 1) % 3];
                        var c = pts[(v + 2) % 3];
                        double abx = b.X - a.X, aby = b.Y - a.Y;
                        double acx = c.X - a.X, acy = c.Y - a.Y;
                        double lenAB = Math.Sqrt(abx * abx + aby * aby);
                        double lenAC = Math.Sqrt(acx * acx + acy * acy);
                        double rightAngle = Math.Abs(abx * acx + aby * acy) / (lenAB * lenAC + 0.001);
                        double legs = Math.Abs(lenAB - lenAC) / (Math.Max(lenAB, lenAC) + 0.001);
                        double smallest = Math.Min(a.ModuleSize, Math.Min(b.ModuleSize, c.ModuleSize));
                        double largest = Math.Max(a.ModuleSize, Math.Max(b.ModuleSize, c.ModuleSize));
                        double sizes = (largest - smallest) / (largest + 0.001);
                        double score = rightAngle + legs + sizes;
                        if (score >= bestScore) continue;
                        bestScore = score;
                        bestTL = a;
                        // Image y points down: with the top-left at the origin, the top-right is the one that is
                        // clockwise of the bottom-left, i.e. cross(TR - TL, BL - TL) > 0.
                        if (abx * acy - aby * acx > 0) { bestTR = b; bestBL = c; }
                        else { bestTR = c; bestBL = b; }
                    }
                    result.Add((bestScore, bestTL!, bestTR!, bestBL!));
                }

        return result.OrderBy(r => r.Score).Take(3).Select(r => (r.TL, r.TR, r.BL)).ToList();
    }

    public static double Distance(FinderPattern a, FinderPattern b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    /// <summary>
    /// The module size in pixels along each edge of the code: along top-left to top-right, and along top-left to bottom-left.
    /// The finder scan measures along image rows, so it is wrong for a rotated code (the row crosses the rings at a slant) and
    /// for a squeezed one (a tilted code is foreshortened along one axis only). Measured instead along each leg, at both of its
    /// finders, by walking out from the finder's centre through dark core, light ring, dark ring: that is 3.5 modules.
    /// </summary>
    public static (double AlongTop, double AlongLeft) MeasureModuleSizes(bool[] binary, int width, int height,
        FinderPattern tl, FinderPattern tr, FinderPattern bl)
    {
        double fallback = (tl.ModuleSize + tr.ModuleSize + bl.ModuleSize) / 3.0;
        double Along(FinderPattern a, FinderPattern b, FinderPattern c)
        {
            // The module size along the line a-b, measured at both end finders.
            var estimates = new List<double>();
            foreach (var (from, toward) in new[] { (a, b), (b, a) })
            {
                double dx = toward.X - from.X, dy = toward.Y - from.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1e-6) continue;
                dx /= length; dy /= length;
                double forward = RunToPatternEdge(binary, width, height, from.X, from.Y, dx, dy);
                double backward = RunToPatternEdge(binary, width, height, from.X, from.Y, -dx, -dy);
                if (!double.IsNaN(forward) && !double.IsNaN(backward)) estimates.Add((forward + backward) / 7.0);
                else if (!double.IsNaN(forward)) estimates.Add(forward * 2 / 7.0);
                else if (!double.IsNaN(backward)) estimates.Add(backward * 2 / 7.0);
            }
            return estimates.Count == 0 ? double.NaN : estimates.Average();
        }

        double top = Along(tl, tr, bl);
        double left = Along(tl, bl, tr);
        return (double.IsNaN(top) ? fallback : top, double.IsNaN(left) ? fallback : left);
    }

    /// <summary>
    /// Distance from a finder's centre, walking in a direction, to the outer edge of its dark ring: half the dark core, the
    /// light ring and the dark ring, 3.5 modules. NaN if the walk leaves the image or the pattern is not there.
    /// </summary>
    private static double RunToPatternEdge(bool[] binary, int width, int height, double cx, double cy, double dx, double dy)
    {
        const double Step = 0.25;
        int phase = 0; // 0 dark core, 1 light ring, 2 dark ring
        double limit = Math.Min(width, height);
        for (double t = 0; t < limit; t += Step)
        {
            int px = (int)Math.Floor(cx + dx * t), py = (int)Math.Floor(cy + dy * t);
            if (px < 0 || px >= width || py < 0 || py >= height) return double.NaN;
            bool dark = binary[py * width + px];
            if (phase == 0 && !dark) phase = 1;
            else if (phase == 1 && dark) phase = 2;
            else if (phase == 2 && !dark) return t;
        }
        return double.NaN;
    }

    #endregion

    #region Alignment pattern

    /// <summary>
    /// Search for the alignment pattern near where the finders predict it, trying a widening window. Returns null when none is
    /// confirmed (the caller then assumes a parallelogram).
    /// </summary>
    private static FinderPattern? FindAlignment(bool[] binary, int width, int height, double estX, double estY, double moduleSize,
        double uxX, double uxY, double uyX, double uyY)
    {
        foreach (int allowance in new[] { 4, 8, 16 })
        {
            double window = allowance * moduleSize;
            int x0 = Math.Max(0, (int)Math.Floor(estX - window)), x1 = Math.Min(width - 1, (int)Math.Ceiling(estX + window));
            int y0 = Math.Max(0, (int)Math.Floor(estY - window)), y1 = Math.Min(height - 1, (int)Math.Ceiling(estY + window));
            if (x1 - x0 < 3 * moduleSize || y1 - y0 < 3 * moduleSize) continue;

            FinderPattern? best = null;
            double bestScore = double.MaxValue;
            for (int y = y0; y <= y1; y++)
            {
                // Run-length encode the row inside the window as (dark, start, length).
                int runStart = x0;
                bool runDark = binary[y * width + x0];
                var runs = new List<(bool Dark, int Start, int Length)>();
                for (int x = x0 + 1; x <= x1; x++)
                {
                    bool dark = binary[y * width + x];
                    if (dark == runDark) continue;
                    runs.Add((runDark, runStart, x - runStart));
                    runStart = x;
                    runDark = dark;
                }
                runs.Add((runDark, runStart, x1 + 1 - runStart));

                // The centre of the pattern is a dark module with a light ring on both sides. The outer dark ring runs into the
                // dark data around it, so its run lengths mean nothing; only the light-dark-light core is checked.
                for (int i = 1; i + 1 < runs.Count; i++)
                {
                    if (!runs[i].Dark || runs[i - 1].Dark || runs[i + 1].Dark) continue;
                    // The first and last runs are cut off by the window, so their length is unknown.
                    if (i - 1 == 0 || i + 1 == runs.Count - 1) continue;
                    double unit = runs[i].Length;
                    if (unit < 0.4 * moduleSize || unit > 2.5 * moduleSize) continue;
                    double variance = unit * 0.5;
                    if (Math.Abs(runs[i - 1].Length - unit) > variance || Math.Abs(runs[i + 1].Length - unit) > variance) continue;

                    double centerX = runs[i].Start + runs[i].Length / 2.0;
                    double centerY = RefineAlignmentY(binary, width, height, (int)centerX, y, unit);
                    if (double.IsNaN(centerY)) continue;

                    double score = AlignmentTemplateMismatch(binary, width, height, centerX, centerY, uxX, uxY, uyX, uyY);
                    if (score > 4) continue; // at most 4 of the 25 modules of the 5x5 pattern may disagree
                    double distance = (centerX - estX) * (centerX - estX) + (centerY - estY) * (centerY - estY);
                    double rank = score * 1e6 + distance;
                    if (rank < bestScore)
                    {
                        bestScore = rank;
                        best = new FinderPattern(centerX, centerY, unit);
                    }
                }
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>The vertical centre of the dark module at (x, y): the middle of its dark run, light above and below.</summary>
    private static double RefineAlignmentY(bool[] binary, int width, int height, int x, int y, double unit)
    {
        if (!binary[y * width + x]) return double.NaN;
        int top = y;
        while (top > 0 && binary[(top - 1) * width + x]) top--;
        int bottom = y;
        while (bottom < height - 1 && binary[(bottom + 1) * width + x]) bottom++;
        if (top == 0 || bottom == height - 1) return double.NaN;
        double length = bottom - top + 1;
        if (length < 0.4 * unit || length > 2.5 * unit) return double.NaN;
        // The light ring above and below the centre module.
        if (top - 1 < 0 || bottom + 1 >= height || binary[(top - 1) * width + x] || binary[(bottom + 1) * width + x]) return double.NaN;
        return (top + bottom + 1) / 2.0;
    }

    /// <summary>
    /// How many of the 25 modules of the 5x5 alignment pattern (dark centre, light ring, dark ring) disagree with the image,
    /// sampled along the code's own axes. This is what rejects a stretch of data that merely has a light-dark-light core.
    /// </summary>
    private static int AlignmentTemplateMismatch(bool[] binary, int width, int height, double cx, double cy,
        double uxX, double uxY, double uyX, double uyY)
    {
        int mismatches = 0;
        for (int dr = -2; dr <= 2; dr++)
        {
            for (int dc = -2; dc <= 2; dc++)
            {
                int px = (int)Math.Floor(cx + dc * uxX + dr * uyX);
                int py = (int)Math.Floor(cy + dc * uxY + dr * uyY);
                bool expectDark = Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1;
                if (px < 0 || px >= width || py < 0 || py >= height) { mismatches++; continue; }
                if (binary[py * width + px] != expectDark) mismatches++;
            }
        }
        return mismatches;
    }

    #endregion

    #region Sampling

    /// <summary>
    /// Sample the code's modules through a projective transform fixed by the three finder centres and a fourth point. Without
    /// <paramref name="useAlignment"/> the fourth point is the corner that completes the parallelogram (exact for a flat,
    /// face-on or rotated code). With it, the fourth point is the alignment pattern found near where the finders predict it,
    /// which is what lets a code seen at an angle (perspective) read; null when the version has none or none is confirmed.
    /// Also null if a module falls outside the image.
    /// </summary>
    public static bool[,]? Sample(bool[] binary, int width, int height, FinderPattern tl, FinderPattern tr, FinderPattern bl,
        double moduleSize, int version, bool useAlignment)
    {
        int dimension = QRTables.ModuleCount(version);
        double dimMinusThreeAndAHalf = dimension - 3.5;

        double bottomRightX = tr.X - tl.X + bl.X;
        double bottomRightY = tr.Y - tl.Y + bl.Y;
        double sourceBottomRight = dimMinusThreeAndAHalf;
        if (useAlignment)
        {
            if (version < 2) return null;
            double correction = 1.0 - 3.0 / (dimension - 7);
            double estX = tl.X + correction * (bottomRightX - tl.X);
            double estY = tl.Y + correction * (bottomRightY - tl.Y);
            double span = dimension - 7.0; // module steps between finder centres
            var alignment = FindAlignment(binary, width, height, estX, estY, moduleSize,
                (tr.X - tl.X) / span, (tr.Y - tl.Y) / span, (bl.X - tl.X) / span, (bl.Y - tl.Y) / span);
            if (alignment == null) return null;
            bottomRightX = alignment.X;
            bottomRightY = alignment.Y;
            sourceBottomRight = dimension - 6.5;
        }

        var transform = PerspectiveTransform.QuadrilateralToQuadrilateral(
            3.5, 3.5, dimMinusThreeAndAHalf, 3.5, sourceBottomRight, sourceBottomRight, 3.5, dimMinusThreeAndAHalf,
            tl.X, tl.Y, tr.X, tr.Y, bottomRightX, bottomRightY, bl.X, bl.Y);

        var modules = new bool[dimension, dimension];
        var points = new double[2 * dimension];
        for (int r = 0; r < dimension; r++)
        {
            for (int c = 0; c < dimension; c++)
            {
                points[2 * c] = c + 0.5;
                points[2 * c + 1] = r + 0.5;
            }
            transform.Apply(points);
            for (int c = 0; c < dimension; c++)
            {
                int px = (int)Math.Floor(points[2 * c]);
                int py = (int)Math.Floor(points[2 * c + 1]);
                if (px < 0 || px >= width || py < 0 || py >= height) return null;
                modules[r, c] = binary[py * width + px];
            }
        }
        return modules;
    }

    #endregion
}

/// <summary>A 3x3 projective transform between planes (the homography a camera view of a flat code needs).</summary>
internal sealed class PerspectiveTransform
{
    private readonly double a11, a12, a13, a21, a22, a23, a31, a32, a33;

    private PerspectiveTransform(double a11, double a21, double a31, double a12, double a22, double a32, double a13, double a23, double a33)
    {
        this.a11 = a11; this.a12 = a12; this.a13 = a13;
        this.a21 = a21; this.a22 = a22; this.a23 = a23;
        this.a31 = a31; this.a32 = a32; this.a33 = a33;
    }

    /// <summary>The transform taking the four source points to the four destination points.</summary>
    public static PerspectiveTransform QuadrilateralToQuadrilateral(
        double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3,
        double x0p, double y0p, double x1p, double y1p, double x2p, double y2p, double x3p, double y3p)
    {
        var quadToSquare = SquareToQuadrilateral(x0, y0, x1, y1, x2, y2, x3, y3).BuildAdjoint();
        var squareToQuad = SquareToQuadrilateral(x0p, y0p, x1p, y1p, x2p, y2p, x3p, y3p);
        return squareToQuad.Times(quadToSquare);
    }

    /// <summary>Map points in place: (x, y) pairs.</summary>
    public void Apply(double[] points)
    {
        for (int i = 0; i + 1 < points.Length; i += 2)
        {
            double x = points[i], y = points[i + 1];
            double denominator = a13 * x + a23 * y + a33;
            points[i] = (a11 * x + a21 * y + a31) / denominator;
            points[i + 1] = (a12 * x + a22 * y + a32) / denominator;
        }
    }

    private static PerspectiveTransform SquareToQuadrilateral(
        double x0, double y0, double x1, double y1, double x2, double y2, double x3, double y3)
    {
        double dx3 = x0 - x1 + x2 - x3;
        double dy3 = y0 - y1 + y2 - y3;
        if (dx3 == 0.0 && dy3 == 0.0)
            return new PerspectiveTransform(x1 - x0, x2 - x1, x0, y1 - y0, y2 - y1, y0, 0.0, 0.0, 1.0);

        double dx1 = x1 - x2, dx2 = x3 - x2, dy1 = y1 - y2, dy2 = y3 - y2;
        double denominator = dx1 * dy2 - dx2 * dy1;
        double a13 = (dx3 * dy2 - dx2 * dy3) / denominator;
        double a23 = (dx1 * dy3 - dx3 * dy1) / denominator;
        return new PerspectiveTransform(x1 - x0 + a13 * x1, x3 - x0 + a23 * x3, x0, y1 - y0 + a13 * y1, y3 - y0 + a23 * y3, y0, a13, a23, 1.0);
    }

    private PerspectiveTransform BuildAdjoint() => new(
        a22 * a33 - a23 * a32, a23 * a31 - a21 * a33, a21 * a32 - a22 * a31,
        a13 * a32 - a12 * a33, a11 * a33 - a13 * a31, a12 * a31 - a11 * a32,
        a12 * a23 - a13 * a22, a13 * a21 - a11 * a23, a11 * a22 - a12 * a21);

    private PerspectiveTransform Times(PerspectiveTransform o) => new(
        a11 * o.a11 + a21 * o.a12 + a31 * o.a13,
        a11 * o.a21 + a21 * o.a22 + a31 * o.a23,
        a11 * o.a31 + a21 * o.a32 + a31 * o.a33,
        a12 * o.a11 + a22 * o.a12 + a32 * o.a13,
        a12 * o.a21 + a22 * o.a22 + a32 * o.a23,
        a12 * o.a31 + a22 * o.a32 + a32 * o.a33,
        a13 * o.a11 + a23 * o.a12 + a33 * o.a13,
        a13 * o.a21 + a23 * o.a22 + a33 * o.a23,
        a13 * o.a31 + a23 * o.a32 + a33 * o.a33);
}
