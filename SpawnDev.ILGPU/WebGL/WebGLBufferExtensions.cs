// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.WebGL
//                 WebGL2 Compute Library for Blazor WebAssembly
//
// File: WebGLBufferExtensions.cs
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.ILGPU.WebGL.Backend;
using System.Runtime.InteropServices;

namespace SpawnDev.ILGPU.WebGL
{
    /// <summary>
    /// Extension methods for WebGL2 buffer operations.
    /// </summary>
    public static class WebGLBufferExtensions
    {
        ///// <summary>
        ///// Copies buffer data to a host array asynchronously.
        ///// For WebGL2, this is synchronous since data is CPU-resident, but
        ///// the async API is provided for interface compatibility with WebGPU.
        ///// </summary>
        //public static Task<T[]> CopyToHostAsync<T>(this MemoryBuffer1D<T, Stride1D.Dense> buffer)
        //    where T : unmanaged
        //{
        //    var internalBuffer = GetWebGLBuffer((MemoryBuffer)buffer);
        //    return Task.FromResult(internalBuffer.BackingArray!.Read<T>(0, buffer.Length));
        //}

        ///// <summary>
        ///// Copies data from the buffer back to the host asynchronously.
        ///// This overload works with any MemoryBuffer (for 2D, 3D, etc.).
        ///// </summary>
        //public static Task<T[]> CopyToHostAsync<T>(this MemoryBuffer buffer) where T : unmanaged
        //{
        //    var internalBuffer = GetWebGLBuffer(buffer);
        //    return Task.FromResult(internalBuffer.BackingArray!.Read<T>(0, buffer.Length));
        //}

        ///// <summary>
        ///// Copies data from the buffer back to the host as a TypedArray asynchronously.
        ///// </summary>
        ///// <typeparam name="T"></typeparam>
        ///// <param name="buffer"></param>
        ///// <returns></returns>
        //public static Task<T> CopyToHostTypeArrayAsync<T>(this MemoryBuffer buffer) where T : TypedArray
        //{
        //    var internalBuffer = GetWebGLBuffer(buffer);
        //    return Task.FromResult(internalBuffer.BackingArray!.ReCast<T>());
        //}

        /// <summary>
        /// Gets the underlying WebGLMemoryBuffer from an ILGPU MemoryBuffer.
        /// </summary>
        private static WebGLMemoryBuffer GetWebGLBuffer(MemoryBuffer buffer)
        {
            var iView = (IArrayView)buffer;
            if (iView.Buffer is WebGLMemoryBuffer webGlBuffer)
                return webGlBuffer;
            throw new InvalidOperationException(
                "CopyToHostAsync is only supported for WebGL-backed buffers. " +
                "The buffer must be allocated on a WebGLAccelerator.");
        }
    }
}
