using ILGPU;
using ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // Part: reading a VIEW back into JS, and the readbacks that take the native buffer behind a view.
    //
    // Lore (SpawnGen, 2026-11-20): an API returned an ArrayView1D<byte, Stride1D.Dense> into a buffer it owns, with no
    // MemoryBuffer1D to call CopyToHostUint8ArrayAsync on. The workaround, ((IContiguousArrayView)view.BaseView).Buffer
    // .CopyToHostUint8ArrayAsync(offset, length), threw InvalidCastException: the MemoryBuffer readbacks cast the buffer
    // to IArrayView, which a MemoryBuffer1D is and the native buffer behind a view is not. Now:
    //  - ArrayView<T> / ArrayView1D<T, Dense>.CopyToHostUint8ArrayAsync() copies exactly the view;
    //  - the MemoryBuffer readbacks take the native buffer too;
    //  - the Uint8Array / TypedArray readbacks return an owned copy on Wasm and WebGL as well (they returned a view of the
    //    buffer's SharedArrayBuffer / CPU mirror, which later writes to the buffer changed: step 4 failed on WebGL first).
    public abstract partial class BackendTestBase
    {
        [TestMethod]
        public async Task ViewReadback_Uint8Array_ExactBytes_OwnedCopy() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is not (AcceleratorType.WebGPU or AcceleratorType.WebGL or AcceleratorType.Wasm))
                throw new UnsupportedTestException("Uint8Array readback is a browser API");

            var data = new byte[1000];
            for (int i = 0; i < data.Length; i++) data[i] = (byte)(i * 7 + 3);
            using var buffer = accelerator.Allocate1D(data);
            var ints = new int[64];
            for (int i = 0; i < ints.Length; i++) ints[i] = i * 1000003 - 7;
            using var intBuffer = accelerator.Allocate1D(ints);

            static void Check(string what, byte[] got, ReadOnlySpan<byte> want)
            {
                if (got.Length != want.Length)
                    throw new Exception($"{what}: {got.Length} bytes, expected {want.Length}");
                for (int i = 0; i < want.Length; i++)
                    if (got[i] != want[i])
                        throw new Exception($"{what}: byte {i} is {got[i]}, expected {want[i]}");
            }

            // 1) a byte sub-view, through the new view overload
            ArrayView1D<byte, Stride1D.Dense> view = buffer.View.SubView(123, 456);
            using var u8 = await view.CopyToHostUint8ArrayAsync();
            Check("byte sub-view", u8.ReadBytes(), data.AsSpan(123, 456));

            // 2) an int sub-view: its raw bytes
            ArrayView1D<int, Stride1D.Dense> intView = intBuffer.View.SubView(10, 20);
            using var u8i = await intView.CopyToHostUint8ArrayAsync();
            Check("int sub-view", u8i.ReadBytes(), System.Runtime.InteropServices.MemoryMarshal.AsBytes(ints.AsSpan(10, 20)));

            // 3) Lore's call: the native buffer behind the view (was InvalidCastException)
            var contiguous = (IContiguousArrayView)view.BaseView;
            using var u8n = await contiguous.Buffer.CopyToHostUint8ArrayAsync(contiguous.IndexInBytes, view.Length);
            Check("native buffer behind the view", u8n.ReadBytes(), data.AsSpan(123, 456));

            // 3b) the interface method called directly on the backend buffer (what SpawnDev.Codecs' DeviceTransfer called;
            //     on 5.4.41 it was still a view of the buffer's memory on Wasm and WebGL: Geordi, 2026-11-20)
            var browserBuffer = (IBrowserMemoryBuffer)contiguous.Buffer;
            using var u8direct = await browserBuffer.CopyToHostUint8ArrayAsync(contiguous.IndexInBytes, view.Length);
            Check("IBrowserMemoryBuffer.CopyToHostUint8ArrayAsync", u8direct.ReadBytes(), data.AsSpan(123, 456));

            // 4) owned copies: overwrite the device buffer, the results read before stay as they were
            await buffer.View.MemSetToZeroAsync(accelerator.DefaultStream);
            await accelerator.SynchronizeAsync();
            var zeros = await buffer.CopyToHostAsync();
            if (zeros[200] != 0) throw new Exception("the buffer was not cleared");
            Check("view result after the buffer was overwritten", u8.ReadBytes(), data.AsSpan(123, 456));
            Check("native-buffer result after the buffer was overwritten", u8n.ReadBytes(), data.AsSpan(123, 456));
            Check("IBrowserMemoryBuffer result after the buffer was overwritten", u8direct.ReadBytes(), data.AsSpan(123, 456));
        });

        [TestMethod]
        public async Task NativeBufferBehindView_CopyToHostAsync_CountsInT() => await RunTest(async accelerator =>
        {
            var ints = new int[300];
            for (int i = 0; i < ints.Length; i++) ints[i] = i * 31 - 4000;
            using var buffer = accelerator.Allocate1D(ints);
            var contiguous = (IContiguousArrayView)buffer.View.SubView(17, 50).BaseView;
            // the native buffer (no MemoryBuffer1D wrapper): was InvalidCastException on the browser backends
            var all = await contiguous.Buffer.CopyToHostAsync<int>();
            if (all.Length != ints.Length)
                throw new Exception($"{accelerator.AcceleratorType}: {all.Length} ints from the native buffer, expected {ints.Length}");
            for (int i = 0; i < ints.Length; i++)
                if (all[i] != ints[i])
                    throw new Exception($"{accelerator.AcceleratorType}: int {i} is {all[i]}, expected {ints[i]}");
        });
    }
}
