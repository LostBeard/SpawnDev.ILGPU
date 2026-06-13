# Limitations & Constraints

SpawnDev.ILGPU runs ILGPU across both Blazor WebAssembly and desktop environments. Each platform introduces specific constraints. This page documents all known limitations and their workarounds.

## Critical: No Blocking Calls

**The #1 rule of Blazor WASM:** Never block the main thread.

Blazor WebAssembly runs on a single thread. Blocking that thread prevents JavaScript promises from resolving, causing a permanent deadlock.

### ❌ Will Deadlock

```csharp
buffer.GetAsArray1D();               // DEADLOCKS — calls synchronous readback internally
var result = task.Result;            // DEADLOCKS — blocks on async result
task.Wait();                         // DEADLOCKS
task.GetAwaiter().GetResult();       // DEADLOCKS
```

### ✅ Correct Async Pattern

```csharp
// Synchronize() flushes commands to the backend (non-blocking, safe in WASM)
accelerator.Synchronize();

// SynchronizeAsync() flushes AND waits for completion
await accelerator.SynchronizeAsync();

// CopyToHostAsync() is the only way to read GPU data back to CPU
var results = await buffer.CopyToHostAsync<float>();
```

> **Rule:** Always propagate `async/await` through your entire call stack. Never use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on the main thread.

## `throw` Not Supported in Kernels

The WGSL and GLSL transpilers cannot translate the IL `throw` instruction. If any code in your kernel (or methods it calls) contains a `throw`, compilation will fail.

### Common Offenders

Many `System.Math` methods contain implicit argument validation with `throw`. All browser backends (WebGPU, WebGL, Wasm) include **throw-free redirects** that handle the most common cases automatically:

| Method | Contains `throw`? | Auto-redirected? | Notes |
|--------|-------------------|:----------------:|-------|
| `Math.Clamp(val, min, max)` | ✅ Yes | ✅ Yes | Remapped at the IL frontend to `IntrinsicMath.Clamp` (= `Max(Min(val, max), min)`) before inlining. Verified on all 6 backends. |
| `Math.Round(x)` | ✅ Yes | ✅ Yes | Redirected to throw-free wrapper |
| `Math.Truncate(x)` | ✅ Yes | ✅ Yes | Redirected to throw-free wrapper |
| `Math.Sign(x)` | ✅ Yes | ✅ Yes | Redirected to throw-free wrapper |
| `MathF.FusedMultiplyAdd` | ✅ Yes | ✅ Yes | Redirected to throw-free wrapper |
| `XMath.Rsqrt(x)` | ✅ Yes | ✅ Yes | Redirected to throw-free wrapper |
| `XMath.Rcp(x)` | ✅ Yes | ✅ Yes | Redirected to throw-free wrapper |
| `MathF.Sin(x)` | ❌ No | — | Safe to use directly |
| `MathF.Sqrt(x)` | ❌ No | — | Safe to use directly |
| `Math.Min(a, b)` | ❌ No | — | Safe to use directly |
| `Math.Max(a, b)` | ❌ No | — | Safe to use directly |

> **Auto-redirects**: Two layers handle problematic .NET methods. (1) `Math.Clamp` (and `Math.Min`/`Max`/`Abs`) are remapped at the IL frontend by `RemappedIntrinsics` to their throw-free `IntrinsicMath` equivalents **before** the body is inlined — this is the robust path and works on all 6 backends (a backend-only redirect fires too late, after the frontend already tries to lower the `throw`). (2) `RegisterMathIntrinsics()` in each browser backend redirects the remaining cases (`Math.Round`, `Math.Truncate`, `Math.Sign`, `MathF.FusedMultiplyAdd`, `XMath.Rsqrt`/`Rcp`) at codegen time. You can use `Math.Clamp`, `Math.Round`, `Math.Truncate`, and `Math.Sign` directly in kernels.

### General Rule

Avoid calling any helper method that might throw exceptions. If you're not sure, check the .NET source for the method — if it contains `throw new ArgumentException(...)` or similar, it won't work unless a redirect is registered for it.

## No Reference Types in Kernels

Kernels can only work with **value types** (structs, primitives). Reference types (`class`, `string`, arrays) are not supported.

