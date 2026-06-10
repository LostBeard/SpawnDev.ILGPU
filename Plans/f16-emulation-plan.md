# Plan: Float16 (Half) Emulation for All Backends

**Author:** Data (with Captain's approval from 2026-04-04)
**Date:** 2026-04-08
**Status:** **APPROVED 2026-04-22 by Captain — ACTIVE**
**Owner (implementation):** Geordi (SpawnDev.ILGPU editor)
**Target version:** Floating (lands on next rc after Riker's RTC/WebTorrent chain settles; does not gate 4.9.2-rc.7 nuget.org promotion of main ILGPU)

---

## Promotion Notes (2026-04-22)

- Captain approved promotion off DRAFT after Geordi's audit confirmed Phase 1 + Phase 2 are still unimplemented: `WebGPUCapabilityContext.cs:33` still conditional on `shader-f16`, `WebGLCapabilityContext.cs:16` still hardcodes `Float16 = false`, and `_f16_to_f32` / `_f32_to_f16` helpers are absent from both `WebGPU/` and `WebGL/` source trees.
- Work scope is additive — no existing code path changes for the native-f16 WebGPU case or the Wasm emulation case. New code only fires when `!HasShaderF16` (WebGPU) or `backend == WebGL`.
- Rule 1 applies. Every test that currently skips on `!Capabilities.Float16` must run and pass on the unlocked backend, with CPU-reference verification, before the emulation is declared done.

## Shipping status (2026-04-22)

- **Phase 1 (WebGPU `!shader-f16` emulation): SHIPPED** at commit `30eb12c`. 8/8 WebGPU Half tests green on native and emulated paths. WGSL dump of `HalfArithmeticKernel` confirms helpers emitted.
- **Phase 2 (WebGL emulation): SHIPPED** at commit `f391042`. 8/8 WebGL Half tests green. Existing inline load/store refactored to helper calls. `KernelUsesEmulatedTypes` extended to detect Float16 via IR scan so emulation library emits before param scan.
- **Phase 3 (OpenCL emulation): SHIPPED (code complete at HEAD)**. OpenCL already had `vload_half` / `vstore_half` emulation code for devices without `cl_khr_fp16`, but `Capabilities.Float16` tracked `cl_khr_fp16` — so tests skipped on such devices. Fix: `Capabilities.Float16 = true` always on OpenCL; new `Capabilities.Float16Native` tracks `cl_khr_fp16`; six gate points in `CLCodeGenerator.Views.cs`, `CLKernelFunctionGenerator.cs`, `CLTypeGenerator.cs` re-pointed from `!Float16` to `!Float16Native`. Manual edit to forked `ILGPU/Static/CapabilityContext.cs` — NOT in sync with `.tt`; re-apply if Transform All regenerates.
- **Phase 4 (`ILGPUReduceHalfTest`): SHIPPED** — `accelerator.Reduce<Half, AddHalf/MaxHalf/MinHalf>()` routed through a widen-to-f32 dispatch at the public entry point: per-workgroup reduction in Half, cross-workgroup atomic combine in f32 via existing `Atomic.Add/Max/Min(ref float)` infrastructure, convert back to Half at the end. **6 of 7 backend variants green** (CPU, CUDA, OpenCL, WebGPU, WebGPU-NoSubgroups, Wasm); WebGL legitimately skipped (no vertex shader atomics — same wall every `ILGPUReduce<T>` test hits). Count=2048 multi-workgroup dispatch verified. Key bug fix: temp buffers (`f32Input`, `f32Output`) allocated inside the helper must NOT be disposed via `using var` — WebGPU's deferred command encoder submits after the helper returns, and disposing mid-submit triggers `Buffer used in submit while destroyed`. Plain `var` lets GC reclaim after GPU completes.

---

## Goal

Enable `Float16 = true` on ALL backends, even when native f16 is unavailable. ML models increasingly use f16 for weights and activations - skipping Half tests means skipping real workloads.

## Why This Is Exact (Not Approximate)

Every f16 value is exactly representable as f32. The f16 format is a strict subset of f32:
- f16: 1 sign + 5 exponent + 10 mantissa (16 bits)
- f32: 1 sign + 8 exponent + 23 mantissa (32 bits)

Unlike Dekker f64 emulation (which is approximate), f16 emulation through f32 is **lossless**. There is zero precision cost.

## Current State (at authoring 2026-04-08 — SUPERSEDED; ALL rows below now SHIPPED, see "Shipping status" above)

> Updated to the shipped reality so this table can't be misread out of context. **f16 is supported on every backend** — emulated losslessly where native `shader-f16`/`cl_khr_fp16` is absent, so `Capabilities.Float16` is always `true` and `Capabilities.Float16Native` only distinguishes the codegen path.

| Backend | Native f16? | Emulated? | Float16 capability | Half tests |
|---------|------------|-----------|-------------------|------------|
| WebGPU (with shader-f16) | Yes | N/A | `true` | All pass |
| WebGPU (without shader-f16) | No | **Yes** (shipped `30eb12c`) | `true` | All pass (emulated) |
| WebGL | No | **Yes** (shipped `f391042`) | `true` | non-algorithm pass (emulated); algo Scan/Reduce skip (no shared mem/barriers) |
| Wasm | No | **Yes** (April 2026) | `true` | All pass |
| CUDA | Yes | N/A | `true` | All pass |
| CPU | Yes (.NET Half) | N/A | `true` | All pass |
| OpenCL | `cl_khr_fp16`-dependent | **Yes** (`vload_half`/`vstore_half`) | `true` | All pass |

## Reference Implementation: Wasm Backend

The Wasm backend already solves this problem. The pattern:

**Storage:** 2-byte memory elements (u16 bit patterns)
**Arithmetic:** All math in f32 locals
**Load:** `i32.load16_u` (2-byte read) -> inline IEEE 754 bit expansion -> `f32.reinterpret_i32`
**Store:** `i32.reinterpret_f32` -> inline IEEE 754 bit compression -> `i32.store16` (2-byte write)

Conversion algorithms in `WasmKernelFunctionGenerator.cs`:

### f16 -> f32 (EmitF16ToF32, line 3828)
```
sign = (h >> 15) & 1
exp  = (h >> 10) & 0x1F
mant = h & 0x3FF
if exp == 0:  result = sign << 31           (zero/denormal -> zero)
if exp == 31: result = (sign<<31) | (0xFF<<23) | (mant<<13)  (inf/nan)
else:         result = (sign<<31) | ((exp+112)<<23) | (mant<<13)  (rebias -15+127=112)
return reinterpret_f32(result)
```

### f32 -> f16 (EmitF32ToF16, line 3935)
```
bits = reinterpret_i32(f)
sign = (bits >> 31) & 1
exp  = ((bits >> 23) & 0xFF) - 112   (rebias 127-15=112)
mant = (bits >> 13) & 0x3FF          (truncate 23->10 mantissa bits)
if exp < 0:  exp=0, mant=0           (underflow to zero)
if exp > 31: exp=31                  (overflow to inf)
return (sign << 15) | (exp << 10) | mant
```

---

## Phase 1: WebGPU Emulation (when shader-f16 unavailable)

### 1a. WGSL Helper Functions

Add to `WGSLEmulationLibrary.cs` (following the f64/i64 emulation pattern):

```wgsl
fn _f16_to_f32(h: u32) -> f32 {
    let sign = (h >> 15u) & 1u;
    let exp  = (h >> 10u) & 0x1Fu;
    let mant = h & 0x3FFu;
    if (exp == 0u) {
        return bitcast<f32>(sign << 31u);
    }
    if (exp == 31u) {
        return bitcast<f32>((sign << 31u) | (0xFFu << 23u) | (mant << 13u));
    }
    return bitcast<f32>((sign << 31u) | ((exp + 112u) << 23u) | (mant << 13u));
}

fn _f32_to_f16(f: f32) -> u32 {
    let bits = bitcast<u32>(f);
    let sign = (bits >> 31u) & 1u;
    var exp  = i32((bits >> 23u) & 0xFFu) - 112;
    var mant = (bits >> 13u) & 0x3FFu;
    if (exp <= 0) { exp = 0; mant = 0u; }
    if (exp >= 31) { exp = 31; }
    return (sign << 15u) | (u32(exp) << 10u) | mant;
}
```

### 1b. Type Mapping Changes

In `WGSLTypeGenerator.cs`:
```csharp
// Current:
BasicValueType.Float16 => Backend.HasShaderF16 ? "f16" : "f32",
// Change to:
BasicValueType.Float16 => Backend.HasShaderF16 ? "f16" : "f32",  // arithmetic type unchanged
```
Arithmetic type stays `f32`. No change needed here - the promotion is already correct.

### 1c. Buffer Load/Store

The critical part. Half buffers contain packed 2-byte f16 values. When `shader-f16` is unavailable:

**Load (reading Half from buffer):**
- Buffer is bound as `array<u32>` (standard for WebGPU storage buffers)
- Element at index `i`: read `buf[i >> 1]`, extract `(val >> ((i & 1) * 16)) & 0xFFFF`
- Convert u16 bits to f32 via `_f16_to_f32()`

**Store (writing Half to buffer):**
- Convert f32 to u16 bits via `_f32_to_f16()`
- Read-modify-write the u32 at `buf[i >> 1]`
- For even index: `(existing & 0xFFFF0000) | half_bits`
- For odd index: `(existing & 0x0000FFFF) | (half_bits << 16)`

**Note:** The read-modify-write for stores needs atomic ops if multiple threads could write to the same u32. For ArrayView<Half> with stride 1, adjacent even/odd elements share a u32. If threads write to adjacent Half elements, this is a race. Solutions:
- Option A: Use `atomicOr` / `atomicAnd` for the read-modify-write (safe but slower)
- Option B: Pad storage to 1 Half per u32 (wastes 50% memory but eliminates races)
- Option C: Detect when writes are guaranteed non-overlapping (stride analysis)

**Recommendation:** Start with Option B (1 Half per u32) for correctness. Optimize to packed storage later if memory pressure demands it. This matches how Int8/Int16 are already promoted to full 32-bit storage.

### 1d. Capability Change

In `WebGPUCapabilityContext.cs`:
```csharp
// Current:
Float16 = enabledFeatures.Contains("shader-f16");
// Change to:
Float16 = true;  // Always true - native or emulated
```

Add a property to distinguish native vs emulated:
```csharp
public bool Float16Native { get; private set; }
// Set from enabledFeatures.Contains("shader-f16")
```

### 1e. Conditional Emission

In `WGSLKernelFunctionGenerator.cs`, add emulation path branching (similar to how f64 has `EnableF64Emulation`):
- If `HasShaderF16`: current code path (native f16)
- If `!HasShaderF16`: emit `_f16_to_f32`/`_f32_to_f16` calls at load/store boundaries

### 1f. Conversion Intrinsics

`GenerateConvertHalfToFloat` and `GenerateConvertFloatToHalf` already exist for native f16. Add emulated paths:
- HalfToFloat: `_f16_to_f32(source)` (source is already u32 bits from buffer load)
- FloatToHalf: `_f32_to_f16(source)` (result is u32 bits for buffer store)

---

## Phase 2: WebGL Emulation

### 2a. GLSL Helper Functions

Add to `GLSLEmulationLibrary.cs`:

```glsl
float _f16_to_f32(uint h) {
    uint sign = (h >> 15u) & 1u;
    uint exp  = (h >> 10u) & 0x1Fu;
    uint mant = h & 0x3FFu;
    if (exp == 0u) return uintBitsToFloat(sign << 31u);
    if (exp == 31u) return uintBitsToFloat((sign << 31u) | (0xFFu << 23u) | (mant << 13u));
    return uintBitsToFloat((sign << 31u) | ((exp + 112u) << 23u) | (mant << 13u));
}

uint _f32_to_f16(float f) {
    uint bits = floatBitsToUint(f);
    uint sign = (bits >> 31u) & 1u;
    int exp = int((bits >> 23u) & 0xFFu) - 112;
    uint mant = (bits >> 13u) & 0x3FFu;
    if (exp <= 0) { exp = 0; mant = 0u; }
    if (exp >= 31) { exp = 31; }
    return (sign << 15u) | (uint(exp) << 10u) | mant;
}
```

### 2b. Type Mapping

Already correct: `BasicValueType.Float16 => "float"` (promoted to f32 for arithmetic).

### 2c. Buffer Access via Transform Feedback

WebGL uses Transform Feedback for output. The load/store pattern differs from WebGPU:
- **Load:** Texel fetch from buffer texture. Read u32, extract f16 bits, convert.
- **Store:** Output f32 via Transform Feedback varying. The CPU-side readback would need to handle the f32->f16 conversion, OR store as u32 with packed f16 bits.

WebGL's constraint (no shared memory, no atomics, no barriers) simplifies the store race issue - each thread writes its own output via Transform Feedback, no read-modify-write needed.

### 2d. Capability Change

In `WebGLCapabilityContext.cs`:
```csharp
// Current:
Float16 = false;  // hardcoded
// Change to:
Float16 = true;   // emulated via f32 promotion + bit conversion
```

---

## Phase 3: OpenCL Emulation (if needed)

OpenCL has `cl_khr_fp16` for native Half. For devices without it, apply the same pattern. Lower priority since most OpenCL devices support f16 natively.

---

## Tests Unblocked

When emulation is enabled, these currently-skipped tests will run on all backends:

| Test | What It Exercises |
|------|------------------|
| AlgorithmRadixSortPairsHalfTest | Half sorting (barrier + multi-group) |
| AlgorithmExclusiveScanHalfTest | Half scan (shared memory + barriers) |
| AlgorithmInclusiveScanHalfTest | Half scan (shared memory + barriers) |
| AlgorithmAllReduceHalfTest | Half reduction (lock-free warp pattern) |
| AlgorithmGroupReduceHalfTest | Half group reduce |
| HalfBufferRoundTripTest | Load/store correctness |
| HalfEdgeCasesTest | Zero, denormal, inf, NaN handling |
| HalfMixedTypeTest | Half + int -> float kernels |

Plus `ILGPUReduceHalfTest` (currently unimplemented - blocked on Half atomics, but the lock-free AllReduce pattern already solves this).

---

## Implementation Order

1. **WebGPU emulation** (Phase 1) - highest value, ML workloads need this
2. **WebGL emulation** (Phase 2) - follows naturally from WebGPU work
3. **OpenCL** (Phase 3) - only if devices without cl_khr_fp16 are encountered
4. **ILGPUReduceHalfTest** - write the missing test using the lock-free pattern

---

## Work Item Breakdown (2026-04-22)

### Phase 1 — WebGPU Emulation (`!HasShaderF16`)

| ID | Task | File(s) | Acceptance |
|----|------|---------|------------|
| **W1.1** | Add `_f16_to_f32(h: u32) -> f32` and `_f32_to_f16(f: f32) -> u32` helper functions | `SpawnDev.ILGPU/WebGPU/Backend/WGSLEmulationLibrary.cs` | Functions emit in shader prelude when needed; ShaderDebugService dump shows them verbatim |
| **W1.2** | Confirm storage layout: **Option A (packed: 2 Halves per u32)** — matches the existing `_subWordFloat16Params` machinery at `WGSLKernelFunctionGenerator.cs:1289,3177,3821`. No buffer-sizing changes needed; same layout used for Int16 sub-word buffers. Atomic RMW at store keeps it thread-safe across adjacent-index writes. | Doc only | Plan reflects actual code state |
| **W1.3** | **Refactor existing inline `!HasShaderF16` load bit-conversion at `WGSLKernelFunctionGenerator.cs:4013-4026` to call `_f16_to_f32(rawU16Bits)`.** The existing inline logic has two latent bugs: (a) it mishandles f16 denormals (`exp==0, mant!=0` takes the "normal" branch producing wrong values) and (b) it maps `exp==31` (Inf/NaN) as if it were normal, producing huge finite numbers instead of Inf/NaN. Helper fixes both. | `WGSLKernelFunctionGenerator.cs:4013-4026` | Emits `_f16_to_f32(rawBits)` call; existing inline bit-math removed; Inf/NaN and denormals now correct |
| **W1.4** | **Refactor existing inline `!HasShaderF16` store bit-conversion at `WGSLKernelFunctionGenerator.cs:4200-4220` to call `_f32_to_f16(fVal)`.** The inline store is mostly OK but loses NaN on overflow (zeroes mantissa). Helper preserves mantissa bits on overflow so NaN propagates. Packed atomic RMW around the converted bits is unchanged. | `WGSLKernelFunctionGenerator.cs:4200-4220` | Emits `_f32_to_f16(fValue)` call; atomic RMW preserved; NaN correctly propagates |
| **W1.5** | Flip capability: `Float16 = true` unconditional; add `Float16Native` property for callers that care about native-vs-emulated | `SpawnDev.ILGPU/WebGPU/WebGPUCapabilityContext.cs` | `Float16 = true`; `Float16Native = enabledFeatures.Contains("shader-f16")` |
| **W1.6** | Verify `GenerateConvertHalfToFloat` / `GenerateConvertFloatToHalf` intrinsic handlers work in emulated path | `WGSLCodeGenerator.cs` + conversion test | Round-trip Half↔float intrinsics match CPU reference on non-native browser |
| **W1.7** | Run full Half test suite on WebGPU **without** `shader-f16` (force-disable the feature request in test harness); expect the 5 non-algorithm Half tests + 5 Algorithm Half tests to pass | `PlaywrightMultiTest` harness + backend config | 10/10 Half tests pass; ShaderDebugService dump confirms `_f16_to_f32` / `_f32_to_f16` emitted |
| **W1.8** | Zero-regression sweep with `shader-f16` **enabled** | Full suite | 3352/0/242 baseline holds |
| **W1.9** | Docs — update `WebGPU/CLAUDE.md` f16 section; update main `CLAUDE.md` Feature Matrix Float16 row | Docs | Accurate native-vs-emulated status |

**Phase 1 acceptance (definition of done):** On Chrome/Edge **with** `shader-f16`, all Half tests pass as before. On any WebGPU browser **without** `shader-f16` (or when the feature is deliberately suppressed), the 5 non-algorithm Half tests + 5 Algorithm Half tests run and pass with CPU-reference verification. Zero regressions on the 3352-test sweep. WGSL dumps in `_debugdump/wgsl/` show the helper functions emitted only when needed.

### Phase 2 — WebGL Emulation

| ID | Task | File(s) | Acceptance |
|----|------|---------|------------|
| **W2.1** | Add `_f16_to_f32(uint h) -> float` and `_f32_to_f16(float f) -> uint` GLSL helpers | `SpawnDev.ILGPU/WebGL/Backend/GLSLEmulationLibrary.cs` | Helpers emit in shader prelude when needed; dump in `_debugdump/glsl/` |
| **W2.2** | Half buffer **load** path: `texelFetch` from R32UI, bit-extract, route through `_f16_to_f32` | `GLSLKernelFunctionGenerator.cs` (search `_subWordFloat16Params` line ~555) | Shader reads packed u16 bits via texel fetch; Capture `TFloat16*.glsl` dump |
| **W2.3** | Half buffer **store** path: Transform Feedback varying as `uint`, packed f16 bits; CPU-side readback handles the u16 → bytes mapping | `GLSLKernelFunctionGenerator.cs` store path + `WebGLAccelerator.cs` readback | TF output buffer contains packed u16 values at the expected byte offsets |
| **W2.4** | Flip capability: `Float16 = true` | `SpawnDev.ILGPU/WebGL/WebGLCapabilityContext.cs:16` | `Float16 = true` (emulation always available) |
| **W2.5** | Verify the 5 non-algorithm Half tests on WebGL: `HalfBufferRoundTripTest`, `HalfArithmeticTest`, `HalfMinMaxTest`, `HalfEdgeCasesTest`, `HalfMixedTypeTest`. Algorithm Half tests remain **legitimately skipped** (WebGL has no shared memory / barriers — existing skip overrides stay) | `WebGLTests.cs` | 5/5 non-algorithm Half tests pass; algorithm overrides unchanged |
| **W2.6** | Zero-regression sweep on WebGL | Full WebGL suite | No regressions on existing tests |
| **W2.7** | Docs — update `WebGL/CLAUDE.md` and main `CLAUDE.md` Feature Matrix | Docs | Accurate emulation status |

**Phase 2 acceptance:** `HalfBufferRoundTripTest`, `HalfArithmeticTest`, `HalfMinMaxTest`, `HalfEdgeCasesTest`, `HalfMixedTypeTest` all pass on WebGL with CPU-reference verification. Algorithm-family Half tests continue to skip with their existing "requires shared memory + barriers" reason. Zero regressions on the rest of the WebGL suite.

### Phase 3 — OpenCL (`!cl_khr_fp16`)

Lower priority; same pattern as WebGPU/WebGL via OpenCL C helpers. Deferred until a device without `cl_khr_fp16` actually shows up in testing.

### Phase 4 — `accelerator.Reduce<Half>` multi-workgroup + `ILGPUReduceHalfTest`

**SHIPPED 2026-04-22.**

Approach: widen-to-f32 dispatch at the public `Reduce<T, TReduction>` entry. When T is Half, allocate temp f32 buffers, convert input Half→float via a kernel, run `Reduce<float, corresponding-float-op>` through the existing f32 atomic CAS infrastructure, convert the final f32 back to Half into the user's output slot. Lossless (f16 is a strict subset of f32 encoding). TReduction → float-op mapping: `AddHalf`→`AddFloat`, `MaxHalf`→`MaxFloat`, `MinHalf`→`MinFloat`.

| ID | Task | Landed |
|----|------|--------|
| **W4.1** | Widen-to-f32 dispatch in `ReductionExtensions.Reduce<T, TReduction>`. Not the AtomicApply refactor originally considered — turned out cleaner to intercept at the public entry instead of modifying `ScanReduceOperationsHalf`. | `ILGPU.Algorithms/ReductionExtensions.cs` |
| **W4.2** | `ILGPUReduceHalfTest` with count=2048 (multi-workgroup on every backend, in Half exact-integer range for Max). Max / Min / Sum variants. WebGL legitimately skips (existing `ILGPUReduce<T>` skip pattern). | `BackendTestBase.Tests2.cs`, `WebGLTests.cs` |
| **W4.3** | Buffer-lifetime fix: temp f32 buffers must NOT be `using var` — WebGPU's deferred command encoder submits after the helper returns, and disposing before submit completes triggers `Buffer used in submit while destroyed` (silent: WebGPU logs a warning but returns zero-valued data). Plain `var` lets GC finalize after GPU. Surfaced via the constant-7 widen diagnostic that revealed the bind-group pipeline validation warning. | `ILGPU.Algorithms/ReductionExtensions.cs` |

Verified: **6 of 7 backend variants green** (CPU, CUDA, OpenCL, WebGPU, WebGPU-NoSubgroups, Wasm). WebGL legitimately skips. Multi-workgroup dispatch exercised (count=2048 forces >1 workgroup on every backend). Max returns 2048 exact; Min returns 1 exact; Sum returns 2048 within 0.5% Half ULP tolerance. Full-solution Release build clean.

**Still-open Approach C** (proper `Atomic.CompareExchange(ref Half, Half, Half)` intrinsics in ILGPU core) is captured in `Plans/PLAN-Atomic-Half-Intrinsics.md` as a next-major-cycle design doc. Not needed for the shipping Half Reduce — the widen-to-f32 path is the forward answer.

### Scheduling / Dependencies

- **No dependency on Riker's RTC/WebTorrent chain.** This is pure ILGPU backend work. Can proceed in parallel.
- **No dependency on Data's VoxelEngine work.** Data can continue Phase B/C carving independently.
- **Does NOT gate main ILGPU 4.9.2-rc.7 nuget.org promotion.** Current rc.7 behaviour on `!shader-f16` browsers is capability=false, which is still accurate — it's just not as good as it could be.
- **Will go in next rc** (likely 4.9.3-rc.1 or 4.9.2-rc.8 depending on what else converges) so Captain can pace the release.

---

## Risks and Open Questions

1. **Buffer packing (u16 in u32):** Option B (1 Half per u32) wastes memory but guarantees correctness. For ML weight buffers (millions of f16 values), this doubles memory usage. May need packed storage with atomic RMW for production ML workloads.

2. **WebGL Transform Feedback:** Need to verify the f16 bit pattern survives the Transform Feedback output path. If TF only outputs float, we may need to output uint with the packed f16 bits.

3. **Struct fields containing Half:** Structs with Half fields need consistent layout between CPU and GPU. The CPU uses 2-byte Half; if GPU pads to 4 bytes, struct offsets break. Need careful alignment analysis.

4. **Performance:** f16 emulation adds ~5 ALU ops per load and ~8 per store. For compute-bound kernels this is negligible. For memory-bound kernels with many f16 loads/stores, measure before assuming overhead is acceptable (but Dekker f64 runs Mandelbrot at 90 FPS, so this should be fine).

---

## References

- Wasm reference implementation: `WasmKernelFunctionGenerator.cs` lines 3828-4011
- WebGPU native f16: `WGSLKernelFunctionGenerator.cs` (search `HasShaderF16`)
- f64 emulation pattern: `WGSLEmulationLibrary.cs`, `GLSLEmulationLibrary.cs`
- Half roadmap: `Notes/half-support-and-quality-roadmap.md`
- Memory file: `project_f16_emulation_roadmap.md`
