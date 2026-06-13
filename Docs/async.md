# SpawnDev.ILGPU Async API

The browser backends - **WebGPU, WebGL, Wasm** - cannot do synchronous GPU&harr;CPU work. The browser's GPU and stream APIs are async-only (`GPUBuffer.mapAsync`, `queue.onSubmittedWorkDone`, async pixel readback, SharedArrayBuffer transfer), and the single JS/WASM thread cannot block on them without deadlocking. So every operation that **waits for GPU completion or observes a GPU result** has a **real async version** that browser callers MUST use. The synchronous versions remain for the desktop backends (CPU/CUDA/OpenCL), where blocking is fine and simpler.

**The governing principle (the sync/async contract, 2026-06-13):** an operation is **async-only on the browser backends if it WAITS for completion or OBSERVES a result** - its synchronous form **throws `NotSupportedException`** on WebGPU/WebGL/Wasm (the single thread cannot block-wait). An operation that is **fire-and-forget** - kernel dispatch, allocation, host&rarr;device upload, and **`Flush()` (submit)** - stays synchronous on every backend, because it does not wait so it cannot lie. Making the wait/observe surface throw (instead of silently flushing) means a desktop-only-tested "portable" library fails **loud** on browser instead of silently reading stale data.

**The rule in one line:** on a browser backend, to WAIT for GPU work or read it back, use the `*Async` method and `await` it. Concretely: sync GPU&rarr;CPU readbacks (`CopyToCPU` / `GetAsArray1D`) **throw**; **sync `Synchronize()` (wait for completion) THROWS** - use `await SynchronizeAsync()`; **sync `Flush()` (submit, no wait) is valid** on browser (it is fire-and-forget); and blocking the single thread on async work (`.Result` / `.Wait()`) **deadlocks**.

> This document exists because the sync-vs-async surface was tribal knowledge, and the gap bit us twice. (1) A teardown `finally` calling synchronous `Synchronize()` released GPU resources before the work finished - it must `await SynchronizeAsync()`. (2) When `Synchronize()` was made to throw on browser (2026-06-13), the fix initially broke `Allocate1D(data)`/`CopyFromCPU` on every browser backend, because core `CopyFromCPU` used `Synchronize()` internally for an upload it didn't need to wait on - now routed through `EnsureHostCopyConsumed()` (desktop waits, browser no-ops; uploads are sync-consumed). Lesson: distinguish "wait/observe" (async-only) from "fire-and-forget" (sync-safe).

## Sync-vs-async contract

