using ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
using global::ILGPU;
using global::ILGPU.Runtime;

namespace SpawnDev.ILGPU.Rendering
{
    /// <summary>
    /// Defines the standard API for presenting an ILGPU MemoryBuffer2D of RGBA pixels to an HTML canvas.
    /// Implementations choose the most efficient path for their backend.
    /// </summary>
    public interface ICanvasRenderer : IDisposable
    {
        /// <summary>Attaches (or re-attaches) the renderer to an HTML canvas element.</summary>
        void AttachCanvas(HTMLCanvasElement canvas);

        /// <summary>Presents a 2D packed-uint (RGBA) pixel buffer to the canvas.</summary>
        Task PresentAsync(MemoryBuffer2D<uint, Stride2D.DenseX> buffer);

        /// <summary>Presents a 2D packed-int (RGBA) pixel buffer to the canvas.</summary>
        Task PresentAsync(MemoryBuffer2D<int, Stride2D.DenseX> buffer);
    }
}
