using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;

namespace SpawnDev.ILGPU.WebGPU;

/// <summary>
/// A captured, replayable WebGPU dispatch plan - the browser twin of a CUDA graph.
///
/// During a capture pass (<see cref="WebGPUAccelerator.BeginDispatchCapture"/> ..
/// <see cref="WebGPUAccelerator.EndDispatchCapture"/>) every kernel dispatch the accelerator encodes
/// is ALSO recorded here as a flat JS-array entry <c>[pipeline, bindGroup, x, y, z]</c>. The JS
/// array holding the <c>GPUComputePipeline</c>/<c>GPUBindGroup</c> objects is what keeps them alive
/// for replay - the dispatch path's own .NET wrapper disposal is unaffected. The plan additionally
/// takes ownership of the per-dispatch scalar-params buffers (they would otherwise return to the
/// scalar pool and be OVERWRITTEN by later dispatches) and coalesced param buffers (they would
/// otherwise be destroyed at flush).
///
/// <see cref="ReplayAsync"/> re-encodes the whole plan with a SINGLE .NET-&gt;JS interop crossing:
/// a JS loop (see wwwroot/webgpuDispatchPlan.js) writes one command encoder - one compute pass per
/// dispatch, preserving WebGPU's inter-pass ordering guarantees - and submits one command buffer.
/// This removes the per-dispatch interop cost (the dominant term of a graph-executor forward on
/// WebGPU) exactly like <c>cuGraphLaunch</c> removes per-kernel launch prep on CUDA.
///
/// VALIDITY CONTRACT (same as CUDA graph capture): a replay re-runs the captured dispatches against
/// the SAME buffers with the SAME parameters. It is only correct when every buffer the plan's bind
/// groups reference is still alive and still plays the same role - i.e. capture under a stable-buffer
/// regime (pre-warmed pool, stable param slots, no mid-forward recycling) at a FIXED input shape,
/// write fresh input data into the captured input buffer(s) before each replay, and do not interleave
/// non-replay work that recycles those buffers. Bind-group caching must be OFF during capture
/// (a cache-hit rewrites its owned scalar buffer, which would retroactively corrupt earlier plan
/// entries); <see cref="WebGPUAccelerator.BeginDispatchCapture"/> enforces this.
/// </summary>
public sealed class WebGPUDispatchPlan : IDisposable
{
    private static Task? _helperLoad;
    private readonly WebGPUAccelerator _accelerator;
    private readonly SpawnDev.SpawnJS.JSObjects.Array _plan;
    private readonly List<GPUBuffer> _retainedScalarBuffers = new();
    private readonly List<GPUBuffer> _retainedCoalesceBuffers = new();
    private bool _disposed;

    /// <summary>Number of dispatches recorded into this plan.</summary>
    public int DispatchCount { get; private set; }

    /// <summary>
    /// When set BEFORE capture, every recorded compute dispatch also snapshots its packed
    /// _scalar_params upload (the retained buffer + the exact bytes written) into
    /// <see cref="ScalarSnapshots"/>, and every recorded copy logs (entryIndex, dstOffset) into
    /// <see cref="CopyEntries"/>. This is the PATCH SURFACE for parameterized replay: a driver that
    /// captures two plans at two values of a loop variable (e.g. an LLM decode step at pastLen P and
    /// P+1) can diff the snapshots to find every scalar byte and copy offset that depends on the
    /// variable, then patch them per replay (<see cref="PatchScalarInt"/> /
    /// <see cref="PatchCopyDstOffsets"/>).
    /// </summary>
    public bool CaptureScalarSnapshots { get; set; }

    /// <summary>Per-dispatch packed-scalar snapshot: the dispatch's plan entry index, the retained
    /// _scalar_params buffer its bind group references, and the exact bytes uploaded. Entries with no
    /// scalar params are absent. Populated only when <see cref="CaptureScalarSnapshots"/>.</summary>
    public List<(int EntryIndex, GPUBuffer Buffer, byte[] Bytes)> ScalarSnapshots { get; } = new();

    /// <summary>Recorded copyBufferToBuffer entries (entry index + src/dst byte offsets + size).
    /// Populated only when <see cref="CaptureScalarSnapshots"/>.</summary>
    public List<(int EntryIndex, ulong SrcOffset, ulong DstOffset, ulong Size)> CopyEntries { get; } = new();

