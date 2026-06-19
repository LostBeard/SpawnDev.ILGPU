# Data Type Support by Backend

Tracks verified support for all data types across the 6 backends.
Updated: 2026-06-19

> **6 backends, 7 test columns.** SpawnDev.ILGPU has **6 backends** - WebGPU, WebGL, Wasm (browser) and CUDA, OpenCL, CPU (desktop). The tables below carry a 7th column, **WebGPU NoSub**, which is not a separate backend: it is WebGPU run with the `subgroups` extension forced off, a distinct test lane that verifies the no-subgroups codegen path. So "all 6 backends" and "7 columns / 7 test lanes" both refer to the same surface.

**Legend:**
- [x] PASS - verified with unit tests (real data, real kernels, real verification)
- [ ] FAIL - tests exist, currently failing
- [!] KNOWN LIMITATION - architectural constraint, not a bug
- [-] NOT TESTED - no tests yet, status unknown
- [N/A] - not applicable to this backend

---

## Buffer Read (Load from ArrayView)

| Type | C# Type | Size | WebGPU | WebGPU NoSub | Wasm | WebGL | CUDA | OpenCL | CPU |
|------|---------|------|:------:|:------------:|:----:|:-----:|:----:|:------:|:---:|
| Int8 | sbyte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt8 | byte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int16 | short | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt16 | ushort | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int32 | int | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt32 | uint | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int64 | long | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt64 | ulong | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float16 | Half | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| BFloat16 | BFloat16 | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float8E4M3 | Float8E4M3 | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float8E5M2 | Float8E5M2 | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float4E2M1 | Float4E2M1 | 4b¹ | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| QInt4 | QInt4 | 4b¹ | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| QUInt4 | QUInt4 | 4b¹ | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float32 | float | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float64 | double | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |

¹ **Packed 4-bit:** `[PackedBits(4)]` - 2 nibbles/byte, 8 per 32-bit word, so an `ArrayView<T>` of N elements is **`ceil(N/2)` device bytes** (not N). The nibble decodes to f32 (or sign/zero-extends to int) in-register at the load; the data stays packed in the buffer. See the packed-4-bit section below.

## Buffer Write (Store to ArrayView)

| Type | C# Type | Size | WebGPU | WebGPU NoSub | Wasm | WebGL | CUDA | OpenCL | CPU |
|------|---------|------|:------:|:------------:|:----:|:-----:|:----:|:------:|:---:|
| Int8 | sbyte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt8 | byte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int16 | short | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt16 | ushort | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int32 | int | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt32 | uint | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int64 | long | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt64 | ulong | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float16 | Half | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| BFloat16 | BFloat16 | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float8E4M3 | Float8E4M3 | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float8E5M2 | Float8E5M2 | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float4E2M1 | Float4E2M1 | 4b¹ | [x] | [x] | [x] | [!]² | [x] | [x] | [!]² |
| QInt4 | QInt4 | 4b¹ | [x] | [x] | [x] | [!]² | [x] | [x] | [!]² |
| QUInt4 | QUInt4 | 4b¹ | [x] | [x] | [x] | [!]² | [x] | [x] | [!]² |
| Float32 | float | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float64 | double | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |

² **Packed 4-bit store is fail-loud on CPU + WebGL.** Writing one nibble means a read-modify-write of the enclosing 32-bit word; on the GPU backends that is an atomic word RMW (`atomicAnd` clear + `atomicOr` set). **WebGL** has no atomics, and the **CPU** managed-reference indexer cannot address a sub-byte element, so a packed-4-bit store on those two throws `UnsupportedKernelFeatureException` / `UnsupportedTestException` rather than silently corrupting the word. Store runs on **CUDA / OpenCL / WebGPU / Wasm**. (Load works on all 6 - decode is read-only.)

## End-to-End (Read + Kernel Process + Write)

