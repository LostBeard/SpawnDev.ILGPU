using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.SpawnJS.Toolbox;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // CopyToStreamAsync: stream a GPU buffer's bytes OUT to a .NET Stream - the save-side mirror of
    // CopyFromStream. These exercise the backend-agnostic DEFAULT path (async CopyToRawAsync -> Stream
    // WriteAsync), which runs on every backend including the browser ones (via the managed hop). The browser
    // zero-copy IJSWriteStream -> WriteUint8ArrayAsync override is covered separately by browser-only tests.
    public abstract partial class BackendTestBase
    {
        // FloatsToBytes is defined in the CopyFromStream partial (same partial class).
        static float[] BytesToFloats(byte[] bytes)
        {
            var values = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, values, 0, values.Length * sizeof(float));
            return values;
        }

        /// <summary>Single-chunk save to a MemoryStream, full round-trip vs the CPU values.</summary>
        [TestMethod]
        public async Task CopyToStreamFloatTest() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var expected = new float[n];
            for (int i = 0; i < n; i++)
                expected[i] = i * 1.5f - 3.0f;

            using var buf = accelerator.Allocate1D(expected);
            await accelerator.SynchronizeAsync();

            using var ms = new MemoryStream();
            await buf.View.CopyToStreamAsync(ms);

            var got = BytesToFloats(ms.ToArray());
            if (got.Length != n)
                throw new Exception($"CopyToStream produced {got.Length} floats, expected {n}");
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"CopyToStream mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });

        /// <summary>A small chunk size forces the multi-chunk loop (readback/write several times).</summary>
        [TestMethod]
        public async Task CopyToStreamMultiChunkTest() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var expected = new float[n];
            for (int i = 0; i < n; i++)
                expected[i] = MathF.Sin(i * 0.01f);

            using var buf = accelerator.Allocate1D(expected);
            await accelerator.SynchronizeAsync();

            using var ms = new MemoryStream();
            // 1024-byte chunks over 16384 bytes => 16 chunks.
            await buf.View.CopyToStreamAsync(ms, chunkSizeInBytes: 1024);

            var got = BytesToFloats(ms.ToArray());
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"CopyToStream multi-chunk mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });

        /// <summary>Save from a sub-range (non-zero source offset) writes exactly that range.</summary>
        [TestMethod]
        public async Task CopyToStreamSubViewTest() => await RunTest(async accelerator =>
        {
            const int n = 1024;
            const int start = 256;
            const int count = 512;
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = i + 100.0f;

            using var buf = accelerator.Allocate1D(data);
            await accelerator.SynchronizeAsync();

            using var ms = new MemoryStream();
            await buf.View.SubView(start, count).CopyToStreamAsync(ms);

            var got = BytesToFloats(ms.ToArray());
            if (got.Length != count)
                throw new Exception($"CopyToStream sub-view produced {got.Length} floats, expected {count}");
            for (int i = 0; i < count; i++)
            {
                float expected = (start + i) + 100.0f;
                if (got[i] != expected)
                    throw new Exception($"CopyToStream sub-view mismatch at [{i}]: expected {expected}, got {got[i]}");
            }
        });

        /// <summary>CopyFromStream INTO a buffer then CopyToStream back OUT must reproduce the bytes exactly.</summary>
        [TestMethod]
        public async Task CopyToStreamRoundTripTest() => await RunTest(async accelerator =>
        {
            const int n = 2048;
            var expected = new float[n];
            for (int i = 0; i < n; i++)
                expected[i] = i * 0.3f - 5.0f;

            using var buf = accelerator.Allocate1D<float>(n);
            using var msIn = new MemoryStream(FloatsToBytes(expected));
            await buf.View.CopyFromStreamAsync(msIn);
            await accelerator.SynchronizeAsync();

            using var msOut = new MemoryStream();
            await buf.View.CopyToStreamAsync(msOut);

            var got = BytesToFloats(msOut.ToArray());
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"CopyToStream round-trip mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });

        /// <summary>Browser zero-copy save path: GPU -> IJSWriteStream (ArrayBufferStream) -> read back. 4-byte aligned.</summary>
        [TestMethod]
        public async Task CopyToStreamJSWriteStreamFloatTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is AcceleratorType.CPU
                or AcceleratorType.Cuda or AcceleratorType.OpenCL)
                throw new UnsupportedTestException("Browser-only: IJSWriteStream is a JS-backed stream");

            const int n = 4096;
            var expected = new float[n];
            for (int i = 0; i < n; i++)
                expected[i] = i * 0.25f - 7.0f;

            using var buf = accelerator.Allocate1D(expected);
            await accelerator.SynchronizeAsync();

            using var jsStream = new ArrayBufferStream(n * sizeof(float));
            await buf.View.CopyToStreamAsync(jsStream);

            var got = BytesToFloats(jsStream.Source.ReadBytes());
            if (got.Length != n)
                throw new Exception($"CopyToStream(IJSWriteStream) produced {got.Length} floats, expected {n}");
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"CopyToStream(IJSWriteStream) mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });

        /// <summary>
        /// Odd-count Half save via IJSWriteStream: byte length is 2 mod 4, exercising the browser readback's
        /// non-4-aligned handling. Verifies the 2-byte Half layout round-trips GPU -> JS -> read back.
        /// </summary>
        [TestMethod]
        public async Task CopyToStreamJSWriteStreamHalfOddCountTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is AcceleratorType.CPU
                or AcceleratorType.Cuda or AcceleratorType.OpenCL)
                throw new UnsupportedTestException("Browser-only: IJSWriteStream is a JS-backed stream");
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");

            const int n = 1025; // odd -> 2050 bytes -> 2 mod 4
            var expected = new global::ILGPU.Half[n];
            for (int i = 0; i < n; i++)
                expected[i] = (global::ILGPU.Half)(float)(i - 512);

            using var buf = accelerator.Allocate1D(expected);
            await accelerator.SynchronizeAsync();

            using var jsStream = new ArrayBufferStream(n * 2);
            await buf.View.CopyToStreamAsync(jsStream);

            byte[] gotBytes = jsStream.Source.ReadBytes();
            if (gotBytes.Length != n * 2)
                throw new Exception($"CopyToStream(IJSWriteStream) Half produced {gotBytes.Length} bytes, expected {n * 2}");
            var got = MemoryMarshal.Cast<byte, global::ILGPU.Half>(gotBytes.AsSpan()).ToArray();
            for (int i = 0; i < n; i++)
                if ((float)got[i] != (float)expected[i])
                    throw new Exception($"CopyToStream(IJSWriteStream) Half mismatch at [{i}]: expected {(float)expected[i]}, got {(float)got[i]}");
        });
    }
}
