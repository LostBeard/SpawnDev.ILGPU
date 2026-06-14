# Wasm SIMD128 — Phase 2 design (decision gate), leaning FusedDequantMatMul

**Date:** 2026-06-14 · **Owner:** Geordi · **Status: DESIGN (Phase 1 foundation shipped, master `a3b8dc3`).**
Parent plan: `wasm-simd128-velocity-port-plan-2026-06-12.md`. Phase-0 measurement + the foundation
(`WasmOpCodes` v128 ops, `WasmModuleBuilder` emit helpers, `RuntimeSupportsWasmSimd` detection,
`ForceScalar`/`ForceSimd`/`EffectiveWasmSimd`, offline `wasm-simd-probe`) are done and verified.

## What Phase 2 must answer (the decision gate)
Phase 2 is the go/no-go for the expensive Phase 3 (the generic Velocity masked-warp port). It must
produce ONE number: **the measured wall-clock win of v128 codegen on a kernel-bound Wasm workload at
production sizes**, with CPU-oracle correctness in BOTH scalar and SIMD modes. If the win is real,
Phase 3 is justified; if not, we stop with the foundation in place and no scalar-path risk taken.

Captain's lean: **FusedDequantMatMul** (the gemma4:12b decode hot path — `Kernels/FusedDequantMatMul.cs`
in SpawnDev.ILGPU.ML). This doc analyzes that target honestly, then scopes Phase 2 so the gate stays
decisive and low-risk.

## The target, analyzed: FusedDequantMatMul M=1 GEMV
At seq=1 decode (M=1) the hot kernel is `GemvDequant{Q4_K,Q6_K,Q8_0,Q4_0}Impl`: one thread group
(G=64) per output column `n`; each thread accumulates a strided-k partial then a shared-mem tree
reduction sums the group:

```
for (int k = tid; k < K; k += 64)
    partial += input[k] * DecodeQ4KElement(w, rowBase, k);   // <-- the ALU + the gather
sh[tid] = partial; Group.Barrier(); /* tree reduction */
```

The per-element work splits into THREE parts with very different SIMD friendliness:
1. **`DecodeQ4KElement` arithmetic** — branchless integer unpack (shift/mask of nibbles, int8 scale
   reconstruction, `HalfToFloatFinite`). **Vectorizable** — uniform control flow, pure ALU. This is
   the ALU density Phase-0 said SIMD can multiply.
2. **`DecodeQ4KElement` memory reads** — `ReadByte(w, ...)` at **data-dependent byte offsets** that
   differ per element (block base + nibble/scale sub-offsets). Across a vector of 4 elements these are
   a **GATHER**. **wasm SIMD128 has NO gather** — you simulate it with extract-lane(addr) → scalar
   `i32.load8_u` → replace-lane, i.e. 4 scalar byte loads per v128 lane group. This caps the win.
3. **`input[k]` read + FMA accumulate** — contiguous when the 4 vector elements are 4 consecutive `k`
   (a clean `v128.load` of `input[k..k+3]` + `f32x4` mul-add). Clean.

**Conclusion:** FusedDequantMatMul is vectorizable but it is a GATHER-HEAVY kernel. The decode
*arithmetic* benefits from v128; the decode *byte reads* do not (gather emulation). The realistic win
is the Phase-0 ceiling (~1.5–2× on the ALU-dense portion, diluted by the gather + the fixed ~35 ms host
floor), NOT 4×.

## Two vectorization AXES for the GEMV (both are really Phase 3)
- **Axis A — across-k within a thread:** one thread owns 4 consecutive `k` per step (`v128.load` of
  input, decode-4-consecutive-weights, `f32x4` FMA, horizontal-sum at the end). Problem: 4 *consecutive*
  Q4_K columns do NOT map to 4 consecutive bytes (the block layout interleaves low-then-high nibbles),
  so the decode is still a gather AND the per-thread restructuring changes the dispatch shape + needs a
  horizontal reduction. Messy.
