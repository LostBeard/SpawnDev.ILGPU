using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Regression guard for the WebGL special-float scalar-param bug (Tuvok's constantofshape_neginf,
    // TJ-diagnosed). WebGL passes scalar uniform values inside the worker.postMessage(dispatchMsg)
    // object, which is marshaled .NET→JS via System.Text.Json (RuntimeJsonSerializerOptions). That
    // serializer REJECTS float ±inf/NaN (SpecialNumberValuesNotSupported), so a kernel with a ±inf/NaN
    // float scalar param threw on WebGL ONLY (WebGPU/Wasm/desktop pack/pass scalars differently). Fixed
    // by sending float scalars as their int32 BIT PATTERN (WebGLAccelerator.EncodeUniformScalarValue +
    // glWorker _bitsToFloat). This test fills an output buffer from a scalar param and checks the value
    // round-trips on every backend — it FAILED on WebGL pre-fix.
    public abstract partial class BackendTestBase
    {
        private static void ScalarFillKernel(Index1D i, ArrayView<float> output, float fillValue)
        {
            output[i] = fillValue;
        }

        [TestMethod]
        public async Task ScalarParam_SpecialFloatValues_RoundTrip() => await RunTest(async accelerator =>
        {
            const int n = 16;
            var cases = new (string name, float fill)[]
            {
                ("-inf", float.NegativeInfinity),
                ("+inf", float.PositiveInfinity),
                ("NaN",  float.NaN),
                ("finite", 3.5f),
            };

            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float>(ScalarFillKernel);

            foreach (var (name, fill) in cases)
            {
                using var outBuf = accelerator.Allocate1D<float>(n);
                kernel((Index1D)n, outBuf.View, fill);
                await accelerator.SynchronizeAsync();
                var r = await outBuf.CopyToHostAsync<float>();

                for (int k = 0; k < n; k++)
                {
                    bool ok = float.IsNaN(fill) ? float.IsNaN(r[k]) : (r[k] == fill);
                    if (!ok)
                        throw new Exception(
                            $"Scalar special-float '{name}': output[{k}]={r[k]} (bits 0x{BitConverter.SingleToUInt32Bits(r[k]):X8}), " +
                            $"expected {fill} (bits 0x{BitConverter.SingleToUInt32Bits(fill):X8}).");
                }
            }
        });
    }
}
