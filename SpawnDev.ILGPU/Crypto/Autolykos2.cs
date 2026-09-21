// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: Autolykos2.cs
//
// Ergo's Autolykos2 proof-of-work: dataset (the "N-table") generation, built on Blake2b.
// See D:\users\tj\Projects\_Brainstorm_2026_09_21\crypto-mining-funding-analysis.md for why
// this exists, and the plan file for scope (this is a throughput/feasibility PoC, not a
// production miner - no pool/wallet/submission logic).
// ---------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using ILGPU;

namespace SpawnDev.ILGPU.Crypto
{
    /// <summary>One 32-byte dataset element: 4 little-endian-conceptual 64-bit words.</summary>
    public readonly struct Element256
    {
        public readonly ulong W0, W1, W2, W3;
        public Element256(ulong w0, ulong w1, ulong w2, ulong w3) { W0 = w0; W1 = w1; W2 = w2; W3 = w3; }
    }

    /// <summary>
    /// Autolykos2 (Ergo) proof-of-work primitives, built on <see cref="Blake2b"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Constants (N schedule, K_LEN, message size) and the dataset-generation algorithm below
    /// are transcribed from the actual working reference miner
    /// <c>github.com/mhssamadani/Autolykos2_AMD_Miner</c> (<c>OCLdefs.h</c>, <c>PreHashKernel.cl</c>),
    /// not from the ErgoDocs prose page (which has the formulas as unrenderable diagrams) and not
    /// from memory - per the "no guessing" rule. <b>Open item, named rather than hidden:</b> this
    /// has been verified for INTERNAL consistency (the GPU kernel and the CPU-callable path below
    /// call the exact same code and are checked bit-exact by the accompanying test), which is what
    /// the PoC's actual goal (measuring SpawnDev.ILGPU's throughput on a real memory-hard random-access
    /// workload) needs. It has NOT yet been independently cross-checked against a real mined Ergo
    /// block's header/nonce/target, so bit-exact compatibility with the live network is not yet
    /// proven - only that this implementation is self-consistent and faithfully mirrors the
    /// reference miner's byte-order choices as best transcribed.
    /// </para>
    /// </remarks>
    public static class Autolykos2
    {
        /// <summary>k: number of dataset elements summed per nonce attempt.</summary>
        public const int K = 32;

        /// <summary>N at genesis (2^26), before the first N-boost step.</summary>
        public const long InitialN = 0x4000000L;

        /// <summary>N's ceiling once the N-boost schedule completes.</summary>
        public const long MaxN = 0x7FC9FF98L;

        /// <summary>Fixed constant message length hashed into every dataset element (8 KB), per ErgoDocs' "M".</summary>
        public const int ConstMessageSizeBytes = 8192;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Bswap32(uint x) =>
            (x << 24) | ((x & 0x0000FF00u) << 8) | ((x >> 8) & 0x0000FF00u) | (x >> 24);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Bswap64(ulong x) =>
            ((x & 0x00000000000000FFUL) << 56) | ((x & 0x000000000000FF00UL) << 40) |
            ((x & 0x0000000000FF0000UL) << 24) | ((x & 0x00000000FF000000UL) << 8) |
            ((x & 0x000000FF00000000UL) >> 8) | ((x & 0x0000FF0000000000UL) >> 24) |
            ((x & 0x00FF000000000000UL) >> 40) | ((x & 0xFF00000000000000UL) >> 56);

