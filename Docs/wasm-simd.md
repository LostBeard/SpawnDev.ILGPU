# Wasm SIMD128 (v128) auto-vectorization

*(Since 4.15.0.)*

The Wasm backend automatically vectorizes kernels to **WebAssembly SIMD128** (the 128-bit `v128` instruction set). You write an ordinary ILGPU kernel; the backend generates a vectorized version alongside the scalar one and runs it when the engine supports SIMD. **No API change and no kernel change are required.**

## How it works

For every kernel, the Wasm backend emits **two** functions into the module:

- `kernel` — the scalar kernel (one thread-id per call). Unchanged, **byte-identical** to pre-4.15.0.
- `kernel_simd` — an additive vectorized variant that processes **four thread-ids per call** with `v128` instructions, plus a scalar tail for the remainder.

At dispatch the accelerator selects `kernel_simd` when the running engine supports SIMD, detected in-process via `System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported` (mirroring the app-level `wasmFeatureDetect.simd` build picker). Otherwise it runs the scalar `kernel`. So:

- **SIMD-less browsers, older devices, and the desktop CLR run the scalar path unchanged** — it is a supported, first-class mode, never a deprecated fallback.
- The vectorizer is **purely additive**: the emitter bails to the scalar path on anything outside its supported class, so it can never make a kernel slower-to-compile-or-wrong. A kernel either vectorizes cleanly or runs exactly as it did before.

Inside `kernel_simd`, values are classified as **lane-variant** (they differ per thread-id — e.g. `view[i]` indexed by the thread index) or **lane-invariant** (uniform across the 4 lanes — e.g. a kernel scalar parameter). Lane-variant values become `v128` lanes; lane-invariant values are computed once and `splat`-broadcast. `f64`/`i64` pack two lanes per `v128`, so they are *double-pumped* (two `v128` registers, each op runs twice).

## What gets vectorized

The full per-lane kernel class:

| Axis | Coverage |
|------|----------|
| **Element types** | `f32` (f32x4), `i32` (i32x4), `f64` (f64x2), `i64` (i64x2) |
| **Shapes** | straight-line elementwise · counted loops (v128 accumulator) · divergent `if`-diamonds (`cond ? a : b`) · gather (`src[idx[i]]`) · scatter (`o[idx[i]] = v`) · conditional / masked stores (`if (cond) o[i] = v`) · general acyclic divergent control flow (chained **and** nested selects) · divergent loops (a data-dependent branch inside a loop) |
| **Math** | `+ - * /`, min/max, neg/abs, sqrt, floor/ceil, all comparisons; transcendentals (sin, cos, tan, asin, acos, atan, sinh, cosh, tanh, exp, exp2, log, log2, log10); reciprocal & reciprocal-sqrt; pow, atan2, log_b — on **both f32 and f64** |

Data-dependent branches are handled by **if-conversion**: both sides execute speculatively and the result is selected per lane with `v128.bitselect` (a per-phi control-dependence mask tree for chained/nested cases). WebAssembly SIMD has no transcendental or gather/scatter instructions, so those run **per lane** (extract lane → the same scalar Math import the scalar path uses → replace lane) while the surrounding arithmetic stays vectorized.

## Cross-mode determinism (a hard invariant)

`kernel_simd` is **bit-exact** to the scalar `kernel` for the same inputs. This is enforced, not hoped for:

- No fused multiply-add (FMA would change rounding vs the scalar path).
- `f32 → i32` conversion stays scalar (WebAssembly SIMD only has *saturating* `trunc_sat`, while the scalar conversion traps — different observable behavior, so it is not vectorized).
- The per-lane Math fallbacks call the **identical** import the scalar path calls, so transcendentals match to the last bit.

Every SIMD feature ships with a gate asserting `kernel_simd` is emitted **and** `simd == scalar == reference` bit-exact, and the whole test suite is run a second time with SIMD forced off as a permanent cross-mode oracle.

## What stays on the scalar / multi-worker path (out of class by design)

- **Group / barrier / atomic / warp kernels** — these use the multi-worker shared-memory execution model, not the per-lane SIMD model. Unchanged.
- `f32 → i32` saturating convert (determinism, above).
- Kernels that call a **non-inlined** helper function.
- Narrow element types (`i8`/`i16`) — the current dispatch is by-4 (32-bit lanes).

These are not gaps to be "fixed" — they fall outside the per-lane v128 class and keep their existing, correct codegen.

## Controlling / inspecting it

```csharp
using SpawnDev.ILGPU.Wasm.Backend;

// Is the SIMD path active for new compilations? (true when the engine supports v128)
bool active = accelerator.SupportsSimd;              // == WasmCapabilityContext.WasmSimd
bool engine = WasmBackend.RuntimeSupportsWasmSimd;   // raw PackedSimd.IsSupported

// Test/verification overrides (mirror WebGPUBackend.ForceEmulatedF16):
WasmBackend.ForceScalar = true;   // force the scalar kernel (disable SIMD selection)
WasmBackend.ForceSimd   = true;   // force kernel_simd generation (offline codegen verification)
// EffectiveWasmSimd = !ForceScalar && (ForceSimd || RuntimeSupportsWasmSimd)
```

The flags read at compile time, so use a fresh `Context` per mode when A/B-comparing (the kernel cache keys on the effective flag).

## Performance note

The gain is bounded by the kernel's arithmetic intensity. ALU-dense kernels (e.g. FMA-heavy inner loops) see meaningful speedups; memory-bound / elementwise kernels are dispatch-bound and gain little — the fixed dispatch and memory floor dominates. The honest envelope is roughly **1.5–3× on ALU-dense kernels**; the Blazor benchmark demo's "Wasm SIMD128 — with vs without" card measures it live in the browser.

See also: [backends.md](backends.md), [limitations.md](limitations.md), [data-type-support.md](data-type-support.md).
