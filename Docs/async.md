# SpawnDev.ILGPU Async API

The browser backends - **WebGPU, WebGL, Wasm** - cannot do synchronous GPU&harr;CPU work. The browser's GPU and stream APIs are async-only (`GPUBuffer.mapAsync`, `queue.onSubmittedWorkDone`, async pixel readback, SharedArrayBuffer transfer), and the single JS/WASM thread cannot block on them without deadlocking. So every operation that crosses the GPU&harr;CPU boundary, or that waits for the GPU, has a **real async version** that browser callers MUST use. The synchronous versions remain for the desktop backends (CPU/CUDA/OpenCL), where blocking is fine and simpler.

**The rule in one line:** on a browser backend, when you need to WAIT for GPU work or read it back to the host, use the `*Async` method and `await` it. Concretely: sync GPU&rarr;CPU readbacks (`CopyToCPU` / `GetAsArray1D`) **throw `NotSupportedException`**; sync `Synchronize()` does NOT throw but only FLUSHES the queued work without waiting (use `SynchronizeAsync` when you need it finished); and blocking the single thread on async work (`.Result` / `.Wait()`) **deadlocks**.

> This document exists because the async surface was never written down, and the gap bit us: a teardown `finally` calling the synchronous `Synchronize()` only flushes (it submits the queued work but does not wait), so it released GPU resources before the work had finished - it must `await SynchronizeAsync()`. Don't let the sync-vs-async contract stay tribal knowledge.

## Sync-vs-async contract

| Operation | CPU / CUDA / OpenCL | WebGPU / WebGL / Wasm |
|---|---|---|
| `Synchronize()` (sync flush) | OK (blocks until done) | **Flushes/submits the queued work but does NOT wait for it** - it starts the work and returns (not a no-op, not a deadlock). To WAIT for completion, `await SynchronizeAsync()`. |
| `CopyToCPU()` / `CopyTo()` (GPU&rarr;CPU) | OK (blocks) | **THROWS `NotSupportedException`**. Use `await CopyToHostAsync()`. |
| `GetAsArray1D()` (GPU&rarr;CPU) | OK (blocks) | **THROWS** (sync readback). Use `await GetAsArray1DAsync()` / `CopyToHostAsync()`. |
| `CopyToHostAsync()` (GPU&rarr;CPU) | OK (sync fallback) | `mapAsync` readback |
| `CopyFrom()` (GPU&rarr;GPU) | OK | OK - native `CopyBufferToBuffer` (WebGPU) / TF readback (WebGL). Safe everywhere. |
| `CopyFromCPU()` (CPU&rarr;GPU) | OK | OK - immediate `queue.writeBuffer` (WebGPU). No command-encoder hazard. |
| Kernel launch / `Dispatch` | OK | OK - batched into the shared command encoder, submitted on flush. |

**Corollaries (each has already bitten us):**
- **When you need to WAIT for GPU work on a browser backend** (before a readback, or before disposing buffers a pending dispatch references), `await accelerator.SynchronizeAsync()`. The synchronous `Synchronize()` only FLUSHES (submits the queued work) and returns without waiting - right when you just need to kick the work off, wrong when you need the result to be ready.
- **GPU&rarr;CPU is the async-only boundary.** GPU&rarr;GPU (`CopyFrom`) and CPU&rarr;GPU (`CopyFromCPU`) are sync-safe on all backends; prefer them over a readback+reupload.
- A kernel that dispatches reading a buffer keeps that buffer alive until the next flush (WebGPU batches dispatches). Don't dispose a buffer a pending dispatch references before `await SynchronizeAsync()`.

The sync counterparts guard themselves: many call `accelerator.EnsureSyncReadbackSupported("<MethodName>")`, which throws on a backend that can't do sync readback (browser) with a message naming the `*Async` method to use instead. That throw is the canonical "sync op on a browser backend" failure.

## The async methods

All three browser backends (WebGPU + WebGL + Wasm) implement the same async surface via their own mechanism:

- **WebGPU** &rarr; `GPUBuffer.mapAsync` + `queue.onSubmittedWorkDone`
- **WebGL** &rarr; transform-feedback / async pixel readback (`ReadbackAndGetUint8ArrayAsync`, `BlitAndDrawAsync`)
- **Wasm** &rarr; SharedArrayBuffer-backed transfer

### Accelerator / lifecycle
- **`SynchronizeAsync()` &rarr; Task** - async flush + wait for all submitted GPU work. The browser-safe replacement for `Synchronize()` when you need to WAIT (the sync `Synchronize()` only flushes/submits the queued work without waiting on the three browser backends). On WebGPU awaits `onSubmittedWorkDone`; on desktop completes synchronously. Call before a readback or before disposing buffers a pending dispatch references.
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
    await accelerator.SynchronizeAsync();   // NOT Synchronize() - that only flushes (submits) without waiting
    // ... release / clear ...
}
```

## Anti-patterns

- Using `accelerator.Synchronize()` on a browser backend when you actually need to WAIT for the result &rarr; it only flushes (submits) and returns immediately, so the GPU work is not finished and you read stale/empty data, or release a resource the work still needs. (A draft opt-in `finally` did exactly this on 2026-06-04 - it must `await SynchronizeAsync()` to drain before clearing/disposing.)
- Blocking the single browser thread on async GPU work - `someAsync().Result` / `.Wait()`, or `Task.Run(() => syncGpuWork()).Result` &rarr; **deadlock** (the thread can't pump the event loop the awaited GPU callback needs).
- `buf.CopyToCPU(host)` / `buf.GetAsArray1D()` on browser &rarr; `NotSupportedException`. Use `await CopyToHostAsync()`.
- `Task.Run(() => syncGpuWork()).Result` / `.Wait()` &rarr; fake-async; blocks the single browser thread and deadlocks.
- Disposing a buffer a pending dispatch references before `await SynchronizeAsync()`.

## Known gaps / tracked items

- **Tiny-buffer readback NRE:** `CopyToHostAsync` on a 1-element / 4-byte WebGPU staging buffer threw an NRE on SpawnDev.ILGPU 4.9.4 (tracked). Consumers may guard it as an optimization-only skip. To be re-verified against the current `WebGPUMemoryBuffer`; if fixed, record the min-version here so consumers can drop the catch.

---

*Async reference authored by Tuvok (#3) from a read of the call sites + the ML consumer; landed by Geordi (#4), owner of SpawnDev.ILGPU. The sync-vs-async contract above is verified against the source; the per-method inventory is verified as the XML `<summary>` doc-comments are added to each method. Per-method exact signatures live in those doc-comments.*
