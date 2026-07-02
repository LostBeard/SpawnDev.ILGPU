# Atomic Operations by Backend

Tracks support for all atomic operations across all backends and data types.
Updated: 2026-07-02

**Legend:**
- [x] PASS - native or correctly emulated, verified with tests
- [CAS] - emulated via compare-and-swap loop (correct, slightly slower)
- [Lock] - emulated via a per-element spinlock (a companion `array<atomic<u32>>` lock buffer, auto-provisioned by `ScanForAtomicUsage`; correct, serializes access so both u32 halves of a 64-bit value update as one unit)
- [!] NOT SUPPORTED - throws `NotSupportedException` at kernel compilation time
- [V] VOTE - WebGL Transform Feedback vote pattern (accumulation only, return value always 0)
- [-] NOT TESTED - no dedicated tests yet

---

## Int32 / UInt32 Atomics

Natively supported on all backends with atomics capability.

| Operation | WebGPU | Wasm | WebGL | CUDA | OpenCL | CPU |
|-----------|:------:|:----:|:-----:|:----:|:------:|:---:|
| Add | [x] | [x] | [V] | [x] | [x] | [x] |
| And | [x] | [x] | [!] | [x] | [x] | [x] |
| Or | [x] | [x] | [!] | [x] | [x] | [x] |
| Xor | [x] | [x] | [!] | [x] | [x] | [x] |
| Min | [x] | [x] | [!] | [x] | [x] | [x] |
| Max | [x] | [x] | [!] | [x] | [x] | [x] |
| Exchange | [x] | [x] | [!] | [x] | [x] | [x] |
| CompareExchange | [x] | [x] | [!] | [x] | [x] | [x] |

**WebGL note:** Only `Atomic.Add` is partially supported via the Transform Feedback vote pattern. Each thread emits its increment to a TF varying; JavaScript sums all per-vertex contributions post-draw. The return value (old value before add) is always 0 - kernels using the return value for slot allocation will silently produce wrong results. All other atomic operations throw `NotSupportedException`.

---

## Int64 / UInt64 Atomics

i64 is emulated as `vec2<u32>` on WebGPU and WebGL. Native on Wasm, CUDA, OpenCL, CPU.

| Operation | WebGPU | Wasm | WebGL | CUDA | OpenCL | CPU |
|-----------|:------:|:----:|:-----:|:----:|:------:|:---:|
| Add | [x] CAS | [x] | [!] | [x] | [x] | [x] |
| And | [x] Dual | [x] | [!] | [x] | [x] | [x] |
| Or | [x] Dual | [x] | [!] | [x] | [x] | [x] |
| Xor | [x] Dual | [x] | [!] | [x] | [x] | [x] |
| Min | [Lock] | [CAS] | [!] | [x] | [x] | [x] |
| Max | [Lock] | [CAS] | [!] | [x] | [x] | [x] |
| Exchange | [Lock] | [x] | [!] | [x] | [x] | [x] |
| CompareExchange | [!] | [x] | [!] | [x] | [x] | [x] |

### WebGPU i64 emulation details

**Bitwise (And/Or/Xor) - "Dual i32":** Two independent `atomicAnd`/`atomicOr`/`atomicXor` on lo and hi halves. Correct because bitwise operations are component-independent - the result of each half doesn't depend on the other.

**Add - "CAS loop":** Lock-free CAS loop on the lo half + `atomicAdd` on the hi half with carry. The CAS loop (`atomicCompareExchangeWeak`) on the lo half serializes low-half updates. When the lo half wraps (unsigned overflow), a carry of 1 is added to the hi half via `atomicAdd`. This works because `atomicAdd` is commutative - multiple threads can add their carry values in any order and the final result is correct.

**Min/Max/Exchange - "[Lock]" spinlock:** These require both halves of the i64 value to be read and/or written atomically as a single 64-bit unit, which WGSL's 32-bit atomics can't do directly. A per-element companion `array<atomic<u32>>` lock buffer (auto-provisioned by `ScanForAtomicUsage`) serializes access: each thread `atomicCompareExchangeWeak`-acquires the slot's lock, reads/compares/writes both u32 words inside the critical section, then releases the lock. Verified with tests on WebGPU.

