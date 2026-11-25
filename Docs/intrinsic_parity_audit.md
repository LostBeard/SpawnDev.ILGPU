# Intrinsic Parity Audit — Full Report

> Generated: 2026-03-02 | Updated with all session fixes (PopC/CLZ/CTZ/IsFinF, Wasm RegisterMathIntrinsics, WebGL gaps)

## Summary

| Backend | Type | Threading | Atomics | Math Coverage |
|---------|------|:---------:|:-------:|:-------------:|
| CPU | Desktop | ✅ | ✅ | ✅ Full |
| OpenCL | Desktop | ✅ | ✅ | ✅ Full |
| PTX/CUDA | Desktop | ✅ | ✅ | ✅ Full |
| Velocity | Desktop | ✅ | ✅ | ✅ Full |
| **Wasm** | Browser | ✅ wasm-threads | ✅ Full (inc. float CAS) | ✅ **27/27 unary** |
| **WebGPU** | Browser | ✅ compute | ✅ Full (inc. float CAS) | ✅ **27/27 unary** |
| **WebGL** | Browser | ❌ vertex shader | ❌ stubs only | ✅ **27/27 codegen** (3 TF-limited) |

> [!IMPORTANT]
> **Wasm** has full wasm-threads: Web Workers, SharedArrayBuffer, pure spin barriers (`i32.atomic.load` loops — `wait32`/`notify` replaced due to V8 visibility gap), shared memory, and `Group.Broadcast`. Full `hardwareConcurrency` multi-worker barrier dispatch with in-Wasm phase dispatcher. It has **full `GenericAtomic` and `AtomicCAS` handlers** including float atomics via CAS loop with bitcast reinterpret (f32↔i32, f64↔i64). This includes float Add/Min/Max/Exchange and integer Add/Sub/And/Or/Xor/Exchange/Cmpxchg plus integer Min/Max via CAS.

> [!IMPORTANT]
> **WebGPU** has full `GenericAtomic` and `AtomicCAS` handlers with `ScanForAtomicUsage()` pre-pass. Float atomics use CAS loop (`atomicCompareExchangeWeak`). Unsigned atomics use `atomic<u32>`. Emulated 64-bit atomics supported via dual-u32 packing.

> [!NOTE]
> **WebGL** `GenericAtomic`/`AtomicCAS`/`Barrier`/`Broadcast` handlers exist but are **stubs** — they emit `// not supported in WebGL2 vertex shaders` and return 0. This is an architectural limitation of GLSL ES 3.0 Transform Feedback.

---

## 1. UnaryArithmeticKind (27 members)

| Kind | Desktop | Wasm | WebGPU | WebGL | Notes |
|------|:-------:|:----:|:------:|:-----:|-------|
| `Neg` | ✅ | ✅ | ✅ | ✅ | All |
| `Not` | ✅ | ✅ | ✅ | ✅ | All |
| `Abs` | ✅ | ✅ native | ✅ `abs()` | ✅ | All |
| `PopC` | ✅ | ✅ `i32.popcnt` | ✅ `countOneBits` | ⚠️ `bitCount()` | WebGL: emits but TF returns 255 |
| `CLZ` | ✅ | ✅ `i32.clz` | ✅ `countLeadingZeros()` | ⚠️ `findMSB()` | WebGL: emits but TF-limited |
| `CTZ` | ✅ | ✅ `i32.ctz` | ✅ `countTrailingZeros()` | ⚠️ `findLSB()` | WebGL: emits but TF-limited |
| `RcpF` | ✅ | ✅ `1/x` | ✅ `1/x` | ✅ | All |
| `IsNaNF` | ✅ | ✅ `x!=x` | ✅ `x!=x` | ✅ `isnan()` | All |
| `IsInfF` | ✅ | ✅ `abs==∞` | ✅ `abs==1/0` | ✅ `isinf()` | All |
| `IsFinF` | ✅ | ✅ `!NaN&&!Inf` | ✅ `!NaN&&!Inf` | ✅ `!isnan&&!isinf` | All |
| `SqrtF` | ✅ | ✅ native | ✅ `sqrt()` | ✅ | All |
| `RsqrtF` | ✅ | ✅ `1/sqrt` | ✅ `1/sqrt` | ✅ `inversesqrt()` | All |
| `SinF` | ✅ | ✅ import | ✅ `sin()` | ✅ | All |
| `CosF` | ✅ | ✅ import | ✅ `cos()` | ✅ | All |
| `TanF` | ✅ | ✅ import | ✅ `tan()` | ✅ | All |
| `AsinF` | ✅ | ✅ import | ✅ `asin()` | ✅ | All |
| `AcosF` | ✅ | ✅ import | ✅ `acos()` | ✅ | All |
| `AtanF` | ✅ | ✅ import | ✅ `atan()` | ✅ | All |
| `SinhF` | ✅ | ✅ import | ✅ `sinh()` | ✅ | All |
| `CoshF` | ✅ | ✅ import | ✅ `cosh()` | ✅ | All |
| `TanhF` | ✅ | ✅ import | ✅ `tanh()` | ✅ | All |
| `ExpF` | ✅ | ✅ import | ✅ `exp()` | ✅ | All |
| `Exp2F` | ✅ | ✅ `exp(x*ln2)` | ✅ `exp2()` | ✅ `exp2()` | All |
| `FloorF` | ✅ | ✅ native | ✅ `floor()` | ✅ | All |
| `CeilingF` | ✅ | ✅ native | ✅ `ceil()` | ✅ | All |
| `LogF` | ✅ | ✅ import | ✅ `log()` | ✅ | All |
| `Log2F` | ✅ | ✅ import | ✅ `log2()` | ✅ `log2()` | All |
| `Log10F` | ✅ | ✅ `log/ln10` | ✅ `log/2.302` | ✅ `log/log(10)` | All |

