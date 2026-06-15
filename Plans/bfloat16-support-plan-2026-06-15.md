# Plan: BFloat16 (bf16 / "Brain Float") Support for All Backends

**Author:** Geordi (from Captain's idea, 2026-06-15)
**Date:** 2026-06-15
**Status:** **APPROVED (Captain, 2026-06-15). COMPLETE on ALL 6 BACKENDS + verified (Geordi) — Phases 0-2b (CPU + WebGPU + WebGL + Wasm) + 3a (OpenCL) + 3b (native CUDA/PTX). Shipped `SpawnDev.ILGPU 4.13.0-local.2` (forks 2.0.18), master `1382a04`. Gates: `PMT_FILTER=BFloat16` all 6 lanes 28/0/0; `PMT_FILTER=Half` 181/0/8 (no f16 regression).**
- **Phase 3b (CUDA/PTX):** f32-register-compute model (PTX has no native bf16 arithmetic, only `cvt.*.bf16`):
  bf16 value→`.f32` register (RegisterTypeMapping/ParameterTypeRemapping/movement-remap); arithmetic+compare
  remap bf16→f32 at the `PTXInstructions` chokepoint; `ConvertValue` bf16↔f32 = register no-op; custom Load
  (`ld.global.b16`+`cvt.f32.bf16`) / Store (`cvt.rn.bf16.f32`+`st.global.b16`); bf16 constant emits the f32
  magnitude. Byte-identical round-trip to the emulated backends; native cvt on sm_80+, verified sm_89.
  *Remaining follow-ups (not blocking):* Wasm bf16-struct-field + `FloatAsInt`/`IntAsFloat` parity, capability
  flags (`Capabilities.BFloat16`/`BFloat16Native`, `RequiresBFloat16`), const-fold.
- **Phase 0 (CPU):** `ILGPU.BFloat16` core type + `BasicValueType.BFloat16` IR primitive. The CPU accelerator
  runs the managed struct directly. (Also fixed the ordinal-array regression this introduced on OpenCL/CUDA/Velocity.)
- **Phase 1 (WebGPU):** WGSL `_bf16_to_f32`/`_f32_to_bf16` emulation (always emulated; reuses f16's packed-u16
  sub-word storage via a parallel `_subWordBFloat16Params` set; threaded through type generator, constant
  emitter, all sub-word classification sites, and minimal-library inclusion).
- **Phase 2 (WebGL):** GLSL `_bf16_to_f32`/`_f32_to_bf16` emulation, mirroring WebGPU — packed-u16 in R32I texel,
  texelFetch load + Transform-Feedback varying store, same `_subWordBFloat16Params`/`IsBFloat16` pattern.
- **Phase 2b (Wasm):** `EmitBF16ToF32`/`EmitF32ToBF16` emit the conversion as inline WebAssembly bytecode
  (mirroring `EmitF16ToF32`/`EmitF32ToF16`); wired the `ArrayView<BFloat16>` load/store + type/size/constant maps.
  *Storage-first (plan §7):* load/store shipped; bf16-struct-field + `FloatAsInt`/`IntAsFloat` (radix-sort key)
  Wasm sites are a tracked follow-up for full f16 parity.
- **Verified:** `PMT_FILTER=BFloat16` → CPU 4/4 + WebGPU 4/4 + WebGPU-NoSubgroups 4/4 + WebGL 4/4 + **Wasm 4/4** PASS
  (20/0; round-trip, arithmetic w/ RNE f64 cross-check, min/max, range+specials incl. 1e30/1e-30 + NaN preservation).
  No f16 regression (`PMT_FILTER=Half` 183/0/8).
- **NEXT:** Phase 3 = native CUDA/OpenCL (OpenCL needs raw-ushort storage — no `vload_bf16` builtin) + the Wasm
  struct-field/FloatAsInt parity follow-up; capability flags + const-fold.
**Owner (implementation):** Geordi (SpawnDev.ILGPU / core ILGPU fork editor)
**Target version:** Floating — fold into the WebGPU-ML hardening lane (bf16 is most valuable exactly where the
ML push is pointed). Does not gate any current release.

**Sibling precedent:** [`Plans/f16-emulation-plan.md`](f16-emulation-plan.md) (the Float16/`Half` rollout —
SHIPPED on all 6 backends). **This plan mirrors it, but bf16 is simpler.** Read that first.

---

## 1. Goal

Add `ILGPU.BFloat16` as a first-class numeric type, native where the hardware supports it and **losslessly
emulated via f32 everywhere else** — exactly the `ILGPU.Half` model. `Capabilities.BFloat16` is always `true`;
`Capabilities.BFloat16Native` distinguishes the codegen path.

## 2. What bf16 is, and why it matters for AI

bf16 = **1 sign / 8 exponent / 7 mantissa** bits. The defining property: it is **the top 16 bits of an fp32**
— same 8 exponent bits, so it has **fp32's full dynamic range**, trading mantissa precision (~2-3 decimal
digits) for range.

