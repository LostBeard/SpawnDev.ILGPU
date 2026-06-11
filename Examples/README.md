# SpawnDev.ILGPU Examples

Self-contained example projects, **basic -> advanced**. Each folder is a standalone project that
references **`SpawnDev.ILGPU`** from NuGet (the real consumer experience) - copy a folder out, run
`dotnet run` (console) or `dotnet run` + open the browser (Blazor WASM), and it works.

Same C# kernel code runs on **all 6 backends**: CUDA / OpenCL / CPU on the desktop, and
WebGPU / WebGL / Wasm in the browser (Blazor WebAssembly).

## The progression

| # | Example | Host | Backends shown | Demonstrates |
|---|---------|------|----------------|--------------|
| 01 | **HelloKernel** | Console | CPU | The simplest kernel - add two arrays. Context -> accelerator -> buffers -> launch -> read back. |
| 02 | **BackendSelection** | Console | CUDA/OpenCL/CPU | `CreatePreferredAccelerator`, device enumeration, capability gating (`AcceleratorRequirements`). |
| 03 | **Algorithms** | Console | preferred | ILGPU Algorithms - `RadixSort`, `Scan`, `Reduce` - the same call on every backend. |
| 04 | **LambdaAndDelegateSpec** | Console | preferred | Lambda kernels (captured scalars) + `DelegateSpecialization` (one kernel, many ops, compile-time inlined). |
| 05 | **BlazorBasic** | Blazor WASM | WebGPU/WebGL/Wasm | The same kernel in the browser - auto-selects the best browser backend, async readback. |
| 06 | **PrecompiledShaders** | Blazor WASM | WebGPU/WebGL/Wasm | Build-time shader precompilation (`[PrecompiledKernel]` + `<SpawnDevPrecompileShaders>`) and the runtime cache - move IL->shader transpilation off the startup path. |
| 07 | **CanvasRendering** | Blazor WASM | WebGPU | Zero-copy GPU -> `<canvas>` rendering (a compute kernel writes pixels, blitted to the canvas). |

> Examples are added incrementally; this index lists the planned set. Each folder has its own README.

## Running

**Console examples:**
```bash
cd Examples/01-HelloKernel
dotnet run
```

**Blazor WASM examples:**
```bash
cd Examples/05-BlazorBasic
dotnet run
# open the printed http(s):// URL
```
WebGPU needs Chrome/Edge 113+ (or Firefox Nightly). The Wasm backend's multi-worker mode needs the
page to be cross-origin isolated (the templates include the `coi-serviceworker.js` that handles it).

## Notes
- Each example pins `SpawnDev.ILGPU` to a released version (e.g. `4.10.0`). Bump it to try a newer release.
- Blazor WASM examples set `<PublishTrimmed>false>` + `<RunAOTCompilation>false>` - ILGPU relies on
  runtime IL reflection, so trimming/AOT must stay off.
