# WebGPU Dispatch-Plan Capture / Replay

*(Since 4.17.2-local.3; parameterized patch surface since 4.17.2-local.7. WebGPU backend only.)*

A dispatch plan records a fixed sequence of GPU dispatches once, then replays it many times while paying the per-dispatch command-encode and .NET->JS orchestration cost only at record time. It is the WebGPU twin of CUDA graph capture (`CudaStream.BeginCapture` / `EndCapture` / `CudaGraphExec.Launch`). The motivating case is a fixed-shape inference forward pass replayed across many steps or generations, where the per-dispatch host overhead dominates the actual GPU work.

## What it does

During a capture pass, every kernel dispatch the accelerator encodes is *also* recorded into a flat JS-side plan array as a tagged entry. Recorded entry kinds:

- **dispatch** - `[pipeline, bindGroup, x, y, z]`
- **copyBufferToBuffer** - encoder-level buffer copies (Concat assembly, coalesce gathers, device copies), which move data that earlier replayed dispatches recompute, so a replay must re-run them in order
- **clearBuffer** - zero-fill regions; a replay must re-zero these or kernels accumulate into stale prior-frame data

The dispatches still execute normally during capture. The plan additionally takes ownership of the per-dispatch `_scalar_params` buffers (they would otherwise return to the scalar pool and be overwritten by later dispatches) and the coalesced param buffers (they would otherwise be destroyed at flush).