- **Axis B — across-column (the Velocity model):** one Wasm fiber executes a 4-lane warp = 4 output
  columns; lane j computes column `n+j`. `input[k]` is **lane-invariant** (shared activation → scalar,
  `f32x4.splat`); the weights are lane-variant (gather 4 columns' bytes → v128); the decode arithmetic
  runs once across 4 lanes; `f32x4` FMA accumulates 4 columns at once; store 4 outputs. This reuses the
  shared activation and is the natural Velocity shape — but it IS the generic across-thread lane port,
  i.e. **Phase 3**.

Either axis to make the REAL kernel fast requires the IR-level lane model. The Wasm codegen
(`WasmCodeGenerator` + `WasmKernelFunctionGenerator`, ~7.7k lines) is today a **scalar per-IR-value
emitter** — one wasm local per IR value, `f32.add`/`i32.load`/etc. per `GenerateCode(BinaryArithmeticValue/Load/Store)`.
Vectorizing it = deciding per IR value "scalar (lane-invariant) vs v128 (lane-variant)" from a
divergence/uniformity analysis and emitting `f32x4.*` + masks for the variant ones. That is exactly the
Velocity port and exactly Phase 3 — too big to also be the decision gate.

## Phase 2 scope (RECOMMENDATION): prove the path on a CLEAN kernel, measure, decide
Do NOT vectorize FusedDequantMatMul in Phase 2. Its gather-heavy decode + the need for the IR lane
model would entangle the decision gate with the full Phase 3 build — and if the measured win turned out
marginal we'd have spent the Phase 3 cost to learn it. Instead, Phase 2 validates the **end-to-end v128
pipeline** (emit → detect → dispatch → readback → CPU-oracle → A/B) on the kernel shape that is BEST
case for SIMD and simplest to emit, so the gate number is clean and the codegen risk is near zero:

**Phase 2 prototype = a contiguous, uniform, ALU-dense element-wise kernel** — the Phase-0 microbench
shape `out[i] = fold of R f32 FMAs over in[i]` (1 contiguous read, R register FMAs, 1 contiguous write).
v128 form: `v128.load` 4 inputs → R× `f32x4` mul/add in a `v128` accumulator → `v128.store` 4 outputs.
No gather, no reduction, no divergence — the cleanest possible exercise of the foundation.

**How to emit it without touching the generic IR emitter (isolate the gate):** add a single
**hand-authored v128 kernel emitter** for this one shape, selected behind `EffectiveWasmSimd`, alongside
the existing scalar emission. Concretely a small `WasmSimdProbeKernel`-style path (extends the Phase-1
`wasm-simd-probe` builder) that the A/B harness dispatches at production N (256K–4M). This keeps Phase 2
a self-contained measurement, not a refactor of `WasmKernelFunctionGenerator`. The generic IR lane model
is deferred to Phase 3 where it belongs.

### Phase 2 deliverables
1. A vectorized + a scalar version of the FMA-fold kernel at matched R (e.g. R∈{1, 16, 64, 256}) and
   N∈{256K, 1M, 4M}, dispatched on the real Wasm worker pool.
2. **Correctness:** SIMD output == scalar output == CPU reference, exact for the integer/exactly-
   representable cases and within f32 ULP otherwise. **Cross-mode determinism:** identical f32 results in
   both modes — do NOT emit fused-multiply-add in one mode only (wasm core SIMD has no FMA; use
   `f32x4.mul` + `f32x4.add` to match scalar `f32.mul`+`f32.add`).
3. **A/B wall-clock** in both modes (toggle `ForceScalar`), reported as the gate number, with the split
   vs the fixed ~35 ms host floor called out (Phase-0 method: batched diff-output sync-once; throw the
   number — Console isn't captured in PMT).
4. **Decision:** proceed to Phase 3 (generic Velocity port → makes FusedDequantMatMul & all kernels
   vectorizable) ONLY if the ALU-dense win is large enough to justify it (Captain's call; time is scarce).

## When we DO vectorize FusedDequantMatMul (Phase 3 sketch, recorded now)
- Use **Axis B (across-column Velocity lanes)**: 4 output columns per fiber. `input[k]` → `f32x4.splat`
  (lane-invariant); the 4 columns' decode is a per-lane gather (extract-lane addr → scalar
  `i32.load8_u` → replace-lane) feeding **vectorized branchless decode arithmetic** (the win); `f32x4`
  FMA accumulates 4 columns; the group tree-reduction stays scalar (it's tiny vs the K-loop) or becomes
  4 parallel reductions.
- The gather is the ceiling: measure decode-arith-vectorized vs gather-bound on the Phase 2 number first.
- `DecodeQ4KElement` is already branchless (no data-dependent control flow) — ideal for lanes; no mask
  divergence needed inside the decode, only at the `n < N`/`k < K` bounds (handle via lane masks or a
  scalar tail, like Velocity).

## Hard constraints (carry from the parent plan)
- **Core SIMD128 only** (no relaxed-simd). **Cross-mode determinism** (no one-mode-only FMA).
- **v128 stores have no atomic variant** — vector stores only on non-shared/per-fiber regions; anything
  that goes through `EmitVerifiedAtomicStore` (barrier-kernel ordered data) stays scalar-verified. The
  GEMV's `sh[tid]` shared write + tree reduction is barrier-ordered → stays scalar. The UNIFORM STORE
  REGIMES LAW (`81b585b`) is unchanged.
- **Non-SIMD devices stay FIRST-CLASS forever** — every Phase 2 kernel keeps its scalar emission, which
  is also the cross-mode correctness oracle (`PMT_WASM_SIMD=off` / `ForceScalar`).
- Probe vector loop sizes early — the Wasm emitter has its own block-duplication behaviors (WebGL block
  explosions were branch-shape-driven; keep vector bodies flat).

## Open questions to resolve during Phase 2
1. Exact A/B harness home: extend `DemoConsole`'s probe path, or a `WasmTests` benchmark gated behind an
   env flag (PMT doesn't capture Console — return the number via a test result string / on-disk dump).
2. Whether the hand-authored Phase-2 kernel should reuse the real dispatch path (worker pool, memory
   layout) so the ~35 ms floor is measured honestly (it should — dispatch through `WasmAccelerator`).
3. The horizontal-sum primitive for any reduction path (extract-lane×4 + add, vs `i8x16.shuffle`-based
   pairwise) — measure both if a reduction is on the Phase 3 path.
