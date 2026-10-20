# API Reference

Public classes and extension methods organized by namespace.

## SpawnDev.ILGPU

Core extension methods that work across all backends.

### SpawnDevContextExtensions

Extension methods for the ILGPU `Context` class.

```csharp
using SpawnDev.ILGPU;
```

| Method | Return | Description |
|--------|--------|-------------|
| `Context.CreateAsync(Action<Builder>)` | `Task<Context>` | Creates a context using an async builder (required for WebGPU probing) |
| `builder.Velocity(VelocityDeviceType.Vector128)` | `Builder` | (namespace `ILGPU.Runtime.Velocity`) Registers the SIMD CPU backend ([velocity.md](velocity.md)) alone or with a device type; `AllAccelerators()` / `AllAcceleratorsAsync()` already register it on desktop. Then `context.CreateVelocityAccelerator(0)` |
| `builder.AllAcceleratorsAsync()` | `Task<Builder>` | Enables all available backends (browser: WebGPU, WebGL, Wasm; desktop: CUDA, OpenCL, Velocity). Silently skips unavailable ones. Async (WebGPU/WebGL probing is async) — `await` it |
| `builder.WebGPU()` | `Task` | Enables WebGPU backend only (async probe — `await` it) |
| `builder.WebGL()` | `Task` | Enables WebGL backend only (async probe — `await` it) |
| `builder.Wasm()` | `Builder` | Enables Wasm backend only (synchronous — chainable) |
| `context.CreatePreferredAcceleratorAsync()` | `Task<Accelerator>` | Creates the best available accelerator (browser: WebGPU > WebGL > Wasm; desktop: CUDA > discrete OpenCL GPU > integrated OpenCL GPU > other OpenCL > Velocity > CPU) |
| `context.CreatePreferredAcceleratorAsync(requirements)` | `Task<Accelerator>` | Same, restricted to devices that satisfy an `AcceleratorRequirements`; configures Ozaki f64 on WebGPU/WebGL for `RequiresFloat64Strict`. Throws `NotSupportedException` when nothing fits |
| `context.CreatePreferredAccelerator(requirements)` | `Accelerator` | Synchronous form (desktop; throws in the browser when WebGPU or Wasm is the pick, as those are async-only to create) |
| `context.EnumerateCompatibleDevices(requirements)` | `IReadOnlyList<Device>` | Every device that satisfies the requirements, context order |
| `device.Satisfies(requirements)` | `bool` | Per-device requirement check |
| `context.GetBestDevice()` | `Device` | Best device per `DevicePreference` (CUDA > discrete OpenCL GPU > integrated OpenCL GPU > other OpenCL > WebGPU > WebGL > Wasm > Velocity > CPU) |
| `devices.OrderByPreference()` | `IReadOnlyList<Device>` | The same ranking applied to any device list, best first; memory size only breaks ties |
| `accelerator.SynchronizeAsync()` | `Task` | Async wait for all GPU work to complete |
| `accelerator.Flush()` | `void` | Submit pending work without waiting (sync-safe on every backend) |
| `buffer.CopyToHostAsync<T>()` | `Task<T[]>` | Copies buffer data to a new array (works with all backends) |
| `view.CopyFromAsync(source)` | `Task` | Async mirror of sync `CopyFrom` (on `ArrayView<T>`, `ArrayView1D<T,TStride>`, `MemoryBuffer1D<T,TStride>`). Optional eager completion: sync `CopyFrom` is already ordered after the producing kernel on every backend |
| `view.CopyFromStreamAsync(stream, chunkSizeInBytes?)` / `view.CopyToStreamAsync(stream, chunkSizeInBytes?)` | `Task` | Bounded-chunk streaming between a `Stream` and a GPU buffer; zero-copy JS<->GPU on the browser when the stream is an `IJSReadStream` / `IJSWriteStream` |
| `context.GetWebGPUDevices()` | `Context.DeviceCollection<WebGPUILGPUDevice>` | Lists registered WebGPU devices |
| `context.GetWebGLDevices()` | `Context.DeviceCollection<WebGLILGPUDevice>` | Lists registered WebGL devices |

### GpuMatrix4x4

GPU-friendly 4×4 matrix struct. Auto-transposes from .NET's row-major `Matrix4x4` to GPU column-major order.