| Type | C# Type | Size | WebGPU | WebGPU NoSub | Wasm | WebGL | CUDA | OpenCL | CPU |
|------|---------|------|:------:|:------------:|:----:|:-----:|:----:|:------:|:---:|
| Int8 | sbyte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt8 | byte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int16 | short | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt16 | ushort | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int32 | int | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt32 | uint | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int64 | long | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt64 | ulong | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float16 | Half | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| BFloat16 | BFloat16 | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float8E4M3 | Float8E4M3 | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float8E5M2 | Float8E5M2 | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float4E2M1 | Float4E2M1 | 4b¹ | [x] | [x] | [x] | [!]² | [x] | [x] | [!]² |
| QInt4 | QInt4 | 4b¹ | [x] | [x] | [x] | [!]² | [x] | [x] | [!]² |
| QUInt4 | QUInt4 | 4b¹ | [x] | [x] | [x] | [!]² | [x] | [x] | [!]² |
| Float32 | float | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float64 | double | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |

(End-to-end needs the store, so the 4-bit types inherit the CPU + WebGL store limitation ². The read + the in-register compute work on all 6.)

## Buffer RoundTrip (CopyFromCPU -> CopyToHostAsync, no kernel)

| Type | C# Type | Size | WebGPU | WebGPU NoSub | Wasm | WebGL | CUDA | OpenCL | CPU |
|------|---------|------|:------:|:------------:|:----:|:-----:|:----:|:------:|:---:|
| Int8 | sbyte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt8 | byte | 1B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Int16 | short | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| UInt16 | ushort | 2B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float16 | Half | 2B | [-] | [-] | [-] | [-] | [-] | [-] | [-] |
| BFloat16 | BFloat16 | 2B | [-] | [-] | [-] | [-] | [-] | [-] | [-] |

## Half Math Intrinsics

| Function | WebGPU | WebGPU NoSub | Wasm | WebGL | CUDA | OpenCL | CPU |
|----------|:------:|:------------:|:----:|:-----:|:----:|:------:|:---:|
| Abs | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Min/Max | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Clamp | [-] | [-] | [-] | [-] | [-] | [-] | [-] |

## BFloat16 Arithmetic / Min-Max (kernel-side, all compute as f32)

`ILGPU.BFloat16` carries fp32's full dynamic range (1 sign / 8 exponent / 7 mantissa - the top 16 bits
of an fp32), so values ~1e30 / ~1e-30 that `Half` cannot hold round-trip exactly. Verified end-to-end by
the 4 `BFloat16_*` tests (round-trip storage, `+ - * /` cross-checked vs the true f64 result with
round-to-nearest-even, min/max, and range + `±Inf`/`NaN`/zero/RNE-tie specials).

| Op | WebGPU | WebGPU NoSub | Wasm | WebGL | CUDA | OpenCL | CPU |
|----|:------:|:------------:|:----:|:-----:|:----:|:------:|:---:|
| Add/Sub/Mul/Div | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Min/Max | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| `(float)`/`(BFloat16)` convert | [x] | [x] | [x] | [x] | [x] | [x] | [x] |

## CopyFromJS (Browser-only: JS TypedArray/ArrayBuffer -> GPU)

| Type | C# Type | Size | WebGPU | Wasm | WebGL |
|------|---------|------|:------:|:----:|:-----:|
| Int32 | int | 4B | [x] | [x] | [x] |
| Float32 | float | 4B | [x] | [x] | [x] |

## Atomic Operations

See **[Docs/atomic-operations.md](atomic-operations.md)** for the complete per-operation support matrix.

| Type | C# Type | WebGPU | Wasm | WebGL | CUDA | OpenCL | CPU |
|------|---------|:------:|:----:|:-----:|:----:|:------:|:---:|
| Int32 | int | [x] | [x] | [!] Add only (vote TF) | [x] | [x] | [x] |
| UInt32 | uint | [x] | [x] | [!] Add only (vote TF) | [x] | [x] | [x] |
| Int64 | long | [x] Add/bitwise, [!] Min/Max/Exch/CAS | [x] | [!] | [x] | [x] | [x] |
| UInt64 | ulong | [x] Add/bitwise, [!] Min/Max/Exch/CAS | [x] | [!] | [x] | [x] | [x] |
| Float32 | float | [x] CAS loop | [x] CAS loop | [!] | [x] | [x] | [x] |
| Float64 | double | [!] | [x] CAS loop | [!] | [x] | [x] | [x] |