Contrast `Half` (fp16): 1/5/10 — more precision, tiny range (max ~65504, underflow-prone). For ML the bf16
trade is the right one: **range beats precision.** fp16 overflows/underflows gradients and activations; bf16
spans fp32's range and doesn't. It's the de-facto ML format — TPUs, NVIDIA Ampere+, and a large fraction of
LLM/GGUF/HF weights ship in bf16. Supporting it directly serves the ML workload (load bf16 weights, halve
memory/bandwidth, convert to f32 for compute).

## 3. Why this is a *small* addition (simpler than Half)

Two reasons bf16 is cheaper than the Half rollout was:

1. **Conversion is trivial — no exponent rebias, no denormal/overflow branches.** bf16 IS truncated fp32:
   - **bf16 → f32 (EXACT):** `f32_bits = u32(bf16_bits) << 16; return reinterpret_f32(f32_bits)`. Zero-extend
     the mantissa. (Every bf16 value is exactly representable in f32 — same "lossless load" property f16 has.)
   - **f32 → bf16 (round-to-nearest-even + truncate):**
     ```
     bits = reinterpret_u32(f)
     if (isNaN(f)) return (bits >> 16) | 0x0040    // force a mantissa bit so NaN stays NaN
     lsb  = (bits >> 16) & 1
     bits = bits + 0x7FFF + lsb                     // RNE rounding bias
     return bits >> 16
     ```
     No exponent rebias (bf16 exp == f32 exp), so **none** of the f16 helper's denormal/overflow exponent
     surgery is needed. (A plain truncate `(bits >> 16)` is also acceptable as a v1; RNE avoids downward bias
     that hurts ML accumulation — recommend RNE.)

2. **It reuses Half's entire 2-byte sub-word storage path.** bf16 and f16 are both 16-bit elements; the
   packed-u16-in-u32 storage, atomic read-modify-write on store, texelFetch/load machinery, and struct layout
   are **identical**. Only the conversion helper at the load/store boundary differs (`_bf16_to_f32` vs
   `_f16_to_f32`). So the per-backend work is mostly "add the conversion helper + select it for bf16 elements."

## 4. The real lift: a distinct IR type in core ILGPU

The type system must distinguish a bf16 buffer element from an f16 one (same byte width, different
conversion). This is the main core change and where the design decision lives.

- **`ILGPU.BFloat16` struct** in the core fork, sibling to `ILGPU/Half.cs` — i.e. `ILGPU/BFloat16.cs`,
  `ILGPU/BFloat16.GenericMath.cs`, `ILGPU/BFloat16Conversion.cs`. Implicit conversion operators to/from `float`
  and `Half` (and interop with `System.Numerics.BFloat16` *if/when* .NET exposes one — do NOT assume it exists;
  ship our own struct like `ILGPU.Half`, add the interop operator only after verifying the BCL type).
  Kernel signatures use `ILGPU.BFloat16` (mirror the "use `ILGPU.Half`, not `System.Half`" rule).
