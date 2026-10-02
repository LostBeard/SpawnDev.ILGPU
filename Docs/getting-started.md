# Getting Started

This guide walks you through installing SpawnDev.ILGPU and running your first GPU kernel — in the browser via Blazor WebAssembly or on desktop via a console app.

SpawnDev.ILGPU extends [ILGPU](https://github.com/m4rs-mt/ILGPU) — a high-performance .NET GPU computing framework by [Marcel Koester](https://github.com/m4rs-mt) — with three browser backends (WebGPU, WebGL, Wasm) while also supporting ILGPU's native desktop backends (CUDA, OpenCL, CPU). Your existing ILGPU kernels work across all six backends with zero changes to the kernel code.

## Prerequisites

- **.NET 10 SDK** (or later)

**For browser (Blazor WebAssembly):**
- Blazor WebAssembly project
- A modern browser (Chrome 113+ for WebGPU, any modern browser for WebGL/Wasm)

**For desktop (Console, WPF, ASP.NET):**
- Any .NET 10 project
- NVIDIA GPU + driver (for CUDA) or OpenCL 2.0+ GPU (for OpenCL) — or CPU-only

## Installation

```bash
dotnet add package SpawnDev.ILGPU
```

This installs SpawnDev.ILGPU along with the bundled ILGPU compiler and [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) for browser interop.

## Configure Program.cs

SpawnDev.ILGPU requires SpawnDev.BlazorJS to be initialized. This replaces the standard `RunAsync()` call:

```csharp
using SpawnDev.BlazorJS;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Add BlazorJS services (required for browser interop)
builder.Services.AddBlazorJSRuntime();

// Use BlazorJSRunAsync instead of RunAsync
await builder.Build().BlazorJSRunAsync();
```

> **Desktop apps** do not require SpawnDev.BlazorJS. Use `SpawnDev.ILGPU` directly — no special initialization needed.

## Publishing Configuration

Trimming and Blazor WebAssembly AOT both work. If you AOT compile, keep the IL:

```xml
<PropertyGroup>
  <!-- Trimming: supported (the default for a Blazor Release publish). Nothing to configure. -->
  <PublishTrimmed>true</PublishTrimmed>

  <!-- AOT: supported, but KEEP THE IL. .NET strips IL after AOT by default, and ILGPU
       compiles kernels FROM IL at runtime. -->
  <RunAOTCompilation>true</RunAOTCompilation>
  <WasmStripILAfterAOT>false</WasmStripILAfterAOT>
</PropertyGroup>
```

- **IL trimming is supported and enforced.** ILGPU, ILGPU.Algorithms, SpawnDev.ILGPU and SpawnDev.ILGPU.ML are
  `IsTrimmable`, and every trim (IL2xxx) analyzer warning is a build error in those libraries, so a new reflection
  hole fails their build instead of a consumer's app. Members ILGPU resolves by name are rooted
  (`ILGPU/Util/TrimmingAnnotations.cs`, `SpawnDev.ILGPU/BrowserTrimRoots.cs`); a consuming app needs no trimming
  configuration of its own. The ILGPU and ILGPU.ML test suites run against TRIMMED publishes.
- **Blazor WebAssembly AOT is supported - with the IL kept.** `RunAOTCompilation=true` +
  `WasmStripILAfterAOT=false`. The old "AOT breaks ILGPU" rule was about the IL being STRIPPED, not about AOT:
  with the IL kept, the frontend reads kernels as usual, and Mono's AOT runtime keeps its interpreter fallback for
  ILGPU's dynamically generated launchers. Verified end to end in a production app (Anaglyphohol: WebGPU kernels +
  ILGPU.ML inference), where AOT halved the per-frame host cost (DAv3 at 168x98: 43.8 -> 21.7 ms). Costs: a much
  larger `dotnet.native.wasm` (~55 MB with ILGPU.ML) and a long AOT build (over an hour, single core, for ILGPU.ML).
- **Desktop NativeAOT (`PublishAot`) is NOT supported**: ILGPU emits kernel launchers with Reflection.Emit, which
  NativeAOT cannot run. The AOT analyzer stays on in these libraries, so those sites remain visible (IL3050).

> Older docs told you to set `PublishTrimmed=false` and `RunAOTCompilation=false`. Both are outdated. If a published
> build of an older version throws `MissingMethodException` / "Not supported intrinsic type", upgrade; do not
> disable trimming.

## Your First Kernel

Here's a complete example that adds two arrays on the GPU:

