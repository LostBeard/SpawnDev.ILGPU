# Wasm backend SIMD128 — Velocity-model port plan

**Date:** 2026-06-12 · **Owner:** Seven (Wasm backend) · **Status: PLANNED — starts on a fresh
session with full quota (Captain's directive 2026-06-12). Do not start piecemeal.**

## Goal and the Captain's two requirements
1. **SIMD where available:** emit wasm SIMD128 (`v128`, 0xFD-prefixed opcodes) from our own
   `WasmModuleBuilder` so kernel bodies execute 4×f32 / 4×i32 / 2×f64 / 2×i64 lanes per
   instruction.
2. **Non-SIMD devices remain FIRST-CLASS.** Captain runs real hardware without wasm SIMD
   (AMD Phenom II X6 1090T on Win10; Android phones where e.g. Firefox 118 on Android 9 lacks
   SIMD while Chrome has it — support is fractured). The scalar path is not a fallback to
   deprecate; it is a supported mode forever. Reference for the app-level dual-build technique:
   Captain's `D:\users\tj\Projects\BlazorWASMSIMDDetectExample` (wasm-feature-detect at Blazor
   startup → load SIMD or compat build). The BACKEND half is simpler than the app half because
   we compile kernels at RUNTIME: detect once, then per-kernel codegen selects the path —
   nothing about the .NET host build changes.

## Architecture: port the Velocity model, not loop auto-vectorization
Our fork already ships ILGPU's **Velocity** CPU-SIMD backend (`ILGPU/Backends/Velocity`,
`ILGPU/Runtime/Velocity`): a warp executes as vector LANES with masked execution for
divergence. That model — not scalar-loop auto-vectorization (fragile pattern matching) — is
the correct shape for GPU-style kernels:
- One Wasm fiber executes a **4-lane warp** (f32/i32; 2-lane for f64/i64): thread-invariant
  values stay scalar, lane-variant values live in `v128` locals.
- **Divergence = lane masks** (`v128.bitselect` merges), mirroring Velocity's IR-level
  masking; reconvergence points come from the same IR analysis Velocity uses.
- **Memory:** lane-wise loads/stores (`v128.load`/`v128.store` for contiguous lanes,
  per-lane extract/replace for gathers/scatters).
- **`Warp.Shuffle` becomes a real lane shuffle** (`i8x16.shuffle`) replacing today's
  shared-memory-exchange emulation (`WasmWarpSize=8` → revisit width vs 4-lane v128: either
  2×v128 per warp or warp width 4 in SIMD mode; decide in Phase 3 against the existing
  Warp tests' CPU oracle).

## Runtime detection + capability surface
- **Detection (REVISED 2026-06-14 per Captain): use `System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported`,
  NOT a hand-built probe module.** The insight: if the Blazor WASM app running the accelerator has
  SIMD enabled, SIMD is supported; if not, assume it isn't. `PackedSimd.IsSupported` is exactly that
  signal in-process — it is `true` only when the running .NET wasm build has SIMD enabled, and a
  SIMD-enabled build only loads/executes if the browser accepts v128 (else the whole app dies before
  our code runs). The Web Workers that instantiate our kernel modules share the same browser engine,
  so main-thread `IsSupported == true` ⇒ workers accept v128. This is the in-process equivalent of the
  app-level `wasmFeatureDetect.simd` check in Captain's `BlazorWASMSIMDDetectExample` (which picks the
  SIMD vs compat *build* at startup); our backend just reads which build won. No JS dependency, works
  worker-side. The compat build / SIMD-less browser / desktop CLR all report `false` → scalar codegen,
  byte-for-byte today's behavior. **SHIPPED** as `WasmBackend.RuntimeSupportsWasmSimd`.
- Expose `Capabilities.WasmSimd` (`WasmCapabilityContext.WasmSimd`) + `WasmAccelerator.SupportsSimd`
  (and wire `AcceleratorRequirements` if a consumer ever needs to require it). **SHIPPED.**
- Per-kernel: codegen consults the accelerator's detected capability; `WasmBackend.ForceScalar`
  forces the scalar path on SIMD hardware (mirrors `WebGPUBackend.ForceEmulatedF16`) and
  `WasmBackend.ForceSimd` forces the SIMD path for OFFLINE codegen verification (desktop CLR, where
  `IsSupported` is false) — so BOTH paths are testable on the dev machine. `EffectiveWasmSimd` =
  `!ForceScalar && (ForceSimd || RuntimeSupportsWasmSimd)`. **SHIPPED.**