**Totals:** Desktop 27/27 • WebGPU 27/27 ✅ • Wasm 27/27 ✅ • WebGL 24/27 ✅ + 3 ⚠️ (TF-limited)

---

## 2. BinaryArithmeticKind (16 members)

| Kind | Desktop | Wasm | WebGPU | WebGL | Notes |
|------|:-------:|:----:|:------:|:-----:|-------|
| `Add` | ✅ | ✅ | ✅ | ✅ | All |
| `Sub` | ✅ | ✅ | ✅ | ✅ | All |
| `Mul` | ✅ | ✅ | ✅ | ✅ | All |
| `Div` | ✅ | ✅ | ✅ | ✅ | All |
| `Rem` | ✅ | ✅ emulated float | ✅ `%` | ✅ | All |
| `And` | ✅ | ✅ | ✅ | ✅ | All |
| `Or` | ✅ | ✅ | ✅ | ✅ | All |
| `Xor` | ✅ | ✅ | ✅ | ✅ | All |
| `Shl` | ✅ | ✅ | ✅ | ✅ | All |
| `Shr` | ✅ | ✅ | ✅ | ✅ | All |
| `Min` | ✅ | ✅ | ✅ `min()` | ✅ | All |
| `Max` | ✅ | ✅ | ✅ `max()` | ✅ | All |
| `Atan2F` | ✅ | ✅ import | ✅ `atan2()` | ✅ `atan(y,x)` | All |
| `PowF` | ✅ | ✅ import | ✅ `pow()` | ✅ | All |
| `BinaryLogF` | ✅ | ✅ `log(x)/log(y)` | ✅ | ✅ `log(x)/log(y)` | All |
| `CopySignF` | ✅ | ✅ native | ✅ `select` | ✅ `abs(x)*sign(y)` | All |

**Totals:** All backends 16/16 ✅

## 3. TernaryArithmeticKind

| Kind | Desktop | Wasm | WebGPU | WebGL |
|------|:-------:|:----:|:------:|:-----:|
| `MultiplyAdd` | ✅ | ✅ `a*b+c` | ✅ `fma()` | ✅ | All |

---

## 4. Atomic Operations

### Wasm — Full support ✅

| Op | i32 | i64 | f32 | f64 |
|----|:---:|:---:|:---:|:---:|
| Add | ✅ `I32AtomicRmwAdd` | ✅ `I64AtomicRmwAdd` | ✅ CAS loop | ✅ CAS loop |
| Sub | ✅ | ✅ | — | — |
| And | ✅ | ✅ | — | — |
| Or | ✅ | ✅ | — | — |
| Xor | ✅ | ✅ | — | — |
| Exchange | ✅ | ✅ | ✅ CAS | ✅ CAS |
| Min | ✅ CAS loop | ✅ CAS | ✅ CAS | ✅ CAS |
| Max | ✅ CAS loop | ✅ CAS | ✅ CAS | ✅ CAS |
| CmpXchg | ✅ | ✅ | — | — |

### WebGPU — Full support ✅

| Op | i32 | u32 | f32 (CAS) | emu64 |
|----|:---:|:---:|:---------:|:-----:|
| Add | ✅ `atomicAdd` | ✅ | ✅ CAS loop | ✅ |
| Min/Max | ✅ `atomicMin/Max` | ✅ | ✅ CAS | ✅ |
| And/Or/Xor | ✅ | ✅ | — | — |
| Exchange | ✅ `atomicExchange` | ✅ | ✅ CAS | ✅ |
| CAS | ✅ `atomicCompareExchangeWeak` | ✅ | — | ✅ |

