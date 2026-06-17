using ILGPU;
using ILGPU.Runtime;

namespace SpawnDev.ILGPU.P2P;

/// <summary>
/// P2P memory buffer — represents data that may reside on a remote peer device.
/// Tracks data locality for intelligent kernel dispatch routing.
///
/// The buffer maintains a local shadow copy of the data. When the coordinator
/// copies data to this buffer (CopyTo), the shadow is updated and marked dirty.
/// When a kernel dispatch targets the buffer's resident peer, the dirty shadow
/// is sent via WebRTC. When the coordinator reads back (CopyFrom), the latest
/// data is retrieved from the shadow (updated after kernel execution).
///
/// This design avoids blocking the synchronous MemoryBuffer API on async WebRTC.
/// </summary>
public class P2PMemoryBuffer : MemoryBuffer
{
    /// <summary>
    /// Which peer this buffer's data currently resides on (null = local/host).
    /// </summary>
    public RemotePeer? ResidentPeer { get; set; }

    /// <summary>
    /// Whether the local copy is current (not stale from remote writes).
    /// </summary>
    public bool IsLocalCurrent { get; internal set; } = true;

    /// <summary>
    /// Local shadow copy of the buffer data (for send/receive staging).
    /// Access must be synchronized via ShadowLock.
    /// </summary>
    public byte[] ShadowData { get; set; }

    /// <summary>
    /// Whether the shadow has been modified locally and needs to be sent to the peer.
    /// </summary>
    public bool IsDirty { get; set; }

    /// <summary>Lock for thread-safe shadow data access.</summary>
    internal readonly object ShadowLock = new();

    /// <summary>
    /// Unique buffer ID for transport-level tracking.
    /// </summary>
    public string BufferId { get; } = Guid.NewGuid().ToString("N");

    public P2PMemoryBuffer(Accelerator accelerator, long length, int elementSize, int bitsPerElement = 0)
        : base(accelerator, length, elementSize, bitsPerElement)
    {
        NativePtr = IntPtr.Zero;
        ShadowData = new byte[length * elementSize];
    }

    /// <inheritdoc/>
    protected override void MemSet(
        AcceleratorStream stream, byte value, in ArrayView<byte> targetView)
    {
        lock (ShadowLock)
        {
            Array.Fill(ShadowData, value, 0, (int)Math.Min(targetView.Length, ShadowData.Length));
            IsDirty = true;
            IsLocalCurrent = true;
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// READ path. ILGPU calls this when a CPU-side caller invokes
    /// <c>view.CopyToCPU(...)</c>: <paramref name="sourceView"/> points into THIS
    /// buffer's storage (ShadowData) and <paramref name="targetView"/> is the CPU
    /// destination. Bytes flow OUT of ShadowData into the target.
    /// </remarks>
    protected override void CopyTo(
        AcceleratorStream stream,
        in ArrayView<byte> sourceView,
        in ArrayView<byte> targetView)
    {
        lock (ShadowLock)
        {
            unsafe
            {
                var dstPtr = targetView.LoadEffectiveAddressAsPtr();
                if (targetView.Length > 0 && ShadowData.Length > 0 && dstPtr != IntPtr.Zero)
                {
                    var copyLen = (int)Math.Min(targetView.Length, ShadowData.Length);
                    var targetSpan = new Span<byte>((void*)dstPtr, copyLen);
                    ShadowData.AsSpan(0, copyLen).CopyTo(targetSpan);
                }
            }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// WRITE path. ILGPU calls this when a CPU-side caller invokes
    /// <c>view.CopyFromCPU(...)</c>: <paramref name="sourceView"/> is the CPU
    /// source and <paramref name="targetView"/> points into THIS buffer's
    /// storage. Bytes flow IN from the source to ShadowData; mark the buffer
    /// dirty so the next dispatch ships fresh data to the resident peer.
    /// </remarks>
    protected override void CopyFrom(
        AcceleratorStream stream,
        in ArrayView<byte> sourceView,
        in ArrayView<byte> targetView)
    {
        lock (ShadowLock)
        {
            unsafe
            {
                var srcPtr = sourceView.LoadEffectiveAddressAsPtr();
                if (sourceView.Length > 0 && ShadowData.Length > 0 && srcPtr != IntPtr.Zero)
                {
                    var copyLen = (int)Math.Min(sourceView.Length, ShadowData.Length);
                    var sourceSpan = new Span<byte>((void*)srcPtr, copyLen);
                    sourceSpan.CopyTo(ShadowData.AsSpan(0, copyLen));
                    IsDirty = true;
                    IsLocalCurrent = true;
                }
            }
        }
    }

    /// <summary>
    /// Update the shadow data from bytes received from a remote peer (after kernel execution).
    /// </summary>
    public void UpdateFromRemote(byte[] data)
    {
        lock (ShadowLock)
        {
            Array.Copy(data, ShadowData, Math.Min(data.Length, ShadowData.Length));
            IsDirty = false;
            IsLocalCurrent = true;
        }
    }

    /// <summary>
    /// Get the current shadow data for transmission to a remote peer.
    /// </summary>
    public byte[] GetShadowForTransmission()
    {
        lock (ShadowLock)
        {
            IsDirty = false;
            return ShadowData.ToArray();
        }
    }

    /// <inheritdoc/>
    protected override void DisposeAcceleratorObject(bool disposing)
    {
        if (disposing)
        {
            ResidentPeer = null;
            ShadowData = Array.Empty<byte>();
        }
    }
}
