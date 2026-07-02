# Advanced Patterns

This guide covers advanced usage patterns: device sharing, external buffers, real-time rendering, canvas blitting, and GPU intrinsics.

## GPU Intrinsics

ILGPU provides built-in intrinsics for GPU-specific operations. SpawnDev.ILGPU supports these across its backends.

### Group Operations

Group (workgroup) operations let threads within a workgroup coordinate:

| Intrinsic | Description | WebGPU | WebGL | Wasm | CUDA | OpenCL | CPU |
|-----------|-------------|--------|-------|------|------|--------|-----|
| `Group.IdxX` / `IdxY` / `IdxZ` | Thread index within workgroup | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `Group.DimX` / `DimY` / `DimZ` | Workgroup size | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| `Group.Barrier()` | Synchronize all threads in group | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Group.BarrierPopCount(bool)` | Barrier + count true values | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Group.BarrierAnd(bool)` | Barrier + AND across group | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Group.BarrierOr(bool)` | Barrier + OR across group | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Group.Broadcast(value, idx)` | Broadcast value from one thread | ✅ | ❌ | ✅ | ✅ | ✅¹ | ✅ |

```csharp
static void BarrierExample(ArrayView<int> data, ArrayView<int> output)
{
    var shared = SharedMemory.Allocate<int>(64);
    int localIdx = Group.IdxX;
    int globalIdx = Grid.GlobalIndex.X;

    // Phase 1: Load into shared memory
    shared[localIdx] = data[globalIdx];
    Group.Barrier();  // All threads must finish loading

    // Phase 2: Read neighbor's data (safe because barrier guarantees completion)
    int neighborIdx = (localIdx + 1) % Group.DimX;
    output[globalIdx] = shared[neighborIdx];
}
```

### Grid Operations

Grid operations provide information about the entire dispatch:

| Intrinsic | Description | Notes |
|-----------|-------------|-------|
| `Grid.IdxX` / `IdxY` / `IdxZ` | Workgroup index in the grid | |
| `Grid.DimX` / `DimY` / `DimZ` | Number of workgroups | |
| `Grid.GlobalIndex.X` / `.Y` / `.Z` | Global thread index | = `Grid.IdxX * Group.DimX + Group.IdxX` |
| `Grid.GlobalLinearIndex` | Flattened global thread index | Useful for 1D access to data |

```csharp
static void GridExample(ArrayView<float> data)
{
    // Global thread index (combines grid + group indices)
    int globalIdx = Grid.GlobalIndex.X;
    
    // Total number of threads
    int totalThreads = Grid.DimX * Group.DimX;
    
    // Grid-stride loop: process more elements than threads
    for (int i = globalIdx; i < (int)data.Length; i += totalThreads)
    {
        data[i] *= 2.0f;
    }
}
```

### Warp Operations

Warp (subgroup) operations allow threads within a warp to communicate directly without shared memory:

| Intrinsic | Description | WebGPU | WebGL | Wasm | CUDA | OpenCL | CPU |
|-----------|-------------|--------|-------|------|------|--------|-----|
| `Warp.WarpSize` | Number of threads in a warp | ✅² | ❌ | ✅ | ✅ | ✅¹ | ✅ |
| `Warp.LaneIdx` | Thread index within warp | ✅² | ❌ | ✅ | ✅ | ✅¹ | ✅ |
| `Warp.Shuffle(value, srcLane)` | Read value from another lane | ✅² | ❌ | ✅ | ✅ | ✅¹ | ✅ |
| `Warp.ShuffleDown(value, delta)` | Read from lane + delta | ✅² | ❌ | ✅ | ✅ | ✅¹ | ✅ |
| `Warp.ShuffleUp(value, delta)` | Read from lane - delta | ✅² | ❌ | ✅ | ✅ | ✅¹ | ✅ |
| `Warp.ShuffleXor(value, mask)` | Read from lane XOR mask | ✅² | ❌ | ✅ | ✅ | ✅¹ | ✅ |

¹ Requires device subgroup support. For OpenCL: base subgroups need `cl_khr_subgroups` or `cl_intel_subgroups`; Warp.Shuffle additionally needs `cl_intel_subgroups` (Intel) or `cl_khr_subgroup_shuffle` + `cl_khr_subgroup_shuffle_relative` (NVIDIA/AMD). Dynamically detected.  
² Requires `subgroups` WebGPU extension (Chrome 128+).