### WebGL — Stubs only ❌
All `GenericAtomic`/`AtomicCAS` emit `// not supported` and return 0.

---

## 5. Threading & Synchronization

| Feature | Wasm | WebGPU | WebGL |
|---------|:----:|:------:|:-----:|
| Multi-threaded dispatch | ✅ Web Workers | ✅ compute shader | ❌ |
| SharedArrayBuffer | ✅ | N/A (GPU mem) | ❌ |
| Shared memory allocations | ✅ `SetupSharedAllocations` | ✅ `var<workgroup>` | ❌ |
| Barriers | ✅ sense-reversing (wait/notify) | ✅ `workgroupBarrier()` | ❌ stub |
| Group.Broadcast | ✅ shared mem + barrier | ✅ | ❌ stub |
| Warp shuffle | ❌ N/A | ✅ `subgroupShuffle` | ❌ stub |

---

## 6. Math/MathF Redirect Registrations

| Function | WebGPU | WebGL | Wasm |
|----------|:------:|:-----:|:----:|
| Abs/Sign/Round/Truncate | ✅ | ✅ | ✅ |
| Atan2/Max/Min/Pow | ✅ | ✅ | ✅ |
| Clamp/FMA | ✅ | ✅ | ✅ |
| IntrinsicMath.Abs/Min/Max | ✅ | ✅ | ✅ |

> [!NOTE]
> All three browser backends now have `RegisterMathIntrinsics()` with throw-free wrappers for `Math.Round`, `Math.Truncate`, `Math.Sign`, `Math.Clamp`, `Math.FusedMultiplyAdd`, and `IntrinsicMath` methods. Wasm support was added in this session.

## 7. XMath & Algorithms Registrations

| Intrinsic | WebGPU | WebGL | Wasm |
|-----------|:------:|:-----:|:----:|
| XMath.Rsqrt/Rcp | ✅ | ✅ | ✅ |
| GroupExtensions.Reduce/AllReduce | ✅ subgroups | ❌ N/A | ✅ WasmAlgorithmContext |
| WarpExtensions.Reduce/AllReduce | ✅ subgroups | ❌ N/A | ✅ WasmAlgorithmContext |
| ILGroupExtensions.*Reduce | ✅ | ❌ | ✅ |
| Scan (Excl/Incl) | ✅ | ❌ N/A | ✅ shared mem |
| RadixSort | ✅ | ❌ N/A | ✅ |
| Histogram | ✅ | ❌ N/A | ✅ |

---

## 8. 64-bit Emulation

| Type | WebGPU | WebGL | Wasm | Desktop |
|------|:------:|:-----:|:----:|:-------:|
| f64 (Dekker default) | ✅ `vec2<f32>` ~48-bit | ✅ `vec2` ~48-bit | ✅ native | ✅ native |
| f64 (Ozaki opt-in) | ✅ `vec4<f32>` (binary64 only on stores; not IEEE 754) | ✅ `vec4` (same) | N/A | N/A |
| i64/u64 | ✅ `vec2<u32>` | ✅ `uvec2` | ✅ native | ✅ native |

---

## Backend Scorecard

| Category | Desktop | WebGPU | Wasm | WebGL |
|----------|:-------:|:------:|:----:|:-----:|
| Unary (27) | 27 | **27** ✅ | **27** ✅ | **24** ✅ + 3 ⚠️ |
| Binary (16) | 16 | **16** ✅ | **16** ✅ | **16** ✅ |
| Ternary (1) | 1 | **1** ✅ | **1** ✅ | **1** ✅ |
| Int atomics | ✅ | ✅ | ✅ | ❌ stub |
| Float atomics | ✅ | ✅ CAS | ✅ CAS | ❌ stub |
| AtomicCAS | ✅ | ✅ | ✅ | ❌ stub |
| Barriers | ✅ | ✅ | ✅ | ❌ stub |
| Shared memory | ✅ | ✅ | ✅ | ❌ |
| Math redirects | ✅ | ✅ | ✅ | ✅ |
| XMath Rsqrt/Rcp | ✅ | ✅ | ✅ | ✅ |
| Algorithms | ✅ | ✅ | ✅ | ❌ |
| f64 emulation | N/A | ✅ Dekker+Ozaki | N/A (native) | ✅ Dekker+Ozaki |

> [!NOTE]
> All previously identified gaps have been addressed. WebGPU and Wasm now have **full parity** with desktop backends. WebGL's remaining limitations (atomics, barriers, shared memory, PopC/CLZ/CTZ runtime behavior) are architectural constraints of GLSL ES 3.0 vertex shader Transform Feedback and cannot be resolved without a fundamentally different approach.

