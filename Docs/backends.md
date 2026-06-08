# Backends

SpawnDev.ILGPU supports multiple backends for running ILGPU kernels. In the browser, three backends (WebGPU, WebGL, Wasm) bring GPU compute to Blazor WebAssembly. On desktop and server, ILGPU's native Cuda, OpenCL, and CPU backends are available. The same kernel code and async extensions work across all backends.

## Overview

### Browser Backends

| | 🎮 **WebGPU** | 🖼️ **WebGL** | 🧊 **Wasm** |
|---|---|---|---|
| **Executes on** | GPU | GPU | Web Workers |
| **Transpiles to** | WGSL | GLSL ES 3.0 | WebAssembly binary |
| **Technique** | Compute shader | Transform Feedback | Multi-worker |
| **Blocking** | Non-blocking | Non-blocking | Non-blocking |
| **Shared Memory** | ✅ | ❌ | ✅ |
| **Group.Barrier()** | ✅ | ❌ | ✅ |
| **Dynamic Shared Memory** | ✅ | ❌ | ✅ |
| **Atomics** | ✅ | ❌ | ✅ |
| **ILGPU Algorithms** | ✅ RadixSort, Scan, Reduce, Histogram | ⚠️ Host RadixSort (all key types incl. Half) + Scan + Reduce (multi-dispatch); no in-kernel group ops | ✅ RadixSort, Scan, Reduce, Histogram |
| **64-bit (f64/i64)** | ✅ Emulated | ✅ Emulated | ✅ Native |
| **Browser support** | Chrome/Edge 113+ | All modern browsers | All modern browsers |

### Desktop/Server Backends

| | 🚀 **Cuda** | 🔧 **OpenCL** | 🐢 **CPU** |
|---|---|---|---|
| **Executes on** | NVIDIA GPU | NVIDIA/AMD/Intel GPU | CPU cores |
| **Transpiles to** | PTX | OpenCL C | — (interpreted) |
| **Shared Memory** | ✅ | ✅ | ✅ |
| **Atomics** | ✅ | ✅ | ✅ |
| **64-bit** | ✅ Native | ✅ Native | ✅ Native |
| **Requirement** | NVIDIA GPU + driver | OpenCL 2.0+ or 3.0 GPU | None |

**Auto-selection priority (browser):** WebGPU → WebGL → Wasm
**Auto-selection priority (desktop):** Cuda → OpenCL → CPU

> **CUDA extras:** The CUDA backend also provides access to NVIDIA-specific libraries: nvJPEG (image encode/decode), cuRand (random numbers), cuBLAS (linear algebra), cuFFT (FFT), and NVML (device monitoring). See [CUDA Libraries](cuda-libraries.md).

## Automatic Backend Selection

### Recommended: Unified Async Pattern

The async pattern works on **all platforms** — both browser and desktop. This is the recommended approach for cross-platform code:

```csharp
using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.ILGPU;

// Works in Blazor WASM, Console, WPF, ASP.NET — everywhere
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();

// ... load kernel, dispatch ...

await accelerator.SynchronizeAsync();                // Waits for GPU completion (no data transfer)
var results = await bufC.CopyToHostAsync<float>();   // The only GPU→CPU data transfer path
```

`AllAcceleratorsAsync()` automatically detects the environment:
- **Browser:** Registers WebGPU, WebGL, and Wasm
- **Desktop:** Registers Cuda, OpenCL, and CPU (browser backends are skipped)

`CreatePreferredAcceleratorAsync()` picks the best available backend on either platform.

> **Why async?** Browser backends (Blazor WASM) **require** async — the single-threaded environment will deadlock on synchronous calls. Desktop backends **support both** sync and async, with async extensions gracefully falling back to synchronous ILGPU calls. Therefore, **async is always recommended** for maximum portability.

### Desktop-Only: Synchronous Pattern

If you're certain your code will **never** run in a browser, you can use ILGPU's standard synchronous API:

