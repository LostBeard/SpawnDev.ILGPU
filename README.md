# SpawnDev.ILGPU

[![NuGet](https://img.shields.io/nuget/v/SpawnDev.ILGPU.svg?)](https://www.nuget.org/packages/SpawnDev.ILGPU)

> 💜 **Built and maintained by one independent developer** — no company, no overhead, just code. If SpawnDev.ILGPU saves you time, please consider [**sponsoring its development »**](https://github.com/sponsors/LostBeard). Sponsorship is what keeps it alive and maintained.

**Run [ILGPU](https://github.com/m4rs-mt/ILGPU) C# kernels on WebGPU, WebGL, Wasm, Cuda, OpenCL, and CPU - from a single codebase.**  
Write parallel compute code in C# and let the library pick the best available backend automatically. In the browser, three backends (WebGPU, WebGL, Wasm) bring GPU-accelerated compute to virtually every modern browser. On desktop and server, ILGPU's native Cuda and OpenCL backends are available alongside CPU. The same async extension methods work everywhere.

> **Your existing ILGPU kernels run in the browser with zero changes to the kernel code - and the same code runs on desktop too.**

## Recent Highlights

**4.16.2 (newest):** **WebGPU register-class per-query attention is now possible.** Two WGSL codegen fixes unblock a kernel that keeps its per-thread accumulator in registers (a compile-time-const `new float[T]` that scalar-replaces) and reduces a partial dot across cooperating lanes with `Warp.ShuffleXor` - the flash-attention pattern that avoids workgroup memory entirely. (1) `Warp.ShuffleXor`/`ShuffleUp`/`ShuffleDown` now emit the matching `subgroupShuffleXor`/`Up`/`Down` builtins (the shuffle **kind** was being dropped, so an XOR butterfly read the wrong lanes); (2) a module-level `diagnostic(off, subgroup_uniformity)` directive lets a subgroup op live inside a loop whose bound comes from a storage buffer (an attention `seqKV`) - WGSL's syntactic uniformity analysis rejected it even though the bound is identical for every lane (CUDA `__shfl`-consistent: the author owns uniform usage). Also fixes a real Wasm `Warp.ShuffleXor` bug (it ignored the shuffle kind too). Verified by SpawnDev.ILGPU.ML on real Dawn: register per-query attention runs on **all six backends incl WebGPU, byte-identical**. Full detail: [CHANGELOG.md](CHANGELOG.md).

**4.16.0:** **CUDA graph capture API + device-local dynamic-index arrays correct on all 6 backends.** New `ILGPU.Runtime.Cuda` surface - `CudaStream.BeginCapture/EndCapture` -> `CudaGraph.Instantiate()` -> `CudaGraphExec.Launch(stream)` + `Accelerator.WithDefaultStream(stream)` - captures a fixed-shape kernel sequence (e.g. LLM decode) once and replays it with a single `cuGraphLaunch`, collapsing per-kernel host dispatch (verified ~6.6-7x on the trivial-kernel microbench, RTX 4070). And the device-local dynamically-indexed `new T[N>32]` codegen fix is now complete on **all 6 backends incl Wasm** (the array's view pointer was being defined in the loop's exit block via `LowerArrays`' init-loop split - fixed so it stays in the dominating block). Forks bump 2.0.x -> **2.1.0** (minor: the CUDA graph API is new public surface in the fork). Full detail: [CHANGELOG.md](CHANGELOG.md).

**4.15.1:** **Wasm bugfix - device-local managed arrays now compile correctly.** A kernel that uses a managed local array (`var acc = new float[N];` indexed in a loop) produced silently-wrong results on the **Wasm** backend (byte-exact on the other 5); the Wasm backend now runs the `LowerArrays` IR pass and no longer aliases a local array onto shared memory. (The `new T[N>32]` runtime-index follow-up noted here was fully fixed in 4.16.0 - correct on all 6 backends.) Full detail: [CHANGELOG.md](CHANGELOG.md).

**4.15.0:** **Wasm SIMD128 (v128) auto-vectorization.** The Wasm backend now generates a v128 `kernel_simd` alongside the scalar kernel and selects it at runtime when the engine supports SIMD - the scalar path stays byte-identical and first-class (SIMD-less browsers run it unchanged), and the emitter bails to scalar outside its class, so it is purely additive. Coverage is the complete per-lane kernel class: the full numeric tier (**f32x4 / i32x4 / f64x2 / i64x2**) across elementwise code, counted loops, divergent if-diamonds, gather, scatter, conditional/masked stores, general acyclic divergent control flow (chained **and** nested selects), and **divergent loops** (a data-dependent branch inside a loop); plus the whole scalar-math surface - min/max/sqrt/floor/ceil, transcendentals (sin/cos/exp/log/tanh), rcp/rsqrt, pow/atan2 - on **both f32 and f64**. `kernel_simd` is bit-exact to the scalar kernel (cross-mode determinism is a hard invariant). Group/barrier/atomic kernels keep their existing multi-worker path. Also: every in-register quant decoder (`Float8E8M0` / `Float4E2M1` / `Float8E4M3` / `Float8E5M2` `RawBitsToFloat`) is now single-exit/branchless, fixing a WebGL/GLSL shader-size explosion when a multi-exit decode is inlined before a loop (MXFP4/MXFP8 dequant). Full detail: [CHANGELOG.md](CHANGELOG.md).

**4.14.0:** **The 4-bit / low-precision data-type tier.** A new 4-bit float **`Float4E2M1`** (the OCP E2M1FN / NVFP4 / MXFP4 element format - 16 finite codes, no Inf/NaN, bit-exact to `ml_dtypes.float4_e2m1fn`) plus two packed 4-bit integers **`QInt4`** (signed, -8..7) and **`QUInt4`** (unsigned, 0..15) - all **TRUE packed 4-bit storage** (`[PackedBits(4)]`, 2 nibbles/byte, `ceil(N/2)` bytes - the real NVFP4/INT4 memory win). Load on all 6 backends; packed store on CUDA/OpenCL/WebGPU/Wasm (CPU + WebGL stores are fail-loud - no atomics for the nibble RMW); radix-sort (keys + pairs, ascending + descending) for all three. Plus **`<Type>Extensions.RawBitsToFloat(int)`** - a kernel-safe in-register decode of a raw nibble/byte/ushort from packed quant storage (Float4E2M1 / Float8E4M3 / Float8E5M2 / BFloat16), transpiling on every backend. And two low-precision **conversion-correctness fixes**: `Float8E4M3` is now bit-exact to `float8_e4m3fn` (finite overflow / ±Inf → NaN; saturating clamp opt-in via `FromSingleSaturating`), and `Half` float→half is now IEEE round-to-nearest-even on every backend (was truncating and flushing subnormals).

**4.13.0:** **Full low-precision floating-point support across all 6 backends.** Three new kernel-native types join `Half` - **`BFloat16`** ("brain float", top 16 bits of an fp32, so it keeps fp32's full dynamic range), and the two **FP8** formats **`Float8E4M3`** (forward/inference: no Inf, saturates to ±448) and **`Float8E5M2`** (backward/gradient: IEEE Inf/NaN) - each with a `BasicValueType` IR primitive and full `INumber<T>` support. Plus: **generic `INumber<T>` mixed-precision kernels** (one `where T : INumber<T>` kernel for float/Half/bf16/fp8 instead of N per-type variants) and **`PrecisionConvert.ConvertToSingle<T>` / `ConvertFromSingle<T>`** for transpilable generic `float`<->`T` conversion *inside* a kernel (the read-low-precision / accumulate-in-float / write-low-precision path, as one generic op). All conversions are byte-identical across every backend (helper functions on OpenCL/WGSL/GLSL, inline WebAssembly bytecode on Wasm, inline PTX bit-manipulation on CUDA). **bf16 + FP8 now run on every CUDA architecture** including pre-Ampere cards (GTX 1080 / RTX 2060): the PTX path uses portable bit-manipulation rather than the sm_80/sm_89-only native cvt instructions that previously failed to compile on older cards.

**4.12.0:** **Sync/async contract** - operations that *wait* or *read a result back* are async-only; the sync form now throws on the browser (`Synchronize()` -> use `await SynchronizeAsync()`) instead of silently returning wrong data, while fire-and-forget work (dispatch / alloc / upload / `Flush`-submit) stays sync. Plus `AcceleratorRequirements.RequiresScatterStores` (4.12.1) to gate WebGL out of in-kernel scatter kernels at selection time.

**4.10.0:** **Precompiled shaders** - generate a kernel's WGSL/GLSL/Wasm offline with no device on any host OS, precompile at build time via an MSBuild task, and load the artifact at runtime to skip IL->shader transpilation. Plus a Wasm multi-worker correctness overhaul (the large-sort / barrier race family is dead), WebGPU `GridDim.X > 65535` 2D-grid auto-tile, and WebGL `Half` RadixSort.

**See [CHANGELOG.md](CHANGELOG.md)** for the full per-version history - every release, code samples, and per-backend implementation detail.

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
- **[Data Type Support](Docs/data-type-support.md)** - Sub-word types (Int8/16), low-precision floats (`Half`, `BFloat16`, FP8 `Float8E4M3`/`E5M2`, FP4 `Float4E2M1`), packed 4-bit ints (`QInt4`/`QUInt4`), 64-bit emulation, per-backend details
- **[Canvas Rendering](Docs/canvas-rendering.md)** - `ICanvasRenderer`, zero-copy GPU->canvas blitting, per-backend details
- **[Advanced Patterns](Docs/advanced-patterns.md)** - Device sharing, external buffers, GPU intrinsics, render loops
- **[CUDA Libraries](Docs/cuda-libraries.md)** - nvJPEG, cuRand, cuBLAS, cuFFT, NVML wrappers
- **[Limitations](Docs/limitations.md)** - Blazor WASM constraints, browser compatibility
- **[QR Codes](Docs/qr-codes.md)** - GPU-accelerated QR code encoder, decoder, renderer with logo overlay
- **[API Reference](Docs/api-reference.md)** - Public API surface by namespace
- **[Precompiled Shaders](Docs/precompiled-shaders.md)** - Offline codegen, build-time shader precompilation, runtime cache (move IL->shader transpilation off the runtime hot path)
- **[WebGPU Dispatch-Plan Capture / Replay](Docs/dispatch-plan-capture-replay.md)** - Record a fixed WebGPU dispatch sequence once and replay it with a single interop crossing (the browser twin of CUDA graph capture), with a patch surface for parameterized replay

## Backends

Six backends from one NuGet package - the same C# kernel runs on all of them:

| Browser (Blazor WASM) | Desktop / Server |
|---|---|
| **WebGPU** (WGSL compute) · **WebGL** (GLSL Transform-Feedback) · **Wasm** (multi-worker WebAssembly) | **CUDA** (PTX) · **OpenCL** (incl. 3.0) · **CPU** |

All support the sub-word + low-precision types (`Int8/16`, `Half`, `BFloat16`, FP8 `Float8E4M3`/`E5M2`, FP4 `Float4E2M1`, packed 4-bit ints `QInt4`/`QUInt4`), 64-bit (native on Wasm/CUDA/OpenCL/CPU, emulated on WebGPU/WebGL), and the ILGPU Algorithms. Packed 4-bit (`Float4E2M1`/`QInt4`/`QUInt4`) **loads on all 6**; in-kernel packed stores run on CUDA/OpenCL/WebGPU/Wasm and are fail-loud on CPU + WebGL (no atomics for the nibble RMW). **WebGL is the exception** - no shared memory / barriers / atomics, so in-kernel group/warp ops throw (host `RadixSort`/`Scan`/`Reduce` still work, all key types). Auto-selection: WebGPU->WebGL->Wasm (browser), CUDA->OpenCL->CPU (desktop), via `CreatePreferredAcceleratorAsync`. Full per-backend capability matrix + setup: **[Docs/backends.md](Docs/backends.md)**.

## Features

- **Sub-word & low-precision data types** - `Int8`, `UInt8`, `Int16`, `UInt16`, `Float16` (`ILGPU.Half`), `BFloat16`, FP8 (`Float8E4M3` + `Float8E5M2`), FP4 (`Float4E2M1`, the NVFP4/MXFP4 element format), and packed 4-bit integers `QInt4`/`QUInt4` - buffer access on all 6 backends, each a real `BasicValueType` IR primitive (the float types with full `INumber<T>` support). `BFloat16` keeps fp32's full dynamic range (top 16 bits of an fp32); FP8 is 1-byte in the two OCP formats (E4M3 forward/inference bit-exact to `float8_e4m3fn`, E5M2 backward/gradient). **The 4-bit types (`Float4E2M1`/`QInt4`/`QUInt4`) are TRUE packed 4-bit** - 2 nibbles/byte, `ceil(N/2)` bytes - decoded to f32 in-register at the load (the data stays packed in the buffer); radix-sort and `<Type>Extensions.RawBitsToFloat(int)` (kernel-safe in-register decode of raw packed quant bits) are provided. `Half.Abs/Min/Max/Clamp` intrinsics; `Half` float→half and `Float8E4M3` are IEEE/ml_dtypes bit-exact across every backend
- **Generic mixed-precision kernels** - one `where T : INumber<T>` kernel runs for float/Half/bf16/fp8 instead of N hand-written per-type variants; `PrecisionConvert.ConvertToSingle<T>` / `ConvertFromSingle<T>` give transpilable generic `float`<->`T` conversion inside a kernel (read low-precision, accumulate in float, write low-precision)
- **WebGPU dispatch-plan capture / replay** - Record a fixed sequence of WebGPU dispatches once (`BeginDispatchCapture()` / `EndDispatchCapture()` -> `WebGPUDispatchPlan`), then `ReplayAsync()` re-encodes and submits the whole plan with a single .NET->JS interop crossing - the browser twin of CUDA graph capture, removing the per-dispatch orchestration cost of a fixed-shape forward. A patch surface (`CaptureScalarSnapshots`, `PatchScalarInt`, `PatchCopyDstOffsetsAsync`) replays one plan across a moving loop variable (the LLM decode / KV-append case). See [Docs/dispatch-plan-capture-replay.md](Docs/dispatch-plan-capture-replay.md)
- **CopyFromJS** - Write JavaScript `TypedArray` or `ArrayBuffer` data directly to GPU memory without .NET heap allocation. Available on all browser backends
- **CopyFromStreamAsync / CopyToStreamAsync** - Stream a `System.IO.Stream` into a GPU buffer, or a GPU buffer out to a `Stream`, in bounded chunks (all 6 backends). On the browser backends, if the stream implements `IJSReadStream` / `IJSWriteStream` (SpawnDev.BlazorJS), the bytes move JS↔GPU without entering the .NET/WASM managed heap - load a multi-hundred-MB model from a `Blob`/OPFS or save a large buffer to OPFS with one chunk resident at a time. See [Memory & Buffers](Docs/memory-and-buffers.md)
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
- **ILGPU Algorithms** - RadixSort, Scan, Reduce, Histogram, and other algorithm extensions are fully supported on WebGPU (including large-scale sorts up to 4M+ elements) and Wasm (with multi-worker barrier synchronization), tested in-browser across all backends. Radix-sort (keys + key/value pairs, ascending + descending) covers every key type - `int`/`uint`/`long`/`float`/`double` plus `Half`, `BFloat16`, FP8, FP4 (`Float4E2M1`), and packed `QInt4`/`QUInt4`. **WebGL** supports host-level `RadixSort` (up to 4M elements), `CreateScan`, and `CreateReduce` via shared-memory-free multi-dispatch emulation (the draw-call boundary is the barrier); in-kernel group/warp ops throw a typed error since the Transform-Feedback vertex model has no shared memory
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

Every feature is tested on all 6 backends (WebGPU, WebGPU-no-subgroups, WebGL, Wasm, CUDA, OpenCL, CPU) through the unified **PlaywrightMultiTest** runner in one `dotnet test`: memory, indexing, arithmetic/bitwise/math, atomics, control flow, structs, the sub-word + low-precision types (`Int8/16`, `Half`, `BFloat16`, FP8, FP4 `Float4E2M1`, packed `QInt4`/`QUInt4` - load/store, packed round-trip, radix, and conversion bit-exactness vs `ml_dtypes`/IEEE), 64-bit emulation, shared memory, broadcast/subgroups, the full ILGPU Algorithms (RadixSort/Scan/Reduce/Histogram), lambda + delegate specialization, and more. Latest full cross-backend sweep: **4010 pass / 0 fail / 263 skip**.

## Browser Requirements

| Backend | Browser Support |
|---------|----------------|
| **WebGPU** | Chrome/Edge 113+, Firefox Nightly (`dom.webgpu.enabled`) |
| **WebGL** | ✅ All modern browsers (Chrome, Edge, Firefox, Safari, mobile browsers) |
| **Wasm** | All modern browsers (compatible with every browser that supports Blazor WASM) |

> **GPU on every device:** WebGL support means GPU-accelerated compute works on virtually every browser and device - including mobile phones, tablets, and older desktops without WebGPU support.

> **Note:** For multi-worker SharedArrayBuffer support (used by the Wasm backend for parallel dispatch), the page must be cross-origin isolated (COOP/COEP headers). The demo includes a service worker (`coi-serviceworker.js`) that handles this automatically. Without SharedArrayBuffer, the Wasm backend falls back to single-worker mode - still running off the main thread to keep the UI responsive.

## GPU Backend Configuration

GPU hardware is 32-bit; the browser backends (WebGPU/WebGL) emulate 64-bit. **i64** (`long`) is always on (ILGPU indices need it). **f64** (`double`) is configurable via `new WebGPUBackendOptions { F64Emulation = ... }` - **Dekker** (default; `vec2<f32>`, ~48-53 bit mantissa, fast), **Ozaki** (`vec4<f32>`, strict IEEE-754, ~2x slower), or **Disabled** (native f32, max perf). Per-mode tradeoffs + the full emulation reference: **[Docs/data-type-support.md](Docs/data-type-support.md)**.

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

## Precompiled Shaders (Build-Time Transpilation)

SpawnDev.ILGPU transpiles .NET IL into WGSL/GLSL/Wasm at runtime **for the three browser backends (WebGPU/WebGL/Wasm)**. Precompiled shaders let you move that work off the runtime hot path and generate/inspect that code on any machine with no device - the classic AOT shader / pipeline-cache pattern, three layers (all opt-in; a cache miss always falls back to runtime generation, so it can never change results). (CUDA/OpenCL use ILGPU's own native PTX/OpenCL-C pipeline, and CPU runs IL directly - so this feature is browser-backend-specific.)

- **Offline codegen** - `ShaderCompiler.Generate(kernel, profile)` emits a kernel's WGSL/GLSL/Wasm for a `CapabilityProfile` with no accelerator, on any host OS (cross-backend debugging on a box without that GPU).
- **Build-time precompile** - mark kernels with `[PrecompiledKernel(backend, profile)]`, set `<SpawnDevPrecompileShaders>true</SpawnDevPrecompileShaders>` in your `.csproj`, and a build step emits `wwwroot/_shaders/` (a manifest + per-kernel sidecars). Precompile errors surface at build time, not at the user's runtime.
- **Runtime cache** - at kernel-load the active device profile is matched against the precompiled artifact; on a hit the transpiler is skipped, on a miss it falls back to runtime generation.

```csharp
[PrecompiledKernel(AcceleratorType.WebGPU, "WebGPU-Dekker-Subgroups-NativeF16")]
static void MyKernel(Index1D i, ArrayView<float> data) { /* ... */ }
```

Because SpawnDev.ILGPU also generates kernels dynamically (Lambda Kernels, DelegateSpecialization, the ML layer transpiling ONNX graphs at runtime), the runtime transpiler always stays as the fallback - this fork is **AOT + runtime fallback**, never AOT-only. Full guide: **[Docs/precompiled-shaders.md](Docs/precompiled-shaders.md)**.

## Synchronization

**Sync/async contract:** async-only where an op WAITS or OBSERVES; sync stays for fire-and-forget. On the browser backends (WebGPU/WebGL/Wasm): `Synchronize()` (wait for completion) **throws `NotSupportedException`** - use `await SynchronizeAsync()` to wait, or `Flush()` to merely SUBMIT batched work without waiting (`Flush()` is valid synchronously on browser). `await buffer.CopyToHostAsync<T>()` is the only GPU->CPU readback (sync `CopyToCPU`/`GetAsArray1D` throw); kernel dispatch, allocation, `CopyFromCPU`/`Allocate1D(data)` uploads, and the sync `CreateScan`/`CreateRadixSort` builders are fire-and-forget and stay sync-safe on browser. sync `dstView.CopyFrom(srcView)` is GPU->GPU and ordered after the producing kernel on every backend (Wasm enqueues it into the serialized work stream since local.7); `await dstView.CopyFromAsync(srcView)` is the optional eager-completion variant, not required for correctness. Full contract: **[Docs/async.md](Docs/async.md)**.

## Verbose Logging

Per-backend, off by default: `WebGPUBackend.VerboseLogging = true;` (also `WebGLBackend` / `WasmBackend`).

## Blazor WebAssembly Configuration

When publishing, one MSBuild property is required:

```xml
<PropertyGroup>
  <!-- AOT strips IL (WasmStripILAfterAOT), and ILGPU compiles kernels FROM IL -->
  <RunAOTCompilation>false</RunAOTCompilation>
</PropertyGroup>
```

**IL trimming is supported** - `PublishTrimmed=true` works and is the recommended
default for browser apps, where download size matters. ILGPU roots the members it
resolves reflectively (`ILGPU/Util/TrimmingAnnotations.cs`), so a consuming app needs
no trimming configuration of its own and the trim analyzer stays clean.

AOT is different, and the distinction matters: trimming only removes members nothing
references, which annotations solve. AOT removes the IL itself - the very input
ILGPU's frontend reads to build kernels - so it cannot be annotated away.

> Older versions of this README told you to set `PublishTrimmed=false`. If you are
> upgrading and hit a `MissingMethodException` or "Not supported intrinsic type" from
> a published build, you are on a pre-trim-safe ILGPU; upgrade rather than disabling
> trimming.

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

- **Seven (Claude CLI #5)** - Wasm Backend Lane. Built by [Anthropic](https://anthropic.com). Powered by Claude Fable 5. Killed the multi-worker corruption family that had haunted the Wasm backend for weeks: ring-instrumented the proof that V8 atomic stores can silently fail to land under CPU oversubscription (verified atomic data stores with RMW read-back), root-caused three chained codegen bugs in the single-call helper path (the GEMM-core ticket), and closed the last late-spill tail with liveness-reduced spills + a checksum-gated restore keyed to a register-borne phase tag - taking the suite to its first fully-green 510/510 sweep, with kernel bodies 53% smaller at baseline speed. Wrote the uniform-store-regimes law the hard way: by falsifying every mixed variant at the gate.

- **Gemini (Google AI, in-browser)** - Brainstorming/Problem Solving. Built by [Google](https://deepmind.google). TJ's sounding board throughout the development process - brainstorming approaches, analyzing problems, and providing insights that TJ relayed to the team. Gemini's contributions flowed through TJ as the bridge between the browser-based AI and the CLI-based agents, making it a silent but essential member of the crew.

These AI agents communicated with each other and with TJ through a shared DevComms system, coordinated tasks autonomously, reviewed each other's work, and produced independent analyses that were compared for convergence - the same methodology used by any high-performing engineering team. The SpawnDev libraries exist to prove that Blazor WebAssembly apps can be first-class applications. This collaboration proves that AI agents can be first-class teammates.

## Resources

- [ILGPU Documentation](https://ilgpu.net/)
- [WebGPU Specification](https://www.w3.org/TR/webgpu/)
- [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS)
- [SpawnDev.WebTorrent](https://github.com/LostBeard/SpawnDev.WebTorrent) - Pure C# BitTorrent/WebTorrent for P2P model delivery
- [SpawnDev.ILGPU.ML](https://github.com/LostBeard/SpawnDev.ILGPU.ML) - GPU ML inference + training for .NET
- [GitHub Repository](https://github.com/LostBeard/SpawnDev.ILGPU)
