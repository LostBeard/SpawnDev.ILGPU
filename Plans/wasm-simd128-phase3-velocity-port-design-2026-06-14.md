# Wasm SIMD128 — Phase 3 design: staged Velocity-model port

**Date:** 2026-06-14 · **Owner:** Geordi · **Status: DESIGN — green-lit by Captain after the Phase 2 gate.**
Parents: `wasm-simd128-velocity-port-plan-2026-06-12.md`, `wasm-simd128-phase2-design-2026-06-14.md`.
Phase 2 gate (Node, pure ALU): f32x4 gives ~3.8–4.2× on ALU-dense kernels, cross-mode deterministic,
correct → GO.

## The model being ported (studied: ILGPU `Backends/Velocity`, ~20k LOC)
ILGPU's Velocity backend is a **fully-vectorized, always-masked, if-converted** CPU-SIMD backend:
- A warp executes as vector LANES; **every** IR value is lowered to a vector (`Vec128/` = .NET
  `Vector128<T>` IL — 4×i32/f32, 2×i64/f64).
- Divergence is handled by **masks** (`Analyses/VelocityMasks.cs`): each block carries an active-lane
  mask; conditionals don't branch per-lane, they execute both sides under masks and merge
  (if-conversion). Loops use back-edge analysis to mask retired lanes.
- The CFG is **linearized** (`Transformations/VelocityBlockScheduling.cs`) so masked execution is
  well-defined (no real per-lane branching).
- Target is .NET IL `Vector128`; **we re-target the SAME model to wasm v128 opcodes** (Phase 1 emitter).

The always-masked model is the correct GENERAL solution but a large, high-risk rewrite of our scalar
per-IR-value Wasm emitter. So we STAGE it — each stage is a shippable increment with its own gate, and
the scalar path stays the cross-mode oracle throughout.

## Mapping onto the EXISTING Wasm fiber/phase dispatcher (do NOT rebuild it)
Vector width is **intra-fiber**; the phase dispatcher, barriers, and group model are UNTOUCHED (parent
plan constraint). Concretely:
- **Warp = 4 lanes = one v128** (f32/i32; 2 lanes for f64/i64). One Wasm fiber that today runs ONE
  thread runs, in SIMD mode, **4 consecutive thread-ids as 4 lanes**. `globalIdx`/`threadIdx` become a
  v128 of `(base, base+1, base+2, base+3)`; the fiber loop advances by 4.
- **Lane-invariant vs lane-variant** is the key classification (a uniformity analysis over the IR):
  values that do NOT depend on the per-lane thread-id (kernel params, group dims, K, loop bounds,
  block-uniform scales) stay SCALAR and are `f32x4.splat`/`i32x4.splat`'d only where they feed a vector
  op. Values that DO depend on the lane (the index, anything derived from it, per-lane loads) are v128.
  This avoids vectorizing everything (Velocity vectorizes all; we keep uniform values scalar = smaller,
  faster code, and it sidesteps mask machinery for uniform control flow).
- **Barrier/phase interaction:** a barrier kernel's shared-memory writes + tree reductions are
  barrier-ordered and go through `EmitVerifiedAtomicStore` / scalar — they STAY scalar (v128 has no
  atomic store; UNIFORM STORE REGIMES LAW). Only the lane-parallel compute region vectorizes.

## Stages (each gated; scalar path is the oracle in every stage)
- **Stage 3a — divergence-free / uniform-control-flow vectorization (FOUNDATION).** Kernels (or kernel
  REGIONS) whose control flow is lane-uniform: the body is straight-line or counted loops whose bounds
  are lane-invariant, with NO data-dependent (lane-variant) branches. Emit v128 for lane-variant values,
  scalar+splat for lane-invariant, contiguous `v128.load/store` for unit-stride lane memory, and the
  gather sequence (extract-lane addr → scalar load → replace-lane) for non-unit-stride/indexed loads.
  Covers the flat element-wise family (Scale/GELU/Add/…) AND the branchless dequant arithmetic in
  `FusedDequantMatMul` (the gemma4 decode hot path — its `Decode*Element` is branchless by design).
  Tails (N not a multiple of 4) handled by a scalar remainder loop (not masks). **First real-kernel
  target: a flat element-wise ILGPU kernel** end-to-end (real IR → v128) behind `EffectiveWasmSimd`,
  CPU-oracle correct in both modes, Node A/B at production N. Then the dequant-matmul decode.
- **Stage 3b — lane-mask divergence (if-conversion).** Port `VelocityMasks` for data-dependent branches:
  active-lane mask per region, execute both sides, `v128.bitselect` merge. Needed for kernels with
  lane-variant `if` (and clean handling of loop tails / bounds without a scalar remainder). This is the
  heavyweight piece; do it only once 3a proves the win on real kernels and a kernel actually needs it.
- **Stage 3c — Warp.* on real shuffles + memory.** Replace the shared-memory `Warp.Shuffle` emulation
  with `i8x16.shuffle` lane shuffles; lane-wise gather/scatter helpers; revisit `WasmWarpSize` (8 today
  via shared-mem) vs a 4-lane v128 warp (or 2×v128 for width 8) — decide against the existing Warp tests'
  CPU oracle.
- **Stage 3d — dual-mode CI.** `PMT_WASM_SIMD=off` knob so PMT exercises the scalar path on SIMD
  hardware; full `WasmTests` green in BOTH modes (the scalar path is the cross-mode oracle).

## Stage 3a implementation plan (starting now)
1. **Uniformity analysis:** classify each IR value lane-invariant vs lane-variant (variant iff it
   transitively depends on the thread index / a per-lane load). Conservative: unknown → variant.
2. **A SIMD codegen mode in `WasmKernelFunctionGenerator`** gated by `EffectiveWasmSimd` + a
   "vectorizable?" predicate (Stage 3a: no lane-variant branches, no barriers): lane-variant value →
   v128 local + `f32x4/i32x4` ops; lane-invariant feeding a vector op → scalar + splat; lane index →
   v128 `(base..base+3)`; unit-stride view load/store → `v128.load/store`; indexed load → gather.
3. **Fiber loop advances by 4** in SIMD mode; **scalar remainder loop** for the tail. Non-vectorizable
   kernels (have lane-variant branches / barriers in Stage 3a) fall back to the existing scalar emit —
   so nothing regresses and unsupported kernels just don't speed up yet.
4. **Gate:** real elementwise ILGPU kernel, CPU-oracle in both modes + Node A/B; then `FusedDequantMatMul`
   decode arithmetic. `WasmTests` green in both modes. Ship as `-local` bumps per stage.

## Constraints (unchanged)
Core SIMD128 only; cross-mode determinism (no one-mode FMA — wasm core has no v128 FMA, use mul+add to
match scalar); v128 stores non-atomic (barrier-ordered data stays scalar-verified; UNIFORM STORE REGIMES
LAW); non-SIMD devices FIRST-CLASS forever (scalar emit always present + is the oracle); probe vector
loop sizes early (emitter block-duplication behaviors).

## Honest risk note
This is the largest piece of the port. Stage 3a alone (real IR → v128 with a uniformity analysis) is a
significant change to a heavily-debugged 7.7k-line emitter. Mitigations: (a) opt-in per kernel via the
vectorizable predicate → zero scalar-path risk; (b) scalar mode is the always-on cross-mode oracle;
(c) Node A/B at each step keeps the win honest; (d) stages are independent shippable increments.
