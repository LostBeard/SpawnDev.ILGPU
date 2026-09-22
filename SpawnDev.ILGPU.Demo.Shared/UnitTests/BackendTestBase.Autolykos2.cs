using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Crypto;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests;

/// <summary>
/// Autolykos2 (Ergo proof-of-work) GPU kernel PoC - correctness tests.
/// Tier 1 only (small/no-N, all viable backends). See
/// D:\users\tj\Projects\_Brainstorm_2026_09_21\crypto-mining-funding-analysis.md for why
/// this exists, and the plan file for the full design (dataset generation + mining kernel
/// tiers land in follow-up commits).
/// </summary>
public abstract partial class BackendTestBase
{
    // ═══════════════════════════════════════════════════════════
    //  Autolykos2 - Blake2b-256 primitive
    // ═══════════════════════════════════════════════════════════

    // Five test vectors, each independently computed via Python's hashlib.blake2b
    // (digest_size=32) - not from memory or a copied table, so there is no risk of
    // silently testing against a wrong "known-good" value. Covers: empty input, a
    // short ASCII string, an odd (non-8-aligned) length, exactly one full block with
    // no padding (128 bytes), and a half-full block (64 bytes) - the padding-boundary
    // cases Autolykos2's actual call shapes (nonce hash, index derivation, sum hash)
    // will all fall inside.
    private readonly record struct Blake2bVector(
        string Name, int ByteLength,
        ulong M0, ulong M1, ulong M2, ulong M3, ulong M4, ulong M5, ulong M6, ulong M7,
        ulong M8, ulong M9, ulong M10, ulong M11, ulong M12, ulong M13, ulong M14, ulong M15,
        ulong Out0, ulong Out1, ulong Out2, ulong Out3);

    private static readonly Blake2bVector[] Blake2bVectors =
    {
        new("empty", 0,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0xb243e526c051570eUL, 0xa1da9960b02eabe8UL, 0x87778f7747dfe5d1UL, 0xa8e32ff1cd45abfaUL),

        new("abc", 3,
            0x0000000000636261UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x723942633c81ddbdUL, 0x9b5798ee3fef7131UL, 0x423ecbb13b4e9694UL, 0x1923d568c0c86272UL),

        new("TheQuickBrownFox", 19,
            0x6369757120656854UL, 0x206e776f7262206bUL, 0x0000000000786f66UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x77604c336921e1edUL, 0x63063f64828f45daUL, 0x67a53380635b8d44UL, 0xc012e39c5041a746UL),

        new("exactly128_range0to127", 128,
            0x0706050403020100UL, 0x0f0e0d0c0b0a0908UL, 0x1716151413121110UL, 0x1f1e1d1c1b1a1918UL,
            0x2726252423222120UL, 0x2f2e2d2c2b2a2928UL, 0x3736353433323130UL, 0x3f3e3d3c3b3a3938UL,
            0x4746454443424140UL, 0x4f4e4d4c4b4a4948UL, 0x5756555453525150UL, 0x5f5e5d5c5b5a5958UL,
            0x6766656463626160UL, 0x6f6e6d6c6b6a6968UL, 0x7776757473727170UL, 0x7f7e7d7c7b7a7978UL,
            0x66beb2eb712f58c3UL, 0xe9aa0bf850d75dfaUL, 0x8b3c6615b0f35475UL, 0xd1c18824cbcf77e3UL),

        new("64bytes_range0to63", 64,
            0x0706050403020100UL, 0x0f0e0d0c0b0a0908UL, 0x1716151413121110UL, 0x1f1e1d1c1b1a1918UL,
            0x2726252423222120UL, 0x2f2e2d2c2b2a2928UL, 0x3736353433323130UL, 0x3f3e3d3c3b3a3938UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL, 0x0000000000000000UL,
            0x3909b034d5e6d810UL, 0x8ce4dac4dce93f84UL, 0xb1822b8b6b8f00dfUL, 0xf58748874d40f556UL),
    };

