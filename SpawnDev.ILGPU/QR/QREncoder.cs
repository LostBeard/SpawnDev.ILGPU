using static SpawnDev.ILGPU.QR.QRTables;

namespace SpawnDev.ILGPU.QR;

/// <summary>
/// QR code encoder. Converts data into a QR code module matrix.
/// Implements the full ISO/IEC 18004 encoding pipeline:
///   1. Data encoding (byte/numeric/alphanumeric mode)
///   2. Error correction (Reed-Solomon via GaloisField)
///   3. Data interleaving (multi-block)
///   4. Matrix construction (finder, timing, alignment, format, version)
///   5. Data placement (zigzag upward columns)
///   6. Masking (8 patterns, penalty scoring, best selection)
/// </summary>
public class QREncoder
{
    /// <summary>
    /// Encode a string as a QR code matrix.
    /// Returns a 2D bool array where true = dark module, false = light module.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="ecLevel">Error correction level. H recommended for logo overlay.</param>
    /// <param name="minVersion">Minimum QR version (0 = auto-select).</param>
    /// <returns>The QR code as a boolean matrix (true = dark).</returns>
    public static bool[,] Encode(string text, ECLevel ecLevel = ECLevel.M, int minVersion = 0)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(text);
        return Encode(data, ecLevel, minVersion);
    }

    /// <summary>
    /// Encode raw bytes as a QR code matrix.
    /// </summary>
    public static bool[,] Encode(byte[] data, ECLevel ecLevel = ECLevel.M, int minVersion = 0)
    {
        // 1. Determine version
        int version = Math.Max(minVersion, FindMinVersion(data.Length, ecLevel));
        var ecInfo = GetECInfo(version, ecLevel);
        int size = ModuleCount(version);

        // 2. Build data bitstream
        var bits = EncodeDataBits(data, version, ecInfo);

        // 3. Generate error correction and interleave
        var codewords = BuildCodewords(bits, version, ecLevel, ecInfo);

        // 4. Create matrix and place fixed patterns
        var matrix = new bool[size, size];
        var reserved = new bool[size, size]; // tracks which modules are fixed (not data)
        PlaceFinderPatterns(matrix, reserved, size);
        PlaceTimingPatterns(matrix, reserved, size);
        PlaceAlignmentPatterns(matrix, reserved, version, size);
        ReserveDarkModule(matrix, reserved, version);
        ReserveFormatArea(reserved, size);
        if (version >= 7) ReserveVersionArea(reserved, size);

        // 5. Place data bits in zigzag pattern
        PlaceDataBits(matrix, reserved, codewords, size);

        // 6. Apply best mask
        int bestMask = FindBestMask(matrix, reserved, size, version, ecLevel);
        ApplyMask(matrix, reserved, size, bestMask);

        // 7. Write format info (after masking)
        PlaceFormatInfo(matrix, size, ecLevel, bestMask);
        if (version >= 7) PlaceVersionInfo(matrix, size, version);

        return matrix;
    }

    /// <summary>
    /// Get the module size for a given text and EC level.
    /// </summary>
    public static int GetSize(string text, ECLevel ecLevel = ECLevel.M)
    {
        var data = System.Text.Encoding.UTF8.GetBytes(text);
        int version = FindMinVersion(data.Length, ecLevel);
        return ModuleCount(version);
    }

    #region Data Encoding

    private static List<byte> EncodeDataBits(byte[] data, int version, ECBlockInfo ecInfo)
    {
        var bits = new BitWriter();

        // Mode indicator: byte mode = 0100
        bits.Write(0b0100, 4);

        // Character count
        int ccBits = CharCountBits(version, Mode.Byte);
        bits.Write(data.Length, ccBits);

        // Data bytes
        foreach (var b in data)
            bits.Write(b, 8);

        // Terminator (up to 4 zero bits)
        int totalDataBits = ecInfo.TotalDataCodewords * 8;
        int terminatorLen = Math.Min(4, totalDataBits - bits.Length);
        if (terminatorLen > 0)
            bits.Write(0, terminatorLen);

        // Pad to byte boundary
        while (bits.Length % 8 != 0)
            bits.Write(0, 1);

        // Pad with alternating 0xEC, 0x11 to fill data capacity
        bool toggle = false;
        while (bits.Length < totalDataBits)
        {
            bits.Write(toggle ? 0x11 : 0xEC, 8);
            toggle = !toggle;
        }

        return bits.ToBytes();
    }

    #endregion

    #region Error Correction & Interleaving

    private static byte[] BuildCodewords(List<byte> dataBytes, int version, ECLevel ecLevel, ECBlockInfo ecInfo)
    {
        var data = dataBytes.ToArray();
        int ecPerBlock = ecInfo.ECCodewordsPerBlock;

        // Split data into blocks
        var dataBlocks = new List<byte[]>();
        int offset = 0;

        for (int i = 0; i < ecInfo.Group1Blocks; i++)
        {
            var block = new byte[ecInfo.Group1DataCodewords];
            System.Array.Copy(data, offset, block, 0, block.Length);
            offset += block.Length;
            dataBlocks.Add(block);
        }
        for (int i = 0; i < ecInfo.Group2Blocks; i++)
        {
            var block = new byte[ecInfo.Group2DataCodewords];
            System.Array.Copy(data, offset, block, 0, block.Length);
            offset += block.Length;
            dataBlocks.Add(block);
        }

        // Generate EC for each block
        var ecBlocks = new List<byte[]>();
        foreach (var block in dataBlocks)
            ecBlocks.Add(GaloisField.ComputeEC(block, ecPerBlock));

        // Interleave data codewords
        var result = new List<byte>();
        int maxDataLen = dataBlocks.Max(b => b.Length);
        for (int i = 0; i < maxDataLen; i++)
        {
            foreach (var block in dataBlocks)
            {
                if (i < block.Length)
                    result.Add(block[i]);
            }
        }

        // Interleave EC codewords
        for (int i = 0; i < ecPerBlock; i++)
        {
            foreach (var block in ecBlocks)
            {
                if (i < block.Length)
                    result.Add(block[i]);
            }
        }

        return result.ToArray();
    }

    #endregion

    #region Matrix Construction

    private static void PlaceFinderPatterns(bool[,] matrix, bool[,] reserved, int size)
    {
        PlaceFinderPattern(matrix, reserved, 0, 0);                    // top-left
        PlaceFinderPattern(matrix, reserved, 0, size - 7);             // top-right
        PlaceFinderPattern(matrix, reserved, size - 7, 0);             // bottom-left

        // Separators (1-module white border around each finder)
        for (int i = 0; i < 8; i++)
        {
            // Top-left
            SetReserved(matrix, reserved, i, 7, false, size);
            SetReserved(matrix, reserved, 7, i, false, size);
            // Top-right
            SetReserved(matrix, reserved, i, size - 8, false, size);
            SetReserved(matrix, reserved, 7, size - 8 + i, false, size);
            // Bottom-left
            SetReserved(matrix, reserved, size - 8, i, false, size);
            SetReserved(matrix, reserved, size - 8 + i, 7, false, size);
        }
    }

    private static void PlaceFinderPattern(bool[,] matrix, bool[,] reserved, int row, int col)
    {
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                bool dark = r == 0 || r == 6 || c == 0 || c == 6 || // outer ring
                           (r >= 2 && r <= 4 && c >= 2 && c <= 4);  // inner 3x3
                matrix[row + r, col + c] = dark;
                reserved[row + r, col + c] = true;
            }
        }
    }

    private static void PlaceTimingPatterns(bool[,] matrix, bool[,] reserved, int size)
    {
        for (int i = 8; i < size - 8; i++)
        {
            bool dark = i % 2 == 0;
            // Horizontal timing pattern (row 6)
            if (!reserved[6, i])
            {
                matrix[6, i] = dark;
                reserved[6, i] = true;
            }
            // Vertical timing pattern (col 6)
            if (!reserved[i, 6])
            {
                matrix[i, 6] = dark;
                reserved[i, 6] = true;
            }
        }
    }

    private static void PlaceAlignmentPatterns(bool[,] matrix, bool[,] reserved, int version, int size)
    {
        if (version < 2) return;

        var positions = AlignmentPatterns[version - 1];
        foreach (int row in positions)
        {
            foreach (int col in positions)
            {
                // Skip if overlapping with finder patterns
                if (IsFinderArea(row, col, size)) continue;

                // Place 5x5 alignment pattern centered at (row, col)
                for (int r = -2; r <= 2; r++)
                {
                    for (int c = -2; c <= 2; c++)
                    {
                        bool dark = Math.Abs(r) == 2 || Math.Abs(c) == 2 || // outer ring
                                   (r == 0 && c == 0);                       // center dot
                        matrix[row + r, col + c] = dark;
                        reserved[row + r, col + c] = true;
                    }
                }
            }
        }
    }

    private static bool IsFinderArea(int row, int col, int size)
    {
        // Top-left finder + separator
        if (row <= 8 && col <= 8) return true;
        // Top-right finder + separator
        if (row <= 8 && col >= size - 8) return true;
        // Bottom-left finder + separator
        if (row >= size - 8 && col <= 8) return true;
        return false;
    }

    private static void ReserveDarkModule(bool[,] matrix, bool[,] reserved, int version)
    {
        // The dark module is always at (4 * version + 9, 8)
        int row = 4 * version + 9;
        matrix[row, 8] = true;
        reserved[row, 8] = true;
    }

    private static void ReserveFormatArea(bool[,] reserved, int size)
    {
        // Around top-left finder
        for (int i = 0; i < 9; i++)
        {
            reserved[i, 8] = true;   // column 8, rows 0-8
            reserved[8, i] = true;   // row 8, cols 0-8
        }
        // Near top-right finder
        for (int i = 0; i < 8; i++)
            reserved[8, size - 8 + i] = true;
        // Near bottom-left finder
        for (int i = 0; i < 7; i++)
            reserved[size - 7 + i, 8] = true;
    }

    private static void ReserveVersionArea(bool[,] reserved, int size)
    {
        // Near bottom-left finder: 6×3 block
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 3; j++)
                reserved[size - 11 + j, i] = true;
        // Near top-right finder: 3×6 block
        for (int i = 0; i < 6; i++)
            for (int j = 0; j < 3; j++)
                reserved[i, size - 11 + j] = true;
    }

    private static void SetReserved(bool[,] matrix, bool[,] reserved, int row, int col, bool dark, int size)
    {
        if (row >= 0 && row < size && col >= 0 && col < size)
        {
            matrix[row, col] = dark;
            reserved[row, col] = true;
        }
    }

    #endregion

    #region Data Placement

    private static void PlaceDataBits(bool[,] matrix, bool[,] reserved, byte[] codewords, int size)
    {
        int bitIndex = 0;
        int totalBits = codewords.Length * 8;

        // Data is placed in 2-column strips, right to left, bottom to top (then top to bottom), zigzag
        int col = size - 1;
        while (col >= 0)
        {
            // Skip column 6 (timing pattern)
            if (col == 6) col--;

            // Two columns at a time: col and col-1
            bool upward = ((size - 1 - col) / 2) % 2 == 0;

            for (int i = 0; i < size; i++)
            {
                int row = upward ? (size - 1 - i) : i;

                // Right column of the pair
                if (!reserved[row, col] && bitIndex < totalBits)
                {
                    matrix[row, col] = GetBit(codewords, bitIndex);
                    bitIndex++;
                }

                // Left column of the pair
                if (col > 0 && !reserved[row, col - 1] && bitIndex < totalBits)
                {
                    matrix[row, col - 1] = GetBit(codewords, bitIndex);
                    bitIndex++;
                }
            }

            col -= 2;
        }
    }

    private static bool GetBit(byte[] data, int bitIndex)
    {
        int byteIdx = bitIndex / 8;
        int bitIdx = 7 - (bitIndex % 8); // MSB first
        return byteIdx < data.Length && ((data[byteIdx] >> bitIdx) & 1) == 1;
    }

    #endregion

    #region Masking

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

    private static void ApplyMask(bool[,] matrix, bool[,] reserved, int size, int maskPattern)
    {
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (!reserved[r, c] && EvaluateMask(maskPattern, r, c))
                    matrix[r, c] = !matrix[r, c];
            }
        }
    }

    private static int FindBestMask(bool[,] matrix, bool[,] reserved, int size, int version, ECLevel ecLevel)
    {
        int bestMask = 0;
        int bestPenalty = int.MaxValue;

        for (int mask = 0; mask < 8; mask++)
        {
            // Create a copy and apply this mask
            var test = (bool[,])matrix.Clone();
            ApplyMask(test, reserved, size, mask);
            PlaceFormatInfo(test, size, ecLevel, mask);

            int penalty = CalculatePenalty(test, size);
            if (penalty < bestPenalty)
            {
                bestPenalty = penalty;
                bestMask = mask;
            }
        }

        return bestMask;
    }

    private static int CalculatePenalty(bool[,] matrix, int size)
    {
        return PenaltyRule1(matrix, size)
             + PenaltyRule2(matrix, size)
             + PenaltyRule3(matrix, size)
             + PenaltyRule4(matrix, size);
    }

    // Rule 1: 5+ consecutive same-color modules → 3 + (n-5)
    private static int PenaltyRule1(bool[,] matrix, int size)
    {
        int penalty = 0;

        for (int r = 0; r < size; r++)
        {
            int count = 1;
            for (int c = 1; c < size; c++)
            {
                if (matrix[r, c] == matrix[r, c - 1]) count++;
                else count = 1;
                if (count == 5) penalty += 3;
                else if (count > 5) penalty++;
            }
        }

        for (int c = 0; c < size; c++)
        {
            int count = 1;
            for (int r = 1; r < size; r++)
            {
                if (matrix[r, c] == matrix[r - 1, c]) count++;
                else count = 1;
                if (count == 5) penalty += 3;
                else if (count > 5) penalty++;
            }
        }

        return penalty;
    }

    // Rule 2: 2×2 same-color blocks → 3 per block
    private static int PenaltyRule2(bool[,] matrix, int size)
    {
        int penalty = 0;
        for (int r = 0; r < size - 1; r++)
        {
            for (int c = 0; c < size - 1; c++)
            {
                bool v = matrix[r, c];
                if (matrix[r, c + 1] == v && matrix[r + 1, c] == v && matrix[r + 1, c + 1] == v)
                    penalty += 3;
            }
        }
        return penalty;
    }

    // Rule 3: Finder-like pattern (1011101 with 4 light on either side) → 40
    private static int PenaltyRule3(bool[,] matrix, int size)
    {
        int penalty = 0;
        // Pattern: 10111010000 or 00001011101
        bool[] p1 = { true, false, true, true, true, false, true, false, false, false, false };
        bool[] p2 = { false, false, false, false, true, false, true, true, true, false, true };

        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c <= size - 11; c++)
            {
                if (MatchPattern(matrix, r, c, p1, true) || MatchPattern(matrix, r, c, p2, true))
                    penalty += 40;
            }
        }

        for (int c = 0; c < size; c++)
        {
            for (int r = 0; r <= size - 11; r++)
            {
                if (MatchPattern(matrix, r, c, p1, false) || MatchPattern(matrix, r, c, p2, false))
                    penalty += 40;
            }
        }

        return penalty;
    }

    private static bool MatchPattern(bool[,] matrix, int row, int col, bool[] pattern, bool horizontal)
    {
        for (int i = 0; i < pattern.Length; i++)
        {
            int r = horizontal ? row : row + i;
            int c = horizontal ? col + i : col;
            if (matrix[r, c] != pattern[i]) return false;
        }
        return true;
    }

    // Rule 4: Dark/light ratio deviation from 50% → penalty
    private static int PenaltyRule4(bool[,] matrix, int size)
    {
        int darkCount = 0;
        int total = size * size;
        for (int r = 0; r < size; r++)
            for (int c = 0; c < size; c++)
                if (matrix[r, c]) darkCount++;

        double percentage = (double)darkCount / total * 100.0;
        int lower = (int)(percentage / 5) * 5;
        int upper = lower + 5;
        int penaltyLower = Math.Abs(lower - 50) / 5 * 10;
        int penaltyUpper = Math.Abs(upper - 50) / 5 * 10;
        return Math.Min(penaltyLower, penaltyUpper);
    }

    #endregion

    #region Format & Version Info

    private static void PlaceFormatInfo(bool[,] matrix, int size, ECLevel ecLevel, int maskPattern)
    {
        int formatBits = FormatInfo[(int)ecLevel * 8 + maskPattern];

        // ISO/IEC 18004 7.9: the 15-bit string is written MSB first along row 8 (columns 0-5, 7, 8) then up column 8 (rows 7, 5-0),
        // and a second copy runs down column 8 from the bottom (MSB at the last row, 7 bits) and along row 8 from column size-8
        // (the next 8 bits, LSB at the last column). Arrays are [row, column] - they were transposed before (MSB at row 0, column 8),
        // which no other QR reader accepts.
        int[] rowPositions = { 8, 8, 8, 8, 8, 8, 8, 8, 7, 5, 4, 3, 2, 1, 0 };
        int[] colPositions = { 0, 1, 2, 3, 4, 5, 7, 8, 8, 8, 8, 8, 8, 8, 8 };

        for (int i = 0; i < 15; i++)
        {
            bool dark = ((formatBits >> (14 - i)) & 1) == 1;
            matrix[rowPositions[i], colPositions[i]] = dark;
        }

        // Second copy, bottom-left: column 8, rows size-1 .. size-7 hold bits 14 .. 8
        for (int i = 0; i < 7; i++)
        {
            bool dark = ((formatBits >> (14 - i)) & 1) == 1;
            matrix[size - 1 - i, 8] = dark;
        }

        // Second copy, top-right: row 8, columns size-8 .. size-1 hold bits 7 .. 0
        for (int i = 0; i < 8; i++)
        {
            bool dark = ((formatBits >> (7 - i)) & 1) == 1;
            matrix[8, size - 8 + i] = dark;
        }
    }

    private static void PlaceVersionInfo(bool[,] matrix, int size, int version)
    {
        if (version < 7) return;

        int versionBits = VersionInfo[version - 7];

        for (int i = 0; i < 18; i++)
        {
            bool dark = ((versionBits >> i) & 1) == 1;
            int row = i / 3;
            int col = size - 11 + (i % 3);

            // Near bottom-left
            matrix[size - 11 + (i % 3), i / 3] = dark;
            // Near top-right
            matrix[i / 3, size - 11 + (i % 3)] = dark;
        }
    }

    #endregion

    #region BitWriter

    private class BitWriter
    {
        private readonly List<byte> _bytes = new();
        private int _bitCount;

        public int Length => _bitCount;

        public void Write(int value, int bits)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                int byteIdx = _bitCount / 8;
                int bitIdx = 7 - (_bitCount % 8);

                while (_bytes.Count <= byteIdx)
                    _bytes.Add(0);

                if (((value >> i) & 1) == 1)
                    _bytes[byteIdx] |= (byte)(1 << bitIdx);

                _bitCount++;
            }
        }

        public List<byte> ToBytes() => new(_bytes);
    }

    #endregion
}