        /// <summary>
        /// Generates one dataset element: BLAKE2b-256(index_be32 || height_raw32 || M), where M is
        /// a fixed 8192-byte constant (sequential big-endian u64 counters 0..1023) - identical for
        /// every element and every height, per the reference's <c>InitPrehash</c> kernel. This is a
        /// 65-block chained compression (8 + 8192 = 8200 bytes), not a single Hash256 call, which is
        /// why it calls <see cref="Blake2b.Compress"/> directly rather than the single-block wrapper.
        /// Register-only: the loop below recomputes each block's message words from the loop counter
        /// every iteration - no local array is ever indexed by a runtime variable (see Blake2b.cs's
        /// remarks on why that matters on this fork).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void GenerateDatasetElement(uint index, uint height, out ulong e0, out ulong e1, out ulong e2, out ulong e3)
        {
            ulong h0 = Blake2b.IV0 ^ Blake2b.Param0;
            ulong h1 = Blake2b.IV1, h2 = Blake2b.IV2, h3 = Blake2b.IV3;
            ulong h4 = Blake2b.IV4, h5 = Blake2b.IV5, h6 = Blake2b.IV6, h7 = Blake2b.IV7;

            // Block 0: index (big-endian 32-bit) || height (raw 32-bit) || M[0..14] (14 counters).
            ulong m0 = ((ulong)height << 32) | Bswap32(index);
            ulong t = 128;
            Blake2b.Compress(
                ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7,
                m0, Bswap64(0), Bswap64(1), Bswap64(2), Bswap64(3), Bswap64(4), Bswap64(5), Bswap64(6),
                Bswap64(7), Bswap64(8), Bswap64(9), Bswap64(10), Bswap64(11), Bswap64(12), Bswap64(13), Bswap64(14),
                t, 0UL, isLastBlock: false);

            // Blocks 1..63: 16 more M counters each (M[15..1022], 1008 values across 63 blocks).
            ulong ctr = 15;
            for (int blk = 1; blk <= 63; blk++)
            {
                t += 128;
                ulong m0b = Bswap64(ctr), m1b = Bswap64(ctr + 1), m2b = Bswap64(ctr + 2), m3b = Bswap64(ctr + 3);
                ulong m4b = Bswap64(ctr + 4), m5b = Bswap64(ctr + 5), m6b = Bswap64(ctr + 6), m7b = Bswap64(ctr + 7);
                ulong m8b = Bswap64(ctr + 8), m9b = Bswap64(ctr + 9), m10b = Bswap64(ctr + 10), m11b = Bswap64(ctr + 11);
                ulong m12b = Bswap64(ctr + 12), m13b = Bswap64(ctr + 13), m14b = Bswap64(ctr + 14), m15b = Bswap64(ctr + 15);
                Blake2b.Compress(
                    ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7,
                    m0b, m1b, m2b, m3b, m4b, m5b, m6b, m7b, m8b, m9b, m10b, m11b, m12b, m13b, m14b, m15b,
                    t, 0UL, isLastBlock: false);
                ctr += 16;
            }

            // Final block: the last M counter (M[1023]) plus zero padding. Total message length
            // 8 (index+height) + 8192 (M) = 8200 bytes.
            t += 8;
            Blake2b.Compress(
                ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7,
                Bswap64(ctr), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                t, 0UL, isLastBlock: true);

            // "takeRight(31, digest)" per ErgoDocs: the reference kernel implements this by zeroing
            // one byte of the 32-byte output rather than shifting the buffer. Best-effort transcription
            // (see the class remarks on the open live-network cross-check) - clears the low byte of h0.
            e0 = h0 & 0xFFFFFFFFFFFFFF00UL;
            e1 = h1; e2 = h2; e3 = h3;
        }

        /// <summary>
        /// GPU kernel: one thread per dataset element index. Positional store only (thread i writes
        /// only element i) - no scatter, no shared memory, no atomics. <see cref="AcceleratorRequirements"/>:
        /// RequiresInt64 only.
        /// </summary>
        /// <remarks>
        /// Index type is <see cref="Index1D"/> (32-bit), not <see cref="LongIndex1D"/>: <see cref="MaxN"/>
        /// (0x7FC9FF98, ~2.14 billion) fits comfortably under <see cref="int.MaxValue"/>, and confirmed
        /// empirically that ILGPU's implicit-grouping kernel loader (<c>LoadAutoGroupedStreamKernel</c>)
        /// rejects <see cref="LongIndex1D"/> outright ("long indices are not supported") - explicit grouping
        /// would be needed to use it, which N's real ceiling doesn't justify.
        /// </remarks>
        public static void GenerateDatasetKernel(Index1D index, ArrayView<Element256> dataset, uint height)
        {
            GenerateDatasetElement((uint)index, height, out ulong e0, out ulong e1, out ulong e2, out ulong e3);
            dataset[index] = new Element256(e0, e1, e2, e3);
        }
    }
}