    private GPUBuffer? _pendingScalarBuf;
    private byte[]? _pendingScalarBytes;

    /// <summary>Called by the dispatch path right after the packed-scalar writeBuffer; attached to the
    /// NEXT recorded dispatch (upload always precedes Record in the dispatch flow).</summary>
    internal void NoteScalarUpload(GPUBuffer buffer, byte[] bytes)
    {
        if (!CaptureScalarSnapshots) return;
        _pendingScalarBuf = buffer;
        _pendingScalarBytes = (byte[])bytes.Clone();
    }

    /// <summary>
    /// CPU-&gt;GPU <c>queue.writeBuffer</c> calls that happened while this plan was recording.
    /// </summary>
    /// <remarks>
    /// 🔴 A HOST WRITE IS NOT REPLAYABLE, and it is invisible in the plan. A plan records dispatches,
    /// copyBufferToBuffer and clearBuffer - all three are command-encoder work. <c>queue.writeBuffer</c> is
    /// not: it moves bytes the CPU is holding. On replay it simply does not happen, so the destination keeps
    /// whatever the capture pass last left there. If those bytes were constant that is harmless; if they
    /// depended on this call's inputs, the replay is confidently wrong and nothing about the output looks
    /// broken.
    /// <para>
    /// MEASURED 2026-09-03 on ZipVoice's fm_decoder: a replay of the captured plan did not reproduce the
    /// forward it recorded AT THE EXACT INPUTS IT CAPTURED - 16,900 of 16,900 values differ, worst 0.711702
    /// (<c>Pipeline_ZipVoice_CaptureReplayFidelity</c>). "At the captured inputs" rules out every
    /// input-plumbing explanation and says the plan is missing WORK. Counting host writes inside the window
    /// is how that stops being a deduction and becomes a number.
    /// </para>
    /// </remarks>
    public int HostWriteCount { get; private set; }

    /// <summary>Total bytes of the host writes counted by <see cref="HostWriteCount"/>.</summary>
    public long HostWriteBytes { get; private set; }

    /// <summary>
    /// The plan currently recording, if any - reachable from the buffer layer.
    /// </summary>
    /// <remarks>
    /// ⚠️ Static because the CPU-&gt;GPU upload path lives on <c>WebGPUNativeAccelerator</c>, which has no
    /// reference to the <c>WebGPUAccelerator</c> that owns the capture. Capture is a single-threaded,
    /// one-at-a-time operation (<c>BeginDispatchCapture</c> throws if one is already active), so one slot
    /// is exact rather than approximate.
    /// </remarks>
    internal static WebGPUDispatchPlan? Recording { get; set; }

    /// <summary>Called from the CPU-&gt;GPU upload paths while a plan is recording. See <see cref="HostWriteCount"/>.</summary>
    internal void NoteHostWrite(long bytes)
    {
        HostWriteCount++;
        HostWriteBytes += bytes;
    }

    /// <summary>True once <see cref="WebGPUAccelerator.EndDispatchCapture"/> sealed this plan.</summary>
    public bool IsSealed { get; internal set; }

    internal WebGPUDispatchPlan(WebGPUAccelerator accelerator)
    {
        _accelerator = accelerator;
        _plan = new SpawnDev.SpawnJS.JSObjects.Array();
    }

    // Plan records are flat 7-element tagged groups (see wwwroot/webgpuDispatchPlan.js):
    //   [0, pipeline, bindGroup, x, y, z, 0]                     dispatch
    //   [1, srcBuffer, srcOfs, dstBuffer, dstOfs, size, 0]       copyBufferToBuffer
    //   [2, buffer, ofs, size, 0, 0, 0]                          clearBuffer
    // Offsets/sizes travel as double (JS number) - always < 2^53 in practice.

    /// <summary>Record one dispatch (called from the accelerator's dispatch path during capture).</summary>
    internal void Record(GPUComputePipeline pipeline, GPUBindGroup bindGroup, uint x, uint y, uint z)
    {
        _plan.JSRef!.CallVoid("push", 0, pipeline, bindGroup, (int)x, (int)y, (int)z, 0);
        if (_pendingScalarBytes != null)
        {
            ScalarSnapshots.Add((DispatchCount, _pendingScalarBuf!, _pendingScalarBytes));
            _pendingScalarBuf = null;
            _pendingScalarBytes = null;
        }
        DispatchCount++;
    }