| Member | Type | Description |
|--------|------|-------------|
| `FromMatrix4x4(Matrix4x4)` | `static GpuMatrix4x4` | Creates from .NET matrix (auto-transposes) |
| `Identity` | `static GpuMatrix4x4` | Identity matrix |
| `TransformPoint(m, x, y, z, out rx, ry, rz)` | `static void` | Rotation + translation (kernel-safe) |
| `TransformDirection(m, x, y, z, out rx, ry, rz)` | `static void` | Rotation only (kernel-safe) |
| `this[row, col]` | `float` | Element accessor (0-indexed) |

### IBrowserMemoryBuffer

Interface implemented by the WebGPU, WebGL and Wasm memory buffers for JS interop without the .NET heap. Get it with `(IBrowserMemoryBuffer)((IArrayView)buffer.View).Buffer`.

```csharp
public interface IBrowserMemoryBuffer
{
    Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null);
    void CopyFromJS(TypedArray source, long targetByteOffset = 0);
    void CopyFromJS(ArrayBuffer source, long targetByteOffset = 0);
}
```

### BrowserBufferPolicy

Static policy and census for host->device copies on the browser backends.

| Member | Description |
|--------|-------------|
| `StrictHostCopyMaxBytes` | `-1` (default) = off. Otherwise a `CopyFromCPU` larger than this many bytes on any browser backend throws, naming the size (bulk data must stream JS-side) |
| `HostCopyCount` / `HostCopyBytes` / `LargestHostCopyBytes` | Census of host->device copies that came from the .NET heap since the last reset |
| `ResetHostCopyCensus()` | Resets the census |

### DevicePreference

| Member | Description |
|--------|-------------|
| `OrderByPreference(this IEnumerable<Device>)` | Devices best first: CUDA, discrete OpenCL GPU, integrated OpenCL GPU (`CL_DEVICE_HOST_UNIFIED_MEMORY`), other OpenCL, WebGPU, WebGL, Wasm, Velocity, other, CPU. Memory size breaks ties |
| `GetBestDevice(this Context)` | The first device of that ranking. Use instead of ILGPU's `GetPreferredDevice` (memory-only sort) |

### AcceleratorRequirements / UnsupportedKernelFeatureException

Declarative capability gating and the compile-time exception that backs it. Full guide: [Capabilities & Backend Selection](capabilities-and-backend-selection.md).

---

## ILGPU — Generic Math (f16 kernels)

```csharp
using ILGPU;
```

Write one generic kernel over the numeric/weight type instead of dedicated per-type kernels. See the
[Generic-Math f16 Kernels](generic-math-f16.md) guide for worked examples.

### NumericConvert

Transpilable generic numeric converts. (The C# `float.CreateTruncating<T>` etc. fail to lower in kernels
— they inspect `typeof(T)`.) Each call monomorphizes to the concrete per-type GPU convert.

| Method | Return | Description |
|--------|--------|-------------|
| `NumericConvert.ToFloat32<T>(T value)` | `float` | Widens a generic numeric value to `float` inside a kernel; lowers to the concrete `(float)T` cast (`(float)Half`, identity for `float`, `(float)int`, ...). `where T : INumberBase<T>` |
| `NumericConvert.ToFloat64<T>(T value)` | `double` | Widens a generic numeric value to `double` (same, targeting `double`) |

### Half

