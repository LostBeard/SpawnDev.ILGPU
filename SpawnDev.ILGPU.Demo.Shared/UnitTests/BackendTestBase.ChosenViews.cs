using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A VIEW chosen at run time: a select of two sub-views, a view assigned in both arms of a branch (a phi), and the
    // length of a chosen view.
    //
    // WGSL has no pointer variables and no select() of pointers, and a sub-view there is an alias of its root binding
    // whose offset is applied where it is indexed, so the WGSL of all three failed shader creation ("unexpected argument
    // type for `select` call", "cannot convert ptr to i32"). WebGPU now lowers a chosen view to an element offset (and a
    // length) into its one root view. A choice between two DIFFERENT buffers cannot be expressed in WGSL and is refused
    // with UnsupportedKernelFeatureException. WebGL (whose GLSL had the same gaps) refuses every chosen view the same way;
    // CUDA, OpenCL, Wasm, Velocity and the CPU run all of them.
    public abstract partial class BackendTestBase
    {
        static void ChosenView_SelectOfSubViewsKernel(Index1D idx, ArrayView<int> output, ArrayView<int> input)
        {
            int i = idx;
            bool even = (i & 1) == 0;
            var b = even ? output.SubView(0, output.Length) : output.SubView(1, output.Length - 1);
            b[even ? i : i - 1] = input[i] * 3 + 1;
        }

        static void ChosenView_PhiOfSubViewsKernel(Index1D idx, ArrayView<int> output, ArrayView<int> input)
        {
            int i = idx;
            ArrayView<int> b;
            int x, j;
            if ((i & 1) == 0)
            {
                b = output.SubView(0, output.Length);
                x = 0;
                for (int k = 0; k < (input[i] & 7); k++) x += input[(i + k) % input.IntLength];
                j = i;
            }
            else
            {
                b = output.SubView(1, output.Length - 1);
                x = 1;
                for (int k = 0; k < (input[i] & 3); k++) x = x * 3 + input[(i + k) % input.IntLength];
                j = i - 1;
            }
            b[j] = x;
        }

        static void ChosenView_LengthKernel(Index1D idx, ArrayView<int> output, ArrayView<int> input)
        {
            int i = idx;
            var b = (i & 1) == 0 ? input.SubView(0, 4) : input.SubView(4, 9);
            output[i] = (int)b.Length * 1000 + b[i % 4];
        }

        static void ChosenView_TwoBuffersKernel(Index1D idx, ArrayView<int> outA, ArrayView<int> outB, ArrayView<int> input)
        {
            int i = idx;
            var b = (i & 1) == 0 ? outA : outB;
            b[i] = input[i] + 5;
        }

        /// <summary>
        /// Runs <paramref name="run"/>; on a backend that <paramref name="mustRefuse"/>s the kernel, passes only if it threw
        /// UnsupportedKernelFeatureException (directly or wrapped) and returns true. Returns false after a normal run.
        /// </summary>
        static async Task<bool> ChosenViewRunOrRefused(Accelerator accelerator, bool mustRefuse, Func<Task> run)
        {
            try
            {
                await run();
            }
            catch (Exception ex) when (mustRefuse)
            {
                for (var e = ex; e != null; e = e.InnerException)
                    if (e is UnsupportedKernelFeatureException) return true;
                throw;
            }
            if (mustRefuse)
                throw new Exception($"{accelerator.GetType().Name} ran a view chosen at run time it must refuse (UnsupportedKernelFeatureException)");
            return false;
        }

        static int[] ChosenViewInput(int n)
        {
            var input = new int[n];
            for (int i = 0; i < n; i++) input[i] = (i * 37 + 11) % 101 - 20;
            return input;
        }

        [TestMethod]
        public async Task ChosenView_SelectOfSubViews_IndexesTheChosenOffset() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 256;
            var input = ChosenViewInput(n);
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<int>(n);
            if (await ChosenViewRunOrRefused(accelerator, accelerator is WebGLAccelerator, async () =>
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(ChosenView_SelectOfSubViewsKernel);
                kernel((Index1D)n, dOut.View, dIn.View);
                await accelerator.SynchronizeAsync();
            })) return;
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
                if (got[i] != input[i] * 3 + 1)
                    throw new Exception($"output[{i}] = {got[i]}, expected {input[i] * 3 + 1}");
        });

        [TestMethod]
        public async Task ChosenView_PhiOfSubViews_IndexesTheChosenOffset() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 256;
            var input = ChosenViewInput(n);
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<int>(n);
            if (await ChosenViewRunOrRefused(accelerator, accelerator is WebGLAccelerator, async () =>
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(ChosenView_PhiOfSubViewsKernel);
                kernel((Index1D)n, dOut.View, dIn.View);
                await accelerator.SynchronizeAsync();
            })) return;
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int x;
                if ((i & 1) == 0) { x = 0; for (int k = 0; k < (input[i] & 7); k++) x += input[(i + k) % n]; }
                else { x = 1; for (int k = 0; k < (input[i] & 3); k++) x = x * 3 + input[(i + k) % n]; }
                if (got[i] != x)
                    throw new Exception($"output[{i}] = {got[i]}, expected {x}");
            }
        });

        [TestMethod]
        public async Task ChosenView_Length_IsTheChosenViewsLength() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 128;
            var input = ChosenViewInput(16);
            using var dIn = accelerator.Allocate1D(input);
            using var dOut = accelerator.Allocate1D<int>(n);
            if (await ChosenViewRunOrRefused(accelerator, accelerator is WebGLAccelerator, async () =>
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>>(ChosenView_LengthKernel);
                kernel((Index1D)n, dOut.View, dIn.View);
                await accelerator.SynchronizeAsync();
            })) return;
            var got = await dOut.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int want = (i & 1) == 0 ? 4 * 1000 + input[i % 4] : 9 * 1000 + input[4 + i % 4];
                if (got[i] != want)
                    throw new Exception($"output[{i}] = {got[i]}, expected {want}");
            }
        });

        [TestMethod]
        public async Task ChosenView_TwoBuffers_RunsOrIsRefusedOnWebGPUAndWebGL() => await RunEmulatedTest(async accelerator =>
        {
            const int n = 128;
            var input = ChosenViewInput(n);
            using var dIn = accelerator.Allocate1D(input);
            using var dA = accelerator.Allocate1D<int>(n);
            using var dB = accelerator.Allocate1D<int>(n);
            if (await ChosenViewRunOrRefused(accelerator, accelerator is WebGPUAccelerator || accelerator is WebGLAccelerator, async () =>
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, ArrayView<int>>(ChosenView_TwoBuffersKernel);
                kernel((Index1D)n, dA.View, dB.View, dIn.View);
                await accelerator.SynchronizeAsync();
            })) return;
            var a = await dA.CopyToHostAsync<int>();
            var b = await dB.CopyToHostAsync<int>();
            for (int i = 0; i < n; i++)
            {
                int got = (i & 1) == 0 ? a[i] : b[i];
                if (got != input[i] + 5)
                    throw new Exception($"{((i & 1) == 0 ? "outA" : "outB")}[{i}] = {got}, expected {input[i] + 5}");
            }
        });
    }
}
