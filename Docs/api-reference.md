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
| `builder.AllAcceleratorsAsync()` | `Task<Builder>` | Enables all available backends (browser: WebGPU, WebGL, Wasm; desktop: CUDA, OpenCL, CPU). Silently skips unavailable ones. Async (WebGPU/WebGL probing is async) — `await` it |
| `builder.WebGPU()` | `Task` | Enables WebGPU backend only (async probe — `await` it) |
| `builder.WebGL()` | `Task` | Enables WebGL backend only (async probe — `await` it) |
| `builder.Wasm()` | `Builder` | Enables Wasm backend only (synchronous — chainable) |
| `context.CreatePreferredAcceleratorAsync()` | `Task<Accelerator>` | Creates the best available accelerator (browser: WebGPU > WebGL > Wasm; desktop: CUDA > OpenCL > CPU) |
| `accelerator.SynchronizeAsync()` | `Task` | Async wait for all GPU work to complete |
| `buffer.CopyToHostAsync<T>()` | `Task<T[]>` | Copies buffer data to a new array (works with all backends) |
| `view.CopyFromAsync(source)` | `Task` | Backend-agnostic async mirror of sync `CopyFrom` (on `ArrayView<T>`, `ArrayView1D<T,TStride>`, `MemoryBuffer1D<T,TStride>`). Drains pending Wasm worker dispatches before the copy so GPU→GPU copies after an unawaited kernel dispatch don't read `SharedArrayBuffer` mid-write; no-op drain on WebGPU/WebGL/CUDA/OpenCL/CPU |
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

Interface implemented by GPU-backed memory buffers for browser interop.

```csharp
public interface IBrowserMemoryBuffer
{
    Task<Uint8Array> CopyToHostUint8ArrayAsync(long sourceByteOffset = 0, long? copyBytes = null);
}
```

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
| `NativeAccelerator` | `WebGPUNativeAccelerator` | Low-level WebGPU access |
| `Backend` | `WebGPUBackend` | The WGSL transpiler backend |
| `EnabledFeatures` | `HashSet<string>` | Detected WebGPU features (e.g., `subgroups`, `shader-f16`) |

### WebGPUBuffer\<T\>

Typed GPU memory buffer with staging buffer caching for efficient readback.

| Member | Type | Description |
|--------|------|-------------|
| `NativeBuffer` | `GPUBuffer?` | Underlying WebGPU buffer |
| `Length` | `long` | Number of elements |
| `ElementSize` | `int` | Size of each element in bytes |
| `LengthInBytes` | `long` | Total buffer size |
| `CopyFromHost(T[])` | `void` | Upload data from CPU to GPU |
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

### WebGPUNativeAccelerator

Low-level WebGPU operations — for advanced users who need direct GPU API access.

| Member | Type | Description |
|--------|------|-------------|
| `NativeDevice` | `GPUDevice?` | The underlying WebGPU device |
| `Queue` | `GPUQueue?` | The device's command queue |
| `EnabledFeatures` | `HashSet<string>` | Detected features |
| `FlushPendingCommands` | `Action?` | Callback to flush batched commands |
| `CreateFromExternalDevice(gpuDevice)` | `WebGPUNativeAccelerator` | Wrap an external device |
| `GetOrCreateComputeShader(source, entry, overrides?)` | `WebGPUComputeShader` | Compile/cache WGSL shader |
| `Dispatch(shader, bindGroup, dims)` | `void` | Dispatch a compute shader |

---

## SpawnDev.ILGPU.WebGPU.Backend

### WebGPUBackend

WGSL transpiler backend.

| Member | Type | Description |
|--------|------|-------------|
| `VerboseLogging` | `static bool` | Enable/disable debug output |
| `EnableReflectionCaching` | `static bool` | Enable reflection metadata caching |
| `EnableBufferPooling` | `static bool` | Enable scalar buffer pooling |
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

### WebGLDevice

Represents a WebGL2 device.

| Member | Type | Description |
|--------|------|-------------|
| `GetDevicesAsync()` | `Task<List<WebGLDevice>>` | Detect available WebGL2 devices |
| `CreateAccelerator(context, options?)` | `WebGLAccelerator` | Create a WebGL accelerator |

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
| `EnableF64Emulation` | `bool` | `true` | Emulate `double` precision |
| `EnableI64Emulation` | `bool` | `true` | Emulate `long`/`ulong` |

---

## SpawnDev.ILGPU.Wasm

### WasmAccelerator

WebAssembly accelerator — compiles kernels to native Wasm and dispatches across Web Workers.

| Member | Type | Description |
|--------|------|-------------|
| `SynchronizeAsync()` | `Task` | Async wait for all workers (via extension method) |

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

### WasmBackendOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MaxWorkers` | `int?` | `null` (auto) | Maximum Web Workers for dispatch |

---

## ILGPU.Runtime.Cuda — NVIDIA CUDA Libraries

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
