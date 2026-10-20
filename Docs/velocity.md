# Velocity (SIMD CPU backend)

Velocity is the desktop/server CPU backend that does real work. It compiles a kernel the way the GPU backends do (ILGPU IR to vectorized .NET IL through `System.Runtime.Intrinsics`), then runs it on all cores, four lanes per warp. The plain **CPU** backend stays what it always was, ILGPU's debugging backend: it runs the kernel's own IL one thread at a time, so you can step through it, and it is slow.

Velocity is what you get on a CPU by default. Register the plain CPU backend explicitly when you want to debug a kernel.

- **Desktop / server only.** It is not available in the browser (use the Wasm backend, which has its own SIMD128 path: [wasm-simd.md](wasm-simd.md)).
- **The default CPU backend.** `AllAccelerators()` and `AllAcceleratorsAsync()` register Velocity in place of the plain CPU accelerator. The plain CPU accelerator is not registered any more; add it yourself for debugging (`builder.DefaultCPU()` or `builder.CPU()`). Where Velocity cannot run (32-bit processes) the plain CPU accelerator is registered as before; the browser registers no CPU accelerator at all (its backends are WebGPU, WebGL and Wasm). A context with no registered accelerator still falls back to the default CPU device.
- Runs the whole shared SpawnDev.ILGPU test suite: **731 of 731 tests pass or are skipped** as capability gates (the `VelocityTests` lane; 677 pass / 0 fail / 56 skip).

## Using it

`AllAccelerators()` / `AllAcceleratorsAsync()` already register it (device type `Vector128`). To register it alone, or to pick a device type:

```csharp
using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using ILGPU.Runtime.Velocity;

using var context = Context.Create(builder => builder
    .Velocity(VelocityDeviceType.Vector128)   // four lanes per warp through Vector128<T> (SSE / AVX / AdvSimd)
    .EnableAlgorithms());                     // RadixSort, Scan, Reduce, ... (optional)

using var accelerator = context.CreateVelocityAccelerator(0);   // or context.GetVelocityDevice(0).CreateAccelerator(context)

// the same kernels and the same API as every other backend
var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(VectorAdd);
```

Device types:

| `VelocityDeviceType` | Lanes per warp | Notes |
|---|---|---|
| `Vector128` | 4 | The one to use. Software fallback on hardware without 128-bit vectors. |
| `Scalar2` | 2 | No SIMD vectors; a fallback that runs the same tests through a second set of operations (`VelocityScalar2Tests`, not in the default lane list). |

`builder.Velocity()` (no argument) registers both, in that order (Scalar2 is device 0).

### Selecting it with the other backends

`context.GetBestDevice()` ranks Velocity **below every GPU backend (CUDA, OpenCL, WebGPU, WebGL, Wasm) and above the plain CPU backend**, so a default context picks the GPU if there is one and Velocity otherwise. ILGPU's own `GetPreferredDevice(preferCPU: false)` also skips Velocity (it reports an unlimited memory size, which would otherwise win its sort by memory), and `GetPreferredDevice(preferCPU: true)` returns the Velocity device (then the plain CPU devices). `AcceleratorRequirements` treats it as a full-featured backend (atomics, shared memory, barriers, packed 4-bit stores, sub-groups, native f64 and i64); Float16 is emulated (`RequiresFloat16` passes, `RequiresFloat16Native` does not). See [capabilities-and-backend-selection.md](capabilities-and-backend-selection.md).

## What is supported

The same kernel language as the other backends:

- **Index types and launch shapes:** `Index1D/2D/3D`, explicit `KernelConfig` with 1D, 2D and 3D grids and groups (`Grid.Idx*`, `Group.Idx*`, `Grid.Dim*`, `Group.Dim*`); grids up to (int.MaxValue, 65535, 65535), groups up to **256 threads in total**.
- **Shared memory, barriers, group predicates** (`Group.Barrier`, `BarrierAnd/Or/PopCount`), `Group.Broadcast`, warp shuffles (`WarpSize` is 4 on `Vector128`), atomics, `[NoInlining]` helpers, early returns and loops with several exits.
- **ILGPU.Algorithms:** RadixSort, Scan, Reduce, Histogram and the group/warp operations (the Algorithms mapping registers Velocity implementations).
- **Types:** `Int8` through `Int64`, `float`, `double`, and the low-precision tier: `Half`, `BFloat16`, FP8 (`Float8E4M3`, `Float8E5M2`), FP4 (`Float4E2M1`) and the packed 4-bit integers `QInt4` / `QUInt4`, with in-kernel loads **and stores**. See [data-type-support.md](data-type-support.md).
- **Buffers are zero-initialized**, like on every other backend.

