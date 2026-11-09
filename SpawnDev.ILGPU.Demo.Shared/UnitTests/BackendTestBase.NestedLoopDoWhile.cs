using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A while loop around a do-while whose condition is a short circuit with side effects (`(x -= np) > 0 && --cnt >= 0`),
    // an early `break` out of the inner loop, and a third loop inside. From SpawnDev.Codecs' MP3 decoder (minimp3's
    // big_values region/band/pair walk, Geordi 2026-11-08): on CUDA (5.4.18) the outer `ireg++` ran once per INNER
    // iteration (ireg came out equal to sfb) and 8 of 10 cases were wrong; CPU and OpenCL were right. One thread per
    // case, so every branch is thread-dependent. Expected values: the same method run on the host (.NET semantics).
    public abstract partial class BackendTestBase
    {
        // MPEG-1 44.1 kHz long-block band widths (minimp3 g_scf_long[0]), 0-terminated.
        static readonly int[] NestedLoopBands = { 4, 4, 4, 4, 4, 4, 6, 6, 8, 8, 10, 12, 16, 20, 24, 28, 34, 42, 50, 54, 76, 158, 0 };

        // (big_values, region0_count, region1_count) per case.
        static readonly int[] NestedLoopCases =
        {
            70, 7, 255,
            13, 7, 255,
            76, 8, 255,
            115, 8, 255,
            88, 7, 6,
            69, 9, 4,
            288, 15, 3,
            0, 7, 255,
            1, 0, 0,
            200, 2, 2,
        };

        // Geordi's exact shape: the loop nest in a helper returning a value tuple.
        static void NestedLoopDoWhileKernel(Index1D i, ArrayView<int> bands, ArrayView<int> cases, ArrayView<int> output)
        {
            var (ireg, sfb, left, total) = NestedLoopDoWhileWalk(bands, cases[3 * i], cases[3 * i + 1], cases[3 * i + 2]);
            output[4 * i] = ireg;
            output[4 * i + 1] = sfb;
            output[4 * i + 2] = left;
            output[4 * i + 3] = total;
        }

        static (int Ireg, int Sfb, int Left, int Total) NestedLoopDoWhileWalk(ArrayView<int> bands, int bigValCnt, int region0, int region1)
        {
            int ireg = 0, sfb = 0, total = 0;
            int np, pairs;
            while (bigValCnt > 0 && ireg < 3)
            {
                int sfbCnt = ireg == 0 ? region0 : ireg == 1 ? region1 : 255;
                ireg++;
                do
                {
                    np = bands[sfb++] / 2;
                    if (np == 0) { bigValCnt = 0; break; }
                    pairs = bigValCnt < np ? bigValCnt : np;
                    do { total++; } while (--pairs > 0);
                } while ((bigValCnt -= np) > 0 && --sfbCnt >= 0);
            }
            return (ireg, sfb, bigValCnt, total);
        }

        [TestMethod]
        public async Task NestedLoop_InnerDoWhileShortCircuit_MatchesHost() => await RunTest(async accelerator =>
        {
            int n = NestedLoopCases.Length / 3;
            using var dBands = accelerator.Allocate1D(NestedLoopBands);
            using var dCases = accelerator.Allocate1D(NestedLoopCases);
            using var dOut = accelerator.Allocate1D<int>(n * 4);
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(NestedLoopDoWhileKernel);
            kernel((Index1D)n, dBands.View, dCases.View, dOut.View);
            var got = await dOut.CopyToHostAsync<int>();

            // host oracle: the kernel method itself on managed arrays, one "thread" at a time
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hBands = cpu.Allocate1D(NestedLoopBands);
            using var hCases = cpu.Allocate1D(NestedLoopCases);
            using var hOut = cpu.Allocate1D<int>(n * 4);
            for (int c = 0; c < n; c++) NestedLoopDoWhileKernel(c, hBands.View, hCases.View, hOut.View);
            var want = await hOut.CopyToHostAsync<int>();

            var fails = new List<string>();
            for (int c = 0; c < n; c++)
                for (int k = 0; k < 4; k++)
                    if (got[4 * c + k] != want[4 * c + k])
                    {
                        fails.Add($"case {c} (big {NestedLoopCases[3 * c]}, r0 {NestedLoopCases[3 * c + 1]}, r1 {NestedLoopCases[3 * c + 2]}): " +
                            $"want ireg/sfb/left/total = {want[4 * c]},{want[4 * c + 1]},{want[4 * c + 2]},{want[4 * c + 3]}, " +
                            $"got {got[4 * c]},{got[4 * c + 1]},{got[4 * c + 2]},{got[4 * c + 3]}");
                        break;
                    }
            if (fails.Count > 0) throw new Exception($"{fails.Count}/{n} cases wrong:\n" + string.Join("\n", fails));
        });

        /// <summary>The kernel above, for offline PTX inspection (DemoConsole ptx-dump).</summary>
        public static Delegate NestedLoopDoWhileKernelDelegate =>
            (Action<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>)NestedLoopDoWhileKernel;
    }
}
