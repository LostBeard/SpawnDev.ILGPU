# Writing Kernels

A kernel is a function that runs in parallel across many threads. SpawnDev.ILGPU compiles your C# kernel code into the target backend's native language — WGSL or GLSL for browser GPUs, PTX or OpenCL C for desktop GPUs, or WebAssembly / native threads for CPU backends.

## Kernel Basics

A kernel is typically a **static void** method. The first parameter is an **index type** that identifies which thread is running. Think of it as the body of a massively parallel `for` loop:

```csharp
// This kernel runs once per element — each thread gets a unique index
static void MyKernel(Index1D index, ArrayView<float> data, float multiplier)
{
    data[index] = data[index] * multiplier;
}
```

When you launch this kernel with 1000 elements, 1000 threads execute simultaneously, each with a different `index` value from 0 to 999.

### Lambda Kernels

You can also write kernels as C# lambdas that capture local variables. Captured scalar values (`int`, `float`, `long`, etc.) are automatically passed to the GPU at dispatch time:

```csharp
int multiplier = 5;
float offset = 0.5f;
var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(
    (index, buf) => { buf[index] = index * multiplier + offset; });
kernel((Index1D)length, buffer.View);
```

> **Note:** Only scalar value types can be captured. `ArrayView` captures are not supported — pass them as explicit kernel parameters instead.

### Higher-Order Kernels with DelegateSpecialization

`DelegateSpecialization<T>` lets you write one kernel that accepts different operations as parameters. The delegate is resolved at dispatch time and **inlined at compile time** — the GPU never sees a function pointer, never branches on op-tag, never pays a virtual-call cost:

```csharp
static int Negate(int x) => -x;
static int Square(int x) => x * x;

static void MapKernel(Index1D index, ArrayView<int> buf,
    DelegateSpecialization<Func<int, int>> transform)
{
    buf[index] = transform.Value(buf[index]);
}

var kernel = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<int>, DelegateSpecialization<Func<int, int>>>(MapKernel);

// Same kernel, different operations
kernel(size, buffer, new DelegateSpecialization<Func<int, int>>(Negate));
kernel(size, buffer, new DelegateSpecialization<Func<int, int>>(Square));
```

Each unique target method produces a **cached specialized kernel compilation**. The first dispatch with `Negate` compiles a kernel that has `-x` baked in. The first dispatch with `Square` compiles a separate kernel that has `x * x` baked in. Subsequent dispatches with the same target method reuse the cached compilation.

#### When to use it

The pattern shines when you have **one data-shape, many ops**. Without `DelegateSpecialization` you'd write N near-identical kernels; with it you write one and pay a single compilation per op variant you actually call.

Real-world examples:
- **Element-wise tensor ops** — Add, Sub, Mul, Div, Min, Max all run over the same `(ArrayView<float> a, ArrayView<float> b, ArrayView<float> out)` shape.
- **Reductions** — sum, product, min, max, all, any all reduce over the same input view, only the combine function differs.
- **Activation functions** in ML — ReLU, GELU, Sigmoid, Tanh applied element-wise over the same buffer shape.
- **Sort / scan with custom comparators or combiners** — `ILGPU.Algorithms` uses this pattern internally.

If you find yourself writing four kernels named `AddKernel`, `SubKernel`, `MulKernel`, `DivKernel` that differ only by one operator, that's the moment to collapse them into a single `BinaryOpKernel<DelegateSpecialization<Func<float, float, float>>>` and four delegate values. Same final code on the GPU; one source-of-truth on the C# side.

#### Multi-argument signatures

Any `Func<...>` or `Action<...>` shape works as long as the parameter and return types are kernel-legal value types:

