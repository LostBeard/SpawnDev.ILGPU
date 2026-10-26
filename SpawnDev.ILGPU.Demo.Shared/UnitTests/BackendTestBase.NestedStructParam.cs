using System;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // 2026-10-26 (SpawnDev.Codecs SILK decoders on WebGL, found by Geordi): a kernel parameter struct holding views and
    // a NESTED struct (SilkPulsesInputs: { ArrayView<byte> Bytes; int ...; SilkShellCoderTableOffsets ShellTables;
    // ArrayView<int> Scratch; int ... }) threw InvalidCastException in WebGLAccelerator.EncodeUniformScalarValue: the
    // body-struct manifest numbers the IR's flattened leaves, the dispatcher read the struct's DIRECT fields, so it handed
    // the nested struct to the scalar encoder and every later field was one off. Both now use
    // KernelParameterKinds.FlattenUserStruct. The layout below has a view and an int after the nested struct, so a shift
    // shows as wrong values, not only as the exception.
    public abstract partial class BackendTestBase
    {
        public struct NspInner
        {
            public int A;
            public int B;
        }

        public struct NspParams
        {
            public ArrayView<int> Input;
            public int Scale;
            public NspInner Inner;
            public ArrayView<int> Output;
            public int Bias;
        }

        private static void NestedStructParamKernel(Index1D i, NspParams p)
        {
            p.Output[i] = p.Input[i] * p.Scale + p.Inner.A * 1000 + p.Inner.B * 10 + p.Bias;
        }

        [TestMethod]
        public async Task NestedStructParam_ViewsAroundNestedStruct_ReadEveryField() => await RunTest(async accelerator =>
        {
            const int N = 4096;
            var input = new int[N];
            for (int k = 0; k < N; k++) input[k] = k * 7 - 9000;
            using var inBuf = accelerator.Allocate1D(input);
            using var outBuf = accelerator.Allocate1D(new int[N]);
            var p = new NspParams
            {
                Input = inBuf.View,
                Scale = 3,
                Inner = new NspInner { A = 5, B = 7 },
                Output = outBuf.View,
                Bias = -11,
            };
            var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, NspParams>(NestedStructParamKernel);
            kernel(N, p);
            await accelerator.SynchronizeAsync();
            var got = await outBuf.CopyToHostAsync<int>();
            for (int k = 0; k < N; k++)
            {
                int want = input[k] * 3 + 5 * 1000 + 7 * 10 - 11;
                if (got[k] != want)
                    throw new Exception($"NestedStructParam: element {k}: expected {want}, got {got[k]} (Scale 3, Inner 5/7, Bias -11).");
            }
        });
    }
}