```csharp
static void WarpReduceExample(ArrayView<float> data, ArrayView<float> output)
{
    int globalIdx = Grid.GlobalIndex.X;
    float value = data[globalIdx];

    // Warp-level parallel reduction (sum)
    for (int offset = Warp.WarpSize / 2; offset > 0; offset /= 2)
    {
        value += Warp.ShuffleDown(value, offset);
    }

    // Lane 0 has the warp's sum
    if (Warp.LaneIdx == 0)
    {
        output[Grid.GlobalIndex.X / Warp.WarpSize] = value;
    }
}
```

### Atomic Operations

Atomics perform thread-safe read-modify-write operations on shared or global memory:

| Intrinsic | Description | WebGPU | WebGL | Wasm | CUDA | OpenCL | CPU |
|-----------|-------------|--------|-------|------|------|--------|-----|
| `Atomic.Add(ref, value)` | Atomic add | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Atomic.Min(ref, value)` | Atomic minimum | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Atomic.Max(ref, value)` | Atomic maximum | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Atomic.And(ref, value)` | Atomic bitwise AND | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Atomic.Or(ref, value)` | Atomic bitwise OR | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Atomic.Xor(ref, value)` | Atomic bitwise XOR | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Atomic.Exchange(ref, value)` | Atomic swap | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| `Atomic.CompareExchange(ref, cmp, val)` | CAS (compare-and-swap) | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |

```csharp
static void AtomicCountKernel(Index1D index, ArrayView<int> data, ArrayView<int> counter)
{
    if (data[index] > 0)
    {
        // Thread-safe increment
        Atomic.Add(ref counter[0], 1);
    }
}
```

### Utility Intrinsics

| Intrinsic | Description | Notes |
|-----------|-------------|-------|
| `Interop.SizeOf<T>()` | Size of type T in bytes | Works with generic structs |
| `Interop.FloatAsInt(float)` | Reinterpret float bits as int | Bitcast |
| `Interop.IntAsFloat(int)` | Reinterpret int bits as float | Bitcast |

## ILGPU Algorithms in the Browser

ILGPU.Algorithms provides high-level primitives like RadixSort, Scan, Reduce, and Histogram. **WebGPU** fully supports these in the browser — same API as desktop, zero code changes.

### RadixSort (WebGPU)

RadixSort is ideal for GPU-based sorting of key-value pairs (e.g., for Gaussian splat rendering). Works with arbitrary element counts and ascending/descending order:

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

### Reduce & Scan (WebGPU, Wasm)

Reduce and inclusive/exclusive scan work on WebGPU and Wasm:

```csharp
var reduce = accelerator.CreateReduce<int, Stride1D.Dense, AddInt32>();
var result = await reduce(stream, input.View);
await accelerator.SynchronizeAsync();

var scan = accelerator.CreateScan<int, Stride1D.Dense, Stride1D.Dense, AddInt32>(ScanKind.Inclusive);
scan(stream, input.View, output.View, tempView);
```

### Backend Support

| Algorithm | WebGPU | WebGL | Wasm |
|-----------|--------|-------|------|
| RadixSort | ✅ | ✅ host† | ✅ |
| Scan | ✅ | ✅ host† | ✅ |
| Reduce | ✅ | ✅ host† | ✅ |
| Histogram | ✅ | ❌ | ✅ |

**† Host-level only on WebGL.** The host-level builders `accelerator.CreateRadixSort`/`CreateRadixSortPairs`/`CreateScan`/`CreateReduce` DO run on WebGL — they use shared-memory-free multi-dispatch, where the draw-call boundary acts as the barrier (RadixSort via render-to-texture scatter). What WebGL cannot do is the **in-kernel** group/warp ops (`Group.Scan`/`Group.Reduce`/`Warp.*`): those need shared memory + barriers and throw `UnsupportedKernelFeatureException` at kernel-compile time.

## GPUDevice Sharing

Share a `GPUDevice` between SpawnDev.ILGPU and another WebGPU library (e.g., ONNX Runtime Web, Three.js). This allows both libraries to access the same GPU buffers without copying.

### Creating from an External Device