| Operation | CPU / CUDA / OpenCL | WebGPU / WebGL / Wasm | Class |
|---|---|---|---|
| `Synchronize()` (wait for completion) | OK (blocks until done) | **THROWS `NotSupportedException`** - cannot block-wait on the single thread. Use `await SynchronizeAsync()` (wait) or `Flush()` (submit only). | wait |
| `Flush()` (submit, no wait) | OK (eager/no-op) | **OK - valid synchronously.** WebGPU submits the batched command encoder; WebGL/Wasm are no-ops (already fire-and-forget). Submit is synchronous on every backend (even a P2P stream's send enqueue), so there is no async twin - only WAIT and host read-back are async. | submit |
| `CopyToCPU()` / `CopyTo()` / `GetAsArray1D()` (GPU&rarr;CPU readback) | OK (blocks) | **THROWS `NotSupportedException`**. Use `await CopyToHostAsync()` / `GetAsArray1DAsync()`. | observe |
| `CopyToHostAsync()` (GPU&rarr;CPU) | OK (sync fallback) | `mapAsync` readback | observe |
| sync scalar `Reduce()` &rarr; T (ends in a readback) | OK | **THROWS**. Use `await ReduceAsync<T,TReduction>()`. | observe |
| `CopyFromCPU()` / `Allocate1D(data)` (CPU&rarr;GPU upload) | OK (waits for DMA) | **OK** - the upload is consumed synchronously (`queue.writeBuffer` / SAB memcpy / backing-array copy). Core routes its post-upload completion through `EnsureHostCopyConsumed()` (desktop waits, browser no-ops) - NOT the throwing sync `Synchronize()`. | fire-and-forget |
| `CreateScan()` / `CreateRadixSort()` / `CreateRadixSortPairs()` (sync builders) | OK | **OK** - the multi-pass scan/sort is fire-and-forget multi-dispatch (inter-pass barrier is a `Flush()` submit, not a wait), so the sync builders run on browser - there are no separate async builders. | fire-and-forget |
| `CopyFrom()` (GPU&rarr;GPU) | OK | OK on WebGPU (native `CopyBufferToBuffer`) / WebGL (TF readback) - these order it after the producing kernel. **On Wasm it is NOT ordered after a producing kernel**: the source copy is an immediate host-side `SharedArrayBuffer` memcpy that runs BEFORE the deferred worker dispatch, so `CopyFrom` from a buffer a kernel just wrote reads STALE data. After a producing kernel on Wasm, `await SynchronizeAsync()` first (or use `CopyFromAsync`, which drains the producer). [Tracked Wasm-backend ordering bug - 2026-06-12.] | fire-and-forget* |
| Kernel launch / `Dispatch` | OK | OK - batched into the shared command encoder, submitted on `Flush()`. | fire-and-forget |

**Corollaries (each has already bitten us):**
- **To WAIT for GPU work on a browser backend** (before a readback, or before disposing buffers a pending dispatch references), `await accelerator.SynchronizeAsync()`. Sync `Synchronize()` THROWS there (it is the wait surface). To merely SUBMIT batched work without waiting (e.g. periodic flush during a long dispatch loop), call `Flush()` - that is sync-valid on browser.
- **GPU&rarr;CPU readback (and any wait) is the async-only boundary.** GPU&rarr;GPU (`CopyFrom`) and CPU&rarr;GPU (`CopyFromCPU`) uploads + dispatch + `Flush()` are fire-and-forget and sync-safe on browser; prefer them over a readback+reupload. **EXCEPTION (Wasm):** `CopyFrom` from a buffer a kernel just wrote is NOT ordered after that kernel on Wasm (deferred worker dispatch vs. immediate host memcpy) and reads stale data - `await SynchronizeAsync()` (or `CopyFromAsync`) before it. (Tracked Wasm-backend ordering bug, 2026-06-12.)
- A kernel that dispatches reading a buffer keeps that buffer alive until the next flush (WebGPU batches dispatches). Don't dispose a buffer a pending dispatch references before `await SynchronizeAsync()`.

The sync counterparts guard themselves: the wait/observe ops throw `NotSupportedException` on browser with a message naming the `*Async` method to use instead (sync readbacks also call `accelerator.EnsureSyncReadbackSupported("<MethodName>")`). That throw is the canonical "sync wait/observe op on a browser backend" failure - it is loud by design.

## The async methods

All three browser backends (WebGPU + WebGL + Wasm) implement the same async surface via their own mechanism:

- **WebGPU** &rarr; `GPUBuffer.mapAsync` + `queue.onSubmittedWorkDone`
- **WebGL** &rarr; transform-feedback / async pixel readback (`ReadbackAndGetUint8ArrayAsync`, `BlitAndDrawAsync`)
- **Wasm** &rarr; SharedArrayBuffer-backed transfer

### Accelerator / lifecycle
- **`SynchronizeAsync()` &rarr; Task** - async wait for all submitted GPU work to COMPLETE. The browser-safe replacement for `Synchronize()` (which THROWS on the three browser backends - it is the wait surface). On WebGPU awaits `onSubmittedWorkDone`; on desktop completes synchronously. Call before a readback or before disposing buffers a pending dispatch references.
- **`Flush()` (sync)** - SUBMIT batched/pending work to the device WITHOUT waiting. Fire-and-forget, so the **sync `Flush()` is valid on browser** (WebGPU submits the command encoder; WebGL/Wasm no-op). Use it to submit periodically during a long dispatch loop, where you'd reach for `Synchronize()` on desktop. Submit is synchronous on every backend (even a P2P stream's send enqueue is a sync call), so there is no async `Flush` twin - only WAIT (`SynchronizeAsync`) and host read-back are async at the GPU boundary.
- **`EnsureHostCopyConsumed()` (protected, core)** - the internal completion step for a synchronous host&rarr;device copy: waits on desktop (DMA in flight), no-ops on browser (upload sync-consumed). It is why `Allocate1D(data)` / `CopyFromCPU` stay sync-safe on browser instead of throwing via `Synchronize()`. Backends with async upload (P2P) override it to throw.
- **`CreateAcceleratorAsync()` / `CreateAsync()` / `CreatePreferredAcceleratorAsync()` / `CreateWebGPUAcceleratorAsync()` / `CreateWebGLAcceleratorAsync()` / `CreateWasmAcceleratorAsync()` &rarr; Task&lt;Accelerator&gt;** - async device/accelerator construction (one per browser backend plus generic/preferred forms). Browser adapter/device acquisition is async (`requestAdapter` / `requestDevice`, WebGL context creation, Wasm worker init).
- **`GetDevicesAsync()` / `GetDefaultDeviceAsync()` &rarr; Task&lt;...&gt;** - async device enumeration.
- **`DisposeAsync()` &rarr; ValueTask** - async teardown where disposal awaits GPU completion.

