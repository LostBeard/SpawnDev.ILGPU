using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    /// <summary>
    /// Creating a WebGPU compute shader on a device that cannot be used must throw a NAMED exception, not a bare
    /// NullReferenceException.
    /// </summary>
    /// <remarks>
    /// ⚠️ WHY (2026-11-06). SpawnDev.ILGPU.ML's full sweep had one intermittent failure ~2,250 tests into the shared-page
    /// WebGPU lane: a NullReferenceException thrown from WebGPUComputeShader's constructor, with nothing below it. A disposed
    /// SpawnJS wrapper has a null JSRef, every GPU wrapper method is <c>JSRef!.Call(...)</c>, and the interpreter inlines those
    /// one-liners - so a dead GPUDevice (or a null pipeline) surfaced as an NRE naming only the constructor. It did not
    /// reproduce alone. The constructor now goes through <c>WebGPUNativeAccelerator.RequireDevice</c> and checks each interop
    /// result, so the next occurrence says what was gone. A disposed accelerator is the one deterministic way to reach that
    /// path: Dispose clears the shader cache, so the next launch has to create a shader.
    /// </remarks>
    public abstract partial class BackendTestBase
    {
        static void DisposedShaderKernel(Index1D i, ArrayView<int> o) => o[i] = i * 3;

        [TestMethod(Timeout = 120000)]
        public async Task WebGPU_DisposedAccelerator_ShaderCreationThrowsObjectDisposed()
        {
            // NOT RunTest: the test disposes its own accelerator.
            var (context, accelerator) = await CreateAcceleratorAsync();
            try
            {
                if (accelerator is not WebGPUAccelerator)
                    throw new UnsupportedTestException($"{accelerator.AcceleratorType}: WebGPU shader creation only");
                var buffer = accelerator.Allocate1D<int>(64);
                var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>>(DisposedShaderKernel);
                accelerator.Dispose();

                Exception? thrown = null;
                try { kernel((Index1D)64, buffer.View); }
                catch (Exception e) { thrown = e; }

                var root = thrown;
                while (root is System.Reflection.TargetInvocationException { InnerException: { } inner }) root = inner;
                if (root is not ObjectDisposedException)
                    throw new Exception("Launching on a disposed WebGPU accelerator should throw ObjectDisposedException, got "
                        + (root == null ? "no exception" : $"{root.GetType().Name}: {root.Message}"));
                Console.WriteLine($"[DisposedShader] {root.Message}");
            }
            finally
            {
                try { accelerator.Dispose(); } catch { }
                try { context.Dispose(); } catch { }
            }
        }
    }
}