    [TestMethod]
    public Task Autolykos2_Blake2b_RFC7693TestVectors()
    {
        foreach (var v in Blake2bVectors)
        {
            Blake2b.Hash256(
                v.M0, v.M1, v.M2, v.M3, v.M4, v.M5, v.M6, v.M7,
                v.M8, v.M9, v.M10, v.M11, v.M12, v.M13, v.M14, v.M15,
                v.ByteLength,
                out var o0, out var o1, out var o2, out var o3);

            if (o0 != v.Out0 || o1 != v.Out1 || o2 != v.Out2 || o3 != v.Out3)
                throw new Exception(
                    $"Blake2b '{v.Name}' mismatch: got {o0:x16}{o1:x16}{o2:x16}{o3:x16}, " +
                    $"expected {v.Out0:x16}{v.Out1:x16}{v.Out2:x16}{v.Out3:x16}");
        }

        Console.WriteLine($"[Autolykos2] Blake2b RFC7693 vectors: {Blake2bVectors.Length}/{Blake2bVectors.Length} match ✓");
        return Task.CompletedTask;
    }

    private static void Blake2bBatchKernel(
        Index1D index,
        ArrayView<ulong> m, // vectorCount x 16 words, row-major
        ArrayView<int> byteLengths,
        ArrayView<ulong> outDigests) // vectorCount x 4 words, row-major
    {
        int baseM = index * 16;
        Blake2b.Hash256(
            m[baseM + 0], m[baseM + 1], m[baseM + 2], m[baseM + 3],
            m[baseM + 4], m[baseM + 5], m[baseM + 6], m[baseM + 7],
            m[baseM + 8], m[baseM + 9], m[baseM + 10], m[baseM + 11],
            m[baseM + 12], m[baseM + 13], m[baseM + 14], m[baseM + 15],
            byteLengths[index],
            out ulong o0, out ulong o1, out ulong o2, out ulong o3);

        int baseOut = index * 4;
        outDigests[baseOut + 0] = o0;
        outDigests[baseOut + 1] = o1;
        outDigests[baseOut + 2] = o2;
        outDigests[baseOut + 3] = o3;
    }

