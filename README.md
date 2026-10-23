<div align="center">

# SpawnDev.ILGPU

**GPU compute in C#, in the browser or on the desktop.**

Write [ILGPU](https://github.com/m4rs-mt/ILGPU) kernels once. They run as WGSL, GLSL ES, WebAssembly, PTX, OpenCL C or SIMD CPU code.
No JavaScript to write, no shaders to write, no native binaries to ship.

[![NuGet](https://img.shields.io/nuget/v/SpawnDev.ILGPU.svg?label=nuget&logo=nuget)](https://www.nuget.org/packages/SpawnDev.ILGPU)
[![Downloads](https://img.shields.io/nuget/dt/SpawnDev.ILGPU.svg?label=downloads)](https://www.nuget.org/packages/SpawnDev.ILGPU)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE.txt)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4.svg)](https://dotnet.microsoft.com/)
[![Live demo](https://img.shields.io/badge/live%20demo-GitHub%20Pages-2ea44f.svg)](https://lostbeard.github.io/SpawnDev.ILGPU/)
[![Sponsor](https://img.shields.io/badge/sponsor-LostBeard-ea4aaa.svg?logo=githubsponsors)](https://github.com/sponsors/LostBeard)

[**Live demo**](https://lostbeard.github.io/SpawnDev.ILGPU/) | [Docs](Docs/) | [Examples](Examples/README.md) | [Changelog](CHANGELOG.md)

<a href="https://lostbeard.github.io/SpawnDev.ILGPU/fractals"><img src="https://raw.githubusercontent.com/LostBeard/SpawnDev.ILGPU/master/SpawnDev.ILGPU.Demo/wwwroot/screenshots/spawndev-ilgpu-fractal-explorer-3.jpg" alt="The Fractal Explorer demo, computed by a C# kernel on the visitor's GPU" width="720"></a>

<sub>The Fractal Explorer from the live demo. The pixels come from a C# kernel running on the visitor's own GPU, in the browser, with double-precision zoom.</sub>

</div>

---

## What it is

SpawnDev.ILGPU extends [ILGPU](https://github.com/m4rs-mt/ILGPU), the .NET compiler that turns C# methods into GPU code, with three backends for the browser (**WebGPU**, **WebGL** and **WebAssembly**) and a SIMD CPU backend for the desktop (**Velocity**). A kernel you wrote for CUDA or OpenCL runs unchanged in a Blazor WebAssembly app, and the same kernel runs on the desktop. Your application picks the best backend that is available at run time.

The library ships as one NuGet package, `SpawnDev.ILGPU`. It brings along the forked ILGPU compiler (`SpawnDev.ILGPU.Fork`, `SpawnDev.ILGPU.Algorithms.Fork`) and [SpawnDev.SpawnJS](https://github.com/LostBeard/SpawnDev.SpawnJS) for browser interop.

## Highlights

- **One kernel, every backend.** WebGPU, WebGL and Wasm in the browser; CUDA, OpenCL, Velocity (SIMD CPU) and the plain CPU accelerator on the desktop. The shared test suite runs on all of them.
- **Existing ILGPU code runs as it is.** `ArrayView`, `Index1D/2D/3D`, shared memory, atomics, `Group.Barrier()`, lambdas and the ILGPU Algorithms (radix sort, scan, reduce, histogram) work on the browser backends.
- **One async API for browser and desktop.** `SynchronizeAsync()`, `CopyToHostAsync()` and `CopyFromAsync()` work everywhere. The browser backends require async, and the desktop backends accept it.
- **Automatic backend selection.** `CreatePreferredAcceleratorAsync()` picks WebGPU, then WebGL, then Wasm in the browser, and the discrete GPU before the CPU on the desktop.
- **Sub-word and low-precision types.** `Int8/16`, `Half`, `BFloat16`, FP8 (`Float8E4M3`, `Float8E5M2`), FP4 (`Float4E2M1`) and packed 4-bit integers (`QInt4`, `QUInt4`) are real IR primitives, with buffer access on every backend.
- **Zero-copy browser I/O.** Write a JavaScript `TypedArray` straight into a GPU buffer, stream a `Blob` or an OPFS file into a buffer in bounded chunks, present a buffer on a `<canvas>`, or copy a `<video>` frame into a buffer, all without the bytes entering the .NET heap.
- **WebGPU performance tools.** Batched dispatch submission, dispatch-plan capture and replay, device-memory budgeting, and shader precompilation with a persisted cache.
- **Trim and AOT safe.** The trim analyzer runs on every build of the libraries. Blazor WebAssembly AOT is supported when the IL is kept.
- **No native dependencies.** The whole library is C#.

## Try it

The [live demo](https://lostbeard.github.io/SpawnDev.ILGPU/) is a Blazor WebAssembly app. Everything runs on your own GPU or CPU, and nothing is sent to a server.

| Demo | What it does |
|------|--------------|
| [Fractal Explorer](https://lostbeard.github.io/SpawnDev.ILGPU/fractals) | Interactive Mandelbrot and other fractals with double-precision zoom |
| [3D Raymarching](https://lostbeard.github.io/SpawnDev.ILGPU/compute3d) | Real-time raymarched scenes computed by a kernel |
| [GPU Boids](https://lostbeard.github.io/SpawnDev.ILGPU/boids3d) | 3D flocking simulation with the physics on the GPU |
| [Game of Life](https://lostbeard.github.io/SpawnDev.ILGPU/gameoflife) | Conway's Game of Life as a GPU cellular automaton |
| [Benchmarks](https://lostbeard.github.io/SpawnDev.ILGPU/benchmarks) | The same workloads timed on every backend your browser offers |
| [Unit tests](https://lostbeard.github.io/SpawnDev.ILGPU/tests) | The shared test suite, runnable in your browser against every available backend |

A [WPF demo](SpawnDev.ILGPU.WpfDemo) (Fractal Explorer, 3D Raymarching, GPU Boids and Benchmarks) runs the same shared kernels on the desktop backends.

<p align="center">
<a href="https://lostbeard.github.io/SpawnDev.ILGPU/benchmarks"><img src="https://raw.githubusercontent.com/LostBeard/SpawnDev.ILGPU/master/SpawnDev.ILGPU.Demo/wwwroot/screenshots/benchmark-browser-4.jpg" alt="The Benchmarks page running in a browser" width="420"></a>
<a href="https://lostbeard.github.io/SpawnDev.ILGPU/benchmarks"><img src="https://raw.githubusercontent.com/LostBeard/SpawnDev.ILGPU/master/SpawnDev.ILGPU.Demo/wwwroot/screenshots/benchmark-desktop-4.jpg" alt="The Benchmarks page running in the desktop demo" width="420"></a>
</p>

## Install

```bash
dotnet add package SpawnDev.ILGPU
```

Requires **.NET 10**. For the browser, use a Blazor WebAssembly project and add the host package that starts the SpawnJS runtime:

```bash
dotnet add package SpawnDev.SpawnJS.Blazor
```

Desktop and server projects (console, WPF, ASP.NET) need nothing else.

## Quick start

### Blazor WebAssembly

Register the SpawnJS runtime and start the app with `SpawnJSRunAsync()` instead of `RunAsync()`:

```csharp
using SpawnDev.SpawnJS;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSpawnJSRuntime();

await builder.Build().SpawnJSRunAsync();
```

Then create the best accelerator and run a kernel. The code is identical on the desktop:

```csharp
using global::ILGPU;
using global::ILGPU.Runtime;
using SpawnDev.ILGPU;

// Registers every backend the environment offers:
// browser = WebGPU, WebGL, Wasm; desktop = CUDA, OpenCL, Velocity
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());

// Browser: WebGPU, then WebGL, then Wasm.
// Desktop: CUDA, then a discrete OpenCL GPU, then other OpenCL devices, then Velocity.
using var accelerator = await context.CreatePreferredAcceleratorAsync();

int length = 256;
using var a = accelerator.Allocate1D(Enumerable.Range(0, length).Select(i => (float)i).ToArray());
using var b = accelerator.Allocate1D(Enumerable.Range(0, length).Select(i => (float)i * 2f).ToArray());
using var c = accelerator.Allocate1D<float>(length);

var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAdd);
kernel((Index1D)length, a.View, b.View, c.View);

await accelerator.SynchronizeAsync();               // wait for the GPU
float[] results = await c.CopyToHostAsync<float>(); // the one way to read results back in the browser

static void VectorAdd(Index1D i, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c) => c[i] = a[i] + b[i];
```

### Desktop and server

The same code works in a console, WPF or ASP.NET app: `AllAcceleratorsAsync()` registers the desktop backends instead, and `SynchronizeAsync()` and `CopyToHostAsync()` fall back to the synchronous ILGPU calls. A complete console project is [Example 01](Examples/01-HelloKernel).

### Choosing a backend yourself

```csharp
// WebGPU: compute shaders, WGSL
using var context = await Context.CreateAsync(builder => builder.WebGPU());
using var accelerator = await context.GetWebGPUDevices()[0].CreateAcceleratorAsync(context);
```

```csharp
// WebGL: GLSL ES 3.0 with Transform Feedback, available on almost every browser
using var context = await Context.CreateAsync(builder => builder.WebGL());
using var accelerator = await context.GetWebGLDevices()[0].CreateAcceleratorAsync(context);
```

```csharp
// Wasm: WebAssembly modules run by Web Workers
using SpawnDev.ILGPU.Wasm;

using var context = await Context.CreateAsync(builder => builder.Wasm());
using var accelerator = await context.GetDevices<WasmILGPUDevice>()[0].CreateAcceleratorAsync(context);
```

```csharp
// Velocity: the SIMD CPU backend (desktop and server)
using ILGPU.Runtime.Velocity;

using var context = Context.Create(builder => builder.Velocity(VelocityDeviceType.Vector128).EnableAlgorithms());
using var accelerator = context.CreateVelocityAccelerator(0);
```

### Why async?

The browser runs .NET on a single thread, so a blocking wait would deadlock the page. On the browser backends `Synchronize()` throws `NotSupportedException`; use `await SynchronizeAsync()` to wait, or `Flush()` to submit batched work without waiting. `await buffer.CopyToHostAsync<T>()` is the only GPU-to-CPU read. Dispatch, allocation and uploads are fire-and-forget and stay synchronous. The full contract is in [Docs/async.md](Docs/async.md).

## Backends

| Backend | Runs on | Compiles C# kernels to | Where |
|---------|---------|------------------------|-------|
| **WebGPU** | GPU | WGSL compute shaders | Browser |
| **WebGL** | GPU | GLSL ES 3.0 vertex shaders with Transform Feedback | Browser |
| **Wasm** | Web Workers | WebAssembly binary, with a SIMD128 variant when the engine has it | Browser |
| **CUDA** | NVIDIA GPU | PTX | Desktop |
| **OpenCL** | Any OpenCL GPU (2.0 and 3.0) | OpenCL C | Desktop |
| **Velocity** | CPU cores | Vectorized IL over `Vector128<T>` (`Vector256` and `Vector512` are opt-in) | Desktop |
| **CPU** | CPU cores | Runs the kernel's own IL one thread at a time; the debugging backend | Desktop |

`AllAccelerators()` and `AllAcceleratorsAsync()` register Velocity, not the plain CPU accelerator. Register the plain one with `builder.DefaultCPU()` when you want to debug a kernel. 32-bit processes keep the plain CPU accelerator, and the browser registers no CPU accelerator at all.

| | WebGPU | WebGL | Wasm | CUDA | OpenCL | Velocity |
|---|:---:|:---:|:---:|:---:|:---:|:---:|
| Shared memory and barriers | yes | no | yes | yes | yes | yes |
| Atomics | yes | no | yes | yes | yes | yes |
| ILGPU Algorithms | yes | host-level only | yes | yes | yes | yes |
| Subgroups, `Warp.*` | with the `subgroups` feature | no | emulated | yes | device-dependent | yes |
| 64-bit `long` and `double` | emulated | emulated | native | native | native | native |
| `Half` | native with `shader-f16`, else emulated | emulated | emulated | native | native with `cl_khr_fp16`, else emulated | yes |
| Packed 4-bit stores | yes | load only | yes | yes | yes | yes |

WebGL has no shared memory, barriers or atomics, so in-kernel group and warp operations throw a typed `UnsupportedKernelFeatureException`. Host-level `RadixSort`, `CreateScan` and `CreateReduction` still work on it. Declare what a kernel needs with `AcceleratorRequirements` and selection skips backends that cannot run it. The complete matrix, with the notes behind each cell, is in [Docs/backends.md](Docs/backends.md).

### Browser requirements

| Backend | Browsers |
|---------|----------|
| **WebGPU** | Chrome and Edge 113 and later (Windows, macOS, ChromeOS), Chrome 121 and later on Android 12 and later, Firefox 141 and later on Windows (macOS on Apple Silicon from 145), Safari 26 and later. Linux support is still rolling out. Current status: [web.dev](https://web.dev/blog/webgpu-supported-major-browsers) and the [gpuweb implementation status](https://github.com/gpuweb/gpuweb/wiki/Implementation-Status) |
| **WebGL** | Every modern browser, including phones and tablets |
| **Wasm** | Every browser that runs Blazor WebAssembly |

The Wasm backend uses all cores through a `SharedArrayBuffer`, which needs a cross-origin-isolated page (COOP and COEP headers). The demo includes a service worker (`coi-serviceworker.js`) that provides that. Without it the backend falls back to one off-thread worker.

## What is in the box

| Area | What you get |
|------|--------------|
| **Data types** | `Int8`, `UInt8`, `Int16`, `UInt16`, `Half`, `BFloat16`, FP8 (`Float8E4M3`, `Float8E5M2`), FP4 (`Float4E2M1`), packed `QInt4` and `QUInt4`. The 4-bit types are truly packed (two per byte) and decode to `float` in-register at the load. `Half` conversion and `Float8E4M3` are bit-exact against IEEE and `ml_dtypes` on every backend. [Docs/data-type-support.md](Docs/data-type-support.md) |
| **Generic kernels** | One `where T : INumber<T>` kernel serves `float`, `Half`, `BFloat16` and FP8. `PrecisionConvert.ConvertToSingle<T>` and `ConvertFromSingle<T>` convert inside a kernel, so you can read low precision, accumulate in `float` and write low precision. |
| **Lambda and higher-order kernels** | Kernels written as capturing lambdas pass captured scalars at dispatch. `DelegateSpecialization<Func<T,R>>` passes an operation as a parameter that is resolved and inlined at compile time. |
| **ILGPU Algorithms** | `RadixSort` (keys and key-value pairs, ascending and descending, every key type including the low-precision ones), `Scan`, `Reduce`, `Histogram`. |
| **Memory and I/O** | `CopyFromJS` (a `TypedArray` or `ArrayBuffer` straight into a buffer), `CopyFromStreamAsync` and `CopyToStreamAsync` (bounded chunks, and with `IJSReadStream` or `IJSWriteStream` the bytes never enter the .NET heap), and a device-memory budget: holders of idle memory register an `IDeviceMemoryReclaimer` and allocations reclaim before the driver spills to system RAM. [Docs/memory-and-buffers.md](Docs/memory-and-buffers.md) |
| **Rendering and image input** | `ICanvasRenderer` presents a pixel buffer on a `<canvas>` without a CPU readback, and `IExternalImageCopier` copies a `<video>`, `<img>`, canvas, `ImageBitmap` or `VideoFrame` into a GPU buffer (entirely on the GPU on WebGPU). [Docs/canvas-rendering.md](Docs/canvas-rendering.md) |
| **WebGPU dispatch tools** | Batched submission (one .NET to JS crossing per submit, and bind-group reuse), plan capture and replay (`BeginDispatchCapture`, `EndDispatchCapture`, `WebGPUDispatchPlan.ReplayAsync`, the browser twin of CUDA graphs), extension auto-detection (`shader-f16`, `subgroups`, `timestamp-query`), and `device.lost` handling. [Docs/dispatch-plan-capture-replay.md](Docs/dispatch-plan-capture-replay.md) |
| **Shaders ahead of time** | `ShaderCompiler.Generate` produces a kernel's WGSL, GLSL or Wasm for a `CapabilityProfile` on any machine, `[PrecompiledKernel]` plus `<SpawnDevPrecompileShaders>true</SpawnDevPrecompileShaders>` precompiles at build time, and `ShaderArtifactSerializer.ExportCache()` and `ImportCache(json)` carry a session's compiled WebGPU shaders into the next one. A cache miss always falls back to runtime generation. [Docs/precompiled-shaders.md](Docs/precompiled-shaders.md) |
| **Velocity** | Shared memory, barriers, atomics, warp shuffles, 1D to 3D grids and groups of up to 256 threads, the Algorithms, and the whole low-precision type tier. [Docs/velocity.md](Docs/velocity.md) |
| **CUDA libraries** | Wrappers for nvJPEG, cuRand, cuBLAS, cuFFT and NVML. Check `NvJpegAPI.IsAvailable` and `CuRandAPI.IsAvailable` before use. [Docs/cuda-libraries.md](Docs/cuda-libraries.md) |
| **QR codes** | A dependency-free QR encoder, decoder and GPU renderer with logo overlay. [Docs/qr-codes.md](Docs/qr-codes.md) |
| **Utilities** | `GpuMatrix4x4` (a .NET `Matrix4x4` that transposes itself for the GPU, with `TransformPoint` and `TransformDirection` inside kernels), `WebGPUBufferAccounting`, and `WebGPUBackend.VerboseLogging` (also on `WebGLBackend` and `WasmBackend`, off by default). |

On WebGPU and WebGL, `long` is always emulated as two 32-bit words. `double` is configurable through `WebGPUBackendOptions.F64Emulation`: **Dekker** (the default, `vec2<f32>`, about 48 to 53 bits of mantissa), **Ozaki** (`vec4<f32>`, strict IEEE-754, about 2x slower) or **Disabled** (plain `float`, fastest).

## How it works

```
  C# kernel (ILGPU IR)
        |
        v
  SpawnDev.ILGPU backend --- WebGPU ---> WGSL           --> browser GPU
                         |-- WebGL  ---> GLSL ES 3.0    --> browser GPU (Transform Feedback)
                         |-- Wasm   ---> WebAssembly    --> Web Workers (SharedArrayBuffer)
  ILGPU backends         |-- CUDA   ---> PTX            --> NVIDIA GPU
                         |-- OpenCL ---> OpenCL C       --> any OpenCL GPU
                         '-- Velocity -> vectorized IL  --> CPU cores (SIMD)
```

The browser code generators are ordinary IR consumers, so they inherit ILGPU's optimization pipeline and add their own passes, for example inlining of helpers that take addresses, removal of debug assertions the target cannot run, and loop-structure recovery for WGSL and GLSL. Kernels are compiled from IL at run time, which is why Blazor AOT needs the IL kept (see below). Offline code generation and the persisted shader cache move that work off the hot path when it matters.

## Performance

Measured numbers, each with the document or release that holds the method. Hardware is noted per row.

| What | Result | Source |
|------|--------|--------|
| WebGPU host time per dispatch, batched submission | 433 to 41 us (RTX 4070, an uncaptured depth-model forward in SpawnDev.ILGPU.ML) | [CHANGELOG 5.2.25](CHANGELOG.md) |
| WebGPU host time per unbatched dispatch after the move to SpawnJS 3 | 251 to 80 us | [CHANGELOG 5.3.0](CHANGELOG.md) |
| Cold first launch of a large WebGPU pipeline (the TRELLIS.2 GEMM shader) | 6.6-7.1 s to 1.25 s, WGSL 592 KB to 331 KB | [CHANGELOG 5.4.1](CHANGELOG.md) |
| Per-frame host cost of a depth model in a production app, without and with Blazor AOT (IL kept) | 43.8 to 21.7 ms | [Docs/limitations.md](Docs/limitations.md) |
| Velocity against the plain CPU backend, LayerNorm 1369 x 384 | 411 ms to 0.69 ms (12-processor PC) | [Docs/velocity.md](Docs/velocity.md) |
| Velocity against the plain CPU backend, MatMul 512 x 512 x 512 | 93.6 ms to 42.6 ms | [Docs/velocity.md](Docs/velocity.md) |
| Velocity against the plain CPU backend, an at-scale inclusive scan | 131 s to 1 s | [Docs/velocity.md](Docs/velocity.md) |
| Velocity vector widths, an arithmetic-bound kernel (AVX-512 PC) | 38 / 22 / 18 ms for 128 / 256 / 512 bits | [CHANGELOG 5.4.0](CHANGELOG.md) |

Correctness comes before speed. Floating-point conversions are checked bit for bit against IEEE and `ml_dtypes`, kernels against a CPU oracle on every backend, and a change to a code generator is screened against the whole suite before it ships.

## Blazor WebAssembly: trimming and AOT

Both work. The only requirement is to keep the IL when you AOT compile, because ILGPU compiles kernels from IL at run time:

```xml
<PropertyGroup>
  <!-- Trimming: supported, and the default for a Blazor Release publish. Nothing to configure. -->
  <PublishTrimmed>true</PublishTrimmed>

  <!-- AOT: supported, but keep the IL. .NET strips it after AOT by default. -->
  <RunAOTCompilation>true</RunAOTCompilation>
  <WasmStripILAfterAOT>false</WasmStripILAfterAOT>
</PropertyGroup>
```

- **Trimming is enforced.** ILGPU, ILGPU.Algorithms and SpawnDev.ILGPU are `IsTrimmable`, and every trim analyzer (IL2xxx) warning is a build error in them, so a new reflection hole fails the library's build instead of an app's. Members ILGPU resolves by name are rooted in the libraries; a consuming app needs no trimming configuration. The test suites run against trimmed publishes.
- **AOT with the IL kept** was verified in a production app, where it halved the per-frame host cost (43.8 to 21.7 ms for a depth model). It costs a much larger `dotnet.native.wasm` and a long build.
- **Faster AOT builds while iterating:** `dotnet publish -c Release -p:WasmDedup=false` compiles each assembly's generic instantiations in parallel. On a project with ILGPU and ILGPU.ML a clean build dropped from 65-80 minutes to 4.5-5.3 minutes, at the cost of a download about 53% larger. Publish the shipped build with the default.
- **Kernels with 6 or more parameters** work under AOT from 5.3.7 on. Prefer the stream loaders (`LoadStreamKernel`, `LoadAutoGroupedStreamKernel`) for them.
- **Desktop NativeAOT (`PublishAot`) is not supported**, because ILGPU emits kernel launchers with Reflection.Emit.

More constraints and workarounds are in [Docs/limitations.md](Docs/limitations.md).

## Examples

Self-contained console projects under [`Examples/`](Examples/README.md). Each references `SpawnDev.ILGPU` from NuGet, so you can copy a folder out and run `dotnet run`.

| # | Example | Shows |
|---|---------|-------|
| 01 | [HelloKernel](Examples/01-HelloKernel) | The smallest kernel: add two arrays, from context to read-back |
| 02 | [BackendSelection](Examples/02-BackendSelection) | `CreatePreferredAccelerator`, device enumeration, capability gating with `AcceleratorRequirements` |
| 03 | [Algorithms](Examples/03-Algorithms) | `RadixSort`, `Scan` and `Reduce`, the same call on every backend |
| 04 | [LambdaAndDelegateSpec](Examples/04-LambdaAndDelegateSpec) | Capturing lambda kernels and `DelegateSpecialization` |

## Documentation

| | |
|---|---|
| [Getting started](Docs/getting-started.md) | Installation, setup, your first kernel |
| [Backends](Docs/backends.md) | Setup and the full capability matrix for each backend |
| [Writing kernels](Docs/kernels.md) | Kernel rules, index types, math functions, shared memory |
| [Memory and buffers](Docs/memory-and-buffers.md) | Allocation, async readback, streaming, the device-memory budget |
| [Async API](Docs/async.md) | The sync and async contract |
| [Data type support](Docs/data-type-support.md) | Sub-word and low-precision types, 64-bit emulation, per-backend details |
| [Capabilities and backend selection](Docs/capabilities-and-backend-selection.md) | `AcceleratorRequirements`, device ranking, `UnsupportedKernelFeatureException` |
| [Velocity](Docs/velocity.md) | The SIMD CPU backend: what it supports, limits, measured speedups |
| [Wasm SIMD128](Docs/wasm-simd.md) | Auto-vectorization in the Wasm backend |
| [Canvas rendering and image input](Docs/canvas-rendering.md) | `ICanvasRenderer` and `IExternalImageCopier` |
| [Dispatch-plan capture and replay](Docs/dispatch-plan-capture-replay.md) | Record a WebGPU dispatch sequence once, replay it with one crossing |
| [Precompiled shaders](Docs/precompiled-shaders.md) | Offline code generation, build-time precompilation, the runtime cache |
| [Advanced patterns](Docs/advanced-patterns.md) | Device sharing, external buffers, GPU intrinsics, render loops |
| [CUDA libraries](Docs/cuda-libraries.md) | nvJPEG, cuRand, cuBLAS, cuFFT, NVML |
| [QR codes](Docs/qr-codes.md) | GPU QR encoder, decoder and renderer |
| [Limitations](Docs/limitations.md) | Blazor WebAssembly constraints and browser compatibility |
| [API reference](Docs/api-reference.md) | The public API by namespace |
| [Development notes](Docs/development.md) | Traps and conventions for contributors, including the T4 templates |
| [Changelog](CHANGELOG.md) | Every release, in detail |

## Repository layout

| Path | Contents |
|------|----------|
| `ILGPU/`, `ILGPU.Algorithms/` | The forked ILGPU compiler and Algorithms (NuGet packages `SpawnDev.ILGPU.Fork` and `SpawnDev.ILGPU.Algorithms.Fork`) |
| `SpawnDev.ILGPU/` | The library: the WebGPU, WebGL and Wasm backends, dispatch, buffers, rendering, QR codes, CUDA library wrappers |
| `SpawnDev.ILGPU.Precompiler/` | The build-time shader precompiler that ships inside the package |
| `SpawnDev.ILGPU.Demo/` | The Blazor WebAssembly demo |
| `SpawnDev.ILGPU.Demo.Shared/` | Shared demo code and the shared test suite |
| `SpawnDev.ILGPU.DemoConsole/` | Desktop host for the shared test suite |
| `SpawnDev.ILGPU.WpfDemo/` | The WPF demo |
| `SpawnDev.ILGPU.P2P/` | Experimental distributed-compute backend (not published, see below) |
| `ILGPU.TrimGate/` | The regression gate for trimming safety |
| `PlaywrightMultiTest/` | The test runner (desktop and browser, every backend) |
| `Examples/` | Console examples |
| `Docs/` | Topic documentation |

## Build and test

```bash
dotnet build SpawnDev.ILGPU.slnx
```

Every backend is tested through one command. **PlaywrightMultiTest** drives the desktop backends and a real Chromium with WebGPU enabled for the browser backends:

```bash
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj -c Release
```

Scope a run with environment variables. `PMT_FILTER` is a comma-separated list of substrings matched against the test name, class and method, and `PMT_LANES` selects backend lanes by test class name. Use these instead of `dotnet test --filter`, which only narrows what NUnit reports while the scheduler still runs everything.

```bash
PMT_FILTER=AlgorithmRadixSortPairsTest dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj
PMT_LANES=WebGPUTests PMT_FILTER=RadixSort dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj
```

The suite has about 900 shared test methods. Each one runs on every backend lane, and a lane skips the tests its backend cannot run. The 5.4.4 sweep ran 6,175 test cases: 5,687 passed, 488 were skipped as backend capability gates (for example shared memory on WebGL) and none failed. The demo's `/tests` page runs the same suite in a browser: `dotnet run --project SpawnDev.ILGPU.Demo`.

## In development: P2P distributed compute

`AcceleratorType.P2P` ([SpawnDev.ILGPU.P2P](SpawnDev.ILGPU.P2P)) is an experimental backend that would distribute kernels across connected devices through [SpawnDev.WebTorrent](https://github.com/LostBeard/SpawnDev.WebTorrent). It is on hold while the core backends take priority, it is not published to NuGet, and it is not ready for use.

## Contributing

Issues and pull requests are welcome. A bug in this library is fixed in the library, not worked around in a consumer, so a good report is a failing test or a minimal repro on a named backend.

Before you edit a `.cs` file under `ILGPU/`, read [Docs/development.md](Docs/development.md). That folder has T4 `.tt` templates that regenerate `.cs` files on a clean build, so a manual edit to a generated file passes a local build and is lost on CI. A `T4 Drift Check` workflow runs on every push and pull request that touches `ILGPU/` or `ILGPU.Algorithms/`, and fails with a pointer to the fix.

## Support the project

SpawnDev is built by one independent developer and a small team of AI agents. If this library saves you time, [sponsoring on GitHub](https://github.com/sponsors/LostBeard) directly funds more backends, more verified kernels and the hardware to test them on. Stars, issues and sharing what you build with it help just as much.

## Credits

- [**ILGPU**](https://github.com/m4rs-mt/ILGPU) by Marcel Koester and the [ILGPU contributors](https://github.com/m4rs-mt/ILGPU/graphs/contributors), the compiler that makes C# GPU kernels possible.
- [**SpawnDev.SpawnJS**](https://github.com/LostBeard/SpawnDev.SpawnJS), typed C# access to the browser's APIs (it replaced SpawnDev.BlazorJS as the interop layer in 5.3).
- Used by [SpawnDev.ILGPU.ML](https://github.com/LostBeard/SpawnDev.ILGPU.ML), neural network inference and training for .NET, which is also where much of this library's real-world testing comes from.

Created by Todd Tanner ([@LostBeard](https://github.com/LostBeard)) together with a crew of Claude AI agents: Riker, Data, Tuvok, Geordi and Seven, with Gemini as a brainstorming partner. Live long and prosper. 🖖

## License

[MIT](LICENSE.txt). SpawnDev.ILGPU includes code derived from ILGPU and ILGPU.Algorithms under the University of Illinois Open Source License; see [LICENSE.txt](LICENSE.txt) for both texts.