```csharp
@page "/gpu-demo"
@using ILGPU
@using ILGPU.Runtime
@using SpawnDev.ILGPU

<h3>GPU Vector Addition</h3>
<p>@_result</p>

@code {
    private string _result = "Running...";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        // 1. Create a context with all available backends
        using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());

        // 2. Create the best available accelerator
        // Browser: WebGPU > WebGL > Wasm | Desktop: CUDA > OpenCL > CPU
        using var accelerator = await context.CreatePreferredAcceleratorAsync();

        // 3. Allocate GPU buffers
        int length = 256;
        using var bufA = accelerator.Allocate1D(
            Enumerable.Range(0, length).Select(i => (float)i).ToArray());
        using var bufB = accelerator.Allocate1D(
            Enumerable.Range(0, length).Select(i => (float)i * 2f).ToArray());
        using var bufC = accelerator.Allocate1D<float>(length);

        // 4. Load and invoke the kernel
        var kernel = accelerator.LoadAutoGroupedStreamKernel<
            Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAddKernel);
        kernel((Index1D)length, bufA.View, bufB.View, bufC.View);

        // 5. Wait for GPU to finish (MUST use async version in Blazor WASM)
        await accelerator.SynchronizeAsync();

        // 6. Read results back to CPU
        var results = await bufC.CopyToHostAsync<float>();

        _result = $"Backend: {accelerator.Name} | " +
                  $"result[0]={results[0]}, result[1]={results[1]}, result[255]={results[255]}";
        StateHasChanged();
    }

    // The kernel — this is the code that runs on the GPU
    // It's a standard ILGPU kernel: static, void, Index as first param
    static void VectorAddKernel(
        Index1D index,
        ArrayView<float> a,
        ArrayView<float> b,
        ArrayView<float> c)
    {
        c[index] = a[index] + b[index];
    }
}
```

## Understanding the Code

### Context and Backend Discovery

```csharp
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
```

`Context.CreateAsync` is the async version of ILGPU's `Context.Create`. The `AllAcceleratorsAsync()` extension probes the environment for all available backends — browser backends (WebGPU, WebGL, Wasm) in Blazor, or native backends (CUDA, OpenCL, CPU) on desktop — and registers them.

### Accelerator Creation

```csharp
using var accelerator = await context.CreatePreferredAcceleratorAsync();
```

This picks the best available backend: **WebGPU → WebGL → Wasm** in the browser, or **CUDA → OpenCL → CPU** on desktop. You can also target a specific backend — see [Backends](backends.md).

### The Kernel

```csharp
static void VectorAddKernel(Index1D index, ArrayView<float> a, ArrayView<float> b, ArrayView<float> c)
{
    c[index] = a[index] + b[index];
}
```

A kernel is a `static void` method. The first parameter is always an **index type** (`Index1D`, `Index2D`, or `Index3D`). Think of it as the body of a parallel `for` loop — each thread gets a unique `index` value and runs the same code.

Key rules:
- Kernels must be `static` methods
- Only **value types** are allowed (no classes, no `string`, no reference types)
- No `throw` statements — see [Limitations](limitations.md)
- No `ref` or `out` parameters
- Use `ArrayView<T>` to access GPU memory (like a `Span<T>` for the GPU)

### Async Synchronization

```csharp
await accelerator.SynchronizeAsync();
```

> **Critical:** In Blazor WASM, you **must** use `SynchronizeAsync()` instead of `Synchronize()`. The main thread is single-threaded, so the synchronous `Synchronize()` **throws `NotSupportedException`** on the browser backends (use `Flush()` if you only need to submit without waiting). On desktop, both sync and async work, but async is recommended for cross-platform code. (Separately, blocking on any async GPU work with `.Result`/`.Wait()` will deadlock the thread.)

### Data Readback

```csharp
var results = await bufC.CopyToHostAsync<float>();
```

This copies data from the GPU back to a C# array. It works with all six backends automatically.

## Next Steps

- **[Backends](backends.md)** — Learn about each backend's capabilities and configuration options
- **[Capabilities & Backend Selection](capabilities-and-backend-selection.md)** — Declare what your kernel needs upfront so the right backend is auto-selected (and incompatible ones rejected with a typed exception instead of silent garbage)
- **[Writing Kernels](kernels.md)** — Deeper dive into kernel programming, math functions, and advanced patterns
- **[Memory & Buffers](memory-and-buffers.md)** — Buffer allocation, transfer patterns, and zero-allocation readback
