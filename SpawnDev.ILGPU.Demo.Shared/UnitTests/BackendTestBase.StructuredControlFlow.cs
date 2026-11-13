using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Branchy multi-block kernels, every lane, against the same C# run on the host. Written for the Wasm backend's
    // structured control flow (SpawnDev.ILGPU/Wasm/Backend/WasmStructuredControlFlow.cs, 5.4.32): a function without
    // barriers is emitted as wasm block / loop / if with direct br instead of the br_table state machine. Each shape
    // stresses a part of that translation: phi copies on each edge (if / else chains, a swap through phis), loop
    // headers with more than one back edge (continue), loops with exits to different targets (break, break out of two
    // loops, return), a switch with shared and default targets, a do-while, a kernel-level return inside a loop, a helper
    // function of several blocks with a return value, a long chain of merge nodes (the probe that measured the state
    // machine at 10x native) and an IRREDUCIBLE graph (two entries into one cycle through goto), which must keep the
    // state machine. WasmTests.StructuredControlFlow_* runs the same kernels with the structured path on and off.
    // Integer math is unsigned where it can overflow (signed overflow is undefined in OpenCL C).
    public abstract partial class BackendTestBase
    {
        public const int ScfThreads = 16384;
        public const int ScfSeed = 0x5EED1234;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ScfMix(uint x)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            return x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static uint ScfStart(int i, int seed) => ScfMix((uint)i * 2654435761u + (uint)seed) | 1u;

        // if / else-if chains, nested ifs, conditional expressions: merge nodes with phis from three and four edges
        public static int ScfIfElseChain(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            uint a = x & 0xFF, b = (x >> 8) & 0xFF, r;
            if (a < 40) r = a * 3 + b;
            else if (a < 90) r = b - a;
            else if (a < 170)
            {
                if ((b & 1) != 0) r = a ^ b;
                else r = (a | b) + 7;
            }
            else r = a + b * 5;
            r += (x & 0x10000) != 0 ? r >> 1 : r << 1;
            if ((r & 3) == 0) r ^= 0x55;
            return (int)(r & 0x7FFFFFFF);
        }

        // nested loops with continue (two back edges into the inner header), break, and a break out of both loops
        public static int ScfNestedLoops(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            uint acc = 0;
            int outer = (int)(x & 7) + 1, inner = (int)((x >> 3) & 15) + 1;
            for (int a = 0; a < outer; a++)
            {
                for (int b = 0; b < inner; b++)
                {
                    x = ScfMix(x);
                    if (((uint)(a ^ b) & 1) != 0) continue;
                    if ((x & 31) == 0) break;
                    if ((x & 1023) == 7) goto Done;
                    acc += x & 0xFFFF;
                }
                acc ^= (uint)a * 0x9E37u;
            }
        Done:
            return (int)(acc & 0x7FFFFFFF);
        }

        // do-while with a two-part condition (two exits from the latch)
        public static int ScfDoWhile(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            int n = 0;
            do
            {
                x = ScfMix(x);
                n++;
            } while ((x & 15) != 0 && n < 64);
            return n * 65536 + (int)(x >> 16);
        }

        // a switch in a loop: cases that share a body, a case that leaves the loop, a default
        public static int ScfSwitch(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            uint acc = 0;
            for (int k = 0; k < 24; k++)
            {
                x = ScfMix(x);
                switch ((int)(x % 11u))
                {
                    case 0: acc += 1; break;
                    case 1:
                    case 4: acc += x & 0xFF; break;
                    case 2: acc ^= x; break;
                    case 3: acc = acc * 3 + 1; break;
                    case 5:
                    case 6:
                    case 7: acc -= x >> 24; break;
                    case 8: goto Out;
                    default: acc += (uint)k; break;
                }
            }
        Out:
            return (int)(acc & 0x7FFFFFFF);
        }

        // values that swap through the phis of one loop header (the parallel copy of an edge)
        public static int ScfPhiSwap(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            uint a = x & 0xFFFF, b = x >> 16, c = 1;
            int n = (int)(x & 31) + 3;
            for (int k = 0; k < n; k++)
            {
                if (((uint)k & 1) == (a & 1))
                {
                    uint t = a; a = b; b = t;
                }
                else
                {
                    uint t = a; a = b + c; b = c; c = t;
                }
            }
            return (int)((a * 31 + b * 7 + c) & 0x7FFFFFFF);
        }

        // a helper that stays a function of its own (several blocks, a return value, a return inside a loop)
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static uint ScfHelper(uint x, int limit)
        {
            uint acc = 0;
            for (int k = 0; k < limit; k++)
            {
                x = ScfMix(x);
                if ((x & 63) == 5) return acc + 1000000u;
                if ((x & 1) != 0) acc += x & 0xFF;
                else acc ^= x >> 20;
            }
            return acc;
        }

        public static int ScfHelperCaller(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            uint r = ScfHelper(x, (int)(x & 15) + 4);
            if ((r & 1) != 0) r = ScfHelper(r | 1u, 9);
            return (int)(r & 0x7FFFFFFF);
        }

        // the probe's shape: xorshift steps, each followed by a data-dependent branch (a long chain of merge nodes)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void ScfChainStep(ref uint x, ref uint acc, uint k)
        {
            x = ScfMix(x);
            if ((x & 3) == 0) acc += x >> (int)(k & 15);
            else if ((x & 3) == 1) acc ^= k * 0x45D9F3Bu;
        }

        public static int ScfLongChain(int i, int seed)
        {
            uint x = ScfStart(i, seed), acc = 0;
            for (uint round = 0; round < 3; round++)
            {
                ScfChainStep(ref x, ref acc, round * 32 + 0); ScfChainStep(ref x, ref acc, round * 32 + 1);
                ScfChainStep(ref x, ref acc, round * 32 + 2); ScfChainStep(ref x, ref acc, round * 32 + 3);
                ScfChainStep(ref x, ref acc, round * 32 + 4); ScfChainStep(ref x, ref acc, round * 32 + 5);
                ScfChainStep(ref x, ref acc, round * 32 + 6); ScfChainStep(ref x, ref acc, round * 32 + 7);
                ScfChainStep(ref x, ref acc, round * 32 + 8); ScfChainStep(ref x, ref acc, round * 32 + 9);
                ScfChainStep(ref x, ref acc, round * 32 + 10); ScfChainStep(ref x, ref acc, round * 32 + 11);
                ScfChainStep(ref x, ref acc, round * 32 + 12); ScfChainStep(ref x, ref acc, round * 32 + 13);
                ScfChainStep(ref x, ref acc, round * 32 + 14); ScfChainStep(ref x, ref acc, round * 32 + 15);
                ScfChainStep(ref x, ref acc, round * 32 + 16); ScfChainStep(ref x, ref acc, round * 32 + 17);
                ScfChainStep(ref x, ref acc, round * 32 + 18); ScfChainStep(ref x, ref acc, round * 32 + 19);
                ScfChainStep(ref x, ref acc, round * 32 + 20); ScfChainStep(ref x, ref acc, round * 32 + 21);
                ScfChainStep(ref x, ref acc, round * 32 + 22); ScfChainStep(ref x, ref acc, round * 32 + 23);
                ScfChainStep(ref x, ref acc, round * 32 + 24); ScfChainStep(ref x, ref acc, round * 32 + 25);
                ScfChainStep(ref x, ref acc, round * 32 + 26); ScfChainStep(ref x, ref acc, round * 32 + 27);
                ScfChainStep(ref x, ref acc, round * 32 + 28); ScfChainStep(ref x, ref acc, round * 32 + 29);
                ScfChainStep(ref x, ref acc, round * 32 + 30); ScfChainStep(ref x, ref acc, round * 32 + 31);
            }
            return (int)(acc & 0x7FFFFFFF);
        }

        // two entries into one cycle (A and B): an irreducible graph; the Wasm backend keeps the state machine for it
        public static int ScfIrreducible(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            int n = 0;
            if ((x & 2) != 0) goto B;
        A:
            x = ScfMix(x);
            n += 1;
            if ((x & 3) == 0 || n > 40) goto End;
        B:
            x = ScfMix(x ^ 0x9E37u);
            n += 2;
            if ((x & 7) != 0 && n < 50) goto A;
        End:
            return (int)(x & 0xFFFF) + n * 65536;
        }

        public static void ScfIfElseChainKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfIfElseChain(i, seed);
        public static void ScfNestedLoopsKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfNestedLoops(i, seed);
        public static void ScfDoWhileKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfDoWhile(i, seed);
        public static void ScfSwitchKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfSwitch(i, seed);
        public static void ScfPhiSwapKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfPhiSwap(i, seed);
        public static void ScfHelperKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfHelperCaller(i, seed);
        public static void ScfLongChainKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfLongChain(i, seed);
        public static void ScfIrreducibleKernel(Index1D i, ArrayView<int> o, int seed) => o[i] = ScfIrreducible(i, seed);

        // a return from the KERNEL inside a loop (the store happens on that path only), and a store after the loop
        public static void ScfEarlyReturnKernel(Index1D i, ArrayView<int> o, int seed)
        {
            uint x = ScfStart(i, seed);
            for (int k = 0; k < 48; k++)
            {
                x = ScfMix(x);
                if ((x & 31) == 3)
                {
                    o[i] = k * 65536 + (int)(x >> 16);
                    return;
                }
            }
            o[i] = -1 - (int)(x & 0xFFFF);
        }

        public static int ScfEarlyReturnOracle(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            for (int k = 0; k < 48; k++)
            {
                x = ScfMix(x);
                if ((x & 31) == 3) return k * 65536 + (int)(x >> 16);
            }
            return -1 - (int)(x & 0xFFFF);
        }

        public struct ScfPair { public int A; public int B; }

        // a STRUCT stored to global memory in the middle of a multi-block kernel, with blocks (a loop, a branch) after
        // it. The Wasm state machine's block map counted one extra state for such a store (a disabled "yield after a
        // struct store") that the emitter never creates: every later block would be mapped one state too far.
        public static void ScfStructStoreKernel(Index1D i, ArrayView<ScfPair> pairs, ArrayView<int> o, int seed)
        {
            uint x = ScfStart(i, seed);
            int n = 0;
            while ((x & 7) != 0 && n < 20) { x = ScfMix(x); n++; }
            ScfPair p;
            p.A = n;
            p.B = (int)(x >> 8);
            if ((x & 16) != 0) pairs[i] = p;
            else { p.B = -p.B; pairs[i] = p; }
            for (int k = 0; k < 6; k++)
            {
                x = ScfMix(x);
                if ((x & 1) != 0) n += k;
            }
            o[i] = n * 7 + (int)(x & 0xFFF);
        }

        public static (int A, int B, int O) ScfStructStoreOracle(int i, int seed)
        {
            uint x = ScfStart(i, seed);
            int n = 0;
            while ((x & 7) != 0 && n < 20) { x = ScfMix(x); n++; }
            int a = n, b = (int)(x >> 8);
            if ((x & 16) == 0) b = -b;
            for (int k = 0; k < 6; k++)
            {
                x = ScfMix(x);
                if ((x & 1) != 0) n += k;
            }
            return (a, b, n * 7 + (int)(x & 0xFFF));
        }

        public static async Task ScfRunStructStore(Accelerator accelerator, string name)
        {
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<ScfPair>, ArrayView<int>, int>(ScfStructStoreKernel);
            using var pairs = accelerator.Allocate1D<ScfPair>(ScfThreads);
            using var o = accelerator.Allocate1D<int>(ScfThreads);
            k((Index1D)ScfThreads, pairs.View, o.View, ScfSeed);
            var gotPairs = await pairs.CopyToHostAsync<ScfPair>();
            var gotO = await o.CopyToHostAsync<int>();
            int bad = 0, first = -1;
            for (int t = 0; t < ScfThreads; t++)
            {
                var e = ScfStructStoreOracle(t, ScfSeed);
                if (gotPairs[t].A != e.A || gotPairs[t].B != e.B || gotO[t] != e.O) { if (bad == 0) first = t; bad++; }
            }
            if (bad > 0)
            {
                var e = ScfStructStoreOracle(first, ScfSeed);
                throw new Exception($"{name}: {bad}/{ScfThreads} threads differ from the host; first thread {first}: got ({gotPairs[first].A}, {gotPairs[first].B}, {gotO[first]}), expected ({e.A}, {e.B}, {e.O})");
            }
        }

        [TestMethod]
        public async Task StructuredControlFlow_StructStoreBetweenBlocks_MatchesHost() => await RunTest(a => ScfRunStructStore(a, "StructStore"));

        /// <summary>The kernels of this file with their host oracles (WasmTests runs them with the structured path on and off).</summary>
        public static (string Name, Action<Index1D, ArrayView<int>, int> Kernel, Func<int, int, int> Oracle)[] ScfCases => new (string, Action<Index1D, ArrayView<int>, int>, Func<int, int, int>)[]
        {
            ("IfElseChain", ScfIfElseChainKernel, ScfIfElseChain),
            ("NestedLoops", ScfNestedLoopsKernel, ScfNestedLoops),
            ("DoWhile", ScfDoWhileKernel, ScfDoWhile),
            ("Switch", ScfSwitchKernel, ScfSwitch),
            ("PhiSwap", ScfPhiSwapKernel, ScfPhiSwap),
            ("Helper", ScfHelperKernel, ScfHelperCaller),
            ("LongChain", ScfLongChainKernel, ScfLongChain),
            ("EarlyReturn", ScfEarlyReturnKernel, ScfEarlyReturnOracle),
            ("Irreducible", ScfIrreducibleKernel, ScfIrreducible),
        };

        /// <summary>Runs <paramref name="kernel"/> over <see cref="ScfThreads"/> threads and compares every output with the host oracle.</summary>
        public static async Task ScfRunAndCheck(Accelerator accelerator, string name, Action<Index1D, ArrayView<int>, int> kernel, Func<int, int, int> oracle)
        {
            var k = accelerator.LoadAutoGroupedStreamKernel(kernel);
            using var o = accelerator.Allocate1D<int>(ScfThreads);
            k((Index1D)ScfThreads, o.View, ScfSeed);
            var got = await o.CopyToHostAsync<int>();
            int bad = 0, first = -1;
            for (int t = 0; t < ScfThreads; t++)
            {
                if (got[t] != oracle(t, ScfSeed)) { if (bad == 0) first = t; bad++; }
            }
            if (bad > 0)
                throw new Exception($"{name}: {bad}/{ScfThreads} threads differ from the host; first thread {first}: got {got[first]}, expected {oracle(first, ScfSeed)}");
        }

        [TestMethod]
        public async Task StructuredControlFlow_IfElseChain_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "IfElseChain", ScfIfElseChainKernel, ScfIfElseChain));

        [TestMethod]
        public async Task StructuredControlFlow_NestedLoopsContinueBreak_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "NestedLoops", ScfNestedLoopsKernel, ScfNestedLoops));

        [TestMethod]
        public async Task StructuredControlFlow_DoWhile_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "DoWhile", ScfDoWhileKernel, ScfDoWhile));

        [TestMethod]
        public async Task StructuredControlFlow_SwitchInLoop_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "Switch", ScfSwitchKernel, ScfSwitch));

        [TestMethod]
        public async Task StructuredControlFlow_PhiSwap_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "PhiSwap", ScfPhiSwapKernel, ScfPhiSwap));

        [TestMethod]
        public async Task StructuredControlFlow_MultiBlockHelperWithResult_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "Helper", ScfHelperKernel, ScfHelperCaller));

        [TestMethod]
        public async Task StructuredControlFlow_LongBranchChain_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "LongChain", ScfLongChainKernel, ScfLongChain));

        [TestMethod]
        public async Task StructuredControlFlow_KernelReturnInLoop_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "EarlyReturn", ScfEarlyReturnKernel, ScfEarlyReturnOracle));

        [TestMethod]
        public async Task StructuredControlFlow_IrreducibleGoto_MatchesHost() => await RunTest(a => ScfRunAndCheck(a, "Irreducible", ScfIrreducibleKernel, ScfIrreducible));
    }
}