```csharp
static float Add(float a, float b) => a + b;
static float Mul(float a, float b) => a * b;

static void BinaryOpKernel(
    Index1D index,
    ArrayView<float> a, ArrayView<float> b, ArrayView<float> result,
    DelegateSpecialization<Func<float, float, float>> op)
{
    result[index] = op.Value(a[index], b[index]);
}

var kernel = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>,
    DelegateSpecialization<Func<float, float, float>>>(BinaryOpKernel);

kernel(length, aView, bView, sumView, new DelegateSpecialization<Func<float, float, float>>(Add));
kernel(length, aView, bView, prdView, new DelegateSpecialization<Func<float, float, float>>(Mul));
```

#### Rules

- **Target method must be `static`.** Instance methods cannot be specialized.
- **No captured locals.** A lambda that captures variables (`(x) => x + capturedScalar`) won't work — `DelegateSpecialization` resolves the target method by reflection and bakes that method's body in. There's no closure to carry the captured value. If you need a captured scalar, pass it as an explicit kernel parameter alongside the delegate.
- **Target body follows kernel rules.** No `throw`, no `ref`/`out` on the delegate's signature itself, value types only. Helper methods called from the target body still follow `[MethodImpl(NoInlining)]` rules described above.

## Kernel Rules

These rules apply to all kernel code — they come from ILGPU's design and the constraints of GPU execution:

| Rule | Details |
|------|---------|
| Must be `static` (or a lambda) | Instance methods are not supported (except capturing lambdas) |
| Must return `void` | Kernels don't return values — use output buffers |
| First parameter is the index | `Index1D`, `Index2D`, or `Index3D` |
| Value types only | No classes, no `string`, no reference types |
| No `throw` | No backend supports exception handling in kernels |
| No recursion | GPU hardware doesn't support call stacks |
| No dynamic allocation | No `new` inside kernels (except fixed-size structs) |

> **`ref` / `out` parameters are supported in helper methods** called from a kernel — see "Helper Methods and Inlining" below. They are NOT supported on the kernel's own top-level signature (the entry point itself).

## Helper Methods and Inlining

Kernels often call private static helper methods to share common logic. By default ILGPU **inlines every helper into the kernel at IR level** — the GPU never sees a function call, just a flat kernel body. For small helpers this is what you want: zero call overhead, all values stay in registers, the optimizer can see across the boundary.

For **large helpers called many times**, default inlining is a problem. Each call site duplicates the helper body. A 500-IL-instruction helper called 32 times becomes a 16,000-IL-instruction straight-line kernel body, and on shader backends that translates to a multi-thousand-line WGSL/GLSL `fn main()` that hits the browser's shader validator size cliff:

- Chrome's WGSL validator (Tint) rejects oversized shaders with `Invalid BindGroupLayout` after 15-30 seconds of validator work.
- ANGLE D3D11 fails to compile vertex shaders past a similar threshold.
- Compile time becomes the dominant cost of every kernel dispatch.

The fix: mark the helper with `[MethodImpl(MethodImplOptions.NoInlining)]`. ILGPU's IR Inliner respects the attribute; the helper stays as a separate Method in the IR; the codegen emits a real WGSL/GLSL `fn` definition + N call sites. Validator chews through it in milliseconds.

```csharp
using System.Runtime.CompilerServices;

public sealed class Vp9Idct16x16Kernel
{
    private static void IdctKernel(
        Index1D blockIdx,
        ArrayView<short> coeffs,
        ArrayView<byte> dest,
        int blockCount,
        int blockStrideBytes)
    {
        // ... 16 row-pass calls + 16 column-pass calls = 32 call sites
        Idct16Row(
            coeffs[rBase + 0], /* ... 15 more short inputs */,
            out int o0, /* ... 15 more out int outputs */);
        // ... rest of kernel body
    }

    // Without [NoInlining], this 500-IL helper inlines 32x = ~16,000 IL =
    // ~3,800-line WGSL straight-line block = Tint validator rejects with
    // "Invalid BindGroupLayout".
    //
    // With [NoInlining], WGSL emits one `fn Idct16Row_NN(...)` definition
    // + 32 function calls. Compile is sub-second.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Idct16Row(
        short i0, short i1, /* ... 14 more short inputs */,
        out int o0, out int o1, /* ... 14 more out int outputs */)
    {
        // ... 7-stage butterfly arithmetic
    }
}
```

