# Memory & Buffers

This guide covers GPU memory management in SpawnDev.ILGPU — allocating buffers, transferring data, and reading results back asynchronously.

## Core Concepts

GPU memory is separate from CPU memory. You allocate **buffers** on the GPU, pass **views** into kernels, and **copy** results back to CPU arrays when done.

| Concept | Description |
|---------|-------------|
| **`MemoryBuffer1D<T, Stride>`** | Host-side handle to GPU memory — allocated and disposed on the CPU |
| **`ArrayView<T>`** | GPU-side reference — passed to kernels, indexable like an array |
| **`CopyToHostAsync`** | Reads data from GPU back to a CPU array |
| **`Flush`** | Submits queued GPU work to the device **without waiting** — valid on every backend (no-op on desktop; submits the batched encoder/queue on the browser backends) |
| **`SynchronizeAsync`** | Submits **and waits** for all GPU work to complete — the browser-safe way to wait (the synchronous `Synchronize()` **throws** on the browser backends) |

## Allocating Buffers

### 1D Buffers

```csharp
// Allocate empty buffer
using var buffer = accelerator.Allocate1D<float>(1024);

// Allocate and initialize from an array
float[] hostData = { 1, 2, 3, 4, 5 };
using var buffer = accelerator.Allocate1D(hostData);
```

### 2D Buffers

```csharp
// Allocate 2D buffer (e.g., for image processing)
int width = 800, height = 600;
using var buffer = accelerator.Allocate2DDenseX<uint>(new Index2D(width, height));

// Access extent for kernel launch
kernel(buffer.IntExtent, buffer.View, ...);
```

### Stride Types

Stride controls how multi-dimensional data is laid out in memory:

| Stride | Use Case |
|--------|----------|
| `Stride1D.Dense` | 1D buffers (default) |
| `Stride2D.DenseX` | 2D buffers — rows are contiguous (X varies fastest) |

When in doubt, use `Dense` for 1D and `DenseX` for 2D. These match standard C# array layouts.

## Passing Data to the GPU

### Via Buffer Initialization

```csharp
var data = new float[] { 1, 2, 3, 4, 5 };
using var buffer = accelerator.Allocate1D(data);
// Buffer now contains the array data on the GPU
```

### Via CopyFromCPU

```csharp
using var buffer = accelerator.Allocate1D<float>(1024);
float[] newData = ComputeNewData();
buffer.CopyFromCPU(newData);
```

### Via Scalar Parameters

Scalar values (`int`, `float`, etc.) are passed directly to kernels as parameters — no buffer needed:

```csharp
static void MyKernel(Index1D index, ArrayView<float> data, float multiplier, int offset)
{
    data[index] = data[index] * multiplier + offset;
}

// Pass scalars directly
kernel((Index1D)length, buffer.View, 2.5f, 10);
```

### Via CopyFromJS (Browser Backends — Zero-Copy from JS)

