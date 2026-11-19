using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // A kernel that calls a method with no IL body and no intrinsic / remap (an InternalCall, like MathF.Cbrt was before
    // 5.4.38) cannot run on a device. Velocity, Wasm, CUDA and OpenCL fail to compile it; WebGPU and WebGL emitted the call
    // as a ZERO ("// Call: X (Unmapped)") and ran: the unremapped MathF.Cbrt came out 0 on WebGPU instead of an error
    // (Data 2026-11-18). Every backend must fail loudly: compiling, launching or synchronizing throws. A run that finishes
    // is the bug. (No suite kernel reached the fallback: kernel-dump --all-browser, 829 kernels, 2026-11-18.)
    public abstract partial class BackendTestBase
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        static extern float UnimplementableInternalCall(float x);

        static void UnimplementableCallKernel(Index1D i, ArrayView<float> x, ArrayView<float> y) =>
            y[i] = UnimplementableInternalCall(x[i]) + 1.0f;

        [TestMethod]
        public async Task Kernel_CallingAMethodWithoutBody_FailsLoudly() => await RunTest(async accelerator =>
        {
            const int n = 64;
            using var x = accelerator.Allocate1D<float>(n);
            using var y = accelerator.Allocate1D<float>(n);
            Exception? error = null;
            float[]? got = null;
            try
            {
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>>(UnimplementableCallKernel);
                kernel(n, x.View, y.View);
                await accelerator.SynchronizeAsync();
                got = await y.CopyToHostAsync<float>();
            }
            catch (Exception e)
            {
                error = e;
            }
            if (error == null)
                throw new Exception($"{accelerator.AcceleratorType}: a kernel calling a method without a body compiled and ran " +
                    $"(y[0] = {got?[0]:R}); it must fail to compile");
            Console.WriteLine($"[report] {accelerator.AcceleratorType}: {error.GetType().Name}: {error.Message.Split('\n')[0]}");
        });
    }
}