### How the low-precision types work

`Half`, `BFloat16`, FP8 and FP4 live in float lanes and always hold a value that is exactly representable in the narrow type: they are decoded from their bits when loaded, passed as kernel arguments or cast from integers, and rounded to the narrow type after every arithmetic operation and conversion. The conversions are the C# structs applied per lane, so the results are bit-identical to the CPU backend. `QInt4` / `QUInt4` are raw nibbles in integer lanes, extended on conversion. Views of the 4-bit types are packed two per byte; a nibble store is a compare-exchange on the containing 32-bit word, so neighboring threads can write the two nibbles of one byte at the same time.

## Groups, barriers and the preferred group size

A group of up to one warp is processed as one warp. A group of more warps **without barriers** is processed warp after warp on one thread. A group **with barriers** runs as a team, one thread per warp, with a lock-free barrier (about 0.5 to 0.8 microseconds while the team fits on the processors).

Velocity reports `MaxNumThreadsPerGroup` = **16**: the group size that algorithms and kernels pick when they ask the device. Larger groups are valid (up to 256 threads) but a barrier is only cheap while all warps of the team run at the same time, so bigger groups with barriers are slower: MEASURED radix sort of 1.4 M keys on a 12-processor PC: 16 threads 8.7 s, 32 threads 10.5 s, 64 threads 40 s, 256 threads 48 s. If you pick a group size yourself, 16 to 32 is the sweet spot for kernels with barriers; kernels without barriers can use any size.

## Performance

MEASURED on a PC with 12 logical processors, .NET 10 Release (full numbers and the method in `Plans/velocity-as-production-cpu-backend-2026-10-20.md`):

| Workload | CPU backend | Velocity (Vector128) |
|---|---|---|
| MatMul 512 x 512 x 512 | 93.6 ms | 42.6 ms |
| GEMV 1 x 2048 x 2048 | 4.72 ms | 1.90 ms |
| LayerNorm 1369 x 384 (one group per row, barriers) | 411.4 ms | 0.69 ms |
| Radix sort, 1.4 M keys | 7.8 s | 6.7 s (was 48 s before the barrier work) |
| `GlobalInclusiveScanAtScaleTest` | 131 s | 1 s |

The first three rows are ML kernels, 2 timed iterations, measured at the start of the Velocity work (identical results); the last two after the barrier work. Group-per-row kernels with barriers are where the plain CPU backend is pathologically slow (barriers between emulated threads); plain elementwise and matrix kernels gain the SIMD factor. Several test processes at once (PMT runs four CPU lanes in parallel) each want all processors and slow each other down.

## Limits and known gaps

- A method without a body (an internal call nothing implements) fails to compile instead of returning a wrong value.
- Groups above 256 threads are rejected at launch.
- The CPU backend's debugger-friendly behavior (stepping through the original IL) does not exist here: the kernel is compiled to different IL. Debug a kernel on the CPU backend, then run it on Velocity.
- 128-bit vectors only (`Vector256` / `Vector512` device types are not implemented).
- `Group.BarrierAnd/Or/PopCount` and the other group operations behave like CUDA on Velocity; on the plain CPU backend `BarrierAnd` after other predicate barriers is wrong for one thread, and OpenCL's `BarrierAnd` is wrong for an 8-thread group (found by the shared tests, not fixed there).

## Tests

`SpawnDev.ILGPU.DemoConsole/VelocityTests.cs` is the desktop test lane (`typeof(VelocityTests)` in `Program.cs`); `PMT_FILTER=VelocityTests PMT_LANES=VelocityTests dotnet test PlaywrightMultiTest -c Release` runs it alone (about 5 minutes). `VelocityScalar2Tests` is the same suite on the `Scalar2` device (register it in `Program.cs` to run it). Running one test as its own process is far faster for iteration than PMT: `SpawnDev.ILGPU.DemoConsole.exe VelocityTests.<TestName>`.

Where to look in the source: `ILGPU/Backends/Velocity` (code generator; `Vec128/` and `Scalar/` are the two specializers; `Vec128Operations.tt` and `ScalarOperations.tt` are T4 templates, edit the template and regenerate), `ILGPU/Runtime/Velocity` (accelerator, group teams and barriers).
