using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // ShaderCompiler.Generate on a kernel whose parameter is a DelegateSpecialization<TDelegate> (SpawnDev.ILGPU.ML
    // ElementWiseKernels.BroadcastBinaryKernel shape, an offline screen of every ML kernel, 2026-11-10). Resolving the
    // parameter's type walked a delegate's fields, which reach the delegate's own types again, and the recursion overflowed
    // the stack: the PROCESS died. ILGPU's TypeInformationManager now stops at a type it is already resolving; the
    // kernel itself is still not generatable without a specialization value, so a managed exception is the right answer.
    public abstract partial class BackendTestBase
    {
        static void ReferenceCycleDelegateKernel(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> o,
            DelegateSpecialization<Func<float, float, float>> op) => o[i] = op.Value(a[i], b[i]);

        [TestMethod]
        public async Task ShaderCompiler_DelegateSpecializationParameter_ThrowsInsteadOfStackOverflow() => await RunTest(async accelerator =>
        {
            var method = typeof(BackendTestBase).GetMethod(nameof(ReferenceCycleDelegateKernel), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Exception? caught = null;
            try { ShaderCompiler.Generate(method!, CapabilityProfiles.WebGPUBaseline); }
            catch (Exception e) { caught = e; }
            // reaching this line at all is the test (the old failure was a stack overflow, which no catch can see)
            if (caught == null)
                throw new Exception("ShaderCompiler.Generate produced a shader for a kernel with an unspecialized delegate parameter; expected a managed exception");
            var inner = caught;
            while (inner.InnerException != null) inner = inner.InnerException;
            if (inner is not NotSupportedException)
                throw new Exception($"expected NotSupportedException at the root, got {inner.GetType().Name}: {inner.Message}");
            await Task.CompletedTask;
        });
    }
}
