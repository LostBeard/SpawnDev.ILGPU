// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: Blake2b.cs
//
// BLAKE2b-256 (RFC 7693), single-block only (message length <= 128 bytes), kernel-callable
// on every backend. Register-only: no ArrayView, no local arrays, no heap allocation.
// ---------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Crypto
{
    /// <summary>
    /// BLAKE2b-256 (RFC 7693), scoped to single-block messages (&lt;= 128 bytes: a nonce,
    /// an index preimage, or a 32-byte sum - every call shape Autolykos2 needs). No streaming
    /// API, because nothing that consumes this primitive hashes more than one block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The 12-round compression function is fully unrolled with literal integer SIGMA indices
    /// (12 hand-written round bodies, not a <c>sigma[round][i]</c> table walked by a runtime
    /// loop variable). This is deliberate, not a style choice: this fork has a documented
    /// latent PTX codegen bug where a fixed-size local array read/written via a RUNTIME loop
    /// index crashes CUDA JIT (see <c>SpawnDev.ILGPU.DemoConsole/LocalArrayDynamicIndexRepro.cs</c>).
    /// A table-driven message schedule is exactly that shape, so it is avoided by construction
    /// here rather than risked.
    /// </para>
    /// <para>
    /// Rotations are hand-written as <c>(x &gt;&gt; n) | (x &lt;&lt; (64 - n))</c> - there is no
    /// <c>ulong</c> rotate intrinsic in <c>ILGPU/IntrinsicMath.BitOperations.cs</c>. This
    /// composes to plain 64-bit shifts, which the WebGPU/WebGL i64 emulation path already
    /// supports across the full 0-63 shift range (see <c>WGSLEmulationLibrary.cs</c>) -
    /// verified end to end by the GPU-vs-CPU correctness test, not assumed.
    /// </para>
    /// </remarks>
    public static class Blake2b
    {
        // RFC 7693 initialization vector.
        private const ulong IV0 = 0x6a09e667f3bcc908UL;
        private const ulong IV1 = 0xbb67ae8584caa73bUL;
        private const ulong IV2 = 0x3c6ef372fe94f82bUL;
        private const ulong IV3 = 0xa54ff53a5f1d36f1UL;
        private const ulong IV4 = 0x510e527fade682d1UL;
        private const ulong IV5 = 0x9b05688c2b3e6c1fUL;
        private const ulong IV6 = 0x1f83d9abfb41bd6bUL;
        private const ulong IV7 = 0x5be0cd19137e2179UL;

        // Parameter block for the no-key, 32-byte-digest case: h0 ^= 0x01010000 | (kk << 8) | nn,
        // with kk = 0 (no key) and nn = 32 (BLAKE2b-256 digest length in bytes).
        private const ulong Param0 = 0x0000000001010020UL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Rotr(ulong x, int n) => (x >> n) | (x << (64 - n));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void G(ref ulong a, ref ulong b, ref ulong c, ref ulong d, ulong x, ulong y)
        {
            a = a + b + x;
            d = Rotr(d ^ a, 32);
            c = c + d;
            b = Rotr(b ^ c, 24);
            a = a + b + y;
            d = Rotr(d ^ a, 16);
            c = c + d;
            b = Rotr(b ^ c, 63);
        }

        /// <summary>
        /// BLAKE2b compression function F, applied to a single 128-byte message block.
        /// Updates <paramref name="h0"/>..<paramref name="h7"/> in place (the chaining value).
        /// <paramref name="t0"/>/<paramref name="t1"/> are the low/high 64 bits of the total
        /// bytes hashed so far (including this block); for the single-block callers this
        /// primitive targets, <paramref name="t1"/> is always 0.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Compress(
            ref ulong h0, ref ulong h1, ref ulong h2, ref ulong h3,
            ref ulong h4, ref ulong h5, ref ulong h6, ref ulong h7,
            ulong m0, ulong m1, ulong m2, ulong m3, ulong m4, ulong m5, ulong m6, ulong m7,
            ulong m8, ulong m9, ulong m10, ulong m11, ulong m12, ulong m13, ulong m14, ulong m15,
            ulong t0, ulong t1, bool isLastBlock)
        {
            ulong v0 = h0, v1 = h1, v2 = h2, v3 = h3;
            ulong v4 = h4, v5 = h5, v6 = h6, v7 = h7;
            ulong v8 = IV0, v9 = IV1, v10 = IV2, v11 = IV3;
            ulong v12 = IV4 ^ t0, v13 = IV5 ^ t1, v14 = IV6, v15 = IV7;
            if (isLastBlock) v14 = ~v14;

            // Round 0 - SIGMA[0] = {0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15}
            G(ref v0, ref v4, ref v8, ref v12, m0, m1);
            G(ref v1, ref v5, ref v9, ref v13, m2, m3);
            G(ref v2, ref v6, ref v10, ref v14, m4, m5);
            G(ref v3, ref v7, ref v11, ref v15, m6, m7);
            G(ref v0, ref v5, ref v10, ref v15, m8, m9);
            G(ref v1, ref v6, ref v11, ref v12, m10, m11);
            G(ref v2, ref v7, ref v8, ref v13, m12, m13);
            G(ref v3, ref v4, ref v9, ref v14, m14, m15);

            // Round 1 - SIGMA[1] = {14,10,4,8,9,15,13,6,1,12,0,2,11,7,5,3}
            G(ref v0, ref v4, ref v8, ref v12, m14, m10);
            G(ref v1, ref v5, ref v9, ref v13, m4, m8);
            G(ref v2, ref v6, ref v10, ref v14, m9, m15);
            G(ref v3, ref v7, ref v11, ref v15, m13, m6);
            G(ref v0, ref v5, ref v10, ref v15, m1, m12);
            G(ref v1, ref v6, ref v11, ref v12, m0, m2);
            G(ref v2, ref v7, ref v8, ref v13, m11, m7);
            G(ref v3, ref v4, ref v9, ref v14, m5, m3);

            // Round 2 - SIGMA[2] = {11,8,12,0,5,2,15,13,10,14,3,6,7,1,9,4}
            G(ref v0, ref v4, ref v8, ref v12, m11, m8);
            G(ref v1, ref v5, ref v9, ref v13, m12, m0);
            G(ref v2, ref v6, ref v10, ref v14, m5, m2);
            G(ref v3, ref v7, ref v11, ref v15, m15, m13);
            G(ref v0, ref v5, ref v10, ref v15, m10, m14);
            G(ref v1, ref v6, ref v11, ref v12, m3, m6);
            G(ref v2, ref v7, ref v8, ref v13, m7, m1);
            G(ref v3, ref v4, ref v9, ref v14, m9, m4);

            // Round 3 - SIGMA[3] = {7,9,3,1,13,12,11,14,2,6,5,10,4,0,15,8}
            G(ref v0, ref v4, ref v8, ref v12, m7, m9);
            G(ref v1, ref v5, ref v9, ref v13, m3, m1);
            G(ref v2, ref v6, ref v10, ref v14, m13, m12);
            G(ref v3, ref v7, ref v11, ref v15, m11, m14);
            G(ref v0, ref v5, ref v10, ref v15, m2, m6);
            G(ref v1, ref v6, ref v11, ref v12, m5, m10);
            G(ref v2, ref v7, ref v8, ref v13, m4, m0);
            G(ref v3, ref v4, ref v9, ref v14, m15, m8);

            // Round 4 - SIGMA[4] = {9,0,5,7,2,4,10,15,14,1,11,12,6,8,3,13}
            G(ref v0, ref v4, ref v8, ref v12, m9, m0);
            G(ref v1, ref v5, ref v9, ref v13, m5, m7);
            G(ref v2, ref v6, ref v10, ref v14, m2, m4);
            G(ref v3, ref v7, ref v11, ref v15, m10, m15);
            G(ref v0, ref v5, ref v10, ref v15, m14, m1);
            G(ref v1, ref v6, ref v11, ref v12, m11, m12);
            G(ref v2, ref v7, ref v8, ref v13, m6, m8);
            G(ref v3, ref v4, ref v9, ref v14, m3, m13);

            // Round 5 - SIGMA[5] = {2,12,6,10,0,11,8,3,4,13,7,5,15,14,1,9}
            G(ref v0, ref v4, ref v8, ref v12, m2, m12);
            G(ref v1, ref v5, ref v9, ref v13, m6, m10);
            G(ref v2, ref v6, ref v10, ref v14, m0, m11);
            G(ref v3, ref v7, ref v11, ref v15, m8, m3);
            G(ref v0, ref v5, ref v10, ref v15, m4, m13);
            G(ref v1, ref v6, ref v11, ref v12, m7, m5);
            G(ref v2, ref v7, ref v8, ref v13, m15, m14);
            G(ref v3, ref v4, ref v9, ref v14, m1, m9);

            // Round 6 - SIGMA[6] = {12,5,1,15,14,13,4,10,0,7,6,3,9,2,8,11}
            G(ref v0, ref v4, ref v8, ref v12, m12, m5);
            G(ref v1, ref v5, ref v9, ref v13, m1, m15);
            G(ref v2, ref v6, ref v10, ref v14, m14, m13);
            G(ref v3, ref v7, ref v11, ref v15, m4, m10);
            G(ref v0, ref v5, ref v10, ref v15, m0, m7);
            G(ref v1, ref v6, ref v11, ref v12, m6, m3);
            G(ref v2, ref v7, ref v8, ref v13, m9, m2);
            G(ref v3, ref v4, ref v9, ref v14, m8, m11);

            // Round 7 - SIGMA[7] = {13,11,7,14,12,1,3,9,5,0,15,4,8,6,2,10}
            G(ref v0, ref v4, ref v8, ref v12, m13, m11);
            G(ref v1, ref v5, ref v9, ref v13, m7, m14);
            G(ref v2, ref v6, ref v10, ref v14, m12, m1);
            G(ref v3, ref v7, ref v11, ref v15, m3, m9);
            G(ref v0, ref v5, ref v10, ref v15, m5, m0);
            G(ref v1, ref v6, ref v11, ref v12, m15, m4);
            G(ref v2, ref v7, ref v8, ref v13, m8, m6);
            G(ref v3, ref v4, ref v9, ref v14, m2, m10);

            // Round 8 - SIGMA[8] = {6,15,14,9,11,3,0,8,12,2,13,7,1,4,10,5}
            G(ref v0, ref v4, ref v8, ref v12, m6, m15);
            G(ref v1, ref v5, ref v9, ref v13, m14, m9);
            G(ref v2, ref v6, ref v10, ref v14, m11, m3);
            G(ref v3, ref v7, ref v11, ref v15, m0, m8);
            G(ref v0, ref v5, ref v10, ref v15, m12, m2);
            G(ref v1, ref v6, ref v11, ref v12, m13, m7);
            G(ref v2, ref v7, ref v8, ref v13, m1, m4);
            G(ref v3, ref v4, ref v9, ref v14, m10, m5);

            // Round 9 - SIGMA[9] = {10,2,8,4,7,6,1,5,15,11,9,14,3,12,13,0}
            G(ref v0, ref v4, ref v8, ref v12, m10, m2);
            G(ref v1, ref v5, ref v9, ref v13, m8, m4);
            G(ref v2, ref v6, ref v10, ref v14, m7, m6);
            G(ref v3, ref v7, ref v11, ref v15, m1, m5);
            G(ref v0, ref v5, ref v10, ref v15, m15, m11);
            G(ref v1, ref v6, ref v11, ref v12, m9, m14);
            G(ref v2, ref v7, ref v8, ref v13, m3, m12);
            G(ref v3, ref v4, ref v9, ref v14, m13, m0);

            // Round 10 = Round 0 (SIGMA has only 10 distinct rows; 10/11 repeat 0/1)
            G(ref v0, ref v4, ref v8, ref v12, m0, m1);
            G(ref v1, ref v5, ref v9, ref v13, m2, m3);
            G(ref v2, ref v6, ref v10, ref v14, m4, m5);
            G(ref v3, ref v7, ref v11, ref v15, m6, m7);
            G(ref v0, ref v5, ref v10, ref v15, m8, m9);
            G(ref v1, ref v6, ref v11, ref v12, m10, m11);
            G(ref v2, ref v7, ref v8, ref v13, m12, m13);
            G(ref v3, ref v4, ref v9, ref v14, m14, m15);

            // Round 11 = Round 1
            G(ref v0, ref v4, ref v8, ref v12, m14, m10);
            G(ref v1, ref v5, ref v9, ref v13, m4, m8);
            G(ref v2, ref v6, ref v10, ref v14, m9, m15);
            G(ref v3, ref v7, ref v11, ref v15, m13, m6);
            G(ref v0, ref v5, ref v10, ref v15, m1, m12);
            G(ref v1, ref v6, ref v11, ref v12, m0, m2);
            G(ref v2, ref v7, ref v8, ref v13, m11, m7);
            G(ref v3, ref v4, ref v9, ref v14, m5, m3);

            h0 ^= v0 ^ v8;
            h1 ^= v1 ^ v9;
            h2 ^= v2 ^ v10;
            h3 ^= v3 ^ v11;
            h4 ^= v4 ^ v12;
            h5 ^= v5 ^ v13;
            h6 ^= v6 ^ v14;
            h7 ^= v7 ^ v15;
        }

        /// <summary>
        /// BLAKE2b-256 of a single message block. <paramref name="m0"/>..<paramref name="m15"/>
        /// are the 128-byte block as 16 little-endian 64-bit words, with the message's actual
        /// bytes placed at the start and every byte past <paramref name="messageByteLength"/>
        /// zeroed by the caller (RFC 7693 zero-padding) - the byte length itself only feeds the
        /// counter, it does not change how the words are read here. Precondition:
        /// <c>0 &lt;= messageByteLength &lt;= 128</c> (single block only).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Hash256(
            ulong m0, ulong m1, ulong m2, ulong m3, ulong m4, ulong m5, ulong m6, ulong m7,
            ulong m8, ulong m9, ulong m10, ulong m11, ulong m12, ulong m13, ulong m14, ulong m15,
            int messageByteLength,
            out ulong out0, out ulong out1, out ulong out2, out ulong out3)
        {
            ulong h0 = IV0 ^ Param0;
            ulong h1 = IV1, h2 = IV2, h3 = IV3, h4 = IV4, h5 = IV5, h6 = IV6, h7 = IV7;

            Compress(
                ref h0, ref h1, ref h2, ref h3, ref h4, ref h5, ref h6, ref h7,
                m0, m1, m2, m3, m4, m5, m6, m7, m8, m9, m10, m11, m12, m13, m14, m15,
                (ulong)messageByteLength, 0UL, isLastBlock: true);

            out0 = h0; out1 = h1; out2 = h2; out3 = h3;
        }
    }
}
