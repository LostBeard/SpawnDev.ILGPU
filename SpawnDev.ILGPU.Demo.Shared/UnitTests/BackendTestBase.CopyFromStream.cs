using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ILGPU;
using ILGPU.Runtime;
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.Shared.UnitTests
{
    // CopyFromStreamAsync: stream a .NET Stream's bytes into a GPU buffer. These exercise the
    // backend-agnostic DEFAULT path (managed ReadExactlyAsync -> CopyFrom), which runs on every
    // backend including the browser ones (via the managed hop). The browser zero-copy
    // IJSReadStream -> CopyFromJS override is covered separately by browser-only tests.
    public abstract partial class BackendTestBase
    {
        static byte[] FloatsToBytes(float[] values)
        {
            var bytes = new byte[values.Length * sizeof(float)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>Single-chunk upload from a MemoryStream, full round-trip vs the CPU values.</summary>
        [TestMethod]
        public async Task CopyFromStreamFloatTest() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var expected = new float[n];
            for (int i = 0; i < n; i++)
                expected[i] = i * 1.5f - 3.0f;

            using var buf = accelerator.Allocate1D<float>(n);
            using var ms = new MemoryStream(FloatsToBytes(expected));
            await buf.View.CopyFromStreamAsync(ms);
            await accelerator.SynchronizeAsync();

            var got = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"CopyFromStream mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });

        /// <summary>A small chunk size forces the multi-chunk loop (read/copy several times).</summary>
        [TestMethod]
        public async Task CopyFromStreamMultiChunkTest() => await RunTest(async accelerator =>
        {
            const int n = 4096;
            var expected = new float[n];
            for (int i = 0; i < n; i++)
                expected[i] = MathF.Sin(i * 0.01f);

            using var buf = accelerator.Allocate1D<float>(n);
            using var ms = new MemoryStream(FloatsToBytes(expected));
            // 1024-byte chunks over 16384 bytes => 16 chunks. (Chunk must be 4-byte aligned for the
            // browser WebGPU zero-copy path; the managed default path here has no such constraint,
            // but keeping chunks element-aligned mirrors real usage.)
            await buf.View.CopyFromStreamAsync(ms, chunkSizeInBytes: 1024);
            await accelerator.SynchronizeAsync();

            var got = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"CopyFromStream multi-chunk mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });

        /// <summary>Upload into a sub-range (non-zero target offset) leaves the rest untouched.</summary>
        [TestMethod]
        public async Task CopyFromStreamSubViewTest() => await RunTest(async accelerator =>
        {
            const int n = 1024;
            const int start = 256;
            const int count = 512;
            var initial = new float[n];
            for (int i = 0; i < n; i++) initial[i] = -1.0f;
            var payload = new float[count];
            for (int i = 0; i < count; i++) payload[i] = i + 100.0f;

            using var buf = accelerator.Allocate1D(initial);
            using var ms = new MemoryStream(FloatsToBytes(payload));
            await buf.View.SubView(start, count).CopyFromStreamAsync(ms);
            await accelerator.SynchronizeAsync();

            var got = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
            {
                float expected = (i >= start && i < start + count) ? (i - start + 100.0f) : -1.0f;
                if (got[i] != expected)
                    throw new Exception($"CopyFromStream sub-view mismatch at [{i}]: expected {expected}, got {got[i]}");
            }
        });

        /// <summary>A stream that ends early must throw, not silently zero-pad the tail.</summary>
        [TestMethod]
        public async Task CopyFromStreamThrowsOnShortStreamTest() => await RunTest(async accelerator =>
        {
            const int n = 256;
            using var buf = accelerator.Allocate1D<int>(n);
            // 4 bytes short of the buffer's byte length.
            using var ms = new MemoryStream(new byte[n * sizeof(int) - 4]);
            bool threw = false;
            try
            {
                await buf.View.CopyFromStreamAsync(ms);
            }
            catch (EndOfStreamException)
            {
                threw = true;
            }
            if (!threw)
                throw new Exception("CopyFromStream must throw EndOfStreamException on a truncated stream, not zero-pad.");
        });

        // Wraps managed bytes in a JS ArrayBufferStream (an IJSReadStream) - the browser zero-copy
        // source: CopyFromStreamAsync's browser override reads it as a Uint8Array and uploads via
        // CopyFromJS without the bytes entering the .NET/WASM managed heap.
        static ArrayBufferStream MakeJSReadStream(byte[] bytes)
        {
            var u8 = new Uint8Array(bytes.Length);
            u8.WriteBytes(bytes, 0);
            return new ArrayBufferStream(u8);
        }

        /// <summary>Browser zero-copy path: IJSReadStream -> CopyFromJS (no managed heap). 4-byte aligned.</summary>
        [TestMethod]
        public async Task CopyFromStreamJSReadStreamFloatTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is AcceleratorType.CPU
                or AcceleratorType.Cuda or AcceleratorType.OpenCL)
                throw new UnsupportedTestException("Browser-only: IJSReadStream is a JS-backed stream");

            const int n = 4096;
            var expected = new float[n];
            for (int i = 0; i < n; i++)
                expected[i] = i * 0.25f - 7.0f;

            using var buf = accelerator.Allocate1D<float>(n);
            using var jsStream = MakeJSReadStream(FloatsToBytes(expected));
            await buf.View.CopyFromStreamAsync(jsStream);
            await accelerator.SynchronizeAsync();

            var got = await buf.CopyToHostAsync<float>();
            for (int i = 0; i < n; i++)
                if (got[i] != expected[i])
                    throw new Exception($"CopyFromStream(IJSReadStream) mismatch at [{i}]: expected {expected[i]}, got {got[i]}");
        });

        /// <summary>
        /// Odd-count Half via IJSReadStream: byte length is 2 mod 4, so the WebGPU override falls
        /// back to the managed (padded) path while Wasm/WebGL upload directly - all must be correct.
        /// Also verifies the 2-byte Half layout round-trips.
        /// </summary>
        [TestMethod]
        public async Task CopyFromStreamJSReadStreamHalfOddCountTest() => await RunTest(async accelerator =>
        {
            if (accelerator.AcceleratorType is AcceleratorType.CPU
                or AcceleratorType.Cuda or AcceleratorType.OpenCL)
                throw new UnsupportedTestException("Browser-only: IJSReadStream is a JS-backed stream");
            if (!accelerator.Capabilities.Float16)
                throw new UnsupportedTestException("Float16 not supported on this device");

            const int n = 1025; // odd -> 2050 bytes -> 2 mod 4 (exercises the WebGPU 4-byte fallback)
            var expected = new global::ILGPU.Half[n];
            for (int i = 0; i < n; i++)
                expected[i] = (global::ILGPU.Half)(float)(i - 512);
            byte[] bytes = MemoryMarshal.AsBytes(expected.AsSpan()).ToArray();

            using var buf = accelerator.Allocate1D<global::ILGPU.Half>(n);
            using var jsStream = MakeJSReadStream(bytes);
            await buf.View.CopyFromStreamAsync(jsStream);
            await accelerator.SynchronizeAsync();

            var got = await buf.CopyToHostAsync<global::ILGPU.Half>();
            for (int i = 0; i < n; i++)
                if ((float)got[i] != (float)expected[i])
                    throw new Exception($"CopyFromStream(IJSReadStream) Half mismatch at [{i}]: expected {(float)expected[i]}, got {(float)got[i]}");
        });
    }
}