### Memory readback (GPU&rarr;CPU - the async-only boundary)
- **`CopyToHostAsync<T>(offset, count)` &rarr; Task&lt;T[]&gt;** - async GPU&rarr;CPU readback (`mapAsync` on WebGPU). THE primary replacement for `CopyToCPU` / `CopyTo` / `GetAsArray1D` on browser. Full-buffer and ranged `(offset, count)` overloads.
- **`CopyToCPUAsync()` / `CopyToCPUUnsafeAsync()` / `CopyToRawAsync()` &rarr; Task&lt;...&gt;** - async forms of the ILGPU-core readback surface.
- **`GetAsArray1DAsync()` / `GetAsArray2DAsync()` / `GetAsArray3DAsync()` &rarr; Task&lt;...&gt;** - async forms of the `GetAsArrayND()` family (1D/2D/3D).
- **`CopyToHostTypeArrayAsync()` / `CopyToHostUint8ArrayAsync()` &rarr; Task&lt;...&gt;** - typed async readback straight to a JS typed array (zero-extra-copy canvas / IO paths).

### Memory upload / copy (CPU&rarr;GPU, GPU&rarr;GPU)
- **`CopyFromAsync(...)` &rarr; Task** - async copy into a buffer view. (`CopyFrom` / `CopyFromCPU` are sync-safe on all backends; the async form composes with other awaits.)
- **`CopyFromCPUUnsafeAsync()` / `CopyFromPageLockedAsync()` / `CopyToPageLockedAsync()` &rarr; Task** - async page-locked / unsafe CPU&harr;GPU transfers.
- **`MemcpyAsync()` &rarr; Task** - async device memcpy (the cross-backend memcpy primitive; the CUDA-native path uses `cuMemcpyAsync` / `cuMemsetD8Async`).
- **`MemSetToZeroAsync()` / `ClearAsync()` &rarr; Task** - async zero-fill / clear.