**CompareExchange (CAS) - NOT SUPPORTED:** a lock-free 64-bit compare-and-swap has no 32-bit-atomic equivalent, so `Atomic.CompareExchange` on an i64 with a WebGPU accelerator throws `NotSupportedException` at kernel compilation time with a clear error message.

---

## Float32 Atomics

| Operation | WebGPU | Wasm | WebGL | CUDA | OpenCL | CPU |
|-----------|:------:|:----:|:-----:|:----:|:------:|:---:|
| Add | [CAS] | [CAS] | [!] | [x] | [x] | [x] |
| Min | [CAS] | [CAS] | [!] | [CAS] | [x] | [x] |
| Max | [CAS] | [CAS] | [!] | [CAS] | [x] | [x] |
| Exchange | [CAS] | [CAS] | [!] | [x] | [x] | [x] |

**CAS loop:** Buffer is declared as `atomic<u32>`. Values are `bitcast` between `f32` and `u32`. The CAS loop reads the current u32, bitcasts to f32, computes the new value, bitcasts back to u32, and attempts `atomicCompareExchangeWeak`. Retries on contention.

---

## Float16 (Half) Atomics

Half-precision hardware atomics are not available on any backend (f16 is 2 bytes; atomics require 4+ byte alignment and operate on u32/u64 granularity). There is no direct `Atomic.Add(ref Half, Half)` primitive. Use cases that need Half reductions go through the `accelerator.Reduce<Half, AddHalf/MaxHalf/MinHalf>` widen-to-f32 dispatch (see `ILGPU.Algorithms/ReductionExtensions.cs`) or group-level `GroupExtensions.AllReduce<Half, AddHalf>` for single-workgroup cases.

| Operation | WebGPU | Wasm | WebGL | CUDA | OpenCL | CPU |
|-----------|:------:|:----:|:-----:|:----:|:------:|:---:|
| Reduce (multi-workgroup) | [W2F] | [W2F] | [!] | [W2F] | [W2F] | [W2F] |
| AllReduce (single-workgroup) | [x] | [x] | [!] | [x] | [x] | [x] |
| Direct Atomic.Add(ref Half) | n/a | n/a | n/a | n/a | n/a | n/a |

**[W2F] Widen-to-f32:** `accelerator.Reduce<Half, AddHalf>` intercepts at the public entry in `ReductionExtensions`, allocates an f32 temp buffer, runs `Reduce<float, AddFloat>` through the existing `Atomic.Add(ref float)` CAS infrastructure, converts the final f32 result back to Half. Lossless (f16 is a strict subset of f32 encoding) and bit-for-bit identical across all 6 supported backends on the same inputs.

**Single-workgroup AllReduce** uses a lock-free shared-memory pattern (per-warp first-lane writes into private slots, first thread serially combines). No atomics required; works on every backend that has shared memory + barriers.

**WebGL:** no vertex shader atomics at all, so `Reduce<Half>` legitimately throws. Matches the existing WebGL skip pattern for every `accelerator.Reduce<T>` variant.

**Long-term:** proper `Atomic.CompareExchange(ref Half, Half, Half)` intrinsics would allow a single-pass path without the widen-to-f32 temp buffer. Design parked in `Plans/PLAN-Atomic-Half-Intrinsics.md` for a future cycle; not needed for the shipping Half Reduce.

---

## Float64 Atomics

| Operation | WebGPU | Wasm | WebGL | CUDA | OpenCL | CPU |
|-----------|:------:|:----:|:-----:|:----:|:------:|:---:|
| Add | [Lock] | [CAS] | [!] | [x] | [x] | [x] |
| Min | [Lock] | [CAS] | [!] | [x] | [x] | [x] |
| Max | [Lock] | [CAS] | [!] | [x] | [x] | [x] |
| Exchange | [Lock] | [CAS] | [!] | [x] | [x] | [x] |

