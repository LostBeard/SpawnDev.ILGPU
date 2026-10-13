using global::ILGPU.Runtime;
using SpawnDev.ILGPU.WebGL;
using SpawnDev.ILGPU.WebGL.Rendering;
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGPU.Rendering;

namespace SpawnDev.ILGPU.Rendering
{
    /// <summary>
    /// Creates the optimal <see cref="ICanvasRenderer"/> for the given accelerator type.
    /// </summary>
    public static class CanvasRendererFactory
    {
        /// <summary>
        /// Returns a <see cref="WebGPUCanvasRenderer"/> for a WebGPU accelerator, a <see cref="WebGLCanvasRenderer"/> for a
        /// WebGL accelerator, and a <see cref="CPUCanvasRenderer"/> for any other accelerator.
        /// </summary>
        /// <param name="accelerator">The accelerator whose buffers will be presented.</param>
        /// <returns>A new renderer; the caller owns it and must dispose it.</returns>
        public static ICanvasRenderer Create(Accelerator accelerator) => accelerator switch
        {
            WebGPUAccelerator wgpu => new WebGPUCanvasRenderer(wgpu),
            WebGLAccelerator wgl   => new WebGLCanvasRenderer(wgl),
            _                      => new CPUCanvasRenderer(accelerator),
        };
    }
}