**[!]** = Throws `NotSupportedException` at kernel compilation time. See [atomic-operations.md](atomic-operations.md) for details.

---

## Implementation Summary

### Sub-word buffer access (Int8, UInt8, Int16, UInt16, Float16)

All sub-word types now have **complete Read/Write/EndToEnd support on all 6 backends** (7 test columns).

| Backend | Mechanism | Signed/Unsigned Detection |
|---------|-----------|--------------------------|
| **WebGPU** | `array<atomic<u32>>` + atomicAnd/atomicOr for Store, atomicLoad for Read. IEEE 754 f16<->f32 inline conversion for Float16. | `EntryPoint.Parameters[N].GetGenericArguments()[0]` CLR type check |
| **Wasm** | Native `i32.load8_s/u`, `i32.load16_s/u`, `i32.store8`, `i32.store16` opcodes. Float16 via EmitF16ToF32/EmitF32ToF16. | CLR type trace via `_generatorArgs.EntryPoint.Parameters` |
| **WebGL** | `texelFetch` from R32I texture, shift+mask extraction. TF output with sub-word packing in `glWorker.js`. Float16 via GLSL f16<->f32 bit manipulation. | `EntryPoint.Parameters[N]` CLR type check |
| **OpenCL** | Native types for Int8/UInt8/Int16/UInt16. Float16 via `vload_half`/`vstore_half` with tracked LEA base pointer. | Native type support |
| **CPU/CUDA** | Native sub-word support, no special handling needed. | Native |

### BFloat16 (bf16 / "brain float") buffer access

`ILGPU.BFloat16` + the `BasicValueType.BFloat16` IR primitive add a second 16-bit float that, unlike
`Half`, keeps **fp32's full dynamic range** (it is literally the top 16 bits of an fp32) - the right
trade for ML weights/activations where fp16's tiny range overflows/underflows. **Complete
Read/Write/EndToEnd support on all 6 backends.** The bf16<->f32 conversion is byte-identical across every
backend: `bf16->f32` is an exact zero-extend `<<16`; `f32->bf16` is round-to-nearest-even truncate with a
NaN-preservation guard. Values compute as f32 everywhere; only the storage is 2-byte.

