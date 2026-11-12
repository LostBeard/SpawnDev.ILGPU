using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 64-bit integer arithmetic whose LOW WORD is all that is read: a view index, a sub-view offset, an (int) / (short) /
    // (byte) cast. WebGPU emulates 64-bit integers (a library function per operation, which the D3D shader compiler
    // inlines): SpawnDev.Codecs' VP9 encode kernel carried 1,456 i64_add and 422 i64_mul calls for
    // `view[(long)i * stride + offset]` index arithmetic, about 47,000 of the 75,000 lines D3D compiled (2026-11-12).
    // NarrowLowWordArithmetic computes such arithmetic in i32. These kernels use intermediates far outside 32 bits
    // (they cancel, or only their low word is kept), so a narrowing that dropped a carry, or narrowed a value whose
    // high word IS read (a right shift, a comparison), gives a different result than the host. Every backend runs them;
    // the WebGPU lane also checks that the index arithmetic left the shader, and that the switch brings it back.
    public abstract partial class BackendTestBase
    {
        // one store per thread at its own slot (WebGL's contract); every index stays inside src
        static void LowWordIndexKernel(Index1D i, ArrayView<int> src, ArrayView<int> dst, long stride, long bias, long big)
        {
            long li = i;
            // products and sums beyond 32 bits that cancel: i * stride + bias
            long idx0 = li * (big + stride) - li * big + bias;
            // a constant shift under 32: 4 * i + bias
            long idx1 = ((li + big) << 2) - (big << 2) + bias;
            // xor / and / or / not / negate: i, then 2 * (i + bias)
            long idx2 = ((li ^ big) ^ big) | (li & ~big & big);
            long idx3 = -(~(li + bias) + 1 - 2 * (li + bias)) - (li + bias);
            int acc = src[idx0] + 3 * src[idx1] + 5 * src[idx2] + 7 * src[idx3];

            // a loop-carried 64-bit index
            long p = bias + big - big;
            for (int k = 0; k < 4; k++)
            {
                acc = acc * 31 + src[p];
                p += stride;
            }

            // a select between 64-bit indices
            long q = (i & 1) != 0 ? li * 2 + bias : li + big - big;
            acc = acc * 31 + src[q];

            // a sub-view whose offset is 64-bit arithmetic: offset i, element 1
            var sub = src.SubView(li * stride - li * (stride - 1), 4);
            acc = acc * 31 + sub[1];
            dst[i] = acc;
        }

        // the low word of 64-bit results that do NOT fit 32 bits, and values whose high word is read as well
        static void LowWordCastKernel(Index1D i, ArrayView<int> dst, long big, long multiplier)
        {
            long li = i;
            int acc = (int)(li * multiplier + big);
            acc = acc * 31 + (short)(li * 40503 + big);
            acc = acc * 31 + (byte)(li * 77 + big);
            acc = acc * 31 + (ushort)(li * 40503 - big);
            acc = acc * 31 + (sbyte)(big - li * 77);
            // shifts: every source bit gone at 32 and beyond, the sign bit position at 31
            acc = acc * 31 + (int)((li + 1) << 40);
            acc = acc * 31 + (int)((li + 1) << 32);
            acc = acc * 31 + (int)((li * big + 1) << 31);
            acc = acc * 31 + (int)((li * big) << 5);
            // unsigned
            ulong u = (ulong)li * 0xFFFFFFFF00000001UL + (ulong)big;
            acc = acc * 31 + (int)(uint)u;
            // the same value read in its low word AND in all 64 bits: the 64-bit readers must still see every bit
            long m = li * big + multiplier;
            acc = acc * 31 + (int)m;
            acc = acc * 31 + (int)(m >> 33);
            acc = acc * 31 + (int)((ulong)m >> 37);
            acc = acc * 31 + (int)(m / 1000003);
            if (m > big * 7) acc += 1;
            if ((ulong)m < (ulong)big) acc += 2;
            // 64-bit products of every sign combination, read in their HIGH bits (the emulated multiply is one unsigned
            // multiply: two's complement makes the low 64 bits sign-agnostic)
            long s1 = (li - 100) * big;
            long s2 = s1 * (multiplier - li);
            long s3 = (li - 100) * (li - 50) * big;
            acc = acc * 31 + (int)(s1 >> 29);
            acc = acc * 31 + (int)(s2 >> 32);
            acc = acc * 31 + (int)(s3 >> 31);
            // both operands widened from 32 bits (emitted as a 32 x 32 -> 64 multiply on WebGPU / WebGL): every sign
            // combination, constants (int.MinValue included), unsigned operands above 2^31, read in their high bits
            int ii = i;
            int x = ii * 7919 - 400000;
            int y = 0x7FFFFFF0 - ii * 3;
            long w1 = (long)x * y;
            long w2 = (long)x * 11585L;
            long w3 = (long)y * -15137L;
            long w4 = (long)x * int.MinValue;
            long w5 = (long)(x - 1000) * (long)(100 - ii);
            ulong w6 = (ulong)(uint)(ii * unchecked((int)0x9E3779B1)) * (ulong)(uint)(unchecked((int)0xF0000000) + ii);
            ulong w7 = (ulong)(uint)x * 4294967295UL;
            acc = acc * 31 + (int)(w1 >> 32);
            acc = acc * 31 + (int)(w1 >> 13);
            acc = acc * 31 + (int)(w2 >> 20);
            acc = acc * 31 + (int)(w3 >> 31);
            acc = acc * 31 + (int)(w4 >> 32);
            acc = acc * 31 + (int)(w5 >> 3);
            acc = acc * 31 + (int)(w6 >> 32);
            acc = acc * 31 + (int)(w6 >> 7);
            acc = acc * 31 + (int)(w7 >> 32);
            // a shift by a run-time count can reach 32..63: not a low-word operation
            acc = acc * 31 + (int)((li + big) << (i & 63));
            dst[i] = acc;
        }

        static string LowWordWgsl(string kernelName, bool narrow)
        {
            var method = typeof(BackendTestBase).GetMethod(kernelName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            bool saved = SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.NarrowInt64LowWordArithmetic;
            SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.NarrowInt64LowWordArithmetic = narrow;
            try
            {
                return ShaderCompiler.Generate(method!, CapabilityProfiles.WebGPUBaseline).Source ?? "";
            }
            finally
            {
                SpawnDev.ILGPU.WebGPU.Backend.WebGPUBackend.NarrowInt64LowWordArithmetic = saved;
            }
        }

        /// <summary>Calls of the emulated 64-bit add / subtract / multiply in the kernel body (the library's own bodies excluded).</summary>
        static int LowWordEmulatedArithmeticCalls(string wgsl)
        {
            int count = 0;
            foreach (var rawLine in wgsl.Split('\n'))
            {
                var line = rawLine.TrimStart();
                if (line.StartsWith("fn ", StringComparison.Ordinal) || !line.StartsWith("let ", StringComparison.Ordinal))
                    continue;
                // a body statement: `let v_N : emu_i64 = i64_add(...)`
                if (!line.Contains(": emu_i64 = ", StringComparison.Ordinal))
                    continue;
                if (line.Contains("= i64_add(", StringComparison.Ordinal) || line.Contains("= i64_sub(", StringComparison.Ordinal)
                    || line.Contains("= i64_mul(", StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        [TestMethod]
        public async Task LowWordNarrowing_ViewIndexArithmetic_MatchesHost() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGPU)
            {
                int narrowed = LowWordEmulatedArithmeticCalls(LowWordWgsl(nameof(LowWordIndexKernel), narrow: true));
                int kept = LowWordEmulatedArithmeticCalls(LowWordWgsl(nameof(LowWordIndexKernel), narrow: false));
                // every 64-bit value of this kernel is read in its low word only
                if (kept < 10)
                    throw new Exception($"LowWordNarrowing: the kernel has only {kept} emulated 64-bit add/sub/mul with narrowing OFF; the gate measures nothing");
                if (narrowed != 0)
                    throw new Exception($"LowWordNarrowing: {narrowed} emulated 64-bit add/sub/mul left in the index arithmetic (narrowing off: {kept})");
            }
            const int n = 96;
            const long stride = 3, bias = 5, big = 0x123456789AL;
            var src = new int[4 * n + 64];
            for (int k = 0; k < src.Length; k++) src[k] = k * 2654435 + 17;
            using var dSrc = accelerator.Allocate1D(src);
            using var dDst = accelerator.Allocate1D<int>(n);
            dDst.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, long, long, long>(LowWordIndexKernel);
            kernel((Index1D)n, dSrc.View, dDst.View, stride, bias, big);
            var got = await dDst.CopyToHostAsync<int>();

            // host oracle: the kernel method itself on CPU-accelerator views, one thread at a time
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hSrc = cpu.Allocate1D(src);
            using var hDst = cpu.Allocate1D<int>(n);
            for (int t = 0; t < n; t++) LowWordIndexKernel(t, hSrc.View, hDst.View, stride, bias, big);
            var want = await hDst.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
                if (got[t] != want[t]) throw new Exception($"dst[{t}] = {got[t]}, host {want[t]}");
        });

        [TestMethod]
        public async Task LowWordNarrowing_CastsAndFullWidthReaders_MatchHost() => await RunTest(async accelerator =>
        {
            const int n = 200;
            const long big = 0x123456789AL, multiplier = unchecked((long)0x9E3779B97F4A7C15UL);
            using var dDst = accelerator.Allocate1D<int>(n);
            dDst.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, long, long>(LowWordCastKernel);
            kernel((Index1D)n, dDst.View, big, multiplier);
            var got = await dDst.CopyToHostAsync<int>();

            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hDst = cpu.Allocate1D<int>(n);
            for (int t = 0; t < n; t++) LowWordCastKernel(t, hDst.View, big, multiplier);
            var want = await hDst.CopyToHostAsync<int>();
            for (int t = 0; t < n; t++)
                if (got[t] != want[t]) throw new Exception($"dst[{t}] = {got[t]}, host {want[t]}");
        });
    }
}
