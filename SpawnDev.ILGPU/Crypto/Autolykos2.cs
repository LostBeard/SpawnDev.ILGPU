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
using ILGPU.Runtime;

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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AddWithCarry(ulong a, ulong b, ulong carryIn, out ulong carryOut)
        {
            ulong sum = a + b;
            ulong c1 = sum < a ? 1UL : 0UL;
            ulong result = sum + carryIn;
            ulong c2 = result < sum ? 1UL : 0UL;
            carryOut = c1 | c2;
            return result;
        }

        /// <summary>
        /// Extracts one big-endian byte (0..31) from a conceptual 32-byte buffer made of 4
        /// big-endian-ordered 64-bit words (word0's byte0 is its most significant byte). Word
        /// selection is a ternary chain, not array indexing, so it stays register-only.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte ByteOfBE(ulong w0, ulong w1, ulong w2, ulong w3, int byteIndex)
        {
            int wordSel = byteIndex >> 3;
            ulong word = wordSel == 0 ? w0 : wordSel == 1 ? w1 : wordSel == 2 ? w2 : w3;
            int shift = 56 - ((byteIndex & 7) << 3);
            return (byte)((word >> shift) & 0xFFUL);
        }

        /// <summary>
        /// Derives one of the K sliding-window indices (0..K-1) from the 32-byte second-stage
        /// hash, mod <paramref name="datasetLength"/>. A 4-byte big-endian window starting at
        /// byte offset <paramref name="k"/>, wrapping mod 32 (equivalent to the reference's
        /// duplicate-first-4-bytes trick, without needing an extended buffer).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint DeriveIndex(ulong sh0, ulong sh1, ulong sh2, ulong sh3, int k, uint datasetLength)
        {
            uint idxWord = 0;
            for (int b = 0; b < 4; b++)
            {
                int byteIndex = (k + b) & 31;
                idxWord = (idxWord << 8) | ByteOfBE(sh0, sh1, sh2, sh3, byteIndex);
            }
            return idxWord % datasetLength;
        }

        /// <summary>
        /// Core per-nonce computation, shared conceptually (not literally, see remarks below on
        /// why) between <see cref="MineKernel"/> (GPU, <c>ArrayView</c>-backed dataset) and
        /// <see cref="MineNonceReference"/> (CPU test reference, plain-array-backed dataset).
        /// </summary>
        /// <remarks>
        /// Deliberate simplification vs. the reference miner, documented rather than silently
        /// diverging: the reference reads only 31 of a dataset element's 32 bytes at this stage
        /// (dropping one specific byte per its own internal storage-order convention). Since this
        /// implementation's dataset elements already carry a structurally-forced zero byte from
        /// generation (<see cref="GenerateDatasetElement"/>'s low byte of e0), using the full 32
        /// bytes here achieves the same "one fixed byte" design property without needing a second,
        /// differently-positioned byte-drop whose exact reference byte-order this implementation
        /// hasn't independently verified. This changes the second-stage message length (72 bytes
        /// here vs. 71 in the reference) but not the computational shape (same hash count, same
        /// random-access memory pattern) - see the class remarks on internal-consistency-over-live-
        /// network-fidelity for why that tradeoff is acceptable for this PoC.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ComputeFinalHash(
            ulong acc0, ulong acc1, ulong acc2, ulong acc3,
            out ulong d0, out ulong d1, out ulong d2, out ulong d3)
        {
            Blake2b.Hash256(acc0, acc1, acc2, acc3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 32,
                out d0, out d1, out d2, out d3);
        }

        /// <summary>
        /// GPU kernel: one thread per nonce candidate in this launch batch. On a hit (final digest
        /// below target), atomically allocates a slot and records the winning nonce.
        /// <see cref="AcceleratorRequirements"/>: RequiresInt64, RequiresAtomics, RequiresScatterStores.
        /// Deliberately no shared memory: the whole point of the K=32 gather is that indices are
        /// pseudorandom across the full dataset (the ASIC-resistance property), so there is no
        /// reusable tile to stage - this kernel is bound by raw random-access memory throughput.
        /// </summary>
        public static void MineKernel(
            Index1D nonceOffset,
            ArrayView<Element256> dataset, uint datasetLength,
            ulong nonceBase,
            ulong h0m, ulong h1m, ulong h2m, ulong h3m,
            ulong target0, ulong target1, ulong target2, ulong target3,
            ArrayView<ulong> winningNonces, ArrayView<int> winningCount)
        {
            ulong nonce = nonceBase + (ulong)nonceOffset;
            ulong nonceBE = Bswap64(nonce);

            // Stage 1: seed hash of (header || nonce), reduced mod N to pick one dataset element.
            Blake2b.Hash256(h0m, h1m, h2m, h3m, nonceBE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 40,
                out ulong sa0, out ulong sa1, out ulong sa2, out ulong sa3);
            uint seedIndex = (uint)(Bswap64(sa3) % (ulong)datasetLength);
            Element256 seed = dataset[(long)seedIndex];

            // Stage 2: second hash of (seed element || header || nonce) - see ComputeFinalHash's
            // remarks for why this is 32+32+8=72 bytes here rather than the reference's 71.
            Blake2b.Hash256(seed.W0, seed.W1, seed.W2, seed.W3, h0m, h1m, h2m, h3m, nonceBE,
                0, 0, 0, 0, 0, 0, 0, 72,
                out ulong sh0, out ulong sh1, out ulong sh2, out ulong sh3);

            // Stage 3: derive K=32 indices from the second hash, gather + sum (mod 2^256) the
            // corresponding dataset elements. Combined into one loop - no local array of indices
            // is ever materialized, avoiding the local-array dynamic-index PTX bug class.
            ulong acc0 = 0, acc1 = 0, acc2 = 0, acc3 = 0;
            for (int k = 0; k < K; k++)
            {
                uint idx = DeriveIndex(sh0, sh1, sh2, sh3, k, datasetLength);
                Element256 e = dataset[(long)idx];
                ulong c;
                acc0 = AddWithCarry(acc0, e.W0, 0, out c);
                acc1 = AddWithCarry(acc1, e.W1, c, out c);
                acc2 = AddWithCarry(acc2, e.W2, c, out c);
                acc3 = AddWithCarry(acc3, e.W3, c, out c);
            }

            // Stage 4: final hash of the sum, compare to target (both treated as big-endian
            // 256-bit numbers, word0 most significant).
            ComputeFinalHash(acc0, acc1, acc2, acc3,
                out ulong d0, out ulong d1, out ulong d2, out ulong d3);

            bool hit = d0 < target0 || (d0 == target0 && (d1 < target1 || (d1 == target1 &&
                (d2 < target2 || (d2 == target2 && d3 < target3)))));
            if (hit)
            {
                int slot = Atomic.Add(ref winningCount[0], 1);
                if (slot < winningNonces.IntLength) winningNonces[slot] = nonce;
            }
        }

        /// <summary>
        /// CPU-only mirror of <see cref="MineKernel"/>'s per-nonce hash chain (stages 1-4, no
        /// target compare), over a plain array instead of an <see cref="ArrayView{T}"/>, for
        /// direct use in test code (not a compiled kernel dispatch). Kept as a real second copy
        /// of the orchestration rather than forced into one generic method shared with the kernel,
        /// since <c>ArrayView</c> and a managed array aren't unifiable here without complexity
        /// this PoC doesn't need - see the plan file's note on keeping the reference physically
        /// separate. The actual hash/math (<see cref="Blake2b"/>, <see cref="AddWithCarry"/>,
        /// <see cref="DeriveIndex"/>, <see cref="ComputeFinalHash"/>) is shared, not duplicated.
        /// </summary>
        public static void ComputeNonceDigest(
            Element256[] dataset, uint datasetLength, ulong nonce,
            ulong h0m, ulong h1m, ulong h2m, ulong h3m,
            out ulong d0, out ulong d1, out ulong d2, out ulong d3)
        {
            ulong nonceBE = Bswap64(nonce);

            Blake2b.Hash256(h0m, h1m, h2m, h3m, nonceBE, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 40,
                out ulong sa0, out ulong sa1, out ulong sa2, out ulong sa3);
            uint seedIndex = (uint)(Bswap64(sa3) % (ulong)datasetLength);
            Element256 seed = dataset[(long)seedIndex];

            Blake2b.Hash256(seed.W0, seed.W1, seed.W2, seed.W3, h0m, h1m, h2m, h3m, nonceBE,
                0, 0, 0, 0, 0, 0, 0, 72,
                out ulong sh0, out ulong sh1, out ulong sh2, out ulong sh3);

            ulong acc0 = 0, acc1 = 0, acc2 = 0, acc3 = 0;
            for (int k = 0; k < K; k++)
            {
                uint idx = DeriveIndex(sh0, sh1, sh2, sh3, k, datasetLength);
                Element256 e = dataset[(long)idx];
                ulong c;
                acc0 = AddWithCarry(acc0, e.W0, 0, out c);
                acc1 = AddWithCarry(acc1, e.W1, c, out c);
                acc2 = AddWithCarry(acc2, e.W2, c, out c);
                acc3 = AddWithCarry(acc3, e.W3, c, out c);
            }

            ComputeFinalHash(acc0, acc1, acc2, acc3, out d0, out d1, out d2, out d3);
        }

        /// <summary>Returns true if <paramref name="nonce"/>'s digest is below the given target.</summary>
        public static bool MineNonceReference(
            Element256[] dataset, uint datasetLength, ulong nonce,
            ulong h0m, ulong h1m, ulong h2m, ulong h3m,
            ulong target0, ulong target1, ulong target2, ulong target3)
        {
            ComputeNonceDigest(dataset, datasetLength, nonce, h0m, h1m, h2m, h3m,
                out ulong d0, out ulong d1, out ulong d2, out ulong d3);

            return d0 < target0 || (d0 == target0 && (d1 < target1 || (d1 == target1 &&
                (d2 < target2 || (d2 == target2 && d3 < target3)))));
        }
    }
}
