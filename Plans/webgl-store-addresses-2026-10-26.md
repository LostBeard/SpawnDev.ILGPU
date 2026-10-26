# WebGL stores go to their real address (transform-feedback address varyings) - 2026-10-26 (Data)

## Why

A WebGL kernel writes its outputs through transform feedback (TF): each store SITE of an output view is one output varying,
and `glWorker.js` copies vertex `v`'s varying to element `v` (positional multi-store: `v * storeCount + slot`). The GLSL
generator computes the store's element address and then DROPS it (`GenerateCode(Store)` assigns the value to the varying;
the address variable is never read). Three silent-wrong cases follow (case 1 MEASURED by Geordi on 5.4.7-local.1; cases
2 and 3 read off the generated GLSL - the red tests below measure them before any change):

1. **Off-slot store.** `samples[blockSize + n] = x` lands at `samples[n]` (SpawnDev.Codecs `DecorrelateKernel`,
   FlacChannelDecorrelationGpu_LeftSide: samples[0], never written, came back changed; SilkBwexpanderGpu, FlacFixed /
   LpcReconstruct, SilkDecodeCoreGpu the same - Geordi 2026-10-26, now gated with `RequiresScatterStores`).
2. **Store not taken.** A thread that skips a conditional store leaves its varying unwritten (GLSL: undefined), and the
   readback still writes it over element `v`.
3. **One site, several addresses in one thread** (a store in a loop over elements, the FWHT butterfly / TurboQuant
   Normalize shapes that bit ML twice): TF keeps one value per site per vertex, so all but one are lost.

The compile-time guard tried on 2026-06-13 (refuse what is not provably own-slot) false-positived on 40 legitimate kernels
and was backed out (WebGL/CLAUDE.md "One-store-per-thread contract"). This design needs no such proof for correctness.

## Design

- **Address varying per store site**: next to each output varying `tf_out_P[_sS]`, a `flat out highp int tf_addr_P[_sS]`
  initialized to `-1` at the top of `main`. The store emits `tf_out = value; tf_addr = (tf_addr == -1 || tf_addr == idx) ?
  idx : -2;` where `idx` is the element index relative to the parameter view (the LEA's int pointer, which since 5.4.7
  includes in-kernel sub-view offsets). -1 = not stored, -2 = this site stored two DIFFERENT elements in one thread.
- **Readback** (`glWorker.js` Step 6): for an output with an address varying, read the address of vertex `v`: -1 -> skip
  (the element keeps its value: fixes case 2), >= 0 -> write the value at `writeOffset + addr * elemSize` (case 1; equal
  addresses from several vertices: the highest vertex wins, a race like on every GPU backend), -2 -> fail the dispatch
  with an error naming the kernel and parameter (case 3 stays unsupported, but LOUD). Bounds: an address outside the
  view's length fails the dispatch too (the GPU backends would write out of bounds).
- Same for the emulated 64-bit (lo/hi), sub-word (packed byte/short/Half/FP8) and struct-field paths: one address varying
  per store site, element-granular placement.
- **Fast path (performance only, never correctness)**: a store whose address is provably the vertex's own slot
  (`idx == _idx`, or positional `_idx * storeCount + slot` with constant slot) keeps today's varying layout and gets no
  address varying, BUT still needs the -1 / not-stored marker for case 2 unless the store is unconditional in `main`'s
  entry path. Wrong or missing proofs only cost one varying.
- **Limits**: address varyings count against `MAX_TRANSFORM_FEEDBACK_INTERLEAVED_COMPONENTS` (120 here, 64 guaranteed);
  `ValidateTransformFeedback` counts them, so a kernel that no longer fits is refused at load (loud), not truncated.
  MEASURE the Codecs WebGL kernels' varying counts before/after (gl_screen tool) - fast path first if many cross the limit.
- `RequiresScatterStores` stays as the selection gate for case 3; its doc changes from "scatter not supported" to "a
  store site writing several elements per thread is refused at dispatch".

## MEASURED cost of one address varying per store site (2026-10-26, GLSL of the 407 SpawnDev.Codecs kernels)

370 of 403 generated kernels fit the 120-component limit today; 31 of them (61..120 varyings: Vp8IntraPredict16x16 88,
Vp8FullLoopFilter 82, Av1Inverse2dTransform 117, WebPLossyFinish FinishHeader 118, ...) would NOT fit with one more varying
per site - mostly positional multi-store kernels (many sites, one output param). So per-site addresses are the FALLBACK,
not the default:

- **Per output PARAM: one base varying + one stored-mask varying.** Split every store site's element address (after the
  sub-view chain) into dynamic part + constant: `B + c_s`. When all sites of param P share the same dynamic part B (same IR
  value, or structurally equal), emit `tf_base_P` (init -1; at a store: `tf_base_P = (tf_base_P == -1 || tf_base_P == B) ?
  B : -2`) and `tf_mask_P` (init 0; site s stores: `tf_mask_P |= 1 << s`). The readback writes site s's value at
  `tf_base_P + c_s` for every set bit; -2 fails the dispatch. Own slot (B = idx), positional (B = idx * K), off-slot (B =
  k + idx) and conditional stores all take this form: +2 varyings per output param instead of +1 per site.
- Sites of one param with different dynamic parts (or more than 32 sites): per-site address varyings (fallback).
- A kernel that no longer fits is refused at load (loud). Re-measure the 31 after implementing.

## Tests (red today on WebGL, every backend)

- Off-slot store: thread n stores `buf[k + n]` (Geordi's DecorrelateKernel shape) and `buf[n]` is never written ->
  unchanged.
- Conditional store: `if (n % 3 == 0) out[n] = x;` -> the other elements keep their initial values.
- Positional multi-store and grid-stride loops (the 40 kernels of the 2026-06-13 false positives) still pass.
- One site writing two elements per thread -> WebGL refuses at dispatch with a clear error; other backends compute.
- Whole WebGL lane + Codecs WebGL families (Geordi) unchanged or better.
