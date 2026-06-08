using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Isolates the suspected WebGL bug behind RadixSort<uint>: extracting a HIGH bit of a uint via a
    // RUNTIME shift amount. AlgorithmRadixSortPairsUInt sorted by the low byte only (keys 256..1 ->
    // 256 to [0]), implying (uint >> shift) & 1 returns 0 for shift>=8. This test checks that directly
    // against the CPU oracle so we know whether the bug is the uint dynamic shift codegen or elsewhere.
    public abstract partial class BackendTestBase
    {
        // out[i] = (int)(in[i] >> shift) & 1  — the exact shape of ExtractRadixBits<uint>.
        static void UintDynamicShiftBitKernel(
            Index1D index, ArrayView<uint> input, ArrayView<int> output, int shift)
        {
            output[index.X] = (int)(input[index.X] >> shift) & 1;
        }

        [TestMethod]
        public async Task UintDynamicShiftHighBitTest() => await RunTest(async accelerator =>
        {
            var input = new uint[] { 256u, 255u, 1u, 0x100u, 0xFF00u, 0x80000000u, 0x01000000u };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kern = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<uint>, ArrayView<int>, int>(UintDynamicShiftBitKernel);

            // Probe several shift amounts; CPU does the same C# math as the oracle.
            foreach (int shift in new[] { 0, 4, 8, 12, 16, 24, 31 })
            {
                kern((Index1D)input.Length, inBuf.View, outBuf.View, shift);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync<int>();
                for (int i = 0; i < input.Length; i++)
                {
                    int expected = (int)(input[i] >> shift) & 1;
                    if (got[i] != expected)
                        throw new Exception(
                            $"uint dynamic-shift wrong: (0x{input[i]:X} >> {shift}) & 1 expected {expected} got {got[i]}");
                }
            }
        });

        // out[i] = AscendingUInt32.ExtractRadixBits(in[i], shift, 1) — the EXACT generic op call the
        // radix extract kernel makes (vs the direct expression above), in case the generic op inlines
        // differently on WebGL.
        static void UintExtractRadixBitsKernel(
            Index1D index, ArrayView<uint> input, ArrayView<int> output, int shift)
        {
            AscendingUInt32 op = default;
            output[index.X] = op.ExtractRadixBits(input[index.X], shift, 1);
        }

        [TestMethod]
        public async Task UintExtractRadixBitsHighBitTest() => await RunTest(async accelerator =>
        {
            var input = new uint[] { 256u, 255u, 1u, 0x10000u, 0x80000000u };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kern = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<uint>, ArrayView<int>, int>(UintExtractRadixBitsKernel);
            AscendingUInt32 op = default;
            foreach (int shift in new[] { 0, 8, 16, 24, 31 })
            {
                kern((Index1D)input.Length, inBuf.View, outBuf.View, shift);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync<int>();
                for (int i = 0; i < input.Length; i++)
                {
                    int expected = op.ExtractRadixBits(input[i], shift, 1);
                    if (got[i] != expected)
                        throw new Exception(
                            $"ExtractRadixBits<uint>(0x{input[i]:X}, {shift}, 1) expected {expected} got {got[i]}");
                }
            }
        });

        // Does ExtractRadixBits<long> (AscendingInt64) transpile + work on WebGL's emulated i64? This
        // is the make-or-break for 64-bit RadixSort on WebGL (task #10). out[i] = bit `shift` of key[i].
        static void Int64ExtractRadixBitsKernel(
            Index1D index, ArrayView<long> input, ArrayView<int> output, int shift)
        {
            AscendingInt64 op = default;
            output[index.X] = op.ExtractRadixBits(input[index.X], shift, 1);
        }

        [TestMethod]
        public async Task Int64ExtractRadixBitsTest() => await RunTest(async accelerator =>
        {
            var input = new long[] { 256L, 1L, -1L, 0x1_0000_0000L, long.MinValue, long.MaxValue, 0L };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kern = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<long>, ArrayView<int>, int>(Int64ExtractRadixBitsKernel);
            AscendingInt64 op = default;
            foreach (int shift in new[] { 0, 8, 31, 32, 40, 63 })
            {
                kern((Index1D)input.Length, inBuf.View, outBuf.View, shift);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync<int>();
                for (int i = 0; i < input.Length; i++)
                {
                    int expected = op.ExtractRadixBits(input[i], shift, 1);
                    if (got[i] != expected)
                        throw new Exception(
                            $"ExtractRadixBits<long>(0x{input[i]:X}, {shift}, 1) expected {expected} got {got[i]}");
                }
            }
        });

        // DIAGNOSTIC 1: isolate FloatAsInt(Half) - does it return the 16-bit f16 RawValue (NOT the 32-bit
        // f32 bit pattern of the widened value)? Splits the ExtractRadixBits<Half> failure into its parts.
        static void HalfRawBitsKernel(
            Index1D index, ArrayView<global::ILGPU.Half> input, ArrayView<int> output)
        {
            output[index.X] = (int)(uint)Interop.FloatAsInt(input[index.X]);
        }

        [TestMethod]
        public async Task HalfRawBitsTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            var input = new global::ILGPU.Half[]
            {
                (global::ILGPU.Half)1.0f, (global::ILGPU.Half)(-1.0f),
                (global::ILGPU.Half)256.0f, (global::ILGPU.Half)(-256.0f),
            };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kern = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<global::ILGPU.Half>, ArrayView<int>>(HalfRawBitsKernel);
            kern((Index1D)input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < input.Length; i++)
            {
                int expected = (int)(uint)Interop.FloatAsInt(input[i]);
                if (got[i] != expected)
                    throw new Exception(
                        $"FloatAsInt(Half {(float)input[i]}) expected 0x{expected:X4} got 0x{got[i]:X4}");
            }
        });

        // DIAGNOSTIC 2: isolate the sub-word sign-extension the ones-complement mask depends on:
        // ((uint)((short)ushortBits >> 15)) & 0xFFFF - must be 0xFFFF when bit 15 is set, else 0. No Half
        // involved. If this fails on a backend, the radix bug is the (short) sign-extend, not FloatAsInt.
        static void ShortSignExtendKernel(
            Index1D index, ArrayView<ushort> input, ArrayView<int> output)
        {
            output[index.X] = (int)(((uint)((short)input[index.X] >> 15)) & 0xFFFFu);
        }

        [TestMethod]
        public async Task HalfSignExtendShiftTest() => await RunTest(async accelerator =>
        {
            var input = new ushort[] { 0x0001, 0x7FFF, 0x8000, 0xBC00, 0xFFFF, 0x3C00 };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kern = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<ushort>, ArrayView<int>>(ShortSignExtendKernel);
            kern((Index1D)input.Length, inBuf.View, outBuf.View);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int i = 0; i < input.Length; i++)
            {
                int expected = (int)(((uint)((short)input[i] >> 15)) & 0xFFFFu);
                if (got[i] != expected)
                    throw new Exception(
                        $"(short)0x{input[i]:X4} >> 15 sign-extend expected 0x{expected:X} got 0x{got[i]:X}");
            }
        });

        // Does ExtractRadixBits<Half> (AscendingHalf) transpile + work on WebGL's emulated f16? This is
        // the make-or-break for Half RadixSort on WebGL (task #13). Half is emulated as a GLSL `float`,
        // so FloatAsInt(Half) must compress back to the 16-bit f16 pattern (_f32_to_f16) instead of
        // taking floatBitsToInt of the widened f32. Includes NEGATIVE Halves to exercise the sign-bit
        // flip + ones-complement-mask path (the (short)bits >> 15 sign extension). CPU op is the oracle.
        static void HalfExtractRadixBitsKernel(
            Index1D index, ArrayView<global::ILGPU.Half> input, ArrayView<int> output, int shift)
        {
            AscendingHalf op = default;
            output[index.X] = op.ExtractRadixBits(input[index.X], shift, 1);
        }

        [TestMethod]
        public async Task HalfExtractRadixBitsTest() => await RunTest(async accelerator =>
        {
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");
            var input = new global::ILGPU.Half[]
            {
                (global::ILGPU.Half)1.0f, (global::ILGPU.Half)256.0f, (global::ILGPU.Half)0.5f,
                global::ILGPU.Half.Zero, (global::ILGPU.Half)(-1.0f), (global::ILGPU.Half)(-256.0f),
                (global::ILGPU.Half)(-0.5f), (global::ILGPU.Half)65504.0f, // 65504 = Half.MaxValue
            };
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D<int>(input.Length);
            var kern = accelerator.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<global::ILGPU.Half>, ArrayView<int>, int>(HalfExtractRadixBitsKernel);
            AscendingHalf op = default;
            foreach (int shift in new[] { 0, 4, 8, 10, 12, 15 })
            {
                kern((Index1D)input.Length, inBuf.View, outBuf.View, shift);
                await accelerator.SynchronizeAsync();
                var got = await outBuf.CopyToHostAsync<int>();
                for (int i = 0; i < input.Length; i++)
                {
                    int expected = op.ExtractRadixBits(input[i], shift, 1);
                    if (got[i] != expected)
                        throw new Exception(
                            $"ExtractRadixBits<Half>({(float)input[i]}, {shift}, 1) expected {expected} got {got[i]}");
                }
            }
        });

        // Full uint KEYS-ONLY radix sort. Isolates the uint keys sort from the pairs value-handling.
        [TestMethod]
        public async Task UintKeysOnlyRadixSortTest() => await RunTest(async accelerator =>
        {
            const int n = 256;
            var keys = new uint[n];
            for (int i = 0; i < n; i++) keys[i] = (uint)(n - i); // 256,255,...,1
            using var keysBuf = accelerator.Allocate1D(keys);
            var tempSize = accelerator.ComputeRadixSortTempStorageSize<uint, AscendingUInt32>(n);
            using var tempBuf = accelerator.Allocate1D<int>(tempSize);
            var radixSort = accelerator.CreateRadixSort<uint, Stride1D.Dense, AscendingUInt32>();
            radixSort(accelerator.DefaultStream, keysBuf.View, tempBuf.View);
            await accelerator.SynchronizeAsync();
            var sorted = await keysBuf.CopyToHostAsync<uint>();
            for (int i = 0; i < n; i++)
                if (sorted[i] != (uint)(i + 1))
                    throw new Exception($"uint keys-only radix wrong at [{i}]: expected {i + 1} got {sorted[i]}");
        });
    }
}