`ILGPU.Half` implements `INumber<Half>` and `ISignedNumber<Half>` (C# 11 generic math), so generic-math
kernels (`where T : INumber<T>`) work over Half — operators (`+ - * / %`, comparisons, `++`/`--`),
identities (`T.One`, `T.Zero`), and methods (`T.Abs`, `T.Clamp`, `T.Min`, `T.Max`, `T.CopySign`,
`T.Sign`, the `T.Is*` predicates) lower to Half's FP32 intrinsics on all six backends. Use `ILGPU.Half`,
not `System.Half`, in kernel signatures.

---

## SpawnDev.ILGPU.Rendering

Canvas rendering API — presents an ILGPU pixel buffer to an HTML `<canvas>` using the most efficient path for each backend.

```csharp
using SpawnDev.ILGPU.Rendering;
```

> **Full guide:** [Canvas Rendering](canvas-rendering.md)

### ICanvasRenderer

```csharp
public interface ICanvasRenderer : IDisposable
{
    void AttachCanvas(HTMLCanvasElement canvas);
    Task PresentAsync(MemoryBuffer2D<uint, Stride2D.DenseX> buffer);
    Task PresentAsync(MemoryBuffer2D<int,  Stride2D.DenseX> buffer);
}
```

| Method | Description |
|--------|-------------|
| `AttachCanvas(canvas)` | Attaches (or re-attaches) the renderer to a canvas element. Disposes any previous context. |
| `PresentAsync(buffer)` | Presents a 2D packed-uint or packed-int RGBA pixel buffer to the canvas. |

### IExternalImageCopier / ExternalImageCopier

```csharp
public interface IExternalImageCopier : IDisposable
{
    void CopyToBuffer(GPUCopyExternalImageSource source, int width, int height,
                      ArrayView1D<int, Stride1D.Dense> destination);
}

public static class ExternalImageCopier
{
    public static IExternalImageCopier Create(Accelerator accelerator);
}
```

Copies a `<video>`, `<img>`, canvas, `OffscreenCanvas`, `ImageBitmap`, `VideoFrame` or `ImageData` into a buffer as packed RGBA8 (the layout `ICanvasRenderer` presents). `Create` returns `WebGPUExternalImageCopier` (GPU only: `copyExternalImageToTexture` + `copyTextureToBuffer`) on WebGPU, else `Canvas2DExternalImageCopier` (2D canvas + `CopyFromJS`). See [Image and Video Input](canvas-rendering.md#image-and-video-input-iexternalimagecopier).

### CanvasRendererFactory

```csharp
public static class CanvasRendererFactory
{
    public static ICanvasRenderer Create(Accelerator accelerator);
}
```

`Create` returns the optimal renderer for the given accelerator:

| Accelerator | Renderer | Technique |
|-------------|----------|-----------|
| `WebGPUAccelerator` | `WebGPUCanvasRenderer` | Fullscreen-triangle render pass — no CPU readback |
| `WebGLAccelerator` | `WebGLCanvasRenderer` | `ImageBitmap` blit from GL worker, drawn synchronously |
| Any other (Wasm, desktop CPU) | `CPUCanvasRenderer` | Cached `ImageData` with fast Uint8Array copy |

### WebGPUCanvasRenderer

`SpawnDev.ILGPU.WebGPU.Rendering`

Zero-copy presenter. Reads the pixel buffer directly from GPU memory via a `read-only-storage` binding in a fullscreen render pass. No CPU readback occurs.

| Member | Description |
|--------|-------------|
| `AttachCanvas(canvas)` | Builds the render pipeline and configures the internal WebGPU canvas. |
| `PresentAsync(buffer)` | Flushes pending commands, runs the render pass, blits to display canvas. |

### WebGLCanvasRenderer

`SpawnDev.ILGPU.WebGL.Rendering`

`ImageBitmap`-based presenter. Renders the WebGL texture to an offscreen FBO in the GL worker, transfers the bitmap to the main thread, then calls `ctx.drawImage` synchronously inside the worker callback — ensuring the draw completes in the same JS event-loop turn as the blit.

| Member | Description |
|--------|-------------|
| `AttachCanvas(canvas)` | Acquires a `CanvasRenderingContext2D` on the display canvas. |
| `PresentAsync(buffer)` | Posts blit to the GL worker, awaits the `ImageBitmap`, and draws synchronously. |

### CPUCanvasRenderer

`SpawnDev.ILGPU.Rendering`

Fallback renderer for the Wasm browser backend and the desktop CPU accelerator. Reuses a pre-allocated `ImageData` object to minimise GC pressure.

| Member | Description |
|--------|-------------|
| `AttachCanvas(canvas)` | Acquires a `CanvasRenderingContext2D` and invalidates cached `ImageData`. |
| `PresentAsync(buffer)` | Copies to `Uint8Array` (fast path for `IBrowserMemoryBuffer`) or CPU array, then calls `putImageData`. |

---

## SpawnDev.ILGPU.WebGPU

### WebGPUAccelerator

The main WebGPU accelerator — extends ILGPU's `KernelAccelerator`.

| Member | Type | Description |
|--------|------|-------------|
| `CreateAsync(context, device)` | `Task<WebGPUAccelerator>` | Creates accelerator with default options |
| `CreateAsync(context, device, options)` | `Task<WebGPUAccelerator>` | Creates accelerator with custom options |
| `CreateFromExternalDevice(context, gpuDevice, options?)` | `WebGPUAccelerator` | Creates from an external `GPUDevice` (device sharing) |
| `FlushPendingCommands()` | `void` | Submits the pending batch (dispatches, copies, clears, small uploads). Call it before submitting your own work on the same queue |
| `F64Mode` | `F64EmulationMode` | Get/set the f64 emulation mode for subsequent kernel compiles |
| `NativeAccelerator` | `WebGPUNativeAccelerator` | Low-level WebGPU access |
| `Backend` | `WebGPUBackend` | The WGSL transpiler backend |
| `EnabledFeatures` | `HashSet<string>` | Detected WebGPU features (e.g., `subgroups`, `shader-f16`) |
| `CachedShaderCount` | `int` | Compiled shaders currently retained (module + pipeline + bind-group layout each) |
| `ClearShaderCache()` | `void` | Releases all cached shaders + the resolution and bind-group caches that reference them, **without** disposing the accelerator. Later dispatches recompile on demand. See [Memory & Buffers](memory-and-buffers.md#compiled-shader-cache--eviction-webgpu) |
| `ClearShaderResolveCache()` | `void` | Clears only the per-(kernel, dispatch-config) resolution cache and its hit/miss counters |
| `ShaderResolveCacheHits` / `ShaderResolveCacheMisses` | `long` | Resolution-cache counters |
| `ClearBindGroupCache()` | `void` | Clears the opt-in bind-group cache |

### WebGPUBuffer\<T\>

Typed GPU memory buffer with staging buffer caching for efficient readback.

| Member | Type | Description |
|--------|------|-------------|
| `NativeBuffer` | `GPUBuffer?` | Underlying WebGPU buffer |
| `Length` | `long` | Number of elements |
| `ElementSize` | `int` | Size of each element in bytes |
| `LengthInBytes` | `long` | Total buffer size |
| `CopyFromHost(T[], targetOffset)` | `void` | Upload data from CPU to GPU (submits pending dispatches first) |
| `CopyToHostAsync()` | `Task<T[]>` | Download data — allocates new array |
| `CopyToHostAsync(T[], offset, count)` | `Task<long>` | Download into pre-allocated array (zero-alloc) |
| `CopyToHostUint8ArrayAsync(offset, bytes)` | `Task<Uint8Array>` | Download as JS Uint8Array |
| `Fill(T)` | `void` | Fill buffer with a value |

### WebGPUDevice

Represents a WebGPU device available in the browser.

| Member | Type | Description |
|--------|------|-------------|
| `GetDevicesAsync()` | `Task<ImmutableArray<WebGPUDevice>>` | Detect all available WebGPU devices |
| `GetDefaultDeviceAsync()` | `Task<WebGPUDevice?>` | Get the first available device |
| `CreateAcceleratorAsync()` | `Task<WebGPUNativeAccelerator>` | Create a low-level native accelerator from this JS-wrapper device. (For the ILGPU `WebGPUAccelerator`, use `WebGPUILGPUDevice.CreateAcceleratorAsync(context)` — the type `context.GetWebGPUDevices()` returns.) |
| `PrintInfo(TextWriter)` | `void` | Print device information to the given writer, e.g. `PrintInfo(Console.Out)` |

`GetDevicesAsync()` lists a distinct high-performance adapter first (devices are compared on vendor, architecture, device and description), then the default adapter.

### WebGPUILGPUDevice

The ILGPU device `context.GetWebGPUDevices()` returns.

| Member | Type | Description |
|--------|------|-------------|
| `CreateAcceleratorAsync(context, options?)` | `Task<WebGPUAccelerator>` | Create an accelerator. More than one can be created from the same device (each later one requests a fresh adapter for the same GPU) |
| `AdapterSupportsShaderF16` / `AdapterSupportsSubgroups` | `bool` | The adapter's features, known before any accelerator exists; `AcceleratorRequirements` selection reads them |
| `NativeDevice` | `WebGPUDevice` | The JS-side adapter wrapper (limits, `SupportedFeatures`, `PrintInfo`) |

### WebGPUNativeAccelerator

Low-level WebGPU operations — for advanced users who need direct GPU API access.

| Member | Type | Description |
|--------|------|-------------|
| `NativeDevice` | `GPUDevice?` | The underlying WebGPU device |
| `Queue` | `GPUQueue?` | The device's command queue |
| `EnabledFeatures` | `HashSet<string>` | Detected features |
| `HasShaderF16` / `HasSubgroups` / `HasTimestampQuery` | `bool` | Feature checks |
| `MaxStorageBuffersPerShaderStage` | `int` | The device limit (Chrome: 10) |
| `IsDeviceLost` / `DeviceLost` | `bool` / `event Action<string, string>` | Device-loss state and notification |
| `CreateFromExternalDevice(gpuDevice)` | `WebGPUNativeAccelerator` | Wrap an external device |
| `GetOrCreateComputeShader(wgslSource, entryPoint = "main", overrideConstants?, label?)` | `WebGPUComputeShader` | Compile/cache a WGSL shader |
| `Dispatch(shader, workgroupCountX, workgroupCountY = 1, workgroupCountZ = 1)` | `void` | Dispatch a compute shader on its own command encoder |

---

## SpawnDev.ILGPU.WebGPU.Backend

### WebGPUBackend

WGSL transpiler backend.

| Member | Type | Description |
|--------|------|-------------|
| `VerboseLogging` | `static bool` | Enable/disable debug output |
| `EnableDispatchBatching` | `static bool` | Record plain dispatches, copies, clears and small uploads and submit them with one JS crossing per submit (default `true`) |
| `BatchSinglePass` / `BatchScalarArena` / `BatchBindGroupReuse` | `static bool` | Batching refinements (all default `true`): consecutive dispatches share one compute pass; per-dispatch scalars go into two per-stream arenas written once per submit; bind groups are cached by layout and entries. `BatchBindGroupCounters` reports hits/misses |
| `EnableReflectionCaching` | `static bool` | Enable reflection metadata caching |
| `EnableBufferPooling` | `static bool` | Pool scalar parameter buffers on the unbatched path |
| `EnableShaderCaching` | `static bool` | Cache compiled shaders across dispatches (default `true`) |
| `ForceEmulatedF16` | `static bool` | Use the emulated f16 path even when the adapter has `shader-f16` (testing) |
| `MaxCachedShaders` | `static int` | Cap on retained compiled shaders per accelerator. **`0` (default) = unlimited**; a positive value enables LRU eviction (a hit refreshes recency). Unlimited suits a fixed kernel set; cap it if you generate kernels dynamically. See [Memory & Buffers](memory-and-buffers.md#compiled-shader-cache--eviction-webgpu) |
| `LastGeneratedWGSL` | `static string?` | WGSL source of the most recently compiled kernel (set on every `LoadKernel` call) |

### WebGPUBackendOptions

Configuration for the WebGPU transpiler.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `F64Emulation` | `F64EmulationMode` | `Dekker` | How to emulate `double`: `Dekker` (`vec2<f32>`, fast), `Ozaki` (`vec4<f32>`, precise), or `Disabled` |
| `ForceDisableSubgroups` | `bool` | `false` | Force-disable subgroup features even when the adapter exposes them |

(There is no i64 emulation toggle — `long`/`ulong` are always emulated as `vec2<u32>` on WebGPU.)

### ExternalWebGPUMemoryBuffer

Non-owning wrapper for externally-managed GPU buffers.

```csharp
// Wrap an external GPUBuffer for use in ILGPU kernels
var wrapped = new ExternalWebGPUMemoryBuffer(accelerator, gpuBuffer, elementCount, elementSize);

// Disposing does NOT destroy the underlying GPUBuffer
wrapped.Dispose();
```

---

## SpawnDev.ILGPU.WebGL

### WebGLAccelerator

WebGL2 accelerator — all GL calls are offloaded to a dedicated Web Worker.

| Member | Type | Description |
|--------|------|-------------|
| `Create(context, device, options?)` | `WebGLAccelerator` | Creates a WebGL accelerator |
| `SynchronizeAsync()` | `Task` | Async wait for GPU work (via extension method) |
| `LastGeneratedGLSL` | `static string?` | GLSL ES 3.0 source of the most recently compiled kernel (set on every `LoadKernel` call) |
| `BlitAndDrawAsync(memBuf, w, h, draw)` | `Task` | Blits a WebGL buffer to an `ImageBitmap` and calls `draw` synchronously in the worker callback before resolving |

### WebGLDevice / WebGLILGPUDevice

`WebGLDevice` is the JS-side WebGL2 device; `WebGLILGPUDevice` is the ILGPU device `context.GetWebGLDevices()` returns.

| Member | Type | Description |
|--------|------|-------------|
| `WebGLDevice.GetDevicesAsync()` | `Task<ImmutableArray<WebGLDevice>>` | Detect available WebGL2 devices |
| `WebGLILGPUDevice.CreateAccelerator(context, options)` | `WebGLAccelerator` | Create a WebGL accelerator (synchronous creation works on WebGL) |
| `WebGLILGPUDevice.CreateAcceleratorAsync(context)` | `Task<Accelerator>` | Async form, for code shared with the other browser backends |

---

## SpawnDev.ILGPU.WebGL.Backend

### WebGLBackend

GLSL ES 3.0 transpiler backend.

| Member | Type | Description |
|--------|------|-------------|
| `VerboseLogging` | `static bool` | Enable/disable debug output |

### WebGLBackendOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `F64Emulation` | `F64EmulationMode` | `Dekker` | How to emulate `double`: `Dekker`, `Ozaki` or `Disabled` (as on WebGPU) |

(`long`/`ulong` are always emulated on WebGL; there is no toggle.)

---

## SpawnDev.ILGPU.Wasm

### WasmAccelerator

WebAssembly accelerator — compiles kernels to native Wasm and dispatches across Web Workers.

| Member | Type | Description |
|--------|------|-------------|
| `SynchronizeAsync()` | `Task` | Async wait for all workers |
| `SupportsSimd` | `bool` | True when kernels run their SIMD128 (`v128`) variant |
| `EnablePipelinedDispatch` | `static bool` | Pipeline small barrier-free dispatches (default `true`) |
| `NonBarrierMinItemsPerWorker` | `static int` | Items per worker for barrier-free dispatches (default 65536) |
| `SharedWorkerPoolSize` / `TotalKernelsCompiled` | `static int` | Diagnostics for the persistent worker pool |

### WasmILGPUDevice

Represents a Wasm compute device.

| Member | Type | Description |
|--------|------|-------------|
| `CreateAcceleratorAsync(context)` | `Task<WasmAccelerator>` | Create a Wasm accelerator |

### WasmMemoryBuffer

Wasm-backed memory buffer using SharedArrayBuffer.

| Member | Type | Description |
|--------|------|-------------|
| `CopyToHostUint8ArrayAsync(offset, bytes)` | `Task<Uint8Array>` | Read data as JS Uint8Array |

---

## SpawnDev.ILGPU.Wasm.Backend

### WasmBackend

Wasm compilation backend.

| Member | Type | Description |
|--------|------|-------------|
| `VerboseLogging` | `static bool` | Enable/disable debug output |
| `ForceScalar` / `ForceSimd` | `static bool` | Force the scalar or the SIMD128 kernel path (testing; default both `false`) |
| `RuntimeSupportsWasmSimd` | `static bool` | Whether the running engine accepts `v128` |
| `ModuleCacheFlushThreshold` | `static int` | Kernels compiled before persistent workers drop old modules (default 256, 0 = never) |

### WasmBackendOptions

Passed to `WasmILGPUDevice.CreateAcceleratorAsync(context, options)`.

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WorkerCount` | `int` | `max(2, hardwareConcurrency - 2)` | Web Workers for dispatch. A non-default count gets a private worker pool |
| `EnableYieldEscape` | `bool?` | `null` (auto) | Let spinning barrier workers yield to JS under CPU oversubscription |
| `MaxLinearMemoryPages` | `int` | `16384` (1 GiB) | Maximum size of the shared linear memory, in 64 KiB pages |

---

## ILGPU.Runtime - Device Memory Budget (5.3.6+)

| Member | Description |
|--------|-------------|
| `IDeviceMemoryReclaimer` | A holder of reclaimable device memory: `ReclaimableBytes`, `LastUsedTimestamp` (a `Stopwatch` timestamp), `Reclaim(bytesWanted)` returning the bytes freed |
| `accelerator.RegisterReclaimer(r)` / `UnregisterReclaimer(r)` | Register (held weakly) / remove a holder. Once anything is registered, allocations check free memory first |
| `accelerator.ReclaimDeviceMemory(bytes)` | Ask the holders to free at least `bytes`, least recently used first; returns the bytes freed |
| `accelerator.DeviceMemoryHeadroomBytes` | Free memory to keep in reserve; `null` = `MemoryPressure.DefaultHeadroomBytes` (512 MiB) capped at 5% of the device |
| `accelerator.TryGetFreeMemory(out long)` | Free device memory, on backends that can report it (CUDA) |
| `MemoryPressure.FreeMemoryCacheDuration` | How long a free-memory measurement is trusted (250 ms) |
| `MemoryPressure.ProactiveReclaimCount` / `ProactiveReclaimedBytes` | Diagnostics |
| `accelerator.AllocateWithReclaim(allocate, reclaim, describeState?)` | Try, reclaim through your callback on failure, retry once |

Guide: [Device Memory Budget](memory-and-buffers.md#device-memory-budget-reclaiming-idle-memory).

## SpawnDev.ILGPU - Shader Artifacts

| Member | Description |
|--------|-------------|
| `ShaderCompiler.Generate(kernel, profile)` | Generate a kernel's WGSL/GLSL/Wasm for a `CapabilityProfile` with no device |
| `ShaderArtifactSerializer.ExportCache()` | The WebGPU shaders this session compiled or imported, as compact JSON (runtime-emitted kernels left out) |
| `ShaderArtifactSerializer.ImportCache(json)` | Register an export from the SAME library build; returns the count registered (0 for another build) |
| `ShaderArtifactSerializer.IsExportable(kernelId)` | Whether a kernel's identity is stable across sessions |
| `ShaderArtifactCache.Hits` / `Misses` / `IrSkippedHits` / `Count` / `Enabled` | Runtime cache counters and kill switch |
| `ShaderArtifactManifestLoader.Configure(url, fetch)` / `TryWarmAsync` / `WarmAllAsync` | Load build-time precompiled artifacts |

Guide: [Precompiled Shaders](precompiled-shaders.md).

## SpawnDev.ILGPU.WebGPU - Diagnostics

| Member | Description |
|--------|-------------|
| `WebGPUBufferAccounting.LiveStorageBytes` / `LiveStagingBytes` / `LiveBufferCount` | What the library currently holds on the device |
| `WebGPUBufferAccounting.LargestLiveBuffers(n)` | Largest live buffers by label |
| `WebGPUBufferAccounting.CaptureCreationSites` / `TopCreationSites(n)` | Record who allocated each buffer (stack walk per allocation) and group live buffers by site |

## ILGPU.Runtime.Cuda - NVIDIA CUDA Libraries

CUDA-only libraries for hardware-accelerated image processing, random numbers, linear algebra, FFT, and device monitoring. Require an NVIDIA GPU.

> **Full guide:** [CUDA Libraries](cuda-libraries.md)

### NvJpeg — JPEG Encode & Decode

| Class | Description |
|-------|-------------|
| `NvJpeg` | Entry point. Version detection, `CreateSimple()` |
| `NvJpegLibrary` | High-level decode/encode, state/params management |
| `NvJpegState` | Decoder state (disposable) |
| `NvJpegEncoderState` | Encoder state (disposable) |
| `NvJpegEncoderParams` | Encoder parameters (disposable) |

### CuRand — Random Number Generation

| Class | Description |
|-------|-------------|
| `GPUCuRand` | GPU random generator — uniform, normal, log-normal, Poisson |
| `CPUCuRand` | CPU random generator (same distributions, host memory) |
| `CuRand` | Factory: `CreateGPU()`, `CreateCPU()` |

### CuBlas — Linear Algebra

| Class | Description |
|-------|-------------|
| `CuBlas<T>` | BLAS Level 1/2/3 (GEMM, AXPY, DOT, etc.) with pointer mode handlers |

### CuFFT — Fast Fourier Transform

| Class | Description |
|-------|-------------|
| `CuFFT` | 1D/2D/3D FFT plans, C2C/R2C/C2R transforms |
| `CuFFTW` | FFTW-compatible wrapper |

### NVML — Device Monitoring

| Class | Description |
|-------|-------------|
| `NvmlDevice` | 113+ GPU query functions (temperature, clocks, memory, power, PCIe) |