    /// <summary>Record an encoder-level buffer copy (Concat assembly, coalesce gathers, device copies) -
    /// these move data recomputed by earlier replayed dispatches, so a replay must re-run them in order.</summary>
    internal void RecordCopy(GPUBuffer src, ulong srcOffset, GPUBuffer dst, ulong dstOffset, ulong size)
    {
        _plan.JSRef!.CallVoid("push", 1, src, (double)srcOffset, dst, (double)dstOffset, (double)size, 0);
        if (CaptureScalarSnapshots)
            CopyEntries.Add((DispatchCount, srcOffset, dstOffset, size));
        DispatchCount++;
    }

    /// <summary>
    /// Overwrite a 4-byte int inside a snapshotted dispatch's retained _scalar_params buffer (a
    /// queue-ordered writeBuffer - lands before the next replay's submit). The patch surface for
    /// parameterized replay; see <see cref="CaptureScalarSnapshots"/>.
    /// </summary>
    public void PatchScalarInt(int snapshotIndex, int byteOffset, int value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var (_, buf, _) = ScalarSnapshots[snapshotIndex];
        var device = _accelerator.NativeAccelerator.NativeDevice
            ?? throw new InvalidOperationException("WebGPU device unavailable (lost or disposed).");
        device.Queue.WriteBuffer(buf, (ulong)byteOffset, BitConverter.GetBytes(value));
    }

