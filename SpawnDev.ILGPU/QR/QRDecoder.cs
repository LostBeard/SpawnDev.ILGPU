using ILGPU;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU.QR;

/// <summary>
/// GPU-accelerated QR code decoder. Reads QR codes from pixel data.
///
/// Pipeline (GPU-parallel steps marked with ⚡):
///   1. ⚡ Grayscale conversion (parallel per pixel)
///   2. ⚡ Adaptive threshold / binarization (parallel per pixel)
///   3.    Finder pattern detection (scan rows/columns for 1:1:3:1:1 ratio)
///   4. ⚡ Perspective correction (parallel pixel sampling)
///   5. ⚡ Bit extraction from corrected grid (parallel per module)
///   6.    Unmask + read format info
///   7.    Reed-Solomon error correction decode
///   8.    Data decode (byte/numeric/alphanumeric mode)
///
/// Usage:
///   var text = QRDecoder.Decode(rgbaPixels, width, height);
///   var text = await QRDecoder.DecodeAsync(accelerator, rgbaPixels, width, height);
/// </summary>
public static class QRDecoder
{
    /// <summary>
    /// Decode a QR code from RGBA pixel data using GPU-accelerated image processing.
    /// </summary>
    /// <param name="accelerator">GPU accelerator for parallel binarization.</param>
    /// <param name="pixels">RGBA pixel buffer (4 bytes per pixel).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <returns>Decoded text, or null if no QR code found.</returns>
    public static async Task<string?> DecodeAsync(Accelerator accelerator, byte[] pixels, int width, int height)
    {
        int totalPixels = width * height;

        // 1. GPU: convert RGBA (packed as int) to grayscale
        var packed = new int[totalPixels];
        Buffer.BlockCopy(pixels, 0, packed, 0, pixels.Length);
        using var packedBuf = accelerator.Allocate1D(packed);
        using var grayBuf = accelerator.Allocate1D<byte>(totalPixels);

        var grayscaleKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<int>, ArrayView<byte>>(GrayscaleKernel);
        grayscaleKernel(totalPixels, packedBuf.View, grayBuf.View);
        await accelerator.SynchronizeAsync();

        // 2. GPU: block-based adaptive binarization, the same scheme as the CPU path (Binarize). Per 8x8 block the GPU
        // gathers sum / min / max; the black points depend on the blocks above and to the left, so that sequential step runs
        // on the host over the block statistics only (4 ints per 64 pixels, not an image); the GPU then thresholds every pixel.
        int blocksX = (width + BlockSize - 1) >> BlockPower;
        int blocksY = (height + BlockSize - 1) >> BlockPower;
        using var statsBuf = accelerator.Allocate1D<int>(blocksX * blocksY * BlockStatsStride);
        var statsKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<byte>, ArrayView<int>, int, int, int>(BlockStatsKernel);
        statsKernel(blocksX * blocksY, grayBuf.View, statsBuf.View, width, height, blocksX);
        await accelerator.SynchronizeAsync();
        var stats = await statsBuf.CopyToHostAsync<int>();
        var thresholds = BlockThresholds(BlackPoints(stats, blocksX, blocksY), blocksX, blocksY);

        using var thresholdBuf = accelerator.Allocate1D(thresholds);
        using var binaryBuf = accelerator.Allocate1D<int>(totalPixels);
        var binarizeKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<byte>, ArrayView<int>, ArrayView<int>, int, int>(BinarizeKernel);
        binarizeKernel(totalPixels, grayBuf.View, thresholdBuf.View, binaryBuf.View, width, blocksX);
        await accelerator.SynchronizeAsync();

        var binaryInts = await binaryBuf.CopyToHostAsync<int>();

        // Convert int[] to bool[]
        var binary = new bool[totalPixels];
        for (int i = 0; i < totalPixels; i++)
            binary[i] = binaryInts[i] != 0;

        var gray = await grayBuf.CopyToHostAsync<byte>();

        // Rest of pipeline: finder detection, grid sampling, decode
        return DecodeFromBinary(binary, gray, width, height);
    }

