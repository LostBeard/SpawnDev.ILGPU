using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // View-taking [NoInlining] helpers in the shapes SpawnDev.Codecs' decoders use, against the kernel run on the host. On
    // WebGPU (5.4.23) such a helper is emitted once per binding tuple and its view parameters index the kernel's bindings
    // (WGSLKernelFunctionGenerator.ViewHelpers.cs), so each shape exercises a binding handling through a helper function:
    // byte views (packed u32 words, atomic word stores, the true-length count slot), the length of a WHOLE parameter read
    // only inside the helper, an early return from a loop, stores only through the helper (binding access mode), an
    // ArrayView1D struct parameter, a call site inside a loop, two tuples of one helper in one kernel, and a helper calling
    // a helper with its own view parameter (whole, whose length the inner one reads, and a sub-view of it).
    public abstract partial class BackendTestBase
    {
        // first index >= start holding value, or -1: an early return out of a loop
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int ViewShapeFindByte(ArrayView<byte> src, int start, int value)
        {
            for (int k = start; k < src.IntLength; k++)
                if (src[k] == value)
                    return k;
            return -1;
        }

        // writes only through the helper; the kernel never stores to dst itself
        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ViewShapeCopyBytes(ArrayView<byte> src, ArrayView<byte> dst, int at, int count)
        {
            for (int k = 0; k < count; k++)
            {
                int s = at + k;
                if (s >= src.IntLength) return;
                dst[k] = (byte)(src[s] ^ 0x5A);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float ViewShapeDot(ArrayView<float> a, ArrayView<float> b, int n)
        {
            float acc = 0f;
            for (int k = 0; k < n; k++)
                acc += a[k] * b[k];
            return acc;
        }

        // a helper handing its own view parameter on: whole (the inner helper reads its length) and as a sub-view
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int ViewShapeOuter(ArrayView<byte> src, ArrayView<int> dst, int i)
        {
            int a = ViewShapeFindByte(src, i, 'o');
            int b = ViewShapeFindByte(src.SubView(1, src.IntLength - 1), i, 'o');
            dst[0] = a;
            dst[1] = b;
            return a * 3 + b;
        }

        const int ViewShapeRow = 16;

        static void ViewHelperShapesKernel(
            Index1D i,
            ArrayView<byte> text,          // 1-byte elements, length not a multiple of 4
            ArrayView<byte> copies,        // written only through ViewShapeCopyBytes
            ArrayView1D<float, Stride1D.Dense> weights,
            ArrayView<float> rows,
            ArrayView<int> found,
            ArrayView<float> dots,
            ArrayView<int> nested)
        {
            // the whole byte parameter (its true length, read only inside the helper) and a sub-view of it
            int first = ViewShapeFindByte(text, i, 'e');
            int second = ViewShapeFindByte(text.SubView(i, text.IntLength - i), 1, 'e');
            found[i * 2] = first;
            found[i * 2 + 1] = second;
            ViewShapeCopyBytes(text, copies.SubView(i * 8, 8), i * 3, 8);

            // the same float helper with two binding tuples (weights x rows, rows x rows), called inside a loop
            float total = 0f;
            for (int r = 0; r < 3; r++)
            {
                var row = rows.SubView(((i + r) % 8) * ViewShapeRow, ViewShapeRow);
                total += ViewShapeDot(weights.SubView(r * ViewShapeRow, ViewShapeRow), row, ViewShapeRow);
                total += ViewShapeDot(row, rows.SubView(r * ViewShapeRow, ViewShapeRow), ViewShapeRow - r);
            }
            dots[i] = total;

            nested[i * 3 + 2] = ViewShapeOuter(text, nested.SubView(i * 3, 2), i);
        }

        [TestMethod]
        public async Task ViewHelper_BindingShapesThroughHelpers_MatchHost() => await RunTest(async accelerator =>
        {
            // the copy loop stores 8 elements of one buffer from one store site per thread (WebGL transform feedback
            // captures one; its codegen refuses such a kernel, loudly)
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: one element per store site per thread (AcceleratorRequirements.RequiresScatterStores)");
            const int n = 24;
            var textBytes = System.Text.Encoding.ASCII.GetBytes("the quick brown fox jumped over the lazy sleepy dogs here!!"); // 59 bytes
            var weights = new float[3 * ViewShapeRow];
            for (int k = 0; k < weights.Length; k++) weights[k] = (k % 7) * 0.25f - 0.5f;
            var rows = new float[8 * ViewShapeRow];
            for (int k = 0; k < rows.Length; k++) rows[k] = ((k * 37) % 11) * 0.125f - 0.625f;

            using var dText = accelerator.Allocate1D(textBytes);
            using var dCopies = accelerator.Allocate1D<byte>(n * 8);
            using var dWeights = accelerator.Allocate1D(weights);
            using var dRows = accelerator.Allocate1D(rows);
            using var dFound = accelerator.Allocate1D<int>(n * 2);
            using var dDots = accelerator.Allocate1D<float>(n);
            using var dNested = accelerator.Allocate1D<int>(n * 3);
            dCopies.MemSetToZero();
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>, ArrayView<byte>,
                ArrayView1D<float, Stride1D.Dense>, ArrayView<float>, ArrayView<int>, ArrayView<float>, ArrayView<int>>(ViewHelperShapesKernel);
            kernel((Index1D)n, dText.View, dCopies.View, dWeights.View, dRows.View, dFound.View, dDots.View, dNested.View);
            var gotCopies = await dCopies.CopyToHostAsync<byte>();
            var gotFound = await dFound.CopyToHostAsync<int>();
            var gotDots = await dDots.CopyToHostAsync<float>();
            var gotNested = await dNested.CopyToHostAsync<int>();

            // host oracle: the kernel method itself on CPU-accelerator views, one thread at a time
            using var cpuContext = Context.Create(b => b.CPU());
            using var cpu = cpuContext.CreateCPUAccelerator(0);
            using var hText = cpu.Allocate1D(textBytes);
            using var hCopies = cpu.Allocate1D<byte>(n * 8);
            using var hWeights = cpu.Allocate1D(weights);
            using var hRows = cpu.Allocate1D(rows);
            using var hFound = cpu.Allocate1D<int>(n * 2);
            using var hDots = cpu.Allocate1D<float>(n);
            using var hNested = cpu.Allocate1D<int>(n * 3);
            hCopies.MemSetToZero();
            for (int t = 0; t < n; t++)
                ViewHelperShapesKernel(t, hText.View, hCopies.View, hWeights.View, hRows.View, hFound.View, hDots.View, hNested.View);
            var wantCopies = await hCopies.CopyToHostAsync<byte>();
            var wantFound = await hFound.CopyToHostAsync<int>();
            var wantDots = await hDots.CopyToHostAsync<float>();
            var wantNested = await hNested.CopyToHostAsync<int>();

            for (int k = 0; k < wantFound.Length; k++)
                if (gotFound[k] != wantFound[k]) throw new Exception($"found[{k}] = {gotFound[k]}, host {wantFound[k]}");
            for (int k = 0; k < wantCopies.Length; k++)
                if (gotCopies[k] != wantCopies[k]) throw new Exception($"copies[{k}] = {gotCopies[k]}, host {wantCopies[k]}");
            for (int k = 0; k < wantNested.Length; k++)
                if (gotNested[k] != wantNested[k]) throw new Exception($"nested[{k}] = {gotNested[k]}, host {wantNested[k]}");
            for (int t = 0; t < n; t++)
                if (Math.Abs(gotDots[t] - wantDots[t]) > 1e-4f * Math.Max(1f, Math.Abs(wantDots[t])))
                    throw new Exception($"dots[{t}] = {gotDots[t]}, host {wantDots[t]}");
        });
    }
}