```csharp
// Desktop only — will deadlock in Blazor WASM
using var context = Context.Create(builder => builder.AllAccelerators());
using var accelerator = context.GetPreferredDevice(preferCPU: false)
    .CreateAccelerator(context);

// ... load kernel, dispatch ...

accelerator.Synchronize();  // Desktop: blocks until GPU done. Browser: only FLUSHES (dispatches) the queue, does NOT wait and does NOT deadlock — use `await accelerator.SynchronizeAsync()` to await completion
var results = bufC.GetAsArray1D();  // Synchronous readback — desktop only (sync GPU->CPU read THROWS on browser; use `await bufC.CopyToHostAsync()`)
```

### Capability-Gated Selection

`CreatePreferredAccelerator(requirements)` filters out backends that can't run a given kernel before they get picked. Use this when your kernel relies on features that aren't universal — atomics (no WebGL), native f64 (no WebGPU/WebGL), 64-bit atomics (no WebGL, no OpenCL without `cl_khr_int64_base_atomics`), etc.:

```csharp
using SpawnDev.ILGPU;

using var accelerator = context.CreatePreferredAccelerator(
    new AcceleratorRequirements
    {
        RequiresAtomics = true,        // rules out WebGL
        RequiresFloat64Native = true,  // rules out WebGPU + WebGL
    });
// On desktop: CUDA > OpenCL > CPU
// On browser with the above combo: only Wasm survives
// Throws NotSupportedException with a diagnostic message if nothing matches
```

The flag set mirrors the per-backend feature matrix below. See [capabilities-and-backend-selection.md](capabilities-and-backend-selection.md) for the full list of flags, the `EnumerateCompatibleDevices` / `Satisfies` companion methods, and the `UnsupportedKernelFeatureException` runtime safety net for cases where a kernel pins to a specific backend.

## WebGPU Backend

The fastest backend. Uses GPU compute shaders via the WebGPU API, transpiling kernels to WGSL.

### Setup

```csharp
using ILGPU;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGPU;

using var context = await Context.CreateAsync(builder => builder.WebGPU());
var devices = context.GetWebGPUDevices();

if (devices.Count > 0)
{
    using var accelerator = await devices[0].CreateAcceleratorAsync(context);
    // Use accelerator...
}
```

### Configuration Options

Use `WebGPUBackendOptions` to configure the transpiler:

```csharp
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

// Default: Dekker f64 emulation (good precision, fast)
var options = new WebGPUBackendOptions();

// Ozaki f64 emulation (strict IEEE 754)
var options = new WebGPUBackendOptions { F64Emulation = F64EmulationMode.Ozaki };

// Disable f64 emulation (double → float, max performance)
var options = new WebGPUBackendOptions { F64Emulation = F64EmulationMode.Disabled };

using var accelerator = await devices[0].CreateAcceleratorAsync(context, options);
```

### Features

- **Compute shaders** — full GPU compute via `@compute @workgroup_size`
- **Shared memory** — `SharedMemory.Allocate<T>()` maps to `var<workgroup>`
- **Barriers** — `Group.Barrier()` maps to `workgroupBarrier()`
- **Atomics** — `Atomic.Add`, `Atomic.Min`, `Atomic.Max`, `Atomic.CompareExchange`
- **ILGPU Algorithms** — RadixSort, Scan, Reduce, Histogram, and other algorithm extensions are fully supported and tested (including large-scale sorts up to 4M+ elements). Use `CreateRadixSortPairs<TKey, TValue>()`, `CreateScan()`, `CreateReduce()`, etc. the same way as on desktop backends
- **Subgroups** — `Group.Broadcast`, `Warp.Shuffle` (when the `subgroups` extension is available)
- **Auto-detected extensions** — probes adapter for `shader-f16`, `subgroups`, `timestamp-query`, etc.
- **Device loss detection** — monitors `device.lost` promise; `IsDeviceLost` property and `DeviceLost` event fire on unexpected GPU device loss (driver crash, GPU reset, VRAM exhaustion). Subsequent dispatch/synchronize calls throw `InvalidOperationException` with a clear message

