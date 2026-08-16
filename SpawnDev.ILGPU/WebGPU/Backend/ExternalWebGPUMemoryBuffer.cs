using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// A non-owning <see cref="WebGPUMemoryBuffer"/> that wraps an externally-managed <see cref="GPUBuffer"/>.
    /// The underlying GPU buffer is NOT destroyed when this instance is disposed — the caller retains ownership.
    /// <para>
    /// Use this to pass an external GPU buffer (e.g. from ONNX Runtime Web) directly to ILGPU kernels
    /// without copying data. Both the external buffer and the accelerator must share the same GPUDevice.
    /// </para>
    /// </summary>
    public sealed class ExternalWebGPUMemoryBuffer : WebGPUMemoryBuffer
    {
        private readonly WebGPUBuffer<byte> _externalBuffer;

        /// <summary>
        /// Creates a non-owning wrapper around an externally-managed <see cref="GPUBuffer"/>.
        /// </summary>
        /// <param name="accelerator">The WebGPU accelerator (must share the same GPUDevice as the external buffer).</param>
        /// <param name="externalBuffer">The externally-owned GPU buffer to wrap.</param>
        /// <param name="elementCount">Number of elements of type <typeparamref name="T"/> in the buffer.</param>
        /// <param name="elementSize">Size in bytes of each element.</param>
        public ExternalWebGPUMemoryBuffer(WebGPUAccelerator accelerator, GPUBuffer externalBuffer, long elementCount, int elementSize)
            : base(accelerator, elementCount, elementSize, skipAllocation: true)
        {
            // Create a non-owning WebGPUBuffer<byte> wrapper so NativeBuffer returns the correct type.
            _externalBuffer = new WebGPUBuffer<byte>(accelerator.NativeAccelerator, externalBuffer, elementCount * elementSize);
        }

        /// <summary>
        /// Returns the non-owning wrapper around the external GPU buffer.
        /// </summary>
        public override WebGPUBuffer<byte> NativeBuffer => _externalBuffer;

        /// <summary>
        /// Disposing this instance releases the non-owning wrapper but does NOT destroy the external GPUBuffer.
        /// </summary>
        protected override void DisposeAcceleratorObject(bool disposing)
        {
            if (disposing)
            {
                // Dispose the non-owning WebGPUBuffer<byte> wrapper (does not destroy the underlying GPUBuffer).
                _externalBuffer.Dispose();
            }
        }
    }
}