### Compute / algorithms
- **`ReduceAsync<T, TReduction>(...)` / `ReduceAsync<T, TStride, TReduction>(...)` &rarr; Task&lt;T&gt;** - real async GPU reduction (sum/min/max), extension methods in `ILGPU.Algorithms/ReductionExtensions.cs`. End-to-end async because a reduction ends in a GPU&rarr;CPU readback; the sync `Reduce` calls `EnsureSyncReadbackSupported("ReduceAsync")`.
- **`UniqueAsync(...)` &rarr; Task&lt;...&gt;** - async form of `Unique` (ends in a readback of the result length).
- **Async kernel dispatch (named per backend):** `DispatchAsync(...)` (WebGPU) / `RunKernelAsync(...)` (Wasm) &rarr; Task. (WebGL dispatches via draw calls under `BlitAndDrawAsync`.) The same logical "async dispatch" operation under backend-specific names.
- **`ExecuteAsync(...)` &rarr; Task** - async operator/graph-node execution; the override point for ops that read GPU values back mid-execution (control-flow conditions, dynamic-shape inputs). Sync `Execute(...)` degrades on browser.

### Codegen / debug / canvas / present
- **`BeginCodeGenerationAsync()` &rarr; Task&lt;...&gt;** - async kernel code generation.
- **`DisassembleAsync()` &rarr; Task&lt;string&gt;** - async kernel disassembly (debug).
- **`PresentAsync()` &rarr; Task** - async present of a rendered frame to a canvas (WebGPU and WebGL).
- **`BlitAndDrawAsync()` / `ReadbackAndGetUint8ArrayAsync()` &rarr; Task&lt;...&gt;** (WebGL) - WebGL's own async dispatch+readback path (transform-feedback / async pixel readback).

> Higher-level domain async helpers built ON the above (ML/training readback like `FetchToCPUAsync` / `LoadParametersAsync`, sparse-matrix `SparseMatrix*Async`, P2P/swarm and canvas-renderer async) are documented with their own subsystems, not here - they are not the GPU-boundary contract.

## Canonical usage patterns

**Inference / decode loop (browser-safe):**
```csharp
var outputs = await session.RunAsync(inputs);                 // async graph exec, async readbacks inside
await accelerator.SynchronizeAsync();                         // flush before reading
var logits = await readBuf.CopyToHostAsync<float>(0, vocab);  // GPU->CPU, async
```

**Teardown / `finally` (browser-safe):**
```csharp
finally
{
    await accelerator.SynchronizeAsync();   // NOT Synchronize() - that THROWS on browser (it's the wait surface)
    // ... release / clear ...
}
```

## Anti-patterns

- Using `accelerator.Synchronize()` on a browser backend &rarr; **throws `NotSupportedException`** (it is the wait surface, which the single thread can't honor). Use `await SynchronizeAsync()` to wait, or `Flush()` to submit without waiting. (Before 2026-06-13 it silently flushed without waiting, so a teardown `finally` released resources before the GPU finished - the throw now makes that misuse loud.)
- Blocking the single browser thread on async GPU work - `someAsync().Result` / `.Wait()`, or `Task.Run(() => syncGpuWork()).Result` &rarr; **deadlock** (the thread can't pump the event loop the awaited GPU callback needs).
- `buf.CopyToCPU(host)` / `buf.GetAsArray1D()` on browser &rarr; `NotSupportedException`. Use `await CopyToHostAsync()`.
- `Task.Run(() => syncGpuWork()).Result` / `.Wait()` &rarr; fake-async; blocks the single browser thread and deadlocks.
- Disposing a buffer a pending dispatch references before `await SynchronizeAsync()`.

## Known gaps / tracked items

- **Tiny-buffer readback NRE:** `CopyToHostAsync` on a 1-element / 4-byte WebGPU staging buffer threw an NRE on SpawnDev.ILGPU 4.9.4 (tracked). Consumers may guard it as an optimization-only skip. To be re-verified against the current `WebGPUMemoryBuffer`; if fixed, record the min-version here so consumers can drop the catch.

---

*Async reference authored by Tuvok (#3) from a read of the call sites + the ML consumer; landed by Geordi (#4), owner of SpawnDev.ILGPU. The sync-vs-async contract above is verified against the source; the per-method inventory is verified as the XML `<summary>` doc-comments are added to each method. Per-method exact signatures live in those doc-comments.*