### When to use `[MethodImpl(NoInlining)]`

| Pattern | Use NoInlining? | Why |
|---------|-----------------|-----|
| Small helper (< ~100 IL), 1-2 call sites | **No** | Default inlining is faster; no compile cliff. |
| Small helper, called dozens of times in a loop | **No** | Inlining is still cheap and lets the compiler hoist invariants. |
| Large helper (> ~200 IL), ≥ 8 call sites | **Yes** | Inlining produces giant straight-line shader code. |
| Helper does butterfly arithmetic / DCT / FFT-style stages | **Likely yes** | These are typically dense and called many times; even if they fit at small scale, scale with kernel size. |
| Helper uses `Group.Barrier()`, `LocalMemory`, atomics | **No** | WGSL barrier-uniformity requires barriers to be at the same textual depth for all threads — only safe under inlining. |
| Helper takes `ArrayView<T>` parameters | **No** (currently) | View-as-fn-param marshaling is not yet supported in fn-def emission; the helper would compile but the view would not pass correctly. Inline it instead. |

### What the codegen does on each backend

| Backend | Without `[NoInlining]` | With `[NoInlining]` |
|---------|------------------------|--------------------|
| **WebGPU** | Inlined into kernel WGSL | Standalone `fn helper_NN(...)` definition + call sites |
| **WebGL** | Inlined into kernel GLSL | Standalone `void fn_helper_NN(...)` + `inout` ref params + call sites |
| **Wasm** | Inlined into kernel Wasm body | Wasm function + `call` instructions (multi-block helpers + barrier helpers also use this path) |
| **CUDA / OpenCL** | Native function calls (the upstream ILGPU PTX/CL backends) | Same — these backends already support native fn calls |
| **CPU** | Native .NET function calls | Same |

### Helper parameter shapes that work with `[NoInlining]`

The fn-definition codegen path (4.9.2-rc.18+) supports:

- `int`, `long`, `float`, `double`, `bool` and other scalar value types as input params
- `short` / `byte` / `Half` / `BFloat16` and other sub-word scalars (with sign-extending narrowing on cast)
- `ref T` / `out T` for primitive value types — lowers to `ptr<function, T>` in WGSL, `inout T` in GLSL
- Struct value types (lowered field-by-field)
- Multiple call sites (each gets its own scratch slot for ref/out params)

Not yet supported on `[NoInlining]` helpers (use default inlining instead):

- `ArrayView<T>` parameters (view-to-fn-param marshaling deferred)
- `LocalMemory<T>` access from inside the helper (use a scratch parameter instead)
- Barrier / shared-memory access from inside the helper

### Why the IR Inliner respects `[MethodImpl(NoInlining)]`