```csharp
using SpawnDev.ILGPU.WebGPU;
using SpawnDev.BlazorJS.JSObjects;

// Get device from another library (e.g., ONNX Runtime Web)
GPUDevice externalDevice = GetORTWebGPUDevice();

// Create an ILGPU context with WebGPU enabled
using var context = await Context.CreateAsync(builder => builder.WebGPU());

// Create accelerator from the external device — no new device is created
var accelerator = WebGPUAccelerator.CreateFromExternalDevice(context, externalDevice);

// Now both libraries share the same GPUDevice
// Buffers created by either library can be used by the other
```

### Accessing the Native GPUDevice

If you need direct WebGPU API access alongside ILGPU:

```csharp
// accelerator is a WebGPUAccelerator
var nativeAccelerator = accelerator.NativeAccelerator;
GPUDevice device = nativeAccelerator.NativeDevice;
GPUQueue queue = nativeAccelerator.Queue;

// Use the device for raw WebGPU operations
using var renderPipeline = device.CreateRenderPipeline(new GPURenderPipelineDescriptor { ... });
```

## External GPU Buffers

Wrap an externally-managed `GPUBuffer` as an ILGPU `MemoryBuffer` for zero-copy kernel execution:

```csharp
using SpawnDev.ILGPU.WebGPU.Backend;
using SpawnDev.BlazorJS.JSObjects;

// A GPUBuffer created by another library
GPUBuffer externalBuffer = GetExternalGPUBuffer();
int elementCount = 1024;
int elementSize = sizeof(float);

// Wrap as an ILGPU buffer (non-owning — won't destroy the buffer on dispose)
var wrapped = new ExternalWebGPUMemoryBuffer(
    accelerator, externalBuffer, elementCount, elementSize);

// Use in ILGPU kernels as an ArrayView
var view = wrapped.AsArrayView<float>(0, elementCount);
kernel((Index1D)elementCount, view, ...);

// Dispose releases the wrapper only — the external buffer is NOT destroyed
wrapped.Dispose();
```

> **Requirements:**
> - Both the external buffer and the accelerator must share the same `GPUDevice`
> - The external buffer must have `GPUBufferUsage.Storage` flag

## Canvas Rendering Pipeline

The recommended way to blit an ILGPU pixel buffer to an HTML `<canvas>` is `ICanvasRenderer`. It picks the fastest rendering path for each backend automatically — no CPU readback on WebGPU or WebGL.

> **Full details:** See [Canvas Rendering](canvas-rendering.md).

### Quick Pattern

```csharp
using SpawnDev.ILGPU.Rendering;

// Create once alongside the accelerator
ICanvasRenderer _renderer = CanvasRendererFactory.Create(_accelerator);

// Attach to the canvas (once, or when the element changes)
using var canvas = new HTMLCanvasElement(_canvasRef);
_renderer.AttachCanvas(canvas);

// Each frame: dispatch → sync → present
_kernel(_outputBuffer.IntExtent, _outputBuffer.View /*, args */);
await _accelerator.SynchronizeAsync();
await _renderer.PresentAsync(_outputBuffer);
```

`CanvasRendererFactory.Create` returns:
- `WebGPUCanvasRenderer` — fullscreen-triangle render pass, zero CPU readback
- `WebGLCanvasRenderer` — `ImageBitmap` transfer from the GL worker, drawn synchronously
- `CPUCanvasRenderer` — reused `ImageData` with a fast Uint8Array copy (Wasm in browser; desktop CPU on server/desktop)

### Pixel Format

Pack pixels as `uint32` little-endian RGBA (R in bits 0–7, A in bits 24–31):

```csharp
static void PixelKernel(Index2D index, ArrayView2D<uint, Stride2D.DenseX> output)
{
    byte r = (byte)(255 * index.X / width);
    byte g = (byte)(255 * index.Y / height);
    byte b = 128;
    byte a = 255;

    output[index] = (uint)((a << 24) | (b << 16) | (g << 8) | r);
}
```

This packing is identical across all three renderer implementations.

## Real-Time Render Loop Pattern

A complete pattern for running a continuous render loop in Blazor:

```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _ = RenderLoop();  // Fire-and-forget
    }
}

private async Task RenderLoop()
{
    await InitializeBackend();

    while (!_disposed)
    {
        await RenderFrame();

        // Throttle UI updates to avoid overwhelming Blazor
        if ((DateTime.UtcNow - _lastUiUpdate).TotalMilliseconds >= 100)
        {
            _lastUiUpdate = DateTime.UtcNow;
            StateHasChanged();
        }

        await Task.Yield();  // Let Blazor process UI events
    }
}
```