**WebGPU - "[Lock]" spinlock:** f64 is emulated as two 32-bit words (Dekker `vec2<f32>` or Ozaki `vec4<f32>`). Add/Min/Max/Exchange each acquire a per-element companion lock (`array<atomic<u32>>`, auto-provisioned by `ScanForAtomicUsage`), read both words + the emulated f64, compute, write both words back, then release - so both words update as one atomic unit. (A lock-free 64-bit CompareExchange has no 32-bit-atomic form and is not offered.)

**Wasm:** f64 is native. CAS loop uses `i64.atomic.rmw.cmpxchg` with `f64.reinterpret_i64` / `i64.reinterpret_f64` for bitwise conversion.

---

## Usage Examples

### i32 Atomic.Add (works everywhere with atomics)

```csharp
static void CountKernel(Index1D index, ArrayView<int> data, ArrayView<int> counter)
{
    if (data[index] > 0)
        Atomic.Add(ref counter[0], 1);
}
```

### i64 Atomic.And (bitwise - works on WebGPU, Wasm, desktop)

```csharp
static void ClearBitKernel(Index1D index, ArrayView<long> faceMasks)
{
    // Clear this thread's bit from the shared mask
    Atomic.And(ref faceMasks[0], ~(1L << index));
}
```

### i64 Atomic.Add (CAS loop on WebGPU, native on Wasm/desktop)

```csharp
static void AccumulateKernel(Index1D index, ArrayView<long> accumulator, ArrayView<long> values)
{
    Atomic.Add(ref accumulator[0], values[index]);
}
```

### f32 Atomic.Add (CAS loop on browser backends, native on desktop)

```csharp
static void SumKernel(Index1D index, ArrayView<float> data, ArrayView<float> sum)
{
    Atomic.Add(ref sum[0], data[index]);
}
```

---

## WebGPU Binding Limit Impact

WebGPU has a `maxStorageBuffersPerShaderStage` limit of 10 (Chrome). Each `ArrayView` kernel parameter uses one storage buffer binding, plus one for `_scalar_params`.

Atomic operations do NOT add extra bindings - they operate on existing buffer bindings using `atomicLoad`, `atomicStore`, `atomicAdd`, `atomicAnd`, etc.

If a buffer has BOTH atomic and non-atomic access patterns (e.g., `Atomic.And(ref buf[x], mask)` and `long val = buf[y]`), the codegen declares the buffer as `array<atomic<u32>>` and emits `atomicLoad` for all reads - not just atomic operations. This ensures WGSL validation passes (Chrome rejects mixed atomic/non-atomic access on the same buffer).

---

## Test Coverage

| Test | File | What It Verifies |
|------|------|-----------------|
| AtomicAndLongTest | Tests19 | i64 Atomic.And with dual i32 atomics |
| AtomicAddLongConcurrentTest | Tests19 | i64 Atomic.Add CAS loop with 64 concurrent writers |
| AtomicAddLongCarryTest | Tests19 | i64 Atomic.Add CAS loop carry between lo/hi halves |
| FullCombinedPatternTest | Tests19 | i64 Atomic.And + Add + bit packing combined |
| ChainedDispatch3KernelTest | Tests18 | Implicit stream ordering (no sync between dispatches) |
| ImplicitSyncOnReadbackTest | Tests18 | CopyToHostAsync without explicit SynchronizeAsync |

---

## Filtering Out Incompatible Backends Up Front

Rather than letting `NotSupportedException` surface from kernel compilation on a backend that can't run your kernel, declare the requirement at backend-selection time:

```csharp
using SpawnDev.ILGPU;

using var accelerator = context.CreatePreferredAccelerator(
    new AcceleratorRequirements
    {
        RequiresAtomics = true,           // rules out WebGL
        RequiresInt64Atomics = true,      // rules out WebGL + OpenCL without cl_khr_int64_base_atomics
    });
```

The selection path picks a backend that can satisfy every flag, or throws `NotSupportedException` naming the unmet requirement and the available devices. See [capabilities-and-backend-selection.md](capabilities-and-backend-selection.md) for the full flag set and per-backend "rules out" matrix.