## Phases (each gated; no phase starts until the previous gate is green)
- **Phase 0 — MEASURE (gate for everything):** profile the kernel-vs-host time split on
  representative Wasm workloads (RadixSort 1.4M, GEMM/FusedFFN, TurboQuant attention, one ML
  reference model). SIMD multiplies kernel ALU only; the known reference-model slowness is
  substantially interpreted-IL host overhead, which v128 does not touch. This phase names the
  workloads where 4× kernel ALU translates to real wall-clock and sets the A/B expectations.
  - **FIRST MEASUREMENT DONE (2026-06-14, Geordi) — ALU-sensitivity microbench.** A per-element
    kernel `out[i] = fold of R FMAs over in[i]` (fixed 1 read + 1 write; only register-ALU scales
    with R), N=256K, 10 iters, on the real Wasm backend: **R=1 = 35.5 ms, R=64 = 39.4 ms, R=256 =
    73.2 ms** → 256× more ALU = only **2.1×** time. So at R=1 (typical elementwise) ALU ≈ 0% of
    time; at R=256 (256 FMA/element) ALU ≈ 51%. There is a **~35 ms FIXED host/dispatch/memory/
    worker-round-trip floor** at this shape that SIMD128 cannot touch. **Refined go/no-go: SIMD128
    pays off ONLY on genuinely ALU-DENSE kernels (large GEMM, dequant-matmul / FusedDequantMatMul,
    the gemma4 decode hot path), and even there expect ~1.5-2× (NOT 4×) because ~half the time is
    the fixed floor. Light/elementwise kernels gain ~nothing — for those the lever is the dispatch/
    host overhead, not ALU.** This empirically confirms the "honest ceiling" below. NEXT Phase-0
    step (needs the machine): measure the SAME split on the ACTUAL hot kernels (FusedDequantMatMul,
    FusedFFN GEMM, RadixSort) to pick the 1-2 highest-intensity targets for the Phase-2 prototype.
    (Microbench was a one-shot — reverted from the tree; restore from this commit's history if needed.)
- **Phase 1 — emitter foundation: ✅ DONE (2026-06-14, Geordi).** v128 value type (`WasmOpCodes.V128`
  = 0x7B) + the 0xFD-prefixed SIMD sub-opcodes (spec-verified values, stored as `uint` because they're
  u32-LEB128 after the prefix — f32x4.add=228 etc. are multi-byte) in `WasmOpCodes`; emit helpers in
  `WasmModuleBuilder` (`EmitSimd`/`EmitSimdMem`/`EmitSimdLane`/`EmitV128Const`/`EmitF32x4ConstSplat`/
  `EmitI8x16Shuffle`); runtime detection (`RuntimeSupportsWasmSimd` via `PackedSimd.IsSupported`) +
  `ForceScalar`/`ForceSimd`/`EffectiveWasmSimd` + `WasmCapabilityContext.WasmSimd` +
  `WasmAccelerator.SupportsSimd`. Verified by `DemoConsole -- wasm-simd-probe` (`WasmSimdProbe.cs`): a
  hand-built v128 module **`wasm-validate`-clean** and **`wasm2wat`-decoded** to exactly the intended
  instructions (v128 local, splat/add/mul, v128.const, store/load, bitselect, i8x16.shuffle,
  extract/replace_lane, i32x4.add/neg). Purely additive — nothing in production codegen emits v128 yet,
  so the scalar path is byte-identical. PMT_FILTER=WasmTests gate + ship as a `-local` bump.
- **Phase 2 — one-kernel prototype (decision gate):** vectorize ONE hot kernel shape
  end-to-end behind the flag (the flat element-wise family or MatMul inner loop), CPU-oracle
  correctness in BOTH modes, A/B wall-clock on SIMD hardware at production sizes. **Proceed to
  Phase 3 only if the measured win on kernel-bound workloads justifies it** (Captain: time is
  a very limited resource).
- **Phase 3 — Velocity masked-warp port:** lane-mask divergence in
  `WasmKernelFunctionGenerator`, lane-wise memory ops, `Warp.*` on real shuffles, fiber/phase
  interaction audit (vector width is intra-fiber; the phase dispatcher and barrier model are
  untouched). Full `WasmTests` green in BOTH modes.
- **Phase 4 — dual-mode CI story:** PMT knob (`PMT_WASM_SIMD=off`) so sweeps can exercise the
  scalar path on SIMD hardware; the scalar path doubles as the cross-mode correctness oracle.

## Constraints + known interactions (from this backend's tribal knowledge)
- **Core SIMD128 only** — no relaxed-simd assumptions (availability is even more fractured).
- **Cross-mode determinism:** scalar and SIMD modes must produce identical f32 results
  (IEEE lane math is; do NOT emit fused-multiply-add in one mode only).
- **Verified-atomic-store machinery (`b0dfc5c`):** v128 stores have no atomic variants.
  Vector stores are legal only on non-shared/per-fiber regions; stores that today go through
  `EmitVerifiedAtomicStore` (barrier-kernel ordered data) must stay scalar-verified or
  decompose at the boundary. The UNIFORM STORE REGIMES LAW (`81b585b`) applies unchanged.
- **Emitter lessons:** keep vector loop bodies flat (the WebGL block-duplication explosions
  were branch-shape-driven; the Wasm emitter has its own duplication behaviors — probe sizes
  early).
- **Quota/scope discipline:** Phases are independent shippable increments; each lands with its
  own PMT gate. No cross-phase WIP left uncommitted.

## Why this is worth doing (and the honest ceiling)
Blazor WASM defaults to SIMD since .NET 8, so for default-config consumers the feature is
already required at the APP level — the backend just hasn't been cashing the check. Expected
wins concentrate in kernel-bound workloads (large sorts, GEMM, attention/dequant ALU);
host-bound graphs (per-node dispatch overhead) gain little until the host side is addressed
separately. Phase 0 exists to keep us honest about which is which before any emitter work.
