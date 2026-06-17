# 4-bit Data Types Plan — FP4 (E2M1), INT4, MXFP4, NF4

**Author:** Geordi · **Date:** 2026-06-17 · **Status:** DRAFT (Captain requested all four before the Wasm SIMD128 work).
**Context:** completes the low-precision data-type family. The existing per-element floats (Half, BFloat16,
Float8E4M3, Float8E5M2) are feature-complete + reference-validated (4.14.0-local.4). This plan adds the 4-bit
tier. Precedent to mirror: the FP8 core-type work (`ILGPU/Float8E4M3.cs` + per-backend emitters + radix +
capability), and the GGUF Q4_K/Q6_K dequant pattern for the block-quant formats.

## The foundational new piece: 4-bit (nibble) sub-word storage

bf16/FP8 reused the existing sub-word machinery, which bottoms out at **1 byte** (Int8/FP8 = 1 byte, 4 per
`u32`; Int16/Half/bf16 = 2 bytes, 2 per `u32`). **FP4 and INT4 are 4 bits = 8 per `u32`** — a NEW stride the
current load/store/LEA/atomic-RMW code does not handle. This nibble path is the shared prerequisite for both
FP4 and INT4 and must land first, on all 6 backends:

- **WebGPU (WGSL):** packed `array<atomic<u32>>`, 8 nibbles/word. Load = `atomicLoad` + shift `(idx%8)*4` +
  `& 0xFu`. Store = `atomicAnd` clear-nibble-mask + `atomicOr` set (thread-safe nibble RMW). Extends the
  existing 1-byte/2-byte sub-word switches (binding-type / body-LEA / direct-LEA / coalesce — the four sites
  the FP8 radix fix touched).
- **WebGL (GLSL):** `texelFetch` R32I + shift/mask nibble extract; Transform-Feedback nibble pack. The
  `BodyStructFieldInfoGL` sub-word path + `_subWord*Params` (just extended for FP8) need a 4-bit case.
- **Wasm:** `i32.load8_u` the byte + nibble shift/mask (no native 4-bit load); store = read-modify-write the
  byte. Inline bytecode like the FP8 path.
- **OpenCL:** `uchar*` storage + nibble shift/mask (no `vload` for 4-bit); RMW store.
- **CUDA (PTX):** `ld.global.u8` + nibble extract; `atom`/RMW store. Portable bit-manip, every arch.
- **CPU:** managed nibble pack/unpack in the struct.
- Decision: model as `BasicValueType.Int4`/`UInt4`/`Float4E2M1` (new IR primitives, append-only) with a
  **4-bit element size** the sub-word machinery keys on (new `SubWordElemSize`-style "half-byte" case). The
  `arrayLength()`-style element-count math (×8 per word) needs the same care as the FP8 radix `view.Length` fix.

**Risk/open:** atomic nibble RMW on WebGPU (two threads writing different nibbles of one word) — the existing
sub-word atomic AND/OR mask generalizes (4-bit mask instead of 8-bit), but verify no lost-update under the
barrier kernels. WebGL nibble scatter has the same one-store-per-thread caveat as FP8 (use the f32-widen
working representation for radix).

## Category A — per-element CORE types (my lane: core ILGPU + 6 emitters)

### 1. FP4 (E2M1) — `ILGPU.Float4E2M1`
1 sign / 2 exp / 1 mantissa, bias 1. **All 16 codes finite — NO Inf, NO NaN** (the OCP/NVFP4 element format).
Representable magnitudes: 0, 0.5, 1, 1.5, 2, 3, 4, 6 (× sign). Convert f32↔FP4 is a tiny RNE (a 16-entry
decode table or branchless bit-manip; overflow saturates to ±6 — confirm the convention vs the reference).
Full FP8 treatment: struct + `Float4E2M1.GenericMath.cs` (INumber) + `BasicValueType.Float4E2M1` IR primitive
+ per-backend convert on the nibble storage + radix keys (`Interop.FloatAsInt(Float4E2M1)` 4-bit, sign-flip
monotonic) + `AcceleratorRequirements.RequiresFloat4E2M1` + `FromSingle`/`FromSingleSaturating`.
**Oracle:** `ml_dtypes.float4_e2m1fn` (verify the exact dtype name/availability) → extend `bf16-f16-oracle`
to FP4 (all 16 codes exhaustively + a dense encode sweep). Pin in CI.

