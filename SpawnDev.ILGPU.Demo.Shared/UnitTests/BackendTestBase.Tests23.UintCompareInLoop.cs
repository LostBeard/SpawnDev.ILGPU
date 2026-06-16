using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part 23: Bisection tests for the Tests22 cross-backend bug
    // (`StaticStructReturnChainedHelpers_*` fail on every backend except CPU
    // with Rng=0 instead of >0x800000 after the libopus Normalize loop pattern).
    //
    // Goal: isolate the failing operation. The full Normalize loop combines
    //   1. uint compare via `<=` against a uint constant
    //   2. uint left-shift inside the loop body
    //   3. struct field reads/writes (`state.Rng`)
    //   4. ArrayView<byte> reads via the buffer cursor
    //   5. integer arithmetic on shifted values
    //
    // If a SUBSET of these fails on the same backends as Tests22, the bug is in
    // that subset. If all sub-tests pass, the bug is in their interaction.
    public abstract partial class BackendTestBase
    {
        #region Bisection — uint compare + shift + loop

        // Test A: Bare uint compare. No loop, no shift. If this fails the bug is in Compare codegen.
        static void Tests23_UintCompareKernel(Index1D _, ArrayView<uint> output)
        {
            uint a = 0x80000000u;
            uint b = 0x00800000u;
            output[0] = (a <= b) ? 1u : 0u;          // unsigned: a > b → 0
            output[1] = (b <= a) ? 1u : 0u;          // unsigned: b < a → 1
            output[2] = (a <  b) ? 1u : 0u;          // unsigned: a > b → 0
            output[3] = (b <  a) ? 1u : 0u;          // unsigned: b < a → 1
        }

        [TestMethod]
        public async Task Tests23_BareUintCompare() => await RunEmulatedTest(async accelerator =>
        {
            using var dOut = accelerator.Allocate1D<uint>(4);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>>(Tests23_UintCompareKernel);
            k((Index1D)1, dOut.View);
            await accelerator.SynchronizeAsync();
            var r = await dOut.CopyToHostAsync<uint>();
            if (r[0] != 0u) throw new Exception($"BareUintCompare a<=b expected 0 got {r[0]} (high-bit uint compared against small uint as signed?)");
            if (r[1] != 1u) throw new Exception($"BareUintCompare b<=a expected 1 got {r[1]}");
            if (r[2] != 0u) throw new Exception($"BareUintCompare a<b expected 0 got {r[2]}");
            if (r[3] != 1u) throw new Exception($"BareUintCompare b<a expected 1 got {r[3]}");
        });

        // Test B: uint left-shift bare. No loop, no compare. Produces the same
        // "shift past sign bit" pattern that the Normalize loop exercises.
        // Output index map (used by both BareUintShift + the constant-write
        // diagnostic Tests23_HighBitConstWrite below):
        //   [0]=v0  [1]=v1  [2]=v2  [3]=v3  [4]=v4
        //   [5]=0x80000000u const-write (NO SHIFT)
        //   [6]=0xFFFFFFFFu const-write (NO SHIFT, all bits)
        //   [7]=0x7FFFFFFFu const-write (NO SHIFT, all bits except sign)
        static void Tests23_UintShiftKernel(Index1D _, ArrayView<uint> output)
        {
            uint v0 = 0x80u;
            uint v1 = v0 << 8;          // 0x8000
            uint v2 = v1 << 8;          // 0x800000
            uint v3 = v2 << 8;          // 0x80000000 — high bit set
            uint v4 = v3 << 8;          // 0 (high byte shifts off)
            output[0] = v0;
            output[1] = v1;
            output[2] = v2;
            output[3] = v3;
            output[4] = v4;
            // Diagnostic: write the same high-bit-set value DIRECTLY, no shift,
            // so the test isolates whether the failure is in the shift codegen
            // or in the TF readback / output-write path.
            output[5] = 0x80000000u;
            output[6] = 0xFFFFFFFFu;
            output[7] = 0x7FFFFFFFu;
        }

        [TestMethod]
        public async Task Tests23_BareUintShift() => await RunEmulatedTest(async accelerator =>
        {
            using var dOut = accelerator.Allocate1D<uint>(8);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>>(Tests23_UintShiftKernel);
            k((Index1D)1, dOut.View);
            await accelerator.SynchronizeAsync();
            var r = await dOut.CopyToHostAsync<uint>();
            if (r[0] != 0x80u) throw new Exception($"v0 expected 0x80 got 0x{r[0]:X8}");
            if (r[1] != 0x8000u) throw new Exception($"v1 expected 0x8000 got 0x{r[1]:X8}");
            if (r[2] != 0x800000u) throw new Exception($"v2 expected 0x800000 got 0x{r[2]:X8}");
            if (r[3] != 0x80000000u)
                throw new Exception($"v3 expected 0x80000000 got 0x{r[3]:X8}. Const-write diagnostic: " +
                    $"out[5]=0x{r[5]:X8} (expect 0x80000000), out[6]=0x{r[6]:X8} (expect 0xFFFFFFFF), out[7]=0x{r[7]:X8} (expect 0x7FFFFFFF). " +
                    "If [5] also wrong → TF readback / output-write path is wrong, not the shift. If [5] right → shift codegen is wrong.");
            if (r[4] != 0u) throw new Exception($"v4 expected 0 got 0x{r[4]:X8}");
            if (r[5] != 0x80000000u) throw new Exception($"const-write [5]: expected 0x80000000 got 0x{r[5]:X8}");
            if (r[6] != 0xFFFFFFFFu) throw new Exception($"const-write [6]: expected 0xFFFFFFFF got 0x{r[6]:X8}");
            if (r[7] != 0x7FFFFFFFu) throw new Exception($"const-write [7]: expected 0x7FFFFFFF got 0x{r[7]:X8}");
        });

        // Test C: the Normalize-shape loop with a LOCAL uint variable (no struct).
        // If C passes but Tests22 fails, the bug is specific to struct field
        // access or SetField persistence inside the loop, not the compare/shift itself.
        static void Tests23_LocalLoopKernel(Index1D _, ArrayView<uint> output)
        {
            uint rng = 0x80u;
            int iter = 0;
            // EC_CODE_BOT = 0x00800000
            while (rng <= 0x00800000u && iter < 8)
            {
                rng <<= 8;
                iter++;
            }
            output[0] = rng;             // expected: 0x80000000 after exactly 3 iterations (0x80→0x8000→0x800000→0x80000000)
            output[1] = (uint)iter;      // expected: 3
        }

        [TestMethod]
        public async Task Tests23_LocalLoop_NoStruct() => await RunEmulatedTest(async accelerator =>
        {
            using var dOut = accelerator.Allocate1D<uint>(2);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>>(Tests23_LocalLoopKernel);
            k((Index1D)1, dOut.View);
            await accelerator.SynchronizeAsync();
            var r = await dOut.CopyToHostAsync<uint>();
            if (r[0] != 0x80000000u)
                throw new Exception($"Local-loop rng expected 0x80000000 got 0x{r[0]:X8} (iter={r[1]}). " +
                    "If iter=8, the loop ran to its safety cap → compare is broken (still TRUE after rng=0x80000000). " +
                    "If iter<3 → compare exited early. If iter=3 but rng=0 → shift broke at 0x800000<<8.");
            if (r[1] != 3u)
                throw new Exception($"Local-loop iter expected 3 got {r[1]} (rng=0x{r[0]:X8})");
        });

        // Test D: struct-of-uint with the same loop. Bisects whether struct field
        // SetField semantics inside a loop are the bug.
        public struct Tests23_StateStruct
        {
            public uint Rng;
            public uint Other;
            public int Iter;
        }
        static void Tests23_StructLoopKernel(Index1D _, ArrayView<uint> output)
        {
            Tests23_StateStruct s = default;
            s.Rng = 0x80u;
            s.Iter = 0;
            while (s.Rng <= 0x00800000u && s.Iter < 8)
            {
                s.Rng <<= 8;
                s.Iter++;
                s.Other += s.Rng;
            }
            output[0] = s.Rng;             // expected 0x80000000
            output[1] = (uint)s.Iter;      // expected 3
            output[2] = s.Other;           // expected 0x8000 + 0x800000 + 0x80000000 = 0x80808000
        }

        [TestMethod]
        public async Task Tests23_StructLoop() => await RunEmulatedTest(async accelerator =>
        {
            using var dOut = accelerator.Allocate1D<uint>(3);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>>(Tests23_StructLoopKernel);
            k((Index1D)1, dOut.View);
            await accelerator.SynchronizeAsync();
            var r = await dOut.CopyToHostAsync<uint>();
            if (r[0] != 0x80000000u)
                throw new Exception($"Struct-loop Rng expected 0x80000000 got 0x{r[0]:X8} (Iter={r[1]} Other=0x{r[2]:X8})");
            if (r[1] != 3u)
                throw new Exception($"Struct-loop Iter expected 3 got {r[1]} (Rng=0x{r[0]:X8})");
            if (r[2] != 0x80808000u)
                throw new Exception($"Struct-loop Other expected 0x80808000 got 0x{r[2]:X8}");
        });

        // Test E: Tuvok-shape kernel WITHOUT the byte ArrayView read inside the loop.
        // Just iterates the shift+compare loop with const-expression constants, multiple
        // uint locals, the sym/val auxiliary computations.
        // Mimics StaticInitChainedHelpersInlineKernel's body shape but without packet reads.
        private const int  TESTS23E_SYM_BITS = 8;
        private const int  TESTS23E_CODE_BITS = 32;
        private const uint TESTS23E_CODE_TOP = 1u << (TESTS23E_CODE_BITS - 1);  // 0x80000000
        private const uint TESTS23E_CODE_BOT = TESTS23E_CODE_TOP >> TESTS23E_SYM_BITS;  // 0x00800000
        private const int  TESTS23E_CODE_EXTRA = (TESTS23E_CODE_BITS - 2) % TESTS23E_SYM_BITS + 1;  // 7
        private const uint TESTS23E_SYM_MAX = (1u << TESTS23E_SYM_BITS) - 1u;  // 0xFF

        static void Tests23_NormalizeShapeNoBufferKernel(Index1D _, ArrayView<uint> output)
        {
            uint rng = 1u << TESTS23E_CODE_EXTRA;  // 0x80
            int  rem  = 0x9C;
            uint val = rng - 1u - (uint)(rem >> (TESTS23E_SYM_BITS - TESTS23E_CODE_EXTRA));
            int  iter = 0;
            // Synthetic Rem stream — replaces ArrayView<byte> reads with deterministic values.
            // Same byte pattern as Tests22's synthetic packet so the math matches.
            uint synthSeed = 0u;
            while (rng <= TESTS23E_CODE_BOT && iter < 8)
            {
                rng <<= TESTS23E_SYM_BITS;
                int sym = rem;
                synthSeed = (synthSeed + 0x37u) & 0xFFu;
                rem = (int)(synthSeed ^ 0x9Cu);
                sym = (sym << TESTS23E_SYM_BITS | rem) >> (TESTS23E_SYM_BITS - TESTS23E_CODE_EXTRA);
                val = ((val << TESTS23E_SYM_BITS) + (TESTS23E_SYM_MAX & (uint)~sym)) & (TESTS23E_CODE_TOP - 1u);
                iter++;
            }
            output[0] = rng;
            output[1] = val;
            output[2] = (uint)iter;
            output[3] = (uint)rem;
        }

        [TestMethod]
        public async Task Tests23_NormalizeShape_NoBuffer() => await RunEmulatedTest(async accelerator =>
        {
            using var dOut = accelerator.Allocate1D<uint>(4);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>>(Tests23_NormalizeShapeNoBufferKernel);
            k((Index1D)1, dOut.View);
            await accelerator.SynchronizeAsync();
            var r = await dOut.CopyToHostAsync<uint>();
            // Expected unsigned-compare exit: 3 iterations (0x80→0x8000→0x800000→0x80000000), Rng=0x80000000.
            // If iter==8 (safety cap) and rng==0, the compare is signed — bug.
            // If iter<3, compare exited too early.
            if (r[0] != 0x80000000u)
                throw new Exception($"NormalizeShape_NoBuffer Rng expected 0x80000000 got 0x{r[0]:X8} (iter={r[2]} val=0x{r[1]:X8} rem=0x{r[3]:X2}). " +
                    "iter==8 + rng==0 → signed-compare bug fires here. iter==3 + rng==0 → shift broke.");
            if (r[2] != 3u)
                throw new Exception($"NormalizeShape_NoBuffer iter expected 3 got {r[2]} (rng=0x{r[0]:X8})");
        });

        // Test F: ADD the byte ArrayView read inside the loop (Tuvok's exact pattern).
        // Test E without the buffer pass; F WITH the buffer. If F fails on
        // CUDA/OpenCL/WebGPU/Wasm but E doesn't, the byte ArrayView read interaction
        // is the trigger.
        static void Tests23_NormalizeShapeWithBufferKernel(
            Index1D _,
            ArrayView<byte> packet, int packetStart, int packetStorage,
            ArrayView<uint> output)
        {
            uint rng = 1u << TESTS23E_CODE_EXTRA;
            uint offs = 0u;
            int  rem = 0;
            if (offs < (uint)packetStorage)
            {
                rem = packet[packetStart + (int)offs];
                offs++;
            }
            uint val = rng - 1u - (uint)(rem >> (TESTS23E_SYM_BITS - TESTS23E_CODE_EXTRA));
            int  iter = 0;
            while (rng <= TESTS23E_CODE_BOT && iter < 8)
            {
                rng <<= TESTS23E_SYM_BITS;
                int sym = rem;
                if (offs < (uint)packetStorage)
                {
                    rem = packet[packetStart + (int)offs];
                    offs++;
                }
                else { rem = 0; }
                sym = (sym << TESTS23E_SYM_BITS | rem) >> (TESTS23E_SYM_BITS - TESTS23E_CODE_EXTRA);
                val = ((val << TESTS23E_SYM_BITS) + (TESTS23E_SYM_MAX & (uint)~sym)) & (TESTS23E_CODE_TOP - 1u);
                iter++;
            }
            output[0] = rng;
            output[1] = val;
            output[2] = offs;
            output[3] = (uint)rem;
            output[4] = (uint)iter;
        }

        [TestMethod]
        public async Task Tests23_NormalizeShape_WithBuffer() => await RunEmulatedTest(async accelerator =>
        {
            var packet = new byte[16];
            for (int i = 0; i < packet.Length; i++)
                packet[i] = (byte)((i * 0x37) ^ 0x9C);
            using var dPacket = accelerator.Allocate1D(packet);
            using var dOut = accelerator.Allocate1D<uint>(5);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, int, int, ArrayView<uint>>(Tests23_NormalizeShapeWithBufferKernel);
            k((Index1D)1, dPacket.View, 0, packet.Length, dOut.View);
            await accelerator.SynchronizeAsync();
            var r = await dOut.CopyToHostAsync<uint>();
            // Expected behavior: 3 iterations, Rng=0x80000000, Offs=4 (1 init + 3 iter).
            if (r[0] != 0x80000000u)
                throw new Exception($"NormalizeShape_WithBuffer Rng expected 0x80000000 got 0x{r[0]:X8} " +
                    $"(val=0x{r[1]:X8} offs={r[2]} rem=0x{r[3]:X2} iter={r[4]}). " +
                    $"If iter==8 → loop ran to safety cap, rng update broken or compare wrong. " +
                    $"If iter<3 → exited early.");
            if (r[4] != 3u)
                throw new Exception($"NormalizeShape_WithBuffer iter expected 3 got {r[4]} (rng=0x{r[0]:X8} offs={r[2]})");
        });

        // Test H: ArrayView<long> kernel param backed by a Cast of an int buffer.
        // Surfaced 2026-05-04 by Tuvok's `Vp9FrameEntropyKernel` Wasm OOB — the trap
        // showed `V4=4 bytes wide` for an `ArrayView<long>` param where 8 bytes were
        // needed. Root cause: WasmAccelerator was using `wasmBuf.ElementSize` (4 for
        // the int-allocated parent buffer) instead of the view's element size (8 for
        // long) when computing the view's byte range; the wasm memory copy region
        // was undersized → kernel reads/writes past the end → OOB.
        //
        // This test allocates a buffer as `MemoryBuffer1D<int>`, casts to long view,
        // writes one long value to the view, and reads it back. Pre-fix: Wasm OOB
        // because the byte range reserved is `length*4` instead of `length*8`.
        // Post-fix: passes on every backend.
        static void Tests23_LongViewOverIntBufferKernel(
            Index1D _,
            ArrayView<long> outLen,
            long valueToStore)
        {
            outLen[0] = valueToStore;
        }

        [TestMethod]
        public async Task Tests23_LongViewOverIntBuffer() => await RunEmulatedTest(async accelerator =>
        {
            // Allocate a MemoryBuffer1D<int> with 2 ints (= 8 bytes).
            // Cast to ArrayView<long> — gives a 1-element long view backed by the
            // int allocation. WasmAccelerator must use the VIEW's element size
            // (8 bytes per long), not the buffer's (4 bytes per int), to size the
            // wasm memory copy region.
            using var intBuf = accelerator.Allocate1D<int>(2);
            var longView = intBuf.View.AsContiguous().Cast<long>();
            const long testVal = 0x1122334455667788L;
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, long>(
                Tests23_LongViewOverIntBufferKernel);
            k((Index1D)1, longView, testVal);
            await accelerator.SynchronizeAsync();

            // Read the parent int buffer back; reinterpret 2 ints as 1 long.
            var ints = await intBuf.CopyToHostAsync<int>();
            long got = ((long)(uint)ints[0]) | (((long)ints[1]) << 32);
            if (got != testVal)
                throw new Exception($"Cast<long> over int buffer: expected 0x{testVal:X16} got 0x{got:X16} (ints=[0x{ints[0]:X8}, 0x{ints[1]:X8}]). " +
                    "If only the LOW 32 bits are correct, WasmAccelerator's view-byte-length used buffer.ElementSize (4) instead of view's actual element size (8) — task #16 follow-up fix.");
        });

        // Test H: NATIVE long buffer (NOT Cast). Tuvok's Vp9 trap surfaced 2026-05-04
        // shows V4 (the kernel's `ArrayView<long> outLen` param) reports byteLen=4
        // even though the underlying buffer is `Allocate1D<long>(1)` directly. Different
        // path from Test E (Cast<long> over int buffer); this isolates the natively-typed
        // long allocation. If the wasm view-byte-length compute uses
        // `wasmBuf.ElementSize` AND that buffer reports 4 bytes per element instead of 8
        // for native long, OR the reflection-based VIEW-element-size override doesn't
        // fire for non-Cast top-level params, this test exposes it.
        static void Tests23_NativeLongBufferKernel(
            Index1D _,
            ArrayView<long> outLen,
            long valueToStore)
        {
            outLen[0] = valueToStore;
        }

        [TestMethod]
        public async Task Tests23_NativeLongBuffer() => await RunEmulatedTest(async accelerator =>
        {
            // Native MemoryBuffer1D<long>(1) - 8 bytes total, kernel param ArrayView<long>.
            // Pre-fix: only LOW 32 bits round-trip on Wasm if buffer.ElementSize=4 path
            // is taken. Post-fix: full 64-bit round-trip on every backend.
            using var longBuf = accelerator.Allocate1D<long>(1);
            const long testVal = 0x1122334455667788L;
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>, long>(
                Tests23_NativeLongBufferKernel);
            k((Index1D)1, longBuf.View, testVal);
            await accelerator.SynchronizeAsync();

            var got = await longBuf.CopyToHostAsync<long>();
            if (got[0] != testVal)
                throw new Exception($"Native ArrayView<long> over Allocate1D<long>(1): expected 0x{testVal:X16} got 0x{got[0]:X16}. " +
                    "If only LOW 32 bits are correct, the view-byte-length compute is using a 4-byte element size for the native long buffer.");
        });

        // Test I: SILK body-struct shape (6 short + 9 int ArrayView fields). Tuvok's
        // `SilkDecodeCoreInputs` body struct from SpawnDev.Codecs hits "Invalid
        // BindGroupLayout" on WebGPU even after rc.5 same-element-type coalesce
        // because the coalesce only handles i32/u32/f32 fields. The 6 sub-word
        // (short) fields stay as 6 separate bindings. Total: 1 coalesced int +
        // 6 short bindings + 1 _scalar_params = 8 bindings — exactly at the
        // WebGPU spec minimum for `maxStorageBuffersPerShaderStage`. Fails on
        // adapters with stricter limits, AND when the binding-count overhead
        // (per-field length uniforms, view metadata fields) pushes the actual
        // descriptor count slightly higher.
        public struct Tests23_SilkShapeStruct
        {
            public ArrayView<short> S0;
            public ArrayView<short> S1;
            public ArrayView<short> S2;
            public ArrayView<short> S3;
            public ArrayView<short> S4;
            public ArrayView<short> S5;
            public ArrayView<int> I0;
            public ArrayView<int> I1;
            public ArrayView<int> I2;
            public ArrayView<int> I3;
            public ArrayView<int> I4;
            public ArrayView<int> I5;
            public ArrayView<int> I6;
            public ArrayView<int> I7;
            public ArrayView<int> I8;
        }

        static void Tests23_SilkShapeKernel(
            Index1D _,
            Tests23_SilkShapeStruct s,
            ArrayView<int> output)
        {
            // Read one element from each view and sum into output[0]. This forces
            // every binding to be alive in the bind-group layout.
            int sum = 0;
            sum += (int)s.S0[0];
            sum += (int)s.S1[0];
            sum += (int)s.S2[0];
            sum += (int)s.S3[0];
            sum += (int)s.S4[0];
            sum += (int)s.S5[0];
            sum += s.I0[0];
            sum += s.I1[0];
            sum += s.I2[0];
            sum += s.I3[0];
            sum += s.I4[0];
            sum += s.I5[0];
            sum += s.I6[0];
            sum += s.I7[0];
            sum += s.I8[0];
            output[0] = sum;
        }

        [TestMethod]
        public async Task Tests23_SilkBodyStructShape() => await RunEmulatedTest(async accelerator =>
        {
            // Allocate 6 short buffers + 9 int buffers, set element[0] of each to a
            // distinct value, dispatch the kernel which sums element[0] of each, and
            // verify the sum.
            using var s0 = accelerator.Allocate1D(new short[] { 1 });
            using var s1 = accelerator.Allocate1D(new short[] { 2 });
            using var s2 = accelerator.Allocate1D(new short[] { 3 });
            using var s3 = accelerator.Allocate1D(new short[] { 4 });
            using var s4 = accelerator.Allocate1D(new short[] { 5 });
            using var s5 = accelerator.Allocate1D(new short[] { 6 });
            using var i0 = accelerator.Allocate1D(new int[] { 10 });
            using var i1 = accelerator.Allocate1D(new int[] { 20 });
            using var i2 = accelerator.Allocate1D(new int[] { 30 });
            using var i3 = accelerator.Allocate1D(new int[] { 40 });
            using var i4 = accelerator.Allocate1D(new int[] { 50 });
            using var i5 = accelerator.Allocate1D(new int[] { 60 });
            using var i6 = accelerator.Allocate1D(new int[] { 70 });
            using var i7 = accelerator.Allocate1D(new int[] { 80 });
            using var i8 = accelerator.Allocate1D(new int[] { 90 });
            using var output = accelerator.Allocate1D<int>(1);

            var inputs = new Tests23_SilkShapeStruct
            {
                S0 = s0.View, S1 = s1.View, S2 = s2.View, S3 = s3.View, S4 = s4.View, S5 = s5.View,
                I0 = i0.View, I1 = i1.View, I2 = i2.View, I3 = i3.View, I4 = i4.View, I5 = i5.View,
                I6 = i6.View, I7 = i7.View, I8 = i8.View,
            };

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_SilkShapeStruct, ArrayView<int>>(
                Tests23_SilkShapeKernel);
            k((Index1D)1, inputs, output.View);
            await accelerator.SynchronizeAsync();

            var got = await output.CopyToHostAsync<int>();
            int expected = 1 + 2 + 3 + 4 + 5 + 6 + 10 + 20 + 30 + 40 + 50 + 60 + 70 + 80 + 90;
            if (got[0] != expected)
                throw new Exception($"SILK shape body-struct: expected sum={expected} got {got[0]}. " +
                    "If WebGPU fires Invalid BindGroupLayout, sub-word body-struct fields aren't being coalesced.");
        });

        // Minimal sub-word + int body-struct shape — narrows the rc.5 bug surface for
        // task #33. If THIS fails on WebGPU, the issue is mixed sub-word + non-sub-word
        // body-struct codegen at minimum. If only Tests23_SilkBodyStructShape (6+9) fails
        // but THIS passes, the issue scales with field count.
        public struct Tests23_MinimalShortIntStruct
        {
            public ArrayView<short> S0;
            public ArrayView<int> I0;
        }

        static void Tests23_MinimalShortIntKernel(
            Index1D _,
            Tests23_MinimalShortIntStruct s,
            ArrayView<int> output)
        {
            int sum = 0;
            sum += (int)s.S0[0];
            sum += s.I0[0];
            output[0] = sum;
        }

        [TestMethod]
        public async Task Tests23_MinimalShortIntBodyStruct() => await RunEmulatedTest(async accelerator =>
        {
            using var s0 = accelerator.Allocate1D(new short[] { 7 });
            using var i0 = accelerator.Allocate1D(new int[] { 100 });
            using var output = accelerator.Allocate1D<int>(1);

            var inputs = new Tests23_MinimalShortIntStruct { S0 = s0.View, I0 = i0.View };
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_MinimalShortIntStruct, ArrayView<int>>(
                Tests23_MinimalShortIntKernel);
            k((Index1D)1, inputs, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            if (got[0] != 107)
                throw new Exception($"Mixed sub-word + int body struct: expected 107 got {got[0]}");
        });

        // Diagnostic: CUDA "illegal memory access" on sub-word body-struct minimal — narrows
        // whether the bug is sub-word ALONE or sub-word + non-sub-word interaction.
        public struct Tests23_OnlyShortStruct
        {
            public ArrayView<short> S0;
        }

        static void Tests23_OnlyShortKernel(
            Index1D _,
            Tests23_OnlyShortStruct s,
            ArrayView<int> output)
        {
            output[0] = (int)s.S0[0];
        }

        [TestMethod]
        public async Task Tests23_OnlyShortBodyStruct() => await RunEmulatedTest(async accelerator =>
        {
            using var s0 = accelerator.Allocate1D(new short[] { 42 });
            using var output = accelerator.Allocate1D<int>(1);
            var inputs = new Tests23_OnlyShortStruct { S0 = s0.View };
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_OnlyShortStruct, ArrayView<int>>(
                Tests23_OnlyShortKernel);
            k((Index1D)1, inputs, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            if (got[0] != 42) throw new Exception($"OnlyShort body struct: expected 42 got {got[0]}");
        });

        // Diagnostic: separate-args (no body struct) ArrayView<short> + ArrayView<int>.
        // If THIS passes on CUDA, the bug is body-struct-specific — argument mapping for
        // mixed sub-word + non-sub-word body-struct fields. Surfaced 2026-05-04 by
        // Tests23_MinimalShortIntBodyStruct CUDA "illegal memory access".
        static void Tests23_SeparateShortIntKernel(
            Index1D _,
            ArrayView<short> s0,
            ArrayView<int> i0,
            ArrayView<int> output)
        {
            output[0] = (int)s0[0] + i0[0];
        }

        [TestMethod]
        public async Task Tests23_SeparateShortInt_NoBodyStruct() => await RunEmulatedTest(async accelerator =>
        {
            using var s0 = accelerator.Allocate1D(new short[] { 7 });
            using var i0 = accelerator.Allocate1D(new int[] { 100 });
            using var output = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<short>, ArrayView<int>, ArrayView<int>>(
                Tests23_SeparateShortIntKernel);
            k((Index1D)1, s0.View, i0.View, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            if (got[0] != 107) throw new Exception($"SeparateShortInt: expected 107 got {got[0]}");
        });

        // Diagnostic: same body struct (1 short + 1 int) but kernel only reads the INT field.
        // Tests whether the bug is reading the short field, OR present from struct serialization
        // alone.
        static void Tests23_MinimalShortIntKernelIntOnly(
            Index1D _,
            Tests23_MinimalShortIntStruct s,
            ArrayView<int> output)
        {
            output[0] = s.I0[0];
        }

        [TestMethod]
        public async Task Tests23_MinimalShortIntBodyStruct_IntOnly() => await RunEmulatedTest(async accelerator =>
        {
            using var s0 = accelerator.Allocate1D(new short[] { 7 });
            using var i0 = accelerator.Allocate1D(new int[] { 100 });
            using var output = accelerator.Allocate1D<int>(1);
            var inputs = new Tests23_MinimalShortIntStruct { S0 = s0.View, I0 = i0.View };
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_MinimalShortIntStruct, ArrayView<int>>(
                Tests23_MinimalShortIntKernelIntOnly);
            k((Index1D)1, inputs, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            if (got[0] != 100) throw new Exception($"IntOnly: expected 100 got {got[0]}");
        });

        // Diagnostic: 2 short views (no int). If this PASSES on CUDA, the bug is mixed
        // sub-word + non-sub-word specifically. If it fails, sub-word + sub-word body
        // struct is also broken.
        public struct Tests23_TwoShortStruct
        {
            public ArrayView<short> S0;
            public ArrayView<short> S1;
        }

        static void Tests23_TwoShortKernel(
            Index1D _,
            Tests23_TwoShortStruct s,
            ArrayView<int> output)
        {
            output[0] = (int)s.S0[0] + (int)s.S1[0];
        }

        [TestMethod]
        public async Task Tests23_TwoShortBodyStruct() => await RunEmulatedTest(async accelerator =>
        {
            using var s0 = accelerator.Allocate1D(new short[] { 7 });
            using var s1 = accelerator.Allocate1D(new short[] { 100 });
            using var output = accelerator.Allocate1D<int>(1);
            var inputs = new Tests23_TwoShortStruct { S0 = s0.View, S1 = s1.View };
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_TwoShortStruct, ArrayView<int>>(
                Tests23_TwoShortKernel);
            k((Index1D)1, inputs, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            if (got[0] != 107) throw new Exception($"TwoShort: expected 107 got {got[0]}");
        });

        // Diagnostic: 2 short ArrayView1D (vs ArrayView). The 1D variant has Extent + Stride
        // fields, which might trigger a different codegen path on CUDA.
        public struct Tests23_TwoShortStructDense
        {
            public ArrayView1D<short, Stride1D.Dense> S0;
            public ArrayView1D<short, Stride1D.Dense> S1;
        }

        static void Tests23_TwoShortDenseKernel(
            Index1D _,
            Tests23_TwoShortStructDense s,
            ArrayView<int> output)
        {
            output[0] = (int)s.S0[0] + (int)s.S1[0];
        }

        [TestMethod]
        public async Task Tests23_TwoShortBodyStructDense() => await RunEmulatedTest(async accelerator =>
        {
            using var s0 = accelerator.Allocate1D(new short[] { 7 });
            using var s1 = accelerator.Allocate1D(new short[] { 100 });
            using var output = accelerator.Allocate1D<int>(1);
            var inputs = new Tests23_TwoShortStructDense { S0 = s0.View, S1 = s1.View };
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_TwoShortStructDense, ArrayView<int>>(
                Tests23_TwoShortDenseKernel);
            k((Index1D)1, inputs, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            if (got[0] != 107) throw new Exception($"TwoShortDense: expected 107 got {got[0]}");
        });

        // Repro for Data's StyleMosaic Wasm hang at rc.16: 24+ sequential InstanceNorm-shape
        // pairs (Pass1 writes fresh per-call buffer, Pass2 reads it). Per-call buffer
        // pattern means each pair allocates 2 fresh buffers, never host-written, GPU-written
        // by Pass 1, GPU-read by Pass 2. With 24+ iterations, 48+ buffers churn through the
        // dispatcher's snapshot path. Tests23_OutputThenInput_NoStaleSnapshot covers the 1-pair
        // case; this one stress-tests scaling.
        static void Tests23_StyleMosaicShape_WriteKernel(
            Index1D idx, ArrayView<float> input, ArrayView<float> intermediate)
        {
            intermediate[idx] = input[idx] * 7.0f + 13.0f;
        }
        static void Tests23_StyleMosaicShape_ReadKernel(
            Index1D idx, ArrayView<float> intermediate, ArrayView<float> output)
        {
            output[idx] = intermediate[idx] * 2.0f;
        }

        [TestMethod]
        public async Task Tests23_StyleMosaicShape_PairedDispatchesScale() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 65536; // 256×256 image scale (StyleMosaic uses ~256×256×3)
            const int Pairs = 32; // mimics StyleMosaic's 24+ InstanceNorm calls

            using var input = accelerator.Allocate1D<float>(N);
            var src = new float[N];
            for (int i = 0; i < N; i++) src[i] = (i + 1) * 0.5f;
            input.CopyFromCPU(src);

            // Allocate output up front for the final read.
            using var finalOutput = accelerator.Allocate1D<float>(N);

            var kWrite = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                Tests23_StyleMosaicShape_WriteKernel);
            var kRead = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                Tests23_StyleMosaicShape_ReadKernel);

            // Track all per-call buffers so we can dispose them after sync.
            var perCallBufs = new System.Collections.Generic.List<MemoryBuffer1D<float, Stride1D.Dense>>(Pairs);
            try
            {
                var prevReadOutput = input;
                for (int p = 0; p < Pairs; p++)
                {
                    // Allocate fresh per-call intermediate buffer (HostWriteCounter==0).
                    var intermediate = accelerator.Allocate1D<float>(N);
                    perCallBufs.Add(intermediate);

                    // Pass 1: write to intermediate from prev result.
                    kWrite((Index1D)N, prevReadOutput.View, intermediate.View);

                    // Pass 2: read intermediate, write to a new fresh per-call output.
                    var output = accelerator.Allocate1D<float>(N);
                    perCallBufs.Add(output);
                    kRead((Index1D)N, intermediate.View, output.View);

                    prevReadOutput = output;
                }

                // Final read: copy the last output to finalOutput so we can verify.
                var kCopy = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                    (Index1D ii, ArrayView<float> a, ArrayView<float> b) => { b[ii] = a[ii]; });
                kCopy((Index1D)N, prevReadOutput.View, finalOutput.View);

                await accelerator.SynchronizeAsync();

                var got = await finalOutput.CopyToHostAsync<float>();
                // Expected: each pair multiplies by (7*2 + 13*2) → not exactly, the chain is:
                //   v_{p+1} = (v_p * 7 + 13) * 2
                // After Pairs iterations, expected[i] = chain(src[i], Pairs).
                for (int i = 0; i < N; i++)
                {
                    float v = src[i];
                    for (int p = 0; p < Pairs; p++) v = (v * 7.0f + 13.0f) * 2.0f;
                    if (System.Math.Abs(got[i] - v) > System.Math.Abs(v) * 1e-3f)
                        throw new System.Exception($"StyleMosaicShape idx {i}: expected {v} got {got[i]}");
                }
            }
            finally
            {
                foreach (var b in perCallBufs) b.Dispose();
            }
        });

        // Closer mimic of ML's InstanceNorm 2-pass shape: 6 ArrayView1D params on Pass2
        // (input, output, scale, bias, inMeans, inInvStds). Repeated 32 iterations with
        // fresh per-call inMeans/inInvStds buffers - matches Data's StyleMosaic pattern
        // exactly except for using compute that's lighter than real InstanceNorm.
        static void Tests23_InstanceNormShape_Pass1Kernel(
            Index1D sliceIdx,
            ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> outMeans,
            ArrayView1D<float, Stride1D.Dense> outInvStds,
            int spatial,
            float eps)
        {
            int s = sliceIdx.X;
            int baseOff = s * spatial;
            float sum = 0f;
            for (int i = 0; i < spatial; i++) sum += input[baseOff + i];
            float mean = sum / spatial;
            float varSum = 0f;
            for (int i = 0; i < spatial; i++) { var d = input[baseOff + i] - mean; varSum += d * d; }
            float var_ = varSum / spatial;
            outMeans[s] = mean;
            outInvStds[s] = 1f / MathF.Sqrt(var_ + eps);
        }
        static void Tests23_InstanceNormShape_Pass2Kernel(
            Index1D idx,
            ArrayView1D<float, Stride1D.Dense> input,
            ArrayView1D<float, Stride1D.Dense> output,
            ArrayView1D<float, Stride1D.Dense> scale,
            ArrayView1D<float, Stride1D.Dense> bias,
            ArrayView1D<float, Stride1D.Dense> inMeans,
            ArrayView1D<float, Stride1D.Dense> inInvStds,
            int N, int C, int spatial)
        {
            int i = idx.X;
            int sliceIdx = i / spatial;
            output[i] = (input[i] - inMeans[sliceIdx]) * inInvStds[sliceIdx] * scale[sliceIdx % C] + bias[sliceIdx % C];
        }

        [TestMethod]
        public async Task Tests23_InstanceNormShape_24Iterations() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 1, C = 32, Spatial = 256 * 256;
            const int Iter = 24;
            int total = N * C * Spatial;

            using var input = accelerator.Allocate1D<float>(total);
            using var output = accelerator.Allocate1D<float>(total);
            using var scale = accelerator.Allocate1D<float>(C);
            using var bias = accelerator.Allocate1D<float>(C);

            var inputData = new float[total];
            for (int i = 0; i < total; i++) inputData[i] = (i % 100) * 0.01f;
            input.CopyFromCPU(inputData);
            var scaleData = new float[C]; var biasData = new float[C];
            for (int i = 0; i < C; i++) { scaleData[i] = 1.0f; biasData[i] = 0.0f; }
            scale.CopyFromCPU(scaleData);
            bias.CopyFromCPU(biasData);

            var k1 = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, int, float>(Tests23_InstanceNormShape_Pass1Kernel);
            var k2 = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView1D<float, Stride1D.Dense>,
                int, int, int>(Tests23_InstanceNormShape_Pass2Kernel);

            var perCallBufs = new System.Collections.Generic.List<MemoryBuffer1D<float, Stride1D.Dense>>();
            try
            {
                var prevOut = input;
                for (int p = 0; p < Iter; p++)
                {
                    var inMeans = accelerator.Allocate1D<float>(N * C);
                    var inInvStds = accelerator.Allocate1D<float>(N * C);
                    perCallBufs.Add(inMeans); perCallBufs.Add(inInvStds);
                    k1((Index1D)(N * C), prevOut.View.AsContiguous(), inMeans.View, inInvStds.View, Spatial, 1e-5f);
                    var nextOut = accelerator.Allocate1D<float>(total);
                    perCallBufs.Add(nextOut);
                    k2((Index1D)total, prevOut.View.AsContiguous(), nextOut.View, scale.View, bias.View, inMeans.View, inInvStds.View, N, C, Spatial);
                    prevOut = nextOut;
                }
                await accelerator.SynchronizeAsync();

                // Sanity: read back output element 0 to confirm no kernel error.
                var got = await prevOut.CopyToHostAsync<float>(0, 4);
                if (float.IsNaN(got[0]))
                    throw new Exception($"InstanceNormShape iter 24: got NaN at [0]");
            }
            finally
            {
                foreach (var b in perCallBufs) b.Dispose();
            }
        });

        // DistilBERT-shape Pow: exponent loaded from ArrayView (NOT a literal PrimitiveValue).
        // Mimics ONNX LayerNorm's `(x - mean)^pow_initializer_2.0` shape that produced NaN
        // on WebGL pre-rc.21 (Data's first-divergent at node 10 Pow). The static-const
        // detection fails when value.Right.Resolve() returns a Load node instead of a
        // PrimitiveValue; the runtime-safe pow wrapper handles the negative-base case.
        static void Tests23_PowFromBufferKernel(
            Index1D idx,
            ArrayView<float> input,
            ArrayView<float> exponent,
            ArrayView<float> output)
        {
            output[idx] = MathF.Pow(input[idx], exponent[0]);
        }

        [TestMethod]
        public async Task Tests23_PowNegativeBase_ExponentFromBuffer_NoNaN() => await RunEmulatedTest(async accelerator =>
        {
            // Same input set as Tests23_PowNegativeBase_IntExp2_NoNaN, but exponent
            // is loaded from a 1-element buffer (mimics ONNX initializer ArrayView).
            var input = new float[] { 0.055f, -0.037f, -1.5f, 2.0f, -0.001f, 3.14f, -3.14f };
            var exp = new float[] { 2.0f };
            var expected = input.Select(v => v * v).ToArray();
            using var inBuf = accelerator.Allocate1D(input);
            using var expBuf = accelerator.Allocate1D(exp);
            using var outBuf = accelerator.Allocate1D<float>(input.Length);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(
                Tests23_PowFromBufferKernel);
            k((Index1D)input.Length, inBuf.View, expBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();

            var got = await outBuf.CopyToHostAsync<float>(0, input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                if (float.IsNaN(got[i]))
                    throw new Exception($"pow({input[i]}, exp[0]=2) returned NaN at idx {i} - the runtime-safe pow wrapper isn't catching the negative-base case when the exponent is loaded from a buffer.");
                float diff = MathF.Abs(got[i] - expected[i]);
                if (diff > MathF.Abs(expected[i]) * 1e-4f + 1e-6f)
                    throw new Exception($"pow({input[i]}, exp[0]=2): expected {expected[i]} got {got[i]}");
            }
        });

        // Test J: WebGL glWorker.js subview-offset leak across same-program dispatches.
        // Surfaced 2026-05-04 by Data's StyleMosaic node 55 Gather first-divergent
        // capture: WebGL's glWorker.js was conditionally setting the
        // `u_paramX_offset` uniform only when elementOffset != 0. WebGL uniforms
        // persist on the program object across draw calls; if dispatch N had
        // elementOffset=K (non-zero), the uniform was set to K. If dispatch N+1 had
        // elementOffset=0 with the SAME program, the uniform was NOT reset and
        // retained K, causing the second dispatch to read at offset K instead of 0.
        // This test fires that sequence directly: SubView(2, 1) then SubView(0, 1)
        // on a fresh buffer with the same Scale program. Pre-fix: WebGL returns
        // bufB[2] (=30) instead of bufB[0] (=10). Post-fix: WebGL returns 10.
        static void Tests23_SubViewOffsetLeakKernel(
            Index1D _,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            output[0] = input[0];
        }

        [TestMethod]
        public async Task Tests23_SubViewOffsetLeakAcrossDispatches() => await RunEmulatedTest(async accelerator =>
        {
            using var bufA = accelerator.Allocate1D(new int[] { 100, 200, 300, 400 });
            using var bufB = accelerator.Allocate1D(new int[] { 10, 20, 30, 40 });
            using var outA = accelerator.Allocate1D<int>(1);
            using var outB = accelerator.Allocate1D<int>(1);

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                Tests23_SubViewOffsetLeakKernel);

            // Dispatch 1: Read bufA[2]=300. Sets the program's u_paramX_offset uniform to 2.
            k((Index1D)1, bufA.View.SubView(2, 1), outA.View.SubView(0, 1));

            // Dispatch 2: Read bufB[0]=10 with the SAME program. If the offset uniform
            // leaked from Dispatch 1, the read goes to bufB[2]=30 instead of bufB[0]=10.
            k((Index1D)1, bufB.View.SubView(0, 1), outB.View.SubView(0, 1));

            await accelerator.SynchronizeAsync();

            var gotA = await outA.CopyToHostAsync<int>();
            var gotB = await outB.CopyToHostAsync<int>();

            if (gotA[0] != 300)
                throw new Exception($"Dispatch 1: expected 300 got {gotA[0]}");
            if (gotB[0] != 10)
                throw new Exception($"Dispatch 2 (SubView(0,1) after SubView(2,1) on same program): " +
                    $"expected 10 got {gotB[0]}. " +
                    $"If WebGL returns 30, glWorker.js skipped setting u_paramX_offset to 0 and the " +
                    $"prior dispatch's offset=2 leaked into this read.");
        });

        // Test K: Data's repro for the GPT-2 NaN/Inf weight-corruption bug
        // surfaced 2026-05-04. `BufferPool.AllocatePermanentChunked` uploads
        // large weight tensors (>1 MB) in chunks via
        //   `buffer.View.SubView(offset, n).CopyFromCPU(chunk)`
        // For GPT-2's lm_head (50257 x 768 = 154 MB float32) this is ~150
        // chunks each at offset > 0. After the rc.10 cascade closed 11 of 15
        // WebGL ML failures, GPT-2 still fires "50257/50257 logits are NaN/Inf
        // — Chunked weight upload may have corrupted weights." DistilBERT +
        // SemanticSearch (which share the same DistilBERT model with a 23.4 M
        // float embedding matrix) also still fail with near-zero logits.
        //
        // This test reproduces the chunked-upload pattern with a tiny buffer:
        // two SubView CopyFromCPU calls at non-overlapping offsets, then read
        // back. Pre-fix on WebGL (if Data's hypothesis holds): chunk B lands
        // at the wrong place in the texture and chunk A's region is wrong.
        // Post-fix: full round-trip.
        [TestMethod]
        public async Task Tests23_SubView_CopyFromCPU_NonZeroOffset_Roundtrip() => await RunEmulatedTest(async accelerator =>
        {
            int total = 1024;
            using var buf = accelerator.Allocate1D<float>(total);
            var chunkA = Enumerable.Repeat(1f, 512).ToArray();
            var chunkB = Enumerable.Repeat(2f, 512).ToArray();
            buf.View.SubView(0, 512).CopyFromCPU(chunkA);
            buf.View.SubView(512, 512).CopyFromCPU(chunkB);
            await accelerator.SynchronizeAsync();

            var got = await buf.CopyToHostAsync<float>(0, total);
            for (int i = 0; i < 512; i++)
                if (got[i] != 1f)
                    throw new Exception($"Chunked upload SubView(0,512): expected 1 at index {i} got {got[i]}");
            for (int i = 512; i < 1024; i++)
                if (got[i] != 2f)
                    throw new Exception($"Chunked upload SubView(512,512): expected 2 at index {i} got {got[i]}");
        });

        // Test L: GPT-2-scale chunked SubView upload. The 2-chunk repro (Test K)
        // PASSED on every backend. GPT-2's lm_head is 154 MB float32 → ~150 chunks
        // of 1 MB each via `BufferPool.AllocatePermanentChunked`. Per Data's triage
        // 2026-05-04, GPT-2 still fires "50257/50257 logits NaN/Inf" on rc.10 with
        // the test author flagging chunked-upload as the suspect. This test scales
        // up the chunk count to mimic GPT-2's actual upload pattern and verifies
        // every element round-trips correctly. If WebGL fails this, the chunked
        // path has a bug at scale that the 2-chunk case doesn't surface.
        [TestMethod]
        public async Task Tests23_SubView_CopyFromCPU_ManyChunks_GPT2Scale_Roundtrip() => await RunEmulatedTest(async accelerator =>
        {
            // Match GPT-2 lm_head pattern: ~150 chunks of 256K floats each (1MB per chunk),
            // total ~38M floats = ~154MB. Each chunk fills with a unique float pattern
            // (chunk index encoded in high bits) so corruption is identifiable per chunk.
            const int CHUNK_FLOATS = 262144; // 1 MB
            const int NUM_CHUNKS = 150;
            int total = CHUNK_FLOATS * NUM_CHUNKS;

            using var buf = accelerator.Allocate1D<float>(total);

            // Upload in chunks. Each chunk's value at index i is `chunkIdx * CHUNK_FLOATS + i`
            // cast to float. So buf[k] should == k after upload.
            var chunkData = new float[CHUNK_FLOATS];
            for (int chunkIdx = 0; chunkIdx < NUM_CHUNKS; chunkIdx++)
            {
                int baseIdx = chunkIdx * CHUNK_FLOATS;
                for (int i = 0; i < CHUNK_FLOATS; i++)
                    chunkData[i] = (float)(baseIdx + i);
                buf.View.SubView(baseIdx, CHUNK_FLOATS).CopyFromCPU(chunkData);
            }
            await accelerator.SynchronizeAsync();

            var got = await buf.CopyToHostAsync<float>(0, total);

            // Verify every chunk round-trips. Spot-check first 10 elements + every chunk
            // boundary + last 10.
            int violations = 0;
            int firstViolationAt = -1;
            float firstViolationGot = 0;
            for (int k = 0; k < total; k++)
            {
                float expected = (float)k;
                if (got[k] != expected)
                {
                    violations++;
                    if (firstViolationAt < 0)
                    {
                        firstViolationAt = k;
                        firstViolationGot = got[k];
                    }
                }
            }
            if (violations > 0)
                throw new Exception($"GPT-2-scale chunked upload: {violations}/{total} elements wrong. " +
                    $"First mismatch at index {firstViolationAt}: expected {firstViolationAt}, got {firstViolationGot}. " +
                    $"chunkIdx={firstViolationAt / CHUNK_FLOATS} offsetInChunk={firstViolationAt % CHUNK_FLOATS}");
        });

        // Test M: ROOT-CAUSE REPRO for the YOLOv8 Wasm Softmax bug. The Wasm dispatcher
        // has a host-write-vs-queued-dispatch race when:
        //   1. Dispatch D1 is queued (returns immediately, async task in flight).
        //   2. Host CopyFromCPU writes buffer B with version V1, queues D2 reading B.
        //   3. Host CopyFromCPU writes buffer B with version V2, queues D3 reading B.
        //   4. D2 runs, awaits prior tasks, then copy-IN reads B.SharedBuffer.
        //   5. By the time D2's copy-IN runs, V2 has already overwritten B. D2 reads V2 data.
        //
        // Sequencing on Blazor WASM (single-threaded async):
        //   - D2's RunKernelAsync runs sync up to the FIRST await (on _pendingWork).
        //   - If _pendingWork has prior tasks, D2's body PAUSES at the await.
        //   - Caller continues to D3.Transpose() which CopyFromCPU's B with V2.
        //   - When D2 resumes after await, its copy-IN reads B.SharedBuffer (= V2).
        //
        // SpawnDev.ILGPU.ML's TransposeKernel reuses `_paramsBuf` across calls, so two
        // back-to-back transpose dispatches with a prior dispatch in flight hit this.
        //
        // Repro shape:
        //   - Buffer X: written by CopyFromCPU with value 100, queued kernel D1 reads X.
        //   - Buffer X: written by CopyFromCPU with value 200, queued kernel D2 reads X.
        //   - D1 should see X=100. D2 should see X=200.
        //   - On Wasm: D1 actually sees X=200 (host already overwrote SharedBuffer).
        //
        // Pre-existing dispatch (D0) is needed to force D1's await. Using a no-op kernel.
        static void Tests23_DispatchRaceNoop(Index1D _, ArrayView<int> dummy) { /* no-op */ }
        static void Tests23_DispatchRaceReadValue(Index1D _, ArrayView<int> source, ArrayView<int> result)
        {
            result[0] = source[0];
        }

        [TestMethod]
        public async Task Tests23_HostWriteVsQueuedDispatchRace() => await RunEmulatedTest(async accelerator =>
        {
            using var dummyBuf = accelerator.Allocate1D<int>(1);
            using var sharedSrc = accelerator.Allocate1D<int>(1);  // host-written, read by D1+D2
            using var d1Result = accelerator.Allocate1D<int>(1);
            using var d2Result = accelerator.Allocate1D<int>(1);

            var noopK = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(Tests23_DispatchRaceNoop);
            var readK = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(Tests23_DispatchRaceReadValue);

            // D0: prior dispatch (forces D1 to await). Reads dummyBuf, no-op.
            noopK((Index1D)1, dummyBuf.View);

            // D1: write 100 to sharedSrc, queue D1 to read sharedSrc[0] -> d1Result[0].
            sharedSrc.View.CopyFromCPU(new[] { 100 });
            readK((Index1D)1, sharedSrc.View, d1Result.View);

            // D2: write 200 to sharedSrc, queue D2 to read sharedSrc[0] -> d2Result[0].
            sharedSrc.View.CopyFromCPU(new[] { 200 });
            readK((Index1D)1, sharedSrc.View, d2Result.View);

            await accelerator.SynchronizeAsync();

            var d1 = await d1Result.CopyToHostAsync<int>();
            var d2 = await d2Result.CopyToHostAsync<int>();

            // Expected: D1 reads what was set right before its queue (100). D2 reads 200.
            // BUG ON WASM: D1's copy-IN runs AFTER D2's CopyFromCPU(200) overwrote SharedBuffer,
            // so D1 reads 200 instead of 100.
            if (d1[0] != 100)
                throw new Exception($"D1 should see sharedSrc=100 (set right before D1 was queued), got {d1[0]}. " +
                    "If d1=200 on Wasm, D1's copy-IN ran AFTER D2's CopyFromCPU overwrote SharedBuffer. " +
                    "Root cause of YOLOv8 Wasm Softmax: the queued-dispatch-vs-host-write race.");
            if (d2[0] != 200)
                throw new Exception($"D2 should see sharedSrc=200, got {d2[0]}");
        });

        // Test N: WebGL Pow with NEGATIVE BASE + integer exponent. GLSL `pow(x, y)` is
        // undefined for x < 0; ANGLE emits `exp(y * log(x))` and `log(negative_x)` is NaN.
        // Surfaced 2026-05-04 by Data's DistilBERT WebGL first-divergent-node capture: at
        // node 10 'Pow' (LayerNorm's `(x - mean)^2`), elements with negative `x - mean`
        // returned NaN on WebGL, cascading NaN through every downstream LayerNorm /
        // ReduceMean / Sqrt and producing 50257/50257 NaN logits in GPT-2.
        // Fix: when the exponent is a constant non-negative integer, expand pow() to
        // repeated multiplication which is well-defined for negative bases.
        static void Tests23_PowNegativeBaseInt2Kernel(
            Index1D idx,
            ArrayView<float> input,
            ArrayView<float> output)
        {
            output[idx] = MathF.Pow(input[idx], 2f);
        }

        [TestMethod]
        public async Task Tests23_PowNegativeBase_IntExp2_NoNaN() => await RunEmulatedTest(async accelerator =>
        {
            var input = new float[] { 0.055f, -0.037f, -1.5f, 2.0f, -0.001f, 3.14f, -3.14f };
            var expected = input.Select(v => v * v).ToArray();
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<float>(input.Length);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(
                Tests23_PowNegativeBaseInt2Kernel);
            k((Index1D)input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();

            var got = await outBuf.CopyToHostAsync<float>(0, input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                float diff = MathF.Abs(got[i] - expected[i]);
                if (float.IsNaN(got[i]))
                    throw new Exception($"pow({input[i]}, 2) returned NaN at idx {i}; expected {expected[i]}. " +
                        "GLSL pow(x, y) is undefined for x<0; codegen should expand to x*x for integer exponents.");
                if (diff > 1e-5f)
                    throw new Exception($"pow({input[i]}, 2): expected {expected[i]} got {got[i]}");
            }
        });

        // Test G: SAME as F but WITHOUT the compound `iter < 8` safety guard.
        // Tuvok's Tests22 inline has no safety cap — loop runs purely on the
        // `rng <= EC_CODE_BOT` condition. This isolates whether the compound
        // && condition was hiding the actual codegen bug.
        // Safety: the loop SHOULD exit in 3 iterations on every backend.
        // If a backend gets stuck in an infinite loop, the test framework's
        // accelerator-level timeout (or kernel watchdog) catches it.
        static void Tests23_NormalizeShapeBareConditionKernel(
            Index1D _,
            ArrayView<byte> packet, int packetStart, int packetStorage,
            ArrayView<uint> output)
        {
            uint rng = 1u << TESTS23E_CODE_EXTRA;
            uint offs = 0u;
            int  rem = 0;
            if (offs < (uint)packetStorage)
            {
                rem = packet[packetStart + (int)offs];
                offs++;
            }
            uint val = rng - 1u - (uint)(rem >> (TESTS23E_SYM_BITS - TESTS23E_CODE_EXTRA));
            int  iter = 0;
            // No safety cap — bare `while (rng <= ...)` matches Tuvok's exact loop shape.
            while (rng <= TESTS23E_CODE_BOT)
            {
                rng <<= TESTS23E_SYM_BITS;
                int sym = rem;
                if (offs < (uint)packetStorage)
                {
                    rem = packet[packetStart + (int)offs];
                    offs++;
                }
                else { rem = 0; }
                sym = (sym << TESTS23E_SYM_BITS | rem) >> (TESTS23E_SYM_BITS - TESTS23E_CODE_EXTRA);
                val = ((val << TESTS23E_SYM_BITS) + (TESTS23E_SYM_MAX & (uint)~sym)) & (TESTS23E_CODE_TOP - 1u);
                iter++;
            }
            output[0] = rng;
            output[1] = val;
            output[2] = offs;
            output[3] = (uint)rem;
            output[4] = (uint)iter;
        }

        [TestMethod]
        public async Task Tests23_NormalizeShape_BareCondition() => await RunEmulatedTest(async accelerator =>
        {
            var packet = new byte[16];
            for (int i = 0; i < packet.Length; i++)
                packet[i] = (byte)((i * 0x37) ^ 0x9C);
            using var dPacket = accelerator.Allocate1D(packet);
            using var dOut = accelerator.Allocate1D<uint>(5);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, int, int, ArrayView<uint>>(Tests23_NormalizeShapeBareConditionKernel);
            k((Index1D)1, dPacket.View, 0, packet.Length, dOut.View);
            await accelerator.SynchronizeAsync();
            var r = await dOut.CopyToHostAsync<uint>();
            if (r[0] != 0x80000000u)
                throw new Exception($"NormalizeShape_BareCondition Rng expected 0x80000000 got 0x{r[0]:X8} " +
                    $"(val=0x{r[1]:X8} offs={r[2]} rem=0x{r[3]:X2} iter={r[4]}).");
            if (r[4] != 3u)
                throw new Exception($"NormalizeShape_BareCondition iter expected 3 got {r[4]} (rng=0x{r[0]:X8} offs={r[2]})");
        });

        // Test I: Tuvok's OpusRangeDecoderGpu.DecodeBitLogP first-bit divergence on WebGPU
        // (DevComms 2026-05-04: tuvok-to-geordi-decodebitlogp-webgpu-2026-05-04.md). The
        // libopus decoder's Init Normalize loop produces Rng = 0x80000000 (high bit set,
        // = INT_MIN as i32). Then `s = r >> logp` for unsigned `r` and small `logp` should
        // shift the high bit off — `0x80000000 >> 15 = 0x10000` (logical / unsigned shift).
        //
        // Bug: WGSLKernelFunctionGenerator.GenerateCode(BinaryArithmeticValue) emitted
        // `target = left >> u32(right)` for both Shl and Shr. WGSL's `i32 >> u32` is
        // arithmetic (sign-extending) shift — for `0x80000000_i32`, the result is
        // `0xFFFF0000_i32` (sign bits replicated), NOT `0x00010000`. ILGPU stores uint
        // as i32 (BasicValueType has no UInt32), so the codegen needs to bitcast through
        // u32 for unsigned shift right.
        //
        // Fix: when `value.IsUnsigned` and Shr on Int32, emit
        // `target = bitcast<i32>(bitcast<u32>(left) >> u32(right))`. Symmetric Shl fix:
        // always cast through u32 (Shl bit pattern is identical signed-vs-unsigned, but
        // i32 << that pushes a 1 into the sign bit triggers WGSL "shift by amount that
        // would overflow" UB on some validators).
        //
        // This test exercises ONLY the Shr code path: a single uint shift right of
        // 0x80000000 by 15 should equal 0x10000. Pre-fix on WebGPU: returns 0xFFFF0000.
        // Post-fix: 0x10000 on every backend.
        static void Tests23_UnsignedShrHighBitKernel(
            Index1D _,
            ArrayView<uint> input,
            ArrayView<uint> output)
        {
            uint r = input[0];
            int logp = (int)input[1];
            output[0] = r >> logp;
            output[1] = r >> 1;
            output[2] = r >> 31;
        }

        [TestMethod]
        public async Task Tests23_UnsignedShr_HighBitNoSignExtend() => await RunEmulatedTest(async accelerator =>
        {
            var input = new uint[] { 0x80000000u, 15u };
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<uint>(3);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(
                Tests23_UnsignedShrHighBitKernel);
            k((Index1D)1, dIn.View, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<uint>();
            if (got[0] != 0x00010000u)
                throw new Exception($"0x80000000u >> 15 expected 0x00010000 got 0x{got[0]:X8} (signed shift would give 0xFFFF0000)");
            if (got[1] != 0x40000000u)
                throw new Exception($"0x80000000u >> 1 expected 0x40000000 got 0x{got[1]:X8} (signed shift would give 0xC0000000)");
            if (got[2] != 0x00000001u)
                throw new Exception($"0x80000000u >> 31 expected 0x00000001 got 0x{got[2]:X8} (signed shift would give 0xFFFFFFFF)");
        });

        // Test J: Mimic the actual DecodeBitLogP first-decode shape end-to-end in a
        // single kernel — same surface that fails Tuvok's
        // OpusRangeDecoderGpu_DecodeBitLogP_LogP15Mixed test on WebGPU. Mirrors the
        // shape of `r >> logp; ret = d < s ? 1 : 0; if (ret == 0) val = d - s;`. With
        // `r = 0x80000000` and `d = 0x40000000` and `logp = 15`, expected:
        //   s = 0x80000000 >> 15 = 0x10000  (unsigned)
        //   d (=0x40000000) < s (=0x10000)? FALSE (0x40000000 > 0x10000 unsigned)
        //   ret = 0
        //   val = d - s = 0x40000000 - 0x10000 = 0x3FFF0000
        //
        // Pre-fix WebGPU: s = 0xFFFF0000 (signed shift), d (=0x40000000) < s (=0xFFFF0000
        // as i32 = -65536) signed compare: d as i32 is +1073741824, +1073741824 < -65536
        // is FALSE, so ret=0 — same result by coincidence here BUT val computation
        // d - s = 0x40000000 - 0xFFFF0000 = 0x40010000 (wraparound) — wrong by exactly
        // the shift error.
        //
        // The IsUnsignedOrUnordered compare bitcast already protects the < check
        // (rc.7 GLSL fix has a parallel WGSL implementation at line 5548). The Shr
        // bitcast is what's missing.
        static void Tests23_DecodeBitLogPShapeKernel(
            Index1D _,
            ArrayView<uint> input,
            ArrayView<uint> output)
        {
            uint r = input[0];
            uint d = input[1];
            int logp = (int)input[2];
            uint s = r >> logp;
            int ret = d < s ? 1 : 0;
            uint newVal = d;
            if (ret == 0) newVal = d - s;
            uint newRng = ret != 0 ? s : r - s;
            output[0] = s;
            output[1] = (uint)ret;
            output[2] = newVal;
            output[3] = newRng;
        }

        [TestMethod]
        public async Task Tests23_DecodeBitLogP_FirstDecodeShape() => await RunEmulatedTest(async accelerator =>
        {
            // r = 0x80000000 (post-Init Normalize value), d = 0x40000000 (mid-range val),
            // logp = 15 (Opus typical for transient/silence/intra flags).
            var input = new uint[] { 0x80000000u, 0x40000000u, 15u };
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<uint>(4);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(
                Tests23_DecodeBitLogPShapeKernel);
            k((Index1D)1, dIn.View, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<uint>();
            uint expectedS = 0x80000000u >> 15;
            uint d_in = 0x40000000u;
            int expectedRet = d_in < expectedS ? 1 : 0;
            uint expectedVal = expectedRet == 0 ? d_in - expectedS : d_in;
            uint expectedRng = expectedRet != 0 ? expectedS : 0x80000000u - expectedS;
            if (got[0] != expectedS)
                throw new Exception($"DecodeBitLogP s expected 0x{expectedS:X8} got 0x{got[0]:X8} (signed shift bug)");
            if ((int)got[1] != expectedRet)
                throw new Exception($"DecodeBitLogP ret expected {expectedRet} got {got[1]}");
            if (got[2] != expectedVal)
                throw new Exception($"DecodeBitLogP val expected 0x{expectedVal:X8} got 0x{got[2]:X8}");
            if (got[3] != expectedRng)
                throw new Exception($"DecodeBitLogP rng expected 0x{expectedRng:X8} got 0x{got[3]:X8}");
        });

        // Test M: WGSL uint-as-i32 division/remainder bitcast. Sister fix to the Shr
        // signed-shift fix (Test I) — Tuvok's `OpusRangeDecoderGpu_DecodeUint_*` on
        // WebGPU surfaces this 2026-05-04. After libopus Init, `state.Rng = 0x80000000`.
        // `state.Rng / ft (= 6)` should produce `0x15555555` (unsigned), but signed div
        // gives `0xEAAAAAAB`. Fix: WGSL Div/Rem on Int32 with IsUnsigned bitcasts
        // through u32 (parallel to the Shr bitcast pattern at lines 5063-5089).
        static void Tests23_UnsignedDivRemHighBitKernel(
            Index1D _,
            ArrayView<uint> input,
            ArrayView<uint> output)
        {
            uint a = input[0];
            uint b = input[1];
            output[0] = a / b;
            output[1] = a % b;
        }

        [TestMethod]
        public async Task Tests23_UnsignedDivRem_HighBitNoSignDiv() => await RunEmulatedTest(async accelerator =>
        {
            // a = 0x80000000 (high bit set, INT_MIN as i32), b = 6
            var input = new uint[] { 0x80000000u, 6u };
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<uint>(2);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(
                Tests23_UnsignedDivRemHighBitKernel);
            k((Index1D)1, dIn.View, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<uint>();
            // Unsigned: 0x80000000 / 6 = 0x15555555, 0x80000000 % 6 = 0x2.
            // Signed (pre-fix WebGPU): -INT_MIN / 6 sign-flipped, gives 0xEAAAAAAB.
            if (got[0] != 0x15555555u)
                throw new Exception($"0x80000000u / 6u expected 0x15555555 got 0x{got[0]:X8}");
            if (got[1] != 0x2u)
                throw new Exception($"0x80000000u % 6u expected 0x2 got 0x{got[1]:X8}");
        });

        // Test K: CUDA "too many resources requested for launch" with unit-extent on a
        // register-heavy multi-ArrayView body-struct kernel. Tuvok's SilkDecodeCoreGpu
        // (DevComms 2026-05-04) fired this with a 15-ArrayView body struct + LPC-synth
        // unroll — auto-grouped picked groupSize=64, register pressure × 64 threads
        // exceeded SM register file, `cudaLaunchKernel` rejected with
        // CUDA_ERROR_LAUNCH_OUT_OF_RESOURCES.
        //
        // Fix: `KernelLauncherBuilder.EmitLoadKernelConfig` now clamps the runtime
        // groupDim.X to `min(extent.X, customGroupSize)`. For Index1D=1 dispatches,
        // groupDim.X collapses to 1 — wasted hardware threads disappear, register
        // budget is freed, launch succeeds.
        //
        // This shape gives moderate register pressure: 12 ArrayView fields + per-field
        // load/multiply/accumulate. Pre-fix on CUDA the launch fails on register-tight
        // hardware; on register-loose hardware (3060+, lots of regs/SM) it may pass
        // either way. Post-fix passes universally.
        public struct Tests23_RegisterHeavyBody
        {
            public ArrayView<int> A0;
            public ArrayView<int> A1;
            public ArrayView<int> A2;
            public ArrayView<int> A3;
            public ArrayView<int> A4;
            public ArrayView<int> A5;
            public ArrayView<int> A6;
            public ArrayView<int> A7;
            public ArrayView<int> A8;
            public ArrayView<int> A9;
            public ArrayView<int> A10;
            public ArrayView<int> Out;
        }

        static void Tests23_RegisterHeavyBodyKernel(Index1D _, Tests23_RegisterHeavyBody b)
        {
            // Force per-field reads + accumulator chain that survives register
            // allocator passes. Simulates LPC-style multiply-accumulate.
            int s = 0;
            s += b.A0[0] * 7;
            s += b.A1[0] * 11;
            s += b.A2[0] * 13;
            s += b.A3[0] * 17;
            s += b.A4[0] * 19;
            s += b.A5[0] * 23;
            s += b.A6[0] * 29;
            s += b.A7[0] * 31;
            s += b.A8[0] * 37;
            s += b.A9[0] * 41;
            s += b.A10[0] * 43;
            b.Out[0] = s;
        }

        [TestMethod]
        public async Task Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure() =>
            await RunEmulatedTest(async accelerator =>
        {
            using var b0 = accelerator.Allocate1D<int>(1);
            using var b1 = accelerator.Allocate1D<int>(1);
            using var b2 = accelerator.Allocate1D<int>(1);
            using var b3 = accelerator.Allocate1D<int>(1);
            using var b4 = accelerator.Allocate1D<int>(1);
            using var b5 = accelerator.Allocate1D<int>(1);
            using var b6 = accelerator.Allocate1D<int>(1);
            using var b7 = accelerator.Allocate1D<int>(1);
            using var b8 = accelerator.Allocate1D<int>(1);
            using var b9 = accelerator.Allocate1D<int>(1);
            using var b10 = accelerator.Allocate1D<int>(1);
            using var bOut = accelerator.Allocate1D<int>(1);

            b0.CopyFromCPU(new[] { 2 });
            b1.CopyFromCPU(new[] { 3 });
            b2.CopyFromCPU(new[] { 5 });
            b3.CopyFromCPU(new[] { 7 });
            b4.CopyFromCPU(new[] { 11 });
            b5.CopyFromCPU(new[] { 13 });
            b6.CopyFromCPU(new[] { 17 });
            b7.CopyFromCPU(new[] { 19 });
            b8.CopyFromCPU(new[] { 23 });
            b9.CopyFromCPU(new[] { 29 });
            b10.CopyFromCPU(new[] { 31 });

            var body = new Tests23_RegisterHeavyBody
            {
                A0 = b0.View, A1 = b1.View, A2 = b2.View, A3 = b3.View, A4 = b4.View,
                A5 = b5.View, A6 = b6.View, A7 = b7.View, A8 = b8.View, A9 = b9.View,
                A10 = b10.View, Out = bOut.View
            };

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_RegisterHeavyBody>(
                Tests23_RegisterHeavyBodyKernel);
            // Index1D=1 is the trigger condition for the register-pressure × groupSize=64 trap.
            // Post-fix the dispatcher clamps groupDim.X to 1, the launch succeeds.
            k((Index1D)1, body);
            await accelerator.SynchronizeAsync();

            int expected = 2 * 7 + 3 * 11 + 5 * 13 + 7 * 17 + 11 * 19 + 13 * 23
                + 17 * 29 + 19 * 31 + 23 * 37 + 29 * 41 + 31 * 43;
            var got = await bOut.CopyToHostAsync<int>();
            if (got[0] != expected)
                throw new Exception($"RegisterHeavyBody: expected {expected} got {got[0]}");
        });

        // Test rc.24 CUDA register-pressure JIT cap (CU_JIT_MAX_REGISTERS=255).
        // Tuvok's SilkDecodeCoreGpu (deep LPC unroll over 16+ live variables) JIT'd
        // to a kernel statically requiring more registers than the per-thread
        // hardware cap of any sm_50+ device, so `cudaLaunchKernel` rejected with
        // `CUDA_ERROR_LAUNCH_OUT_OF_RESOURCES` even at blockDim=1. The fix is
        // structural: ILGPU now passes `CU_JIT_MAX_REGISTERS = 255` to
        // `cuModuleLoadDataEx`, forcing ptxas to spill instead of producing an
        // un-launchable kernel. Configurable via
        // `CudaAccelerator.DefaultMaxRegistersPerThread` (default 255, set 0 to
        // disable).
        //
        // The kernel below is intentionally register-pressure-heavy: a long
        // mutual-dependency chain of 24 live integers from a single ArrayView
        // input that must all stay alive through the final reduction, exercising
        // the deep-unroll pattern that surfaces ptxas' >255 reg/thread choice on
        // some shapes.
        static void Tests23_DeepUnrollKernel(
            Index1D _,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            int v0 = input[0];
            int v1 = input[1] * 7 + v0;
            int v2 = input[2] * 11 + v1;
            int v3 = input[3] * 13 + v2;
            int v4 = input[4] * 17 + v3;
            int v5 = input[5] * 19 + v4;
            int v6 = input[6] * 23 + v5;
            int v7 = input[7] * 29 + v6;
            int v8 = input[8] * 31 + v7;
            int v9 = input[9] * 37 + v8;
            int v10 = input[10] * 41 + v9;
            int v11 = input[11] * 43 + v10;
            int v12 = input[12] * 47 + v11;
            int v13 = input[13] * 53 + v12;
            int v14 = input[14] * 59 + v13;
            int v15 = input[15] * 61 + v14;
            int v16 = input[16] * 67 + v15;
            int v17 = input[17] * 71 + v16;
            int v18 = input[18] * 73 + v17;
            int v19 = input[19] * 79 + v18;
            int v20 = input[20] * 83 + v19;
            int v21 = input[21] * 89 + v20;
            int v22 = input[22] * 97 + v21;
            int v23 = input[23] * 101 + v22;
            // Final reduction folds every live value back, prevents register
            // re-use across the chain.
            output[0] = v0 ^ v1 ^ v2 ^ v3 ^ v4 ^ v5 ^ v6 ^ v7
                ^ v8 ^ v9 ^ v10 ^ v11 ^ v12 ^ v13 ^ v14 ^ v15
                ^ v16 ^ v17 ^ v18 ^ v19 ^ v20 ^ v21 ^ v22 ^ v23;
        }

        [TestMethod]
        public async Task Tests23_DeepUnroll_NoLaunchFailure() =>
            await RunEmulatedTest(async accelerator =>
        {
            var inputData = new int[24];
            for (int i = 0; i < 24; i++)
                inputData[i] = i + 1;
            using var inBuf = accelerator.Allocate1D<int>(24);
            using var outBuf = accelerator.Allocate1D<int>(1);
            inBuf.CopyFromCPU(inputData);
            var k = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>, ArrayView<int>>(
                Tests23_DeepUnrollKernel);
            k((Index1D)1, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();

            // CPU reference
            int v0 = inputData[0];
            int v1 = inputData[1] * 7 + v0;
            int v2 = inputData[2] * 11 + v1;
            int v3 = inputData[3] * 13 + v2;
            int v4 = inputData[4] * 17 + v3;
            int v5 = inputData[5] * 19 + v4;
            int v6 = inputData[6] * 23 + v5;
            int v7 = inputData[7] * 29 + v6;
            int v8 = inputData[8] * 31 + v7;
            int v9 = inputData[9] * 37 + v8;
            int v10 = inputData[10] * 41 + v9;
            int v11 = inputData[11] * 43 + v10;
            int v12 = inputData[12] * 47 + v11;
            int v13 = inputData[13] * 53 + v12;
            int v14 = inputData[14] * 59 + v13;
            int v15 = inputData[15] * 61 + v14;
            int v16 = inputData[16] * 67 + v15;
            int v17 = inputData[17] * 71 + v16;
            int v18 = inputData[18] * 73 + v17;
            int v19 = inputData[19] * 79 + v18;
            int v20 = inputData[20] * 83 + v19;
            int v21 = inputData[21] * 89 + v20;
            int v22 = inputData[22] * 97 + v21;
            int v23 = inputData[23] * 101 + v22;
            int expected = v0 ^ v1 ^ v2 ^ v3 ^ v4 ^ v5 ^ v6 ^ v7
                ^ v8 ^ v9 ^ v10 ^ v11 ^ v12 ^ v13 ^ v14 ^ v15
                ^ v16 ^ v17 ^ v18 ^ v19 ^ v20 ^ v21 ^ v22 ^ v23;

            var got = await outBuf.CopyToHostAsync<int>();
            if (got[0] != expected)
                throw new Exception($"DeepUnroll: expected {expected} got {got[0]}");
        });

        // Test rc.11/rc.12 dispatcher snapshot bug surfaced by Data 2026-05-04: ML
        // weights uploaded via chunked SubView.CopyFromCPU then read by many kernel
        // dispatches return zeros. Mimics BlazeFace pattern: large buffer,
        // chunked upload, multiple read-only dispatches.
        static void Tests23_ChunkedReadKernel(
            Index1D idx,
            ArrayView<int> input,
            ArrayView<int> output)
        {
            output[idx] = input[idx];
        }

        [TestMethod]
        public async Task Tests23_ChunkedUpload_ManyReads_NoZeros() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 1024;
            const int CHUNK = 256;
            using var bigBuf = accelerator.Allocate1D<int>(N);
            using var outBuf1 = accelerator.Allocate1D<int>(N);
            using var outBuf2 = accelerator.Allocate1D<int>(N);
            using var outBuf3 = accelerator.Allocate1D<int>(N);

            // Chunked upload: 4 SubView writes, each bumps HostWriteCounter.
            for (int c = 0; c < N / CHUNK; c++)
            {
                var chunk = new int[CHUNK];
                for (int i = 0; i < CHUNK; i++) chunk[i] = (c * CHUNK + i) + 1; // 1..N
                bigBuf.View.SubView(c * CHUNK, CHUNK).CopyFromCPU(chunk);
            }

            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                Tests23_ChunkedReadKernel);

            // Three dispatches all read bigBuf — same buffer, multiple reads.
            // Pre-rc.11 snapshot bug: would return zeros if snapshot was stale.
            k((Index1D)N, bigBuf.View, outBuf1.View);
            k((Index1D)N, bigBuf.View, outBuf2.View);
            k((Index1D)N, bigBuf.View, outBuf3.View);
            await accelerator.SynchronizeAsync();

            var got1 = await outBuf1.CopyToHostAsync<int>();
            var got2 = await outBuf2.CopyToHostAsync<int>();
            var got3 = await outBuf3.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int expected = i + 1;
                if (got1[i] != expected || got2[i] != expected || got3[i] != expected)
                    throw new Exception($"ChunkedUpload_ManyReads idx {i}: expected {expected}, "
                        + $"got1={got1[i]} got2={got2[i]} got3={got3[i]}");
            }
        });

        // The actual BlazeFace pattern: D1 WRITES intermediate buffer, D2 READS it.
        // Intermediate buffer has HostWriteCounter=0 (only GPU-written). At rc.11/rc.12
        // the snapshot logic always takes a fresh snapshot on first call (because
        // _currentSnapshot is null), capturing SharedBuffer at QUEUE time of D2 — which
        // is BEFORE D1 has finished. Snapshot is stale (zeros). D2 reads zeros.
        //
        // Fix: skip snapshot when HostWriteCounter == 0. Output buffers and intermediate
        // GPU-written buffers fall through to SharedBuffer; by D2's run time, D1's
        // copy-OUT has populated SharedBuffer and D2 reads correct data.
        static void Tests23_WriteToBufKernel(Index1D idx, ArrayView<int> input, ArrayView<int> intermediate)
        {
            intermediate[idx] = input[idx] * 7 + 13;
        }

        static void Tests23_ReadFromBufKernel(Index1D idx, ArrayView<int> intermediate, ArrayView<int> output)
        {
            output[idx] = intermediate[idx] * 2;
        }

        [TestMethod]
        public async Task Tests23_OutputThenInput_NoStaleSnapshot() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 64;
            using var input = accelerator.Allocate1D<int>(N);
            using var intermediate = accelerator.Allocate1D<int>(N);
            using var output = accelerator.Allocate1D<int>(N);

            var src = new int[N];
            for (int i = 0; i < N; i++) src[i] = i + 1;
            input.CopyFromCPU(src);

            var kWrite = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                Tests23_WriteToBufKernel);
            var kRead = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(
                Tests23_ReadFromBufKernel);

            // D1: input -> intermediate.  HostWriteCounter for intermediate stays 0.
            kWrite((Index1D)N, input.View, intermediate.View);
            // D2: intermediate -> output. Reads intermediate, which was GPU-written by D1.
            kRead((Index1D)N, intermediate.View, output.View);
            await accelerator.SynchronizeAsync();

            var got = await output.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int expected = (src[i] * 7 + 13) * 2;
                if (got[i] != expected)
                    throw new Exception($"OutputThenInput idx {i}: expected {expected} got {got[i]} "
                        + "— D2 likely read a stale snapshot of `intermediate` taken at queue time, "
                        + "before D1's GPU write completed.");
            }
        });

        // Test K-Diag: explicit 1×1 launch for the heavy struct kernel. If THIS fails on
        // CUDA the bug is purely register pressure independent of block size — meaning
        // ILGPU's launch validator (`MaxThreadsPerBlock` check) is rejecting the kernel
        // even at 1 thread. If THIS passes but auto-grouped fails, the problem is the
        // auto-grouped path's groupDim is still > 1.
        static void Tests23_RegisterHeavyExplicitLaunchKernel(Tests23_RegisterHeavyBody b)
        {
            int s = 0;
            s += b.A0[0] * 7;
            s += b.A1[0] * 11;
            s += b.A2[0] * 13;
            s += b.A3[0] * 17;
            s += b.A4[0] * 19;
            s += b.A5[0] * 23;
            s += b.A6[0] * 29;
            s += b.A7[0] * 31;
            s += b.A8[0] * 37;
            s += b.A9[0] * 41;
            s += b.A10[0] * 43;
            b.Out[0] = s;
        }

        [TestMethod]
        public async Task Tests23_RegisterHeavyBody_ExplicitOneByOne_NoLaunchFailure() =>
            await RunEmulatedTest(async accelerator =>
        {
            using var b0 = accelerator.Allocate1D<int>(1); b0.CopyFromCPU(new[] { 2 });
            using var b1 = accelerator.Allocate1D<int>(1); b1.CopyFromCPU(new[] { 3 });
            using var b2 = accelerator.Allocate1D<int>(1); b2.CopyFromCPU(new[] { 5 });
            using var b3 = accelerator.Allocate1D<int>(1); b3.CopyFromCPU(new[] { 7 });
            using var b4 = accelerator.Allocate1D<int>(1); b4.CopyFromCPU(new[] { 11 });
            using var b5 = accelerator.Allocate1D<int>(1); b5.CopyFromCPU(new[] { 13 });
            using var b6 = accelerator.Allocate1D<int>(1); b6.CopyFromCPU(new[] { 17 });
            using var b7 = accelerator.Allocate1D<int>(1); b7.CopyFromCPU(new[] { 19 });
            using var b8 = accelerator.Allocate1D<int>(1); b8.CopyFromCPU(new[] { 23 });
            using var b9 = accelerator.Allocate1D<int>(1); b9.CopyFromCPU(new[] { 29 });
            using var b10 = accelerator.Allocate1D<int>(1); b10.CopyFromCPU(new[] { 31 });
            using var bOut = accelerator.Allocate1D<int>(1);

            var body = new Tests23_RegisterHeavyBody
            {
                A0 = b0.View, A1 = b1.View, A2 = b2.View, A3 = b3.View, A4 = b4.View,
                A5 = b5.View, A6 = b6.View, A7 = b7.View, A8 = b8.View, A9 = b9.View,
                A10 = b10.View, Out = bOut.View
            };

            // Explicit grouped launch with config (1, 1) — gridDim=(1,1,1), groupDim=(1,1,1).
            var k = accelerator.LoadStreamKernel<Tests23_RegisterHeavyBody>(
                Tests23_RegisterHeavyExplicitLaunchKernel);
            k(new KernelConfig(1, 1), body);
            await accelerator.SynchronizeAsync();

            int expected = 2 * 7 + 3 * 11 + 5 * 13 + 7 * 17 + 11 * 19 + 13 * 23
                + 17 * 29 + 19 * 31 + 23 * 37 + 29 * 41 + 31 * 43;
            var got = await bOut.CopyToHostAsync<int>();
            if (got[0] != expected)
                throw new Exception($"RegisterHeavyBody explicit (1,1): expected {expected} got {got[0]}");
        });

        // Test K': Verifies the runtime groupDim.X clamp fires for Index1D=1 dispatches.
        // Reads Group.DimX inside the kernel and writes to output. Pre-fix on CUDA the
        // auto-grouped block size could be 32/64/128/256; post-fix groupDim.X collapses
        // to min(extent.X, customGroupSize) = 1 for unit dispatches.
        static void Tests23_GroupDimXProbeKernel(Index1D _, ArrayView<int> output)
        {
            if (Group.IdxX == 0)
                output[0] = Group.DimX;
        }

        [TestMethod]
        public async Task Tests23_GroupDimX_Clamps_To_Extent_OnUnitDispatch() =>
            await RunEmulatedTest(async accelerator =>
        {
            using var dOut = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(
                Tests23_GroupDimXProbeKernel);
            k((Index1D)1, dOut.View);
            await accelerator.SynchronizeAsync();
            var got = await dOut.CopyToHostAsync<int>();
            // Post-fix: groupDim.X = min(1, customGroupSize) = 1 on every backend that
            // honors the IL clamp. Pre-fix on CUDA / OpenCL: groupDim.X could be
            // 32/64/128/256 from cuOccupancyMaxPotentialBlockSize.
            if (got[0] != 1)
                throw new Exception($"groupDim.X for unit dispatch expected 1 got {got[0]} — "
                    + "EmitLoadKernelConfig clamp not firing.");
        });

        // Test L: Same shape, larger extent. Validates the IL clamp doesn't regress the
        // normal extent>customGroupSize case — gridDim.X must still cover all elements
        // and groupDim.X stays at customGroupSize for warp efficiency.
        static void Tests23_RegisterHeavyBodyParallelKernel(Index1D idx, Tests23_RegisterHeavyBody b)
        {
            // Each thread sums one position across all 11 views. Tests the parallel
            // dispatch path post-clamp (extent > customGroupSize → effective = customGroupSize).
            int i = idx.X;
            int s = 0;
            s += b.A0[i] * 7;
            s += b.A1[i] * 11;
            s += b.A2[i] * 13;
            s += b.A3[i] * 17;
            s += b.A4[i] * 19;
            s += b.A5[i] * 23;
            s += b.A6[i] * 29;
            s += b.A7[i] * 31;
            s += b.A8[i] * 37;
            s += b.A9[i] * 41;
            s += b.A10[i] * 43;
            b.Out[i] = s;
        }

        [TestMethod]
        public async Task Tests23_RegisterHeavyBody_LargeExtent_Parallel() =>
            await RunEmulatedTest(async accelerator =>
        {
            const int N = 256;
            var src = new int[11][];
            int[] muls = { 7, 11, 13, 17, 19, 23, 29, 31, 37, 41, 43 };
            for (int j = 0; j < 11; j++)
            {
                src[j] = new int[N];
                for (int i = 0; i < N; i++) src[j][i] = (i + 1) * (j + 1);
            }
            var bufs = new MemoryBuffer1D<int, Stride1D.Dense>[12];
            try
            {
                for (int j = 0; j < 11; j++)
                {
                    bufs[j] = accelerator.Allocate1D<int>(N);
                    bufs[j].CopyFromCPU(src[j]);
                }
                bufs[11] = accelerator.Allocate1D<int>(N);
                var body = new Tests23_RegisterHeavyBody
                {
                    A0 = bufs[0].View, A1 = bufs[1].View, A2 = bufs[2].View, A3 = bufs[3].View,
                    A4 = bufs[4].View, A5 = bufs[5].View, A6 = bufs[6].View, A7 = bufs[7].View,
                    A8 = bufs[8].View, A9 = bufs[9].View, A10 = bufs[10].View,
                    Out = bufs[11].View
                };
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, Tests23_RegisterHeavyBody>(
                    Tests23_RegisterHeavyBodyParallelKernel);
                k((Index1D)N, body);
                await accelerator.SynchronizeAsync();

                var got = await bufs[11].CopyToHostAsync<int>();
                for (int i = 0; i < N; i++)
                {
                    int expected = 0;
                    for (int j = 0; j < 11; j++) expected += src[j][i] * muls[j];
                    if (got[i] != expected)
                        throw new Exception($"RegisterHeavyBody parallel: idx {i} expected {expected} got {got[i]}");
                }
            }
            finally
            {
                for (int j = 0; j < 12; j++) bufs[j]?.Dispose();
            }
        });

        #endregion

        #region Test N — DecodeUint long-form (libopus shape) WGSL compile shape

        // Mirrors SpawnDev.Codecs OpusRangeDecoderGpu.DecodeUint long-form path
        // (ftb > EC_UINT_BITS) with a 10-field state struct passed by ref through
        // 5 nested helper functions, each containing a `while` loop over an
        // ArrayView<byte>. Surfaced 2026-05-04 as a 30s WGSL cold-compile timeout
        // on WebGPU when wgpu's Tint validator processes this kernel shape.
        // Mirrored here so we can profile the WGSL emit and fix codegen at source.
        public struct Tests23_OpusRangeState
        {
            public uint Offs;
            public uint EndOffs;
            public uint EndWindow;
            public int NEndBits;
            public int NBitsTotal;
            public uint Rng;
            public uint Val;
            public uint Ext;
            public int Rem;
            public int Error;
        }

        const int Tests23_EC_SYM_BITS = 8;
        const uint Tests23_EC_SYM_MAX = (1u << 8) - 1u;
        const uint Tests23_EC_CODE_TOP = 1u << 31;
        const uint Tests23_EC_CODE_BOT = (1u << 31) >> 8;
        const int Tests23_EC_CODE_EXTRA = 7;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Tests23_ReadByte(
            ref Tests23_OpusRangeState state,
            ArrayView<byte> buf, int bufStart, uint storage)
        {
            if (state.Offs < storage)
            {
                int b = buf[bufStart + (int)state.Offs];
                state.Offs++;
                return b;
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Tests23_ReadByteFromEnd(
            ref Tests23_OpusRangeState state,
            ArrayView<byte> buf, int bufStart, uint storage)
        {
            if (state.EndOffs < storage)
            {
                state.EndOffs++;
                int b = buf[bufStart + (int)(storage - state.EndOffs)];
                return b;
            }
            return 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Tests23_Normalize(
            ref Tests23_OpusRangeState state,
            ArrayView<byte> buf, int bufStart, uint storage)
        {
            while (state.Rng <= Tests23_EC_CODE_BOT)
            {
                state.NBitsTotal += Tests23_EC_SYM_BITS;
                state.Rng <<= Tests23_EC_SYM_BITS;
                int sym = state.Rem;
                state.Rem = Tests23_ReadByte(ref state, buf, bufStart, storage);
                sym = (sym << Tests23_EC_SYM_BITS | state.Rem) >> (Tests23_EC_SYM_BITS - Tests23_EC_CODE_EXTRA);
                state.Val = ((state.Val << Tests23_EC_SYM_BITS) + (Tests23_EC_SYM_MAX & (uint)~sym)) & (Tests23_EC_CODE_TOP - 1u);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static uint Tests23_DecodeBits(
            ref Tests23_OpusRangeState state,
            ArrayView<byte> buf, int bufStart, uint storage,
            int bits)
        {
            uint window = state.EndWindow;
            int available = state.NEndBits;
            if (available < bits)
            {
                do
                {
                    window |= (uint)Tests23_ReadByteFromEnd(ref state, buf, bufStart, storage) << available;
                    available += Tests23_EC_SYM_BITS;
                }
                while (available <= 32 - Tests23_EC_SYM_BITS);
            }
            uint ret = window & ((1u << bits) - 1u);
            window >>= bits;
            available -= bits;
            state.EndWindow = window;
            state.NEndBits = available;
            state.NBitsTotal += bits;
            return ret;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Tests23_EcIlog(uint v)
        {
            int n = 0;
            while (v != 0u) { v >>= 1; n++; }
            return n;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static uint Tests23_DecodeUint(
            ref Tests23_OpusRangeState state,
            ArrayView<byte> buf, int bufStart, uint storage,
            uint ft)
        {
            const int EC_UINT_BITS = 8;
            if (ft <= 1u) { state.Error = 1; return 0; }
            uint decoded = ft - 1u;
            int ftb = Tests23_EcIlog(decoded);
            if (ftb > EC_UINT_BITS)
            {
                ftb -= EC_UINT_BITS;
                uint scaledFt = (decoded >> ftb) + 1u;
                state.Ext = state.Rng / scaledFt;
                uint sd = state.Val / state.Ext;
                uint s = scaledFt - (sd + 1u < scaledFt ? sd + 1u : scaledFt);
                uint upd = state.Ext * (scaledFt - (s + 1u));
                state.Val -= upd;
                state.Rng = s > 0u ? state.Ext * ((s + 1u) - s) : state.Rng - upd;
                Tests23_Normalize(ref state, buf, bufStart, storage);
                uint t = (s << ftb) | Tests23_DecodeBits(ref state, buf, bufStart, storage, ftb);
                if (t <= decoded) return t;
                state.Error = 1;
                return decoded;
            }
            else
            {
                state.Ext = state.Rng / ft;
                uint sd = state.Val / state.Ext;
                uint s = ft - (sd + 1u < ft ? sd + 1u : ft);
                uint upd = state.Ext * (ft - (s + 1u));
                state.Val -= upd;
                state.Rng = s > 0u ? state.Ext * ((s + 1u) - s) : state.Rng - upd;
                Tests23_Normalize(ref state, buf, bufStart, storage);
                return s;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static Tests23_OpusRangeState Tests23_OpusInit(
            ArrayView<byte> buf, int bufStart, uint storage)
        {
            var state = new Tests23_OpusRangeState
            {
                Offs = 0,
                EndOffs = 0,
                EndWindow = 0,
                NEndBits = 0,
                NBitsTotal = 9,
                Rem = -1,
                Rng = 0,
                Val = 0,
                Ext = 0,
                Error = 0,
            };
            state.Rem = Tests23_ReadByte(ref state, buf, bufStart, storage);
            state.Rng = 1u << (Tests23_EC_SYM_BITS - Tests23_EC_CODE_EXTRA);
            state.Val = state.Rng - 1u - (uint)(state.Rem >> (Tests23_EC_SYM_BITS - Tests23_EC_CODE_EXTRA));
            state.Rng <<= Tests23_EC_SYM_BITS;
            return state;
        }

        static void Tests23_DecodeUintLongFormKernel(
            Index1D _,
            ArrayView<byte> packet, int packetStart, int packetStorage,
            ArrayView<uint> ftPerSymbol,
            ArrayView<uint> decodedOut, int symbolCount)
        {
            var state = Tests23_OpusInit(packet, packetStart, (uint)packetStorage);
            for (int i = 0; i < symbolCount; i++)
            {
                decodedOut[i] = Tests23_DecodeUint(
                    ref state, packet, packetStart, (uint)packetStorage,
                    ftPerSymbol[i]);
            }
        }

        // CODEGEN-shape regression: kernel must compile + run on every backend
        // in well under the PMT 30s/test budget. We don't assert bit-exactness
        // here — Codecs OpusRangeDecoderGpu_DecodeUint_*_BitExactVsCpu owns
        // bit-correctness; this test owns "the WGSL Tint validator can compile
        // this kernel shape inside the PMT 30s/test budget."
        //
        // CURRENTLY FAILING on WebGPU + WebGPUNoSubgroups (cold-compile > 30s,
        // PASS on CPU/CUDA/OpenCL/WebGL/Wasm). Root cause: ILGPU IR Inliner
        // (Aggressive default) inlines every helper method into the kernel
        // entry, producing a ~600-line WGSL fn with 217 var declarations + 5
        // nested loops + heavy bitcast<u32>/<i32> sign-toggle pairs. wgpu's
        // Tint validator cold-compile of this shape exceeds 30s.
        //
        // The fix path is the rc.16 fn-def codegen Bug D
        // (Plans/rc16-fn-def-codegen-harden.md): ArrayView-as-fn-param
        // marshaling. With NoInlining attrs on each helper, the WGSL emit
        // collapses to ~60 lines per fn (11 fns, fast Tint validation), but
        // ArrayView fn-params currently emit as `i32` instead of
        // `ptr<storage, array<atomic<u32>>, read_write>` and helper-to-helper
        // calls show "Unmapped fallback" — bug D in the rc.16 plan.
        [TestMethod]
        public async Task Tests23_DecodeUint_LongForm_CompileSmoke() => await RunEmulatedTest(async accelerator =>
        {
            // 16-byte synthetic packet with arbitrary bytes — the kernel doesn't
            // need bit-correct input, just exercise the long-form code path.
            var packet = new byte[]
            {
                0x80, 0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE,
                0xF0, 0x0F, 0x55, 0xAA, 0x33, 0xCC, 0x77, 0x88,
            };
            // ft values > 256 force the long-form path (ftb > EC_UINT_BITS = 8).
            var fts = new uint[] { 1024u, 65536u, 100000u, 1u << 24, 0xFFFFFFFFu };
            using var dPacket = accelerator.Allocate1D(packet);
            using var dFt = accelerator.Allocate1D(fts);
            using var dOut = accelerator.Allocate1D<uint>(fts.Length);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var k = accelerator.LoadAutoGroupedStreamKernel<
                Index1D,
                ArrayView<byte>, int, int,
                ArrayView<uint>,
                ArrayView<uint>, int>(Tests23_DecodeUintLongFormKernel);
            k((Index1D)1, dPacket.View, 0, packet.Length, dFt.View, dOut.View, fts.Length);
            await accelerator.SynchronizeAsync();
            sw.Stop();

            // Sanity: kernel compiled + ran (any crash inside compile / dispatch
            // would have thrown before reaching here). We don't gate on a wall
            // time threshold inside the test (that varies per machine) — but
            // the PMT 30s/test budget is the real gate; any regression that
            // pushes this kernel past the budget breaks PMT and surfaces the
            // codegen perf issue immediately.
            await dOut.CopyToHostAsync<uint>();
        });

        // Tuvok's AV1 walker `EncodeFrameKernel` reads `constsUshort[cdfBase + N]` where
        // `cdfBase` is `long` (so the offset arithmetic widens to i64). On WebGPU this
        // surfaces as `var v_X : i32 = v_Y;` where v_Y is `vec2<u32>` (emu_i64) — i64 →
        // i32 implicit truncation rejected by Tint. The fix is to ensure i64 IR values
        // feeding into a non-LEA i32 sink get wrapped with `i64_to_i32(...)`.
        // Repro shape mirrors Tuvok's helper without sub-word coalesce dependency.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Tests23_LongOffsetReadHelper(
            ArrayView<ushort> consts,
            int ctx)
        {
            // i64 widening: int * int + int → int, but then stored to long.
            long cdfBase = ctx * 11 + 7;
            // Indexer takes `long`. The IR carries cdfBase + N as i64.
            ushort c0 = consts[cdfBase + 0];
            ushort c1 = consts[cdfBase + 1];
            ushort c2 = consts[cdfBase + 2];
            return (int)c0 + (int)c1 + (int)c2;
        }

        static void Tests23_LongOffsetReadKernel(
            Index1D _,
            ArrayView<ushort> consts,
            int ctx,
            ArrayView<int> output)
        {
            output[0] = Tests23_LongOffsetReadHelper(consts, ctx);
        }

        [TestMethod]
        public async Task Tests23_LongOffset_OnSubWordView_NoCodegenError() => await RunEmulatedTest(async accelerator =>
        {
            var data = new ushort[64];
            for (int i = 0; i < data.Length; i++) data[i] = (ushort)(i * 5);
            using var consts = accelerator.Allocate1D(data);
            using var output = accelerator.Allocate1D<int>(1);
            int ctx = 1; // cdfBase = 11 + 7 = 18, reads consts[18..20]
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ushort>, int, ArrayView<int>>(Tests23_LongOffsetReadKernel);
            k((Index1D)1, consts.View, ctx, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            // consts[18] + consts[19] + consts[20] = 90 + 95 + 100 = 285
            int expected = data[18] + data[19] + data[20];
            if (got[0] != expected)
                throw new System.Exception($"LongOffset sub-word: expected {expected}, got {got[0]}");
        });

        // Tuvok's local.7 PMT exposed `vec2<u32> >> u32` Tint error in helper
        // arithmetic. Mirrors the shape via a NoInlining helper that does
        // `long bits = ...; long shifted = bits >> n;`. Without the i64-aware
        // shift codegen, helper emits raw `>>` on vec2<u32> + u32 — Tint rejects.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static long Tests23_I64ShiftHelper(long input, int amount)
        {
            return input >> amount;
        }

        static void Tests23_I64ShiftKernel(
            Index1D _,
            ArrayView<int> output)
        {
            long input = 0x1234567890ABCDEFL;
            long shifted = Tests23_I64ShiftHelper(input, 8);
            output[0] = (int)shifted;
        }

        [TestMethod]
        public async Task Tests23_I64Shift_InHelper_NoCodegenError() => await RunEmulatedTest(async accelerator =>
        {
            using var output = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(Tests23_I64ShiftKernel);
            k((Index1D)1, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            // 0x1234567890ABCDEF >> 8 = 0x001234567890ABCD; (int)low = 0x7890ABCD
            int expected = unchecked((int)(0x1234567890ABCDEFL >> 8));
            if (got[0] != expected)
                throw new System.Exception($"I64Shift in helper: expected {expected:X}, got {got[0]:X}");
        });

        // Tuvok's local.12 PMT exposed a fn-def call-site arg type mismatch: helper
        // takes ArrayView<long> param, kernel binding for emu-i64 ArrayView is
        // `array<u32>` (raw bits, see WGSLKernelFunctionGenerator GenerateHeader
        // line 1899-1903), but pre-fix helper signature was `array<emu_i64>` —
        // Tint rejected the call site as "expected ptr<array<vec2<u32>>>, got
        // ptr<array<u32>>". Closes 2026-05-05 walker WGSL L44452 family.
        // Repro shape: helper with [NoInlining] taking ArrayView<long> + an int
        // index, doing read + write on the i64 view. Minimal pipeline mirrors
        // Tuvok's `EncodeFrameBody` shape.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Tests23_I64ArrayViewHelper(
            ArrayView<long> data,
            int idx)
        {
            long val = data[idx];
            data[idx] = val + 0x100000000L; // add 1 << 32 to test hi-word write
        }

        static void Tests23_I64ArrayViewKernel(
            Index1D _,
            ArrayView<long> data)
        {
            Tests23_I64ArrayViewHelper(data, 0);
        }

        [TestMethod]
        public async Task Tests23_I64ArrayView_InHelper_NoCodegenError() => await RunEmulatedTest(async accelerator =>
        {
            using var data = accelerator.Allocate1D<long>(1);
            data.View.CopyFromCPU(new long[] { 0x123456789L });
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<long>>(Tests23_I64ArrayViewKernel);
            k((Index1D)1, data.View);
            await accelerator.SynchronizeAsync();
            var got = await data.CopyToHostAsync<long>();
            long expected = 0x123456789L + 0x100000000L;
            if (got[0] != expected)
                throw new System.Exception($"I64 ArrayView in helper: expected {expected:X}, got {got[0]:X}");
        });

        // Tuvok's local.13 walker WGSL L44455 — reproduces the "invalid aliased
        // pointer argument" Tint error. Sub-word coalesce v2 (local.6) merges
        // 10+ same-element-size sub-word ArrayView kernel params into 1 shared
        // `array<atomic<u32>>` binding. When those params are then passed to a
        // [NoInlining] helper as positional ptr args, all the coalesced ones
        // resolve to the SAME leader binding root — Tint rejects the call site.
        // Pre-fix on WebGPU only when the kernel exceeds the 10-binding cap and
        // sub-word v2 fires; below threshold, no coalesce → no aliasing.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Tests23_CoalesceAliasingHelper(
            ArrayView<byte> a0, ArrayView<byte> a1, ArrayView<byte> a2,
            ArrayView<byte> a3, ArrayView<byte> a4, ArrayView<byte> a5,
            ArrayView<byte> a6, ArrayView<byte> a7, ArrayView<byte> a8,
            ArrayView<byte> a9,
            ArrayView<int> output)
        {
            int sum = 0;
            sum += a0[0]; sum += a1[0]; sum += a2[0]; sum += a3[0]; sum += a4[0];
            sum += a5[0]; sum += a6[0]; sum += a7[0]; sum += a8[0]; sum += a9[0];
            output[0] = sum;
        }

        static void Tests23_CoalesceAliasingKernel(
            Index1D _,
            ArrayView<byte> a0, ArrayView<byte> a1, ArrayView<byte> a2,
            ArrayView<byte> a3, ArrayView<byte> a4, ArrayView<byte> a5,
            ArrayView<byte> a6, ArrayView<byte> a7, ArrayView<byte> a8,
            ArrayView<byte> a9,
            ArrayView<int> output)
        {
            Tests23_CoalesceAliasingHelper(a0, a1, a2, a3, a4, a5, a6, a7, a8, a9, output);
        }

        [TestMethod]
        public async Task Tests23_SubWordCoalesceAliasingInHelper_NoCodegenError() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 10;
            var bufs = new MemoryBuffer1D<byte, Stride1D.Dense>[N];
            try
            {
                for (int i = 0; i < N; i++)
                {
                    bufs[i] = accelerator.Allocate1D<byte>(1);
                    bufs[i].View.CopyFromCPU(new byte[] { (byte)(i + 1) });
                }
                using var output = accelerator.Allocate1D<int>(1);
                var k = accelerator.LoadAutoGroupedStreamKernel<
                    Index1D,
                    ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>,
                    ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>,
                    ArrayView<int>>(Tests23_CoalesceAliasingKernel);
                k((Index1D)1,
                    bufs[0].View, bufs[1].View, bufs[2].View, bufs[3].View, bufs[4].View,
                    bufs[5].View, bufs[6].View, bufs[7].View, bufs[8].View, bufs[9].View,
                    output.View);
                await accelerator.SynchronizeAsync();
                var got = await output.CopyToHostAsync<int>();
                int expected = 1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10; // = 55
                if (got[0] != expected)
                    throw new System.Exception($"Coalesce aliasing helper: expected {expected}, got {got[0]}");
            }
            finally
            {
                for (int i = 0; i < N; i++) bufs[i]?.Dispose();
            }
        });

        #endregion

        #region Cumulative IL Inliner budget — recursive helper fan-out

        // Regression for the IR Inliner cumulative-IL budget added in
        // local.17 (`CumulativeInlinedILBudget = 16384`). Mimics the
        // shape that motivated the fix — a 4-deep recursive partition
        // tree where each individual helper sits comfortably under
        // `MaxNumILInstructionsAggressiveCap` (1024) but the transitive
        // call graph would inline tens of thousands of IL instructions
        // into the kernel root if not capped.
        //
        // Pre-fix (rc.28 with per-function cap only): 4^4 = 256 leaf
        // call sites × ~50-80 IL each ≈ 13K-20K IL inlined. Combined
        // with intermediate-level call inlining, the kernel grew large
        // enough to choke V8's 50K Wasm-locals cap on big real-world
        // codecs (Tuvok's VP9 EncodeFrameKernel). Synthetic test stays
        // well clear of the V8 cap so it compiles even pre-fix, but
        // exercises the budget logic so any regression that disables
        // or breaks the budget surfaces here on a clean failure mode.
        //
        // Post-fix (local.17): the budget kicks in mid-tree and the
        // remaining helpers stay as fn-defs. Both inlining patterns
        // must produce the same output as the CPU reference path.

        private static int Tests23_DeepTreeLeaf(int x, int y)
        {
            // ~50-80 IL of arithmetic. NOT NoInlining — explicitly
            // letting the inliner work so the budget logic gets exercised.
            int a = x ^ y;
            int b = x + y;
            int c = a * b;
            int d = c - (a >> 2);
            int e = d + (b << 1);
            int f = e ^ 0x5A5A5A5A;
            int g = f * 17 + 31;
            int h = g - (e >> 1);
            int i = h ^ (a << 1);
            int j = i + (b >> 3);
            return j;
        }

        private static int Tests23_DeepTreeLevel2(int seed)
        {
            int s0 = Tests23_DeepTreeLeaf(seed, seed + 1);
            int s1 = Tests23_DeepTreeLeaf(seed + 2, seed + 3);
            int s2 = Tests23_DeepTreeLeaf(seed + 4, seed + 5);
            int s3 = Tests23_DeepTreeLeaf(seed + 6, seed + 7);
            return s0 + s1 + s2 + s3;
        }

        private static int Tests23_DeepTreeLevel1(int seed)
        {
            int s0 = Tests23_DeepTreeLevel2(seed);
            int s1 = Tests23_DeepTreeLevel2(seed + 100);
            int s2 = Tests23_DeepTreeLevel2(seed + 200);
            int s3 = Tests23_DeepTreeLevel2(seed + 300);
            return s0 + s1 + s2 + s3;
        }

        private static int Tests23_DeepTreeLevel0(int seed)
        {
            int s0 = Tests23_DeepTreeLevel1(seed);
            int s1 = Tests23_DeepTreeLevel1(seed + 10000);
            int s2 = Tests23_DeepTreeLevel1(seed + 20000);
            int s3 = Tests23_DeepTreeLevel1(seed + 30000);
            return s0 + s1 + s2 + s3;
        }

        private static void Tests23_DeepInlineTreeKernel(Index1D _, ArrayView<int> output)
        {
            int s0 = Tests23_DeepTreeLevel0(1);
            int s1 = Tests23_DeepTreeLevel0(2);
            int s2 = Tests23_DeepTreeLevel0(3);
            int s3 = Tests23_DeepTreeLevel0(4);
            output[0] = s0 + s1 + s2 + s3;
        }

        [TestMethod]
        public async Task Tests23_DeepInlineTree_BudgetDoesNotBreakCorrectness() => await RunEmulatedTest(async accelerator =>
        {
            using var output = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<int>>(Tests23_DeepInlineTreeKernel);
            k((Index1D)1, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();

            // Compute the same thing on the CPU. Must match bit-for-bit
            // regardless of whether the kernel inlined the whole tree
            // or auto-extracted helpers as fn-defs.
            int e0 = Tests23_DeepTreeLevel0(1);
            int e1 = Tests23_DeepTreeLevel0(2);
            int e2 = Tests23_DeepTreeLevel0(3);
            int e3 = Tests23_DeepTreeLevel0(4);
            int expected = e0 + e1 + e2 + e3;

            if (got[0] != expected)
                throw new System.Exception(
                    $"DeepInlineTree budget regression: expected {expected}, got {got[0]}. " +
                    $"Cumulative-IL Inliner budget may be producing wrong fragmentation or " +
                    $"helper-side codegen lost the result.");
        });

        #endregion
    }
}
