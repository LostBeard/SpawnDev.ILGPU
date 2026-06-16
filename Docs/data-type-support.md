# Data Type Support by Backend

Tracks verified support for all data types across all 7 backends.
Updated: 2026-06-16

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
| Float32 | float | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float64 | double | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |

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
| Float32 | float | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float64 | double | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |

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
| Float32 | float | 4B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |
| Float64 | double | 8B | [x] | [x] | [x] | [x] | [x] | [x] | [x] |

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

All sub-word types now have **complete Read/Write/EndToEnd support on ALL 7 backends**.

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
Read/Write/EndToEnd support on ALL 7 backends.** The bf16<->f32 conversion is byte-identical across every
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
  infinities** (the only non-finite value is NaN at `0x7F`/`0xFF`), max finite magnitude **448**, finite
  overflow **saturates** to ±448. The FP8 **forward / inference** format (one extra mantissa bit vs E5M2,
  at the cost of range).
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

> **Convention note (E4M3 overflow):** out-of-range *inputs* to `Float8E4M3` saturate finite overflow to
> ±448 and map ±Inf -> NaN (the OCP / NVIDIA Transformer Engine saturating-forward convention). Only the
> out-of-range input behavior is convention-dependent; every *representable* value round-trips exactly.

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

**175 tests total** across the sub-word test methods + Half intrinsics + BFloat16, all x 7 backends:
- Int8: 28 tests (RoundTrip + Read + Write + EndToEnd x 7 backends)
- UInt8: 28 tests
- Int16: 35 tests (+ existing CopyFromJS tests)
- UInt16: 28 tests
- Float16: 21 tests (Read + Write + EndToEnd x 7 backends)
- Half Abs: 7 tests
- Half MinMax: 7 tests
- BFloat16: 28 tests (BufferRoundTrip + Arithmetic + MinMax + RangeAndSpecials x 7 backends)

### Test Files
- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.Tests17.BrowserBuffer.cs` (sub-word + Half)
- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.BFloat16.cs` (bf16)

### How to Run
```bash
# All sub-word + Half tests
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj --filter "FullyQualifiedName~Int8|FullyQualifiedName~UInt8|FullyQualifiedName~Int16|FullyQualifiedName~UInt16|FullyQualifiedName~Float16|FullyQualifiedName~Half_"

# All BFloat16 tests
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj --filter "FullyQualifiedName~BFloat16"
```