| Backend | Mechanism |
|---------|-----------|
| **WebGPU** | Always emulated (no native WGSL `bf16`). Packed 2 bf16 per `array<atomic<u32>>` word (reuses f16's sub-word storage via a parallel `_subWordBFloat16Params` set); `_bf16_to_f32` / `_f32_to_bf16` WGSL helpers at the load/store boundary. |
| **Wasm** | `EmitBF16ToF32` / `EmitF32ToBF16` emit the conversion as inline WebAssembly bytecode; 2-byte `i32.load16_u` / `i32.store16` (atomic in barrier kernels). |
| **WebGL** | Packed-u16 in an R32I texel; `texelFetch` + shift/mask load, Transform-Feedback varying store; `_bf16_to_f32` / `_f32_to_bf16` GLSL helpers. |
| **OpenCL** | Emulated (no common native bf16 extension; `cl_khr_fp16` is fp16, not bf16). View params are `ushort*` (2-byte storage stride - a `float*` typedef silently corrupts), `_bf16_bits_to_f32` / `_f32_to_bf16_bits` OpenCL-C helpers + tracked LEA base pointer. |
| **CUDA** | **f32-register-compute model** (PTX has no native bf16 *arithmetic*): the value lives in an `.f32` register and computes as f32; arithmetic/compare route through the f32 tables; `ConvertValue` bf16<->f32 is a register no-op. **The bf16<->f32 conversion at the load/store boundary uses PORTABLE bit-manipulation (basic integer ops on EVERY CUDA arch), NOT the native `cvt.*.bf16`** - those `cvt` instructions are sm_80+ (Ampere) only, so the earlier native-cvt path failed to compile on pre-Ampere cards (Pascal sm_61 / Volta sm_70 / Turing sm_75). Load = `ld.global.u8`... no: `ld.global.b16` + zero-extend + `shl 16` + reinterpret (exact, bf16 = top 16 bits of fp32); store = RNE round + NaN-guard + `st.global.b16`. Byte-identical to every other backend. (4.13.0+; pre-4.13.0 used the sm_80 native cvt and broke on older cards.) |
| **CPU** | Native - the managed `BFloat16` struct runs directly (`DefaultILBackend`). |

### FP8 (`Float8E4M3` + `Float8E5M2`) buffer access

`ILGPU.Float8E4M3` and `ILGPU.Float8E5M2` add the two OCP 8-bit floating-point formats, each with the
`BasicValueType.Float8E4M3` / `Float8E5M2` IR primitive. **Complete Read/Write/EndToEnd support on ALL
6 backends.**

- **`Float8E4M3`** - 1 sign / 4 exponent / 3 mantissa, bias 7. The "E4M3FN" finite variant: **no
  infinities** (the only non-finite value is NaN at `0x7F`/`0xFF`), max finite magnitude **448**. The
  overflow convention is **selectable** (see the convention note below). The FP8 **forward / inference**
  format (one extra mantissa bit vs E5M2, at the cost of range).
- **`Float8E5M2`** - 1 sign / 5 exponent / 2 mantissa, bias 15. IEEE-754-style: **has infinities and
  NaNs** (like fp16 but with 8 fewer mantissa bits). The FP8 **backward / gradient** format (fp16-class
  dynamic range, which gradients need).

Like `Half`/`BFloat16`, FP8 uses the **f32-register model**: values compute as f32 in-register and are
converted to the 1-byte FP8 grid only at the load/store boundary, so accumulation stays full-precision
(matching how real FP8 tensor-core hardware accumulates). Unlike bf16 (a trivial top-16-bits shift), the
FP8 conversion needs exponent rebias (127 -> 7/15), round-to-nearest-even from 23 to 2/3 mantissa bits,
subnormal normalization, and the per-format specials. The conversion is **byte-identical across every
backend** (CPU-verified idempotence 0/256 for all representable values).

| Backend | Mechanism |
|---------|-----------|
| **WebGPU** | Always emulated. Packed **4 FP8 per `array<atomic<u32>>` word** (1-byte sub-word storage); `_e4m3_to_f32`/`_e5m2_to_f32` + inverse WGSL helpers at the load/store boundary. |
| **Wasm** | Conversion emitted as **inline WebAssembly bytecode** (`EmitFP8ToF32`/`EmitF32ToFP8`, the subnormal-normalize loop unrolled for bit-exactness); 1-byte `i32.load8_u` / `i32.store8` (verified-atomic in barrier kernels). |
| **WebGL** | Packed 4 FP8 per R32I texel; `texelFetch` + shift/mask load, Transform-Feedback varying store; `_e4m3/_e5m2` GLSL helpers. |
| **OpenCL** | Emulated as `uchar*` storage (1-byte stride); `_e4m3_bits_to_f32` / `_f32_to_e4m3_bits` (+ E5M2) OpenCL-C helpers + tracked LEA base pointer. |
| **CUDA** | f32-register model. The FP8<->f32 conversion is **inline PTX bit-manipulation** (branchless `setp`/`selp`, unrolled normalize) using only basic integer ops - FP8 has no portable native PTX cvt (`cvt.*.e4m3` is sm_89/Hopper only), so this works on every CUDA arch. Load = `ld.global.u8` + convert; store = convert + `st.global.u8`. |
| **CPU** | Native - the managed `Float8E4M3`/`Float8E5M2` structs run directly. |

> **Convention note (E4M3 overflow).** The conversion is **bit-exact** to the `ml_dtypes` reference (the
> impl PyTorch / JAX `float8_e4m3fn` share) - verified by `DemoConsole -- fp8-oracle`: decode 0/256,
> encode rounding/subnormal/overflow 0 divergences across 1099 probes, on all 6 backends. The overflow
> behavior is **selectable**, with the reference-matching `fn` convention as the default:
>
> | Entry point | Finite overflow (`\|x\|>464`) | ±Inf | Matches |
> |---|---|---|---|
> | `(Float8E4M3)x` cast / `FromSingleFn(x)` / `FromSingle(x, saturate: false)` — **DEFAULT** | → NaN | → NaN | **PyTorch / JAX / ml_dtypes `float8_e4m3fn`** (bit-exact) |
> | `FromSingleSaturating(x)` / `FromSingle(x, saturate: true)` | clamps to ±448 | → NaN | NVIDIA Transformer Engine saturating cast / OCP saturating-forward |
>
> The cast operator and the IR-level convert (so `PrecisionConvert` and the generic `INumber<T>` path too)
> are all `fn`. `449..464` round **down** to 448 under both conventions; the two differ only for `|x|>464`,
> which rounds up past the 448 slot (`fn` → NaN, saturating → ±448). Every *representable* value round-trips
> exactly. `FromSingleSaturating` is composed only of existing intrinsics (a bit-level finite check + the fn
> cast + a `>464` redirect), so it transpiles and is bit-exact on **all 6 backends** (PMT
> `Float8E4M3_FromSingleFn_OverflowToNaN`). Use the default for reference-matching ML (loading/comparing
> PyTorch FP8 checkpoints); use `FromSingleSaturating` when you want overflow clamped rather than
> NaN-poisoning a downstream reduction.
>
> `Float8E5M2` is IEEE-754-style (has ±Inf): overflow → ±Inf, bit-exact to `float8_e5m2` (decode 0/256,
> encode 723/723); its canonical NaN byte is `0x7F` (ml_dtypes uses `0x7E` - both are valid NaN patterns).

### Packed 4-bit types (`Float4E2M1` / `QInt4` / `QUInt4`)

Three **TRUE packed 4-bit** types - the real NVFP4 / INT4 memory layout, not a 1-byte placeholder. Each is marked `[PackedBits(4)]`, so an `ArrayView<T>` of N elements allocates **`ceil(N/2)` device bytes** (2 nibbles/byte, 8 per 32-bit word) - half the footprint of a 1-byte-per-value layout.

- **`Float4E2M1`** - the OCP **E2M1FN** 4-bit float (the NVFP4 / MXFP4 element format): 1 sign / 2 exponent / 1 mantissa, 16 codes with magnitudes `{0, 0.5, 1, 1.5, 2, 3, 4, 6}`, **no Inf, no NaN**. `float`→FP4 is round-to-nearest-even among the 16 codes; finite overflow and ±Inf **saturate to ±6**; NaN encodes to `-0` (code `0x8`). Bit-exact to `ml_dtypes.float4_e2m1fn`.
- **`QInt4`** - signed packed 4-bit integer, range **-8..7**, sign-extends to `int` on read.
- **`QUInt4`** - unsigned packed 4-bit integer, range **0..15**, zero-extends to `int` on read.

Like the other low-precision types, values compute in a wider register (f32 for FP4, i32 for the ints) and the nibble is decoded on load / encoded on store; the buffer stays packed.

| Backend | Load (all 6) | Store (CUDA/OpenCL/WebGPU/Wasm) |
|---------|--------------|----------------------------------|
| **WebGPU** | `array<atomic<u32>>`; `atomicLoad` the word, shift by `(i&7)*4`, mask `0xF`, then the E2M1 decode (`_e2m1_to_f32`) or int sign/zero-extend. | Atomic word RMW: `atomicAnd` clears the target nibble, `atomicOr` writes the new one (disjoint masks compose - thread-safe). |
| **Wasm** | Load the byte at `addr>>1`, shift `(addr&1)*4`, mask `0xF`, decode (`EmitFP4ToF32`). | Atomic nibble RMW (mirrors the QInt4 word-RMW path). |
| **CUDA** | `ld.global.u8` the byte, `shr` by the nibble shift, mask, inline-PTX decode (`EmitFP4BitsToF32`). | Atomic word RMW (`red.and` clear + `red.or` set). |
| **OpenCL** | `(b[i>>1] >> ((i&1)<<2)) & 0xF`, then `_e2m1_bits_to_f32`. | `_qint4_store(base, i, nibble)` word RMW. |
| **WebGL** | `texelFetch` the R32I word, shift/mask the nibble, decode. **Load only.** | **Fail-loud** - no atomics (`UnsupportedKernelFeatureException`). |
| **CPU** | The managed struct's packed indexer decodes the nibble directly (`DefaultILBackend`). **Load only.** | **Fail-loud** - the managed reference indexer cannot address a sub-byte element. |

**Working with packed buffers from the host.** There is **no transparent typed host pack/unpack** for packed types (the host element is still a 1-byte struct, value in the low nibble). To upload, pack two nibbles per byte yourself and write the raw bytes:

```csharp
// Pack N FP4 codes (each a Float4E2M1.RawValue, 0..15) into ceil(N/2) bytes
var packed = new byte[(n + 1) / 2];
for (int k = 0; k < packed.Length; k++)
    packed[k] = (byte)((codes[2*k] & 0xF) | ((2*k+1 < n ? codes[2*k+1] & 0xF : 0) << 4));

using var buf = accelerator.Allocate1D<Float4E2M1>(n);                 // ceil(N/2) device bytes
((IContiguousArrayView)buf.View.BaseView).AsRawArrayView().CopyFromCPU(packed);
// ... dispatch a kernel that reads buf as ArrayView<Float4E2M1> (decodes in-register) ...
// read back the raw packed bytes and unpack:
var got = await ((IContiguousArrayView)buf.View.BaseView).AsRawArrayView().CopyToHostAsync<byte>();
```

**`RawBitsToFloat` - kernel-safe in-register decode of raw quant bits.** When you hold packed quant data as raw integer words (e.g. a GGUF/MXFP4 block of `u32`s) and want to decode an element inside a kernel without an `ArrayView<Float4E2M1>`, call `<Type>Extensions.RawBitsToFloat(int rawBits)`. It transpiles on **all 6 backends** and returns the f32 value for a raw nibble/byte/ushort pattern. Available for the float types whose storage is sub-word: **`Float4E2M1`, `Float8E4M3`, `Float8E5M2`, `BFloat16`** (`Half` has `FromRawBits`/`RawValue` but no `RawBitsToFloat`; the integer `QInt4`/`QUInt4` use the ordinary `(int)`/`(float)` convert). Host-side, the float types also expose `FromRawBits(...)` + a public `RawValue` for raw round-trips; **`QInt4`/`QUInt4` keep `RawValue` internal and have no `FromRawBits`** (pack via the nibble arithmetic above).

**Radix-sort** (keys + key/value pairs, ascending + descending) works for all three 4-bit types on **CUDA / OpenCL / WebGPU / Wasm** (the packed scatter needs the store path, so CPU + WebGL are gated, like the store). `RadixSortOperations.{Float4E2M1,QInt4,QUInt4}` provide the key transforms; the FP4 key is the E2M1 bit pattern with the float sign-flip, the int keys are the nibble with the signed/unsigned bias.

**Capability flag.** `AcceleratorRequirements.RequiresFloat4E2M1` gates selection to backends that support the FP4 type. There is **no** `RequiresQInt4`/`RequiresQUInt4` and **no** packed-store capability flag yet - a kernel that *stores* a packed-4-bit view on CPU/WebGL relies on the fail-loud throw, not a selection-time gate (tracked follow-up).

### All low-precision conversions are validated against the authoritative references

Every `float`→low-precision conversion is **bit-exact** to its reference, verified exhaustively and pinned
in CI (`DemoConsole -- bf16-f16-oracle` / `fp8-oracle` + the PMT `LowPrecision_ConversionPinnedToExternalReference`
gate, which pins each backend's on-device convert to hardcoded numpy/ml_dtypes values):

| Type | Reference | float→type rounding |
|------|-----------|---------------------|
| **Half** | `numpy.float16` (IEEE binary16) | round-to-nearest-even incl. subnormals + overflow→Inf (was truncating + flushing subnormals before 4.14.0) |
| **BFloat16** | `ml_dtypes.bfloat16` | round-to-nearest-even (NaN-preserving) |
| **Float8E4M3** | PyTorch/JAX/ml_dtypes `float8_e4m3fn` | RNE; overflow→NaN (fn, default) |
| **Float8E5M2** | `float8_e5m2` | RNE; overflow→±Inf |
| **Float4E2M1** | `ml_dtypes.float4_e2m1fn` | RNE among 16 codes; overflow + ±Inf saturate to ±6; NaN → -0 (`0x8`). No `saturate` overload (always saturates). |
| **QInt4 / QUInt4** | n/a (integer) | integer convert: `float`→int truncates toward zero, keeps the low 4 bits; read sign-extends (QInt4) / zero-extends (QUInt4) to `int`. |

**Selectable saturating cast (all four types).** Each type exposes `FromSingle(float, bool saturate)` and
`FromSingleSaturating(float)` (E4M3 additionally has `FromSingleFn`, its non-saturating name). The saturating
cast clamps finite overflow to the max finite magnitude instead of the default (→NaN for E4M3, →±Inf for the
IEEE types) - the NVIDIA Transformer Engine / OCP mode for activations you don't want producing Inf/NaN. Each
is composed only of existing intrinsics (a bit-level finite check + the default cast + a max-finite-constant
cast), so it transpiles with no per-backend codegen and is bit-exact on all 6 backends.

**Radix-sort: complete for all four byte/2-byte low-precision floats on all 6 backends.** Keys-only and key/value pairs, ascending and
descending, plus body-struct key fields - every `type × {keys, pairs} × {asc, desc}` cell is covered
(`Interop.FloatAsInt(T)` + `Ascending/Descending{Half,BFloat16,Float8E4M3,Float8E5M2}` + per-backend
`FloatAsIntCast`; PMT `RadixGrid_*` + `Fp8Radix_*` + `BFloat16_RadixSort*`). On WebGL the FP8/Half/bf16 keys
route through the unpacked-f32 working representation (the whole-texel scatter can't move a sub-word value);
on the other 5 backends they sort as native packed sub-word keys. The **packed 4-bit types
(`Float4E2M1`/`QInt4`/`QUInt4`) also sort** (keys + pairs, asc + desc) on CUDA/OpenCL/WebGPU/Wasm - see the
packed-4-bit section above (CPU + WebGL gated with the packed store).

### Sub-Word Usage Notes

These apply to any kernel using `ArrayView<byte>`, `ArrayView<sbyte>`, `ArrayView<short>`, `ArrayView<ushort>`, or `ArrayView<Half>`:

- **Use `ILGPU.Half`, NOT `System.Half`, in kernel signatures.** Implicit conversion operators are defined for interop, so you can mix the two on the host side; inside the kernel signature the `ILGPU.Half` type is what the IR + codegen expect.
- **Sub-word writes on WebGPU lower to atomic RMW.** Two threads writing different halves of the same `u32` word would race without RMW; the codegen always synthesizes `atomicAnd` mask + `atomicOr` set so the writes are thread-safe. Setting `RequiresAtomics = true` in `AcceleratorRequirements` (or pinning to a backend with atomics) is therefore mandatory whenever a kernel writes a sub-word view — WebGL has no atomics and rejects sub-word writes at compile time. See [capabilities-and-backend-selection.md](capabilities-and-backend-selection.md).
- **Sub-word view reads can return stale data on WebGPU if you wrote to the same slot in the same kernel invocation.** Byte writes lower to atomic RMW on WebGPU; reading a byte slot you just wrote may observe pre-RMW state in the same dispatch. Treat `ArrayView<byte>` and `ArrayView<sbyte>` as **write-only within a kernel invocation** — buffer the value in a register and route results through that register, not back through the view.
- **`arrayLength()` on sub-word buffers returns the `u32`-count, not the element-count.** A 256-byte buffer reports `arrayLength = 64` (256/4 u32s). Multiply by elements-per-word (4 for byte/sbyte, 2 for short/ushort/Half) when computing element bounds inside the kernel.
- **Sign extension on load is automatic.** `ArrayView<sbyte>` and `ArrayView<short>` reads sign-extend the narrow value to `int` when used in arithmetic (unsigned views zero-extend). The codegen emits `extractBits(x, 0u, 16u)` (WGSL, sign-extends a signed `i32`) / `((x & 0xFFFF) ^ 0x8000) - 0x8000` (GLSL - GLSL ES 3.0 has no `int16_t`, and the obvious `(x << 16) >> 16` is undefined behavior when bit 15 is set, so this `(v ^ signbit) - signbit` idiom is used) / `i32.extend16_s` (Wasm).
- **Signedness reinterprets (`(short)someUshort`, `(ushort)someShort`, `Int8` analogues) re-extend on the browser backends (4.9.13+).** Signed and unsigned sub-word types collapse to one `BasicValueType`, so the reinterpret's `conv` is elided in the IR; the browser `ConvertValue` codegen therefore re-applies sign/zero extension (per the convert's source signedness) when a sub-word value is widened to `int`. Before 4.9.13 this was dropped, silently corrupting the high bits of a reinterpreted sub-word value on WebGPU/WebGL/Wasm (e.g. `(short)bits >> 15` on a value that came from a `ushort`). Desktop backends use native sub-word registers and were never affected.
- **Wasm minimum buffer size is 4 bytes.** Allocating an `ArrayView<byte>` of length 1, 2, or 3 throws `Invalid typed array length: 4` on Wasm. Pad per-block scalar buffers to `Math.Max(blockCount, 4L)` if your kernel writes one byte per block.

### Test Coverage

Every data type is exercised across all 6 backends (7 test columns) through PlaywrightMultiTest. Coverage by family:
- **Sub-word ints + Half** (`Int8`/`UInt8`/`Int16`/`UInt16`/`Float16`): RoundTrip + Read + Write + EndToEnd, plus `Half.Abs/Min/Max`.
- **BFloat16**: BufferRoundTrip + Arithmetic + MinMax + RangeAndSpecials.
- **FP8** (`Float8E4M3`/`Float8E5M2`): `PrecisionConvert_*_RoundTripBitExact`, `Fp8Radix_*`, the `relu(x*scale+bias)` generic `INumber<T>` kernel, and the `fp8-oracle` external-reference pin.
- **Packed 4-bit** (`Float4E2M1`/`QInt4`/`QUInt4`): `PackedFloat4`/`PackedQInt4`/`PackedQUInt4` (allocation = `ceil(N/2)` bytes, load all 6, store round-trip on the 4 store backends), `Fp4Radix`/`QInt4Radix`/`QUInt4Radix` (keys + pairs, asc + desc), `Float4E2M1_FloatToFP4_RneSaturateNaN`, `GenericPrecision_Float4E2M1_*`, and `RawBitsToFloat_*`/`FromRawBits_*` (in-register + host raw decode).
- **Conversion correctness** pinned to numpy / `ml_dtypes` (`LowPrecision_ConversionPinnedToExternalReference`, `Half_FloatToHalf_RoundToNearestEven`).

Latest full cross-backend sweep (the entire suite, all 6 backends / 7 lanes): **3886 pass / 0 fail / 252 skip** (the skips are the genuinely-impossible cells - in-kernel scatter/atomics/packed-store on WebGL, packed-store on CPU).

### Test Files
- `BackendTestBase.Tests17.BrowserBuffer.cs` (sub-word + Half), `BackendTestBase.BFloat16.cs` (bf16)
- `BackendTestBase.PackedFloat4.cs` / `BackendTestBase.PackedQInt4.cs` / `BackendTestBase.PackedQUInt4.cs` (packed 4-bit load/store)
- `BackendTestBase.Float4.RadixSort.cs` / `BackendTestBase.QInt4.RadixSort.cs` / `BackendTestBase.QUInt4.RadixSort.cs` (4-bit radix)
- `BackendTestBase.GenericPrecision.cs` (FP8 + FP4 convert/relu), `BackendTestBase.FromRawBits.cs` (`RawBitsToFloat`/`FromRawBits`)

### How to Run
```bash
# Packed 4-bit (load/store/radix/convert)
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj --filter "FullyQualifiedName~PackedFloat4|FullyQualifiedName~PackedQInt4|FullyQualifiedName~PackedQUInt4|FullyQualifiedName~Radix|FullyQualifiedName~Float4E2M1|FullyQualifiedName~RawBitsToFloat"

# Sub-word + Half + BFloat16
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj --filter "FullyQualifiedName~Int8|FullyQualifiedName~Int16|FullyQualifiedName~Float16|FullyQualifiedName~Half_|FullyQualifiedName~BFloat16"
```
