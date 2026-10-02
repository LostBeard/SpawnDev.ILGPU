// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                Strongly-Typed PostMessage DTOs for the WebGL GL worker
//
// Replaces the anonymous-object PostMessage payloads WebGLAccelerator used (the same change the Wasm
// backend made 2026-04-26, see Wasm/WasmDispatchMessages.cs). Property names are lowercase to match the
// field reads in wwwroot/glWorker.js (msg.type, msg.bufferId, ...).
//
// ⚠️ TRIMMING (2026-10-01): SpawnJS marshals a message by reflecting over its PUBLIC PROPERTIES, and
// nothing else ever reads them, so in a trimmed app the trimmer removed every getter of the anonymous
// types: the worker received {} and every WebGL dispatch waited until its test's 30 s timeout. The class-level
// [DynamicallyAccessedMembers(PublicProperties)] below documents the intent but is NOT sufficient on its own (a DTO
// stored in a List<object> still lost its getters); the guarantee is the DynamicDependency set on
// WebGLAccelerator.InitializeGLWorker. A new DTO must be added there too.
// ---------------------------------------------------------------------------------------

using SpawnDev.SpawnJS.JSObjects;
using System.Diagnostics.CodeAnalysis;

namespace SpawnDev.ILGPU.WebGL
{
    /// <summary>Hands the OffscreenCanvas to the GL worker (the canvas also goes in the transfer list).</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerInitMessage
    {
        /// <summary>Always "init".</summary>
        public string type => "init";
        /// <summary>The transferred OffscreenCanvas.</summary>
        public OffscreenCanvas canvas { get; init; } = null!;
    }

    /// <summary>Allocates a worker-side buffer.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerAllocBufferMessage
    {
        /// <summary>Always "allocBuffer".</summary>
        public string type => "allocBuffer";
        /// <summary>Worker buffer id.</summary>
        public int bufferId { get; init; }
        /// <summary>Size in bytes.</summary>
        public int byteSize { get; init; }
        /// <summary>GLSL element type of the buffer.</summary>
        public string? glslType { get; init; }
    }

    /// <summary>Uploads a whole buffer into a worker-side buffer.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public class GLWorkerUploadBufferMessage
    {
        /// <summary>Always "uploadBuffer".</summary>
        public string type => "uploadBuffer";
        /// <summary>Worker buffer id.</summary>
        public int bufferId { get; init; }
        /// <summary>The transferred bytes.</summary>
        public ArrayBuffer buffer { get; init; } = null!;
        /// <summary>Offset into <see cref="buffer"/>.</summary>
        public int byteOffset { get; init; }
        /// <summary>Number of bytes.</summary>
        public int byteLength { get; init; }
    }

    /// <summary>Uploads a byte range into a worker-side buffer at <see cref="dstByteOffset"/>.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerUploadRangeMessage : GLWorkerUploadBufferMessage
    {
        /// <summary>Destination offset in the worker buffer.</summary>
        public int dstByteOffset { get; init; }
    }

    /// <summary>Copies bytes between two worker-side buffers.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerCopyBufferMessage
    {
        /// <summary>Always "copyBuffer".</summary>
        public string type => "copyBuffer";
        /// <summary>Source buffer id.</summary>
        public int srcBufferId { get; init; }
        /// <summary>Source byte offset.</summary>
        public int srcByteOffset { get; init; }
        /// <summary>Destination buffer id.</summary>
        public int dstBufferId { get; init; }
        /// <summary>Destination byte offset.</summary>
        public int dstByteOffset { get; init; }
        /// <summary>Number of bytes.</summary>
        public int byteLength { get; init; }
    }

    /// <summary>Host-level scatter (render-to-texture) between worker-side buffers.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerScatterMessage
    {
        /// <summary>Always "scatter".</summary>
        public string type => "scatter";
        /// <summary>Destination buffer id.</summary>
        public int dstBufferId { get; init; }
        /// <summary>Source buffer id.</summary>
        public int srcBufferId { get; init; }
        /// <summary>Destination-index buffer id.</summary>
        public int destBufferId { get; init; }
        /// <summary>Element count.</summary>
        public int n { get; init; }
        /// <summary>Components per element: 1 for 32-bit, 2 for i64/f64 (two texels per element).</summary>
        public int cpe { get; init; }
    }

    /// <summary>Frees a worker-side buffer.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerFreeBufferMessage
    {
        /// <summary>Always "freeBuffer".</summary>
        public string type => "freeBuffer";
        /// <summary>Worker buffer id.</summary>
        public int bufferId { get; init; }
    }

    /// <summary>Requests a readback of a worker-side buffer.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerReadbackMessage
    {
        /// <summary>Always "readbackBuffer".</summary>
        public string type => "readbackBuffer";
        /// <summary>Worker buffer id.</summary>
        public int bufferId { get; init; }
        /// <summary>Correlates the worker's reply.</summary>
        public int requestId { get; init; }
    }

    /// <summary>Blits a worker-side buffer to the canvas.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerBlitMessage
    {
        /// <summary>Always "blitBuffer".</summary>
        public string type => "blitBuffer";
        /// <summary>Worker buffer id.</summary>
        public int bufferId { get; init; }
        /// <summary>Blit width.</summary>
        public int width { get; init; }
        /// <summary>Blit height.</summary>
        public int height { get; init; }
        /// <summary>Correlates the worker's reply.</summary>
        public int requestId { get; init; }
    }

