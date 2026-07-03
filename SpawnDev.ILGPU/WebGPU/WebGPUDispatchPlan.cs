using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.JSObjects;

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
    private readonly SpawnDev.BlazorJS.JSObjects.Array _plan;
    private readonly List<GPUBuffer> _retainedScalarBuffers = new();
    private readonly List<GPUBuffer> _retainedCoalesceBuffers = new();
    private bool _disposed;

    /// <summary>Number of dispatches recorded into this plan.</summary>
    public int DispatchCount { get; private set; }

    /// <summary>True once <see cref="WebGPUAccelerator.EndDispatchCapture"/> sealed this plan.</summary>
    public bool IsSealed { get; internal set; }

    internal WebGPUDispatchPlan(WebGPUAccelerator accelerator)
    {
        _accelerator = accelerator;
        _plan = new SpawnDev.BlazorJS.JSObjects.Array();
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
        DispatchCount++;
    }

    /// <summary>Record an encoder-level buffer copy (Concat assembly, coalesce gathers, device copies) -
    /// these move data recomputed by earlier replayed dispatches, so a replay must re-run them in order.</summary>
    internal void RecordCopy(GPUBuffer src, ulong srcOffset, GPUBuffer dst, ulong dstOffset, ulong size)
    {
        _plan.JSRef!.CallVoid("push", 1, src, (double)srcOffset, dst, (double)dstOffset, (double)size, 0);
        DispatchCount++;
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
            var baseUri = BlazorJSRuntime.JS.Get<string>("document.baseURI");
            var url = new Uri(new Uri(baseUri), "_content/SpawnDev.ILGPU/webgpuDispatchPlan.js").ToString();
            using var module = await BlazorJSRuntime.JS.CallAsync<JSObject>("import", url);
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
        return BlazorJSRuntime.JS.Call<int>("ilgpuWebGPUPlan.replay", device, _plan);
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
        var encode = BlazorJSRuntime.JS.Get<double?>("ilgpuWebGPUPlan.last.encodeMs") ?? -1;
        var submit = BlazorJSRuntime.JS.Get<double?>("ilgpuWebGPUPlan.last.submitMs") ?? -1;
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