`ReplayAsync()` re-encodes the whole plan with a **single .NET->JS interop crossing**: a JS loop (`wwwroot/webgpuDispatchPlan.js`) writes one command encoder with one compute pass per dispatch (preserving WebGPU's inter-pass ordering guarantees) and submits one command buffer. This removes the per-dispatch interop cost, which is the dominant term of a graph-executor forward on WebGPU, the same way `cuGraphLaunch` removes per-kernel launch prep on CUDA.

## Basic API

```csharp
using SpawnDev.ILGPU.WebGPU;

var accelerator = (WebGPUAccelerator)acc;

// Record: dispatch the fixed forward once. Dispatches execute normally AND are recorded.
WebGPUDispatchPlan plan = accelerator.BeginDispatchCapture();
// ... run the fixed sequence of kernel dispatches / device copies here ...
accelerator.EndDispatchCapture();   // seals the plan for replay

// Replay: one interop crossing re-encodes and submits every recorded dispatch.
for (int step = 0; step < steps; step++)
{
    // Write fresh input into the captured input buffer(s) before each replay.
    inputView.CopyFromCPU(freshInput);           // queue-ordered upload
    int encoded = await plan.ReplayAsync();       // returns the dispatch count
    await accelerator.SynchronizeAsync();         // wait before reading results
}

plan.Dispose();   // returns retained scalar buffers to the pool, destroys retained coalesce buffers
```

`BeginDispatchCapture()` returns the plan being recorded (also reachable via `accelerator.ActiveDispatchPlan`). `EndDispatchCapture()` seals it and hands ownership to the caller. `WebGPUDispatchPlan.DispatchCount` reports the number of recorded entries; `IsSealed` is true once `EndDispatchCapture()` sealed it.

`ReplayAsync()` returns the number of dispatches encoded. The submit is fire-and-forget (queue-ordered after any prior `writeBuffer` input uploads); `await accelerator.SynchronizeAsync()` to wait for GPU completion before reading results. Calling `ReplayAsync()` on a plan that is still recording throws `InvalidOperationException`.

Internally, `ReplayAsync()` first calls `FlushPendingCommands()`. A caller that refreshes the captured input buffers via a *kernel dispatch* (for example a video pipeline's per-frame preprocess into the stable input) has that dispatch sitting in the accelerator's pending encoder; the plan's own submit is a separate JS-side command buffer, so without the flush the pending work would land *after* the replay and the replay would read the previous frame's data. `writeBuffer` uploads (`CopyFromCPU`) were always safe because they are queue-ordered, not encoder-batched.

## Parameterized replay (patch surface)

To replay a single captured plan across a moving loop variable (the LLM decode case: one plan, patched per token) instead of re-capturing, set `CaptureScalarSnapshots = true` on the plan *before* the capture pass:

```csharp
WebGPUDispatchPlan plan = accelerator.BeginDispatchCapture();
plan.CaptureScalarSnapshots = true;   // must be set before the recorded dispatches
// ... run the fixed sequence ...
accelerator.EndDispatchCapture();
```

When set, every recorded dispatch also snapshots its packed `_scalar_params` upload (the retained buffer plus the exact bytes written) into `ScalarSnapshots`, and every recorded copy logs `(EntryIndex, SrcOffset, DstOffset, Size)` into `CopyEntries`. Entries with no scalar params are absent from `ScalarSnapshots`.

- `ScalarSnapshots` is a `List<(int EntryIndex, GPUBuffer Buffer, byte[] Bytes)>`.
- `CopyEntries` is a `List<(int EntryIndex, ulong SrcOffset, ulong DstOffset, ulong Size)>`.

This is the discovery surface for parameterized replay: a driver captures **two** plans at two values of the loop variable (for example an LLM decode step at past-length P and P+1), diffs the snapshots to find every scalar byte and copy offset that depends on the variable, and then patches only those between replays. No per-consumer knowledge of the packed param layouts is required.

Two patch methods apply the discovered deltas:

- **`PatchScalarInt(int snapshotIndex, int byteOffset, int value)`** - overwrites a 4-byte int inside a snapshotted dispatch's retained `_scalar_params` buffer via a queue-ordered `writeBuffer` (it lands before the next replay's submit).
- **`PatchCopyDstOffsetsAsync(int[] entryIndices, double[] newDstOffsets)`** - rewrites the `dstOffset` of recorded copy entries in place, one interop crossing for the whole batch. Entry indices come from `CopyEntries`; offsets are in bytes. This is the KV-cache-append case, where the destination row advances per decode token. The two array arguments must be equal length (otherwise `ArgumentException`).

Measured on SpawnDev.ILGPU.ML's qwen2.5-0.5b decode: 686 ms/token direct falls to 44.8 ms/token token-identical (patch 1.9 ms + plan 0.3 ms + GPU 23.3 ms).

## Timing helpers

Two diagnostic helpers measure replay cost. They are for instrumentation only.

- **`ReplayTimedAsync()`** replays the plan with a GPU timestamp (`timestamp-query`) at the start of every compute pass and the end of the last, then returns a JSON string aggregating GPU time by pipeline label (the kernel name): `{"supported":true,"passes":N,"ops":N,"totalMs":x,"spanMs":x,"kernels":[{"label","ms","count","maxMs"},...]}` sorted by total ms descending, or `{"supported":false,"reason":...}` when the device lacks the feature. It runs the same dispatches as `ReplayAsync()` (same validity contract) and waits for GPU completion internally, so no separate `SynchronizeAsync()` is needed. The per-pass `timestampWrites` add overhead, so it is not for frame timing. Chrome quantizes GPU timestamps to 100 us unless launched with `--enable-webgpu-developer-features`; totals telescope exactly either way, but fine per-kernel attribution wants the flag.

- **`GetLastReplayTimings()`** (static) returns `(double EncodeMs, double SubmitMs)` for the most recent `ReplayAsync()` on the page (any plan): `EncodeMs` is the JS re-encode loop and `SubmitMs` is `enc.finish()` + `queue.submit()`. GPU execution is *not* included - it completes asynchronously after the submit (await `SynchronizeAsync()` for that). The replay hot path records these numbers JS-side unconditionally via `performance.now()`; the accessor costs two interop reads.

## The CUDA graph capture parallel

The desktop equivalent lives in the forked `ILGPU.Runtime.Cuda` surface: `CudaStream.BeginCapture()` records a sequence of kernel launches into a `CudaGraph` (`EndCapture()`), which `Instantiate()`s to a `CudaGraphExec` that `Launch(stream)`s with a single `cuGraphLaunch`, collapsing per-kernel host dispatch overhead. `Accelerator.WithDefaultStream(stream)` reroutes `*StreamKernel` launches onto a capturable stream so they get captured without any per-call-site change. The proof (`SpawnDev.ILGPU.DemoConsole/CudaGraphCaptureProof.cs`) shows the same validity model as the WebGPU plan: capture records but does *not* execute, only the replays count; the captured kernel reads its per-step value (token id / KV position) from a stable-pointer device buffer whose address is fixed at capture time, and the host mutates that buffer's contents between replays. WebGPU's `PatchScalarInt` / `PatchCopyDstOffsetsAsync` fill the same per-replay-update role on the browser side.

## Validity contract and when not to use it

A replay re-runs the captured dispatches against the **same** buffers with the **same** parameters (except for the explicit patches above). It is correct only when every buffer the plan's bind groups reference is still alive and still plays the same role. Concretely:

- Capture under a **stable-buffer regime**: pre-warmed pool, stable param slots, no mid-forward buffer recycling.
- Capture at a **fixed input shape**. The plan records a fixed dispatch sequence with fixed buffer bindings and fixed dispatch dimensions; a different shape needs a different plan.
- **Write fresh input into the captured input buffer(s) before each replay.** The bindings are fixed; only the contents of the input buffers change between replays.
- **Do not interleave non-replay work that recycles those buffers** between replays.
- **Bind-group caching must be OFF during capture.** A cache hit rewrites its owned scalar buffer, which would retroactively corrupt earlier plan entries. `BeginDispatchCapture()` enforces this by throwing if `WebGPUBackend.EnableBindGroupCaching` is true.

`Dispose()` releases the plan: it returns the retained scalar buffers to the accelerator's scalar pool, destroys the retained coalesced buffers, and drops the JS plan array (releasing the recorded pipelines and bind groups to normal JS GC).

## Measured results

- SpawnDev.ILGPU.ML DAv3-5D on RTX 4070 hardware Dawn: direct forward 18.9 s falls to a 99.5 ms/frame replay, bit-exact (maxAbsDiff = 0), 2515 ops - 190x, at 1.36x of ORT-Web's 73 ms warm.
- qwen2.5-0.5b decode (patch surface): 686 ms/token falls to 44.8 ms/token, token-identical.