    /// <summary>
    /// Rewrite the dstOffset of recorded copy entries in place (one interop crossing for the whole
    /// batch). Entry indices come from <see cref="CopyEntries"/>; offsets are BYTES (what
    /// copyBufferToBuffer takes). The replayed copies then write at the new destinations - the
    /// KV-cache-append case, where the destination row advances per decode token.
    /// </summary>
    public async Task PatchCopyDstOffsetsAsync(int[] entryIndices, double[] newDstOffsets)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (entryIndices.Length != newDstOffsets.Length)
            throw new ArgumentException("entryIndices and newDstOffsets must have equal length");
        await EnsureHelperLoadedAsync();
        SpawnJSRuntime.Instance.CallVoid("ilgpuWebGPUPlan.patchCopyDst", _plan, entryIndices, newDstOffsets);
    }

    /// <summary>Record an encoder-level clearBuffer (zero-fill) - replays must re-zero, or kernels
    /// accumulate into stale prior-frame data.</summary>
    internal void RecordClear(GPUBuffer buffer, ulong offset, ulong size)
    {
        _plan.JSRef!.CallVoid("push", 2, buffer, (double)offset, (double)size, 0, 0, 0);
        DispatchCount++;
    }

    /// <summary>Take ownership of this dispatch's scalar-params buffers (moves + clears the list).</summary>
    internal void RetainScalarBuffers(List<GPUBuffer> buffers)
    {
        _retainedScalarBuffers.AddRange(buffers);
        buffers.Clear();
    }

    /// <summary>Take ownership of this dispatch's coalesced param buffers (moves + clears the list).</summary>
    internal void RetainCoalesceBuffers(List<GPUBuffer> buffers)
    {
        _retainedCoalesceBuffers.AddRange(buffers);
        buffers.Clear();
    }

    private static Task EnsureHelperLoadedAsync()
    {
        return _helperLoad ??= LoadAsync();
        static async Task LoadAsync()
        {
            // Resolve against the app base so the import works regardless of the calling module's URL.
            var baseUri = SpawnJSRuntime.Instance.AppBaseUri;
            var url = new Uri(new Uri(baseUri), "_content/SpawnDev.ILGPU/webgpuDispatchPlan.js").ToString();
            // JS.Import() routes through SpawnJSInterop.import (dynamic import()); the runtime's CallAsync
            // would look up globalThis.import, which does not exist - import() is syntax, not a callable.
            using var module = await SpawnJSRuntime.Instance.Import(url);
        }
    }

    /// <summary>
    /// Replays the captured plan: ONE interop crossing encodes every dispatch into a fresh command
    /// encoder JS-side and submits it. Returns the number of dispatches encoded. The submit is
    /// fire-and-forget (queue-ordered after any prior writeBuffer input uploads); await the
    /// accelerator's <c>SynchronizeAsync()</c> to wait for completion before reading results.
    /// </summary>
    public async Task<int> ReplayAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSealed)
            throw new InvalidOperationException("Dispatch plan is still recording - call EndDispatchCapture() first.");
        await EnsureHelperLoadedAsync();
        var device = _accelerator.NativeAccelerator.NativeDevice
            ?? throw new InvalidOperationException("WebGPU device unavailable (lost or disposed).");
        // Submit any batched-but-unsubmitted accelerator work FIRST. A caller that refreshed the
        // captured input buffers via a KERNEL DISPATCH (e.g. a video pipeline's per-frame preprocess
        // into the stable input) has that dispatch sitting in the accelerator's pending encoder;
        // the plan's own submit below is a SEPARATE JS-side command buffer, so without this flush
        // the pending work would land AFTER the replay and the replay would read the PREVIOUS
        // frame's data (caught by the DA3 video-path stale-replay guard, 2026-07-03). writeBuffer
        // uploads (CopyFromCPU) were always safe - they are queue-ordered, not encoder-batched.
        _accelerator.FlushPendingCommands();
        return SpawnJSRuntime.Instance.Call<GPUDevice, SpawnDev.SpawnJS.JSObjects.Array, int>("ilgpuWebGPUPlan.replay", device, _plan);
    }

    /// <summary>
    /// Replays the captured plan with per-pass GPU timestamps (WebGPU 'timestamp-query') and returns
    /// a JSON string aggregating GPU time by pipeline label (the kernel name): <c>{"supported":true,
    /// "passes":N,"ops":N,"totalMs":x,"spanMs":x,"kernels":[{"label","ms","count","maxMs"},...]}</c>
    /// sorted by total ms descending, or <c>{"supported":false,"reason":...}</c> when the device
    /// lacks the feature. Runs the SAME dispatches as <see cref="ReplayAsync"/> (same validity
    /// contract) and waits for GPU completion internally - no separate SynchronizeAsync needed.
    /// Diagnostic path: per-pass timestampWrites add overhead; do not use it for frame timing.
    /// NOTE: Chrome quantizes GPU timestamps to 100us unless launched with
    /// --enable-webgpu-developer-features; totals telescope exactly either way, but fine per-kernel
    /// attribution wants the flag.
    /// </summary>
    public async Task<string> ReplayTimedAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSealed)
            throw new InvalidOperationException("Dispatch plan is still recording - call EndDispatchCapture() first.");
        await EnsureHelperLoadedAsync();
        var device = _accelerator.NativeAccelerator.NativeDevice
            ?? throw new InvalidOperationException("WebGPU device unavailable (lost or disposed).");
        _accelerator.FlushPendingCommands();   // same stale-input ordering contract as ReplayAsync
        return await SpawnJSRuntime.Instance.CallAsync<GPUDevice, SpawnDev.SpawnJS.JSObjects.Array, string>("ilgpuWebGPUPlan.replayTimed", device, _plan);
    }

    /// <summary>
    /// JS-side timing of the most recent <see cref="ReplayAsync"/> on this page (any plan):
    /// EncodeMs = the JS re-encode loop, SubmitMs = <c>enc.finish()</c> + <c>queue.submit()</c>.
    /// GPU execution is NOT included - it completes asynchronously after the submit (await
    /// <c>SynchronizeAsync()</c> for that). Diagnostic accessor - two interop reads, call it only
    /// when instrumenting; the replay hot path records the numbers JS-side for free either way.
    /// </summary>
    public static (double EncodeMs, double SubmitMs) GetLastReplayTimings()
    {
        var encode = SpawnJSRuntime.Instance.Get<double?>("ilgpuWebGPUPlan.last.encodeMs") ?? -1;
        var submit = SpawnJSRuntime.Instance.Get<double?>("ilgpuWebGPUPlan.last.submitMs") ?? -1;
        return (encode, submit);
    }

    /// <summary>
    /// Releases the plan: returns retained scalar buffers to the accelerator's scalar pool,
    /// destroys retained coalesced buffers, and drops the JS plan array (releasing the recorded
    /// pipelines/bind groups to normal JS GC).
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var buf in _retainedScalarBuffers)
        {
            try { WebGPUAccelerator.ReturnPooledScalarBuffer(buf); } catch { }
        }
        _retainedScalarBuffers.Clear();
        foreach (var buf in _retainedCoalesceBuffers)
        {
            try { buf.Destroy(); buf.Dispose(); } catch { }
        }
        _retainedCoalesceBuffers.Clear();
        try { _plan.Dispose(); } catch { }
    }
}
