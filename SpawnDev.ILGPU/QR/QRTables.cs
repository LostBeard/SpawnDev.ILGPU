namespace SpawnDev.ILGPU.QR;

/// <summary>
/// QR code specification tables: error correction, alignment patterns,
/// format strings, version strings, and character capacities.
/// All data from ISO/IEC 18004.
/// </summary>
public static class QRTables
{
    /// <summary>Error correction level.</summary>
    public enum ECLevel
    {
        /// <summary>Low: recovers about 7% of codewords.</summary>
        L = 0,
        /// <summary>Medium: recovers about 15% of codewords.</summary>
        M = 1,
        /// <summary>Quartile: recovers about 25% of codewords.</summary>
        Q = 2,
        /// <summary>High: recovers about 30% of codewords.</summary>
        H = 3
    }

    /// <summary>Data encoding mode. Each value equals the mode's 4-bit indicator in ISO/IEC 18004.</summary>
    public enum Mode
    {
        /// <summary>Numeric mode (digits 0-9), indicator 0001.</summary>
        Numeric = 1,
        /// <summary>Alphanumeric mode (0-9, A-Z, space, $ % * + - . / :), indicator 0010.</summary>
        Alphanumeric = 2,
        /// <summary>Byte mode (8-bit data), indicator 0100.</summary>
        Byte = 4,
        /// <summary>Kanji mode (Shift JIS double-byte), indicator 1000.</summary>
        Kanji = 8
    }

    /// <summary>
    /// Error correction block info for a specific version + EC level.
    /// </summary>
    public record ECBlockInfo(
        int TotalDataCodewords,
        int ECCodewordsPerBlock,
        int Group1Blocks,
        int Group1DataCodewords,
        int Group2Blocks,
        int Group2DataCodewords);

    /// <summary>QR code module size: 4 * version + 17.</summary>
    public static int ModuleCount(int version) => 4 * version + 17;

    /// <summary>
    /// Get EC block info for a version (1-40) and EC level.
    /// </summary>
    public static ECBlockInfo GetECInfo(int version, ECLevel level)
    {
        int idx = (version - 1) * 4 + (int)level;
        if (idx < 0 || idx >= ECData.Length)
            throw new ArgumentOutOfRangeException(nameof(version));
        return ECData[idx];
    }

    /// <summary>
    /// Find the minimum QR version that can encode the given byte count at the given EC level.
    /// </summary>
    public static int FindMinVersion(int byteCount, ECLevel level)
    {
        for (int v = 1; v <= 40; v++)
        {
            var info = GetECInfo(v, level);
            if (info.TotalDataCodewords >= byteCount + ModeHeaderSize(v))
                return v;
        }
        throw new ArgumentException($"Data too large for any QR version at EC level {level}");
    }

    /// <summary>Mode indicator (4 bits) + character count indicator size in bytes overhead.</summary>
    private static int ModeHeaderSize(int version)
    {
        // Mode indicator: 4 bits. Byte mode character count: 8 bits (v1-9) or 16 bits (v10+).
        // Plus terminator (up to 4 bits). Total overhead: ~2-3 bytes.
        return version <= 9 ? 2 : 3;
    }

    /// <summary>
    /// Character count indicator bit length for byte mode.
    /// </summary>
    public static int CharCountBits(int version, Mode mode)
    {
        if (mode == Mode.Byte)
            return version <= 9 ? 8 : 16;
        if (mode == Mode.Numeric)
            return version <= 9 ? 10 : (version <= 26 ? 12 : 14);
        if (mode == Mode.Alphanumeric)
            return version <= 9 ? 9 : (version <= 26 ? 11 : 13);
        // Kanji
        return version <= 9 ? 8 : (version <= 26 ? 10 : 12);
    }

