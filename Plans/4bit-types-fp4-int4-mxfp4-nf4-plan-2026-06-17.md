# 4-bit Data Types Plan — FP4 (E2M1), INT4, MXFP4, NF4

**Author:** Geordi · **Date:** 2026-06-17 · **Status:** DRAFT (Captain requested all four before the Wasm SIMD128 work).
**Context:** completes the low-precision data-type family. The existing per-element floats (Half, BFloat16,
Float8E4M3, Float8E5M2) are feature-complete + reference-validated (4.14.0-local.4). This plan adds the 4-bit
tier. Precedent to mirror: the FP8 core-type work (`ILGPU/Float8E4M3.cs` + per-backend emitters + radix +
capability), and the GGUF Q4_K/Q6_K dequant pattern for the block-quant formats.

## STORAGE MODEL DECISION (corrected 2026-06-17 after reading the IR size model)

**The IR type-size model is byte-granular and integer (minimum 1 byte).** `PrimitiveTypes.BasicTypeInformation`
sizes `Int1` as **4 bytes** (stored as i32!) and `Float8E4M3`/`E5M2` as 1 byte; `PrimitiveType.Size` is an `int`
(bytes) used pervasively for offsets/strides/alloca (`view[i]` offset = `i * Size`). A true 0.5-byte packed type
(8 per u32) does **not** fit this model without a deep allocation+offset-layer change (a separate "packed storage
size" threaded everywhere) — high risk, and NOT where ML actually needs the win.

**Therefore: the per-element core types `Float4E2M1` + `Int4`/`UInt4` use 1-BYTE storage (Size=1), exactly the
FP8/Int8 pattern** — the 4-bit VALUE in a byte. This reuses the existing 1-byte sub-word machinery wholesale (no
new nibble storage, no allocation change, no codegen surgery): `BasicValueType.Int4`/`UInt4`/`Float4E2M1` appended
(append-only), Size=1, wired exactly like FP8 (the IR core + 6 emitters already handle 1-byte sub-word).

**The 4-bit MEMORY SAVING lives in the MXFP4/NF4 DEQUANT layer, NOT the core per-element type.** MXFP4/NF4 store
PACKED nibbles (2 per byte) in raw `ArrayView<byte>`/`ArrayView<uint>` buffers, and the dequant kernel reads them
with shift/mask + scale/codebook → f32 — exactly the GGUF Q4_K model that already exists. That is where the 2×
compression matters for browser big-models, and it needs no core-type packing. (My earlier "nibble storage as
foundational core mechanism" framing was over-engineered — superseded by this.)

## FP4 (E2M1FN) reference spec — `ml_dtypes.float4_e2m1fn` (verified 2026-06-17, ml_dtypes 0.5.4)
16 finite codes, NO Inf, NO NaN:

| code | value | code | value |
|---|---|---|---|
| 0x0 | 0.0 | 0x8 | -0.0 |
| 0x1 | 0.5 | 0x9 | -0.5 |
| 0x2 | 1.0 | 0xA | -1.0 |
| 0x3 | 1.5 | 0xB | -1.5 |
| 0x4 | 2.0 | 0xC | -2.0 |
| 0x5 | 3.0 | 0xD | -3.0 |
| 0x6 | 4.0 | 0xE | -4.0 |
| 0x7 | 6.0 | 0xF | -6.0 |

Encode (RNE among the 16): `0.25→0x0` (tie→even 0), `0.75→0x2` (tie→even 1.0), `5.0→0x6` (tie 4/6→even 4),
finite overflow + **±Inf → saturate to ±6** (0x7/0xF), **NaN → 0x8 (-0)** (the format has no NaN; ml_dtypes maps
NaN→0x8 — match it for bit-exactness). 1-byte storage (value in the low nibble). Oracle harness: extend
`bf16-f16-oracle` to all 16 codes + an encode probe sweep, pin in CI like the other types.

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

## Sequencing (corrected — no foundational nibble phase; core types are 1-byte, FP8 pattern)
1. **FP4 (E2M1FN)** core type — `ILGPU.Float4E2M1`, 1-byte storage, the exact FP8 bring-up pattern: struct +
   conversion (CPU-verify vs `float4_e2m1fn` first) → `BasicValueType.Float4E2M1` IR primitive (append-only) +
   GenericMath → 6-backend convert (reuse the 1-byte sub-word machinery) → radix keys + capability + FromSingle/
   Saturating → `bf16-f16-oracle` extended to FP4 (16 codes + probes) + pinned CI + cross-backend PMT.
2. **INT4** (`Int4` signed −8..7 / `UInt4` 0..15) core type — 1-byte storage, the Int8 sub-word pattern +
   sign-extend on widen + radix + capability + CI.
3. **MXFP4** dequant (ML layer, Tuvok) — packed FP4 nibbles + `float8_e8m0fnu` block scale → f32; I provide the
   FP4 decode primitive, Tuvok wires the dequant-matmul (GGUF Q-format pattern). ml_dtypes has `float8_e8m0fnu`.
4. **NF4** dequant (ML layer, Tuvok) — packed 4-bit index + 16-entry NormalFloat codebook + block scale → f32.
Each phase: external-reference oracle pin + cross-backend PMT, shipped as its own `-local.N`, before the next.

## Verification standard (same as the existing 4 types)
External-reference oracle (ml_dtypes/NumPy/bitsandbytes) over all representable codes + encode probes;
cross-backend kernel equivalence; pinned-to-reference CI gate; radix grid (keys/pairs × asc/desc); no regression.

## Open questions for Captain
- FP4 overflow convention (saturate-to-±6 vs other) — confirm vs the reference once `ml_dtypes` dtype verified.
- INT4 as `INumber`? (Int8/Int16 are not exposed as INumber floats — match that, or add integer INumber?)
- MXFP4/NF4 lane: ML-layer dequant (Tuvok) with me providing the FP4 decode primitive — confirm split.
- Block sizes / exact specs (MXFP4 block=32 E8M0; NF4 block size) — verify against the canonical sources.
