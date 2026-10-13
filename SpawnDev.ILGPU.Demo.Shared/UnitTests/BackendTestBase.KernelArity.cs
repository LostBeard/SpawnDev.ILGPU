using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Kernel delegates with many parameters. Added 2026-10-13: under Blazor WebAssembly AOT, launching
    // FusedFFN_TiledLinearGELU_Correct (LoadStreamKernel, KernelConfig + 9 kernel parameters) killed the .NET runtime
    // with a Mono assertion (interp.c:3420, "couldn't compile wrapper": entering an interpreted method whose signature
    // has more than 8 parameters needs a per-signature interp_in wrapper). Each test launches ONE kernel shape, from
    // 8 kernel parameters up, so an AOT run shows exactly which shapes cross the boundary.
    public abstract partial class BackendTestBase
    {
        static void Arity7Kernel(Index1D i, ArrayView<int> o, int a, int b, int c, int d, int e, int f, int g)
        {
            if (i == 0) o[0] = a + b + c + d + e + f + g;
        }

        static void Arity8Kernel(Index1D i, ArrayView<int> o, int a, int b, int c, int d, int e, int f, int g, int h)
        {
            if (i == 0) o[0] = a + b + c + d + e + f + g + h;
        }

        static void Arity7StreamKernel(ArrayView<int> o, int a, int b, int c, int d, int e, int f, int g)
        {
            if (Grid.GlobalIndex.X == 0) o[0] = a + b + c + d + e + f + g;
        }

        static void Arity8StreamKernel(ArrayView<int> o, int a, int b, int c, int d, int e, int f, int g, int h)
        {
            if (Grid.GlobalIndex.X == 0) o[0] = a + b + c + d + e + f + g + h;
        }

        static void Arity11StreamKernel(ArrayView<int> o, int a, int b, int c, int d, int e, int f, int g, int h,
            int j, int k, int l)
        {
            if (Grid.GlobalIndex.X == 0) o[0] = a + b + c + d + e + f + g + h + j + k + l;
        }

        static void Arity4StreamKernel(ArrayView<int> o, int a, int b, int c, int d)
        {
            if (Grid.GlobalIndex.X == 0) o[0] = a + b + c + d;
        }

        static void Arity5StreamKernel(ArrayView<int> o, int a, int b, int c, int d, int e)
        {
            if (Grid.GlobalIndex.X == 0) o[0] = a + b + c + d + e;
        }

        static void Arity5Kernel(Index1D i, ArrayView<int> o, int a, int b, int c, int d, int e)
        {
            if (i == 0) o[0] = a + b + c + d + e;
        }

        static void UnusedSignatureStreamKernel(ArrayView<int> o, int a, int b, int c, int d, int e, long f)
        {
            if (Grid.GlobalIndex.X == 0) o[0] = a + b + c + d + e + (int)f;
        }

        static async Task ExpectSum(MemoryBuffer1D<int, Stride1D.Dense> o, int expected, string shape)
        {
            var got = (await o.CopyToHostAsync<int>())[0];
            if (got != expected) throw new Exception($"{shape}: got {got}, expected {expected}");
        }

        [TestMethod]
        public async Task KernelArity_1_AutoGrouped_8Params() => await RunTest(async accelerator =>
        {
            // Index1D + view + 7 ints: a 9-parameter delegate.
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int, int, int, int, int, int>(Arity7Kernel);
            k(1, o.View, 1, 2, 3, 4, 5, 6, 7);
            await ExpectSum(o, 28, "AutoGrouped Index1D + 8 params");
        });

        [TestMethod]
        public async Task KernelArity_2_Stream_8Params() => await RunTest(async accelerator =>
        {
            // KernelConfig + view + 7 ints: a 9-parameter delegate.
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadStreamKernel<ArrayView<int>, int, int, int, int, int, int, int>(Arity7StreamKernel);
            k(new KernelConfig(1, 32), o.View, 1, 2, 3, 4, 5, 6, 7);
            await ExpectSum(o, 28, "Stream KernelConfig + 8 params");
        });

        [TestMethod]
        public async Task KernelArity_3_AutoGrouped_9Params() => await RunTest(async accelerator =>
        {
            // Index1D + view + 8 ints: a 10-parameter delegate.
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int, int, int, int, int, int, int>(Arity8Kernel);
            k(1, o.View, 1, 2, 3, 4, 5, 6, 7, 8);
            await ExpectSum(o, 36, "AutoGrouped Index1D + 9 params");
        });

        [TestMethod]
        public async Task KernelArity_4_Stream_9Params() => await RunTest(async accelerator =>
        {
            // KernelConfig + view + 8 ints: a 10-parameter delegate (the FusedFFN_TiledLinearGELU shape).
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadStreamKernel<ArrayView<int>, int, int, int, int, int, int, int, int>(Arity8StreamKernel);
            k(new KernelConfig(1, 32), o.View, 1, 2, 3, 4, 5, 6, 7, 8);
            await ExpectSum(o, 36, "Stream KernelConfig + 9 params");
        });

        [TestMethod]
        public async Task KernelArity_5_Stream_12Params() => await RunTest(async accelerator =>
        {
            // KernelConfig + view + 11 ints: a 13-parameter delegate.
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadStreamKernel<ArrayView<int>, int, int, int, int, int, int, int, int, int, int, int>(Arity11StreamKernel);
            k(new KernelConfig(1, 32), o.View, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
            await ExpectSum(o, 66, "Stream KernelConfig + 12 params");
        });

        // The launcher the browser backends build is (Kernel, AcceleratorStream, index, kernel params...): 3 + the
        // kernel's parameter count. 6 and 7 sit either side of Mono's 8-argument interpreter entry limit.
        [TestMethod]
        public async Task KernelArity_6_Stream_5Params() => await RunTest(async accelerator =>
        {
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadStreamKernel<ArrayView<int>, int, int, int, int>(Arity4StreamKernel);
            k(new KernelConfig(1, 32), o.View, 1, 2, 3, 4);
            await ExpectSum(o, 10, "Stream KernelConfig + 5 params");
        });

        [TestMethod]
        public async Task KernelArity_7_Stream_6Params() => await RunTest(async accelerator =>
        {
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadStreamKernel<ArrayView<int>, int, int, int, int, int>(Arity5StreamKernel);
            k(new KernelConfig(1, 32), o.View, 1, 2, 3, 4, 5);
            await ExpectSum(o, 15, "Stream KernelConfig + 6 params");
        });

        [TestMethod]
        public async Task KernelArity_8_AutoGrouped_6Params() => await RunTest(async accelerator =>
        {
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, int, int, int, int, int>(Arity5Kernel);
            k(1, o.View, 1, 2, 3, 4, 5);
            await ExpectSum(o, 15, "AutoGrouped Index1D + 6 params");
        });

        [TestMethod]
        public async Task KernelArity_9_LoadKernel_8Params() => await RunTest(async accelerator =>
        {
            // The launcher delegate itself, called from test code: no LoadStreamKernel closure in between.
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadKernel<ArrayView<int>, int, int, int, int, int, int, int>(Arity7StreamKernel);
            k(accelerator.DefaultStream, new KernelConfig(1, 32), o.View, 1, 2, 3, 4, 5, 6, 7);
            await ExpectSum(o, 28, "LoadKernel stream + KernelConfig + 8 params");
        });

        [TestMethod]
        public async Task KernelArity_10_KernelLaunch_8Params() => await RunTest(async accelerator =>
        {
            // Kernel.Launch with boxed arguments: the launcher is invoked by reflection, not through a typed delegate.
            using var o = accelerator.Allocate1D<int>(1);
            var kernel = accelerator.LoadKernel(((Action<ArrayView<int>, int, int, int, int, int, int, int>)Arity7StreamKernel).Method);
            ArrayView<int> view = o.View; // the kernel parameter type exactly: Launch does not convert boxed arguments
            kernel.Launch(accelerator.DefaultStream, new KernelConfig(1, 32), view, 1, 2, 3, 4, 5, 6, 7);
            await ExpectSum(o, 28, "Kernel.Launch boxed + KernelConfig + 8 params");
        });

        [TestMethod]
        public async Task KernelArity_11_Stream_UnusedSignature_7Params() => await RunTest(async accelerator =>
        {
            // A delegate signature (ends in a long) that nothing else in the app invokes, so the AOT compiler built no
            // wrapper for it. Before the boxed launch path, merely LOADING this kernel through LoadStreamKernel killed
            // the runtime (creating the launcher delegate is what needs the wrapper).
            using var o = accelerator.Allocate1D<int>(1);
            var k = accelerator.LoadStreamKernel<ArrayView<int>, int, int, int, int, int, long>(UnusedSignatureStreamKernel);
            k(new KernelConfig(1, 32), o.View, 1, 2, 3, 4, 5, 6L);
            await ExpectSum(o, 21, "Stream KernelConfig + 7 params, unused signature");
        });
    }
}
