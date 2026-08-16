using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.WebGPU.Backend;
using System.Runtime.InteropServices;

namespace SpawnDev.ILGPU.WebGPU
{
    /// <summary>
    /// Extension methods for ILGPU buffer types to support WebGPU-specific operations:
    /// async readback, native buffer access, and GPU→GPU copy.
    /// </summary>
    public static class WebGPUBufferExtensions
    {
        /// <summary>
        /// Copies a sub-range of data from the GPU buffer back to the host asynchronously.
        /// WebGPU-optimized: uses staging buffer to copy only the requested range (unlike the
        /// cross-platform version which reads the entire buffer then slices).
        /// </summary>
        /// <param name="buffer">The source GPU buffer.</param>
        /// <param name="sourceOffset">Offset in elements from the start of the GPU buffer.</param>
        /// <param name="length">Number of elements to copy.</param>
        public static async Task<T[]> CopyToHostRangeAsync<T>(this MemoryBuffer1D<T, Stride1D.Dense> buffer, long sourceOffset, long length) where T : unmanaged
        {
            var result = new T[length];
            await CopyToHostAsync(buffer, result, sourceOffset, length);
            return result;
        }

        /// <summary>
        /// Copies GPU data into a caller-provided array, reusing a cached staging buffer.
        /// Zero-allocation hot path for rendering loops.
        /// </summary>
        /// <param name="buffer">The source GPU buffer.</param>
        /// <param name="destination">Pre-allocated array to receive the data.</param>
        /// <param name="sourceOffset">Offset in elements from the start of the GPU buffer.</param>
        /// <param name="count">Number of elements to copy. If null, copies as many as will fit in destination.</param>
        public static async Task CopyToHostAsync<T>(this MemoryBuffer1D<T, Stride1D.Dense> buffer, T[] destination, long sourceOffset = 0, long? count = null) where T : unmanaged
        {
            var webGpuBuffer = buffer.GetWebGPUMemoryBuffer();
            var elementSize = Marshal.SizeOf<T>();
            var copyCount = count ?? Math.Min(destination.Length, buffer.Length - sourceOffset);
            // NativeBuffer is WebGPUBuffer<byte>, use generic overload to reinterpret as T
            await webGpuBuffer.NativeBuffer.CopyToHostAsync<T>(destination, sourceOffset, copyCount, elementSize);
        }

        /// <summary>
        /// Gets the underlying WebGPUMemoryBuffer from an ILGPU MemoryBuffer1D.
        /// Provides access to NativeBuffer and other WebGPU-specific functionality.
        /// </summary>
        public static WebGPUMemoryBuffer GetWebGPUMemoryBuffer<T>(this MemoryBuffer1D<T, Stride1D.Dense> buffer) where T : unmanaged
            => GetWebGPUMemoryBuffer((MemoryBuffer)buffer);

        /// <summary>
        /// Gets the underlying WebGPUMemoryBuffer from an ILGPU MemoryBuffer.
        /// Provides access to NativeBuffer and other WebGPU-specific functionality.
        /// </summary>
        public static WebGPUMemoryBuffer GetWebGPUMemoryBuffer(this MemoryBuffer buffer)
        {
            var iView = (IArrayView)buffer;
            if (iView.Buffer is WebGPUMemoryBuffer webGpuBuffer)
                return webGpuBuffer;
            throw new InvalidOperationException("Only supported for WebGPU-backed buffers. The buffer must be allocated on a WebGPUAccelerator.");
        }

        /// <summary>
        /// Gets the native WebGPU GPUBuffer from an ILGPU MemoryBuffer1D.
        /// Shortcut for buffer.GetWebGPUMemoryBuffer().NativeBuffer.NativeBuffer.
        /// Returns null if the buffer is not WebGPU-backed.
        /// </summary>
        public static GPUBuffer? GetGPUBuffer<T>(this MemoryBuffer1D<T, Stride1D.Dense> buffer) where T : unmanaged
            => GetGPUBuffer((MemoryBuffer)buffer);

        /// <summary>
        /// Gets the native WebGPU GPUBuffer from an ILGPU MemoryBuffer.
        /// Returns null if the buffer is not WebGPU-backed.
        /// </summary>
        public static GPUBuffer? GetGPUBuffer(this MemoryBuffer buffer)
        {
            var iView = (IArrayView)buffer;
            if (iView.Buffer is WebGPUMemoryBuffer webGpuMem)
                return webGpuMem.NativeBuffer?.NativeBuffer;
            return null;
        }
    }
}