> **Important:** The `await Task.Yield()` between frames is critical — without it, the UI thread never gets a chance to process user input (mouse events, button clicks, etc.).

## Runtime Backend Switching

Switch backends at runtime without restarting the app. This is used by the demo's Compute3D and FractalExplorer pages:

```csharp
private async Task SwitchBackend(string backendName)
{
    // 1. Dispose everything (reverse order)
    _kernel = null;
    _outputBuffer?.Dispose(); _outputBuffer = null;
    _accelerator?.Dispose(); _accelerator = null;
    _context?.Dispose(); _context = null;

    // 2. Initialize new backend
    switch (backendName)
    {
        case "WebGPU":
            _context = await Context.CreateAsync(builder => builder.WebGPU());
            var gpuDevices = _context.GetWebGPUDevices();
            _accelerator = await gpuDevices[0].CreateAcceleratorAsync(_context);
            break;
        case "WebGL":
            _context = await Context.CreateAsync(builder => builder.WebGL());
            var glDevices = _context.GetWebGLDevices();
            _accelerator = glDevices[0].CreateAccelerator(_context);
            break;
        case "Wasm":
            _context = await Context.CreateAsync(builder => builder.Wasm());
            var wasmDevices = _context.GetDevices<WasmILGPUDevice>();
            _accelerator = await wasmDevices[0].CreateAcceleratorAsync(_context);
            break;
    }

    // 3. Reload kernel on new accelerator
    _kernel = _accelerator.LoadAutoGroupedStreamKernel<...>(MyKernel);
    _outputBuffer = _accelerator.Allocate2DDenseX<uint>(new Index2D(_width, _height));
}
```

## Debugging & Diagnostics

### Verbose Logging

```csharp
WebGPUBackend.VerboseLogging = true;
```

This enables detailed console output including:
- Generated WGSL/GLSL shader source
- Buffer binding layout (binding index, size, offset)
- Dispatch dimensions
- Dynamic shared memory configuration

### Compiled Shader Source

After loading a kernel the generated shader source is captured automatically:

```csharp
using SpawnDev.ILGPU.WebGPU.Backend;   // WebGPUBackend
using SpawnDev.ILGPU.WebGL;            // WebGLAccelerator

// Available immediately after LoadAutoGroupedStreamKernel / LoadStreamKernel
string? wgsl = WebGPUBackend.LastGeneratedWGSL;       // WebGPU backend
string? glsl = WebGLAccelerator.LastGeneratedGLSL;    // WebGL backend
```

Both properties are `static` and updated on every kernel load, so they always hold the most recently compiled shader.

### Device Information

```csharp
// Print device info
var devices = context.GetWebGPUDevices();
devices[0].PrintInformation(Console.Out);

// Check enabled features
var features = accelerator.NativeAccelerator.EnabledFeatures;
Console.WriteLine($"Features: {string.Join(", ", features)}");
```

## ILGPU Algorithms

ILGPU includes built-in high-performance algorithms that work with SpawnDev.ILGPU backends:

| Algorithm | Description | Notes |
|-----------|-------------|-------|
| **Reduce** | Parallel reduction (sum, min, max, etc.) | Uses subgroups + shared memory |
| **Scan** | Prefix sum (inclusive/exclusive) | |
| **RadixSort** | Parallel radix sort | |
| **Initialize** | Fill buffer with a value or sequence | |
| **Transform** | Apply a function to each element | |
| **Sequence** | Generate sequential values | |
| **Histogram** | Count occurrences | |

```csharp
using ILGPU.Algorithms;

// Example: parallel reduce (sum all elements)
using var input = accelerator.Allocate1D(new float[] { 1, 2, 3, 4, 5 });
using var output = accelerator.Allocate1D<float>(1);

accelerator.Reduce<float, AddFloat>(
    accelerator.DefaultStream,
    input.View,
    output.View);

await accelerator.SynchronizeAsync();
var result = await output.CopyToHostAsync<float>();
// result[0] == 15
```

> **Note:** Not all algorithms work with all backends. Algorithms using shared memory or atomics require WebGPU, Wasm, CUDA, OpenCL, or CPU — they do not work on WebGL. See [Backends](backends.md) for the full compatibility matrix.