ILGPU's `Inliner` pass at `ILGPU/IR/Transformations/Inliner.cs` checks the `MethodImplementationFlags` of each method's source `MethodInfo`. When `NoInlining` is set, the Inliner returns early before tagging the method with `MethodFlags.Inline` — so the call survives as a `MethodCall` IR node instead of being expanded into the caller. The backend codegen then sees the call and emits a real function-definition + call-site pair (or the inline-at-codegen-time fallback on backends that don't yet support fn definitions for that helper shape).

`[MethodImpl(MethodImplOptions.AggressiveInlining)]` always inlines (overrides the body-size heuristic). The default is "inline if AggressiveInlining or if the method body fits a heuristic size cap"; the cap is intentionally generous so most user helpers inline.

### Diagnosing the compile cliff

If you see this on WebGPU:
```
[WebGPU] 4 GPU error(s) during dispatch:
[Invalid BindGroupLayout (unlabeled)] is invalid.
 - While calling [Device].CreateBindGroup([BindGroupDescriptor]).
[Invalid ComputePipeline (unlabeled)] is invalid.
```
…with each test taking 15-30 seconds before the failure surfaces, the kernel is hitting Tint's shader size limit. Find the largest helper called repeatedly from your kernel, mark it `[MethodImpl(MethodImplOptions.NoInlining)]`, and re-run. Compile should drop from 15-30 s to sub-second.

## Index Types

The index type determines the dimensionality of the kernel's execution grid:

### Index1D — Linear Processing

```csharp
static void Process1D(Index1D index, ArrayView<float> data, float value)
{
    data[index] = value;
}

// Launch: each element gets one thread
kernel((Index1D)data.Length, data.View, 42.0f);
```

### Index2D — Image/Matrix Processing

```csharp
static void Process2D(
    Index2D index,
    ArrayView2D<uint, Stride2D.DenseX> pixels,
    int width, int height)
{
    int x = index.X;
    int y = index.Y;
    if (x >= width || y >= height) return;

    // Process pixel at (x, y)
    uint r = (uint)(255 * x / width);
    uint g = (uint)(255 * y / height);
    pixels[index] = (0xFFu << 24) | (r << 16) | (g << 8) | 0xFF;
}

// Launch with 2D extent
kernel(buffer.IntExtent, buffer.View, width, height);
```

### Index3D — Volume/Voxel Processing

```csharp
static void Process3D(
    Index3D index,
    ArrayView<float> volume,
    int width, int height, int depth)
{
    int x = index.X, y = index.Y, z = index.Z;
    int i = x + y * width + z * width * height;
    volume[i] = x + y + z;
}
```

## Loading and Launching Kernels

### LoadAutoGroupedStreamKernel (Recommended)

The simplest way to load a kernel. ILGPU automatically determines the optimal workgroup size:

```csharp
// Load once (compile + cache)
var kernel = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAddKernel);

// Launch (fire-and-forget — work is queued)
kernel((Index1D)length, bufA.View, bufB.View, bufC.View);

// Wait for completion
await accelerator.SynchronizeAsync();
```

### Kernel Delegate Caching

For render loops and repeated invocations, cache the kernel delegate:

```csharp
// Declare as a field
private Action<Index2D, ArrayView2D<uint, Stride2D.DenseX>, float, float>? _renderKernel;

// Load once
_renderKernel = accelerator.LoadAutoGroupedStreamKernel<
    Index2D, ArrayView2D<uint, Stride2D.DenseX>, float, float>(RenderKernel);

// Invoke repeatedly (no stream argument needed for auto-grouped)
_renderKernel(buffer.IntExtent, buffer.View, time, zoom);
```

> **Note:** The delegate type for `LoadAutoGroupedStreamKernel` does **not** include an `AcceleratorStream` parameter. The index type is the first argument when calling.

### Explicitly Grouped Kernels

For full control over workgroup size (required for shared memory and barriers):

```csharp
static void GroupedKernel(ArrayView<int> data, ArrayView<int> output)
{
    var globalIdx = Grid.GlobalIndex.X;
    var localIdx = Group.IdxX;
    var groupSize = Group.DimX;

    // Use shared memory
    var sharedMem = SharedMemory.Allocate<int>(64);
    sharedMem[localIdx] = data[globalIdx];

    Group.Barrier(); // Wait for all threads in group

    // Process with shared data...
    output[globalIdx] = sharedMem[(localIdx + 1) % groupSize];
}
```

## Parameter Types

### Scalar Parameters

Scalars (`int`, `float`, `double`, etc.) are passed by value:

```csharp
static void ScalarKernel(Index1D index, ArrayView<float> data, float multiplier, int offset)
{
    data[index] = data[index] * multiplier + offset;
}
```

### Struct Parameters

Custom structs work if they are **value types with fixed size**:

```csharp
public struct SimParams
{
    public float DeltaTime;
    public float Gravity;
    public int MaxIterations;
}

static void PhysicsKernel(Index1D index, ArrayView<float> positions, SimParams p)
{
    positions[index] += p.Gravity * p.DeltaTime;
}
```

### GpuMatrix4x4 — GPU-Friendly 4×4 Matrix

SpawnDev.ILGPU includes `GpuMatrix4x4`, a GPU-friendly 4×4 matrix struct that auto-transposes from .NET's row-major `System.Numerics.Matrix4x4` to GPU column-major order. Use it for 3D transformations inside kernels:

```csharp
using SpawnDev.ILGPU;
using System.Numerics;

// On the host: create from a .NET Matrix4x4 (auto-transposes to GPU column-major)
var viewMatrix = Matrix4x4.CreateLookAt(
    new Vector3(0, 0, 5),   // eye
    Vector3.Zero,            // target
    Vector3.UnitY);          // up
var gpuMatrix = GpuMatrix4x4.FromMatrix4x4(viewMatrix);

// Pass directly as a kernel parameter
kernel((Index1D)count, positionsView, outputView, gpuMatrix);
```

```csharp
// In the kernel: use static transform methods
static void TransformKernel(
    Index1D index,
    ArrayView<float> positions,
    ArrayView<float> output,
    GpuMatrix4x4 matrix)
{
    int i = index * 3;
    float x = positions[i], y = positions[i + 1], z = positions[i + 2];

    // Transform point (rotation + translation)
    GpuMatrix4x4.TransformPoint(matrix, x, y, z, out float rx, out float ry, out float rz);

    output[i] = rx;
    output[i + 1] = ry;
    output[i + 2] = rz;
}
```

| Method | Description |
|--------|-------------|
| `GpuMatrix4x4.FromMatrix4x4(Matrix4x4)` | Auto-transposes from .NET row-major to GPU column-major |
| `GpuMatrix4x4.Identity` | Returns the identity matrix |
| `GpuMatrix4x4.TransformPoint(m, x, y, z, out rx, ry, rz)` | Applies rotation + translation |
| `GpuMatrix4x4.TransformDirection(m, x, y, z, out rx, ry, rz)` | Applies rotation only (no translation) |

> **Why not `System.Numerics.Matrix4x4`?** .NET uses row-major layout with `v * M` convention, while GPUs use column-major with `M * v`. `GpuMatrix4x4` handles this transpose automatically so your transforms work correctly on all backends.

### ArrayView Parameters

`ArrayView<T>` is the primary way to access GPU memory from kernels:

```csharp
static void CopyKernel(Index1D index, ArrayView<float> source, ArrayView<float> dest)
{
    dest[index] = source[index];
}
```

Multi-dimensional views:

```csharp
static void MatrixKernel(
    Index2D index,
    ArrayView2D<float, Stride2D.DenseX> matrix,
    ArrayView<float> result)
{
    int x = index.X, y = index.Y;
    result[y * matrix.IntExtent.X + x] = matrix[index] * 2.0f;
}
```

## Math Functions

### Supported Functions

ILGPU maps standard .NET math to GPU-native operations:

| C# | GPU Mapping | Notes |
|----|-------------|-------|
| `MathF.Sin(x)` | `sin(x)` | ✅ All backends |
| `MathF.Cos(x)` | `cos(x)` | ✅ All backends |
| `MathF.Tan(x)` | `tan(x)` | ✅ All backends |
| `MathF.Sqrt(x)` | `sqrt(x)` | ✅ All backends |
| `MathF.Pow(x, y)` | `pow(x, y)` | ✅ All backends |
| `MathF.Log(x)` | `log(x)` | ✅ All backends |
| `MathF.Exp(x)` | `exp(x)` | ✅ All backends |
| `MathF.Abs(x)` | `abs(x)` | ✅ All backends |
| `MathF.Floor(x)` | `floor(x)` | ✅ All backends |
| `MathF.Ceiling(x)` | `ceil(x)` | ✅ All backends |
| `Math.Min(a, b)` | `min(a, b)` | ✅ All backends |
| `Math.Max(a, b)` | `max(a, b)` | ✅ All backends |
| `MathF.FusedMultiplyAdd` | `fma(a, b, c)` | ✅ All backends |
| `MathF.Atan2(y, x)` | `atan2(y, x)` | ✅ All backends |

### Previously Unsupported Functions (Now Auto-Redirected)

These .NET methods contain internal `throw` statements, but all browser backends now include **throw-free redirects** that handle them automatically:

| C# | Status | Notes |
|----|:------:|----------|
| `Math.Clamp(val, min, max)` | ✅ Auto-redirected | Replaced with `Min(Max(val, min), max)` |
| `Math.Round(x)` | ✅ Auto-redirected | Throw-free wrapper |
| `Math.Truncate(x)` | ✅ Auto-redirected | Throw-free wrapper |
| `Math.Sign(x)` | ✅ Auto-redirected | Throw-free wrapper |
| `MathF.FusedMultiplyAdd` | ✅ Auto-redirected | Throw-free wrapper |

> **Safe to use:** These functions work directly in kernels on all backends thanks to `RegisterMathIntrinsics()`. See [Limitations](limitations.md) for the general `throw` constraint.

## Shared Memory

Shared memory allows threads within a workgroup to share data. It's much faster than global memory but limited in size.

> **Availability:** Supported on WebGPU, Wasm, CUDA, OpenCL, and CPU backends. WebGL does not support shared memory.

### Static Shared Memory

```csharp
static void SharedMemKernel(ArrayView<int> data, ArrayView<int> output)
{
    // Allocate shared memory (compile-time size)
    var shared = SharedMemory.Allocate<int>(64);

    var localIdx = Group.IdxX;
    var globalIdx = Grid.GlobalIndex.X;

    // Load data into shared memory
    shared[localIdx] = data[globalIdx];

    // Wait for all threads
    Group.Barrier();

    // Read from neighbor in shared memory
    output[globalIdx] = shared[(localIdx + 1) % Group.DimX];
}
```

### Dynamic Shared Memory

Dynamic shared memory is sized at launch time:

```csharp
static void DynamicSharedKernel(ArrayView<int> data)
{
    var shared = SharedMemory.GetDynamic<int>();
    // Size is determined by the launch configuration
}

// Launch with dynamic shared memory config
var config = SharedMemoryConfig.RequestDynamic<int>(groupSize);
kernel((gridDim, groupDim, config), data.View);
```

## Control Flow

Standard C# control flow works in kernels:

```csharp
static void ControlFlowKernel(Index1D index, ArrayView<float> data, float threshold)
{
    float val = data[index];

    // If/else
    if (val > threshold)
        data[index] = threshold;
    else
        data[index] = val * 2.0f;

    // Loops
    float sum = 0;
    for (int i = 0; i < 10; i++)
        sum += val * i;

    data[index] = sum;
}
```

> **Performance tip:** Avoid divergent branches within a workgroup. When threads in the same workgroup take different paths, performance degrades because the GPU executes both paths sequentially.

## Common Patterns

### Stencil (Neighbor Access)

```csharp
static void Stencil1D(Index1D index, ArrayView<float> input, ArrayView<float> output)
{
    int i = index;
    int len = (int)input.Length;

    float left  = i > 0 ? input[i - 1] : 0;
    float center = input[i];
    float right = i < len - 1 ? input[i + 1] : 0;

    output[i] = (left + center + right) / 3.0f;
}
```

### Bounds Checking

Always guard against out-of-bounds access when the dispatch size may exceed the data size:

```csharp
static void SafeKernel(Index1D index, ArrayView<float> data, int actualLength)
{
    if (index >= actualLength) return;
    data[index] = data[index] * 2.0f;
}
```

### Packed Parameters

When you need many parameters, pack them into a struct or encode multiple values into fewer parameters:

```csharp
// Pack width and height into a single int
int packedSize = width * 65536 + height;

// In kernel:
int width = packedSize / 65536;
int height = packedSize - width * 65536;
```