    // KNOWN OPEN ISSUE (found running this test 2026-09-21, re-investigated same day after
    // fixing the separate WebGPU widening bug below): WebGL fails this test with a wrong (not a
    // graceful skip/exception) digest even for the all-zero-message "empty" vector - CPU/CUDA/
    // OpenCL/WebGPU (both subgroup variants)/Wasm all pass. Ruled out so far, each independently:
    // - The GLSL i64 shift emulation (GLSLEmulationLibrary.cs i64_shl/u64_shr/i64_shr) by hand for
    //   every rotate amount Blake2b uses (1, 16, 24, 32, 63) - all correct.
    // - "The isLastBlock bool parameter isn't reaching the shader" - computed what the digest
    //   would be with isLastBlock forced false - doesn't match the wrong output either.
    // - The same uint-widening sign-extension mistake just fixed in WGSLKernelFunctionGenerator
    //   (see the fixed note above) - checked GLSLEmulationLibrary's i64_from_i32/u64_from_u32 and
    //   the GLSL kernel-function-generator's own ConvertValue handling; not the same bug pattern.
    // - Dumped the actual generated GLSL offline (ShaderCompiler.Generate with
    //   CapabilityProfiles.WebGL2Baseline, no GPU context needed - mirrors the technique that
    //   found the WGSL bug) and spot-checked the IV/param-block initialization constants by hand
    //   (v_1 = h0 = IV0^Param0, v_9 = v[8] = IV0 unmodified - both correct). The output is ~5600
    //   lines of fully-unrolled emu_i64 (uvec2) arithmetic with no per-thread variance (this test
    //   vector's message is all zero), so there's no sharp reproduction lever the way "index 128"
    //   was for the WGSL bug - tracing further requires either instrumenting intermediate values
    //   or comparing round-by-round against a reference trace, not spot-checking by eye. Stopped
    //   here rather than continue an unbounded manual trace through ~500+ generated locals.
    // Root cause is still open; this is a SpawnDev.ILGPU WebGL backend bug, not an Autolykos2/
    // Blake2b logic bug (the same primitive is correct on every other backend, including the
    // other i64-emulated one, WebGPU). Not chased further: WebGL is out of scope for production-
    // scale mining regardless (see crypto-mining-funding-analysis.md and the plan file - multi-GB
    // resident buffers exceed practical WebGL limits independent of this). Whoever picks this up
    // next should instrument round-by-round intermediate v[] values (e.g. write them to an output
    // buffer after each of the 12 rounds) and diff against a CPU trace of the same rounds, rather
    // than re-deriving the emulation library by hand as both attempts here did.
    [TestMethod]
    public async Task Autolykos2_Blake2b_GPU_CPUMatch() => await RunTest(async accelerator =>
    {
        int n = Blake2bVectors.Length;
        var mHost = new ulong[n * 16];
        var lenHost = new int[n];
        for (int i = 0; i < n; i++)
        {
            var v = Blake2bVectors[i];
            mHost[i * 16 + 0] = v.M0; mHost[i * 16 + 1] = v.M1; mHost[i * 16 + 2] = v.M2; mHost[i * 16 + 3] = v.M3;
            mHost[i * 16 + 4] = v.M4; mHost[i * 16 + 5] = v.M5; mHost[i * 16 + 6] = v.M6; mHost[i * 16 + 7] = v.M7;
            mHost[i * 16 + 8] = v.M8; mHost[i * 16 + 9] = v.M9; mHost[i * 16 + 10] = v.M10; mHost[i * 16 + 11] = v.M11;
            mHost[i * 16 + 12] = v.M12; mHost[i * 16 + 13] = v.M13; mHost[i * 16 + 14] = v.M14; mHost[i * 16 + 15] = v.M15;
            lenHost[i] = v.ByteLength;
        }

        using var mBuf = accelerator.Allocate1D(mHost);
        using var lenBuf = accelerator.Allocate1D(lenHost);
        using var outBuf = accelerator.Allocate1D<ulong>(n * 4);

        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<ulong>, ArrayView<int>, ArrayView<ulong>>(Blake2bBatchKernel);
        kernel(n, mBuf.View, lenBuf.View, outBuf.View);
        await accelerator.SynchronizeAsync();

        var outHost = await outBuf.CopyToHostAsync();

        for (int i = 0; i < n; i++)
        {
            var v = Blake2bVectors[i];
            ulong o0 = outHost[i * 4 + 0], o1 = outHost[i * 4 + 1], o2 = outHost[i * 4 + 2], o3 = outHost[i * 4 + 3];
            if (o0 != v.Out0 || o1 != v.Out1 || o2 != v.Out2 || o3 != v.Out3)
                throw new Exception(
                    $"Blake2b GPU '{v.Name}' mismatch on {BackendName}: got {o0:x16}{o1:x16}{o2:x16}{o3:x16}, " +
                    $"expected {v.Out0:x16}{v.Out1:x16}{v.Out2:x16}{v.Out3:x16}");
        }

        Console.WriteLine($"[Autolykos2] Blake2b GPU/CPU match on {BackendName}: {n}/{n} ✓");
    });

    // ═══════════════════════════════════════════════════════════
    //  Autolykos2 - dataset (N-table) generation, small N
    // ═══════════════════════════════════════════════════════════