    /// <summary>GPU kernel: sum, min, max and pixel count of one 8x8 block (one thread per block).</summary>
    static void BlockStatsKernel(Index1D blockIndex, ArrayView<byte> gray, ArrayView<int> stats, int width, int height, int blocksX)
    {
        int bx = blockIndex % blocksX;
        int by = blockIndex / blocksX;
        int x0 = bx * BlockSize, y0 = by * BlockSize;
        int x1 = x0 + BlockSize < width ? x0 + BlockSize : width;
        int y1 = y0 + BlockSize < height ? y0 + BlockSize : height;
        int sum = 0, min = 255, max = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int v = gray[y * width + x];
                sum += v;
                if (v < min) min = v;
                if (v > max) max = v;
            }
        }
        int s = blockIndex * BlockStatsStride;
        stats[s] = sum;
        stats[s + 1] = min;
        stats[s + 2] = max;
        stats[s + 3] = (x1 - x0) * (y1 - y0);
    }

    /// <summary>GPU kernel: dark when at or below the threshold of the pixel's block.</summary>
    static void BinarizeKernel(Index1D idx, ArrayView<byte> gray, ArrayView<int> thresholds, ArrayView<int> binary, int width, int blocksX)
    {
        int x = idx % width;
        int y = idx / width;
        binary[idx] = gray[idx] <= thresholds[(y >> BlockPower) * blocksX + (x >> BlockPower)] ? 1 : 0;
    }

    /// <summary>GPU kernel: RGBA packed int → grayscale byte.</summary>
    static void GrayscaleKernel(Index1D idx, ArrayView<int> rgba, ArrayView<byte> gray)
    {
        int pixel = rgba[idx];
        int r = pixel & 0xFF;
        int g = (pixel >> 8) & 0xFF;
        int b = (pixel >> 16) & 0xFF;
        gray[idx] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
    }

    /// <summary>
    /// Decode a QR code from RGBA pixel data (CPU fallback).
    /// </summary>
    /// <param name="pixels">RGBA pixel buffer (4 bytes per pixel).</param>
    /// <param name="width">Image width.</param>
    /// <param name="height">Image height.</param>
    /// <returns>Decoded text, or null if no QR code found.</returns>
    public static string? Decode(byte[] pixels, int width, int height)
    {
        // 1. Convert to grayscale
        var gray = new byte[width * height];
        for (int i = 0; i < gray.Length; i++)
        {
            int pi = i * 4;
            // Luminance: 0.299R + 0.587G + 0.114B
            gray[i] = (byte)((pixels[pi] * 77 + pixels[pi + 1] * 150 + pixels[pi + 2] * 29) >> 8);
        }

        return DecodeFromGrayscale(gray, width, height);
    }

    /// <summary>Decode from pre-computed binary + grayscale (GPU path).</summary>
    private static string? DecodeFromBinary(bool[] binary, byte[] gray, int width, int height)
    {
        return DecodeFromBinaryData(binary, width, height);
    }

    /// <summary>Shared decode pipeline from grayscale image (CPU path).</summary>
    private static string? DecodeFromGrayscale(byte[] gray, int width, int height)
    {
        // 2. Binarize (adaptive threshold using local mean)
        var binary = Binarize(gray, width, height);
        return DecodeFromBinaryData(binary, width, height);
    }

    /// <summary>Core decode pipeline from binary image.</summary>
    private static string? DecodeFromBinaryData(bool[] binary, int width, int height)
    {

        // 3. Find finder patterns (rotation-invariant: a 1:1:3:1:1 run ratio holds along any line through the centre)
        var finders = QRLocator.FindFinders(binary, width, height);
        if (finders.Count < 3) return null;

        // 4. Try the most plausible finder triples, each ordered top-left, top-right, bottom-left
        foreach (var (tl, tr, bl) in QRLocator.RankTriples(finders))
        {
            // 5. Estimate module size and version. The finder centres are (size - 7) modules apart and the module size is
            // only measured, so the estimate is a few percent off either way. Round to the NEAREST version (truncating, as
            // this once did, always errs low and loses a version on any small underestimate), then also try the
            // neighbours: a wrong version fails Reed-Solomon, which verifies its own result, so a wrong guess costs time and
            // never produces a wrong answer.
            var (alongTop, alongLeft) = QRLocator.MeasureModuleSizes(binary, width, height, tl, tr, bl);
            double moduleSize = (alongTop + alongLeft) / 2.0;
            if (moduleSize < 1) continue;
            // Each leg is divided by the module size measured along that leg, so a code squeezed along one axis still counts right.
            double legs = (QRLocator.Distance(tl, tr) / alongTop + QRLocator.Distance(tl, bl) / alongLeft) / 2.0;
            int estimatedModules = (int)Math.Round(legs) + 7;
            int estimatedVersion = (int)Math.Round((estimatedModules - 17) / 4.0);
            foreach (int version in new[] { estimatedVersion, estimatedVersion - 1, estimatedVersion + 1 })
            {
                if (version < 1 || version > 40) continue;
                var text = DecodeAtVersion(binary, width, height, tl, tr, bl, moduleSize, version);
                if (text != null) return text;
            }
        }
        return null;
    }

    /// <summary>
    /// Sample, unmask, correct and decode the code assuming it is the given version. Tried first on the plain parallelogram
    /// through the three finder centres (exact for a flat code seen face-on or rotated, and it needs no search); if that fails
    /// Reed-Solomon, again with the alignment pattern as the fourth point, which corrects for perspective. Reed-Solomon
    /// verifies each attempt, so a wrong sampling can only fail, never return wrong text.
    /// </summary>
    private static string? DecodeAtVersion(bool[] binary, int width, int height,
        FinderPattern tl, FinderPattern tr, FinderPattern bl, double moduleSize, int version)
    {
        // 6. Sample the grid
        var modules = QRLocator.Sample(binary, width, height, tl, tr, bl, moduleSize, version, useAlignment: false);
        var text = modules == null ? null : DecodeModules(modules, version);
        if (text != null || version < 2) return text;

        modules = QRLocator.Sample(binary, width, height, tl, tr, bl, moduleSize, version, useAlignment: true);
        return modules == null ? null : DecodeModules(modules, version);
    }

    /// <summary>Format info, unmask, extract, correct and decode a sampled module grid.</summary>
    private static string? DecodeModules(bool[,] modules, int version)
    {
        int qrSize = QRTables.ModuleCount(version);

        // 7. Read format info to determine EC level and mask
        var (ecLevel, maskPattern) = ReadFormatInfo(modules, qrSize);
        if (ecLevel < 0) return null;

        // 8. Unmask
        UnmaskData(modules, qrSize, maskPattern);

        // 9. Extract data bits
        var codewords = ExtractDataBits(modules, qrSize, version);

        // 10. Deinterleave and error correct
        var ecInfo = QRTables.GetECInfo(version, (QRTables.ECLevel)ecLevel);
        var dataBytes = DeinterleaveAndCorrect(codewords, ecInfo);
        if (dataBytes == null) return null;

        // 11. Decode data (version-aware for correct char count bits)
        return DecodeData(dataBytes, version);
    }

    #region Binarization

    // Block-based adaptive binarisation (the hybrid scheme ZXing uses): per 8x8 block a "black point", then each pixel is
    // compared with the average black point of the 5x5 blocks around it. This replaces a local mean taken from only 9
    // sample points, which fell below zero inside any dark region (all nine samples dark, threshold -5) and turned the
    // dark core of a finder pattern light, so large codes had no finder patterns at all.
    internal const int BlockPower = 3;
    internal const int BlockSize = 1 << BlockPower;
    /// <summary>A block flatter than this holds no code edge: it is background or solid, not data.</summary>
    internal const int MinDynamicRange = 24;
    /// <summary>Ints per block in the statistics array: sum, min, max, pixel count.</summary>
    internal const int BlockStatsStride = 4;

    private static bool[] Binarize(byte[] gray, int width, int height)
    {
        int blocksX = (width + BlockSize - 1) >> BlockPower;
        int blocksY = (height + BlockSize - 1) >> BlockPower;
        var stats = new int[blocksX * blocksY * BlockStatsStride];
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int x0 = bx << BlockPower, y0 = by << BlockPower;
                int x1 = Math.Min(width, x0 + BlockSize), y1 = Math.Min(height, y0 + BlockSize);
                int sum = 0, min = 255, max = 0;
                for (int y = y0; y < y1; y++)
                {
                    for (int x = x0; x < x1; x++)
                    {
                        int v = gray[y * width + x];
                        sum += v;
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
                }
                int s = (by * blocksX + bx) * BlockStatsStride;
                stats[s] = sum;
                stats[s + 1] = min;
                stats[s + 2] = max;
                stats[s + 3] = (x1 - x0) * (y1 - y0);
            }
        }

        var thresholds = BlockThresholds(BlackPoints(stats, blocksX, blocksY), blocksX, blocksY);
        var binary = new bool[width * height];
        // (BlackPoints derives its flat-block cut-off from the image's own noise level; see NoiseAwareDynamicRange.)
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
                binary[y * width + x] = gray[y * width + x] <= thresholds[(y >> BlockPower) * blocksX + (x >> BlockPower)];
        }
        return binary;
    }

    /// <summary>
    /// One black point per block from its statistics. A block with real contrast uses its mean; a flat block uses half its
    /// level, or - when it is darker than the blocks above and to the left - their average, so a solid dark area inside
    /// the code (the core of a finder pattern) is still read as dark.
    /// </summary>
    internal static int[] BlackPoints(int[] stats, int blocksX, int blocksY)
    {
        int dynamicRange = NoiseAwareDynamicRange(stats, blocksX * blocksY);
        var blackPoints = new int[blocksX * blocksY];
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int s = (by * blocksX + bx) * BlockStatsStride;
                int sum = stats[s], min = stats[s + 1], max = stats[s + 2], count = stats[s + 3];
                int average;
                if (max - min > dynamicRange)
                {
                    average = sum / count;
                }
                else
                {
                    average = min / 2;
                    if (by > 0 && bx > 0)
                    {
                        int neighbors = (blackPoints[(by - 1) * blocksX + bx]
                            + 2 * blackPoints[by * blocksX + bx - 1]
                            + blackPoints[(by - 1) * blocksX + bx - 1]) / 4;
                        if (min < neighbors) average = neighbors;
                    }
                }
                blackPoints[by * blocksX + bx] = average;
            }
        }
        return blackPoints;
    }

    /// <summary>
    /// The min-max range below which a block counts as flat. A fixed 24 is right for a clean frame, but in a noisy one every
    /// flat block spans far more than that (about five standard deviations), so each would be thresholded at its own mean and
    /// the background would speckle. Most of a frame is flat (the code is a small part of it), so the MEDIAN block range is the
    /// image's noise floor (taken at the lower quartile, so a code that fills the frame, whose blocks nearly all hold edges, is
    /// not mistaken for noise): a block must clear 1.5x that to count as holding an edge. The cut-off is capped at half of the
    /// image's own strong-edge range (the 95th percentile) so a low-contrast code is not discarded, and is never below the fixed
    /// minimum.
    /// </summary>
    internal static int NoiseAwareDynamicRange(int[] stats, int blockCount)
    {
        var ranges = new int[blockCount];
        for (int i = 0; i < blockCount; i++)
            ranges[i] = stats[i * BlockStatsStride + 2] - stats[i * BlockStatsStride + 1];
        System.Array.Sort(ranges);
        int noiseFloor = ranges[blockCount / 4];
        int contrast = ranges[blockCount * 95 / 100];
        return Math.Max(MinDynamicRange, Math.Min(noiseFloor + noiseFloor / 2, contrast / 2));
    }

    /// <summary>The threshold for each block: the mean black point of the 5x5 blocks centred on it (clamped at the edges).</summary>
    internal static int[] BlockThresholds(int[] blackPoints, int blocksX, int blocksY)
    {
        var thresholds = new int[blocksX * blocksY];
        for (int by = 0; by < blocksY; by++)
        {
            int cy = Math.Max(2, Math.Min(by, blocksY - 3));
            for (int bx = 0; bx < blocksX; bx++)
            {
                int cx = Math.Max(2, Math.Min(bx, blocksX - 3));
                int sum = 0, count = 0;
                for (int dy = -2; dy <= 2; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= blocksY) continue;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= blocksX) continue;
                        sum += blackPoints[y * blocksX + x];
                        count++;
                    }
                }
                thresholds[by * blocksX + bx] = sum / count;
            }
        }
        return thresholds;
    }

    #endregion

    #region Format Info & Unmasking

    private static (int ecLevel, int maskPattern) ReadFormatInfo(bool[,] modules, int size)
    {
        // Read format bits from around top-left finder
        int bits = 0;
        int[] rowPos = { 0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8 };
        int[] colPos = { 8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0 };

        for (int i = 0; i < 15; i++)
        {
            if (modules[rowPos[i], colPos[i]])
                bits |= 1 << (14 - i);
        }

        // Match directly against stored format info strings
        // (QRTables.FormatInfo already includes BCH + XOR mask)
        int bestEc = -1, bestMask = -1, bestDist = int.MaxValue;
        for (int ec = 0; ec < 4; ec++)
        {
            for (int mask = 0; mask < 8; mask++)
            {
                int expected = QRTables.FormatInfo[ec * 8 + mask];
                int dist = BitCount(bits ^ expected);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestEc = ec;
                    bestMask = mask;
                }
            }
        }

        // Accept if Hamming distance <= 3 (format info has distance 7, can correct 3)
        if (bestDist > 3) return (-1, -1);
        return (bestEc, bestMask);
    }

    private static int BitCount(int n)
    {
        int count = 0;
        while (n != 0) { count += n & 1; n >>= 1; }
        return count;
    }

    private static void UnmaskData(bool[,] modules, int size, int maskPattern)
    {
        // Create reserved map
        var reserved = new bool[size, size];

        // Mark finder patterns + separators
        MarkFinderReserved(reserved, 0, 0, size);
        MarkFinderReserved(reserved, 0, size - 7, size);
        MarkFinderReserved(reserved, size - 7, 0, size);
        for (int i = 0; i < 8; i++)
        {
            MarkReserved(reserved, i, 7, size);
            MarkReserved(reserved, 7, i, size);
            MarkReserved(reserved, i, size - 8, size);
            MarkReserved(reserved, 7, size - 8 + i, size);
            MarkReserved(reserved, size - 8, i, size);
            MarkReserved(reserved, size - 8 + i, 7, size);
        }

        // Timing patterns
        for (int i = 8; i < size - 8; i++)
        {
            reserved[6, i] = true;
            reserved[i, 6] = true;
        }

        // Format info area
        for (int i = 0; i < 9; i++)
        {
            reserved[i, 8] = true;
            reserved[8, i] = true;
        }
        for (int i = 0; i < 8; i++) reserved[8, size - 8 + i] = true;
        for (int i = 0; i < 7; i++) reserved[size - 7 + i, 8] = true;

        // Dark module
        int version = (size - 17) / 4;
        reserved[4 * version + 9, 8] = true;

        // Alignment patterns
        if (version >= 2)
        {
            var positions = QRTables.AlignmentPatterns[version - 1];
            foreach (int r in positions)
            {
                foreach (int c in positions)
                {
                    if ((r <= 8 && c <= 8) || (r <= 8 && c >= size - 8) || (r >= size - 8 && c <= 8))
                        continue;
                    for (int dr = -2; dr <= 2; dr++)
                        for (int dc = -2; dc <= 2; dc++)
                            if (r + dr >= 0 && r + dr < size && c + dc >= 0 && c + dc < size)
                                reserved[r + dr, c + dc] = true;
                }
            }
        }

        // Unmask data modules
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (!reserved[r, c] && EvaluateMask(maskPattern, r, c))
                    modules[r, c] = !modules[r, c];
            }
        }
    }

    private static bool EvaluateMask(int maskPattern, int row, int col) => maskPattern switch
    {
        0 => (row + col) % 2 == 0,
        1 => row % 2 == 0,
        2 => col % 3 == 0,
        3 => (row + col) % 3 == 0,
        4 => (row / 2 + col / 3) % 2 == 0,
        5 => (row * col) % 2 + (row * col) % 3 == 0,
        6 => ((row * col) % 2 + (row * col) % 3) % 2 == 0,
        7 => ((row + col) % 2 + (row * col) % 3) % 2 == 0,
        _ => false,
    };

    private static void MarkFinderReserved(bool[,] reserved, int row, int col, int size)
    {
        for (int r = 0; r < 7 && row + r < size; r++)
            for (int c = 0; c < 7 && col + c < size; c++)
                reserved[row + r, col + c] = true;
    }

    private static void MarkReserved(bool[,] reserved, int r, int c, int size)
    {
        if (r >= 0 && r < size && c >= 0 && c < size)
            reserved[r, c] = true;
    }

    #endregion

    #region Data Extraction

    private static byte[] ExtractDataBits(bool[,] modules, int size, int version)
    {
        var bits = new List<bool>();

        // Create reserved map (same as unmask)
        var reserved = new bool[size, size];
        MarkFinderReserved(reserved, 0, 0, size);
        MarkFinderReserved(reserved, 0, size - 7, size);
        MarkFinderReserved(reserved, size - 7, 0, size);
        for (int i = 0; i < 8; i++)
        {
            MarkReserved(reserved, i, 7, size);
            MarkReserved(reserved, 7, i, size);
            MarkReserved(reserved, i, size - 8, size);
            MarkReserved(reserved, 7, size - 8 + i, size);
            MarkReserved(reserved, size - 8, i, size);
            MarkReserved(reserved, size - 8 + i, 7, size);
        }
        for (int i = 8; i < size - 8; i++)
        {
            reserved[6, i] = true;
            reserved[i, 6] = true;
        }
        for (int i = 0; i < 9; i++) { reserved[i, 8] = true; reserved[8, i] = true; }
        for (int i = 0; i < 8; i++) reserved[8, size - 8 + i] = true;
        for (int i = 0; i < 7; i++) reserved[size - 7 + i, 8] = true;
        reserved[4 * version + 9, 8] = true;
        if (version >= 2)
        {
            var positions = QRTables.AlignmentPatterns[version - 1];
            foreach (int r in positions)
                foreach (int c in positions)
                {
                    if ((r <= 8 && c <= 8) || (r <= 8 && c >= size - 8) || (r >= size - 8 && c <= 8)) continue;
                    for (int dr = -2; dr <= 2; dr++)
                        for (int dc = -2; dc <= 2; dc++)
                            if (r + dr >= 0 && r + dr < size && c + dc >= 0 && c + dc < size)
                                reserved[r + dr, c + dc] = true;
                }
        }
        if (version >= 7)
        {
            for (int i = 0; i < 6; i++)
                for (int j = 0; j < 3; j++)
                {
                    reserved[size - 11 + j, i] = true;
                    reserved[i, size - 11 + j] = true;
                }
        }

        // Read data in zigzag pattern (same as encoder)
        int col = size - 1;
        while (col >= 0)
        {
            if (col == 6) col--;

            bool upward = ((size - 1 - col) / 2) % 2 == 0;

            for (int i = 0; i < size; i++)
            {
                int row = upward ? (size - 1 - i) : i;

                if (!reserved[row, col])
                    bits.Add(modules[row, col]);

                if (col > 0 && !reserved[row, col - 1])
                    bits.Add(modules[row, col - 1]);
            }

            col -= 2;
        }

        // Convert bits to bytes
        var bytes = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++)
        {
            if (bits[i])
                bytes[i / 8] |= (byte)(0x80 >> (i % 8));
        }

        return bytes;
    }

    #endregion

    #region Error Correction Decode

    private static byte[]? DeinterleaveAndCorrect(byte[] codewords, QRTables.ECBlockInfo ecInfo)
    {
        int totalBlocks = ecInfo.Group1Blocks + ecInfo.Group2Blocks;
        int ecPerBlock = ecInfo.ECCodewordsPerBlock;
        int totalDataCodewords = ecInfo.TotalDataCodewords;
        int totalCodewords = totalDataCodewords + totalBlocks * ecPerBlock;

        if (codewords.Length < totalDataCodewords)
            return null;

        // Deinterleave data codewords
        var dataBlocks = new byte[totalBlocks][];
        for (int i = 0; i < ecInfo.Group1Blocks; i++)
            dataBlocks[i] = new byte[ecInfo.Group1DataCodewords];
        for (int i = 0; i < ecInfo.Group2Blocks; i++)
            dataBlocks[ecInfo.Group1Blocks + i] = new byte[ecInfo.Group2DataCodewords];

        int maxDataLen = dataBlocks.Max(b => b.Length);
        int idx = 0;
        for (int col = 0; col < maxDataLen; col++)
        {
            for (int block = 0; block < totalBlocks; block++)
            {
                if (col < dataBlocks[block].Length && idx < codewords.Length)
                    dataBlocks[block][col] = codewords[idx++];
            }
        }

        // Deinterleave EC codewords
        var ecBlocks = new byte[totalBlocks][];
        for (int i = 0; i < totalBlocks; i++)
            ecBlocks[i] = new byte[ecPerBlock];

        for (int col = 0; col < ecPerBlock; col++)
        {
            for (int block = 0; block < totalBlocks; block++)
            {
                if (idx < codewords.Length)
                    ecBlocks[block][col] = codewords[idx++];
            }
        }

        // Reed-Solomon error correction per block. A block that cannot be corrected fails the whole decode: returning its
        // damaged bytes as if they were data hands the caller a confident wrong answer (a camera-read ID that is not
        // the ID), which is worse than no answer.
        for (int block = 0; block < totalBlocks; block++)
        {
            var corrected = ReedSolomonCorrect(dataBlocks[block], ecBlocks[block]);
            if (corrected == null)
                return null;
            dataBlocks[block] = corrected;
        }

        // Concatenate data blocks
        var result = new byte[totalDataCodewords];
        int offset = 0;
        foreach (var block in dataBlocks)
        {
            System.Array.Copy(block, 0, result, offset, block.Length);
            offset += block.Length;
        }

        return result;
    }

    /// <summary>
    /// Reed-Solomon error correction decode for a single block.
    /// Uses syndrome computation + Berlekamp-Massey + Chien search + Forney.
    /// Returns corrected data, or null if uncorrectable.
    /// </summary>
    private static byte[]? ReedSolomonCorrect(byte[] data, byte[] ec)
    {
        int n = data.Length + ec.Length; // total codewords
        int ecLen = ec.Length;

        // Build received polynomial: data + ec
        var received = new byte[n];
        System.Array.Copy(data, 0, received, 0, data.Length);
        System.Array.Copy(ec, 0, received, data.Length, ec.Length);

        // The codeword polynomial is r(x) = sum received[j] * x^(n-1-j): received[0] is the HIGHEST degree. QR's generator
        // has roots alpha^0 .. alpha^(ecLen-1), so the syndromes are S_i = r(alpha^i), evaluated by Horner.
        var syndromes = Syndromes(received, ecLen);
        if (syndromes == null)
            return data; // all syndromes zero: no errors

        // Berlekamp-Massey: error locator Lambda(x) = 1 + l1 x + l2 x^2 + ... (lowest degree first)
        var lambda = new byte[ecLen + 1];
        var prev = new byte[ecLen + 1];
        lambda[0] = 1;
        prev[0] = 1;
        int errors = 0;      // L: current number of errors assumed
        int shift = 1;       // m: iterations since the last length change
        byte prevDelta = 1;  // b: the discrepancy at the last length change
        for (int step = 0; step < ecLen; step++)
        {
            byte delta = syndromes[step];
            for (int i = 1; i <= errors; i++)
                delta ^= GaloisField.Multiply(lambda[i], syndromes[step - i]);
            if (delta == 0)
            {
                shift++;
                continue;
            }
            var before = (byte[])lambda.Clone();
            byte scale = GaloisField.Divide(delta, prevDelta);
            for (int i = 0; i + shift <= ecLen; i++)
                lambda[i + shift] ^= GaloisField.Multiply(scale, prev[i]);
            if (2 * errors <= step)
            {
                errors = step + 1 - errors;
                prev = before;
                prevDelta = delta;
                shift = 1;
            }
            else
            {
                shift++;
            }
        }

        if (errors == 0 || 2 * errors > ecLen)
            return null; // more errors than this block can repair

        // Chien search: position k (the x^k term, k = 0 is the LAST codeword) is in error when Lambda(alpha^-k) == 0.
        var errorDegrees = new List<int>();
        for (int k = 0; k < n; k++)
        {
            byte xInv = GaloisField.Exp[(255 - k) % 255];
            byte value = 0;
            byte power = 1;
            for (int i = 0; i <= errors; i++)
            {
                value ^= GaloisField.Multiply(lambda[i], power);
                power = GaloisField.Multiply(power, xInv);
            }
            if (value == 0)
                errorDegrees.Add(k);
        }

        if (errorDegrees.Count != errors)
            return null; // locator roots do not match its degree: the damage is beyond repair

        // Forney: error evaluator Omega(x) = S(x) * Lambda(x) mod x^ecLen, with S(x) = sum S_i x^i.
        var omega = new byte[ecLen];
        for (int i = 0; i < ecLen; i++)
        {
            byte sum = 0;
            for (int j = 0; j <= errors && j <= i; j++)
                sum ^= GaloisField.Multiply(lambda[j], syndromes[i - j]);
            omega[i] = sum;
        }

        foreach (int k in errorDegrees)
        {
            byte x = GaloisField.Exp[k % 255];
            byte xInv = GaloisField.Inverse(x);

            byte omegaValue = 0;
            byte power = 1;
            for (int i = 0; i < ecLen; i++)
            {
                omegaValue ^= GaloisField.Multiply(omega[i], power);
                power = GaloisField.Multiply(power, xInv);
            }

            // Formal derivative of Lambda in characteristic 2: only odd-degree terms survive, as l_i * x^(i-1).
            byte derivative = 0;
            byte xInvSquared = GaloisField.Multiply(xInv, xInv);
            byte oddPower = 1; // x^(i-1) for i = 1, 3, 5, ...
            for (int i = 1; i <= errors; i += 2)
            {
                derivative ^= GaloisField.Multiply(lambda[i], oddPower);
                oddPower = GaloisField.Multiply(oddPower, xInvSquared);
            }
            if (derivative == 0)
                return null;

            // First consecutive root is alpha^0 (b = 0): e = X * Omega(X^-1) / Lambda'(X^-1).
            byte magnitude = GaloisField.Divide(GaloisField.Multiply(x, omegaValue), derivative);
            received[n - 1 - k] ^= magnitude;
        }

        // Trust nothing: the repaired codeword must have all-zero syndromes, or this was a miscorrection.
        if (Syndromes(received, ecLen) != null)
            return null;

        System.Array.Copy(received, 0, data, 0, data.Length);
        return data;
    }

    /// <summary>Syndromes S_i = r(alpha^i), i = 0 .. ecLen-1, or null when every one is zero (the block is clean).</summary>
    private static byte[]? Syndromes(byte[] received, int ecLen)
    {
        var syndromes = new byte[ecLen];
        bool any = false;
        for (int i = 0; i < ecLen; i++)
        {
            byte x = GaloisField.Exp[i];
            byte acc = 0;
            for (int j = 0; j < received.Length; j++)
                acc = (byte)(GaloisField.Multiply(acc, x) ^ received[j]);
            syndromes[i] = acc;
            if (acc != 0) any = true;
        }
        return any ? syndromes : null;
    }

    #endregion

    #region Data Decoding

    private static string? DecodeData(byte[] data, int version = 1)
    {
        int bitPos = 0;

        int ReadBits(int count)
        {
            int val = 0;
            for (int i = 0; i < count; i++)
            {
                int byteIdx = bitPos / 8;
                int bitIdx = 7 - (bitPos % 8);
                if (byteIdx < data.Length)
                    val = (val << 1) | ((data[byteIdx] >> bitIdx) & 1);
                else
                    val <<= 1;
                bitPos++;
            }
            return val;
        }

        var result = new System.Text.StringBuilder();

        while (bitPos < data.Length * 8 - 4)
        {
            int mode = ReadBits(4);
            if (mode == 0) break; // terminator

            if (mode == 4) // Byte mode
            {
                int ccBits = QRTables.CharCountBits(version, QRTables.Mode.Byte);
                int charCount = ReadBits(ccBits);
                for (int i = 0; i < charCount; i++)
                {
                    int b = ReadBits(8);
                    result.Append((char)b);
                }
            }
            else if (mode == 2) // Alphanumeric
            {
                int ccBits = QRTables.CharCountBits(version, QRTables.Mode.Alphanumeric);
                int charCount = ReadBits(ccBits);
                for (int i = 0; i < charCount - 1; i += 2)
                {
                    int pair = ReadBits(11);
                    result.Append(AlphanumericChar(pair / 45));
                    result.Append(AlphanumericChar(pair % 45));
                }
                if (charCount % 2 == 1)
                {
                    int single = ReadBits(6);
                    result.Append(AlphanumericChar(single));
                }
            }
            else if (mode == 1) // Numeric
            {
                int ccBits = QRTables.CharCountBits(version, QRTables.Mode.Numeric);
                int charCount = ReadBits(ccBits);
                int remaining = charCount;
                while (remaining >= 3)
                {
                    int triple = ReadBits(10);
                    result.Append(triple.ToString("D3"));
                    remaining -= 3;
                }
                if (remaining == 2)
                {
                    int pair = ReadBits(7);
                    result.Append(pair.ToString("D2"));
                }
                else if (remaining == 1)
                {
                    int single = ReadBits(4);
                    result.Append(single.ToString());
                }
            }
            else
            {
                break; // unsupported mode (ECI, kanji, etc.)
            }
        }

        return result.Length > 0 ? result.ToString() : null;
    }

    private static char AlphanumericChar(int value) => value switch
    {
        >= 0 and <= 9 => (char)('0' + value),
        >= 10 and <= 35 => (char)('A' + value - 10),
        36 => ' ', 37 => '$', 38 => '%', 39 => '*',
        40 => '+', 41 => '-', 42 => '.', 43 => '/', 44 => ':',
        _ => '?'
    };

    #endregion
}