    /// <summary>A kernel dispatch.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLWorkerDispatchMessage
    {
        /// <summary>Always "dispatch".</summary>
        public string type => "dispatch";
        /// <summary>Correlates the worker's completion reply.</summary>
        public int dispatchId { get; init; }
        /// <summary>Compiled program id (the worker caches programs by it).</summary>
        public int programId { get; init; }
        /// <summary>GLSL source.</summary>
        public string source { get; init; } = "";
        /// <summary>Transform-feedback varying names.</summary>
        public string[] varyingNames { get; init; } = System.Array.Empty<string>();
        /// <summary>Vertices to draw (one per thread).</summary>
        public int totalVertices { get; init; }
        /// <summary>Launch extent X.</summary>
        public int dimX { get; init; }
        /// <summary>Launch extent Y.</summary>
        public int dimY { get; init; }
        /// <summary>Launch extent Z.</summary>
        public int dimZ { get; init; }
        /// <summary>Group size X (explicitly grouped kernels).</summary>
        public int groupDimX { get; init; }
        /// <summary>Grid size X.</summary>
        public int gridDimX { get; init; }
        /// <summary>Grid size Y.</summary>
        public int gridDimY { get; init; }
        /// <summary>Kernel parameters.</summary>
        public object[] @params { get; init; } = System.Array.Empty<object>();
        /// <summary>Per-parameter view strides.</summary>
        public System.Collections.Generic.Dictionary<int, int[]> strides { get; init; } = new();
        /// <summary>Output descriptors.</summary>
        public object[] outputs { get; init; } = System.Array.Empty<object>();
    }
    /// <summary>A buffer kernel parameter.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLParamBufferRef
    {
        /// <summary>Always "buffer_ref".</summary>
        public string kind => "buffer_ref";
        /// <summary>Worker buffer id.</summary>
        public int bufferId { get; init; }
        /// <summary>GLSL parameter index.</summary>
        public int paramIndex { get; init; }
        /// <summary>Elements in the view.</summary>
        public int elementCount { get; init; }
        /// <summary>Element offset of the view.</summary>
        public int elementOffset { get; init; }
    }

    /// <summary>A 64-bit scalar parameter split into two u32 halves (emulated i64/f64).</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLParamScalarEmu64
    {
        /// <summary>Always "scalar_emu64".</summary>
        public string kind => "scalar_emu64";
        /// <summary>GLSL parameter index.</summary>
        public int paramIndex { get; init; }
        /// <summary>Low 32 bits.</summary>
        public uint lo { get; init; }
        /// <summary>High 32 bits.</summary>
        public uint hi { get; init; }
    }

    /// <summary>A scalar uniform parameter.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLParamScalar
    {
        /// <summary>Always "scalar".</summary>
        public string kind => "scalar";
        /// <summary>GLSL parameter index.</summary>
        public int paramIndex { get; init; }
        /// <summary>GLSL scalar type.</summary>
        public string scalarType { get; init; } = "";
        /// <summary>Encoded value.</summary>
        public object value { get; init; } = null!;
    }

    /// <summary>A struct parameter flattened to uniform fields.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLParamStruct
    {
        /// <summary>Always "struct".</summary>
        public string kind => "struct";
        /// <summary>GLSL parameter index.</summary>
        public int paramIndex { get; init; }
        /// <summary>Flattened GLUniformField entries.</summary>
        public object[] fields { get; init; } = System.Array.Empty<object>();
    }

    /// <summary>One flattened struct field.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLUniformField
    {
        /// <summary>Field path.</summary>
        public string path { get; init; } = "";
        /// <summary>GLSL scalar type.</summary>
        public string scalarType { get; init; } = "";
        /// <summary>Encoded value.</summary>
        public object value { get; init; } = null!;
    }

    /// <summary>One transform-feedback output: where a varying lands in which buffer.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
    public sealed class GLOutputEntry
    {
        /// <summary>bufferId.</summary>
        public int bufferId { get; init; }
        /// <summary>paramIndex.</summary>
        public int paramIndex { get; init; }
        /// <summary>outputIndex.</summary>
        public int outputIndex { get; init; }
        /// <summary>varyingName.</summary>
        public string varyingName { get; init; } = "";
        /// <summary>isEmulated.</summary>
        public bool isEmulated { get; init; }
        /// <summary>emulatedSuffix.</summary>
        public string emulatedSuffix { get; init; } = "";
        /// <summary>fieldIndex.</summary>
        public int fieldIndex { get; init; }
        /// <summary>storeSlot.</summary>
        public int storeSlot { get; init; }
        /// <summary>storeCount.</summary>
        public int storeCount { get; init; }
        /// <summary>isAtomicVote.</summary>
        public bool isAtomicVote { get; init; }
        /// <summary>fieldByteOffset.</summary>
        public int fieldByteOffset { get; init; }
        /// <summary>fieldByteSize.</summary>
        public int fieldByteSize { get; init; }
        /// <summary>structByteSize.</summary>
        public int structByteSize { get; init; }
        /// <summary>writeByteOffset.</summary>
        public int writeByteOffset { get; init; }
        /// <summary>writeLengthBytes.</summary>
        public int writeLengthBytes { get; init; }
        /// <summary>subWordElementSize.</summary>
        public int subWordElementSize { get; init; }
    }
}