### ILGPU Algorithms (WebGPU)

The WebGPU backend fully supports ILGPU.Algorithms, including RadixSort, Scan, Reduce, and Histogram. All algorithm tests pass in the browser test suite. Example:

```csharp
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Algorithms.RadixSortOperations;

var radixSort = accelerator.CreateRadixSortPairs<float, Stride1D.Dense, int, Stride1D.Dense, AscendingFloat>();
var tempSize = accelerator.ComputeRadixSortPairsTempStorageSize<float, int, AscendingFloat>(keys.Length);
using var tempBuf = accelerator.Allocate1D<int>(tempSize);

radixSort(stream, keys.View, values.View, tempBuf.View);
await accelerator.SynchronizeAsync();
```

### Important: Buffer Copy Behavior

Synchronous GPU→CPU copy methods (`CopyTo`, `CopyToCPU`, `GetAsArray1D`) throw `NotSupportedException` on WebGPU because GPU readback requires an async `mapAsync` call. Use `CopyToHostAsync` instead.

GPU→GPU copies via `CopyFrom` work perfectly — they use the native `CopyBufferToBuffer` command. See [Memory & Buffers](memory-and-buffers.md#cross-backend-buffer-operations-reference) for the full compatibility table.

### Browser Support

Chrome/Edge 113+, Firefox Nightly (with `dom.webgpu.enabled`).

## WebGL Backend

Universal GPU backend that works in virtually every modern browser. Transpiles kernels to GLSL ES 3.0 vertex shaders and uses Transform Feedback for GPU compute.

### Setup

```csharp
using ILGPU;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGL;

using var context = await Context.CreateAsync(builder => builder.WebGL());
var devices = context.GetWebGLDevices();

if (devices.Count > 0)
{
    using var accelerator = devices[0].CreateAccelerator(context);
    // Use accelerator...
}
```

### Configuration Options

```csharp
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGL.Backend;

// Default: Dekker f64 emulation
var options = new WebGLBackendOptions();

// Ozaki f64 emulation (strict IEEE 754)
var options = new WebGLBackendOptions { F64Emulation = F64EmulationMode.Ozaki };

using var accelerator = devices[0].CreateAccelerator(context, options);
```

### Architecture

The WebGL backend is unique — all GL calls are dispatched to a dedicated Web Worker via `glWorker.js`. This keeps the main thread responsive even during intensive GPU compute.

Buffers persist as **GPU-resident textures** in the worker. Kernel dispatch sends buffer references (not data) — no `ArrayBuffer` transfers occur per dispatch. Data only moves to the CPU when explicitly requested via `CopyToHostAsync()`.

### Limitations

- **No shared memory** — GLSL ES 3.0 vertex shaders don't support workgroup memory
- **No atomics** — not available in the vertex shader stage  
- **No barriers** — no workgroup synchronization
- **No sync GPU→CPU** — `CopyTo`/`CopyToCPU`/`GetAsArray1D` throw `NotSupportedException`. Use `CopyToHostAsync`. GPU→GPU `CopyFrom` works.
- **No in-kernel group/warp ops** - `Group.ExclusiveScan`/`InclusiveScan`/`AllReduce`/`Reduce` and the `Warp` equivalents need shared memory + barriers, which the vertex-shader model lacks. Calling them in a kernel throws `UnsupportedKernelFeatureException` at compile time (rather than silently returning 0). Use the host-level algorithms below instead, or declare `RequiresSharedMemory = true` on `AcceleratorRequirements` to filter WebGL at selection time.

### ILGPU Algorithms

Despite having no shared memory, atomics, or barriers, WebGL supports the **host-level** algorithm extensions via shared-memory-free multi-dispatch emulation (the draw-call boundary between passes acts as the global barrier):

- **`RadixSort`** - `CreateRadixSort` / `CreateRadixSortPairs`, all key types (`int`/`uint`/`float`/`long`/`double`/`Half`), keys-only and pairs, ascending/descending, power-of-two and arbitrary counts, up to 4M elements. Uses a render-to-texture GPGPU scatter (1-bit stable split per pass); `Half` keys sort via an unpacked-f32 working representation since the whole-texel scatter can't move a sub-texel `Half`.
- **`CreateScan`** - Hillis-Steele multi-pass scan (log2(N) element-wise passes, ping-pong buffers).
- **`CreateReduce`** - multi-pass pairwise reduction.

These are used the same way as on desktop backends. (The in-kernel `GroupExtensions` group/warp scan & reduce above are a *different*, unsupported API - those throw.)

### Browser Support

All modern browsers — Chrome, Edge, Firefox, Safari, mobile browsers.

## Wasm Backend

Compiles kernels to native WebAssembly binary modules and dispatches them across Web Workers for parallel CPU execution.

### Setup

```csharp
using ILGPU;
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.Wasm;

using var context = await Context.CreateAsync(builder => builder.Wasm());
var devices = context.GetDevices<WasmILGPUDevice>();

if (devices.Count > 0)
{
    using var accelerator = await devices[0].CreateAcceleratorAsync(context);
    // Use accelerator...
}
```

### Features

- **Multi-worker dispatch** — distributes work across all available CPU cores
- **Native 64-bit** — `double` and `long` work natively (no emulation needed)
- **Shared memory** — uses `SharedArrayBuffer` for zero-copy data sharing
- **Group.Barrier()** — full workgroup barrier synchronization across Web Workers
- **Dynamic shared memory** — runtime-sized workgroup memory via `SharedMemory.GetDynamic()`
- **Group.Broadcast** — intra-group value sharing
- **Atomics** — supported via `SharedArrayBuffer`
- **ILGPU Algorithms** — RadixSort, Scan, Reduce, and Histogram are fully supported with full `hardwareConcurrency` multi-worker barrier synchronization. The Wasm backend uses fiber-based phase dispatch with pure spin barriers, per-thread scratch memory, and an in-Wasm phase dispatcher that eliminates JS-Wasm boundary crossings between phases

### Limitations

- **No sync GPU→CPU** — `CopyTo`/`CopyToCPU`/`GetAsArray1D` throw `NotSupportedException`. Use `CopyToHostAsync`. GPU→GPU `CopyFrom` works.

### SharedArrayBuffer Requirement

For multi-worker mode, the page must be cross-origin isolated (COOP/COEP headers). The demo includes `coi-serviceworker.js` which handles this automatically. Without SharedArrayBuffer, the Wasm backend falls back to a single off-thread worker.

### Browser Support

All modern browsers that support Blazor WebAssembly.

## Desktop Backends (Cuda, OpenCL & CPU)

When running outside the browser (console apps, WPF, ASP.NET, etc.), SpawnDev.ILGPU uses ILGPU's native Cuda and OpenCL backends automatically. These are registered by the standard `builder.AllAccelerators()` call.

### Setup

```csharp
// Recommended: use the unified async pattern (same as Blazor WASM)
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();

// Lists all detected devices
foreach (var device in context)
    Console.WriteLine($"{device.Name} ({device.AcceleratorType})");
```

### Cuda

- Requires an NVIDIA GPU with a supported driver
- Uses PTX intermediate representation
- Best performance for NVIDIA hardware
- Full ILGPU feature support (shared memory, atomics, warp ops)

### OpenCL

- Supports NVIDIA, AMD, and Intel GPUs
- Uses OpenCL C kernel language
- OpenCL 2.0+ and OpenCL 3.0 devices are supported
- NVIDIA GPUs with OpenCL 3.0 drivers are now compatible — the `GenericAddressSpace` requirement that previously blocked these devices has been relaxed
- Subgroup-dependent tests (e.g., `Warp.Shuffle`) are dynamically skipped on devices that don't report subgroup support

### CPU (Desktop)

Multi-threaded CPU accelerator using `Parallel.For`. Useful as a reference or for machines without GPU drivers. Full ILGPU feature support (shared memory, barriers, atomics). Not available in the browser — use the Wasm backend for off-main-thread compute in Blazor.

```csharp
using ILGPU;
using ILGPU.Runtime.CPU;

using var context = Context.Create(b => b.CPU());
using var accelerator = context.CreateCPUAccelerator(0);
```

> **Note:** Cuda, OpenCL, and CPU are not available in Blazor WebAssembly — they are skipped silently when registering via `AllAcceleratorsAsync()` in the browser.

## 64-bit Emulation

GPU hardware typically supports only 32-bit operations. Both GPU backends provide software emulation for 64-bit types.

**i64 emulation** (`long`/`ulong` via `vec2<u32>`) is always enabled — ILGPU's IR requires Int64 for `ArrayView.Length` and indices.

**f64 emulation** (`double`) is configurable via `F64EmulationMode`:

| | **Dekker** (Default) | **Ozaki** | **Disabled** |
|---|---|---|---|
| **Representation** | `vec2<f32>` (high + low) | `vec4<f32>` (quad-float) | Native `f32` |
| **Precision** | ~48–53 bits mantissa | Strict IEEE 754 | 32-bit only |
| **Memory** | 8 bytes | 16 bytes | 4 bytes |
| **Performance** | ⚡ Fast | 🐢 ~2× slower | ⚡⚡ Fastest |
| **Best for** | General compute, fractals | Scientific, financial | Rendering, max perf |

### Configuration Examples

```csharp
using SpawnDev.ILGPU;

// Default: Dekker double-float emulation
var options = new WebGPUBackendOptions();

// Strict IEEE 754 precision
var options = new WebGPUBackendOptions { F64Emulation = F64EmulationMode.Ozaki };

// Disable f64 emulation (double promoted to float, max performance)
var options = new WebGPUBackendOptions { F64Emulation = F64EmulationMode.Disabled };
```

## Runtime Backend Switching

You can switch backends at runtime by disposing old resources and creating new ones:

```csharp
// Dispose old resources
kernel = null;
outputBuffer?.Dispose();
accelerator?.Dispose();
context?.Dispose();

// Create new backend
context = await Context.CreateAsync(builder => builder.WebGL());
var devices = context.GetWebGLDevices();
accelerator = devices[0].CreateAccelerator(context);

// Reload kernel on new accelerator
kernel = accelerator.LoadAutoGroupedStreamKernel<...>(MyKernel);
outputBuffer = accelerator.Allocate1D<float>(length);
```

> **Important:** Kernels, buffers, and other resources are tied to their accelerator. You must recreate everything when switching backends.

## Verbose Logging

Enable debug logging per-backend:

```csharp
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.ILGPU.WebGL.Backend;
using SpawnDev.ILGPU.Wasm.Backend;

WebGPUBackend.VerboseLogging = true;   // WebGPU
WebGLBackend.VerboseLogging = true;    // WebGL
WasmBackend.VerboseLogging = true;     // Wasm
```

This outputs compiled shader source, buffer binding details, and dispatch information to the browser console.

## Compiled Shader Inspection

After loading a kernel the generated shader source is captured automatically:

```csharp
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.ILGPU.WebGL;

// Available immediately after LoadAutoGroupedStreamKernel / LoadStreamKernel
string? wgsl = WebGPUAccelerator.LastGeneratedWGSL;   // WebGPU backend
string? glsl = WebGLAccelerator.LastGeneratedGLSL;    // WebGL backend
```

Both properties are `static` and updated on every kernel load (not just on dispatch), so they always reflect the most recently compiled shader regardless of whether the kernel has been launched yet.
