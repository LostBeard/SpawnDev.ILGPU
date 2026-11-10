using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A value-returning helper with an early return inside a loop inside an if, then a shared tail and the final return
    // (SpawnDev.Codecs Bc7EncoderGpu.CellCompression: `if (!one) { for (..) { ..; if (best == 0) return 0; } }
    // if (p) {..} return best;`, Geordi 2026-11-10). On WebGPU the helper is emitted as a WGSL function (it takes a view).
    // The structured walker ran the loop's exits up to their common post-dominator, the function's exit, so the tail and
    // the final return went into the loop's arm and the other arm fell off the end: "missing return at end of function",
    // the shader module failed to compile. Values checked against the kernel on the CPU accelerator on every backend; on
    // WebGPU the tail is also checked to be emitted once in the helper function.
    public abstract partial class BackendTestBase
    {
        static int EarlyReturnTail(ArrayView<int> d, int seed, bool one, bool part)
        {
            int best = 1000 + (seed & 15);
            if (!one)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int k = 0; k < 4; k++) best = Math.Min(best, d[(seed + k + pass * 7) & 255] & 1023);
                    if (best == 0) return 0;
                }
            }
            if (part)
            {
                best ^= 7777777;
                best += d[(seed + 3) & 255] & 7;
            }
            return best;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int EarlyReturnTailHelper(ArrayView<int> d, int seed, bool one, bool part) => EarlyReturnTail(d, seed, one, part);

        static void EarlyReturnInLoopHelperKernel(Index1D i, ArrayView<int> d, ArrayView<int> o) =>
            o[i] = EarlyReturnTailHelper(d.SubView(0, 256), d[i & 255], (d[(i + 1) & 255] & 1) != 0, (d[(i + 2) & 255] & 1) != 0);

        static void EarlyReturnInLoopInlinedKernel(Index1D i, ArrayView<int> d, ArrayView<int> o) =>
            o[i] = EarlyReturnTail(d, d[i & 255], (d[(i + 1) & 255] & 1) != 0, (d[(i + 2) & 255] & 1) != 0);

        async Task EarlyReturnInLoopCheck(Accelerator accelerator, Action<Index1D, ArrayView<int>, ArrayView<int>> kernelMethod, string name)
        {
            const int n = 512;
            var data = new int[256];
            var rnd = new Random(1110);
            // small values so `best == 0` (the early return) is taken by some threads and not by others
            for (int k = 0; k < data.Length; k++) data[k] = rnd.Next(0, 4) == 0 ? rnd.Next(0, 3) * 1024 : rnd.Next();
            using var dData = accelerator.Allocate1D(data);
            using var dOut = accelerator.Allocate1D<int>(n);
            dOut.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel(kernelMethod);
            kernel((Index1D)n, dData.View, dOut.View);
            var got = await dOut.CopyToHostAsync<int>();

            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hData = cpu.Allocate1D(data);
            using var hOut = cpu.Allocate1D<int>(n);
            hOut.MemSetToZero();
            for (int t = 0; t < n; t++) kernelMethod(t, hData.View, hOut.View);
            var want = await hOut.CopyToHostAsync<int>();
            int zeros = 0, tails = 0;
            for (int t = 0; t < n; t++)
            {
                if (got[t] != want[t]) throw new Exception($"{name}: o[{t}] = {got[t]}, host {want[t]}");
                if (want[t] == 0) zeros++;
                if ((want[t] & 0x700000) != 0) tails++;
            }
            // the shape is only exercised when both the early return and the tail are taken somewhere
            if (zeros == 0 || tails == 0)
                throw new Exception($"{name}: the data did not exercise both paths (early returns {zeros}, tails {tails})");
        }

        [TestMethod]
        public async Task EarlyReturnInLoop_ValueHelper_CompilesAndMatchesHost() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGPU)
            {
                var method = typeof(BackendTestBase).GetMethod(nameof(EarlyReturnInLoopHelperKernel), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                var wgsl = ShaderCompiler.Generate(method!, CapabilityProfiles.WebGPUBaseline).Source ?? "";
                int at = wgsl.IndexOf("fn EarlyReturnTailHelper", StringComparison.Ordinal);
                if (at < 0) throw new Exception("EarlyReturnTailHelper was not emitted as a WGSL function: the test no longer covers its shape");
                int markers = 0, i = at;
                while ((i = wgsl.IndexOf("7777777", i, StringComparison.Ordinal)) >= 0) { markers++; i += 7; }
                if (markers != 1)
                    throw new Exception($"EarlyReturnTailHelper: the tail is emitted {markers} times in its WGSL function, expected once");
            }
            await EarlyReturnInLoopCheck(accelerator, EarlyReturnInLoopHelperKernel, nameof(EarlyReturnInLoopHelperKernel));
        });

        [TestMethod]
        public async Task EarlyReturnInLoop_Inlined_MatchesHost() => await RunTest(async accelerator =>
            await EarlyReturnInLoopCheck(accelerator, EarlyReturnInLoopInlinedKernel, nameof(EarlyReturnInLoopInlinedKernel)));
    }
}