- **IR representation — DECISION NEEDED (recommend new `BasicValueType.BFloat16`).** ILGPU's `BasicValueType`
  enum drives every backend's type switch (currently `Float16`/`Float32`/`Float64`). Options:
  - **(A) New `BasicValueType.BFloat16` (recommended).** Cleanest, mirrors `Float16` exactly. Cost: touches
    every `switch (basicValueType)` that handles `Float16` across all 6 backends + the IR. Mechanical but broad.
  - **(B) Represent bf16 as `Float16` + a "format" flag on the storage/view.** Smaller IR change, but the flag
    has to thread through to every load/store codegen site — error-prone and less honest. Not recommended.
  - **Arithmetic is promoted to f32 on every emulated path** (exactly like emulated `Half`): the IR only needs
    bf16 at the load/store boundary; all math happens in f32 locals. Native paths (CUDA bf16) can keep bf16
    arithmetic where the hardware has it.

## 5. Per-backend plan (mostly: add the conversion helper)

| Backend | Native bf16 | Emulated path | Notes |
|---|---|---|---|
| CUDA | Yes on Ampere+ (cc 8.0+, `__nv_bfloat16`) | f32 below cc 8.0 | `BFloat16Native` = (cc ≥ 8.0). PTX intrinsics for native compute; storage+convert otherwise. |
| OpenCL | `cl_khr_bf16` extension if device exposes it | `vload`/`vstore` + f32 (mirror the f16 `vload_half` pattern) | `BFloat16Native` = extension present. Lower priority (verify the exact ext name at impl time). |
| CPU | via our `BFloat16` struct / f32 | n/a | Reference for all equivalence tests. |
| **WebGPU** | No (WGSL has no bf16) | **Yes** — `_bf16_to_f32`/`_f32_to_bf16` WGSL helpers in `WGSLEmulationLibrary.cs`; reuse the existing `_subWordFloat16Params` packed-u16 storage + atomic-RMW store at the bf16 load/store sites | Mirror f16 tasks W1.1/W1.3/W1.4. Helpers are ~3 ALU ops (vs f16's ~5-8). |
| **WebGL** | No | **Yes** — `_bf16_to_f32`/`_f32_to_bf16` GLSL helpers in `GLSLEmulationLibrary.cs`; texelFetch load + Transform-Feedback uint store (mirror f16 W2.1-W2.3) | TF store outputs packed u16 bits, like f16. |
| **Wasm** | No | **Yes** — `EmitBF16ToF32`/`EmitF32ToBF16` in `WasmKernelFunctionGenerator.cs` (mirror `EmitF16ToF32` ~line 3828 / `EmitF32ToF16` ~3935, but just shifts) | Native `i32.load16_u`/`i32.store16` 2-byte storage already there. |

**Capabilities:** add `BFloat16` (always `true`) + `BFloat16Native` to each `*CapabilityContext.cs`
(`WebGPUCapabilityContext.cs`, `WebGLCapabilityContext.cs`, OpenCL `CapabilityContext.cs` — note the `.tt`
re-generation caveat the f16 plan flagged). Mirror `Float16` / `Float16Native`.

## 6. ML value (why fold it into the WebGPU-ML lane)

- **Memory + bandwidth:** bf16 weight storage halves footprint vs f32. LLM inference is usually
  memory-bandwidth-bound, so this is a real speed win, not just a size win.
- **Correct mixed-precision numerics:** the emulated path (load bf16 → compute f32 → store bf16) IS the
  standard ML mixed-precision recipe (bf16 storage, f32 accumulate). The "emulation" is the correct behavior,
  not a compromise.
- **GGUF/HF interop:** many checkpoints are bf16; a first-class bf16 storage type + cheap dequant-to-f32 slots
  straight into the ML loaders (coordinate with the ML lane / Tuvok).

## 7. Phasing

1. **Phase 0 — core type + IR.** `ILGPU.BFloat16` struct + conversion + `BasicValueType.BFloat16` (decision A)
   + CPU path. CPU-reference round-trip + arithmetic tests pass. (This is the gating lift.)
2. **Phase 1 — WebGPU emulation (storage + f32 compute).** Highest ML value. Mirror f16 Phase 1 with the
   trivial bf16 helpers, reusing the sub-word storage machinery.
3. **Phase 2 — Wasm + WebGL emulation.** (Deprioritized per Captain's "WebGL/Wasm last," but they're nearly
   free once the helper exists — reuse f16 storage paths.)
4. **Phase 3 — native compute:** CUDA `__nv_bfloat16` on Ampere+ (real speedup), OpenCL `cl_khr_bf16` if a
   device shows up.
5. **Phase 4 — algorithm-layer** (RadixSort/Scan/Reduce over bf16) via the same widen-to-f32 dispatch the f16
   Reduce used (f16 plan Phase 4) — bf16→f32, run the f32 op, convert back.

**Storage-first within each phase:** ship `ArrayView<BFloat16>` load/store + convert before native arithmetic.
That's most of the ML value at the lowest risk.

## 8. Test plan (mirror the Half suite, CPU reference is the bar)

`BFloat16BufferRoundTripTest`, `BFloat16ArithmeticTest`, `BFloat16MinMaxTest`, `BFloat16MixedTypeTest`, and a
`BFloat16RangeAndSpecialsTest` that specifically exercises bf16's headline property — **large-magnitude values
that fp16 cannot hold** (e.g. ~1e30, 1e-30), plus ±Inf / NaN / zero / the RNE rounding boundary. Each asserts
against a CPU `BFloat16` reference (Rule 1: real production path, CPU-reference compare). Algorithm-family tests
where the backend supports shared mem/barriers (WebGL skips those as it does for Half). Full cross-backend
equivalence — never CPU/console alone (the Wasm-only divergence lesson).

## 9. Risks / open questions

1. **`BasicValueType` extension breadth** — option A touches many backend type switches. Mechanical but wide;
   do it with a full cross-backend build + sweep, like any core IR change (`ILGPU/CLAUDE.md`: changes here hit
   all 6 backends).
2. **RNE vs truncate on f32→bf16** — recommend RNE (avoids downward bias in accumulation); confirm against how
   the ML reference (PyTorch/llama.cpp) rounds so dequant matches the oracle.
3. **NaN preservation on rounding** — the `+0x7FFF` RNE bias can turn a NaN into Inf; the NaN guard in §3 is
   required. Test it.
4. **`System.Numerics.BFloat16` availability** — do NOT assume the BCL has it. Ship `ILGPU.BFloat16`; add BCL
   interop operators only after verifying the type exists in the target framework.
5. **Struct field alignment** — bf16 fields in kernel structs need consistent CPU↔GPU 2-byte layout (same
   concern the f16 plan raised; reuse the resolution).

## 9b. Broader low-precision format family (bf16's neighbors) — Captain raised 2026-06-15

bf16 is the lead, but it sits in a family of low-precision ML formats. They split into **two categories that
live in two different places**, and getting that split right is the key architectural call:

### Category A — primitive IEEE-style float types → CORE ILGPU (the `Half`/`BFloat16` model)
Per-element floats (sign/exp/mantissa), packed into bytes, bit-converted to/from f32, native where HW supports.
Each is a core `ILGPU.*` struct + `BasicValueType` + per-backend convert helper — exactly this plan's model.

| Format | Bits (S/E/M) | Phase | Native HW | In ILGPU |
|---|---|---|---|---|
| **bf16** | 1/8/7 | train + inference | Ampere+, RDNA2+ | **this plan** (core type) |
| **FP8 E4M3** | 1/4/3 | forward / inference | Hopper+, Blackwell | core type `ILGPU.Float8E4M3` (1-byte; reuse `ArrayView<byte>` sub-word storage). NB: E4M3 has **no Inf** + a special NaN encoding — convert helper differs from IEEE. |
| **FP8 E5M2** | 1/5/2 | backward / gradients | Hopper+, Blackwell | core type `ILGPU.Float8E5M2` (IEEE-style Inf/NaN). **In scope — SpawnDev.ILGPU.ML supports TRAINING (Blazor WASM training demo exists), and FP8 training is the canonical E4M3-forward + E5M2-backward recipe, so both FP8 variants matter.** |

### Category B — block-quantization schemes → ML DEQUANT layer (the GGUF Q4_K model, NOT core types)
These are NOT per-element primitives — they're **packed blocks + shared metadata**, decoded by a dequant kernel
(read packed nibbles + per-block scale/codebook → f32). They belong with the existing GGUF Q4_K/Q6_K dequant
work in the ML layer ([[project-gemma4-decode-gemv-4.5x-2026-06-13]]), not in core ILGPU's type system.

| Format | Bits | What it is | Native HW | In ILGPU |
|---|---|---|---|---|
| **MXFP4** | 4.25 (FP4 E2M1 + shared block scale, OCP "microscaling") | next-gen throughput | Blackwell+ only | ML dequant kernel (block FP4 + scale → f32). Software dequant on everything we target. |
| **NF4** | 4 (NormalFloat: 16-entry codebook mapped to a normal dist + block scale; QLoRA) | "cold storage in VRAM," fit big models on consumer GPUs | none — software unpack | ML dequant kernel (4-bit index → codebook[idx]·scale → f32). Pure software, works on every backend. |

### The mission-aligned priority call (important)
**Native FP8/MXFP4 require Hopper/Blackwell — NOT browser GPUs and not consumer desktop GPUs.** On every target
we actually ship to (WebGPU/WebGL/Wasm + consumer CUDA/OpenCL/CPU), FP8/MXFP4/NF4 are all **software** (convert
or dequant), so the "native" column is mostly irrelevant to us today. That flips the intuition:

- **For browser + consumer-GPU inference (our mission — big models in Blazor WASM), the Category-B
  block-quant DEQUANT layer (NF4, MXFP4, and the GGUF Q-formats we already do) is the higher-value work** — it's
  what crams a 7B model into limited browser/VRAM memory, and it runs everywhere in software. NF4 especially
  (no HW dependency, QLoRA-grade compression) fits the "first-class big-model apps in the browser" vision.
- **bf16 (Category A) is the right first step** — base ML type for BOTH inference and **training stability**
  (SpawnDev.ILGPU.ML does training — Blazor WASM training demo), broadly native (Ampere/RDNA2 incl. many
  consumer GPUs), cheapest to add (this plan). **FP8 is a Category-A follow-on for BOTH passes** (E4M3 forward,
  E5M2 backward — the standard FP8 training recipe; both variants in scope since we train). Caveat: FP8's big
  win is faster matmuls on **native** FP8 HW (Hopper/Blackwell); on our browser/consumer targets FP8 is a
  storage/bandwidth play (8-bit store, f32 compute), so it ranks behind the block-quant compression for our
  near-term mission.

### Suggested sequencing
1. **bf16** (this plan) — core type, biggest near-term win (inference + training stability), cheapest.
2. **NF4 / MXFP4 dequant** in the ML layer — the "big models on consumer/browser GPUs" enabler; extends the
   existing GGUF dequant-matmul (gemma4 decode) pattern. (Likely Tuvok's ML lane + my dequant-kernel help.)
3. **FP8 E4M3 + E5M2** core types — the FP8 training/inference pair; biggest payoff on native FP8 desktop HW,
   storage/bandwidth benefit (emulated) elsewhere. Both ship together (FP8 training needs both).

(This roadmap section is informational; only bf16 §1-§9 is the approval-pending work item. The others are
captured so the family is on record and we sequence deliberately.)

## 10. References
- Sibling plan (mirror this): [`Plans/f16-emulation-plan.md`](f16-emulation-plan.md)
- Core type precedent: `ILGPU/Half.cs`, `ILGPU/Half.GenericMath.cs`, `ILGPU/HalfConversion.cs`
- Wasm conversion precedent: `SpawnDev.ILGPU/Wasm/Backend/WasmKernelFunctionGenerator.cs` `EmitF16ToF32` (~3828) / `EmitF32ToF16` (~3935)
- WGSL/GLSL emulation libs: `WebGPU/Backend/WGSLEmulationLibrary.cs`, `WebGL/Backend/GLSLEmulationLibrary.cs`
- Sub-word 2-byte storage machinery (reused as-is): `_subWordFloat16Params` in `WGSLKernelFunctionGenerator.cs` (~1289/3177/3821/4013/4200), `GLSLKernelFunctionGenerator.cs` (~555)
- Capability contexts: `WebGPU/WebGPUCapabilityContext.cs`, `WebGL/WebGLCapabilityContext.cs`, OpenCL `CapabilityContext.cs` (+ `.tt` regen caveat)
- Feature matrix to update on completion: main `CLAUDE.md` (add a BFloat16 row next to f16)