When data originates in JavaScript — fetched from a `WebSocket`, decoded by a `FileReader`, pulled from `IndexedDB`, returned by `fetch`, produced by `MediaRecorder`, etc. — going through `CopyFromCPU` would marshal every byte through .NET arrays for no reason. `CopyFromJS` is the zero-copy path: data stays in JS, gets pushed straight into the GPU buffer. Available on **all three browser backends** (WebGPU, WebGL, Wasm). Not available on desktop (CUDA / OpenCL / CPU don't have JS).

```csharp
using SpawnDev.ILGPU;
using SpawnDev.BlazorJS.JSObjects;

// Get the IBrowserMemoryBuffer for any GPU buffer
var browserBuffer = (IBrowserMemoryBuffer)((IArrayView)buffer.View).Buffer;

// From a JS TypedArray (e.g. data already in JS land)
Int16Array jsData = ...;            // 16-bit PCM samples from a WebSocket message
browserBuffer.CopyFromJS(jsData);

// From a raw ArrayBuffer (e.g. a fetch() result body)
ArrayBuffer rawBytes = ...;          // image bytes from a fetch response
browserBuffer.CopyFromJS(rawBytes);

// Optional target offset — write into the middle of an existing GPU buffer
browserBuffer.CopyFromJS(jsData, targetByteOffset: 1024);
```

**Per-backend behavior:**
- **WebGPU** — calls `queue.WriteBuffer` directly on the JS-side typed array. Zero copy through .NET, no managed allocation.
- **WebGL** — copies the JS bytes into the buffer's backing array and sets `NeedsUpload = true`; the data is uploaded to the GPU on the next dispatch (not immediately, by design — WebGL has no async write queue).
- **Wasm** — pure JS→JS copy within the SharedArrayBuffer that backs Wasm linear memory. Worker threads see the new bytes immediately.

**When to use it:**
- Streaming pipelines where JS produces data (audio decode, video frames, network bytes)
- Loading models / assets from `fetch` or `IndexedDB`
- Canvas → GPU pipelines where you read `ImageData` once on the JS side and want to skip the .NET round trip

If your data is already in a `byte[]` / `float[]` / etc., use `CopyFromCPU` — that's the right path for managed-array sources. `CopyFromJS` is specifically for when the data hasn't crossed into .NET yet and you want to keep it that way.

### Via CopyFromStreamAsync (Bounded Streaming — Browser Zero-Copy from `IJSReadStream`)

`CopyFromJS` uploads data that is *already fully resident* in a JS `TypedArray` / `ArrayBuffer`. When the source is a **stream** — a multi-hundred-MB model file, a `Blob` / `File`, a WebTorrent piece stream — you don't want the whole thing in memory at once. `ArrayView<T>.CopyFromStreamAsync` streams a .NET `Stream` into a GPU buffer **in bounded chunks** (default 16 MiB), reading exactly `view.Length * sizeof(T)` bytes:

```csharp
using SpawnDev.ILGPU;

using var buf = accelerator.Allocate1D<float>(weightCount);

await buf.View.CopyFromStreamAsync(sourceStream);                              // default 16 MiB chunks
await buf.View.CopyFromStreamAsync(sourceStream, chunkSizeInBytes: 4 * 1024 * 1024);
await buf.View.SubView(offset, count).CopyFromStreamAsync(sourceStream);       // into a sub-range
```

- **All 6 backends.** On CUDA / OpenCL / CPU it `await`s `Stream.ReadExactlyAsync` into a pooled buffer and copies each chunk (a model streaming off disk / network never blocks a thread). Correct on the browser backends too via the managed hop.
- **Browser zero-copy fast path.** If the source `Stream` implements `SpawnDev.BlazorJS.Toolbox.IJSReadStream`, the browser backends read each chunk as a JS `Uint8Array` and upload it via `CopyFromJS` — the bytes go **JS → GPU without ever entering the .NET/WASM managed heap**. (WebGPU requires 4-byte-aligned transfers, which fp32 and even-count `Half` always satisfy; an odd-count `Half` upload falls back to the padded managed path.)
- **Exact length.** A stream that ends before `view.Length * sizeof(T)` bytes throws `EndOfStreamException` — a truncated asset surfaces instead of silently zero-padding the tail.

**`IJSReadStream`** (in `SpawnDev.BlazorJS.Toolbox`) is the primitive that enables the fast path — a `Stream` whose data can be read *while it stays in JS*:

| Member | Meaning |
|--------|---------|
| `bool CanReadSync` | `true` if a synchronous read works (`ArrayBufferStream` — data already in JS memory); `false` for async-only sources (`BlobStream`, network streams). |
| `Task<Uint8Array> ReadUint8ArrayAsync(int count, …)` | Reads up to `count` bytes from the current `Position` as a JS `Uint8Array` (stays JS-side), advances `Position`; empty `Uint8Array` at EOF. |
| `Uint8Array ReadUint8Array(int count)` | Synchronous counterpart (throws when `CanReadSync` is false). |

Implemented by **`BlobStream`** (`Blob` / `File`, `CanReadSync = false`) and **`ArrayBufferStream`** (`ArrayBuffer`, `CanReadSync = true`). Any `Stream` that implements it gets the zero-copy upload automatically.

## Copying Between GPU Buffers (GPU→GPU)

Use `CopyFrom` to copy data between GPU buffers. This works on **all six backends** — it's a native GPU operation with no CPU involvement:

```csharp
using var source = accelerator.Allocate1D(new float[] { 1, 2, 3, 4, 5 });
using var dest = accelerator.Allocate1D<float>(5);

// Copy GPU→GPU (fast, native, works everywhere)
dest.CopyFrom(source);
```

On WebGPU this maps to `CopyBufferToBuffer`. On CUDA/OpenCL it's a device-to-device memcpy. On Wasm it copies within the SharedArrayBuffer. No shader compilation, no kernel dispatch.

### Cross-Backend Buffer Operations Reference

Not all copy operations work the same way on every backend. This table shows what works and what throws:

| Operation | Method | CPU / CUDA / OpenCL | WebGPU | WebGL | Wasm |
|-----------|--------|--------------------:|-------:|------:|-----:|
| **GPU→GPU** | `CopyFrom` | Sync | `CopyBufferToBuffer` | TF readback | SharedArrayBuffer |
| **CPU→GPU** | `CopyFromCPU` / `Allocate1D(data)` | Sync | `queue.WriteBuffer` | `texImage2D` | SharedArrayBuffer |
| **JS→GPU** | `IBrowserMemoryBuffer.CopyFromJS` | N/A | `queue.WriteBuffer` | Backing-array copy + `NeedsUpload` | JS→JS within SharedArrayBuffer |
| **GPU→CPU (async)** | `CopyToHostAsync` | Sync fallback | `mapAsync(Read)` | Readback | SharedArrayBuffer |
| **GPU→CPU (sync)** | `CopyTo` / `CopyToCPU` / `GetAsArray1D` | Sync | **THROWS** | **THROWS** | **THROWS** |
| **Stream→GPU (chunked)** | `ArrayView.CopyFromStreamAsync` | `ReadExactlyAsync` + copy | `IJSReadStream`→`CopyFromJS` | `IJSReadStream`→backing copy | `IJSReadStream`→JS copy |
| **GPU→Stream (chunked)** | `ArrayView.CopyToStreamAsync` | readback + `WriteAsync` | `IJSWriteStream`→`WriteUint8ArrayAsync` | `IJSWriteStream`→`WriteUint8ArrayAsync` | `IJSWriteStream`→`WriteUint8ArrayAsync` |

**Key rules:**
- **GPU→GPU copies: always use `CopyFrom`.** It's fast, native, and works on all backends.
- **GPU→CPU reads: always use `CopyToHostAsync`.** The sync methods (`CopyTo`, `CopyToCPU`, `GetAsArray1D`) throw `NotSupportedException` on browser backends (WebGPU, WebGL, Wasm) because they require async GPU readback (`mapAsync`).
- **Never replace `CopyFrom` with a kernel dispatch** (e.g., a Scale-by-1 kernel). `CopyFrom` is a hardware copy command — it's faster, simpler, and doesn't require shader compilation. Using a kernel dispatch for GPU→GPU copies can cause initialization errors on WebGPU when the accelerator isn't fully ready.

## Reading Data from the GPU

### Synchronization

After launching a kernel, you must synchronize before reading results:

```csharp
kernel((Index1D)length, bufA.View, bufB.View, bufC.View);

// Flush() SUBMITS the batched work without waiting — valid on every backend
// (no-op on desktop; submits the command encoder / worker queue on the browser backends).
accelerator.Flush();

// SynchronizeAsync() SUBMITS and WAITS for GPU completion — the browser-safe way to wait.
await accelerator.SynchronizeAsync();
```

> **Semantics (the sync/async contract).** Three distinct surfaces — none of them transfer data (use `CopyToHostAsync()` to read results back):
>
> - **`Flush()`** submits pending work to the device but does **not** wait. It is fire-and-forget, so it is valid **synchronously on every backend** (desktop: a no-op, work is already submitted as it is enqueued; browser: submits the batched command encoder / worker queue and returns). Use it to submit periodically during a long dispatch loop. It is the honestly-named replacement for the old browser habit of calling `Synchronize()` as a non-blocking flush.
> - **`SynchronizeAsync()`** submits **and waits** for all submitted GPU work to complete. This is the portable way to wait before disposing buffers a pending dispatch references (a host readback via `CopyToHostAsync()` already drains, so it does not need a separate wait). `await` it.
> - **`Synchronize()`** (synchronous wait) blocks until done on the desktop backends (CPU/CUDA/OpenCL) but **throws `NotSupportedException` on the browser backends (WebGPU/WebGL/Wasm)** — the single Blazor thread cannot block-wait, so the throw makes the misuse loud instead of silently reading stale data. To wait on a browser backend, `await SynchronizeAsync()`; to submit without waiting, call `Flush()`.
>
> See [async.md](async.md) for the full per-operation sync/async surface.

### CopyToHostAsync — Unified Extension Method

The simplest way to read GPU data. Works with **all six backends** (WebGPU, WebGL, Wasm, CUDA, OpenCL, CPU):

```csharp
using SpawnDev.ILGPU;

// Read entire buffer to a new array
float[] results = await buffer.CopyToHostAsync<float>();

// Read 1D buffer directly
var results = await buffer1D.CopyToHostAsync<float>();
```

### CopyToHostAsync — Pre-allocated Destination

For render loops, avoid allocating a new array every frame:

```csharp
// Allocate once
float[] cachedResults = new float[bufferLength];

// Reuse every frame
await buffer.CopyToHostAsync(cachedResults);
```

### ArrayView&lt;T&gt;.CopyToHostAsync — Partial Readback (4.9.3+)

Reads only a sub-range of a GPU buffer to a host array. The byte range outside the view never crosses the device-host boundary - this is a real per-backend partial copy, **not** a full-buffer readback followed by a CPU-side slice.

Use this when a single GPU buffer holds multiple logical regions (per-channel image planes, per-tensor model outputs, per-frame audio chunks, etc.) and you need each region as its own host array:

```csharp
using SpawnDev.ILGPU;

// One GPU buffer with three logical regions (Y / U / V planes for YUV 4:2:0):
var y = await dRecon.View.SubView(0,            yLen ).CopyToHostAsync();
var u = await dRecon.View.SubView(yLen,         uvLen).CopyToHostAsync();
var v = await dRecon.View.SubView(yLen + uvLen, uvLen).CopyToHostAsync();
```

Each call only transfers its own slice's bytes. Compare with the full-buffer pattern, which reads the whole buffer and slices on the CPU:

```csharp
// AVOID — reads the entire dRecon buffer to host every call,
// then slices on the CPU. Fine for small buffers, wasteful for large ones.
var full = await dRecon.CopyToHostAsync<byte>();
var y = new byte[yLen];  Buffer.BlockCopy(full, 0,             y, 0, yLen);
var u = new byte[uvLen]; Buffer.BlockCopy(full, yLen,          u, 0, uvLen);
var v = new byte[uvLen]; Buffer.BlockCopy(full, yLen + uvLen,  v, 0, uvLen);
```

**Per-backend implementation** (no fallback to full-buffer + slice on any backend):

| Backend | Underlying primitive |
|---|---|
| **WebGPU** | `queue.CopyBufferToBuffer(srcBuf, srcByteOffset, staging, 0, byteCount)` -> `mapAsync(Read, 0, byteCount)`. Staging is sized to the slice, not the parent buffer. |
| **WebGL** | GL-worker `ReadbackAndGetUint8ArrayAsync(buf, sourceByteOffset, byteCount)` partial range path. |
| **Wasm** | `new Uint8Array(SharedBuffer, byteOffset, byteCount)` window onto the SAB slot. The rest of wasm linear memory is not touched. |
| **CUDA / OpenCL / CPU** | ILGPU's native `view.CopyToCPU(target)`. The view's start offset and length encode the partial range, so this is one `cudaMemcpy` / `clEnqueueReadBuffer` / direct memcpy of just the slice's bytes. |

**Two overloads** are provided so that `MemoryBuffer1D.View.SubView(...)` resolves naturally without an explicit cast:

```csharp
public static Task<T[]> CopyToHostAsync<T>(this ArrayView<T> view)
    where T : unmanaged;

public static Task<T[]> CopyToHostAsync<T, TStride>(this ArrayView1D<T, TStride> view)
    where T : unmanaged
    where TStride : struct, IStride1D;
```

The `ArrayView1D` overload forwards to the `ArrayView<T>` overload via `view.BaseView`, which is already the sliced range on a SubView'd 1D view.

**Throws:**
- `InvalidOperationException` if the view has no backing buffer.
- `ArgumentOutOfRangeException` if the view's byte range exceeds the buffer's length.

**When NOT to use this overload:**
- You want the entire buffer's contents - use `buffer.CopyToHostAsync<T>()` directly. The `MemoryBuffer` overload exists for that case and avoids the SubView object construction.
- You're writing into a pre-allocated array - use `buffer.CopyToHostAsync(targetArray)` for the per-frame render loop pattern. The partial-readback overload always allocates a fresh `T[]`.

### CopyToHostUint8ArrayAsync — JavaScript Interop

Returns a JavaScript `Uint8Array` for direct use with browser APIs (Canvas, WebGL textures, etc.):

```csharp
// Get raw bytes as a JS Uint8Array
using var bytes = await browserBuffer.CopyToHostUint8ArrayAsync(0, byteCount);

// Use directly with Canvas ImageData
destPixels.Set(bytes);
```

This is the fastest path for GPU → Canvas rendering. See [Advanced Patterns](advanced-patterns.md) for the full render pipeline.

### IBrowserMemoryBuffer Interface

All GPU-backed buffers implement `IBrowserMemoryBuffer`, which provides `CopyToHostUint8ArrayAsync`:

```csharp
var internalBuffer = ((IArrayView)outputBuffer).Buffer;

if (internalBuffer is IBrowserMemoryBuffer browserBuffer)
{
    // Fast JS-interop readback
    using var bytes = await browserBuffer.CopyToHostUint8ArrayAsync(0, byteCount);
    destPixels.Set(bytes);
}
else
{
    // CPU fallback
    outputBuffer.View.BaseView.CopyToCPU(resultArray);
}
```

### CopyToStreamAsync — Bounded Save (Browser Zero-Copy to `IJSWriteStream`)

The save-side mirror of [`CopyFromStreamAsync`](#via-copyfromstreamasync-bounded-streaming--browser-zero-copy-from-ijsreadstream). `ArrayView<T>.CopyToStreamAsync` streams a GPU buffer **out** to a .NET `Stream` **in bounded chunks** (default 16 MiB) — so saving a multi-hundred-MB buffer to OPFS / disk / network never materializes the whole thing at once:

```csharp
using SpawnDev.ILGPU;

// Save to any .NET Stream (e.g. a MemoryStream, a file):
using var ms = new MemoryStream();
await buf.View.CopyToStreamAsync(ms);                              // default 16 MiB chunks
await buf.View.CopyToStreamAsync(ms, chunkSizeInBytes: 4 * 1024 * 1024);
await buf.View.SubView(offset, count).CopyToStreamAsync(ms);       // just a sub-range
```

- **All 6 backends.** The base path does an async GPU→CPU readback of each chunk (`CopyToRawAsync`) and writes it to the target `Stream` — correct everywhere via the managed hop.
- **Browser zero-copy fast path.** If the target `Stream` implements `SpawnDev.BlazorJS.Toolbox.IJSWriteStream`, the browser backends read each chunk back as a JS `Uint8Array` (`CopyToHostUint8ArrayAsync`) and write it via `WriteUint8ArrayAsync` — the bytes go **GPU → JS → stream without ever entering the .NET/WASM managed heap**.

**`IJSWriteStream`** (in `SpawnDev.BlazorJS.Toolbox`) is the write-side sibling of `IJSReadStream`:

| Member | Meaning |
|--------|---------|
| `bool CanWriteSync` | `true` if a synchronous write works (`ArrayBufferStream` — in-memory JS buffer); `false` for async-only sinks (`FileSystemHandleWritableStream` — OPFS / disk). |
| `Task WriteUint8ArrayAsync(Uint8Array data, …)` | Writes `data` at the current `Position` (stays JS-side), advances `Position`. |
| `void WriteUint8Array(Uint8Array data)` | Synchronous counterpart (throws when `CanWriteSync` is false). |

Implemented by **`FileSystemHandleWritableStream`** (OPFS / disk, `CanWriteSync = false`) and **`ArrayBufferStream`** (in-memory `ArrayBuffer`, `CanWriteSync = true` — a full duplex `IJSReadStream` + `IJSWriteStream`).

**Saving a GPU buffer to an OPFS file — the full pattern:**

```csharp
using SpawnDev.BlazorJS.JSObjects;
using SpawnDev.BlazorJS.Toolbox;

using var storage = JS.Get<StorageManager>("navigator.storage");
using var root = await storage.GetDirectory();
using var fileHandle = await root.GetFileHandle("scene.bin", create: true);

// ⚠ await using — the OPFS close() is the async disk commit. CopyToStreamAsync WRITES the
// bytes but does NOT own/close the target stream; a plain `using` can return before the file
// is flushed, leaving an empty/short file (browser-timing dependent). await using (or an
// explicit await writable.CloseAsync()) guarantees the bytes are on disk before you read back.
await using var writable = await fileHandle.GetWritableStream();
await packedBuf.View.CopyToStreamAsync(writable);
```

> `CopyToStreamAsync` requires **SpawnDev.BlazorJS 3.5.14+** for `IJSWriteStream` and the `FileSystemHandleWritableStream` async commit (`DisposeAsync` / `CloseAsync`).

## Buffer Lifecycle

### Disposal Order

Dispose in **reverse order** of creation:

```csharp
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();
using var buffer = accelerator.Allocate1D<float>(1024);

// ... use buffer ...

// `using` disposes in reverse: buffer → accelerator → context
```

### Key Rules

1. **Buffers are tied to their accelerator** — you cannot use a buffer from one accelerator with another
2. **Dispose buffers before their accelerator** — disposing out of order causes errors
3. **Views don't need disposal** — `ArrayView<T>` is a lightweight struct (like `Span<T>`)
4. **Don't access disposed buffers** — `CopyToHostAsync` on a disposed buffer throws `ObjectDisposedException`

## Zero-Allocation Hot Path

For real-time rendering, the buffer readback system supports a zero-allocation path:

```csharp
// Cache the staging buffer (created once, reused)
// This happens automatically inside WebGPUBuffer<T>.CopyToHostAsync

// Pre-allocate the result array
uint[] cachedResult = new uint[pixelCount];

// Each frame: no GC allocations
await nativeBuffer.CopyToHostAsync(cachedResult, 0, pixelCount);
```

The `WebGPUBuffer<T>` internally maintains a cached staging buffer for readback operations. The first call creates it; subsequent calls reuse it (as long as the size doesn't increase).

## Compiled-Shader Cache & Eviction (WebGPU)

Buffers are not the only GPU memory an accelerator holds. The WebGPU backend caches every kernel it
compiles, and that cache is **bound to the accelerator's lifetime** — it is emptied when the accelerator is
disposed, and not before.

Each distinct kernel retains three GPU objects plus its source:

| Retained per distinct kernel | What it costs |
|---|---|
| `GPUShaderModule` | compiled WGSL |
| `GPUComputePipeline` | compiled GPU machine code — the expensive one |
| `GPUBindGroupLayout` | binding layout |
| the WGSL source string | held as the cache key, one .NET string per entry |

**"Distinct" means distinct (WGSL, override-constants).** The same C# kernel dispatched at a different
auto-grouped size produces different WGSL (the dispatch size is baked into the range check), so it is a
separate entry.

### When the default is fine, and when it is not

For an application with a **fixed kernel set** this is exactly what you want: each kernel compiles once,
the cache reaches a small steady size, and every later dispatch hits it. Nothing needs tuning.

It becomes a problem when a long-lived accelerator keeps **minting new kernels** — many lambda
specializations, ML work specialized per tensor shape, or a test suite where every test defines its own
kernel. There, every kernel ever compiled is retained for the life of the accelerator, and memory grows
without bound.

### Bounding it

Two controls, and they compose:

```csharp
using SpawnDev.ILGPU.WebGPU.Backend;

// 1. LRU cap. 0 (default) = unlimited. A positive value evicts the
//    least-recently-used entry once the cache exceeds it; a cache HIT
//    refreshes recency, so a working set that fits still hits ~100%.
WebGPUBackend.MaxCachedShaders = 64;

// 2. Explicit release, without disposing the accelerator.
if (accelerator is WebGPUAccelerator webgpu)
{
    Console.WriteLine($"retained: {webgpu.CachedShaderCount}");
    webgpu.ClearShaderCache();   // later dispatches recompile on demand
}
```

**Prefer the cap over clearing.** A periodic full clear discards the hot working set as well as the cold
tail, so the next dispatch of a kernel you are still using pays a full recompile. Measured on the browser
test suite, a clear-after-every-test policy bounded memory but made the app visibly unresponsive from
constant recompilation; an LRU cap gave the same ceiling at a fraction of the cost. Reach for
`ClearShaderCache()` at genuine phase boundaries — finishing a workload, tearing down a scene — not on a
timer.

`ClearShaderCache()` also clears the dispatch-path shader-resolution cache and the bind-group cache, since
both reference the shaders being released.

### Eviction safety

Eviction disposes real GPU objects, so anything still holding a reference to an evicted shader would be
left pointing at a released pipeline. That is handled internally: the native accelerator raises
`ShaderEvicting` **before** disposing, and the owning `WebGPUAccelerator` drops its matching
shader-resolution entries and clears its bind-group cache in lockstep. An eviction can never hand back a
disposed pipeline.

If you build a cache of your own that stores `WebGPUComputeShader` references, it must do the same — a
stale reference surfaces as an opaque WebGPU error far from its cause.

> **Other backends:** this cache is WebGPU-only. `CachedShaderCount` reports `0` and `ClearShaderCache()`
> is a no-op elsewhere, so the calls are safe to leave in cross-backend code.

## Memory Tips

1. **Batch your uploads** — call `CopyFromCPU` before launching kernels, not inside render loops
2. **Reuse buffers** — allocate once, use many times, dispose at cleanup
3. **Avoid per-frame allocation** — pre-allocate host arrays and use the `CopyToHostAsync(destination)` overload
4. **Use `IBrowserMemoryBuffer`** for Canvas rendering — it skips the .NET array entirely when possible
5. **Buffer pooling is automatic** — the WebGPU backend pools scalar parameter buffers internally
6. **Cap the shader cache if you generate kernels dynamically** — set `WebGPUBackend.MaxCachedShaders`; the
   default is unlimited, which only suits a fixed kernel set (see [Compiled-Shader Cache & Eviction](#compiled-shader-cache--eviction-webgpu))
