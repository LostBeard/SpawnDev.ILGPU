// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
// ---------------------------------------------------------------------------------------
// File: IScatterProvider.cs
//
// Bridges the algorithm layer (ILGPU.Algorithms) to a backend-provided GPGPU scatter
// primitive. WebGL2 transform feedback is gather-only, so reorder algorithms (RadixSort)
// need a real scatter (dst[destIndex[i]] = src[i]). WebGLAccelerator implements this; the
// RadixSort factory checks `accelerator is IScatterProvider` and routes to a scatter-based
// 1-bit-split sort. Other backends never implement it (they scatter natively).
// ---------------------------------------------------------------------------------------

namespace ILGPU.Algorithms
{
    /// <summary>
    /// Implemented by accelerators that expose a GPGPU scatter primitive:
    /// <c>destination[destIndex[i]] = source[i]</c> for i in [0, count). The result lives in
    /// the destination buffer's GPU storage (zero-copy); a later host readback refreshes lazily.
    /// </summary>
    public interface IScatterProvider
    {
        /// <summary>
        /// Scatters <paramref name="source"/> into <paramref name="destination"/> using
        /// <paramref name="destIndex"/> (int indices). <paramref name="destination"/> and
        /// <paramref name="source"/> share the element type named by <paramref name="valueGlslType"/>
        /// ("int", "uint", or "float"). <paramref name="componentsPerElement"/> is the number of
        /// 32-bit texels per element (1 for 32-bit types, 2 for i64/f64 stored as [lo,hi] pairs).
        /// Arguments are ILGPU views/buffers (passed as object to keep this interface backend-agnostic).
        /// </summary>
        void Scatter(object destination, object source, object destIndex, int count, string valueGlslType,
            int componentsPerElement = 1);
    }
}
