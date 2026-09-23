using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Struct buffers whose fields are not all 4 bytes, read AND written by a kernel. WebGL stores a
    // buffer as its raw bytes, one 32-bit texel per 4 bytes, and used to address a struct buffer as
    // "one texel per field": a 64-bit field (long/ulong/double - two texels) failed to compile on
    // load ("cannot convert from highp int to highp 2-component vector of uint"), reached by
    // Autolykos2.MineKernel reading Element256 from the dataset. The store side assumed tight packing,
    // so alignment padding ({ int; long; } puts the long at byte 8) misplaced every later field, and a
    // SubView's element offset was added to the texel index unscaled.
    public abstract partial class BackendTestBase
    {
        // Managed layout (sequential): A @0, pad, B @8, C @16, D @24, pad -> 32 bytes.
        public struct PaddedMix
        {
            public int A;
            public long B;
            public double C;
            public float D;
        }

        static PaddedMix PaddedMixInput(int i) => new PaddedMix
        {
            A = i * 7 - 300,
            B = ((long)i << 36) - 0x1234_5678_9ABCL * (i & 3),
            C = i * 1.25 - 3.5,
            D = i * 0.5f + 0.25f,
        };

        // Every operation is exact in float, Dekker and Ozaki emulated double, so the check is ==.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static PaddedMix PaddedMixTransform(PaddedMix s, int i) => new PaddedMix
        {
            A = s.A * 3 + 1,
            B = s.B ^ ((long)i << 40) ^ 0x0F0F_0000_0001L,
            C = s.C * 2.0 + 0.5,
            D = s.D - 1.0f,
        };

        [MethodImpl(MethodImplOptions.NoInlining)]
        static PaddedMix PaddedMixTransformHelper(PaddedMix s, int i) => PaddedMixTransform(s, i);

        static void PaddedMixKernel(Index1D i, ArrayView<PaddedMix> src, ArrayView<PaddedMix> dst) =>
            dst[i] = PaddedMixTransform(src[i], i);

        static void PaddedMixHelperKernel(Index1D i, ArrayView<PaddedMix> src, ArrayView<PaddedMix> dst) =>
            dst[i] = PaddedMixTransformHelper(src[i], i);

        static void CheckPaddedMix(PaddedMix[] got, PaddedMix[] input, int srcStart, string what, string backend)
        {
            for (int i = 0; i < got.Length; i++)
            {
                var e = PaddedMixTransform(input[i + srcStart], i);
                var g = got[i];
                if (g.A != e.A || g.B != e.B || g.C != e.C || g.D != e.D)
                    throw new Exception(
                        $"{what} on {backend}, element {i}: got A={g.A} B=0x{g.B:x16} C={g.C:R} D={g.D:R}, " +
                        $"expected A={e.A} B=0x{e.B:x16} C={e.C:R} D={e.D:R}");
            }
        }

        [TestMethod]
        public async Task StructBuffer_PaddedMixedFields_LoadAndStore() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var input = new PaddedMix[n];
            for (int i = 0; i < n; i++) input[i] = PaddedMixInput(i);
            using var src = accelerator.Allocate1D(input);

            // Whole buffer, in the kernel and through a [NoInlining] helper taking the struct by value.
            foreach (var (name, fn) in new (string, Action<Index1D, ArrayView<PaddedMix>, ArrayView<PaddedMix>>)[]
                { ("kernel", PaddedMixKernel), ("NoInlining helper", PaddedMixHelperKernel) })
            {
                using var dst = accelerator.Allocate1D<PaddedMix>(n);
                var k = accelerator.LoadAutoGroupedStreamKernel(fn);
                k(n, src.View, dst.View);
                await accelerator.SynchronizeAsync();
                CheckPaddedMix(await dst.CopyToHostAsync(), input, 0, $"PaddedMix {name}", BackendName);
            }

            // A SubView that starts at element 1: the view's element offset must be scaled to the
            // element's texel count on WebGL, not added to the texel index as is.
            {
                using var dst = accelerator.Allocate1D<PaddedMix>(n - 1);
                var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<PaddedMix>, ArrayView<PaddedMix>>(PaddedMixKernel);
                k(n - 1, src.View.SubView(1, n - 1), dst.View);
                await accelerator.SynchronizeAsync();
                CheckPaddedMix(await dst.CopyToHostAsync(), input, 1, "PaddedMix SubView(1)", BackendName);
            }

            Console.WriteLine($"[StructBuffer] padded mixed-field struct load/store on {BackendName}: kernel, helper, SubView ✓");
        });

        // Sub-word fields sharing 32-bit words with each other, next to a 64-bit field. Managed layout:
        // A @0, B @2, C @4, D @8, E @16 -> 24 bytes. (No bool: a bool field makes a struct non-blittable,
        // which ILGPU rejects for buffers.) WebGPU addresses this struct as u32 words (it has a
        // 64-bit field), WebGL as texels; both must extract and replace only each field's own bits, and a
        // negative sbyte / short must come back sign-extended where the kernel widens it.
        public struct SubWordMix
        {
            public byte A;
            public short B;
            public ushort C;
            public long D;
            public sbyte E;
        }

        // Only sub-word fields: A @0, B @1, C @2, D @4 -> 6 bytes, so elements do not start on 4-byte
        // boundaries and neighbouring elements share 32-bit words (WebGPU stores them atomically).
        public struct SubWordOnly
        {
            public byte A;
            public sbyte B;
            public short C;
            public byte D;
        }

        static SubWordMix SubWordMixInput(int i) => new SubWordMix
        {
            A = (byte)(i * 37),
            B = (short)(i * 91 - 20000),
            C = (ushort)(65535 - i * 5),
            D = ((long)i << 35) - 12345,
            E = (sbyte)(i * 13 - 100),
        };

        static SubWordOnly SubWordOnlyInput(int i) => new SubWordOnly
        {
            A = (byte)(i * 29 + 7),
            B = (sbyte)(i * 11 - 128),
            C = (short)(30000 - i * 17),
            D = (byte)(255 - i),
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static SubWordMix SubWordMixTransform(SubWordMix s, int i) => new SubWordMix
        {
            A = (byte)(s.A + i),
            B = (short)(s.B * 3 - 7),
            C = (ushort)(s.C * 7 + 1),
            D = s.D ^ ((long)i << 33) ^ (s.E < 0 ? 1L : 2L),   // s.E < 0 needs E sign-extended
            E = (sbyte)(s.E - 3),
        };

        static void SubWordMixKernel(Index1D i, ArrayView<SubWordMix> src, ArrayView<SubWordMix> dst) =>
            dst[i] = SubWordMixTransform(src[i], i);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static SubWordOnly SubWordOnlyTransform(SubWordOnly s, int i) => new SubWordOnly
        {
            A = (byte)(s.A ^ i),
            B = (sbyte)(s.B + 1),
            C = (short)(s.C - i),
            D = (byte)(s.D + 3),
        };

        static void SubWordOnlyKernel(Index1D i, ArrayView<SubWordOnly> src, ArrayView<SubWordOnly> dst) =>
            dst[i] = SubWordOnlyTransform(src[i], i);

        // Reads the sub-word-only struct and widens every field, signed ones included, into ints.
        static void SubWordOnlyReadKernel(Index1D i, ArrayView<SubWordOnly> src, ArrayView<int> dst)
        {
            var s = src[i];
            dst[i * 4 + 0] = s.A;
            dst[i * 4 + 1] = s.B;
            dst[i * 4 + 2] = s.C;
            dst[i * 4 + 3] = s.D;
        }

        [TestMethod]
        public async Task StructBuffer_SubWordFields_LoadAndStore() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var input = new SubWordMix[n];
            for (int i = 0; i < n; i++) input[i] = SubWordMixInput(i);
            using var src = accelerator.Allocate1D(input);
            using var dst = accelerator.Allocate1D<SubWordMix>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<SubWordMix>, ArrayView<SubWordMix>>(SubWordMixKernel);
            k(n, src.View, dst.View);
            await accelerator.SynchronizeAsync();
            var got = await dst.CopyToHostAsync();
            for (int i = 0; i < n; i++)
            {
                var e = SubWordMixTransform(input[i], i);
                var g = got[i];
                if (g.A != e.A || g.B != e.B || g.C != e.C || g.D != e.D || g.E != e.E)
                    throw new Exception(
                        $"SubWordMix on {BackendName}, element {i}: got A={g.A} B={g.B} C={g.C} D=0x{g.D:x16} E={g.E}, " +
                        $"expected A={e.A} B={e.B} C={e.C} D=0x{e.D:x16} E={e.E}");
            }

            var only = new SubWordOnly[n];
            for (int i = 0; i < n; i++) only[i] = SubWordOnlyInput(i);
            using var srcOnly = accelerator.Allocate1D(only);
            using var dstOnly = accelerator.Allocate1D<int>(n * 4);
            var k2 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<SubWordOnly>, ArrayView<int>>(SubWordOnlyReadKernel);
            k2(n, srcOnly.View, dstOnly.View);
            await accelerator.SynchronizeAsync();
            var gotOnly = await dstOnly.CopyToHostAsync();
            for (int i = 0; i < n; i++)
            {
                var s = only[i];
                int[] want = { s.A, s.B, s.C, s.D };
                for (int f = 0; f < 4; f++)
                    if (gotOnly[i * 4 + f] != want[f])
                        throw new Exception($"SubWordOnly read on {BackendName}, element {i} field {f}: got {gotOnly[i * 4 + f]}, expected {want[f]}");
            }

            // Sub-word-only struct through a SubView(1) (element offset 6 bytes: not word aligned) into a
            // struct store - every thread writes bytes that share words with its neighbours.
            using var dstOnlyStruct = accelerator.Allocate1D<SubWordOnly>(n - 1);
            var k3 = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<SubWordOnly>, ArrayView<SubWordOnly>>(SubWordOnlyKernel);
            k3(n - 1, srcOnly.View.SubView(1, n - 1), dstOnlyStruct.View);
            await accelerator.SynchronizeAsync();
            var gotOnlyStruct = await dstOnlyStruct.CopyToHostAsync();
            for (int i = 0; i < n - 1; i++)
            {
                var e = SubWordOnlyTransform(only[i + 1], i);
                var g = gotOnlyStruct[i];
                if (g.A != e.A || g.B != e.B || g.C != e.C || g.D != e.D)
                    throw new Exception(
                        $"SubWordOnly SubView(1) store on {BackendName}, element {i}: got A={g.A} B={g.B} C={g.C} D={g.D}, " +
                        $"expected A={e.A} B={e.B} C={e.C} D={e.D}");
            }

            Console.WriteLine($"[StructBuffer] sub-word struct fields on {BackendName}: mixed load/store, sub-word-only load + SubView store ✓");
        });

        // Four 64-bit fields, read from a buffer: exactly Autolykos2's Element256 dataset read.
        static void Element256ReadKernel(Index1D i, ArrayView<Crypto.Element256> src, ArrayView<ulong> dst)
        {
            var e = src[i];
            dst[i * 4 + 0] = e.W0 + 1;
            dst[i * 4 + 1] = e.W1 ^ 0xFFFF_0000_FFFF_0000UL;
            dst[i * 4 + 2] = e.W2 >> 3;
            dst[i * 4 + 3] = e.W3 * 3;
        }

        [TestMethod]
        public async Task StructBuffer_UInt64Fields_Load() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var input = new Crypto.Element256[n];
            for (int i = 0; i < n; i++)
            {
                ulong s = (ulong)i * 0x9E37_79B9_7F4A_7C15UL;
                input[i] = new Crypto.Element256(s, ~s, s ^ 0x8000_0000_0000_0001UL, (ulong)i << 33);
            }
            using var src = accelerator.Allocate1D(input);
            using var dst = accelerator.Allocate1D<ulong>(n * 4);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<Crypto.Element256>, ArrayView<ulong>>(Element256ReadKernel);
            k(n, src.View, dst.View);
            await accelerator.SynchronizeAsync();
            var got = await dst.CopyToHostAsync();
            for (int i = 0; i < n; i++)
            {
                var e = input[i];
                ulong[] want = { e.W0 + 1, e.W1 ^ 0xFFFF_0000_FFFF_0000UL, e.W2 >> 3, e.W3 * 3 };
                for (int w = 0; w < 4; w++)
                    if (got[i * 4 + w] != want[w])
                        throw new Exception($"Element256 read on {BackendName}, element {i} word {w}: got 0x{got[i * 4 + w]:x16}, expected 0x{want[w]:x16}");
            }
            Console.WriteLine($"[StructBuffer] Element256 (4 x ulong) load on {BackendName}: {n} elements ✓");
        });
    }
}
