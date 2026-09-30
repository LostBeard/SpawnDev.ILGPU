using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.SpawnJS.JSObjects;
// SpawnJS declares this WebGPU typedef as a global using, which is visible only inside SpawnJS.
using GPUCopyExternalImageSource = SpawnDev.SpawnJS.Union<SpawnDev.SpawnJS.JSObjects.ImageBitmap, SpawnDev.SpawnJS.JSObjects.ImageData, SpawnDev.SpawnJS.JSObjects.HTMLImageElement, SpawnDev.SpawnJS.JSObjects.HTMLVideoElement, SpawnDev.SpawnJS.JSObjects.VideoFrame, SpawnDev.SpawnJS.JSObjects.HTMLCanvasElement, SpawnDev.SpawnJS.JSObjects.OffscreenCanvas>;

namespace SpawnDev.ILGPU.Rendering
{
    /// <summary>
    /// Copies the current pixels of a browser image source (a playing <c>&lt;video&gt;</c>, an <c>&lt;img&gt;</c>,
    /// a canvas, an <see cref="ImageBitmap"/>, a <see cref="VideoFrame"/>, ...) into an ILGPU buffer as packed
    /// RGBA8 - one <c>int</c> per pixel, R in the low byte, row-major, top row first. This is the same layout
    /// <see cref="ICanvasRenderer.PresentAsync(MemoryBuffer2D{int, Stride2D.DenseX})"/> presents, so a frame can go
    /// source -> kernels -> canvas without leaving the device.
    /// </summary>
    /// <remarks>
    /// The inverse of <see cref="ICanvasRenderer"/>. Create one with <see cref="ExternalImageCopier.Create"/>, which
    /// picks the backend's best path: on WebGPU the pixels go source -> texture -> buffer entirely on the GPU
    /// (<c>queue.copyExternalImageToTexture</c> + <c>copyTextureToBuffer</c>), never through a canvas
    /// <c>getImageData</c> readback. Reuse the instance across frames: it keeps its staging resources.
    /// <para>
    /// ⚠️ A cross-origin source without CORS approval is TAINTED; the browser refuses to expose its pixels and the
    /// copy throws (a SecurityError on WebGPU). That is the browser's rule, not a backend limitation.
    /// </para>
    /// </remarks>
    public interface IExternalImageCopier : IDisposable
    {
        /// <summary>
        /// Copies <paramref name="width"/> x <paramref name="height"/> pixels from the top-left of
        /// <paramref name="source"/> into <paramref name="destination"/> as packed RGBA8.
        /// </summary>
        /// <param name="source">The image source. Its size must be at least width x height (use the natural size:
        /// <c>videoWidth</c>/<c>videoHeight</c>, <c>naturalWidth</c>/<c>naturalHeight</c>).</param>
        /// <param name="width">Pixels per row to copy.</param>
        /// <param name="height">Rows to copy.</param>
        /// <param name="destination">Receives width*height packed pixels. Must hold at least that many elements.</param>
        void CopyToBuffer(GPUCopyExternalImageSource source, int width, int height, ArrayView1D<int, Stride1D.Dense> destination);
    }
}
