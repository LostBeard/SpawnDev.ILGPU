using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-22 (SpawnDev.Codecs Av1CoefRoundTripKernel, found by Geordi): a kernel that passes
    // `view.SubView(k, 1)` of a view PARAMETER to a multi-block helper failed WebGPU shader validation with
    // "unresolved value 'v_N'". GenerateCode(SubViewValue) emits `let v_N = v_param;` and registers v_N in
    // declaredVariables; the multi-block post-pass then turned every `let v_N = expr;` into an assignment unless
    // the RHS was a KNOWN pointer alias, and only struct-field view pointers were known - so v_N was assigned and
    // never declared. Present since at least 5.3.1. Fix: the post-pass learns every `let v_N = &x;` alias.
    // The same kernel is clean on CPU/CUDA/OpenCL/Wasm/Velocity; the WebGPU lanes are the ones that turn red.
    public abstract partial class BackendTestBase
    {
        // A multi-block helper (loop + branch) writing through the whole view AND through a SubView of it.
        private static void SubViewParamHelper(ArrayView<int> input, ArrayView<int> info, ArrayView<int> sub, int n)
        {
            int acc = 0;
            for (int k = 0; k < n; k++)
            {
                if ((input[k] & 1) != 0) acc += input[k];
                else acc -= 1;
            }
            info[0] = acc;
            sub[0] = acc * 2;
        }

        private static void SubViewOfViewParamKernel(Index1D _, ArrayView<int> input, ArrayView<int> info, int n)
        {
            SubViewParamHelper(input, info, info.SubView(2, 1), n);
            SubViewParamHelper(input, info.SubView(1, 1), info.SubView(3, 1), n);
        }

        [TestMethod]
        public async Task SubViewOfViewParam_PassedToMultiBlockHelper_MatchesCpuOracle() => await RunTest(async accelerator =>
        {
            // WebGL writes one output element per thread at its own slot; this kernel stores to four elements.
            if (accelerator.AcceleratorType == AcceleratorType.WebGL)
                throw new UnsupportedTestException("WebGL: in-kernel multi-store per thread is not supported.");

            const int N = 7;
            var input = new int[] { 3, -4, 9, 10, -7, 2, 5 };
            int expected = 0;
            for (int k = 0; k < N; k++)
                expected += (input[k] & 1) != 0 ? input[k] : -1;

            using var inBuf = accelerator.Allocate1D(input);
            using var infoBuf = accelerator.Allocate1D(new int[4]);

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<int>, int>(
                SubViewOfViewParamKernel);
            kernel(1, inBuf.View, infoBuf.View, N);
            await accelerator.SynchronizeAsync();
            var info = await infoBuf.CopyToHostAsync<int>();

            var want = new[] { expected, expected, expected * 2, expected * 2 };
            for (int i = 0; i < want.Length; i++)
                if (info[i] != want[i])
                    throw new Exception($"SubViewOfViewParam mismatch at info[{i}]: expected {want[i]}, got {info[i]} (all: {string.Join(",", info)}).");
        });
    }
}