| ❌ Not Allowed | ✅ Allowed |
|----------------|-----------|
| `string` | `int`, `float`, `double`, `long` |
| `class` instances | `struct` instances |
| `int[]` (managed array) | `ArrayView<int>` |
| `object` | Primitives and value-type structs |

## No `ref` / `out` on the Kernel Entry Point

The kernel's top-level signature must use only by-value parameters. Use `ArrayView<T>` for output:

```csharp
// ❌ Won't work — kernel entry point can't take ref/out
static void Bad(Index1D i, ref int result) { }

// ✅ Use a buffer
static void Good(Index1D i, ArrayView<int> result) { result[0] = 42; }
```

**Helper methods called from a kernel CAN use `ref` / `out` parameters** (4.9.2-rc.18+). See [`kernels.md` — Helper Methods and Inlining](kernels.md#helper-methods-and-inlining) for when to mark helpers `[MethodImpl(MethodImplOptions.NoInlining)]` to avoid the WGSL/GLSL shader compile cliff at large kernels.

## No Recursion

GPU hardware doesn't support call stacks. Recursive functions must be rewritten as iterative loops.

## Float Precision

### f32 (Default for GPU Backends)

WGSL and GLSL natively support 32-bit floats (`f32` / `float`). Using `float` and `MathF` in kernels gives native GPU precision and performance.

### f64 (Double Precision)

`double` is **not natively supported** on most GPU hardware. Both GPU backends provide software emulation, controlled by `F64EmulationMode`:

- **Dekker** (default): `vec2<f32>` — ~48–53 bits mantissa, fast
- **Ozaki**: `vec4<f32>` — full IEEE 754, ~2x slower
- **Disabled**: `double` promoted to `float` — max performance, loses precision

Emulated doubles work well for many use cases (fractals, scientific compute) but have performance overhead. For rendering and visual applications, use `F64EmulationMode.Disabled`.

> **Deep zoom limitation:** f32 precision limits useful Mandelbrot zoom to ~10⁶× magnification. Emulated f64 extends this significantly.

### NaN / Infinity Detection

GPU shader compilers may optimize away `val != val` self-comparisons or flush NaN during arithmetic. The WebGPU backend uses **bit-level detection** via `bitcast<u32>()` for reliable `float.IsNaN()` and `float.IsInfinity()` support. In kernels, always use `float.IsNaN(val)` and `float.IsInfinity(val)` — do not rely on manual patterns like `val != val` or `val * 0.0f != 0.0f`.

### i64 (Long / ULong)

`long` and `ulong` are always emulated as `vec2<u32>`. This cannot be disabled — ILGPU's IR uses Int64 for `ArrayView.Length` and indices, so i64 emulation is required for correctness.

## IL Trimming & AOT

ILGPU compiles kernels at runtime by reading .NET IL (Intermediate Language). Both trimming and AOT compilation will break this:

```xml
<PropertyGroup>
  <!-- REQUIRED: ILGPU needs IL reflection at runtime -->
  <PublishTrimmed>false</PublishTrimmed>
  <RunAOTCompilation>false</RunAOTCompilation>
</PropertyGroup>
```

## SharedArrayBuffer Requirements (Wasm Backend)

The Wasm backend uses Web Workers for parallel dispatch. `SharedArrayBuffer` is required for zero-copy data sharing between workers.

### Cross-Origin Isolation

The page must be served with these HTTP headers:

```
Cross-Origin-Opener-Policy: same-origin
Cross-Origin-Embedder-Policy: require-corp
```

### Automatic Setup

The demo includes `coi-serviceworker.js` which auto-injects these headers via a service worker — no server configuration needed for development.

### Fallback

Without `SharedArrayBuffer`, the Wasm backend still works but falls back to a single off-thread worker (no multi-worker parallelism).

## Backend Feature Availability

Not all ILGPU features work on all backends:

| Feature | WebGPU | WebGL | Wasm | Cuda | OpenCL | CPU (Desktop) |
|---------|--------|-------|------|------|--------|---------------|
| Basic kernels | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| 1D/2D/3D index | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Scalar params | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Struct params | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| SharedMemory | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Group.Barrier() | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Dynamic SharedMemory | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Group.Broadcast | ✅ | ❌ | ✅ | ✅ | ✅¹ | ✅ |
| Atomics | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| In-kernel scatter / multi-store per thread⁶ | ✅ | ❌ | ✅ | ✅ | ✅ | ✅ |
| Warp/Subgroup ops | ✅² | ❌ | ❌ | ✅ | ✅¹ | ✅ |
| f64 emulation | ✅ | ✅ | N/A (native) | N/A (native) | N/A (native) | N/A (native) |
| i64 emulation | ✅ | ✅ | N/A (native) | N/A (native) | N/A (native) | N/A (native) |
| ILGPU Algorithms | ✅⁴ | ❌³ | ⚠️⁵ | ✅ | ✅ | ✅ |

¹ Requires device subgroup support. OpenCL shuffle needs `cl_intel_subgroups` (Intel) or `cl_khr_subgroup_shuffle` + `cl_khr_subgroup_shuffle_relative` (NVIDIA/AMD). Dynamically detected.  
² Requires `subgroups` WebGPU extension  
³ Most algorithms require shared memory or atomics  
⁴ WebGPU: RadixSort, Scan, Reduce, Histogram fully supported and tested
⁵ Wasm: RadixSort, Scan, Reduce, Histogram fully supported with fiber-based phase dispatch and pure spin barriers at full `hardwareConcurrency` (v4.6.0). 249 pass / 0 fail / 3 skip.  
⁶ WebGL Transform-Feedback captures one output record per vertex at the thread's own slot (gather-only). A kernel writing a computed/arbitrary output index, or >1 element of one buffer per thread that isn't the consecutive `v*K+slot` layout, can't run in-kernel on WebGL. Declare `AcceleratorRequirements.RequiresScatterStores = true` to filter WebGL at selection time. (WebGL still scatters at the host/algorithm layer — e.g. RadixSort via render-to-texture.) A compile-time fail-loud guard for this is a tracked open item (`Plans/webgl-multistore-fail-loud-guard-plan-2026-06-13.md`).

## Browser Compatibility

| Browser | WebGPU | WebGL | Wasm |
|---------|--------|-------|------|
| Chrome 113+ | ✅ | ✅ | ✅ |
| Edge 113+ | ✅ | ✅ | ✅ |
| Firefox 128+ | ✅ (Nightly) | ✅ | ✅ |
| Safari 18+ | 🧪 Experimental | ✅ | ✅ |
| Mobile Chrome | ✅ (Android) | ✅ | ✅ |
| Mobile Safari | ❌ | ✅ | ✅ |

## Namespace Collision

SpawnDev.ILGPU uses the `SpawnDev.ILGPU` namespace, which can collide with the `ILGPU` namespace from the forked ILGPU library. When both are in scope, use the `global::` prefix:

```csharp
using SpawnDev.ILGPU;       // SpawnDev extensions
using global::ILGPU;         // Original ILGPU types
using global::ILGPU.Runtime; // Original ILGPU runtime
```

This is a known issue preserved for backward compatibility.

## Maximum Parameter Count

ILGPU supports up to ~19 kernel parameters. If you approach this limit, pack related values into structs:

```csharp
// ❌ Too many parameters
static void Bad(Index1D i, ArrayView<float> d, float a, float b, float c, float d2, ...) { }

// ✅ Pack into a struct
public struct Config { public float A; public float B; public float C; public float D; }
static void Good(Index1D i, ArrayView<float> data, Config config) { }
```

## Troubleshooting

### "Unknown hard error" dialog on Windows

If you see an "Unknown hard error" dialog when running ILGPU kernels on the CPU backend, this is caused by the **Windows Error Reporting** service being disabled.

**Fix:** Enable the Windows Error Reporting service:
1. Open Services (Win+R, type `services.msc`)
2. Find "Windows Error Reporting Service"
3. Set startup type to "Manual" or "Automatic"
4. Start the service

This allows Windows to handle process crashes properly instead of showing the raw error dialog. The underlying issue is typically an assertion failure in the CPU backend from out-of-bounds array access — check your kernel index calculations and buffer sizes.