    // FIXED (2026-09-21): WebGPU (both subgroup variants) used to fail at exactly element index
    // 128, the first index where Bswap32(index) has bit 31 set (Bswap32(127)=0x7F000000,
    // Bswap32(128)=0x80000000) - elements 0..127 all matched CPU. Root-caused via
    // SpawnDev.ILGPU.DemoConsole's (now-removed) autolykos2-wgsl-dump offline probe: a genuine
    // SpawnDev.ILGPU WGSL codegen bug in WGSLKernelFunctionGenerator.GenerateCode(ConvertValue) -
    // a `uint` widening to a 64-bit type was unconditionally sign-extended via `i64_from_i32`
    // instead of checking the already-computed-but-unused `isSourceUnsigned` flag, because the
    // dead `targetType == "emu_u64"` check it used instead can never be true (emu_i64 and emu_u64
    // are both `alias ... = vec2&lt;u32&gt;`, and this backend's TypeGenerator only ever emits the
    // string "emu_i64"). Fixed in that file; verified against the FULL PlaywrightMultiTest suite
    // (4447 tests) with zero regressions - only the already-documented, unrelated failures below
    // and on the mining test remain.
    //
    // WebGL fails this test too, but earlier and differently: a vertex shader COMPILE error
    // ("cannot convert from highp 2-component vector of uint to flat out highp uint"), not a
    // wrong-answer at runtime. Root-caused precisely (2026-09-21) via an offline GLSL dump
    // (ShaderCompiler.Generate with CapabilityProfiles.WebGL2Baseline): GLSLKernelFunctionGenerator.
    // GetBufferElementType maps a 64-bit struct FIELD (e.g. this Element256's ulong fields) to the
    // scalar string "uint" instead of "uvec2" (its true GLSL representation) - correct for
    // GetBufferElementType's own non-struct callers, which branch on isEmulatedI64/F64 separately
    // and never use that string for such params, but wrong for struct-field flattening
    // (FlattenStructFields/GenerateStructFieldPaths), which takes the returned string as the
    // field's real type. This makes the TF output declaration loop (~line 1721 at the time of
    // writing) declare a scalar `uint` varying for a field whose actual value is `uvec2`, which
    // is exactly the reported dimension-mismatch error.
    //
    // ATTEMPTED A FULL FIX the same day and reverted it: correcting the field-type string is a
    // small, safe change (add a GetStructFieldGlslType wrapper used only by the struct-flattening
    // paths), but it's not sufficient alone - the TF output DECLARATION and STORE code must also
    // split such a field into lo/hi scalar outputs (mirroring the existing non-struct
    // isEmulatedI64/F64 whole-value handling right above it), and CRITICALLY the JS-side readback
    // (wwwroot/glWorker.js, the struct-reconstruction branch around "out.fieldIndex === 0") assumes
    // every struct field is 4 bytes when computing structElemSize and per-field byte offsets -
    // it needs the same fix, computed per-field (4 bytes normally, 8 for a lo/hi-paired field).
    // Implementing all three (declaration, store, JS readback) got the shader to COMPILE, but the
    // test then hung - a Playwright timeout waiting for the test's own "Run" button locator, not a
    // clean assertion failure - most likely a WebGL Transform-Feedback separate-attribute limit
    // being exceeded now that a 4-ulong-field struct needs 8 TF outputs instead of 4 (x4 for this
    // test's 4 chunk-buffer parameters), though this wasn't confirmed with live browser DevTools.
    // A silent hang is worse than a clean compile error, so the attempt was reverted rather than
    // shipped half-working (git diff of GLSLKernelFunctionGenerator.cs and glWorker.js from this
    // session's earlier commits shows exactly what was tried, if picking this back up).
    // Whoever continues this needs live browser DevTools (chrome://inspect or CDP) to see the
    // actual console error/GL error when the hang happens, not more static code reading - both
    // WebGL bugs on this page have now had two rounds of static analysis each without a full fix.
    [TestMethod]
    public async Task Autolykos2_DatasetGeneration_SmallN_GPU_CPUMatch() => await RunTest(async accelerator =>
    {
        const int n = 2000; // small N for fast cross-backend iteration, not real mainnet scale (tier 2)
        const uint height = 1_500_000; // arbitrary plausible Ergo mainnet height, fixed for reproducibility

        using var datasetBuf = accelerator.Allocate1D<Element256>(n);
        // Unused chunk slots are tiny real 1-element buffers, not ArrayView.Empty - an empty/
        // null-backed view as a kernel argument crashes WebGPU's launch path (NullReferenceException
        // in WebGPUAccelerator.RunKernel) and silently corrupts results on Wasm, even though the
        // kernel logic itself never dereferences an unused chunk (elementsPerChunk == n routes every
        // real index to chunk0). A real backing buffer per parameter is the portable choice.
        using var unusedChunk1 = accelerator.Allocate1D<Element256>(1);
        using var unusedChunk2 = accelerator.Allocate1D<Element256>(1);
        using var unusedChunk3 = accelerator.Allocate1D<Element256>(1);
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, uint, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, uint>(
            Autolykos2.GenerateDatasetKernel);
        kernel(n, (uint)n, datasetBuf.View, unusedChunk1.View, unusedChunk2.View, unusedChunk3.View, height);
        await accelerator.SynchronizeAsync();

        var gpuDataset = await datasetBuf.CopyToHostAsync();

        int mismatches = 0;
        for (int i = 0; i < n; i++)
        {
            Autolykos2.GenerateDatasetElement((uint)i, height, out ulong e0, out ulong e1, out ulong e2, out ulong e3);
            var gpu = gpuDataset[i];
            if (gpu.W0 != e0 || gpu.W1 != e1 || gpu.W2 != e2 || gpu.W3 != e3)
            {
                if (mismatches < 5)
                    throw new Exception(
                        $"Autolykos2 dataset element {i} mismatch on {BackendName}: " +
                        $"GPU={gpu.W0:x16}{gpu.W1:x16}{gpu.W2:x16}{gpu.W3:x16} CPU={e0:x16}{e1:x16}{e2:x16}{e3:x16}");
                mismatches++;
            }
        }

        Console.WriteLine($"[Autolykos2] Dataset generation GPU/CPU match on {BackendName}: {n}/{n} elements ✓");
    });

