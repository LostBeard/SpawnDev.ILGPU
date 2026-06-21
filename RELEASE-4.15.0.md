# SpawnDev.ILGPU 4.15.0

**Headline: the Wasm backend now auto-vectorizes kernels to WebAssembly SIMD128 (v128).**

A separate `kernel_simd` is generated alongside the scalar kernel and selected at runtime when the engine supports SIMD (`System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported`). The scalar path stays **byte-identical and first-class** — SIMD-less browsers, older devices, and the desktop CLR run it unchanged — and the emitter bails to the scalar path on anything outside its class, so the feature is **purely additive with zero regression**. A by-4 dispatch processes four thread-ids per `kernel_simd` call with a scalar tail.

## What vectorizes — the complete per-lane kernel class

- **Numeric tier:** `f32x4`, `i32x4`, and (double-pumped, 2 lanes per v128) `f64x2`, `i64x2`.
- **Shapes:** straight-line elementwise · counted loops (v128 accumulator) · divergent if-diamonds (mask + `v128.bitselect`) · **gather** · **scatter** · **conditional/masked stores** · **general acyclic divergent control flow** (chained *and* nested selects, via a per-phi control-dependence bitselect tree) · **divergent loops** (a data-dependent branch inside a counted loop).
- **Math (f32 and f64):** `+ - * /`, min/max, neg/abs, sqrt, floor/ceil, all compares; **transcendentals** (sin, cos, tan, asin, acos, atan, sinh, cosh, tanh, exp, exp2, log, log2, log10); **rcp/rsqrt**; **pow/atan2/log_b**.

**Cross-mode determinism is a hard invariant:** `kernel_simd` is bit-exact to the scalar kernel (no fused FMA; no saturating-vs-trapping convert; the per-lane Math fallbacks call the identical import). 34 `Wasm_Simd128_*` gates assert kernel_simd-is-emitted **and** `simd == scalar == reference` bit-exact, and the whole suite also runs in a SIMD-off CI mode as a permanent cross-mode oracle.

**Kept on the existing path (out of class by design):** group/barrier/atomic/warp kernels (the multi-worker shared-memory model), `f32 → i32` saturating convert (kept scalar for determinism), non-inlined helper calls, and narrow `i8`/`i16` element types.

## Also in 4.15.0

- **All in-register quant decoders are now single-exit / branchless** — `Float8E8M0`, `Float4E2M1`, `Float8E4M3`, `Float8E5M2` `RawBitsToFloat` (the subnormal-normalize `while` loops folded to a computed shift count; value-identical, verified bit-exact on all 6 backends). This fixes a WebGL/GLSL shader-size explosion: an early-return (multi-exit) decode inlined before a loop made the structurizer duplicate the loop continuation per exit arm, blowing past WebGL's compile limit (hit in MXFP4 dequant; the same class would hit MXFP8).

## Verification

Full PMT suite **3980 pass / 0 fail / 258 skip** across all six backends (CUDA, OpenCL, CPU, WebGPU, WebGL, Wasm) against the 4.15.0 bits. Forks bump to `2.0.42`.

---

🖖 The SpawnDev Crew — LostBeard (Captain), Riker, Data, Tuvok, Geordi, Seven.