### 2. INT4 — `ILGPU.Int4` (signed) + `ILGPU.UInt4` (unsigned)
4-bit packed integers (signed −8..7 / unsigned 0..15) on the nibble storage — the Int8 sub-word model at 4
bits. Sign-extend on widen-to-i32 (the same `SignExtend` care the Int8/Int16 sub-word path needed). Radix
keys (plain integer ordering, sign-bias for signed). Capability flag. Likely no GenericMath (int sub-word,
not INumber-float) — match how Int8/Int16 are exposed.

## Category B — block-quant DEQUANT (ML layer; coordinate with Tuvok / SpawnDev.ILGPU.ML)

These are NOT per-element ArrayView types — they're packed blocks + shared metadata decoded by a dequant
kernel, exactly the GGUF Q4_K/Q6_K model ([[project-gemma4-decode-gemv-4.5x-2026-06-13]]). They reuse the FP4
decode (MXFP4) and a codebook (NF4). Lane: the dequant kernels live in the ML layer; I provide the FP4 decode
primitive + any core support, Tuvok wires the ML dequant-matmul. Confirm lane split with Tuvok before starting.

### 3. MXFP4 — FP4 E2M1 elements + one shared E8M0 (power-of-two) scale per 32-element block (OCP microscaling)
Dequant kernel: read 32 packed FP4 nibbles + the block's E8M0 scale → `fp4_decode(nibble) * 2^scale` → f32.
Reuses FP4 (#1) decode. **Oracle:** `ml_dtypes` MX types if present, else a NumPy reference. Block layout per
the OCP MX spec (verify block size = 32, scale = E8M0).

### 4. NF4 — 4-bit index → 16-entry NormalFloat codebook (normal-dist quantiles) + per-block absmax scale (QLoRA)
Dequant kernel: `codebook[nibble] * blockScale` → f32. The 16 NF4 codebook constants are fixed (QLoRA spec).
Pure software, every backend. **Oracle:** bitsandbytes / the published NF4 codebook + a reference dequant.
The plan calls NF4 the highest mission value (browser big-model compression, no HW dependency).

## Sequencing
1. **Nibble (4-bit) sub-word storage** on all 6 backends + a round-trip `ArrayView<Int4>` PMT gate. (Foundational.)
2. **FP4 (E2M1)** core type on the nibble storage + oracle (`float4_e2m1fn`) + radix + capability + pinned CI.
3. **INT4** (signed+unsigned) core type on the nibble storage + radix + sign-extend + CI.
4. **MXFP4** dequant (ML layer, reuses FP4 decode) — coordinate Tuvok.
5. **NF4** dequant (ML layer, codebook) — coordinate Tuvok.
Each phase: cross-backend PMT + external-reference oracle pin, shipped as its own `-local.N`, before the next.

## Verification standard (same as the existing 4 types)
External-reference oracle (ml_dtypes/NumPy/bitsandbytes) over all representable codes + encode probes;
cross-backend kernel equivalence; pinned-to-reference CI gate; radix grid (keys/pairs × asc/desc); no regression.

## Open questions for Captain
- FP4 overflow convention (saturate-to-±6 vs other) — confirm vs the reference once `ml_dtypes` dtype verified.
- INT4 as `INumber`? (Int8/Int16 are not exposed as INumber floats — match that, or add integer INumber?)
- MXFP4/NF4 lane: ML-layer dequant (Tuvok) with me providing the FP4 decode primitive — confirm split.
- Block sizes / exact specs (MXFP4 block=32 E8M0; NF4 block size) — verify against the canonical sources.