    // ═══════════════════════════════════════════════════════════
    //  Autolykos2 - mining kernel, small N
    // ═══════════════════════════════════════════════════════════

    // KNOWN OPEN ISSUES (found running this test 2026-09-21), neither chased further for the
    // same out-of-scope-for-mining reason as the issues documented on the other two tests above:
    // - Both WebGPU variants (subgroups and no-subgroups) time out (>30s) on this kernel as of the
    //   dataset-chunking parameter shape (4 chunk ArrayViews + elementsPerChunk, header/target
    //   packed into Element256 structs to stay under LoadAutoGroupedStreamKernel's 15-type-param
    //   ceiling). Before that change, the subgroup-enabled variant passed this test cleanly and
    //   quickly and only the no-subgroups variant timed out - so the extra parameters pushed the
    //   subgroup-enabled path into the same pathologically slow regime, not a new distinct bug.
    //   Not a correctness bug (the small, non-chunked correctness case this test exercises isn't
    //   where the slowness is - it's dispatch/compile-time related to the parameter shape itself).
    // - Wasm: reports 0 hits when exactly 1 is expected (the known nonce goes missing, no false
    //   positives) - Wasm passed both the Blake2b and dataset-generation tests above cleanly, so
    //   this points specifically at the atomic-allocate-then-scatter-write pattern
    //   (Atomic.Add(ref winningCount[0], 1) then winningNonces[slot] = nonce), not at the hash
    //   math itself.
    [TestMethod]
    public async Task Autolykos2_Mine_SmallN_FindsKnownHits() => await RunTest(async accelerator =>
    {
        const int n = 128; // small N, fast to generate and to gather from on every backend
        const uint height = 1_500_000;
        const int nonceRange = 256; // small enough that an exhaustive CPU scan is instant
        const ulong nonceBase = 0;

        // Arbitrary but fixed 32-byte "block header hash" for reproducibility.
        const ulong h0m = 0x0102030405060708UL, h1m = 0x1112131415161718UL,
                    h2m = 0x2122232425262728UL, h3m = 0x3132333435363738UL;

        // Build the small dataset once (GPU dispatch, already proven correct by the prior test)
        // and copy it to host so the CPU reference path has a plain array to index.
        using var datasetBuf = accelerator.Allocate1D<Element256>(n);
        // See the dataset-generation test above for why these are real 1-element buffers, not
        // ArrayView.Empty (WebGPU crashes, Wasm silently corrupts results on an empty view arg).
        using var unusedChunk1 = accelerator.Allocate1D<Element256>(1);
        using var unusedChunk2 = accelerator.Allocate1D<Element256>(1);
        using var unusedChunk3 = accelerator.Allocate1D<Element256>(1);
        var genKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, uint, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, uint>(
            Autolykos2.GenerateDatasetKernel);
        genKernel(n, (uint)n, datasetBuf.View, unusedChunk1.View, unusedChunk2.View, unusedChunk3.View, height);
        await accelerator.SynchronizeAsync();
        Element256[] hostDataset = await datasetBuf.CopyToHostAsync();

        // Digest comparison is a standard big-number "value < target" (word0 most significant),
        // so a target built from an arbitrary nonce's own digest is NOT tight - roughly half of
        // all other nonces will have a numerically smaller digest and hit too (word0 alone
        // decides it about half the time). The deterministic way to get a single guaranteed hit:
        // find the nonce with the numerically SMALLEST digest across the whole tested range, and
        // set the target one increment above exactly that digest - nothing else in the range can
        // be smaller than the range's own minimum, by definition.
        ulong minNonce = 0, minD0 = ulong.MaxValue, minD1 = 0, minD2 = 0, minD3 = 0;
        for (ulong nonce = 0; nonce < nonceRange; nonce++)
        {
            Autolykos2.ComputeNonceDigest(hostDataset, n, nonce, h0m, h1m, h2m, h3m,
                out ulong dd0, out ulong dd1, out ulong dd2, out ulong dd3);
            bool smaller = dd0 < minD0 || (dd0 == minD0 && (dd1 < minD1 || (dd1 == minD1 &&
                (dd2 < minD2 || (dd2 == minD2 && dd3 < minD3)))));
            if (nonce == 0 || smaller) { minNonce = nonce; minD0 = dd0; minD1 = dd1; minD2 = dd2; minD3 = dd3; }
        }
        ulong target0 = minD0, target1 = minD1, target2 = minD2, target3 = minD3 + 1;

        var expectedHits = new System.Collections.Generic.HashSet<ulong>();
        for (ulong nonce = 0; nonce < nonceRange; nonce++)
        {
            if (Autolykos2.MineNonceReference(hostDataset, n, nonce, h0m, h1m, h2m, h3m,
                target0, target1, target2, target3))
                expectedHits.Add(nonce);
        }
        if (expectedHits.Count != 1 || !expectedHits.Contains(minNonce))
            throw new Exception(
                $"Test setup bug: expected exactly nonce {minNonce} (the range's minimum digest) to hit, " +
                $"got {expectedHits.Count} hit(s): [{string.Join(",", expectedHits)}]");

        // Run the actual GPU kernel over the same nonce range and collect what it reports.
        const int maxWinners = 32;
        using var winningNonces = accelerator.Allocate1D<ulong>(maxWinners);
        using var winningCount = accelerator.Allocate1D<int>(1);
        winningCount.MemSetToZero();

        var mineKernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, uint, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, ArrayView<Element256>, uint,
            ulong, Element256, Element256, ArrayView<ulong>, ArrayView<int>>(Autolykos2.MineKernel);
        mineKernel(nonceRange, (uint)n, datasetBuf.View, unusedChunk1.View, unusedChunk2.View, unusedChunk3.View,
            n, nonceBase, new Element256(h0m, h1m, h2m, h3m), new Element256(target0, target1, target2, target3),
            winningNonces.View, winningCount.View);
        await accelerator.SynchronizeAsync();

        int gpuCount = (await winningCount.CopyToHostAsync())[0];
        if (gpuCount > maxWinners)
            throw new Exception($"Autolykos2 mining on {BackendName}: {gpuCount} hits overflowed the {maxWinners}-slot buffer - loosen the test target.");

        var gpuNonces = await winningNonces.CopyToHostAsync();
        var gpuHits = new System.Collections.Generic.HashSet<ulong>();
        for (int i = 0; i < gpuCount; i++) gpuHits.Add(gpuNonces[i]);

        if (!gpuHits.SetEquals(expectedHits))
        {
            var missing = new System.Collections.Generic.List<ulong>();
            var extra = new System.Collections.Generic.List<ulong>();
            foreach (var e in expectedHits) if (!gpuHits.Contains(e)) missing.Add(e);
            foreach (var g in gpuHits) if (!expectedHits.Contains(g)) extra.Add(g);
            throw new Exception(
                $"Autolykos2 mining hit-set mismatch on {BackendName}: expected {expectedHits.Count} hits, GPU reported {gpuHits.Count}. " +
                $"Missing: [{string.Join(",", missing)}] Extra: [{string.Join(",", extra)}]");
        }

        Console.WriteLine($"[Autolykos2] Mining GPU/CPU hit-set match on {BackendName}: {expectedHits.Count} hit(s) in {nonceRange} nonces ✓");
    });
}
