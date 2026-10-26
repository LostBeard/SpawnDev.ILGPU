using ILGPU;
using ILGPU.Runtime;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // `view.Length` of a view parameter that WebGPU gathered into a coalesced binding (over 10 storage bindings).
    // arrayLength() of the shared binding is the SUM of all members, so every coalesced input reported the total
    // (silent wrong values; the generator's v1 comment said so). A member whose length is read now gets its own
    // element-count slot. Different lengths per input, int and byte (sub-word) views.
    public abstract partial class BackendTestBase
    {
        static void CoalescedLength_IntKernel(Index1D idx, ArrayView<int> output,
            ArrayView<int> v0, ArrayView<int> v1, ArrayView<int> v2, ArrayView<int> v3, ArrayView<int> v4,
            ArrayView<int> v5, ArrayView<int> v6, ArrayView<int> v7, ArrayView<int> v8, ArrayView<int> v9)
        {
            if (idx != 0) return;
            output[0] = v0.IntLength; output[1] = v1.IntLength; output[2] = v2.IntLength; output[3] = v3.IntLength;
            output[4] = v4.IntLength; output[5] = v5.IntLength; output[6] = v6.IntLength; output[7] = v7.IntLength;
            output[8] = v8.IntLength; output[9] = v9.IntLength;
            output[10] = v9[v9.IntLength - 1];
        }

        static void CoalescedLength_ByteKernel(Index1D idx, ArrayView<int> output,
            ArrayView<byte> v0, ArrayView<byte> v1, ArrayView<byte> v2, ArrayView<byte> v3, ArrayView<byte> v4,
            ArrayView<byte> v5, ArrayView<byte> v6, ArrayView<byte> v7, ArrayView<byte> v8, ArrayView<byte> v9)
        {
            if (idx != 0) return;
            output[0] = v0.IntLength; output[1] = v1.IntLength; output[2] = v2.IntLength; output[3] = v3.IntLength;
            output[4] = v4.IntLength; output[5] = v5.IntLength; output[6] = v6.IntLength; output[7] = v7.IntLength;
            output[8] = v8.IntLength; output[9] = v9.IntLength;
            output[10] = v9[v9.IntLength - 1];
        }

        [TestMethod]
        public async Task Coalesce_ReadLengthOfCoalescedInputs_IsEachInputsLength() => await RunEmulatedTest(async accelerator =>
        {
            int[] lengths = { 3, 17, 4, 9, 1, 30, 12, 5, 21, 7 };
            var ints = new MemoryBuffer1D<int, Stride1D.Dense>[10];
            var bytes = new MemoryBuffer1D<byte, Stride1D.Dense>[10];
            try
            {
                for (int f = 0; f < 10; f++)
                {
                    var iData = new int[lengths[f]];
                    var bData = new byte[lengths[f]];
                    for (int i = 0; i < lengths[f]; i++) { iData[i] = f * 100 + i; bData[i] = (byte)(f * 20 + i); }
                    ints[f] = accelerator.Allocate1D(iData);
                    bytes[f] = accelerator.Allocate1D(bData);
                }
                using var outInt = accelerator.Allocate1D<int>(11);
                using var outByte = accelerator.Allocate1D<int>(11);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>,
                    ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>, ArrayView<int>>(CoalescedLength_IntKernel)(
                    (Index1D)1, outInt.View, ints[0].View, ints[1].View, ints[2].View, ints[3].View, ints[4].View,
                    ints[5].View, ints[6].View, ints[7].View, ints[8].View, ints[9].View);
                accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>,
                    ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>,
                    ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>, ArrayView<byte>>(CoalescedLength_ByteKernel)(
                    (Index1D)1, outByte.View, bytes[0].View, bytes[1].View, bytes[2].View, bytes[3].View, bytes[4].View,
                    bytes[5].View, bytes[6].View, bytes[7].View, bytes[8].View, bytes[9].View);
                await accelerator.SynchronizeAsync();
                var gotInt = await outInt.CopyToHostAsync<int>();
                var gotByte = await outByte.CopyToHostAsync<int>();
                for (int f = 0; f < 10; f++)
                {
                    if (gotInt[f] != lengths[f]) throw new Exception($"int input {f}: IntLength {gotInt[f]}, expected {lengths[f]}");
                    if (gotByte[f] != lengths[f]) throw new Exception($"byte input {f}: IntLength {gotByte[f]}, expected {lengths[f]}");
                }
                if (gotInt[10] != 900 + lengths[9] - 1) throw new Exception($"int last element {gotInt[10]}, expected {900 + lengths[9] - 1}");
                if (gotByte[10] != (byte)(180 + lengths[9] - 1)) throw new Exception($"byte last element {gotByte[10]}, expected {(byte)(180 + lengths[9] - 1)}");
            }
            finally
            {
                for (int f = 0; f < 10; f++) { ints[f]?.Dispose(); bytes[f]?.Dispose(); }
            }
        });
    }
}
