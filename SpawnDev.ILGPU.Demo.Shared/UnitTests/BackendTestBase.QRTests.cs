using SpawnDev.ILGPU.QR;
using SpawnDev.UnitTesting;
using static SpawnDev.ILGPU.QR.QRTables;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// QR code unit tests — encoding, rendering, GPU acceleration.
/// Tests verify structure, not visual appearance — a QR decoder
/// is the ultimate verification (planned).
/// </summary>
public abstract partial class BackendTestBase
{
    // ═══════════════════════════════════════════════════════════
    //  QR Code — Galois Field
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public Task QR_GaloisField_ExpLogRoundTrip()
    {
        // Every non-zero value should round-trip through exp/log
        for (int i = 1; i < 256; i++)
        {
            int exp = GaloisField.Exp[GaloisField.Log[(byte)i]];
            if (exp != i)
                throw new Exception($"GF(256) exp/log round-trip failed: {i} → log={GaloisField.Log[(byte)i]} → exp={exp}");
        }

        // Verify primitive polynomial: 2^8 should XOR with 285 to give 29
        if (GaloisField.Exp[8] != 29)
            throw new Exception($"GF(256) 2^8 should be 29, got {GaloisField.Exp[8]}");

        Console.WriteLine("[QR] GaloisField exp/log round-trip ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_GaloisField_Multiply()
    {
        // Known multiplications in GF(256)
        // 2 * 2 = 4
        if (GaloisField.Multiply(2, 2) != 4)
            throw new Exception($"GF(256) 2*2 should be 4, got {GaloisField.Multiply(2, 2)}");

        // Anything * 0 = 0
        if (GaloisField.Multiply(0, 137) != 0)
            throw new Exception("GF(256) 0*x should be 0");

        // Anything * 1 = itself
        if (GaloisField.Multiply(1, 42) != 42)
            throw new Exception("GF(256) 1*x should be x");

        // Multiply and divide should round-trip
        byte a = 37, b = 213;
        byte product = GaloisField.Multiply(a, b);
        byte divided = GaloisField.Divide(product, b);
        if (divided != a)
            throw new Exception($"GF(256) multiply/divide round-trip failed: {a}*{b}={product}, {product}/{b}={divided}");

        Console.WriteLine("[QR] GaloisField multiply/divide ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_GaloisField_ReedSolomon()
    {
        // Test EC generation for a known case: Version 1-M has 16 data codewords, 10 EC codewords
        // We can verify the EC codewords are non-zero and the correct length
        var data = new byte[16];
        for (int i = 0; i < 16; i++) data[i] = (byte)(i + 32); // some test data

        var ec = GaloisField.ComputeEC(data, 10);

        if (ec.Length != 10)
            throw new Exception($"EC should be 10 codewords, got {ec.Length}");

        // EC should not be all zeros (extremely unlikely for non-zero data)
        bool allZero = true;
        foreach (var b in ec) if (b != 0) { allZero = false; break; }
        if (allZero)
            throw new Exception("EC codewords should not be all zeros");

        Console.WriteLine($"[QR] Reed-Solomon EC: {ec.Length} codewords generated ✓");
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════
    //  QR Code — Encoder
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public Task QR_Encode_Version1()
    {
        // "HELLO" in byte mode fits in Version 1-M (16 data codewords)
        var matrix = QREncoder.Encode("HELLO", ECLevel.M);

        int size = matrix.GetLength(0);
        int expectedSize = ModuleCount(1); // 21

        if (size != expectedSize)
            throw new Exception($"Version 1 should be {expectedSize}x{expectedSize}, got {size}x{size}");

        // Verify finder patterns exist (top-left corner should be dark)
        if (!matrix[0, 0])
            throw new Exception("Top-left finder pattern corner should be dark");
        if (!matrix[0, 6])
            throw new Exception("Top-left finder pattern right edge should be dark");
        if (!matrix[6, 0])
            throw new Exception("Top-left finder pattern bottom edge should be dark");

        // Top-right finder
        if (!matrix[0, size - 1])
            throw new Exception("Top-right finder pattern corner should be dark");

        // Bottom-left finder
        if (!matrix[size - 1, 0])
            throw new Exception("Bottom-left finder pattern corner should be dark");

        Console.WriteLine($"[QR] Encode Version 1 ({size}x{size}): finder patterns verified ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Encode_URL()
    {
        // A typical URL
        var matrix = QREncoder.Encode("https://hub.spawndev.com/compute", ECLevel.M);
        int size = matrix.GetLength(0);

        // URL is ~32 bytes → should be Version 3 or 4 in M mode
        if (size < 25 || size > 45)
            throw new Exception($"URL QR should be reasonable size, got {size}x{size}");

        Console.WriteLine($"[QR] Encode URL: {size}x{size} ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Encode_ECLevelH_LargerThanM()
    {
        // Same text at H should produce a larger (or same) QR than M
        var matrixM = QREncoder.Encode("SpawnDev.ILGPU P2P Compute", ECLevel.M);
        var matrixH = QREncoder.Encode("SpawnDev.ILGPU P2P Compute", ECLevel.H);

        int sizeM = matrixM.GetLength(0);
        int sizeH = matrixH.GetLength(0);

        // H has more EC overhead, so it needs a higher version (larger matrix)
        if (sizeH < sizeM)
            throw new Exception($"EC level H should be >= M in size: H={sizeH}, M={sizeM}");

        Console.WriteLine($"[QR] EC level comparison: M={sizeM}x{sizeM}, H={sizeH}x{sizeH} ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Encode_AllECLevels()
    {
        // Verify all 4 EC levels produce valid matrices
        foreach (var level in new[] { ECLevel.L, ECLevel.M, ECLevel.Q, ECLevel.H })
        {
            var matrix = QREncoder.Encode("Test", level);
            int size = matrix.GetLength(0);
            if (size < 21) // minimum is Version 1 = 21x21
                throw new Exception($"EC level {level} produced too-small matrix: {size}");
        }

        Console.WriteLine("[QR] All 4 EC levels encode successfully ✓");
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════
    //  QR Code — CPU Renderer
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public Task QR_Render_CPU()
    {
        var (pixels, w, h) = QRCode.Generate("https://spawndev.com", moduleSize: 4, quietZone: 2);

        if (w != h)
            throw new Exception($"QR image should be square: {w}x{h}");
        if (w <= 0)
            throw new Exception("Image width should be positive");
        if (pixels.Length != w * h * 4)
            throw new Exception($"Pixel buffer size mismatch: {pixels.Length} vs expected {w * h * 4}");

        // Check corners are white (quiet zone)
        uint topLeft = BitConverter.ToUInt32(pixels, 0);
        if (topLeft != 0xFFFFFFFF)
            throw new Exception($"Top-left pixel should be white (quiet zone), got 0x{topLeft:X8}");

        Console.WriteLine($"[QR] CPU render: {w}x{h}, {pixels.Length} bytes ✓");
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════
    //  QR Code — GPU Renderer
    // ═══════════════════════════════════════════════════════════

    [TestMethod]
    public async Task QR_Render_GPU() => await RunTest(async accelerator =>
    {
        var (pixels, w, h) = await QRCode.GenerateAsync(
            accelerator, "https://spawndev.com", moduleSize: 4, quietZone: 2);

        if (w != h)
            throw new Exception($"QR image should be square: {w}x{h}");
        if (pixels.Length != w * h * 4)
            throw new Exception($"Pixel buffer size mismatch: {pixels.Length} vs expected {w * h * 4}");

        // Verify quiet zone corner is white
        uint topLeft = BitConverter.ToUInt32(pixels, 0);
        if (topLeft != 0xFFFFFFFF)
            throw new Exception($"Top-left pixel should be white (quiet zone), got 0x{topLeft:X8}");

        Console.WriteLine($"[QR] GPU render: {w}x{h}, {pixels.Length} bytes ✓");
    });

    [TestMethod]
    public async Task QR_Render_GPU_WithLogo() => await RunTest(async accelerator =>
    {
        // Create a small red square as the logo
        int logoSize = 20;
        var logo = new byte[logoSize * logoSize * 4];
        for (int i = 0; i < logo.Length; i += 4)
        {
            logo[i] = 0x00;     // B
            logo[i + 1] = 0x00; // G
            logo[i + 2] = 0xFF; // R
            logo[i + 3] = 0xFF; // A
        }

        var (pixels, w, h) = await QRCode.GenerateWithLogoAsync(
            accelerator, "https://hub.spawndev.com/compute",
            logo, logoSize, logoSize, moduleSize: 6);

        if (w != h)
            throw new Exception($"QR image should be square: {w}x{h}");

        // Verify center pixel is red (the logo)
        int centerIdx = (h / 2 * w + w / 2) * 4;
        if (centerIdx + 3 < pixels.Length)
        {
            byte b = pixels[centerIdx], g = pixels[centerIdx + 1], r = pixels[centerIdx + 2];
            if (r != 0xFF || g != 0x00 || b != 0x00)
                throw new Exception($"Center pixel should be red (logo), got R={r} G={g} B={b}");
        }

        Console.WriteLine($"[QR] GPU render with logo: {w}x{h} ✓");
    });

    [TestMethod]
    public async Task QR_Render_GPU_CPUMatch() => await RunTest(async accelerator =>
    {
        // GPU and CPU renders should produce identical output
        string text = "MATCH TEST";
        int moduleSize = 5;
        int quietZone = 3;

        var (cpuPixels, cpuW, cpuH) = QRCode.Generate(text, moduleSize: moduleSize, quietZone: quietZone);
        var (gpuPixels, gpuW, gpuH) = await QRCode.GenerateAsync(
            accelerator, text, moduleSize: moduleSize, quietZone: quietZone);

        if (cpuW != gpuW || cpuH != gpuH)
            throw new Exception($"Size mismatch: CPU={cpuW}x{cpuH}, GPU={gpuW}x{gpuH}");

        int mismatches = 0;
        for (int i = 0; i < cpuPixels.Length && i < gpuPixels.Length; i++)
        {
            if (cpuPixels[i] != gpuPixels[i])
                mismatches++;
        }

        if (mismatches > 0)
            throw new Exception($"GPU/CPU mismatch: {mismatches} bytes differ out of {cpuPixels.Length}");

        Console.WriteLine($"[QR] GPU/CPU render match: {cpuW}x{cpuH}, {cpuPixels.Length} bytes identical ✓");
    });

    [TestMethod]
    public Task QR_Decode_RoundTrip()
    {
        string original = "https://hub.spawndev.com";

        // Encode → render → decode
        var (pixels, w, h) = QRCode.Generate(original, ECLevel.M, moduleSize: 8, quietZone: 4);

        // The renderer outputs ARGB as uint (little-endian: BGRA in bytes)
        // The decoder expects RGBA. Swap B↔R.
        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);

        var decoded = QRDecoder.Decode(pixels, w, h);

        if (decoded == null)
            throw new Exception("QR decode returned null — could not read the QR code");
        if (decoded != original)
            throw new Exception($"QR round-trip mismatch: encoded '{original}', decoded '{decoded}'");

        Console.WriteLine($"[QR] Round-trip: '{original}' → encode → render → decode → '{decoded}' ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Decode_RoundTrip_WithLogo()
    {
        string original = "https://spawndev.com/p2p";

        // Create a small logo (white square)
        int logoSize = 16;
        var logo = new byte[logoSize * logoSize * 4];
        for (int i = 0; i < logo.Length; i++) logo[i] = 0xFF; // white

        var (pixels, w, h) = QRCode.GenerateWithLogo(
            original, logo, logoSize, logoSize, moduleSize: 8, quietZone: 4);

        // Swap B↔R for decoder
        for (int i = 0; i < pixels.Length; i += 4)
            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);

        var decoded = QRDecoder.Decode(pixels, w, h);

        if (decoded == null)
            throw new Exception("QR decode with logo returned null — logo may be too large or EC level insufficient");
        if (decoded != original)
            throw new Exception($"QR logo round-trip mismatch: encoded '{original}', decoded '{decoded}'");

        Console.WriteLine($"[QR] Logo round-trip: '{original}' → encode(H) → logo → decode → '{decoded}' ✓");
        return Task.CompletedTask;
    }

    // ═══════════════════════════════════════════════════════════
    //  QR Code - decoder correctness (found 2026-10-15, reading a code off a robot camera)
    //
    //  The decoder once (1) corrected more than one byte error wrongly and returned the damaged bytes as text, (2) binarised
    //  with a local mean taken from 9 sample points, which turned the dark core of a finder pattern light so large codes had
    //  no finder patterns at all, and (3) merged look-alike patterns from the data into the real finders. Measured before the
    //  fix: ONE flipped module decoded wrongly 31% of the time, ten flipped modules 87% wrong with no failure reported, and a
    //  clean version 5 code never decoded. These tests fail on that code.
    // ═══════════════════════════════════════════════════════════

    static string QRPayload(int length, int alphabet = 7)
        => new string(Enumerable.Range(0, length).Select(i => (char)('a' + (i * alphabet + 3) % 26)).ToArray());

    /// <summary>Inverts <paramref name="count"/> random data-area modules of a rendered code, never a finder, timing or alignment pattern.</summary>
    static byte[] QRFlipModules(byte[] pixels, int width, int moduleCount, int moduleSize, int quietZone, int count, Random rng)
    {
        bool InFunctionPattern(int r, int c) =>
            (r <= 8 && c <= 8) || (r <= 8 && c >= moduleCount - 8) || (r >= moduleCount - 8 && c <= 8)
            || r == 6 || c == 6
            || (moduleCount >= 25 && moduleCount < 45 && r >= moduleCount - 9 && r <= moduleCount - 5 && c >= moduleCount - 9 && c <= moduleCount - 5);
        var candidates = new List<(int r, int c)>();
        for (int r = 0; r < moduleCount; r++)
            for (int c = 0; c < moduleCount; c++)
                if (!InFunctionPattern(r, c)) candidates.Add((r, c));
        var copy = (byte[])pixels.Clone();
        foreach (var index in Enumerable.Range(0, candidates.Count).OrderBy(_ => rng.Next()).Take(count))
        {
            var (r, c) = candidates[index];
            for (int y = 0; y < moduleSize; y++)
                for (int x = 0; x < moduleSize; x++)
                {
                    int px = ((r + quietZone) * moduleSize + y) * width + (c + quietZone) * moduleSize + x;
                    for (int ch = 0; ch < 3; ch++) copy[px * 4 + ch] = (byte)(255 - copy[px * 4 + ch]);
                }
        }
        return copy;
    }

    [TestMethod]
    public Task QR_Decode_CorrectsModuleErrors()
    {
        // One Reed-Solomon block (version 2, level H repairs 11 byte errors) and several interleaved blocks (version 5).
        foreach (var text in new[] { "LNT1:c31d3c46", "LNT1:c31d3c46-multi-block-payload-0123456789" })
        {
            var (pixels, w, _) = QRCode.Generate(text, ecLevel: ECLevel.H, moduleSize: 6, quietZone: 4);
            int n = QRCode.GetModuleCount(text, ECLevel.H);
            var rng = new Random(20261015);
            foreach (int flips in new[] { 1, 2, 4, 8, 10 })
            {
                for (int trial = 0; trial < 40; trial++)
                {
                    var damaged = QRFlipModules(pixels, w, n, 6, 4, flips, rng);
                    var decoded = QRCode.Decode(damaged, w, w);
                    if (decoded != text)
                        throw new Exception($"{flips} flipped modules, '{text}': decoded {(decoded == null ? "null" : $"'{decoded}'")}, expected the text (trial {trial})");
                }
            }
        }
        Console.WriteLine("[QR] Reed-Solomon: 1-10 flipped modules decode exactly, single and multi-block ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Decode_BeyondCapacityNeverReturnsWrongText()
    {
        // Far past what the code can repair the decoder must say so (null), never hand back damaged bytes as text.
        const string text = "LNT1:c31d3c46";
        var (pixels, w, _) = QRCode.Generate(text, ecLevel: ECLevel.H, moduleSize: 6, quietZone: 4);
        int n = QRCode.GetModuleCount(text, ECLevel.H);
        var rng = new Random(7);
        int nulls = 0;
        for (int trial = 0; trial < 200; trial++)
        {
            var decoded = QRCode.Decode(QRFlipModules(pixels, w, n, 6, 4, 60, rng), w, w);
            if (decoded == null) nulls++;
            else if (decoded != text)
                throw new Exception($"60 flipped modules decoded as wrong text '{decoded}' (trial {trial})");
        }
        if (nulls < 190)
            throw new Exception($"Expected the decoder to report failure for nearly all 60-module damage, got {nulls}/200 nulls");
        Console.WriteLine($"[QR] Beyond capacity: {nulls}/200 reported failure, 0 wrong texts ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Decode_EveryPayloadLengthAtEveryECLevel()
    {
        // Clean codes of every length 1..120 at all four levels, two module sizes: versions 1 to about 8, single and multi-block.
        int checkedCount = 0;
        foreach (int moduleSize in new[] { 3, 6 })
            foreach (var ec in new[] { ECLevel.L, ECLevel.M, ECLevel.Q, ECLevel.H })
                for (int length = 1; length <= 120; length++)
                {
                    var text = QRPayload(length);
                    var (pixels, w, h) = QRCode.Generate(text, ecLevel: ec, moduleSize: moduleSize, quietZone: 4);
                    var decoded = QRCode.Decode(pixels, w, h);
                    if (decoded != text)
                        throw new Exception($"clean code, level {ec}, {length} chars, {moduleSize}px modules, version {(QRCode.GetModuleCount(text, ec) - 17) / 4}: decoded {(decoded == null ? "null" : $"'{decoded}'")}");
                    checkedCount++;
                }
        Console.WriteLine($"[QR] Clean decode: all {checkedCount} payload/level/size combinations exact ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Decode_SurvivesBlurAndLowResolution()
    {
        // What a camera does: lose resolution and blur. 12px modules reduced 4x (3px per module) and box-blurred.
        var ids = Enumerable.Range(0, 30).Select(i => "LNT1:" + Convert.ToHexString(BitConverter.GetBytes(0x9E3779B1u * (uint)(i + 1))).ToLowerInvariant()).ToArray();
        foreach (var text in ids)
        {
            var (pixels, w, h) = QRCode.Generate(text, ecLevel: ECLevel.H, moduleSize: 12, quietZone: 4);
            // Box blur radius 3, then 4x down-sample.
            var gray = new double[w * h];
            for (int i = 0; i < gray.Length; i++) gray[i] = pixels[i * 4];
            var blurred = new double[gray.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    double sum = 0; int count = 0;
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            int sx = Math.Clamp(x + dx, 0, w - 1), sy = Math.Clamp(y + dy, 0, h - 1);
                            sum += gray[sy * w + sx]; count++;
                        }
                    blurred[y * w + x] = sum / count;
                }
            int nw = w / 4, nh = h / 4;
            var small = new byte[nw * nh * 4];
            for (int y = 0; y < nh; y++)
                for (int x = 0; x < nw; x++)
                {
                    double sum = 0;
                    for (int dy = 0; dy < 4; dy++) for (int dx = 0; dx < 4; dx++) sum += blurred[(y * 4 + dy) * w + x * 4 + dx];
                    byte v = (byte)Math.Round(sum / 16);
                    int o = (y * nw + x) * 4;
                    small[o] = small[o + 1] = small[o + 2] = v; small[o + 3] = 255;
                }
            var decoded = QRCode.Decode(small, nw, nh);
            if (decoded != text)
                throw new Exception($"blurred 3px-module code '{text}' decoded as {(decoded == null ? "null" : $"'{decoded}'")}");
        }
        Console.WriteLine($"[QR] Blur + 3px modules: {ids.Length}/{ids.Length} exact ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public async Task QR_Decode_GPU_MatchesCpu() => await RunTest(async accelerator =>
    {
        // The GPU path binarises with kernels; it must read exactly what the CPU path reads, damaged codes included.
        var rng = new Random(11);
        // Payloads kept at or below version 5 so the module-flipping helper's function-pattern skip list is complete.
        foreach (var text in new[] { "LNT1:c31d3c46", QRPayload(30), QRPayload(40, 13) })
        {
            var (pixels, w, h) = QRCode.Generate(text, ecLevel: ECLevel.H, moduleSize: 5, quietZone: 4);
            int n = QRCode.GetModuleCount(text, ECLevel.H);
            foreach (int flips in new[] { 0, 3, 8 })
            {
                var image = flips == 0 ? pixels : QRFlipModules(pixels, w, n, 5, 4, flips, rng);
                var cpu = QRCode.Decode(image, w, h);
                var gpu = await QRCode.DecodeAsync(accelerator, image, w, h);
                if (cpu != gpu)
                    throw new Exception($"'{text}' with {flips} flipped modules: CPU decoded {(cpu ?? "null")}, GPU decoded {(gpu ?? "null")}");
                if (flips < 4 && gpu != text)
                    throw new Exception($"'{text}' with {flips} flipped modules was not read back exactly: {(gpu ?? "null")}");
            }
        }
        Console.WriteLine("[QR] GPU decode matches CPU decode, clean and damaged ✓");
    });

    // ═══════════════════════════════════════════════════════════
    //  QR Code - geometry: rotation and perspective (found 2026-10-15)
    //
    //  The decoder once located the code with a row scan that failed on ANY rotation (3 degrees missed the top-left finder, 10
    //  degrees found none) and sampled modules through an affine map of the three finder centres, so a code seen at an angle
    //  could not read. Now the finders are cross-checked along three directions (rotation-invariant), the module size is
    //  measured along each leg, and the modules are sampled through a projective transform anchored on the alignment pattern.
    //  Limits, measured: version 1 has no alignment pattern, so strong perspective on a version 1 code does not read; tilts
    //  past about 60 degrees do not read.
    // ═══════════════════════════════════════════════════════════

    static string[] QRIds(int count, string suffix = "")
        => Enumerable.Range(0, count).Select(i => "LNT1:" + Convert.ToHexString(BitConverter.GetBytes(0x9E3779B1u * (uint)(i + 1))).ToLowerInvariant() + suffix).ToArray();

    static double[] QRToGray(byte[] px, int w, int h)
    {
        var g = new double[w * h];
        for (int i = 0; i < g.Length; i++) g[i] = (px[i * 4] * 77 + px[i * 4 + 1] * 150 + px[i * 4 + 2] * 29) / 256.0;
        return g;
    }

    static byte[] QRToRgba(double[] g)
    {
        var o = new byte[g.Length * 4];
        for (int i = 0; i < g.Length; i++)
        {
            byte v = (byte)Math.Clamp((int)Math.Round(g[i]), 0, 255);
            o[i * 4] = o[i * 4 + 1] = o[i * 4 + 2] = v;
            o[i * 4 + 3] = 255;
        }
        return o;
    }

    /// <summary>The homography taking four source points to four destination points (Gaussian elimination, 8 unknowns).</summary>
    static double[] QRHomography((double x, double y)[] s, (double x, double y)[] d)
    {
        var a = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            a[2 * i, 0] = s[i].x; a[2 * i, 1] = s[i].y; a[2 * i, 2] = 1; a[2 * i, 6] = -s[i].x * d[i].x; a[2 * i, 7] = -s[i].y * d[i].x; a[2 * i, 8] = d[i].x;
            a[2 * i + 1, 3] = s[i].x; a[2 * i + 1, 4] = s[i].y; a[2 * i + 1, 5] = 1; a[2 * i + 1, 6] = -s[i].x * d[i].y; a[2 * i + 1, 7] = -s[i].y * d[i].y; a[2 * i + 1, 8] = d[i].y;
        }
        for (int c = 0; c < 8; c++)
        {
            int p = c;
            for (int r = c + 1; r < 8; r++) if (Math.Abs(a[r, c]) > Math.Abs(a[p, c])) p = r;
            for (int k = 0; k < 9; k++) (a[c, k], a[p, k]) = (a[p, k], a[c, k]);
            for (int r = 0; r < 8; r++)
            {
                if (r == c) continue;
                double f = a[r, c] / a[c, c];
                for (int k = c; k < 9; k++) a[r, k] -= f * a[c, k];
            }
        }
        var h = new double[9];
        for (int i = 0; i < 8; i++) h[i] = a[i, 8] / a[i, i];
        h[8] = 1;
        return h;
    }

    /// <summary>
    /// A flat code photographed from an angle: rotated by <paramref name="yawDeg"/> about its vertical axis and
    /// <paramref name="pitchDeg"/> about its horizontal axis, seen by a pinhole camera <paramref name="distance"/> code-sides
    /// away, then rolled in the image plane. Light background where the code does not reach.
    /// </summary>
    static double[] QRView(double[] g, int w, int h, double yawDeg, double pitchDeg, double rollDeg, double distance = 3.0)
    {
        var corners = new[] { (-0.5, -0.5), (0.5, -0.5), (0.5, 0.5), (-0.5, 0.5) };
        double yaw = yawDeg * Math.PI / 180, pitch = pitchDeg * Math.PI / 180, roll = rollDeg * Math.PI / 180;
        var dst = new (double x, double y)[4];
        for (int i = 0; i < 4; i++)
        {
            double x = corners[i].Item1, y = corners[i].Item2;
            double x1 = x * Math.Cos(yaw), z1 = -x * Math.Sin(yaw);
            double y2 = y * Math.Cos(pitch) - z1 * Math.Sin(pitch), z2 = y * Math.Sin(pitch) + z1 * Math.Cos(pitch);
            double u = x1 * distance / (distance + z2), v = y2 * distance / (distance + z2);
            double ur = u * Math.Cos(roll) - v * Math.Sin(roll), vr = u * Math.Sin(roll) + v * Math.Cos(roll);
            dst[i] = (w / 2.0 + ur * w * 0.55, h / 2.0 + vr * h * 0.55);
        }
        var src = new (double x, double y)[] { (0, 0), (w, 0), (w, h), (0, h) };
        var hm = QRHomography(dst, src); // view pixel -> code pixel
        var o = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double den = hm[6] * x + hm[7] * y + hm[8];
                double sx = (hm[0] * x + hm[1] * y + hm[2]) / den, sy = (hm[3] * x + hm[4] * y + hm[5]) / den;
                if (sx < 0 || sy < 0 || sx >= w - 1 || sy >= h - 1) { o[y * w + x] = 255; continue; }
                int x0 = (int)sx, y0 = (int)sy;
                double fx = sx - x0, fy = sy - y0;
                o[y * w + x] = g[y0 * w + x0] * (1 - fx) * (1 - fy) + g[y0 * w + x0 + 1] * fx * (1 - fy)
                    + g[(y0 + 1) * w + x0] * (1 - fx) * fy + g[(y0 + 1) * w + x0 + 1] * fx * fy;
            }
        return o;
    }

    static double[] QRBoxBlur(double[] g, int w, int h, int radius)
    {
        if (radius == 0) return g;
        var t = new double[g.Length];
        var o = new double[g.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int d = -radius; d <= radius; d++) s += g[y * w + Math.Clamp(x + d, 0, w - 1)];
                t[y * w + x] = s / (2 * radius + 1);
            }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double s = 0;
                for (int d = -radius; d <= radius; d++) s += t[Math.Clamp(y + d, 0, h - 1) * w + x];
                o[y * w + x] = s / (2 * radius + 1);
            }
        return o;
    }

    static double[] QRDownsample(double[] g, int w, int h, int factor, out int nw, out int nh)
    {
        nw = w / factor; nh = h / factor;
        var o = new double[nw * nh];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                double s = 0;
                for (int dy = 0; dy < factor; dy++) for (int dx = 0; dx < factor; dx++) s += g[(y * factor + dy) * w + x * factor + dx];
                o[y * nw + x] = s / (factor * factor);
            }
        return o;
    }

    /// <summary>Decodes each payload after <paramref name="degrade"/>; returns how many read exactly and fails if any read WRONG.</summary>
    static int QRCountExact(string[] payloads, int moduleSize, Func<double[], int, int, Random, (double[] g, int w, int h)> degrade, string what)
    {
        int exact = 0;
        var rng = new Random(77);
        foreach (var text in payloads)
        {
            var (px, w, h) = QRCode.Generate(text, ecLevel: ECLevel.H, moduleSize: moduleSize, quietZone: 8);
            var (g, nw, nh) = degrade(QRToGray(px, w, h), w, h, rng);
            var decoded = QRCode.Decode(QRToRgba(g), nw, nh);
            if (decoded == text) exact++;
            else if (decoded != null) throw new Exception($"{what}: '{text}' decoded as WRONG text '{decoded}'");
        }
        return exact;
    }

    [TestMethod]
    public Task QR_Decode_ReadsRotatedCodes()
    {
        var ids = QRIds(12);
        foreach (double angle in new[] { 3.0, 8.0, 15.0, 30.0, 45.0, 60.0, 90.0, 135.0, 180.0, 270.0 })
        {
            int exact = QRCountExact(ids, 10, (g, w, h, r) => (QRView(g, w, h, 0, 0, angle, 1e6), w, h), $"rotated {angle}");
            if (exact != ids.Length) throw new Exception($"rotated {angle} degrees (version 2): {exact}/{ids.Length} read");
        }
        // Version 1 (no alignment pattern) and version 5 (several Reed-Solomon blocks).
        var v1 = Enumerable.Range(0, 12).Select(i => "LN" + (char)('a' + i % 26) + (char)('a' + (i * 3) % 26)).ToArray();
        var v5 = QRIds(12, "-multi-block-payload-0123456789");
        if (QRCountExact(v1, 10, (g, w, h, r) => (QRView(g, w, h, 0, 0, 17, 1e6), w, h), "rotated v1") != v1.Length) throw new Exception("rotated 17 degrees (version 1) not all read");
        if (QRCountExact(v5, 10, (g, w, h, r) => (QRView(g, w, h, 0, 0, 17, 1e6), w, h), "rotated v5") != v5.Length) throw new Exception("rotated 17 degrees (version 5) not all read");
        Console.WriteLine("[QR] Rotation 3-270 degrees, versions 1, 2 and 5: all read exactly ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Decode_ReadsPerspectiveViews()
    {
        var ids = QRIds(12);
        var v5 = QRIds(12, "-multi-block-payload-0123456789");
        foreach (double yaw in new[] { 15.0, 30.0, 45.0, 55.0 })
        {
            int exact = QRCountExact(ids, 10, (g, w, h, r) => (QRView(g, w, h, yaw, 0, 0), w, h), $"yaw {yaw}");
            if (exact != ids.Length) throw new Exception($"yaw tilt {yaw} degrees (version 2): {exact}/{ids.Length} read");
        }
        foreach (double yaw in new[] { 30.0, 55.0 })
        {
            int exact = QRCountExact(v5, 10, (g, w, h, r) => (QRView(g, w, h, yaw, 0, 0), w, h), $"yaw {yaw} v5");
            if (exact != v5.Length) throw new Exception($"yaw tilt {yaw} degrees (version 5): {exact}/{v5.Length} read");
        }
        int pitch = QRCountExact(ids, 10, (g, w, h, r) => (QRView(g, w, h, 0, 40, 0), w, h), "pitch 40");
        if (pitch != ids.Length) throw new Exception($"pitch tilt 40 degrees: {pitch}/{ids.Length} read");
        int mixed = QRCountExact(ids, 10, (g, w, h, r) => (QRView(g, w, h, 35, 20, 12), w, h), "mixed");
        if (mixed != ids.Length) throw new Exception($"yaw 35 + pitch 20 + roll 12: {mixed}/{ids.Length} read");
        Console.WriteLine("[QR] Perspective: yaw to 55, pitch 40, mixed tilt and roll; versions 2 and 5 all read exactly ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Decode_ReadsCameraLikeViews()
    {
        var ids = QRIds(20);
        // About 6 camera pixels per module, tilted 30 degrees and rolled 8, blurred, then with sensor noise added.
        Func<double[], int, int, Random, double, (double[] g, int w, int h)> camera = (g, w, h, rng, noise) =>
        {
            var view = QRBoxBlur(QRView(g, w, h, 30, 0, 8), w, h, 2);
            var small = QRDownsample(view, w, h, 2, out int nw, out int nh);
            if (noise > 0)
                for (int i = 0; i < small.Length; i++) small[i] += noise * (rng.NextDouble() + rng.NextDouble() + rng.NextDouble() - 1.5) * 1.55;
            return (small, nw, nh);
        };
        int clean = QRCountExact(ids, 13, (g, w, h, r) => camera(g, w, h, r, 0), "camera clean");
        if (clean != ids.Length) throw new Exception($"camera-like view (6.5 px modules, yaw 30, roll 8, blur): {clean}/{ids.Length} read");
        int noisy = QRCountExact(ids, 13, (g, w, h, r) => camera(g, w, h, r, 20), "camera noisy");
        if (noisy < 17)
            throw new Exception($"camera-like view with sensor noise (sigma 20): only {noisy}/{ids.Length} read, expected at least 17");
        // Dark scene (the robot camera's frame is mostly black with the screen in it) and low contrast.
        int dark = QRCountExact(ids, 10, (g, w, h, r) => { var v = QRView(g, w, h, 30, 0, 8); for (int i = 0; i < v.Length; i++) v[i] = 2 + v[i] * 28 / 255; return (v, w, h); }, "dark");
        if (dark != ids.Length) throw new Exception($"dark scene (levels 2..30), yaw 30: {dark}/{ids.Length} read");
        int low = QRCountExact(ids, 10, (g, w, h, r) => { var v = QRView(g, w, h, 0, 0, 8, 1e6); for (int i = 0; i < v.Length; i++) v[i] = 90 + v[i] * 70 / 255; return (v, w, h); }, "low contrast");
        if (low != ids.Length) throw new Exception($"low contrast (levels 90..160), roll 8: {low}/{ids.Length} read");
        Console.WriteLine($"[QR] Camera-like: clean {clean}/{ids.Length}, noisy {noisy}/{ids.Length}, dark {dark}/{ids.Length}, low contrast {low}/{ids.Length}; never wrong text ✓");
        return Task.CompletedTask;
    }

    [TestMethod]
    public Task QR_Encode_LongURL()
    {
        // Test with a realistic P2P join link
        string url = "https://hub.spawndev.com/compute/join?compute=a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2&n=Demo+Compute+Swarm";
        var matrix = QREncoder.Encode(url, ECLevel.H);
        int size = matrix.GetLength(0);

        if (size < 21)
            throw new Exception($"Long URL QR too small: {size}");

        Console.WriteLine($"[QR] Long URL encode: {url.Length} chars → {size}x{size} (EC=H) ✓");
        return Task.CompletedTask;
    }
}
