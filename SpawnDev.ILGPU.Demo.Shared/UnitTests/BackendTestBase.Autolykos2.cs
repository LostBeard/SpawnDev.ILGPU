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

    // KNOWN OPEN ISSUE (found running this test 2026-09-21): WebGL fails this test with a
    // wrong (not a graceful skip/exception) digest even for the all-zero-message "empty"
    // vector - CPU/CUDA/OpenCL/WebGPU (both subgroup variants)/Wasm all pass. Traced the
    // GLSL i64 shift emulation (GLSLEmulationLibrary.cs i64_shl/u64_shr) by hand for every
    // rotate amount Blake2b uses (1, 16, 24, 32, 63) - all correct. Also ruled out "the
    // isLastBlock bool parameter isn't reaching the shader" (computed what the digest would
    // be with isLastBlock forced false - doesn't match the wrong output either). Root cause
    // is still open; this is a SpawnDev.ILGPU WebGL backend bug, not an Autolykos2/Blake2b
    // logic bug (the same primitive is correct on every other backend, including the other
    // i64-emulated one, WebGPU). Not chased further here: WebGL is out of scope for
    // production-scale mining regardless (see crypto-mining-funding-analysis.md and the plan
    // file - multi-GB resident buffers exceed practical WebGL limits independent of this),
    // and this needs its own investigation as a SpawnDev.ILGPU WebGL-backend issue.
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
}
