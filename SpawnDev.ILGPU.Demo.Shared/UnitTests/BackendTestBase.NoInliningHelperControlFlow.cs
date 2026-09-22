using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;
using System.Runtime.CompilerServices;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Control flow INSIDE a [MethodImpl(NoInlining)] helper - a loop and data-dependent branches
    // in a function that is emitted as a standalone shader function rather than inlined into the
    // kernel body. Found while chasing the WebGL Autolykos2 dataset hang: flipping
    // Autolykos2.GenerateDatasetElement to NoInlining produced a WebGL helper with its 63-iteration
    // loop flattened to straight-line code (only 3 of 65 Compress calls survived) and wrong digests
    // on WebGPU, Wasm and WebGL. The loop was never unrolled (LoopUnrolling returned (1, 63) for
    // bodyCost 1266); the helper-function codegen dropped the control flow itself.
    //
    // Shape mirrors GenerateDatasetElement at production scale: 64-bit state, a compile-time
    // 63-iteration loop with extra linear counters, an AggressiveInlining round that makes
    // several calls to a single-block NoInlining mixer (Blake2b.G's role).
    public abstract partial class BackendTestBase
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void NoInlCF_Mix(ref ulong a, ref ulong b, ulong m)
        {
            a = a + b + m;
            b ^= a;
            b = (b >> 24) | (b << 40);
            a += b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void NoInlCF_Round(ref ulong h0, ref ulong h1, ref ulong h2, ref ulong h3, ulong m0, ulong m1)
        {
            NoInlCF_Mix(ref h0, ref h1, m0);
            NoInlCF_Mix(ref h2, ref h3, m1);
            NoInlCF_Mix(ref h0, ref h2, m1);
            NoInlCF_Mix(ref h1, ref h3, m0);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void NoInlCF_LoopHelper(uint seed, out ulong e0, out ulong e1)
        {
            ulong h0 = 0x6A09E667F3BCC908UL ^ seed;
            ulong h1 = 0xBB67AE8584CAA73BUL;
            ulong h2 = 0x3C6EF372FE94F82BUL;
            ulong h3 = 0xA54FF53A5F1D36F1UL;
            ulong t = 128;
            NoInlCF_Round(ref h0, ref h1, ref h2, ref h3, seed, t);

            ulong ctr = 15;
            for (int blk = 1; blk <= 63; blk++)
            {
                t += 128;
                NoInlCF_Round(ref h0, ref h1, ref h2, ref h3, ctr, t);
                ctr += 16;
            }

            t += 8;
            NoInlCF_Round(ref h0, ref h1, ref h2, ref h3, ctr, t);
            e0 = h0 ^ h2;
            e1 = h1 ^ h3;
        }

        static void NoInlCF_LoopKernel(Index1D i, ArrayView<ulong> out0, ArrayView<ulong> out1)
        {
            NoInlCF_LoopHelper((uint)i.X, out ulong e0, out ulong e1);
            out0[i] = e0;
            out1[i] = e1;
        }

        [TestMethod]
        public async Task NoInliningHelper_CompileTimeLoop_64BitState_MatchesCpu() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 256;
            using var out0 = accelerator.Allocate1D<ulong>(N);
            using var out1 = accelerator.Allocate1D<ulong>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<ulong>>(NoInlCF_LoopKernel);
            k(N, out0.View, out1.View);
            await accelerator.SynchronizeAsync();
            var got0 = await out0.CopyToHostAsync<ulong>();
            var got1 = await out1.CopyToHostAsync<ulong>();
            for (int i = 0; i < N; i++)
            {
                NoInlCF_LoopHelper((uint)i, out ulong e0, out ulong e1);
                if (got0[i] != e0 || got1[i] != e1)
                    throw new Exception($"NoInlining helper loop mismatch at {i}: GPU=({got0[i]:X16},{got1[i]:X16}) CPU=({e0:X16},{e1:X16})");
            }
        });

        // Data-dependent branches and a data-dependent loop exit inside a NoInlining helper,
        // returning a value (non-void helper). The if/else bodies call another NoInlining helper so
        // IfConversion cannot collapse them into selects.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int NoInlCF_Step(int r) => (r & 1) == 0 ? r >> 1 : r * 3 + 1;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int NoInlCF_BranchyHelper(int x)
        {
            int r;
            if ((x & 3) == 0)
                r = NoInlCF_Step(x + 5) * 7;
            else if ((x & 3) == 1)
                r = NoInlCF_Step(x) + 11;
            else
                r = x + NoInlCF_Step(x + 1);

            int steps = 0;
            while (r > 1)
            {
                r = NoInlCF_Step(r);
                steps++;
            }
            return steps * 1000 + r;
        }

        static void NoInlCF_BranchyKernel(Index1D i, ArrayView<int> output)
        {
            output[i] = NoInlCF_BranchyHelper(i.X + 1);
        }

        // Loop-carried values that ROTATE: every loop-header phi's back-edge operand is another phi
        // of the same header (a' = b, b' = c, c' = a). Phi copies emitted one after another
        // clobber a value the next copy still has to read - a parallel copy must go through
        // temporaries. The trip count is data-dependent so the loop cannot be unrolled away.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int NoInlCF_Rotate(int x)
        {
            int a = x, b = x * 7 + 3, c = 11;
            int n = (x % 7) + 1;
            for (int k = 0; k < n; k++)
            {
                int t = a;
                a = b;
                b = c;
                c = t;
            }
            return a * 10007 + b * 101 + c;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int NoInlCF_RotateHelper(int x) => NoInlCF_Rotate(x);

        static void NoInlCF_RotateKernel(Index1D i, ArrayView<int> inKernel, ArrayView<int> inHelper)
        {
            inKernel[i] = NoInlCF_Rotate(i.X);
            inHelper[i] = NoInlCF_RotateHelper(i.X);
        }

        [TestMethod]
        public async Task LoopPhiRotation_KernelAndNoInliningHelper_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int N = 1024;
            using var inKernel = accelerator.Allocate1D<int>(N);
            using var inHelper = accelerator.Allocate1D<int>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(NoInlCF_RotateKernel);
            k(N, inKernel.View, inHelper.View);
            await accelerator.SynchronizeAsync();
            var gotK = await inKernel.CopyToHostAsync<int>();
            var gotH = await inHelper.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int expected = NoInlCF_Rotate(i);
                if (gotK[i] != expected || gotH[i] != expected)
                    throw new Exception($"Loop phi rotation mismatch at {i}: kernel={gotK[i]} helper={gotH[i]} CPU={expected}");
            }
        });

        // A do-while whose exit edge leaves from the latch's conditional branch, with code after the
        // loop reading the value the loop-carried variable had at the top of the LAST iteration
        // (`prev`). Only the back edge may update the header phis - updating them on the exit edge
        // too makes `prev` read the next iteration's value.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int NoInlCF_DoWhilePrev(int x)
        {
            int i = x & 15;
            int step = (x % 5) + 1;
            int limit = 40 + (x & 31);
            int prev;
            do
            {
                prev = i;
                i += step;
            }
            while (i < limit);
            return prev * 1000 + i;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int NoInlCF_DoWhilePrevHelper(int x) => NoInlCF_DoWhilePrev(x);

        static void NoInlCF_DoWhilePrevKernel(Index1D i, ArrayView<int> inKernel, ArrayView<int> inHelper)
        {
            inKernel[i] = NoInlCF_DoWhilePrev(i.X);
            inHelper[i] = NoInlCF_DoWhilePrevHelper(i.X);
        }

        [TestMethod]
        public async Task LoopExitReadsHeaderPhi_KernelAndNoInliningHelper_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int N = 1024;
            using var inKernel = accelerator.Allocate1D<int>(N);
            using var inHelper = accelerator.Allocate1D<int>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(NoInlCF_DoWhilePrevKernel);
            k(N, inKernel.View, inHelper.View);
            await accelerator.SynchronizeAsync();
            var gotK = await inKernel.CopyToHostAsync<int>();
            var gotH = await inHelper.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int expected = NoInlCF_DoWhilePrev(i);
                if (gotK[i] != expected || gotH[i] != expected)
                    throw new Exception($"Loop exit header-phi mismatch at {i}: kernel={gotK[i]} helper={gotH[i]} CPU={expected}");
            }
        });

        // uint -> ulong / long must ZERO-extend. On the emulated-64-bit backends a 32-bit value
        // widened with a sign-extending helper (or a scalar constructor that replicates into both
        // words) is only right below 2^31 (or only for 0) - so the inputs straddle the sign bit.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong NoInlCF_Widen(uint x) => ((ulong)x * 3UL) ^ (ulong)(long)x;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static ulong NoInlCF_WidenHelper(uint x) => NoInlCF_Widen(x);

        static void NoInlCF_WidenKernel(Index1D i, ArrayView<uint> input, ArrayView<ulong> inKernel, ArrayView<ulong> inHelper)
        {
            uint x = input[i];
            inKernel[i] = NoInlCF_Widen(x);
            inHelper[i] = NoInlCF_WidenHelper(x);
        }

        [TestMethod]
        public async Task UInt32To64_ZeroExtends_KernelAndNoInliningHelper_MatchesCpu() => await RunEmulatedTest(async accelerator =>
        {
            var data = new uint[] { 0u, 1u, 0x7FFFFFFFu, 0x80000000u, 0x80000001u, 0xDEADBEEFu, 0xFFFFFFFEu, 0xFFFFFFFFu };
            int n = data.Length;
            using var input = accelerator.Allocate1D(data);
            using var inKernel = accelerator.Allocate1D<ulong>(n);
            using var inHelper = accelerator.Allocate1D<ulong>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<ulong>, ArrayView<ulong>>(NoInlCF_WidenKernel);
            k(n, input.View, inKernel.View, inHelper.View);
            await accelerator.SynchronizeAsync();
            var gotK = await inKernel.CopyToHostAsync<ulong>();
            var gotH = await inHelper.CopyToHostAsync<ulong>();
            for (int i = 0; i < n; i++)
            {
                ulong expected = NoInlCF_Widen(data[i]);
                if (gotK[i] != expected || gotH[i] != expected)
                    throw new Exception($"uint->64 widening mismatch for 0x{data[i]:X8}: kernel={gotK[i]:X16} helper={gotH[i]:X16} CPU={expected:X16}");
            }
        });

        // A [NoInlining] helper WITHOUT barriers keeping locals it passes `ref` to another
        // [NoInlining] helper. On Wasm those locals live in the helper's scratch region, whose
        // base the caller passes in - and a caller without barriers never initialized that base,
        // so the helper's locals sat at linear-memory address 0, shared with buffer data and
        // every other thread: wrong values that changed from run to run.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void NoInlCF_RefMix(ref ulong a, ref ulong b, ulong m)
        {
            a = a + b + m;
            b ^= a;
            b = (b >> 24) | (b << 40);
            a += b;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void NoInlCF_RefCallerHelper(uint seed, out ulong e0, out ulong e1)
        {
            ulong h0 = 0x6A09E667F3BCC908UL ^ seed, h1 = 0xBB67AE8584CAA73BUL;
            for (int k = 1; k <= 63; k++)
                NoInlCF_RefMix(ref h0, ref h1, (ulong)k);
            if ((seed & 1) != 0)
                h1 += 5;
            NoInlCF_RefMix(ref h0, ref h1, seed);
            e0 = h0;
            e1 = h1;
        }

        static void NoInlCF_RefCallerKernel(Index1D i, ArrayView<ulong> out0, ArrayView<ulong> out1)
        {
            NoInlCF_RefCallerHelper((uint)i.X, out ulong e0, out ulong e1);
            out0[i] = e0;
            out1[i] = e1;
        }

        [TestMethod]
        public async Task NoInliningHelper_PassesLocalsByRefToNoInliningHelper_MatchesCpu() => await RunEmulatedTest(async accelerator =>
        {
            const int N = 256;
            using var out0 = accelerator.Allocate1D<ulong>(N);
            using var out1 = accelerator.Allocate1D<ulong>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<ulong>>(NoInlCF_RefCallerKernel);
            k(N, out0.View, out1.View);
            await accelerator.SynchronizeAsync();
            var got0 = await out0.CopyToHostAsync<ulong>();
            var got1 = await out1.CopyToHostAsync<ulong>();
            for (int i = 0; i < N; i++)
            {
                NoInlCF_RefCallerHelper((uint)i, out ulong e0, out ulong e1);
                if (got0[i] != e0 || got1[i] != e1)
                    throw new Exception($"Ref-passing helper mismatch at {i}: GPU=({got0[i]:X16},{got1[i]:X16}) CPU=({e0:X16},{e1:X16})");
            }
        });

        // 32-bit UNSIGNED shift / divide / remainder inside a [NoInlining] helper, on operands
        // with the high bit set. uint lives in a signed 32-bit register on WGSL, and the helper
        // path emitted plain `>>` / `/` / `%` - all signed - while the kernel path bitcast
        // through u32.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static uint NoInlCF_UnsignedOps(uint x, uint d)
        {
            uint shifted = (x >> 12) | (x << 20);
            return (shifted / d) ^ (x % d) ^ (x >> 31);
        }

        static void NoInlCF_UnsignedOpsKernel(Index1D i, ArrayView<uint> input, ArrayView<uint> output)
        {
            output[i] = NoInlCF_UnsignedOps(input[i], (uint)(i.X % 13) + 3u);
        }

        [TestMethod]
        public async Task NoInliningHelper_UnsignedShiftDivRem_HighBit_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int N = 1024;
            var data = new uint[N];
            uint s = 0x9E3779B9u;
            for (int i = 0; i < N; i++)
            {
                s ^= s << 13; s ^= s >> 17; s ^= s << 5;
                data[i] = s | (i % 2 == 0 ? 0x80000000u : 0u);
            }
            using var input = accelerator.Allocate1D(data);
            using var output = accelerator.Allocate1D<uint>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<uint>, ArrayView<uint>>(NoInlCF_UnsignedOpsKernel);
            k(N, input.View, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<uint>();
            for (int i = 0; i < N; i++)
            {
                uint expected = NoInlCF_UnsignedOps(data[i], (uint)(i % 13) + 3u);
                if (got[i] != expected)
                    throw new Exception($"Unsigned ops in helper mismatch at {i} (x=0x{data[i]:X8}): GPU=0x{got[i]:X8} CPU=0x{expected:X8}");
            }
        });

        // A dense C# switch (IL `switch` -> IR SwitchBranch) inside a loop, in a kernel body and
        // in a [NoInlining] helper. The cases call a [NoInlining] helper so they cannot be folded
        // into selects.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int NoInlCF_Switchy(int x)
        {
            int acc = 0;
            for (int k = 0; k < (x & 7) + 1; k++)
            {
                switch ((x + k) % 6)
                {
                    case 0: acc += NoInlCF_Step(x + k); break;
                    case 1: acc -= NoInlCF_Step(k + 3) * 2; break;
                    case 2: acc ^= NoInlCF_Step(x) + k; break;
                    case 3: acc += 17; break;
                    case 4: acc = acc * 3 - NoInlCF_Step(acc & 1023); break;
                    default: acc += k * k; break;
                }
            }
            return acc;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int NoInlCF_SwitchyHelper(int x) => NoInlCF_Switchy(x);

        static void NoInlCF_SwitchyKernel(Index1D i, ArrayView<int> inKernel, ArrayView<int> inHelper)
        {
            inKernel[i] = NoInlCF_Switchy(i.X);
            inHelper[i] = NoInlCF_SwitchyHelper(i.X);
        }

        [TestMethod]
        public async Task Switch_KernelAndNoInliningHelper_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int N = 1024;
            using var inKernel = accelerator.Allocate1D<int>(N);
            using var inHelper = accelerator.Allocate1D<int>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(NoInlCF_SwitchyKernel);
            k(N, inKernel.View, inHelper.View);
            await accelerator.SynchronizeAsync();
            var gotK = await inKernel.CopyToHostAsync<int>();
            var gotH = await inHelper.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int expected = NoInlCF_Switchy(i);
                if (gotK[i] != expected || gotH[i] != expected)
                    throw new Exception($"Switch mismatch at {i}: kernel={gotK[i]} helper={gotH[i]} CPU={expected}");
            }
        });

        // A bool PARAMETER tested inside a [NoInlining] helper, exactly Blake2b.Compress's
        // `if (isLastBlock) v14 = ~v14;` shape: Roslyn emits `brfalse`, which reaches the backend
        // as a logical NOT of the bool. Inlined with a constant argument it folds away; as a real
        // parameter Wasm emitted it as a bitwise `x ^ -1` - never zero for a 0/1 bool, so the
        // branch always went the same way.
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int NoInlCF_BoolParam(int x, bool flag)
        {
            if (flag)
                x = ~(NoInlCF_Step(x) * 5);
            return x + 1;
        }

        static void NoInlCF_BoolParamKernel(Index1D i, ArrayView<int> output)
        {
            output[i] = NoInlCF_BoolParam(i.X, (i.X % 3) == 1);
        }

        [TestMethod]
        public async Task NoInliningHelper_NegatedBoolParameter_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int N = 1024;
            using var output = accelerator.Allocate1D<int>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(NoInlCF_BoolParamKernel);
            k(N, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int expected = NoInlCF_BoolParam(i, (i % 3) == 1);
                if (got[i] != expected)
                    throw new Exception($"Negated bool parameter mismatch at {i}: GPU={got[i]} CPU={expected}");
            }
        });

        // Constant 64-bit shift amounts at every boundary of the emulated (lo, hi) word split, on
        // values with the sign bit and both words populated. The emulated backends emit a constant
        // shift as a branch-free expression (not the general shift function), one per case below.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static ulong NoInlCF_ConstShifts(ulong u)
        {
            long s = (long)u;
            ulong acc = 0;
            acc ^= (u << 0) ^ (u << 1) ^ (u << 31) ^ (u << 32) ^ (u << 33) ^ (u << 63);
            acc += (u >> 0) ^ (u >> 1) ^ (u >> 31) ^ (u >> 32) ^ (u >> 33) ^ (u >> 63);
            acc ^= (ulong)((s >> 0) ^ (s >> 1) ^ (s >> 31) ^ (s >> 32) ^ (s >> 33) ^ (s >> 63));
            acc += (ulong)((s << 7) ^ (s << 40));
            return acc;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static ulong NoInlCF_ConstShiftsHelper(ulong u) => NoInlCF_ConstShifts(u);

        static void NoInlCF_ConstShiftsKernel(Index1D i, ArrayView<ulong> input, ArrayView<ulong> inKernel, ArrayView<ulong> inHelper)
        {
            ulong u = input[i];
            inKernel[i] = NoInlCF_ConstShifts(u);
            inHelper[i] = NoInlCF_ConstShiftsHelper(u);
        }

        [TestMethod]
        public async Task ConstantShift64_Boundaries_KernelAndNoInliningHelper_MatchesCpu() => await RunEmulatedTest(async accelerator =>
        {
            var data = new ulong[] { 0UL, 1UL, 0x8000000000000000UL, 0xFFFFFFFFFFFFFFFFUL, 0x0123456789ABCDEFUL,
                0xFEDCBA9876543210UL, 0x00000000FFFFFFFFUL, 0xFFFFFFFF00000000UL, 0x80000000_00000001UL, 0x7FFFFFFF_80000000UL };
            int n = data.Length;
            using var input = accelerator.Allocate1D(data);
            using var inKernel = accelerator.Allocate1D<ulong>(n);
            using var inHelper = accelerator.Allocate1D<ulong>(n);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ulong>, ArrayView<ulong>, ArrayView<ulong>>(NoInlCF_ConstShiftsKernel);
            k(n, input.View, inKernel.View, inHelper.View);
            await accelerator.SynchronizeAsync();
            var gotK = await inKernel.CopyToHostAsync<ulong>();
            var gotH = await inHelper.CopyToHostAsync<ulong>();
            for (int i = 0; i < n; i++)
            {
                ulong expected = NoInlCF_ConstShifts(data[i]);
                if (gotK[i] != expected || gotH[i] != expected)
                    throw new Exception($"Constant 64-bit shift mismatch for 0x{data[i]:X16}: kernel={gotK[i]:X16} helper={gotH[i]:X16} CPU={expected:X16}");
            }
        });

        [TestMethod]
        public async Task NoInliningHelper_BranchesAndDataDependentLoop_MatchesCpu() => await RunTest(async accelerator =>
        {
            const int N = 1024;
            using var output = accelerator.Allocate1D<int>(N);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(NoInlCF_BranchyKernel);
            k(N, output.View);
            await accelerator.SynchronizeAsync();
            var got = await output.CopyToHostAsync<int>();
            for (int i = 0; i < N; i++)
            {
                int expected = NoInlCF_BranchyHelper(i + 1);
                if (got[i] != expected)
                    throw new Exception($"NoInlining branchy helper mismatch at {i}: GPU={got[i]} CPU={expected}");
            }
        });
    }
}