    /// <summary>
    /// Alphanumeric character encoding table.
    /// </summary>
    public static int AlphanumericValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'A' and <= 'Z' => c - 'A' + 10,
        ' ' => 36, '$' => 37, '%' => 38, '*' => 39,
        '+' => 40, '-' => 41, '.' => 42, '/' => 43, ':' => 44,
        _ => -1
    };

    /// <summary>
    /// Alignment pattern center positions for each version (1-40).
    /// Version 1 has no alignment patterns.
    /// </summary>
    public static readonly int[][] AlignmentPatterns = new[]
    {
        System.Array.Empty<int>(),          // v1: none
        new[] { 6, 18 },                    // v2
        new[] { 6, 22 },                    // v3
        new[] { 6, 26 },                    // v4
        new[] { 6, 30 },                    // v5
        new[] { 6, 34 },                    // v6
        new[] { 6, 22, 38 },               // v7
        new[] { 6, 24, 42 },               // v8
        new[] { 6, 26, 46 },               // v9
        new[] { 6, 28, 50 },               // v10
        new[] { 6, 30, 54 },               // v11
        new[] { 6, 32, 58 },               // v12
        new[] { 6, 34, 62 },               // v13
        new[] { 6, 26, 46, 66 },           // v14
        new[] { 6, 26, 48, 70 },           // v15
        new[] { 6, 26, 50, 74 },           // v16
        new[] { 6, 30, 54, 78 },           // v17
        new[] { 6, 30, 56, 82 },           // v18
        new[] { 6, 30, 58, 86 },           // v19
        new[] { 6, 34, 62, 90 },           // v20
        new[] { 6, 28, 50, 72, 94 },       // v21
        new[] { 6, 26, 50, 74, 98 },       // v22
        new[] { 6, 30, 54, 78, 102 },      // v23
        new[] { 6, 28, 54, 80, 106 },      // v24
        new[] { 6, 32, 58, 84, 110 },      // v25
        new[] { 6, 30, 58, 86, 114 },      // v26
        new[] { 6, 34, 62, 90, 118 },      // v27
        new[] { 6, 26, 50, 74, 98, 122 },  // v28
        new[] { 6, 30, 54, 78, 102, 126 }, // v29
        new[] { 6, 26, 52, 78, 104, 130 }, // v30
        new[] { 6, 30, 56, 82, 108, 134 }, // v31
        new[] { 6, 34, 60, 86, 112, 138 }, // v32
        new[] { 6, 30, 58, 86, 114, 142 }, // v33
        new[] { 6, 34, 62, 90, 118, 146 }, // v34
        new[] { 6, 30, 54, 78, 102, 126, 150 }, // v35
        new[] { 6, 24, 50, 76, 102, 128, 154 }, // v36
        new[] { 6, 28, 54, 80, 106, 132, 158 }, // v37
        new[] { 6, 32, 58, 84, 110, 136, 162 }, // v38
        new[] { 6, 26, 54, 82, 110, 138, 166 }, // v39
        new[] { 6, 30, 58, 86, 114, 142, 170 }, // v40
    };

    /// <summary>
    /// Format information strings (15-bit) indexed by [ecLevel * 8 + maskPattern].
    /// Pre-computed with BCH error correction.
    /// </summary>
    public static readonly ushort[] FormatInfo = new ushort[]
    {
        // L: patterns 0-7
        0x77C4, 0x72F3, 0x7DAA, 0x789D, 0x662F, 0x6318, 0x6C41, 0x6976,
        // M: patterns 0-7
        0x5412, 0x5125, 0x5E7C, 0x5B4B, 0x45F9, 0x40CE, 0x4F97, 0x4AA0,
        // Q: patterns 0-7
        0x355F, 0x3068, 0x3F31, 0x3A06, 0x24B4, 0x2183, 0x2EDA, 0x2BED,
        // H: patterns 0-7
        0x1689, 0x13BE, 0x1CE7, 0x19D0, 0x0762, 0x0255, 0x0D0C, 0x083B,
    };

    /// <summary>
    /// Version information strings (18-bit) for versions 7-40.
    /// Index: version - 7.
    /// </summary>
    public static readonly int[] VersionInfo = new[]
    {
        0x07C94, 0x085BC, 0x09A99, 0x0A4D3, 0x0BBF6, 0x0C762, 0x0D847, 0x0E60D,
        0x0F928, 0x10B78, 0x1145D, 0x12A17, 0x13532, 0x149A6, 0x15683, 0x168C9,
        0x177EC, 0x18EC4, 0x191E1, 0x1AFAB, 0x1B08E, 0x1CC1A, 0x1D33F, 0x1ED75,
        0x1F250, 0x209D5, 0x216F0, 0x228BA, 0x2379F, 0x24B0B, 0x2542E, 0x26A64,
        0x27541, 0x28C69,
    };

    /// <summary>
    /// EC block data for all 40 versions × 4 EC levels.
    /// Index: (version - 1) * 4 + ecLevel.
    /// </summary>
    private static readonly ECBlockInfo[] ECData = new ECBlockInfo[]
    {
        // Version 1
        new(19, 7, 1, 19, 0, 0),    // L
        new(16, 10, 1, 16, 0, 0),   // M
        new(13, 13, 1, 13, 0, 0),   // Q
        new(9, 17, 1, 9, 0, 0),     // H
        // Version 2
        new(34, 10, 1, 34, 0, 0),   // L
        new(28, 16, 1, 28, 0, 0),   // M
        new(22, 22, 1, 22, 0, 0),   // Q
        new(16, 28, 1, 16, 0, 0),   // H
        // Version 3
        new(55, 15, 1, 55, 0, 0),   // L
        new(44, 26, 1, 44, 0, 0),   // M
        new(34, 18, 2, 17, 0, 0),   // Q
        new(26, 22, 2, 13, 0, 0),   // H
        // Version 4
        new(80, 20, 1, 80, 0, 0),   // L
        new(64, 18, 2, 32, 0, 0),   // M
        new(48, 26, 2, 24, 0, 0),   // Q
        new(36, 16, 4, 9, 0, 0),    // H
        // Version 5
        new(108, 26, 1, 108, 0, 0), // L
        new(86, 24, 2, 43, 0, 0),   // M
        new(62, 18, 2, 15, 2, 16),  // Q
        new(46, 22, 2, 11, 2, 12),  // H
        // Version 6
        new(136, 18, 2, 68, 0, 0),  // L
        new(108, 16, 4, 27, 0, 0),  // M
        new(76, 24, 4, 19, 0, 0),   // Q
        new(60, 28, 4, 15, 0, 0),   // H
        // Version 7
        new(156, 20, 2, 78, 0, 0),  // L
        new(124, 18, 4, 31, 0, 0),  // M
        new(88, 18, 2, 14, 4, 15),  // Q
        new(66, 26, 4, 13, 1, 14),  // H
        // Version 8
        new(194, 24, 2, 97, 0, 0),  // L
        new(154, 22, 2, 38, 2, 39), // M
        new(110, 22, 4, 18, 2, 19), // Q
        new(86, 26, 4, 14, 2, 15),  // H
        // Version 9
        new(232, 30, 2, 116, 0, 0), // L
        new(182, 22, 3, 36, 2, 37), // M
        new(132, 20, 4, 16, 4, 17), // Q
        new(100, 24, 4, 12, 4, 13), // H
        // Version 10
        new(274, 18, 2, 68, 2, 69), // L
        new(216, 26, 4, 43, 1, 44), // M
        new(154, 24, 6, 19, 2, 20), // Q
        new(122, 28, 6, 15, 2, 16), // H
        // Version 11
        new(324, 20, 4, 81, 0, 0),  // L
        new(254, 30, 1, 50, 4, 51), // M
        new(180, 28, 4, 22, 4, 23), // Q
        new(140, 24, 3, 12, 8, 13), // H
        // Version 12
        new(370, 24, 2, 92, 2, 93), // L
        new(290, 22, 6, 36, 2, 37), // M
        new(206, 26, 4, 20, 6, 21), // Q
        new(158, 28, 7, 14, 4, 15), // H
        // Version 13
        new(428, 26, 4, 107, 0, 0), // L
        new(334, 22, 8, 37, 1, 38), // M
        new(244, 24, 8, 20, 4, 21), // Q
        new(180, 22, 12, 11, 4, 12),// H
        // Version 14
        new(461, 30, 3, 115, 1, 116),// L
        new(365, 24, 4, 40, 5, 41), // M
        new(261, 20, 11, 16, 5, 17),// Q
        new(197, 24, 11, 12, 5, 13),// H
        // Version 15
        new(523, 22, 5, 87, 1, 88), // L
        new(415, 24, 5, 41, 5, 42), // M
        new(295, 30, 5, 24, 7, 25), // Q
        new(223, 24, 11, 12, 7, 13),// H
        // Version 16
        new(589, 24, 5, 98, 1, 99), // L
        new(453, 28, 7, 45, 3, 46), // M
        new(325, 24, 15, 19, 2, 20),// Q
        new(253, 30, 3, 15, 13, 16),// H
        // Version 17
        new(647, 28, 1, 107, 5, 108),// L
        new(507, 28, 10, 46, 1, 47),// M
        new(367, 28, 1, 22, 15, 23),// Q
        new(283, 28, 2, 14, 17, 15),// H
        // Version 18
        new(721, 30, 5, 120, 1, 121),// L
        new(563, 26, 9, 43, 4, 44), // M
        new(397, 28, 17, 22, 1, 23),// Q
        new(313, 28, 2, 14, 19, 15),// H
        // Version 19
        new(795, 28, 3, 113, 4, 114),// L
        new(627, 26, 3, 44, 11, 45),// M
        new(445, 26, 17, 21, 4, 22),// Q
        new(341, 26, 9, 13, 16, 14),// H
        // Version 20
        new(861, 28, 3, 107, 5, 108),// L
        new(669, 26, 3, 41, 13, 42),// M
        new(485, 28, 15, 24, 5, 25),// Q
        new(385, 28, 15, 15, 10, 16),// H
        // Version 21
        new(932, 28, 4, 116, 4, 117),// L
        new(714, 26, 17, 42, 0, 0), // M
        new(512, 30, 17, 22, 6, 23),// Q
        new(406, 28, 19, 16, 6, 17),// H
        // Version 22
        new(1006, 28, 2, 111, 7, 112),// L
        new(782, 28, 17, 46, 0, 0), // M
        new(568, 24, 7, 24, 16, 25),// Q
        new(442, 30, 34, 13, 0, 0), // H
        // Version 23
        new(1094, 30, 4, 121, 5, 122),// L
        new(860, 28, 4, 47, 14, 48),// M
        new(614, 30, 11, 24, 14, 25),// Q
        new(464, 30, 16, 15, 14, 16),// H
        // Version 24
        new(1174, 30, 6, 117, 4, 118),// L
        new(914, 28, 6, 45, 14, 46),// M
        new(664, 30, 11, 24, 16, 25),// Q
        new(514, 30, 30, 16, 2, 17),// H
        // Version 25
        new(1276, 26, 8, 106, 4, 107),// L
        new(1000, 28, 8, 47, 13, 48),// M
        new(718, 30, 7, 24, 22, 25),// Q
        new(538, 30, 22, 15, 13, 16),// H
        // Version 26
        new(1370, 28, 10, 114, 2, 115),// L
        new(1062, 28, 19, 46, 4, 47),// M
        new(754, 28, 28, 22, 6, 23),// Q
        new(596, 30, 33, 16, 4, 17),// H
        // Version 27
        new(1468, 30, 8, 122, 4, 123),// L
        new(1128, 28, 22, 45, 3, 46),// M
        new(808, 30, 8, 23, 26, 24),// Q
        new(628, 30, 12, 15, 28, 16),// H
        // Version 28
        new(1531, 30, 3, 117, 10, 118),// L
        new(1193, 28, 3, 45, 23, 46),// M
        new(871, 30, 4, 24, 31, 25),// Q
        new(661, 30, 11, 15, 31, 16),// H
        // Version 29
        new(1631, 30, 7, 116, 7, 117),// L
        new(1267, 28, 21, 45, 7, 46),// M
        new(911, 30, 1, 23, 37, 24),// Q
        new(701, 30, 19, 15, 26, 16),// H
        // Version 30
        new(1735, 30, 5, 115, 10, 116),// L
        new(1373, 28, 19, 47, 10, 48),// M
        new(985, 30, 15, 24, 25, 25),// Q
        new(745, 30, 23, 15, 25, 16),// H
        // Version 31
        new(1843, 30, 13, 115, 3, 116),// L
        new(1455, 28, 2, 46, 29, 47),// M
        new(1033, 30, 42, 24, 1, 25),// Q
        new(793, 30, 23, 15, 28, 16),// H
        // Version 32
        new(1955, 30, 17, 115, 0, 0),// L
        new(1541, 28, 10, 46, 23, 47),// M
        new(1115, 30, 10, 24, 35, 25),// Q
        new(845, 30, 19, 15, 35, 16),// H
        // Version 33
        new(2071, 30, 17, 115, 1, 116),// L
        new(1631, 28, 14, 46, 21, 47),// M
        new(1171, 30, 29, 24, 19, 25),// Q
        new(901, 30, 11, 15, 46, 16),// H
        // Version 34
        new(2191, 30, 13, 115, 6, 116),// L
        new(1725, 28, 14, 46, 23, 47),// M
        new(1231, 30, 44, 24, 7, 25),// Q
        new(961, 30, 59, 16, 1, 17),// H
        // Version 35
        new(2306, 30, 12, 121, 7, 122),// L
        new(1812, 28, 12, 47, 26, 48),// M
        new(1286, 30, 39, 24, 14, 25),// Q
        new(986, 30, 22, 15, 41, 16),// H
        // Version 36
        new(2434, 30, 6, 121, 14, 122),// L
        new(1914, 28, 6, 47, 34, 48),// M
        new(1354, 30, 46, 24, 10, 25),// Q
        new(1054, 30, 2, 15, 64, 16),// H
        // Version 37
        new(2566, 30, 17, 122, 4, 123),// L
        new(1992, 28, 29, 46, 14, 47),// M
        new(1426, 30, 49, 24, 10, 25),// Q
        new(1096, 30, 24, 15, 46, 16),// H
        // Version 38
        new(2702, 30, 4, 122, 18, 123),// L
        new(2102, 28, 13, 46, 32, 47),// M
        new(1502, 30, 48, 24, 14, 25),// Q
        new(1142, 30, 42, 15, 32, 16),// H
        // Version 39
        new(2812, 30, 20, 117, 4, 118),// L
        new(2216, 28, 40, 47, 7, 48),// M
        new(1582, 30, 43, 24, 22, 25),// Q
        new(1222, 30, 10, 15, 67, 16),// H
        // Version 40
        new(2956, 30, 19, 118, 6, 119),// L
        new(2334, 28, 18, 47, 31, 48),// M
        new(1666, 30, 34, 24, 34, 25),// Q
        new(1276, 30, 20, 15, 61, 16),// H
    };
}
