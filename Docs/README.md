# SpawnDev.ILGPU Documentation

Comprehensive documentation for SpawnDev.ILGPU — run ILGPU C# kernels on WebGPU, WebGL, and Wasm in the browser, or Cuda, OpenCL, and CPU on desktop/server, from a single codebase.

> **Your existing ILGPU kernels run in the browser with zero changes to the kernel code — and the same code runs on desktop too.**

## Table of Contents

### Getting Started
- **[Getting Started](getting-started.md)** — Installation, setup, and your first GPU kernel (browser or desktop)

### Core Concepts
- **[Backends](backends.md)** — WebGPU, WebGL, Wasm (browser) and Cuda, OpenCL, CPU (desktop): setup, capabilities, and auto-selection
- **[Capabilities & Backend Selection](capabilities-and-backend-selection.md)** — `AcceleratorRequirements` declarative gating, `UnsupportedKernelFeatureException`, choosing the right backend for a given kernel
- **[Writing Kernels](kernels.md)** — Kernel fundamentals, index types, loading, launching, math functions, and kernel rules
- **[Memory & Buffers](memory-and-buffers.md)** — Allocation, data transfer, async readback, buffer lifecycle, and the WebGPU compiled-shader cache (retention, `MaxCachedShaders` LRU cap, `ClearShaderCache()`)

### Advanced
- **[Advanced Patterns](advanced-patterns.md)** — ILGPU Algorithms (RadixSort, Scan, Reduce), GPU device sharing, external buffers, rendering pipelines, canvas blitting
- **[WebGPU Dispatch-Plan Capture / Replay](dispatch-plan-capture-replay.md)** - Record a fixed WebGPU dispatch sequence once and replay it with a single interop crossing (the browser twin of CUDA graph capture); parameterized replay for a moving loop variable, timing helpers, and the validity contract
- **[Wasm SIMD128 (v128)](wasm-simd.md)** — Automatic WebAssembly SIMD vectorization of the Wasm backend: how `kernel_simd` is generated and selected, what vectorizes, cross-mode determinism, and the `ForceScalar`/`ForceSimd` knobs
- **[Generic-Math f16 Kernels](generic-math-f16.md)** — One generic kernel over the numeric/weight type (`float` or `Half`): `INumber<Half>` operators + `NumericConvert.ToFloat32` for fp32 accumulation
- **[CUDA Libraries](cuda-libraries.md)** — NVIDIA-specific libraries: nvJPEG (image encode/decode), cuRand (random numbers), cuBLAS (linear algebra), cuFFT (FFT), NVML (device monitoring)
- **[Limitations & Constraints](limitations.md)** — Blazor WASM restrictions, unsupported features, precision, and browser compatibility

### Reference
- **[API Reference](api-reference.md)** — Public classes, methods, and extension methods organized by namespace
- **[Data Type Support](data-type-support.md)** — Per-type, per-backend verification matrix (buffer read/write, end-to-end, atomics)
- **[Atomic Operations](atomic-operations.md)** — Per-operation, per-backend atomic support (Add, And, Min, Max, Exchange, CAS) with emulation details

### Contributing
- **[Development Notes](development.md)** — Build commands, test commands, and the traps you need to know before editing source — particularly the **T4 template trap in `ILGPU/`** that has bitten this repo at least three times

## Quick Links

| Resource | Link |
|----------|------|
| **NuGet Package** | [SpawnDev.ILGPU](https://www.nuget.org/packages/SpawnDev.ILGPU) |
| **GitHub Repository** | [LostBeard/SpawnDev.ILGPU](https://github.com/LostBeard/SpawnDev.ILGPU) |
| **Live Demo** | [lostbeard.github.io/SpawnDev.ILGPU](https://lostbeard.github.io/SpawnDev.ILGPU/) |
| **ILGPU Project** | [m4rs-mt/ILGPU](https://github.com/m4rs-mt/ILGPU) |
| **ILGPU Homepage** | [www.ilgpu.net](http://www.ilgpu.net) |
| **ILGPU Documentation** | [ILGPU Docs](https://github.com/m4rs-mt/ILGPU/tree/master/Docs) |
| **ILGPU Samples** | [ILGPU Samples](https://github.com/m4rs-mt/ILGPU/tree/master/Samples) |
| **ILGPU NuGet** | [ILGPU on NuGet](https://www.nuget.org/packages/ILGPU) |
| **SpawnDev.BlazorJS** | [LostBeard/SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) |

## Acknowledgments

SpawnDev.ILGPU is built on top of the incredible [ILGPU](https://github.com/m4rs-mt/ILGPU) project — a JIT compiler for high-performance GPU programs written in .NET. ILGPU was originally developed by [Marcel Koester](https://github.com/m4rs-mt) and the ILGPU Project contributors.

We are deeply grateful for their work in creating such a powerful and well-designed GPU computing framework. Without ILGPU's clean architecture and extensible backend design, bringing GPU compute to the browser via Blazor WebAssembly would not have been possible.

ILGPU is licensed under the [University of Illinois/NCSA Open Source License](https://github.com/m4rs-mt/ILGPU/blob/master/LICENSE.txt).

> **Cross-platform note:** SpawnDev.ILGPU bundles the full ILGPU library, so it works in both **Blazor WebAssembly** and **desktop/server** environments. The `AllAcceleratorsAsync()` method registers native backends (Cuda, OpenCL, CPU) alongside browser backends, and the async extension methods (`SynchronizeAsync`, `CopyToHostAsync`) gracefully fall back to synchronous ILGPU calls for native accelerators. This means you can target both browser and desktop from a single codebase without swapping libraries.
