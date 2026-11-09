using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A [NoInlining] helper taking STORAGE views (sub-views of kernel parameters), called from many sites. On WebGPU and
    // WebGL every view-taking helper used to be inlined at every call site (InlineAddressParameterCalls), so shader size
    // grew with call sites x helper size: SpawnDev.Codecs' VP9 decoder kernels came out at 33-87 MB of WGSL (300k-820k
    // lines) and lost the device in Tint (Geordi, 2026-11-09). Since 5.4.23 WebGPU keeps such a call and emits the helper
    // once per binding tuple (WGSLKernelFunctionGenerator.ViewHelpers.cs). Correctness on every backend; on the WebGPU lane
    // also the size gate: 16 call sites must not cost 16 helper bodies.
    public abstract partial class BackendTestBase
    {
        const int ViewHelperSegment = 64;

        [MethodImpl(MethodImplOptions.NoInlining)]
        static int ViewHelperMix(ArrayView<int> src, ArrayView<int> dst, int i, int salt)
        {
            int acc = salt;
            for (int k = 0; k < 8; k++)
                acc = acc * 31 + src[(i + k) % (int)src.Length];
            if ((acc & 1) != 0)
                acc ^= src[i] << 3;
            dst[i] = acc;
            return acc;
        }

        // 16 call sites, each with its own sub-views (offset c * 64)
        static void ViewHelperManyCallSitesKernel(Index1D i, ArrayView<int> a, ArrayView<int> b, ArrayView<int> sums)
        {
            int s = 0;
            s += ViewHelperMix(a.SubView(0 * ViewHelperSegment, ViewHelperSegment), b.SubView(0 * ViewHelperSegment, ViewHelperSegment), i, 0);
            s += ViewHelperMix(a.SubView(1 * ViewHelperSegment, ViewHelperSegment), b.SubView(1 * ViewHelperSegment, ViewHelperSegment), i, 1);
            s += ViewHelperMix(a.SubView(2 * ViewHelperSegment, ViewHelperSegment), b.SubView(2 * ViewHelperSegment, ViewHelperSegment), i, 2);
            s += ViewHelperMix(a.SubView(3 * ViewHelperSegment, ViewHelperSegment), b.SubView(3 * ViewHelperSegment, ViewHelperSegment), i, 3);
            s += ViewHelperMix(a.SubView(4 * ViewHelperSegment, ViewHelperSegment), b.SubView(4 * ViewHelperSegment, ViewHelperSegment), i, 4);
            s += ViewHelperMix(a.SubView(5 * ViewHelperSegment, ViewHelperSegment), b.SubView(5 * ViewHelperSegment, ViewHelperSegment), i, 5);
            s += ViewHelperMix(a.SubView(6 * ViewHelperSegment, ViewHelperSegment), b.SubView(6 * ViewHelperSegment, ViewHelperSegment), i, 6);
            s += ViewHelperMix(a.SubView(7 * ViewHelperSegment, ViewHelperSegment), b.SubView(7 * ViewHelperSegment, ViewHelperSegment), i, 7);
            s += ViewHelperMix(a.SubView(8 * ViewHelperSegment, ViewHelperSegment), b.SubView(8 * ViewHelperSegment, ViewHelperSegment), i, 8);
            s += ViewHelperMix(a.SubView(9 * ViewHelperSegment, ViewHelperSegment), b.SubView(9 * ViewHelperSegment, ViewHelperSegment), i, 9);
            s += ViewHelperMix(a.SubView(10 * ViewHelperSegment, ViewHelperSegment), b.SubView(10 * ViewHelperSegment, ViewHelperSegment), i, 10);
            s += ViewHelperMix(a.SubView(11 * ViewHelperSegment, ViewHelperSegment), b.SubView(11 * ViewHelperSegment, ViewHelperSegment), i, 11);
            s += ViewHelperMix(a.SubView(12 * ViewHelperSegment, ViewHelperSegment), b.SubView(12 * ViewHelperSegment, ViewHelperSegment), i, 12);
            s += ViewHelperMix(a.SubView(13 * ViewHelperSegment, ViewHelperSegment), b.SubView(13 * ViewHelperSegment, ViewHelperSegment), i, 13);
            s += ViewHelperMix(a.SubView(14 * ViewHelperSegment, ViewHelperSegment), b.SubView(14 * ViewHelperSegment, ViewHelperSegment), i, 14);
            s += ViewHelperMix(a.SubView(15 * ViewHelperSegment, ViewHelperSegment), b.SubView(15 * ViewHelperSegment, ViewHelperSegment), i, 15);
            sums[i] = s;
        }

        static int WgslLineCount(string kernelName)
        {
            var method = typeof(BackendTestBase).GetMethod(kernelName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var wgsl = ShaderCompiler.Generate(method!, CapabilityProfiles.WebGPUBaseline).Source ?? "";
            return wgsl.Split('\n').Length;
        }

        // the same helper from ONE call site: the size reference
        static void ViewHelperOneCallSiteKernel(Index1D i, ArrayView<int> a, ArrayView<int> b, ArrayView<int> sums) =>
            sums[i] = ViewHelperMix(a.SubView(0, ViewHelperSegment), b.SubView(0, ViewHelperSegment), i, 0);

        [TestMethod]
        public async Task ViewHelper_ManyCallSitesWithSubViews_MatchesHost() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType == AcceleratorType.WebGPU)
            {
                int many = WgslLineCount(nameof(ViewHelperManyCallSitesKernel));
                int one = WgslLineCount(nameof(ViewHelperOneCallSiteKernel));
                // emitted once: 651 vs 516 lines (the emulation library is most of both); inlined at every site: ~3x
                if (many * 2 > one * 3)
                    throw new Exception($"ViewHelper: 16 call sites generate {many} WGSL lines, 1 call site {one}; the helper body is repeated per call site");
            }
            const int n = ViewHelperSegment;
            var a = new int[16 * ViewHelperSegment];
            for (int k = 0; k < a.Length; k++) a[k] = k * 2654435 + 17;
            using var dA = accelerator.Allocate1D(a);
            using var dB = accelerator.Allocate1D<int>(a.Length);
            using var dSums = accelerator.Allocate1D<int>(n);
            dB.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(ViewHelperManyCallSitesKernel);
            kernel((Index1D)n, dA.View, dB.View, dSums.View);
            var gotB = await dB.CopyToHostAsync<int>();
            var gotSums = await dSums.CopyToHostAsync<int>();

            // host oracle: the kernel method itself on CPU-accelerator views, one thread at a time
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hA = cpu.Allocate1D(a);
            using var hB = cpu.Allocate1D<int>(a.Length);
            using var hSums = cpu.Allocate1D<int>(n);
            hB.MemSetToZero();
            for (int t = 0; t < n; t++) ViewHelperManyCallSitesKernel(t, hA.View, hB.View, hSums.View);
            var wantB = await hB.CopyToHostAsync<int>();
            var wantSums = await hSums.CopyToHostAsync<int>();
            for (int k = 0; k < wantB.Length; k++)
                if (gotB[k] != wantB[k]) throw new Exception($"b[{k}] = {gotB[k]}, host {wantB[k]}");
            for (int t = 0; t < n; t++)
                if (gotSums[t] != wantSums[t]) throw new Exception($"sums[{t}] = {gotSums[t]}, host {wantSums[t]}");
        });
    }
}
