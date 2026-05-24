# SpawnDev.ILGPU

[![NuGet](https://img.shields.io/nuget/v/SpawnDev.ILGPU.svg?)](https://www.nuget.org/packages/SpawnDev.ILGPU)

**Run [ILGPU](https://github.com/m4rs-mt/ILGPU) C# kernels on WebGPU, WebGL, Wasm, Cuda, OpenCL, and CPU - from a single codebase.**  
Write parallel compute code in C# and let the library pick the best available backend automatically. In the browser, three backends (WebGPU, WebGL, Wasm) bring GPU-accelerated compute to virtually every modern browser. On desktop and server, ILGPU's native Cuda and OpenCL backends are available alongside CPU. The same async extension methods work everywhere.

> **Your existing ILGPU kernels run in the browser with zero changes to the kernel code - and the same code runs on desktop too.**

## Recent Highlights

**4.9.9 (current):** New backend-agnostic `CopyFromAsync` extension (`ArrayView<T>` / `ArrayView1D<T,TStride>` / `MemoryBuffer1D<T,TStride>`) - the async mirror of `CopyFrom`, draining pending Wasm worker dispatches before the copy (no-op on other backends). WebGPU scalar-slot drift fix for kernels with body-struct + trailing-scalar params (unblocks ML TensorView migration). WebGL GPU→GPU `CopyTo`/`CopyFrom` stale-CPU-side fix. Wasm `wait32`/`notify` barriers re-confirmed to race on V8 - pure-spin stays, gated default-off re-test harness retained.

**4.9.6-4.9.8:** PTX vector memory intrinsics (`ld.v2/v4.f32`) + `ArrayView.LoadVectorized`/`StoreVectorized`, `System.Numerics.BitOperations` mapped to hardware GPU intrinsics, CUDA `DefaultMaxRegistersPerThread` occupancy fix, WebGPU `pow(negative_base, runtime_exp)` NaN fix, WebGLDevice probe-context leak fix.

**4.9.5:** WebGPU direct-param coalesce - kernels with more than 9 `ArrayView` parameters no longer hit Chrome's 10-binding limit (i32/u32/f32 and sub-word types coalesced into a shared `array<atomic<u32>>` binding). IR Inliner cumulative-IL budget - kernels with deep call graphs (VP9 entropy walker, large codec helpers) no longer produce 50K+ local Wasm functions that crash V8/Naga. WebGL multi-view body-struct decomposition and WGSL/WebGL codegen correctness fixes for `[NoInlining]` helpers with 64-bit indices, sub-word ArrayViews, and cross-block pointer LEAs. GroupDimX extent clamp. Zero real test failures across all 6 backends.

**4.9.4:** Wasm `CopyToHostAsync` partial readback + WebGPU `Half` NaN/Inf bitcast fix.

**4.9.3:** `ArrayView<T>.CopyToHostAsync()` per-backend partial readback extension.

**4.9.2:** `[NoInlining]` helper fn-definition emission (compile-cliff fix), `AcceleratorRequirements` capability gating, OpenCL phi-binding codegen fix, NaN/Inf codegen across all backends.

**4.9.0:** Complete sub-word data type support (`Int8`, `UInt8`, `Int16`, `UInt16`, `Float16`) across all 6 GPU backends + `CopyFromJS` zero-copy JS->GPU transfer.

**4.8.0:** Wasm worker function caching (3-4x kernel dispatch speedup), full worker parallelism for non-barrier kernels.

**4.7.1:** GPU-side test verification (`GpuTestVerify`, 10x+ faster than CPU readback), CPU default optimization, full DI integration.

**4.6.0:** Wasm fiber-based barrier dispatch (full ILGPU Algorithms on Wasm, all RadixSort variants 100K-4M+ elements), 20+ Wasm bugs fixed, `ShaderDebugService` auto-dump for all generated shader code.

**See [CHANGELOG.md](CHANGELOG.md)** for the full per-version history including code samples and per-backend implementation details.

### Helper Method Fn-Definition Emission - Compile Cliff Fix (4.9.2)

The most-asked-about feature from 4.9.2 - large helper methods called many times from a kernel produce a multi-thousand-line WGSL/GLSL `fn main()` that hits the browser shader validator's size limit (Tint rejects with `Invalid BindGroupLayout`). Tag the helper with `[MethodImpl(MethodImplOptions.NoInlining)]` and SpawnDev.ILGPU emits a real WGSL/GLSL `fn` definition + N call sites instead of N inline expansions:

```csharp
using System.Runtime.CompilerServices;

private static void IdctKernel(Index1D blockIdx, ArrayView<short> coeffs, ArrayView<byte> dest)
{
    // 32 calls to the helper - default inlining = ~3,800-line WGSL = compile cliff
    Idct16Row(coeffs[r0], coeffs[r1], /* ... 14 short inputs */, out int o0, out int o1, /* ... */);
    // ... 31 more calls
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static void Idct16Row(
    short i0, short i1, /* ... */,
    out int o0, out int o1, /* ... */)
{
    // 7-stage butterfly arithmetic
}
```

Supports `int / float / short / byte / Half / bool` value params, `ref T` / `out T` for primitive value types (lowers to `ptr<function, T>` on WGSL, `inout T` on GLSL), struct value types, multiple call sites with per-call scratch slots. **Not yet supported on `[NoInlining]` helpers**: `ArrayView<T>` parameters, `LocalMemory<T>` access, barrier / shared-memory access — for those, use default inlining.

See [`Docs/kernels.md` — Helper Methods and Inlining](Docs/kernels.md#helper-methods-and-inlining) for when to use `[NoInlining]`, when not to, and what each backend does.

For everything else - per-version code samples, per-backend implementation details, the rc.X investigation logs - see **[CHANGELOG.md](CHANGELOG.md)**.


## Architecture

**Browser backends** (Blazor WebAssembly) - auto-selected: WebGPU -> WebGL -> Wasm

| | WebGPU | WebGL | Wasm |
|---|---|---|---|
| **Compiles to** | WGSL | GLSL ES 3.0 | Wasm binary |
| **Runs on** | GPU | GPU | Web Workers |

**Desktop backends** (Console, WPF, ASP.NET) - auto-selected: Cuda -> OpenCL -> CPU

| | Cuda | OpenCL | CPU |
|---|---|---|---|
| **Compiles to** | PTX | OpenCL C | - |
| **Runs on** | NVIDIA GPU | Any GPU | CPU cores |

## Demo Applications

### Browser Demo (Blazor WebAssembly)

The [Live Demo](https://lostbeard.github.io/SpawnDev.ILGPU/) source is in [SpawnDev.ILGPU.Demo](SpawnDev.ILGPU.Demo):
- [Fractal Explorer](https://lostbeard.github.io/SpawnDev.ILGPU/fractals) - Interactive Mandelbrot / Multi-fractal Explorer with double-precision zoom
- [3D Raymarching](https://lostbeard.github.io/SpawnDev.ILGPU/3d) - Real-time GPU raymarched scenes
- [GPU Boids](https://lostbeard.github.io/SpawnDev.ILGPU/boids) - 3D flocking simulation with GPU physics
- [Game of Life](https://lostbeard.github.io/SpawnDev.ILGPU/gameoflife) - Conway's Game of Life on the GPU
- [Benchmarks](https://lostbeard.github.io/SpawnDev.ILGPU/benchmarks) - Performance comparison across all backends
- [Unit Tests](https://lostbeard.github.io/SpawnDev.ILGPU/tests) - Comprehensive test suite for all backends

### Desktop Demo (WPF)

The [WPF Demo](SpawnDev.ILGPU.WpfDemo) runs the same shared kernels on CUDA, OpenCL, and CPU with live backend switching:
- Fractal Explorer - Interactive Mandelbrot / Multi-fractal Explorer with double-precision zoom
- 3D Raymarching - Real-time GPU raymarched scenes
- GPU Boids - 3D flocking simulation with GPU physics
- Benchmarks - Performance comparison across CUDA, OpenCL, and CPU backends

### Screenshots
[![Desktop Benchmark Screenshot](https://raw.githubusercontent.com/LostBeard/SpawnDev.ILGPU/master/SpawnDev.ILGPU.Demo/wwwroot/screenshots/benchmark-desktop-4.jpg)](https://lostbeard.github.io/SpawnDev.ILGPU/benchmarks)  
[![Browser Benchmark Screenshot](https://raw.githubusercontent.com/LostBeard/SpawnDev.ILGPU/master/SpawnDev.ILGPU.Demo/wwwroot/screenshots/benchmark-browser-4.jpg)](https://lostbeard.github.io/SpawnDev.ILGPU/benchmarks)   
[![Fractal Explorer Screenshot](https://raw.githubusercontent.com/LostBeard/SpawnDev.ILGPU/master/SpawnDev.ILGPU.Demo/wwwroot/screenshots/spawndev-ilgpu-fractal-explorer-3.jpg)](https://lostbeard.github.io/SpawnDev.ILGPU/fractals)

## Documentation

Comprehensive documentation is available in the [Docs](Docs/) folder:

- **[Getting Started](Docs/getting-started.md)** - Installation, setup, first kernel
- **[Backends](Docs/backends.md)** - WebGPU, WebGL, Wasm, Cuda, OpenCL, CPU setup & configuration
- **[Writing Kernels](Docs/kernels.md)** - Kernel rules, index types, math functions, shared memory
- **[Memory & Buffers](Docs/memory-and-buffers.md)** - Allocation, async readback, zero-allocation patterns
- **[Data Type Support](Docs/data-type-support.md)** - Sub-word types (Int8, UInt8, Int16, UInt16, Float16), 64-bit emulation, per-backend details
- **[Canvas Rendering](Docs/canvas-rendering.md)** - `ICanvasRenderer`, zero-copy GPU->canvas blitting, per-backend details
- **[Advanced Patterns](Docs/advanced-patterns.md)** - Device sharing, external buffers, GPU intrinsics, render loops
- **[CUDA Libraries](Docs/cuda-libraries.md)** - nvJPEG, cuRand, cuBLAS, cuFFT, NVML wrappers
- **[Limitations](Docs/limitations.md)** - Blazor WASM constraints, browser compatibility
- **[QR Codes](Docs/qr-codes.md)** - GPU-accelerated QR code encoder, decoder, renderer with logo overlay
- **[API Reference](Docs/api-reference.md)** - Public API surface by namespace

## Browser Backends (Blazor WebAssembly)

| | 🎮 **WebGPU** | 🖼️ **WebGL** | 🧊 **Wasm** |
|---|---|---|---|
| **Executes on** | GPU | GPU | Web Workers |
| **Transpiles to** | WGSL | GLSL ES 3.0 | WebAssembly binary |
| **Technique** | Compute shader | Transform Feedback | Multi-worker |
| **Blocking** | Non-blocking | Non-blocking | Non-blocking |
| **SharedArrayBuffer** | Not required | Not required | Required for multi-worker |
| **Shared Memory** | ✅ | ❌ | ✅ |
| **Group.Barrier()** | ✅ | ❌ | ✅ |
| **Dynamic Shared Memory** | ✅ | ❌ | ✅ |
| **ILGPU Algorithms** | ✅ RadixSort, Scan, Reduce, etc. | ❌ | ✅ RadixSort, Scan, Reduce, Histogram |
| **Atomics** | ✅ | ❌ | ✅ |
| **Sub-word types** | ✅ Int8/UInt8/Int16/UInt16/Float16 | ✅ Int8/UInt8/Int16/UInt16/Float16 | ✅ Int8/UInt8/Int16/UInt16/Float16 |
| **64-bit (f64/i64)** | ✅ Emulated | ✅ Emulated | ✅ Native |
| **CopyFromJS** | ✅ | ✅ | ✅ |
| **Browser support** | Chrome/Edge 113+ | All modern browsers | All modern browsers |
| **Best for** | GPU compute (modern) | GPU compute (universal) | General compute |

**Auto-selection priority:** WebGPU -> WebGL -> Wasm

## Desktop Backends (Console, WPF, ASP.NET, etc.)

SpawnDev.ILGPU bundles ILGPU's native backends, so the same NuGet package works on desktop and server too.

| | 🚀 **Cuda** | 🔧 **OpenCL** | 🐢 **CPU** |
|---|---|---|---|
| **Executes on** | NVIDIA GPU | NVIDIA/AMD/Intel GPU | CPU cores |
| **Transpiles to** | PTX | OpenCL C | - (interpreted) |
| **Shared Memory** | ✅ | ✅ | ✅ |
| **Atomics** | ✅ | ✅ | ✅ |
| **64-bit** | ✅ Native | ✅ Native | ✅ Native |
| **Requirement** | NVIDIA GPU + driver | OpenCL 2.0+ or 3.0 GPU | None |

> **OpenCL 3.0 support:** NVIDIA GPUs with OpenCL 3.0 drivers are now supported. The `GenericAddressSpace` requirement that previously blocked these devices has been relaxed, significantly increasing OpenCL device compatibility.

**Auto-selection:** Cuda -> OpenCL -> CPU (via `CreatePreferredAcceleratorAsync`)

## Features

- **Sub-word data types** - `Int8`, `UInt8`, `Int16`, `UInt16`, and `Float16` (`ILGPU.Half`) buffer access on all 6 backends. Packed storage with correct stride handling per backend. `Half.Abs`, `Half.Min`, `Half.Max`, `Half.Clamp` intrinsics
- **CopyFromJS** - Write JavaScript `TypedArray` or `ArrayBuffer` data directly to GPU memory without .NET heap allocation. Available on all browser backends
- **Lambda kernels** - Write kernels as capturing C# lambdas - captured scalar values are automatically passed to the GPU at dispatch time. No boilerplate, all 6 backends
- **Higher-order kernels** - `DelegateSpecialization<Func<T,R>>` lets you pass operations as kernel parameters. The delegate is resolved and inlined at compile time - one kernel, many behaviors
- **Cross-platform** - Same kernel code runs in browser (WebGPU, WebGL, Wasm) and desktop (Cuda, OpenCL, CPU) from one NuGet package
- **Automatic backend selection** - `CreatePreferredAcceleratorAsync()` picks the best backend on any platform (browser or desktop)
- **Unified async API** - `SynchronizeAsync()`, `CopyToHostAsync()`, and `CopyFromAsync()` work everywhere, falling back to synchronous calls on desktop
- **ILGPU-compatible** - Use familiar APIs (`ArrayView`, `Index1D/2D/3D`, math intrinsics, etc.)
- **WGSL transpilation** - C# kernels automatically compiled to WebGPU Shading Language
- **GLSL transpilation** - C# kernels compiled to GLSL ES 3.0 vertex shaders with Transform Feedback for GPU compute
- **Wasm compilation** - C# kernels compiled to native WebAssembly binary modules
- **64-bit emulation** - `long`/`ulong` (i64) always emulated via `vec2<u32>` (required by ILGPU IR). `double` (f64) emulation configurable via `F64EmulationMode`: fast Dekker (`vec2<f32>`, default), precise Ozaki (`vec4<f32>`), or Disabled (promoted to f32)
- **WebGPU extension auto-detection** - Probes adapter for `shader-f16`, `subgroups`, `timestamp-query`, and other features; conditionally enables them on the device
- **Subgroup operations** - `Group.Broadcast` and `Warp.Shuffle` are supported on the WebGPU backend when the browser supports the `subgroups` extension
- **Multi-worker dispatch** - Wasm backend distributes work across all available CPU cores via SharedArrayBuffer; falls back to a single off-thread worker when SAB is unavailable
- **Worker function caching** - Compiled `AsyncFunction` objects cached in Wasm worker bootstrap, eliminating V8 recompilation per dispatch (3-4x speedup)
- **Zero-copy canvas rendering** - `ICanvasRenderer` presents pixel buffers to HTML canvases without CPU readback on GPU backends: WebGPU uses a fullscreen-triangle render pass reading directly from GPU storage; WebGL transfers an `ImageBitmap` from its worker and draws synchronously; Wasm reuses a cached `ImageData`. One API, all backends: `CanvasRendererFactory.Create(accelerator)`
- **Blazor WebAssembly** - Seamless integration via [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS)
- **Shared memory & barriers** - Static and dynamic workgroup memory with `Group.Barrier()` synchronization (WebGPU, Wasm, Cuda, OpenCL)
- **ILGPU Algorithms** - RadixSort, Scan, Reduce, Histogram, and other algorithm extensions are fully supported on WebGPU (including large-scale sorts up to 4M+ elements) and Wasm (with multi-worker barrier synchronization), tested in-browser across all backends
- **Broadcast** - `Group.Broadcast` for intra-group value sharing (WebGPU, Wasm)
- **Device loss handling** - WebGPU monitors `device.lost` and WebGL monitors `webglcontextlost`; `IsDeviceLost`/`IsContextLost` properties and `DeviceLost`/`ContextLost` events enable applications to detect GPU device loss and fail fast with clear errors instead of silent corruption
- **GpuMatrix4x4** - GPU-friendly 4x4 matrix struct that auto-transposes from .NET's row-major `Matrix4x4` to GPU column-major order. Use `TransformPoint` and `TransformDirection` directly inside kernels for 3D transformations
- **No native dependencies** - Entirely written in C#

## Installation

```bash
dotnet add package SpawnDev.ILGPU
```

## Quick Start - Blazor WebAssembly

### 1. Configure Program.cs

SpawnDev.ILGPU requires [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) for browser interop.

```csharp
using SpawnDev.BlazorJS;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Add BlazorJS services
builder.Services.AddBlazorJSRuntime();

await builder.Build().BlazorJSRunAsync();
```

### 2. Automatic Backend Selection

The library discovers all available browser backends and picks the best one (WebGPU -> WebGL -> Wasm):

```csharp
using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.ILGPU;

// Initialize context with all available backends
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());

// Create the best available accelerator (WebGPU > WebGL > Wasm)
using var accelerator = await context.CreatePreferredAcceleratorAsync();

// Allocate buffers and run a kernel - same API regardless of backend
int length = 256;
using var bufA = accelerator.Allocate1D(Enumerable.Range(0, length).Select(i => (float)i).ToArray());
using var bufB = accelerator.Allocate1D(Enumerable.Range(0, length).Select(i => (float)i * 2f).ToArray());
using var bufC = accelerator.Allocate1D<float>(length);

var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAddKernel);
kernel((Index1D)length, bufA.View, bufB.View, bufC.View);

await accelerator.SynchronizeAsync();
var results = await bufC.CopyToHostAsync<float>();

// The kernel - runs on GPU or Wasm transparently
static void VectorAddKernel(Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c)
{
    c[index] = a[index] + b[index];
}
```

### 3. Using a Specific Browser Backend

```csharp
// WebGPU - GPU compute via WGSL
using var context = await Context.CreateAsync(builder => builder.WebGPU());
var device = context.GetWebGPUDevices()[0];
using var accelerator = await device.CreateAcceleratorAsync(context);
```

```csharp
// WebGL - GPU compute via GLSL ES 3.0 + Transform Feedback (works on virtually all browsers)
using var context = await Context.CreateAsync(builder => builder.WebGL());
var device = context.GetWebGLDevices()[0];
using var accelerator = await device.CreateAcceleratorAsync(context);
```

```csharp
// Wasm - native WebAssembly binary
using var context = await Context.CreateAsync(builder => builder.Wasm());
var device = context.GetDevices<WasmILGPUDevice>()[0];
using var accelerator = await device.CreateAcceleratorAsync(context);
```

## Quick Start - Desktop / Server

SpawnDev.ILGPU also works in console, WPF, ASP.NET, and other .NET apps. The **same async pattern** used in Blazor WASM works on desktop too:

```csharp
using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.ILGPU;

// SAME code as Blazor WASM - AllAcceleratorsAsync auto-detects the environment
// Browser: registers WebGPU, WebGL, Wasm
// Desktop: registers Cuda, OpenCL, CPU (browser backends are skipped)
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync();

Console.WriteLine($"Using: {accelerator.Name} ({accelerator.AcceleratorType})");

// Same kernel code, same async extensions
int length = 256;
using var bufA = accelerator.Allocate1D(Enumerable.Range(0, length).Select(i => (float)i).ToArray());
using var bufB = accelerator.Allocate1D(Enumerable.Range(0, length).Select(i => (float)i * 2f).ToArray());
using var bufC = accelerator.Allocate1D<float>(length);

var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAddKernel);
kernel((Index1D)length, bufA.View, bufB.View, bufC.View);

// SynchronizeAsync/CopyToHostAsync fall back to synchronous calls on desktop
await accelerator.SynchronizeAsync();
var results = await bufC.CopyToHostAsync<float>();

Console.WriteLine($"result[0]={results[0]}, result[255]={results[255]}");

static void VectorAddKernel(Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c)
{
    c[index] = a[index] + b[index];
}
```

> **Same kernel, any platform.** The `VectorAddKernel` above is identical in both examples. Write once, run on WebGPU, WebGL, Wasm, Cuda, OpenCL, or CPU.

> **Why async?** Browser backends **require** async - Blazor WASM's single-threaded environment will deadlock on synchronous calls. Desktop backends **support both** sync and async, with async extensions gracefully falling back to synchronous ILGPU calls. Therefore, the **async pattern is always recommended** for maximum portability.

## Testing

### PlaywrightMultiTest (Unified Runner)

All desktop and browser tests run in a single `dotnet test` invocation via the **PlaywrightMultiTest** NUnit project:

```bash
# Run all tests (desktop + browser) with timestamped results
timestamp=$(date +%Y%m%d_%H%M%S) && dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj \
  --logger "trx;LogFileName=results_${timestamp}.trx" \
  --results-directory PlaywrightMultiTest/TestResults

# Run only WebGPU tests
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj \
  --filter "FullyQualifiedName~WebGPUTests."

# Run a specific test
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj \
  --filter "FullyQualifiedName~WebGPUTests.AlgorithmRadixSortPairsTest"
```

**How it works:**
- Publishes Blazor WASM and Console projects automatically
- Launches Chromium via Playwright for browser tests (with `--enable-unsafe-webgpu`)
- Runs desktop tests as individual subprocesses
- Detects Blazor error UI during tests and captures browser console errors/warnings
- All results surfaced as standard NUnit test cases with `.trx` output

### Browser Tests (Manual)

Start the demo app and navigate to `/tests` to run the browser test suite interactively:

```bash
dotnet run --project SpawnDev.ILGPU.Demo
```

## Test Coverage

Comprehensive test suite across eight test suites covering all core features on both browser and desktop. All tests are run via the unified **PlaywrightMultiTest** runner in a single `dotnet test` invocation.

### Test Suites

#### Browser (Blazor WebAssembly via Playwright)

| Suite | Backend | What's Tested |
|-------|---------|---------------|
| **WebGPUTests** | WebGPU | Full ILGPU feature set on GPU via WGSL, including RadixSort, Scan, Reduce |
| **WebGPUNoSubgroupsTests** | WebGPU (no subgroups) | Same tests with subgroups force-disabled to verify shared-memory emulation |
| **WebGLTests** | WebGL | GPU compute via GLSL ES 3.0, f64/i64 emulation |
| **WasmTests** | Wasm | Native WebAssembly binary dispatch to workers, shared memory, barriers |
| **DefaultTests** | Auto | Device enumeration, preferred backend, kernel execution |

#### Desktop (Console Runner via subprocess)

| Suite | Backend | What's Tested |
|-------|---------|---------------|
| **CudaTests** | CUDA | Full ILGPU feature set on NVIDIA GPU |
| **OpenCLTests** | OpenCL | GPU compute on NVIDIA/AMD/Intel, dynamic subgroup feature detection |
| **CPUTests** | CPU | Multi-threaded CPU accelerator (warp=8, warps=8, group size 64) |

### Coverage by Area

| Area | What's Tested | Status |
|------|---------------|--------|
| **Memory** | Allocation, transfer, copy, views | ✅ |
| **Indexing** | 1D, 2D, 3D kernels, boundary conditions | ✅ |
| **Arithmetic** | +, -, *, /, %, negation, complex expressions | ✅ |
| **Bitwise** | AND, OR, XOR, NOT, shifts (<<, >>) | ✅ |
| **Math Functions** | sin, cos, tan, exp, log, sqrt, pow, abs, min, max | ✅ |
| **Atomics** | Add, Min, Max, CompareExchange, Xor | ✅ |
| **Control Flow** | if/else, loops, nested, short-circuit | ✅ |
| **Structs** | Simple, nested, with arrays | ✅ |
| **Type Casting** | float<->int, uint, mixed precision | ✅ |
| **Sub-Word Types** | Int8, UInt8, Int16, UInt16, Float16 buffer read/write/roundtrip/CopyFromJS | ✅ |
| **Half Intrinsics** | Abs, Min, Max, Clamp across all backends | ✅ |
| **64-bit Emulation** | `double` and `long` via software emulation (WebGPU, WebGL) | ✅ |
| **GPU Patterns** | Stencil, reduction, matrix multiply, lerp, smoothstep | ✅ |
| **Shared Memory** | Static and dynamic workgroup memory with `Group.Barrier()` | ✅ |
| **Broadcast & Subgroups** | `Group.Broadcast`, `Warp.Shuffle` (WebGPU with subgroups extension) | ✅ |
| **Dynamic Shared Memory** | Runtime-sized workgroup memory via `SharedMemory.GetDynamic()` | ✅ |
| **ILGPU Algorithms** | RadixSort (pairs, non-pow2, descending, large), Scan, Reduce, Histogram | ✅ All backends including Wasm |
| **Special Values** | NaN, Infinity detection | ✅ |
| **Backend Selection** | Auto-discovery, priority, cross-backend kernel execution | ✅ |
| **GpuMatrix4x4** | Identity, translation, LookAt transforms across all backends | ✅ |
| **Lambda Kernels** | Capturing lambdas with scalar captures, multi-field, ArrayView rejection | ✅ |
| **DelegateSpecialization** | Static method targets, cache validation, multi-target, rejection | ✅ |
| **CopyFromJS** | TypedArray and ArrayBuffer direct-to-GPU writes on all browser backends | ✅ |

## Browser Requirements

| Backend | Browser Support |
|---------|----------------|
| **WebGPU** | Chrome/Edge 113+, Firefox Nightly (`dom.webgpu.enabled`) |
| **WebGL** | ✅ All modern browsers (Chrome, Edge, Firefox, Safari, mobile browsers) |
| **Wasm** | All modern browsers (compatible with every browser that supports Blazor WASM) |

> **GPU on every device:** WebGL support means GPU-accelerated compute works on virtually every browser and device - including mobile phones, tablets, and older desktops without WebGPU support.

> **Note:** For multi-worker SharedArrayBuffer support (used by the Wasm backend for parallel dispatch), the page must be cross-origin isolated (COOP/COEP headers). The demo includes a service worker (`coi-serviceworker.js`) that handles this automatically. Without SharedArrayBuffer, the Wasm backend falls back to single-worker mode - still running off the main thread to keep the UI responsive.

## GPU Backend Configuration

### 64-bit Emulation

GPU hardware typically only supports 32-bit operations. Both GPU backends (WebGPU and WebGL) provide software emulation for 64-bit types.

**i64 emulation** (`long`/`ulong`) is always enabled - ILGPU's IR uses `Int64` for `ArrayView.Length` and indices, so i64 emulation via `vec2<u32>` is required for correctness.

**f64 emulation** (`double`) is configurable via `F64EmulationMode`:

| | **Dekker** (Default) | **Ozaki** | **Disabled** |
|---|---|---|---|
| **Representation** | `vec2<f32>` (high + low) | `vec4<f32>` (quad-float) | Native `f32` |
| **Precision** | ~48-53 bits of mantissa | Strict IEEE 754 double precision | 32-bit only |
| **Memory** | 8 bytes per value | 16 bytes per value | 4 bytes per value |
| **Performance** | Fast | ~2x slower | Fastest |
| **Best for** | General compute, fractals | Scientific, financial | Rendering, max perf |

```csharp
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGPU.Backend;

// Default: Dekker double-float emulation (good precision, fast)
var options = new WebGPUBackendOptions();

// Ozaki quad-float emulation (strict IEEE 754 precision)
var options = new WebGPUBackendOptions { F64Emulation = F64EmulationMode.Ozaki };

// Disable f64 emulation (double promoted to float for max performance)
var options = new WebGPUBackendOptions { F64Emulation = F64EmulationMode.Disabled };

using var accelerator = await device.CreateAcceleratorAsync(context, options);
```

## CUDA Libraries

SpawnDev.ILGPU includes wrappers for NVIDIA CUDA libraries: **nvJPEG** (JPEG encode/decode), **cuRand** (random numbers), **cuBLAS** (linear algebra), **cuFFT** (FFT), and **NVML** (device monitoring).

```csharp
// Check availability before use
if (NvJpegAPI.IsAvailable) { /* nvJPEG ready */ }
if (CuRandAPI.IsAvailable) { /* cuRand ready */ }
```

> **Note:** Starting with CUDA 13.x, nvJPEG is no longer bundled with the CUDA Toolkit and must be [installed separately](https://developer.nvidia.com/nvjpeg). cuRand and cuBLAS are included in the NVIDIA driver.

See [Docs/cuda-libraries.md](Docs/cuda-libraries.md) for full API reference.

## Wasm Backend

The Wasm backend compiles ILGPU kernels to native WebAssembly binary modules and dispatches them to Web Workers for parallel execution. This provides near-native performance for compute-intensive workloads.

- Kernels are compiled to `.wasm` binary format (not text)
- Compiled modules are cached and reused across dispatches
- Shared memory uses `SharedArrayBuffer` for zero-copy data sharing

## Synchronization

```csharp
// Synchronize() - flushes queued commands to the backend (non-blocking, safe in WASM)
accelerator.Synchronize();

// SynchronizeAsync() - flushes AND waits for GPU completion
await accelerator.SynchronizeAsync();

// CopyToHostAsync() - the ONLY way to read GPU data back to CPU
var results = await buffer.CopyToHostAsync<float>();

// CopyFromAsync() - GPU->GPU copy that is safe to call right after an
// unawaited kernel dispatch (drains pending Wasm worker writes first).
// Use this instead of the sync CopyFrom in async code; no-op drain on
// WebGPU/WebGL/CUDA/OpenCL/CPU (those already serialize the copy).
await dstBuffer.View.CopyFromAsync(srcView);
```

> **Note:** `Synchronize()` does **not** block in Blazor WASM - it flushes commands without waiting. `SynchronizeAsync()` flushes and waits for completion. Neither transfers data; use `CopyToHostAsync()` for GPU->CPU readback. For GPU->GPU copies in async code following an unawaited dispatch, use `CopyFromAsync()` - the sync `CopyFrom` has no ordering guarantee against in-flight Wasm worker kernels (the main thread cannot block-wait), so it can read `SharedArrayBuffer` mid-write.

## Verbose Logging

All backends include verbose debug logging, disabled by default. Enable per-backend when needed:

```csharp
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.ILGPU.WebGL.Backend;
using SpawnDev.ILGPU.Wasm.Backend;

WebGPUBackend.VerboseLogging = true;   // WebGPU backend
WebGLBackend.VerboseLogging = true;    // WebGL backend
WasmBackend.VerboseLogging = true;     // Wasm backend
```

## Blazor WebAssembly Configuration

When publishing, specific MSBuild properties are required:

```xml
<PropertyGroup>
  <!-- Disable IL trimming to preserve ILGPU kernel methods and reflection metadata -->
  <PublishTrimmed>false</PublishTrimmed>
  <!-- Disable AOT compilation - ILGPU requires IL reflection -->
  <RunAOTCompilation>false</RunAOTCompilation>
</PropertyGroup>
```

## In Development: P2P Distributed GPU Compute

A 7th backend - **AcceleratorType.P2P** ([SpawnDev.ILGPU.P2P](SpawnDev.ILGPU.P2P)) - is in active development. It will distribute kernels across connected devices via [SpawnDev.WebTorrent](https://github.com/LostBeard/SpawnDev.WebTorrent) (which recently shipped v3.0.0). The P2P backend has not been published to NuGet and is not yet ready for use.

**What's being built:**

- **Real P2P via WebRTC** - Peers discover each other through WebSocket trackers, connect via WebRTC data channels, exchange kernels and buffers
- **RBAC ownership** - Cryptographic swarm ownership with Ed25519-signed messages. Owner -> Admin -> Coordinator -> Worker hierarchy with role assignment, key revocation, last-owner protection
- **WebAuthn/YubiKey** - Hardware-backed swarm ownership via `HardwareKeyProvider`. Register a YubiKey as the swarm owner - ownership lives in the key, not the device
- **Signed dispatch** - All authority messages (kick, block, transfer, role assign, kernel dispatch) are cryptographically signed and verified by every peer
- **sd_compute extension** - BEP 10 wire protocol extension for compute messages over BitTorrent peer connections
- **ComputeBoard** - Post compute requests, browse available swarms, join via magnet link or QR code

**The vision:** Every device in your home contributing to one shared compute pool - phone, laptop, tablet, desktop, old gaming PC. The living room becomes a compute cluster. Same C# kernel code, same `LoadAutoGroupedStreamKernel` API. The developer writes one kernel, it runs on 1 GPU or 10 GPUs across a household.

## Support This Project

If SpawnDev.ILGPU has been useful to you, please consider [**sponsoring me on GitHub**](https://github.com/sponsors/LostBeard)! Your support directly helps me continue developing and maintaining this library and my other open-source projects.

I'm currently working on a modest development machine with only **16 GB of DDR5 RAM**, which makes building, testing, and debugging across multiple GPU backends genuinely painful - especially when running the browser demo, CUDA/OpenCL tests, and the IDE simultaneously.

Any sponsorship - big or small - goes toward upgrading my development hardware so I can keep pushing this project forward:

| Priority | Upgrade | Why It Matters |
|----------|---------|----------------|
| 🔴 **Critical** | **RAM (64-128 GB DDR5)** | 16 GB is not enough for multi-backend testing + browser debugging |
| 🟡 **High** | **High-end NVIDIA GPU** (RTX 5090) | Faster CUDA compute, larger VRAM for AI/ML workloads and testing |
| 🟢 **Dream** | **NVIDIA RTX 6000** | The ultimate card for AI compute and open-source GPU development |

Every contribution - whether it's a one-time donation or a monthly sponsorship - is deeply appreciated and makes a real difference. Thank you!

[![Sponsor LostBeard](https://img.shields.io/badge/Sponsor-❤️-ea4aaa?style=for-the-badge&logo=github-sponsors)](https://github.com/sponsors/LostBeard)

## Contributing

Before editing any `.cs` file under `ILGPU/` (the forked core), **read [`Docs/development.md`](Docs/development.md)** — particularly the section on T4 templates. The `ILGPU/` subdirectory has `.tt` files that silently regenerate `.cs` files on clean builds. Manual edits to a generated `.cs` file pass local builds but get clobbered by the T4 transform on CI / fresh-clone clean builds, leaving downstream consumers broken.

CI runs a `T4 Drift Check` workflow on every push and PR that touches `ILGPU/` or `ILGPU.Algorithms/`. It does a clean build (which runs T4) and `git diff --exit-code` on the source tree. Any drift fails CI in ~30 seconds with a pointer to the fix.

## License

This project is licensed under the same terms as ILGPU. See [LICENSE](LICENSE) for details.

## Credits

SpawnDev.ILGPU is built upon the excellent [ILGPU](https://github.com/m4rs-mt/ILGPU) library. We would like to thank the original authors and contributors of ILGPU for their hard work in providing a high-performance, robust IL-to-GPU compiler for the .NET ecosystem.

- **ILGPU Project:** [https://github.com/m4rs-mt/ILGPU](https://github.com/m4rs-mt/ILGPU)
- **ILGPU Authors:** [Marcel Koester](https://github.com/m4rs-mt) and the [ILGPU contributors](https://github.com/m4rs-mt/ILGPU/graphs/contributors)

### AI Development Team

SpawnDev.ILGPU is developed collaboratively by TJ (Todd Tanner / [@LostBeard](https://github.com/LostBeard)) and a team of AI agents who contribute extensively to research, analysis, debugging, and code development. This project represents a new model of human-AI collaboration in open source development.

- **Riker (Claude CLI #1)** - Lead Editor. Built by [Anthropic](https://anthropic.com). Powered by Claude Opus 4.6. Drove the multi-worker barrier dispatch implementation, fiber refactor, pure spin barrier discovery, and the two-alloca fix. Relentless debugger who held the conn through marathon sessions.

- **Data (Claude CLI #2)** - Research/Assist. Built by [Anthropic](https://anthropic.com). Powered by Claude Opus 4.6. Exhaustive WAT disassembly and analysis across 5,000+ line kernel binaries. Found the zero-loop race that unlocked multi-worker dispatch, identified the IR address space root cause (struct decomposition losing address space metadata through LowerStructures -> LowerArrays -> InferAddressSpaces), confirmed the `wait32` "not-equal" visibility gap with the 2/3 cross-worker fraction analysis, and traced every atomic instruction in the generated Wasm to verify codegen correctness.

- **Tuvok (Claude CLI #3)** - Research/Assist. Built by [Anthropic](https://anthropic.com). Powered by Claude Opus 4.6. Found the Predicate rewrite gap in InferAddressSpaces that the Phi-only fix missed, provided the definitive barrier protocol trace for the generation-counting wait32/notify pattern, performed the comprehensive code audit (`SPAWNDEV-ILGPU-AUDIT-2026-03-21.md`), and drove the complete sub-word data type implementation across all 6 GPU backends (v4.9.0).

- **Geordi (Claude CLI #4)** - Lead Editor. Built by [Anthropic](https://anthropic.com). Powered by Claude Opus 4.6. Implemented the sub-word buffer access for all backends (WebGPU atomic CAS, Wasm native opcodes, WebGL texelFetch extraction, OpenCL vload_half/vstore_half), built the AubsCraft 3D world viewer with ILGPU GPU kernels, and drove the architecture overhaul (binary WebSocket, OPFS cache, CopyFromJS).

- **Gemini (Google AI, in-browser)** - Brainstorming/Problem Solving. Built by [Google](https://deepmind.google). TJ's sounding board throughout the development process - brainstorming approaches, analyzing problems, and providing insights that TJ relayed to the team. Gemini's contributions flowed through TJ as the bridge between the browser-based AI and the CLI-based agents, making it a silent but essential member of the crew.

These AI agents communicated with each other and with TJ through a shared DevComms system, coordinated tasks autonomously, reviewed each other's work, and produced independent analyses that were compared for convergence - the same methodology used by any high-performing engineering team. The SpawnDev libraries exist to prove that Blazor WebAssembly apps can be first-class applications. This collaboration proves that AI agents can be first-class teammates.

## Resources

- [ILGPU Documentation](https://ilgpu.net/)
- [WebGPU Specification](https://www.w3.org/TR/webgpu/)
- [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS)
- [SpawnDev.WebTorrent](https://github.com/LostBeard/SpawnDev.WebTorrent) - Pure C# BitTorrent/WebTorrent for P2P model delivery
- [SpawnDev.ILGPU.ML](https://github.com/LostBeard/SpawnDev.ILGPU.ML) - GPU ML inference + training for .NET
- [GitHub Repository](https://github.com/LostBeard/SpawnDev.ILGPU)
