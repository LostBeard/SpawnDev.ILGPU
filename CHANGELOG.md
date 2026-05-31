# SpawnDev.ILGPU Changelog

This file tracks notable changes per release. The README's "Recent Highlights" section links here for the full version history.

## 4.9.11 (2026-05-31) - Math.Clamp in kernels on all backends + async browser parity

Bundles forks **SpawnDev.ILGPU.Fork 2.0.8** and **SpawnDev.ILGPU.Algorithms.Fork 2.0.8** (both
carry the `ILGPU/` core changes below - consumers must take all three packages together).

### `Math.Clamp` now compiles and runs in kernels on all 6 backends

`System.Math.Clamp(value, min, max)` inlines `throw ThrowMinMaxException(...)`, which the IL
frontend cannot lower to IR. On WebGPU / WebGL / Wasm this failed at kernel PreCompile with
`InternalCompilerException -> NotSupportedException('Throw')`. The pre-existing backend-level
redirect fired too late (it runs in the IR Intrinsics pass, after the frontend already tried to
lower the throwing body).

Fix: `Math.Clamp(T,T,T)` is now remapped at the IL frontend (`RemappedIntrinsics`) to the
throw-free `IntrinsicMath.Clamp` (`= Max(Min(value, max), min)`, built on the `[MathIntrinsic]`
Min/Max) before inlining - the same robust path `Math.Min`/`Max`/`Abs` already use. Covers all 10
`IntrinsicMath`-supported types (sbyte/short/int/long, byte/ushort/uint/ulong, float, double).
`MathF` has no `Clamp` overload, so none is remapped. Verified GREEN on WebGPU, WebGL, Wasm, CPU,
CUDA, OpenCL via `MathClampIntTest` / `MathClampFloatTest`.

### Async browser parity - overridable core async primitives

`Synchronize()` cannot block on the single Blazor thread, so on Wasm/WebGL it only reaps completed
tasks and on WebGPU only flushes the encoder - none of them drain in-flight work. Any immediate
buffer op after an unawaited dispatch raced the workers (Wasm read stale, WebGPU sync GPU->CPU
`CopyTo` threw).

- `Accelerator.SynchronizeAsync()` / `AcceleratorStream.SynchronizeAsync()` are now `virtual` in
  core; Wasm/WebGPU/WebGL override to their real async waits (the core `AcceleratorStream` version
  was previously a non-virtual `Task.Run(synchronizeAction)` - fake on Wasm).
- `MemoryBuffer.CopyToRawAsync(stream, offset, length)` is now `virtual` in core, exposed to
  consumers as `ArrayView<T>.CopyToCPUAsync(stream)`. Per-backend overrides do a true drain +
  readback.
- `ReductionExtensions.ReduceAsync` (in `ILGPU.Algorithms`) was `Task.Run(sync Reduce)` - threw on
  WebGPU / returned stale on Wasm. It now uses the async primitives. The synchronous `Reduce`->scalar
  overloads throw a clear `NotSupportedException` on Wasm/WebGL/WebGPU instead of returning stale data.
- New `ArrayView<T>.MemSetToZeroAsync()` - async sibling of `CopyFromAsync`.

### Wasm: TensorView + Half struct-param dispatch

`TensorView<T>`-shaped body structs with trailing `Half` fields now serialize and dispatch
correctly on Wasm (struct-with-view IR-layout serialization + Float16 path). Plus residual large-sort
hardening + diagnostics (the rare heavy-duplicate multi-pass race remains under investigation).

### WebGL: nested-struct hoisted init via per-field constructors

Nested struct hoisted initialization (e.g. BVH ray traversal) now emits per-field constructors;
PointerType hoists as int.

### Opt-in Wasm host-buffer race detector

`WasmMemoryBuffer.DetectHostBufferRaces` (default false) makes the synchronous host ops throw if the
buffer has an in-flight dispatch - enable it in a sweep to enumerate any remaining sync-readback race
sites that the async APIs replace.

## 4.9.10-local.12 (2026-05-28) - Wasm/WebGL Float16 constant codegen fix

### Wasm + WebGL: `PrimitiveValue` for `Float16` must not use `Float32Value`

`Float16` IR constants store raw half bits in `PrimitiveValue.rawValue`. Wasm codegen promoted them to `f32` via `Float32Value`, which reinterprets those bits as IEEE-754 single precision (garbage near zero). Kernels that assigned `(Half)1.5f` to `ArrayView1D<Half>` or `TensorView<Half>` wrote zeros while float paths worked.

- `WasmCodeGenerator.GenerateCode(PrimitiveValue)`: emit `(float)value.Float16Value` when `BasicValueType == Float16`.
- `GLSLCodeGenerator.GenerateCode(PrimitiveValue)`: same fix for WebGL.

### Verification

- `WasmTests.ML_ArrayView1D_Half_OneBuffer_Write`, `ML_ArrayView1D_Half_TwoBuffer_Sanity`, `ML_TensorView_Half_RoundTrip_CrossAssembly` pass.
- `WebGLTests.ML_TensorView_Half_RoundTrip_CrossAssembly` pass.

## 4.9.10 (2026-05-28) - Wasm residual large-sort race fix (scan/broadcast hardening)

### Wasm: Group.Broadcast now validates per-group publish with an atomic tag handshake

Fixes the residual descending-sort corruption that persisted after earlier dispatcher/barrier work and amplified under heavy external CPU contention (Fallout76 repro). The core issue manifested as occasional stale shared-slot consumption around scan + broadcast phases in large multi-group RadixSort workloads.

`WasmKernelFunctionGenerator.GenerateCode(Broadcast)` now allocates two shared-memory slots per broadcast:

- **value slot** - the broadcast payload
- **tag slot** - an i32 group tag (`globalIdx / groupDimX`)

Emit pattern:

1. origin thread atomically stores value to the value slot
2. origin thread atomically stores group tag to the tag slot
3. barrier
4. all threads spin on atomic tag load until it equals expected group tag
5. all threads atomically load the value slot
6. barrier

This prevents consuming stale slot contents from a previous group when timing/scheduler pressure is high.

### Verification

- Targeted Wasm tests (all pass): `ScanBroadcastIsolationTest`, `GroupBroadcastDiagTest`, `RadixSortRepeatedResortTest`, `RadixSortDescending1_4MTest`, `RadixSortDescendingOddCountTest`, `RadixSortDescending4MTest`.
- Full Wasm sweeps on patched tree: **459 pass / 0 fail / 4 skip** (run twice).
- FO76 contention repro (two concurrent full sweeps): **1664 pass / 0 fail / 149 skip** on both runs, `RunState=Done`.

### Scope

- Pure SpawnDev.ILGPU wrapper/codegen change.
- Fork dependencies unchanged (`SpawnDev.ILGPU.Fork` / `SpawnDev.ILGPU.Algorithms.Fork` remain `2.0.7`).

## 4.9.9 (2026-05-24) — WebGPU scalar-slot drift fix, WebGL GPU→GPU copy fix, new `CopyFromAsync`, Wasm barrier verdict

Batches the three browser-backend fixes published locally as `4.9.9-local.1/2/3` plus the Wasm wait/notify barrier re-validation. Forks unchanged (`SpawnDev.ILGPU.Fork` / `SpawnDev.ILGPU.Algorithms.Fork` stay `2.0.7`) — these are pure SpawnDev.ILGPU codegen/runtime changes.

### WebGPU: scalar-slot drift for trailing scalars after body-struct params (ML TensorView unblock)

Kernels whose signature placed plain scalar params (`float`, `float`, `int`, …) **after** one or more body-struct params (e.g. two `TensorView<>`-shaped structs) emitted those trailing scalars at the wrong `_scalar_params` indices. Two sides disagreed:

- `WGSLKernelFunctionGenerator.SetupParameterBindings` `continue`d on body-struct params without advancing `scalarSlotOffset`, so the trailing scalars landed at `_scalar_params[0..2]`.
- `GenerateHeader` correctly assigned them slots 10/11/12.

Both sides now advance the scalar-slot counter identically per param, and `WebGPUAccelerator`'s runtime arg lookup accounts for body-struct params expanding into N `FlattenStructFields` slots. This was the root cause of the ML Phase 2 TensorView `/depth` flat-blue regression that had been papered over with a revert. WebGPU full sweep after the fix: **606 pass / 0 fail / 7 skip**. Repro tests in `BackendTestBase.Tests24.TensorViewStructParam.cs`. GitHub commits `d5154c6` + `4d8db3a`.

### WebGL: GPU→GPU `CopyTo`/`CopyFrom` read stale CPU-side data

WebGL→WebGL copies read from the main-thread `_backingArray`, which is **never refreshed** after a kernel Transform Feedback write — only the worker's `entry.data` (in the OffscreenCanvas worker) holds the canonical post-kernel bytes. So `dstBuf.View.CopyFrom(kernelOutputView)` returned all zeros. This surfaced as the "`MemoryBuffer2D<T>.BaseView` kernel-write silently zeros" symptom blocking the ML TensorView migration on WebGL — but the 2D angle was a red herring; the same bug fired on `Allocate1D<T>` outputs staged through `CopyFrom`. Fix: route WebGL→WebGL copies through a new worker-side `copyBuffer` message that copies between worker `entry.data` arrays and re-uploads the destination texture; the stale `_backingArray` is no longer touched. WebGL full sweep: **484 pass / 0 fail / 144 skip**. GitHub commit `bb26aa6`.

### New: backend-agnostic `CopyFromAsync` extension

New `CopyFromAsync` on `ArrayView<T>`, `ArrayView1D<T,TStride>`, and `MemoryBuffer1D<T,TStride>` — the async mirror of the sync `CopyFrom`, available on all 6 backends. On Wasm it awaits the source/destination accelerator's pending kernel dispatches (`SynchronizeAsync`) before issuing the copy; on WebGPU/WebGL/CUDA/OpenCL/CPU the drain is a no-op since their command encoder / GL worker queue / accelerator stream already serialize the copy after pending work.

This closes a Blazor-WASM-only race: the single-threaded main thread cannot block, so `WasmAccelerator.SynchronizeInternal()` is intentionally a no-op, leaving the sync void `CopyFrom` with no ordering guarantee against in-flight worker dispatches (it read `SharedArrayBuffer` mid-write → stale/partial bytes). Use `CopyFromAsync` for GPU→GPU copies that follow an unawaited kernel dispatch in async code, mirroring `CopyToHostAsync`'s implicit-sync contract. **14/14** across all 7 backend test classes (`CopyFromAsync_After_KernelWrite_NoExplicitSync` + sync sibling). GitHub commit `575237f`.

### Wasm: wait/notify dispatcher barriers re-confirmed to race on V8 (spin-wait stays)

Re-validated the rc.25/rc.27 fallback from `memory.atomic.wait32`/`notify` dispatcher barriers to pure spin. With the new default-off `WasmBackend.UseWaitNotifyBarriers` flag ON (dispatcher phase + group barriers converted to `notify(INT_MAX)` + `wait32(1ms, spurious-wakeup defense)`), large multi-group RadixSorts corrupt on current Chrome (1.4M: 1067 sort-order violations, 500K: 187, 1M: duplicate keys) while small single-group sorts pass. This is a memory-visibility failure, not timeout logic — our codegen is seq_cst-correct — so it's a V8 linear-memory wait/notify ordering bug (chromium#490434403 family). The April "275-local spill" theory is disproven: the barrier lives in the ~38-local dispatcher and still races. Pure spin avoids the buggy futex path and remains correct. The flag is retained, **default false**, purely as a one-flip re-test harness for when a future V8 ships a FutexEmulation fix. Guard test: `WasmTests.WasmWaitNotifyBarriersDefaultOffTest`. Full investigation: `Plans/wasm-waitnotify-still-races-2026-05-24.md`. GitHub commit `cd163d3`.

## 4.9.8 (2026-05-23) — WebGL device probe no longer leaks a context per registration

### WebGLDevice constructor was leaking one WebGL2 context per registration

`WebGLDevice` is constructed (via `WebGL()` builder extension / `AllAcceleratorsAsync()`) for every page that probes available accelerators, even when the app never selects the WebGL backend. The constructor created a 1×1 `OffscreenCanvas` plus a `WebGL2RenderingContext`, read capability values (max texture size, max UBO size, max TF components, renderer string, vendor string) into fields, then **stored the canvas+context in fields and never read them again**. Browsers throttle WebGL contexts (~16 per page on Chromium); apps with many demo pages or repeated context creation hit "too many active WebGL contexts" warnings.

The fix: the probe canvas and context are now `using`-scoped inside the constructor and disposed before it returns. All capabilities are still cached. Per-accelerator contexts are unchanged — `CreateContext()` still mints fresh canvas + context per `WebGLAccelerator` instance, owned by that accelerator and disposed when it disposes.

Surfaced by Captain on the SpawnDev.ILGPU.ML demo: navigating between WebGPU-backed demo pages produced a Chrome console warning about too many WebGL contexts despite WebGL never being selected.

## 4.9.7 (2026-05-22) — WebGPU pow negative-base fix

### WebGPU: pow(negative_base, runtime_exponent) now returns correct result (not NaN)

WGSL's `pow(x, y)` is undefined for `x < 0` and returns NaN. The rc.21 WebGL fix applied a runtime-safe guard (static constant exponents get expanded to multiplications; runtime exponents get a branch), but the WGSL paths in `WGSLKernelFunctionGenerator.cs` and `WGSLCodeGenerator.cs` were still emitting raw `pow(left, right)`.

The guard is now applied to all three WGSL pow emission sites:

```wgsl
pow(abs(base), exp) * select(1.0, -1.0, (base < 0.0) && ((abs(exp) % 2.0) >= 1.0))
```

Note: uses `abs(exp) % 2.0` instead of `exp % 2.0` because WGSL `%` is the IEEE remainder (can be negative for negative `exp`), while the even/odd test requires a non-negative result. Matches GLSL's `mod(exp, 2.0)` behavior for all real-world ONNX exponent values.

Surfaced by `Tests23_PowNegativeBase_ExponentFromBuffer_NoNaN` (`pow(-0.037, exp_from_buffer=2)` → NaN on WebGPU). Both WebGPU pow tests pass.

## 4.9.6 (2026-05-22) — PTX vector memory intrinsics + CUDA register fix

### PTX vector memory intrinsics (ILGPU.Algorithms.PTX)

New `PTXMemory` class in `ILGPU.Algorithms.PTX` namespace exposes explicit PTX vector memory operations for CUDA kernels. These generate the NVIDIA PTX instructions `ld.v2.f32`, `ld.v4.f32`, `st.v2.f32`, `st.v4.f32` directly, enabling peak-bandwidth coalesced memory access on all Ampere/Turing/Volta and newer GPUs.

- `PTXMemory.LoadF32x2(ref float)` / `LoadF32x4(ref float)` - vectorized load returning `Float2` / `Float4`
- `PTXMemory.StoreF32x2(ref float, Float2)` / `StoreF32x4(ref float, Float4)` - vectorized store from struct
- `PTXMemory.StoreF32x2(ref float, float, float)` / `StoreF32x4(ref float, float, float, float, float)` - scalar-argument forms
- `ArrayView<float>` convenience overloads for all of the above (index-based instead of ref)
- New `Float2` and `Float4` readonly structs (`StructLayout.Sequential`)

These are CUDA-only; calling them on non-PTX backends throws `NotImplementedException`. Contributed by `ilehtoranta` (PR #4).

### ArrayView vectorized load/store helpers

New extension methods in `ILGPU.Runtime.ArrayViewExtensions`:

- `ArrayView<T>.LoadVectorized<T, TVector>(long elementIndex, int alignmentInBytes)` - loads a struct `TVector` at the given element index with explicit alignment hint (uses `AsAligned` + `Cast`)
- `ArrayView<T>.StoreVectorized<T, TVector>(long elementIndex, TVector value, int alignmentInBytes)` - stores a struct at the given element index with alignment hint
- `ArrayView<T>.CastAligned<T, TOther>(int alignmentInBytes)` - aligned cast convenience wrapper
- `ArrayView1D<T,Dense>` overloads for all of the above delegating to `BaseView`

### CUDA: DefaultMaxRegistersPerThread reverted to 0 (Discussion #5)

`CudaAccelerator.DefaultMaxRegistersPerThread` changed from `255` back to `0` (pre-rc.24 behavior). With `255`, ptxas treated the limit as a permissive ceiling and chose higher-register code paths on normal kernels (e.g., 94 registers instead of 42), cutting occupancy in half and degrading throughput. Default `0` lets ptxas apply its occupancy heuristics as designed. Set to `255` explicitly only if a kernel overflows with `CUDA_ERROR_LAUNCH_OUT_OF_RESOURCES`. Reported by `ilehtoranta` (Discussion #5).

### System.Numerics.BitOperations: hardware GPU intrinsics

`RemappedIntrinsics.RegisterBitOperationsRemappings()` now maps `System.Numerics.BitOperations.LeadingZeroCount`, `PopCount`, and `TrailingZeroCount` to the hardware-backed `IntrinsicMath` methods (annotated with `[MathIntrinsic(CLZ/PopC/CTZ)]`) instead of the software `IntrinsicMath.BitOperations` fallbacks. On all hardware-capable backends these now lower to native GPU instructions.

## 4.9.5 (2026-05-22) — Stable release

Stable promotion of rc.25 through rc.28 plus local.17 and PMT infrastructure fixes. All 6 backends (WebGPU, WebGL, Wasm, CUDA, OpenCL, CPU) pass the full test sweep with zero real failures.

### What's new since 4.9.4

- WebGPU direct-param coalesce: kernels with >9 `ArrayView` parameters no longer hit Chrome's 10-binding limit
- WGSL/WebGL codegen correctness for `[NoInlining]` helpers with 64-bit indices, sub-word ArrayViews, and cross-block pointer LEAs
- WebGL multi-view body-struct kernel parameter decomposition (rc.25-26)
- WebGPU body-struct output field exclusion from coalesce (rc.27)
- IR Inliner cumulative-IL budget: kernels with deep call graphs (VP9 entropy walker, etc.) no longer produce 50K+ local Wasm/WGSL functions that crash browser compilers
- PMT test discovery parser fix: prevents initialization console output from becoming phantom test names

See rc.25, rc.26, rc.27, rc.28 and local.5-17 entries below for per-fix details.

## 4.9.5-rc.28 (2026-05-05) — WebGPU/WebGL codegen correctness + IR Inliner cumulative budget

Nuget.org build bundling local.5 through local.16. Zero real test failures across all 6 backends (269 pass / 4 skip / 0 fail).

### WebGPU: direct-param coalesce (local.5-6)

Kernels with more than 9 `ArrayView` parameters previously hit Chrome's 10-binding limit at dispatch time. Direct-param coalesce groups same-typed input-only `ArrayView` kernel parameters into a single shared storage buffer binding when the raw count would exceed the limit. v1 covers `i32`/`u32`/`f32`; v2 adds sub-word types (`Int8`/`UInt8`/`Int16`/`UInt16`/`Float16`) packed via `array<atomic<u32>>` with element-count offsets. Closes Tuvok's "11 storage buffer bindings, device max 10" on the AV1 walker.

### WGSL: i64 offset and shift codegen in helper functions (local.7-9)

Three sequential fixes closing WGSL compilation errors in kernels that use `[NoInlining]` helpers with 64-bit array indices:

- **LEA i64 wrap** (`local.7`) - `WGSLFunctionGenerator.GenerateCode(LoadElementAddress)` now mirrors the kernel-side wrap at `WGSLKernelFunctionGenerator:4445`; detects `Int64` offset and wraps with `i64_to_i32(...)`. Closes `var v_X : i32 = vec2<u32>` Naga error from long `cdfBase` offsets.
- **i64 shift dispatch** (`local.8`) - `WGSLCodeGenerator.GenerateBinOp` shift branch routes emu_i64/emu_u64 LHS through `i64_shl`/`i64_shr`/`u64_shr` instead of raw `>>`. Closes `vec2<u32> >> u32` Naga error.
- **Sub-word LEA cross-block hoist** (`local.9`) - sub-word LEA `var v_X : i32` was block-scoped; cross-block uses failed Naga with "unresolved value". Fix: hoist to function scope via deferred-decl list when LEA is in `_crossBlockPointers`. Also adds defensive base `BinaryArithmetic` emu_i64 dispatch so helper-side `long a + long b` routes through library helpers instead of emitting component-wise `vec2<u32>`.

### WGSL: helper-side monomorphization + cross-case substitution (local.10-11)

- **Bug D phase 7 - helper signature monomorphization** (`local.10`) - `NoInlining` helpers that take `ArrayView` parameters from shared or local address spaces now get a separate WGSL binding per address space. Kernel scans `MethodCall` sites, records `(param, addressSpace)` pairs into a shared dict; helper signature emit reads from the dict. Closes `ptr<workgroup>` vs `ptr<storage>` Naga rejection at call sites.
- **Cross-case substitution** (`local.11`) - second-pass post-process in `WGSLCodeGenerator` text-substitutes `*v_X` with the full deref expression for complex pointer LEAs that span switch-case blocks in the helper state machine. Closes "unresolved value" errors on multi-block helpers with pointer arithmetic.

### WebGPU/WebGL: GroupDimX clamp + explicit-launch param offset (local.12)

- **Group.DimX clamp** - `GroupDimensionValue` X-dim emit overridden to `i32(min(workgroup_size.x, _ilgpu_user_dim))` so auto-grouped kernels with fewer elements than workgroup size don't read out-of-bounds. Closes `Tests23_GroupDimX_Clamps_To_Extent_OnUnitDispatch` on WebGPU/WebGPUNoSubgroups.
- **WebGL explicit-launch param offset** - `KernelParamOffset` dynamic detection mirrors WGSL; `WebGLAccelerator.MarshalArguments` glslParamOffset alignment fixed. Closes `Tests23_RegisterHeavyBody_ExplicitOneByOne` on WebGL.

### WGSL: helper-side emu-64 ArrayView raw u32 storage (local.13)

Helper function signatures for `ArrayView<long>`/`<ulong>`/`<double>` parameters now emit `array<u32>` matching the kernel's raw-bits binding. LEA/Load/Store in helpers mirror the kernel's stride=2 raw u32 access pattern. Closes Tuvok walker L44452 fn-def call-site type mismatch ("expected `array<u32>`, got `array<i64>`").

### WebGL: i64 shift dispatch + emulation library forward declarations (local.14)

- **i64 shift dispatch** - `GLSLCodeGenerator.GenerateBinOp` now routes `uvec2` LHS with `Int64` BasicValueType through `i64_shl`/`i64_shr`/`u64_shr` GLSL helpers instead of raw component-wise `uvec2 >> int`. Closes silent-wrong-output on kernels with 64-bit shift in helpers.
- **Helper emulation library forward declarations** - ~30 prototype forward declarations emitted at the top of the helper builder so `i64_shr` and friends resolve before the library definition appears later in the merged output. Closes "no matching overloaded function found" linker error.

### IR Inliner: cumulative IL budget (local.16-17)

ILGPU's `Inliner.SetupInliningAttributes` in Aggressive mode (the default) previously inlined every method regardless of size. For kernels with deep call graphs (e.g. Tuvok's VP9 `EncodeFrameKernel` - recursive partition tree + bool-coder helper graph), this produced a single Wasm function with 52,012 locals and 750 KB of instruction bytes. V8's TurboFan and Naga both reject beyond ~50K locals; the result was a compile-time crash inside the browser, not a runtime error.

- **local.16** - Per-function cap: `MaxNumILInstructionsAggressiveCap = 1024`. Methods over the cap get `MethodFlags.FunctionDefinition` (emitted as a WGSL `fn` / Wasm function call, not inlined). `[AggressiveInlining]` and ILGPU-internal helpers bypass the cap via `MethodFlags.ForceInline`.
- **local.17** - Cumulative budget: `cumulativeInlinedIL` seeds from the kernel's own IL count; non-`ForceInline` calls that would push the running total past 16384 are left as fn-defs. This handles the VP9 fan-out case (4-deep partition tree, 50K+ IL when fully inlined) where every individual helper was under the per-function cap but the total exploded. Regression test `Tests23_DeepInlineTree_BudgetDoesNotBreakCorrectness` locks down correctness under the budget.

## 4.9.5-local.17 (2026-05-05) — IR Inliner cumulative-IL budget (closes Tuvok codecs Wasm)

Local-feed-only build. See rc.28 entry above for full description of the inliner cumulative budget mechanism.

## 4.9.5-local.16 (2026-05-05) — IR Inliner aggressive-mode IL instruction count cap

Local-feed-only build. See rc.28 entry above for description.

## 4.9.5-local.15 (2026-05-05) — version bump + release notes

Local-feed-only bump. No code changes beyond version metadata.

## 4.9.5-local.14 (2026-05-05) — WebGL i64 shift dispatch + helper emulation-library forward decls

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.13 (2026-05-05) — WGSL helper-side emu-64 ArrayView raw u32 storage

Local-feed-only build. See rc.28 entry above for full description. Closes Tuvok walker L44452.

## 4.9.5-local.12 (2026-05-05) — WebGPU GroupDim X-clamp + WebGL explicit-launch param offset

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.11 (2026-05-05) — WGSL helper-side cross-case substitution for complex pointer LEAs

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.10 (2026-05-05) — Bug D phase 7 helper-side cross-block hoist + monomorphization

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.9 (2026-05-05) — WGSL sub-word LEA cross-block hoist + defensive emu_i64 BinaryArithmetic

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.8 (2026-05-05) — WGSL i64 emulation library shift dispatch

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.7 (2026-05-05) — WGSL helper-side LEA i64 offset wrap

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.6 (2026-05-05) — WebGPU direct-param coalesce v2 (sub-word)

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.5 (2026-05-05) — WebGPU direct-param coalesce v1 (i32/u32/f32)

Local-feed-only build. See rc.28 entry above for full description.

## 4.9.5-local.4 (2026-05-05) — Wasm: explicit-launch body-struct kernel implicit-index detection + safer struct serialization

Local-feed-only build. Closes `WasmTests.Tests23_RegisterHeavyBody_ExplicitOneByOne_NoLaunchFailure`
(`MarshalDirectiveException: Type ILGPU.Util.DisposeBase ... must have a StructLayout attribute`).

### Root cause

Two interacting failures, both in WasmKernelFunctionGenerator parameter setup +
WasmAccelerator dispatcher fallback:

1. **`hasImplicitIndex` mis-classification.** `WasmKernelFunctionGenerator.SetupParameters` checked `IndexType != IndexType.None && !IsViewType(parameters[0].Type)` to decide whether `Method.Parameters[0]` was an implicit Index. For an explicit-launch kernel `LoadStreamKernel<TBody>` whose first user parameter is a multi-view body struct (12 ArrayView<int> fields):
   - `entryPoint.IndexType == IndexType.KernelConfig` (not `None`).
   - `IsViewType(body)` returns false (multi-view).
   - So `hasImplicitIndex` was true and `startIdx = 1`. The body's parameter loop ran `for (int i = 1; i < 1; i++)` — never executed. `_paramInfos` stayed empty.

2. **Dispatcher fallback used `Marshal.StructureToPtr`.** With empty `paramInfos`, the dispatcher's IR-aware struct-serialization path at `WasmAccelerator.RunKernelAsync:1115` failed its `irLayout != null && irLayout.Count > 0 && irStructSize > 0` gate and fell through to `Marshal.StructureToPtr(value, ...)`. ArrayView<T>'s `Buffer` field is a `MemoryBuffer` reference, which derives from `AcceleratorObject` -> `DisposeBase` — none have `[StructLayout]`. The CLR marshaler walked up the inheritance, hit `DisposeBase`, and threw.

### Fix

1. **`LooksLikeIndexType` shape check.** New helper in WasmKernelFunctionGenerator that requires `parameters[0]` to actually look like an index type (PrimitiveType for Index1D/LongIndex1D/KernelConfig, or StructureType with exactly N Int32 leaf fields where N matches `entryPoint.IndexType`'s dimensionality for Index2D/Index3D). Multi-view body structs return false. The `hasImplicitIndex` check now AND's against this — multi-view body structs as `parameters[0]` correctly resolve to `startIdx = 0`, the loop runs once, and paramInfos contains the body's struct layout.

2. **Unsafe.Write everywhere.** Replaced the `Marshal.StructureToPtr` fallback branch with `Unsafe.Write` (which was previously gated on `IsGenericType`). Unsafe.Write is layout-agnostic — works for any value-type struct including those with class-reference fields. Defense in depth: if a future code path falls through to this fallback again, it won't crash with a marshaling exception (though the kernel-side bytes will be wrong without view-pointer patching, signaling a different bug to investigate). The IR-aware path remains the primary serialization route.

### Verified

- `WasmTests.Tests23_RegisterHeavyBody_ExplicitOneByOne_NoLaunchFailure`: PASS (was FAIL `MarshalDirectiveException`).
- `WasmTests.Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure`: PASS.
- `WasmTests.Tests23_RegisterHeavyBody_LargeExtent_Parallel`: PASS.
- `WasmTests.Tests23_OnlyShortBodyStruct`: PASS.
- `WasmTests.Tests23_HostWriteVsQueuedDispatchRace`: PASS.
- `WasmTests.Tests23_DecodeUint_LongForm_CompileSmoke`: PASS.
- `WasmTests.AlgorithmRadixSortPairsIntTest`: PASS.
- `WasmTests.AlgorithmRadixSortNonPairsIntTest`: PASS.
- 8/8 across the targeted regression filter, 0 failures.

## 4.9.5-local.3 (2026-05-05) — Wasm: lazy per-HWC snapshot fixes RadixSort regression + perf

Local-feed-only build. Fixes the RadixSort multi-pass data-corruption regression
(TJ report 2026-05-05; tests `WasmTests.AlgorithmRadixSortNonPairs{Int,Float}Test`
"got 32 expected 1" — input order returned unchanged) AND the StyleMosaic ML-weight
5GB-allocation perf regression in ONE coherent mechanism.

### Root cause

The eager queue-time snapshot path introduced in `4222bef`/`fab13fd` (rc.11) and
extended in `2fa321f` (rc.13) had two interacting failures:

1. **Cached pre-write data pinned across GPU writes.** Multi-pass kernels that sort
   in-place (RadixSort) queue back-to-back dispatches with no host writes between.
   The cache check `_lastSnapshottedGpuWriteSeq` in `GetOrCreateSnapshotForDispatch`
   ran AT QUEUE TIME, before the previous dispatch's task had executed and bumped
   `_gpuWriteSeq`. So pass-2's snapshot capture happened with `_gpuWriteSeq` still
   at the pre-pass-1 value and returned the cached pre-pass-1 snapshot. Pass-2's
   copy-IN replayed the original input. Final read returned input order unchanged.

2. **Eager allocation for read-only buffers.** Every dispatch's queue-time scan
   allocated a fresh SharedArrayBuffer the first time a buffer was referenced
   (and re-allocated whenever `HostWriteCounter` advanced). For ML pipelines
   uploading weights once and reading 100+ times, the 50MB weight buffer was
   re-snapshotted 100 times × no perf benefit since weights never change. Data's
   StyleMosaic 11+ minute hang at rc.13 was 5GB of wasted SAB allocations.

### Fix: lazy per-HWC snapshot tier

`WasmMemoryBuffer` now tracks dispatch intents instead of taking eager snapshots:

- **At RunKernel queue time (sync):** each ArrayView arg's underlying buffer
  registers a dispatch intent. The intent records the current
  `HostWriteCounter` value (qhwc). NO bytes are copied at this point.
- **At any host-write path (CopyFromHost / CopyFromJS / CopyFrom):** `PrepareHostWrite`
  is called BEFORE the SharedBuffer is mutated. If at least one dispatch intent is
  pending and no snapshot exists at the current HWC tier, the CURRENT (pre-write)
  SharedBuffer is captured into a fresh SAB keyed by HWC. Refcount = number of
  pending intents at that moment (every pre-write intent shares this tier).
- **At dispatch start (copy-IN):** if the buffer has a snapshot tier matching the
  dispatch's qhwc AND `HostWriteCounter > qhwc`, copy-IN reads from the snapshot.
  Otherwise reads SharedBuffer directly (which carries every prior dispatch's
  copy-OUT data — exactly what multi-pass RadixSort needs).
- **At dispatch end (copy-OUT):** buffers whose copy-IN read from a snapshot have
  their copy-OUT skipped (writing the snapshot data back to SharedBuffer would
  clobber the host write that subsequent dispatches need). All other buffers
  copy-OUT normally.
- **At dispatch completion:** `CompleteDispatchIntent(qhwc)` decrements the tier's
  refcount; the SAB is released when the last referencer completes. All tiers are
  released when the buffer's pending-intent count returns to zero.

### Perf characterization

- **ML weight reuse (StyleMosaic 102-dispatch pipeline):** `CopyFromCPU(weights)`
  fires once with no pending intents → no snapshot. 102 subsequent dispatches
  register and complete intents at the same HWC → no host writes → no snapshot
  ever materialized → ZERO snapshot allocations. (Was 5GB pre-fix.)
- **Multi-pass RadixSort:** all passes register intents at qhwc=initial-HWC.
  No host writes during sort → no snapshot. Each pass's copy-IN reads SharedBuffer
  carrying the prior pass's copy-OUT data. Each pass's copy-OUT writes back.
- **Host-write race (Tests23_HostWriteVsQueuedDispatchRace):** D1 queued at HWC=1.
  CopyFromCPU triggers `PrepareHostWrite` → tier @ HWC=1 captured. HWC bumps to 2.
  D2 queued at HWC=2 — no tier @ HWC=2. D1 reads tier[1] (pre-write data). D1's
  copy-OUT skipped for the snapshot-sourced buffer (preserves SharedBuffer = 200).
  D2 reads SharedBuffer = 200. Both dispatches see the data they queued with intent for.

### Verified

- `WasmTests.AlgorithmRadixSortNonPairsIntTest`: PASS (was FAIL "got 32 expected 1").
- `WasmTests.AlgorithmRadixSortNonPairsFloatTest`: PASS (was FAIL "got 128 expected 2").
- `WasmTests.AlgorithmRadixSortPairsIntTest`: PASS.
- `WasmTests.Tests23_HostWriteVsQueuedDispatchRace`: PASS (snapshot defense + copy-OUT skip both wired).
- (Plus broader RadixSort + body-struct + sub-word sweep — see DevComms publish-log row.)

### Files

- `SpawnDev.ILGPU/Wasm/WasmMemoryBuffer.cs` — lazy snapshot mechanism replacing the
  eager `GetOrCreateSnapshotForDispatch` / `_gpuWriteSeq` / `NotifyGpuWrite` API.
- `SpawnDev.ILGPU/Wasm/WasmAccelerator.cs` — RunKernel registers intents (was: eager
  snapshots); copy-IN tracks per-buffer "read from snapshot" set; copy-OUT skips
  those at write-back; finally-block completes intents.

## 4.9.5-local.2 (2026-05-05) — Wasm linear memory: drop 100% margin, exact + 1-page pad

Local-feed-only build closing Data's StyleMosaic 2 GiB OOM.

### Root cause

`WasmAccelerator.RunKernelAsync` was computing `wasmPages = ceil(totalWithBarriers / 65536) * 2` — doubling every linear-memory request as "100% margin." The exact `totalWithBarriers` arithmetic already accounts for every region (buffers + per-thread/per-worker scratch + struct + shared memory + barriers + fence + per-worker yield state); the doubling was gratuitous defensive padding originally added when worker count grew from a hardcoded 4 to `navigator.hardwareConcurrency` (commit `856d8cb`).

For ML inference workloads with intermediate tensors that push a single dispatch's actual layout past ~1 GiB, the doubled request crossed the browser's 2 GiB SharedArrayBuffer cap and `WebAssembly.Memory.grow()` rejected with `RangeError: Maximum memory size exceeded`. Verified path: Data's `StyleMosaic_DiagnosticPerOpSync` failed at ~17s, Chromium working set at 4.2 GB (SAB component bounded by `MaxLinearMemoryPages * 64 KiB` = 2 GiB).

### Fix

`wasmPages = wasmPagesExact + 1` where `wasmPagesExact = ceil(totalWithBarriers / 65536)`. The 1-page (64 KB) absolute pad absorbs any single-byte miscalculation without doubling the linear-memory footprint. Effective cap usage drops from `~totalLayout / 2` to `~totalLayout - 64 KB`, recovering the lost half of the 2 GiB ceiling.

### Diagnostics added

Per Data's request, a `WasmBackend.VerboseLogging`-gated log fires at every dispatch boundary with the full layout breakdown:

```
[Wasm-MEM] disp=N totalLayout=B exactPages=P pages=P+1 bytes=... cap=... buf=... scratch=... struct=... shared=... barrier=... fence=... spt=... gs=... _wc=...
```

Plus dedicated single-line logs on memory init / grow / reuse events:

- `[Wasm-MEM-INIT]` / `[Wasm-MEM-INIT-CC]` — first allocation per accelerator (or concurrent-context init).
- `[Wasm-MEM-GROW]` / `[Wasm-MEM-GROW-CC]` — explicit growth, with from/to/growBy/cap.
- `[Wasm-MEM-REUSE]` — cache reuse, with need vs cached.

`OutOfMemoryException` thrown on grow failure now includes the cap value alongside current and requested page counts.

### Verified

- Build clean; existing Wasm tests in the body-struct + sub-word + algorithm areas (Tests23_RegisterHeavyBody, Tests23_OnlyShortBodyStruct, Tests23_TwoShortBodyStruct*, AlgorithmReduceByteTest, AlgorithmGroupReduceHalfTest, LocalMemoryRepro_Int64_ShortByteViews) regression sweep: same green/known-fail status as before this change.
- Pre-existing Wasm test failures (NOT caused by this change, verified by running the same tests on master before this commit): `WasmTests.AlgorithmRadixSortNonPairsIntTest` + `WasmTests.AlgorithmRadixSortNonPairsFloatTest` (data corruption, "Expected 1, got 32" — separate root cause, tracked as Rule 2a follow-up); `WasmTests.Tests23_RegisterHeavyBody_ExplicitOneByOne_NoLaunchFailure` (`MarshalDirectiveException` on `ILGPU.Util.DisposeBase` — separate root cause, tracked as Rule 2a follow-up).

### What downstream needs to do

Bump `SpawnDev.ILGPU.ML` PackageReference to `4.9.5-local.2` and re-run StyleMosaic_DiagnosticPerOpSync on Wasm. Expected outcome: passes (or fails for a DIFFERENT reason inside the kernel, in which case the new per-dispatch verbose log will pinpoint the offending dispatch).

## 4.9.5-local.1 (2026-05-05) — WGSL Bug D phases 2/3/5 + Tuvok's i64 emul-lib scan fix

Local-feed-only build for Tuvok to consume the rc.16 fn-definition codegen
fix on his AV1 walker (commit `a203e5e`).

### What landed

- **Phase 2** — sub-word ArrayView fn-params (byte/sbyte/short/ushort/Half) now flow through standalone-fn-def helpers on WebGPU. `WGSLFunctionGenerator` overrides `LoadElementAddress` / `Load` / `Store` to emit the kernel's atomicLoad+shift+mask+sign-extend chain with the helper's local ptr alias as the binding.
- **Phase 3** — helper-to-helper non-inline calls now emit a real WGSL fn call (was "Unmapped fallback" emitting `v_X = i32(0)`).
- **Phase 5** — verified `EmitNonInlinedMethodCall` correctly routes the kernel's ptr alias from Phase 4 to NoInlining helpers' ArrayView args.
- **Cross-block field address** — helper `LoadFieldAddress` registers a dereffed inline expression so `Load` / `Store` in different switch-case blocks substitute `(*src).field` instead of referencing a case-scoped `let`.
- **AddressSpaceCast on local alloca in helper** — matches kernel behavior of registering `&v_local` as the cast result so ref-style args land as ptrs at call sites.
- **GLSL function-return fallback** — struct return type now emits a constructor with one zero per field (was rejected with "Number of constructor parameters does not match").
- **WebGL struct-defs ordering** — top-of-file placeholder set up in `CreateKernelBuilder`; struct definitions now land BEFORE helper functions that use them as parameter types.
- **Tuvok's i64_ge / i64_eq missing-definition fix** — `SetEmulationFlags()` and `ScanForSubgroupAndBroadcastUsage()` now walk a unified `EnumerateAllHelperMethods()` that includes both Inline-flagged and NoInline-flagged helpers via a new `GeneratorArgs.NonInlineMethods` list. NoInlining helpers with 64-bit ops (e.g. AV1 walker `long cdfBase`, `state.Low` u64) used to leave `KernelUsesI64=false` so the WGSL emit called `i64_ge` / `i64_eq` without their definitions; now they're seen and the emulation library is pulled in.

### Verified passing post-local.1

- `Tests23_DecodeUint_LongForm_CompileSmoke` — 6/7 backends pass (WebGPU 622ms, WebGPUNoSubgroups 213ms, Wasm 272ms, CPU + CUDA + OpenCL pass), WebGL skipped via `UnsupportedTestException` (GLSL ES has no pointer types - ArrayView fn-params can't go through standalone fn defs; tracked as Bug D follow-up).
- 23 body-struct + sub-word + algorithm regression tests pass (LocalMemoryRepro_Int64_ShortByteViews, Tests23_OnlyShortBodyStruct, Tests23_TwoShortBodyStruct*, Tests23_RegisterHeavyBody_*, AlgorithmGroupReduceHalfTest across WebGPU + WebGPUNoSubgroups + Wasm).

### Known caveats

- WebGL fn-def-with-ArrayView path requires force-inline or signature redesign. Force-skipped via `UnsupportedTestException` for the smoke test.
- f16 emulation `_kernelReferencesF16Helpers` is per-instance and would still miss a non-inline helper's `Half` use; theoretical until tested.
- Wasm bitstream divergence on dead-code helpers (Tuvok's symptom 2) — separate bug, not investigated yet.

## 4.9.5-rc.27 (2026-05-05) — WebGPU coalesce excludes OUTPUT body-struct view fields

### Fix: Tests23_RegisterHeavyBody on WebGPU was reading 0 instead of 5194

When a body struct contains an OUTPUT view (e.g. `Tests23_RegisterHeavyBody { ArrayView<int> A0..A10; ArrayView<int> Out; }`), pre-rc.27 WGSL coalesce bundled all 12 same-typed views (A0..A10 + Out) into a single shared GPU storage buffer (`param1_i32_coalesced`). The host-side coalesce path in `WebGPUAccelerator.MarshalArguments` concatenates INPUT data into the shared buffer at dispatch time via `CopyBufferToBuffer`, then binds the shared buffer ONCE.

Output writes never copy back. The kernel's `*v_80 = v_20;` (where `v_80 = &param1_i32_coalesced[scalar_params[11] + 0]`) writes the sum into the **shared input buffer** at slot 11, not into `bOut`'s actual GPU storage. Host reads `bOut`'s storage post-dispatch and finds it untouched (zero).

### Fix

New `ScanForBodyStructOutputs` walks every `Store` / `GenericAtomic` / `AtomicCAS` IR op in the kernel + every helper method it calls, resolves the target via `ResolveToParameterWithFieldChain`, and adds `(paramIdx, fieldIdx)` to `_bodyStructOutputFields` whenever the target is a body-struct view field.

`DecideCoalesceGroups` then adds an early-`continue` for any field in `_bodyStructOutputFields` — output fields keep their own per-field binding, so kernel writes hit the actual output ArrayView's GPU storage and the host reads back correctly.

### Verified passing post-rc.27

- `WebGPUTests.Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure` (was FAIL, now PASS)
- `WebGPUNoSubgroupsTests.Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure` (was FAIL, now PASS)
- All other body-struct WebGPU + WebGL + Wasm + CPU + Cuda + OpenCL tests still PASS (42/42 PMT body-struct sweep)
- 14/14 desktop regression sweep PASS

### Files changed

- `SpawnDev.ILGPU/WebGPU/Backend/WGSLKernelFunctionGenerator.cs` — added `_bodyStructOutputFields` field, `ScanForBodyStructOutputs` method (walks Store/Atomic IR ops in kernel + helpers via `ResolveToParameterWithFieldChain`), early-continue in `DecideCoalesceGroups`. Wired into constructor between `ScanBodyStructParams` and `DecideCoalesceGroups`.

## 4.9.5-rc.26 (2026-05-05) — WebGL body-struct rc.25 v2 follow-ups CLOSED — all 8 tests PASS

### What changed

Two fixes that close the two remaining body-struct WebGL tests:

**(1) Single-view body struct unwrap** — `Tests23_OnlyShortBodyStruct` repro:
```csharp
public struct Tests23_OnlyShortStruct { public ArrayView<short> S0; }
static void Tests23_OnlyShortKernel(Index1D _, Tests23_OnlyShortStruct s, ArrayView<int> output)
{
    output[0] = (int)s.S0[0];
}
```

ILGPU's IR pre-transforms the param: `Tests23_OnlyShortStruct` (struct holding one `ArrayView<T>`) collapses to `ViewType<T>` directly. The kernel parameter ends up at IR position 1 with `param.ParameterType = ViewType<Int16>`, not a `StructureType`. So `ScanBodyStructParams` never matches, the standard view path emits a single `u_param1` sampler — but the host-side `MarshalArguments` doesn't know the IR unwrapped the struct, passes the struct value through `FlattenStructFields` for uniform packing, no buffer ever binds to `u_param1`, texelFetch returns zero, kernel writes 0 to output.

Fix: `WebGLAccelerator.MarshalArguments` now walks struct args with no `BaseView` property. If the struct has exactly **one** IArrayView field, unwrap to that view — mirrors the IR transform host-side.

**(2) ArrayView1D wrapper metadata fields always intercepted** — `Tests23_TwoShortBodyStructDense` repro:
```csharp
public struct Tests23_TwoShortStructDense
{
    public ArrayView1D<short, Stride1D.Dense> S0;
    public ArrayView1D<short, Stride1D.Dense> S1;
}
```

ArrayView1D wrapper flattens in IR to `(View, Int64-length, optional Int8-Dense-flag)` per wrapper, so the body struct has IR field count 4 (or 6 with Dense flags). The rc.25 state machine used WGSL's `isLastField` heuristic to disambiguate the trailing Int64 (could be length OR a user reduce-value scalar). In GLSL, `GenerateCode(GetField)` falls through to a 2D-view stride emit when a body-struct GetField doesn't match — emitting `u_param{N}_stride[0]` which was never declared and broke compile.

Fix: rc.26 unconditionally marks Int64-after-View as IsViewMetadata. The GetField hook now also intercepts metadata fields, emitting a length-uniform reference (`i64_from_i32({BindingName}_length)`). Critically, metadata fields share the **associated view's binding name** (so `u_param1_f1_length` aliases to `u_param1_f0_length` which is declared) — without this aliasing the metadata fields would reference their own undeclared synthetic uniform name.

### Test status post-fix (WebGL on 2026-05-05)

| Test | Pre-rc.26 | Post-rc.26 |
|------|-----------|------------|
| `Tests23_TwoShortBodyStruct` | PASS | **PASS** |
| `Tests23_SilkBodyStructShape` (Tuvok shape) | PASS | **PASS** |
| `Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure` | PASS | **PASS** |
| `Tests23_MinimalShortIntBodyStruct` | PASS | **PASS** |
| `Tests23_MinimalShortIntBodyStruct_IntOnly` | PASS | **PASS** |
| `Tests23_DeepUnroll_NoLaunchFailure` | PASS | **PASS** |
| `Tests23_OnlyShortBodyStruct` (single ArrayView<short>) | FAIL | **PASS** |
| `Tests23_TwoShortBodyStructDense` (ArrayView1D<,Dense>) | FAIL | **PASS** |

8/8 WebGL body-struct tests PASS. 12/12 desktop regression PASS (CUDA + OpenCL + CPU body-struct + invariant tests). No regression to WGSL / Wasm.

### Files changed

- `SpawnDev.ILGPU/WebGL/Backend/GLSLKernelFunctionGenerator.cs` — state machine drops `isLastField` heuristic; metadata fields' `BindingName` aliases to the preceding view's binding; GetField hook intercepts IsViewMetadata fields with length-uniform emit.
- `SpawnDev.ILGPU/WebGL/WebGLAccelerator.cs` — single-view-struct unwrap in `MarshalArguments` for IR-pre-unwrapped body structs.

## 4.9.5-rc.25 (2026-05-04) — WebGL multi-view body-struct kernel parameter codegen (#26)

### Fix: WebGL/GLSL now decomposes body struct kernel parameters into per-field sampler bindings

When a kernel takes a struct parameter whose fields include 2 or more `ArrayView<T>` (a "body struct" — e.g. Tuvok's `SilkDecodeCoreInputs` with 6 short + 9 int views, or `Tests23_RegisterHeavyBody` with 12 ArrayView<int> + 1 output), WebGL was misclassifying the param via `IsMultiDim` (line 3056-3098): NumFields ∈ {3,4,6} got routed as fake 1D/2D/3D ArrayView wrappers (so every field aliased the same single sampler), and NumFields outside {3,4,6} fell into a UBO struct path (where uniform field references received meaningless ArrayView pointer values). Both paths produced silent-wrong output.

WebGPU (WGSL) shipped this body-struct codegen support in rc.17 + rc.18 today; this rc ports the working WGSL pattern to GLSL.

### Approach (mirror of WGSL pattern)

1. **Synthetic param-index encoding** `(paramIdx + 1) * 1000 + fieldIdx` — every body-struct view field gets a virtual param-index ≥ 1000 that threads through `_leaParamMap`, `_subWordLEAVars`, `_inputParamIndices`, `_outputParamIndices`, and `_outputVaryings` like a regular param. Same encoding as WGSL.
2. **`_bodyStructParamsGL` infrastructure** — `BodyStructFieldInfoGL` per-field metadata, populated by `ScanBodyStructParams` in the constructor flow before the analyzers run.
3. **`IsBodyStruct` + `IsViewFieldType` helpers** — discriminate "body struct (≥2 view fields)" from single ArrayView wrappers; body-struct early-out in `EmitParameterDeclarations` runs BEFORE the `IsMultiDim` cascade.
4. **`EmitBodyStructDeclarations`** — one `uniform highp isampler2D u_param{N}_f{M}` (+ `_tileW`, `_offset`, `_length` companions) per ArrayView field. Sub-word fields (Int8/Int16/Half) register the synthetic index in `_subWordParams` so the existing texelFetch + shift+mask machinery picks them up.
5. **`GetParamBindingName(int paramIdx)` helper** — every `texelFetch(u_param{N}, ...)` emit site now calls this helper, which returns `u_param{N}` for direct params or `u_param{realN}_f{M}` for synthetic body-struct field indices via `_bodyStructFieldBindingNames`. Single source of truth.
6. **GetField + LoadElementAddress redirect** — when LEA's source is `GetField(bodyStructParam, fi)` with view field, the code path uses the synthetic param idx and emits `texelFetch(u_param{N}_f{M}, ...)` via the helper.
7. **TF varyings per body-struct OUTPUT view field** — `tf_out_param{N}_f{M}` keyed by synthetic param idx in `_outputVaryings`. Existing Store path's `_singleStoreIndex` / `_storeSlotIndex` / `_emulatedIndex` lookups already consult ParamIndex, so they find body-struct output varyings automatically.
8. **Host-side `EmitBodyStructDispatch` in `WebGLAccelerator.MarshalArguments`** — walks the user's body struct via reflection in IR-field order, emits one `kind = "buffer_ref"` per ArrayView field with synthetic param idx. Scalar fields go through `kind = "scalar"` with the same encoding. Output readback decodes `outputInfo.ParamIndex >= 1000` to extract the right ArrayView field for buffer-ID lookup.
9. **glWorker.js synthetic-index decoder** — new `resolveParamPrefix(paramIndex)` returns `u_param{N}` or `u_param{realN}_f{M}` based on `paramIndex >= 1000`. All sampler/scalar/emu64 bind sites consult it.
10. **Manifest** — new public `BodyStructBindingEntry` class on `WebGLBackend.cs` (parallel to WGSL's `CoalesceGroupEntry`) + `WebGLCompiledKernel.BodyStructManifest` flows from codegen to runtime for dispatch decomposition.

### What v1 supports

- `ArrayView<T>` and `ArrayView1D<T, Stride1D.Dense>` body-struct view fields
- Sub-word fields: Int8/Int16/UInt8/UInt16/Float16-emulated
- Mixed view + scalar field body structs (scalars packed via existing scalar uniform path with synthetic indices)
- Body-struct **output** view fields (Transform Feedback varying emission per field, demuxed to per-buffer at readback)

### What v1 throws on (deferred to v2)

- Emulated 64-bit body-struct view fields (`ArrayView<long>` / `ArrayView<double>` inside a body struct) — clear `NotSupportedException` directing to use separate kernel params
- Coalesce groups — WebGL's `MAX_TEXTURE_IMAGE_UNITS` is typically 16 (vs WebGPU's 8-10), more headroom; defer until needed by a real kernel
- Atomics on body-struct view fields (WebGL has no atomics anywhere; existing capability check already gates this)
- Packed-struct view fields inside body structs

### Test status post-fix (WebGL on 2026-05-04)

| Test | Pre-rc.25 | Post-rc.25 |
|------|-----------|------------|
| `Tests23_TwoShortBodyStruct` (2 × ArrayView<short>) | FAIL | **PASS** |
| `Tests23_SilkBodyStructShape` (Tuvok's actual: 6 × ArrayView<short> + 9 × ArrayView<int>) | FAIL | **PASS** |
| `Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure` (12 × ArrayView<int>) | FAIL | **PASS** |
| `Tests23_MinimalShortIntBodyStruct` (ArrayView<short> + ArrayView<int>) | FAIL | **PASS** |
| `Tests23_MinimalShortIntBodyStruct_IntOnly` | FAIL | **PASS** |
| `Tests23_DeepUnroll_NoLaunchFailure` (single-view regression) | PASS | **PASS** |
| `Tests23_OnlyShortBodyStruct` (single ArrayView<short>) | FAIL | FAIL (v2 follow-up) |
| `Tests23_TwoShortBodyStructDense` (ArrayView1D<,Dense>) | FAIL | FAIL (v2 follow-up) |

Desktop regression: 10/10 sample sweep PASS (CUDA + OpenCL + CPU Tests23). No regression in WGSL / WebGPU / Wasm paths from this rc — those backends remain fully working.

### What this unblocks

- **Tuvok's `SilkDecodeCoreGpu` WebGL path** — bump `SpawnDev.Codecs` to `SpawnDev.ILGPU 4.9.5-rc.25`. The shipped tests cover Tuvok's actual `Tests23_SilkBodyStructShape` (6 short + 9 int) shape end-to-end.
- **Any consumer kernel** using a struct param with multiple ArrayView fields — gets WebGL support automatically when the struct holds raw `ArrayView<T>` fields and isn't sized like an ArrayView wrapper.

### Known limitations (v2 follow-ups)

- **Single-view body struct** (`struct { ArrayView<short> S0 }`): kernel runs without GLSL compile error but reads zero. Likely a buffer-bind path issue specific to NumFields=1 body structs. Workaround: pass the ArrayView as a direct kernel parameter instead of wrapping it in a struct.
- **`ArrayView1D<T, Stride1D.Dense>` body fields**: GLSL compile error referencing missing `u_param_stride` uniform — the IR emits a multi-dim view access path for the wrapped struct that conflicts with the body-struct decomposition. Workaround: use raw `ArrayView<T>` body fields instead of the Dense wrapper.

Both v2 items have crisp repro tests (`Tests23_OnlyShortBodyStruct` and `Tests23_TwoShortBodyStructDense`) so they'll be the next gate.

### Files changed

- `SpawnDev.ILGPU/WebGL/Backend/GLSLKernelFunctionGenerator.cs` — body-struct infrastructure, EmitBodyStructDeclarations, LEA hook, GetParamBindingName helper, output varying emission per field
- `SpawnDev.ILGPU/WebGL/Backend/GLSLTypeGenerator.cs` — `GenerateTypeDefinitions(builder, skipTypeIds)` overload skips body-struct UBO emit
- `SpawnDev.ILGPU/WebGL/Backend/GLSLCodeGenerator.cs` — `GeneratorArgs.BodyStructTypeIdsToSkip` + `BodyStructManifest` plumbing
- `SpawnDev.ILGPU/WebGL/Backend/WebGLBackend.cs` — `BodyStructBindingEntry` class + `WebGLCompiledKernel.BodyStructManifest`
- `SpawnDev.ILGPU/WebGL/WebGLAccelerator.cs` — `EmitBodyStructDispatch` host-side decomposition + output-readback synthetic-index decoding
- `SpawnDev.ILGPU/wwwroot/glWorker.js` — `resolveParamPrefix` synthetic-index decoder

## 4.9.5-rc.24 (2026-05-04) — CUDA body-struct ArrayView-field alignment ROOT-CAUSE FIX (#32)

### Fix: ViewType.Alignment was hardcoded 4, should be 8 on 64-bit targets

ILGPU IR `ViewType` constructor at `ILGPU/IR/Types/PointerTypes.cs` was hardcoding `Size = Alignment = 4` regardless of target platform pointer size. After `LowerViews` runs, every `ViewType` becomes a `StructureType` of `{void*, long}` = 16 bytes, 8-byte aligned (per the host-side `ViewImplementation<T>` layout that the argument mapper produces). But the IR carried the pre-lowered `Alignment = 4` metadata when computing body struct alignment.

Body struct containing 2+ `ArrayView<T>` fields propagated `Alignment = max(4, 4) = 4` up. PTX emitted

```ptx
.param .align 4 .b8 _s_91[32]   // 32 bytes, but only 4-byte aligned
```

while the host-side argument mapper laid out two `ViewImplementation<T>` fields at 8-byte alignment. `cuLaunchKernel` detected the mismatch and rejected with `CUDA_ERROR_LAUNCH_OUT_OF_RESOURCES` even at `blockDim=1`, even though `cuFuncGetAttribute` reported `MAX_THREADS_PER_BLOCK=1024 / NUM_REGS=24 / no spilling / no shared/local/const mem`.

Fix: `ViewType` now mirrors `PointerType` — `Size = Alignment = 8` on 64-bit targets, 4 on 32-bit:

```csharp
if (typeContext.TargetPlatform.Is64Bit())
    Size = Alignment = 8;
else
    Size = Alignment = 4;
```

PTX now emits `.param .align 8 .b8 _s_91[32]`, matches the host buffer, launch succeeds.

### Why single-ArrayView body structs and ArrayView1D Dense kept working pre-fix

Single-ArrayView body structs (`Tests23_OnlyShortBodyStruct`) and `ArrayView1D<T, Stride1D.Dense>` body fields (`Tests23_TwoShortBodyStructDense`) used different alignment-propagation paths in the IR — the inner structure type or the wrapping/single-field case picked up 8-byte alignment from the actual lowered layout, sidestepping the bug. That's why Tuvok's `MaxNumThreadsPerGroup = 1` workaround appeared to "fix register pressure" — it actually changed codegen path enough to avoid the alignment mismatch by coincidence.

### What this unblocks

- **Tuvok's `SilkDecodeCoreGpu` CUDA path** — bump `SpawnDev.Codecs` to `SpawnDev.ILGPU 4.9.5-rc.24` and lift the `MaxNumThreadsPerGroup = 1` workaround.
- **Any kernel with a body struct containing 2+ `ArrayView<T>` fields on CUDA** — including the `Tests23_RegisterHeavyBody*` shapes that previously failed at launch despite trivial register usage.

### Defense in depth (also in rc.24)

(1) **`CU_JIT_MAX_REGISTERS=255` cap** auto-forwarded to `cuModuleLoadDataEx` — forces ptxas to spill instead of producing kernels that exceed the per-thread hardware cap on any sm_50+ device. Configurable via `CudaAccelerator.DefaultMaxRegistersPerThread` (default 255, set 0 to disable). Closes a separate class of `CUDA_ERROR_LAUNCH_OUT_OF_RESOURCES` failures driven by ptxas overshoot.

(2) **`CU_JIT_INFO_LOG_BUFFER` + `CU_JIT_LOG_VERBOSE`** plumbed — ptxas register/spill/shared-mem info now surfaces via `Trace.WriteLine` when `CudaAccelerator.VerboseModuleLoad = true` (off by default).

(3) **`cuFuncGetAttribute`** via direct `DllImport` in `CudaKernel` — same `VerboseModuleLoad` flag dumps `MAX_THREADS_PER_BLOCK / NUM_REGS / SHARED_SIZE_BYTES / LOCAL_SIZE_BYTES / CONST_SIZE_BYTES / PTX_VERSION / BINARY_VERSION` per loaded kernel. Useful when debugging future CUDA launch errors.

### Lockdown tests

- `Tests23_DeepUnroll_NoLaunchFailure` (new) — 24 live integers through final reduction + chain accumulator, locks down deep-unroll register-pressure shape across all backends.
- `Tests23_TwoShortBodyStruct`, `Tests23_SilkBodyStructShape`, `Tests23_RegisterHeavyBody_UnitExtent_NoLaunchFailure`, `Tests23_RegisterHeavyBody_LargeExtent_Parallel` — all PASS post-fix on CUDA (FAIL pre-fix).

### Verification

18/18 broader sample sweep across CUDA + OpenCL + CPU + Tests1-23 — no regressions. Cuda non-Tests23 tests sampled: `LocalMemoryRepro_Int64_ShortByteViews`, `QR_GaloisField_Multiply`, `QR_Render_GPU_CPUMatch`, `QR_Decode_RoundTrip`, `KernelTest`, `KernelFloatTest`, `FloatInfinityLiteralTest`, `P2P_Dispatcher_Create` — all PASS.

## 4.9.5-rc.9 (2026-05-04) — Wasm cast-view byte-length fix

### Fix: Wasm OOB when an ArrayView's element type differs from its parent buffer's element type

`WasmAccelerator` was computing each kernel-param view's wasm-memory byte range as `iav.Length * wasmBuf.ElementSize` — using the **BUFFER's** element size rather than the **VIEW's**. For a `MemoryBuffer1D<int>` (4-byte element) viewed as `ArrayView<long>` via `.Cast<long>()` (8-byte element), this produced byte length `N*4` instead of `N*8`. Wasm reserved only half the bytes the kernel actually accessed, so reads/writes to the back half of any element landed past the allocated range and triggered an OOB at dispatch time.

Surfaced 2026-05-04 by Tuvok's `Vp9FrameEntropyKernel` Wasm OOB (`V4=4` bytes wide where the kernel signature has `ArrayView<long> outLen`). Same trap pattern: a 1-element `ArrayView<long>` showing as 4 bytes in the dispatcher's view layout diagnostic.

### Fix surface

`SpawnDev.ILGPU/Wasm/WasmAccelerator.cs` ~line 525-554. The SubView byte-offset path already extracted the view's actual element size via reflection on the generic argument; the byte-length path next to it used `wasmBuf.ElementSize` directly. Refactored to compute `viewElemSizeForLength` once (using the view's generic type), then apply it to BOTH the SubView byte-offset AND the byte-length range update.

### Lockdown test

New `Tests23_LongViewOverIntBuffer` in `BackendTestBase.Tests23.UintCompareInLoop.cs`:
1. Allocate `MemoryBuffer1D<int>(2)` (= 8 bytes total).
2. View as `ArrayView<long>` via `.Cast<long>()` (= 1 long element).
3. Kernel writes a known 64-bit value to the view.
4. Read parent int buffer back; reinterpret 2 ints as 1 long; assert equality.

Pre-fix on Wasm: only the LOW 32 bits of the value land correctly (back half is past the reserved range). Post-fix: full 64-bit round-trip on every backend.

### What this unblocks

- **Tuvok's Vp9 Codecs Wasm path** — `Vp9FrameEntropyKernel.Run` takes `ArrayView<long> outLen`. Pre-fix the dispatch OOB'd at disp=4 with V4=4 bytes wide. Bump SpawnDev.Codecs to rc.9 and lift the Wasm gate on `WasmCodecsTests.GpuTranscodeDemo_Vp9_GradientFrame_RoundTripsViaGpuPair`.
- **Any kernel signature using `ArrayView<long>` / `ArrayView<double>` / `ArrayView<RadixSortPair<...>>` over a Cast view** — same shape, same fix path.

### Verification

- `Tests23_LongViewOverIntBuffer` 7/7 PASS across all backends.
- Wasm canary (RadixSort + ManyDispatches) 3 PASS / 1 SKIP, no regressions in the SubView paths that share the now-unified element-size logic.

### Files changed

- `SpawnDev.ILGPU/Wasm/WasmAccelerator.cs` — view-element-size unified across byte-offset + byte-length paths
- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.Tests23.UintCompareInLoop.cs` — lockdown test

### Four-package bundle: Fork stays at 2.0.4

No edits to the forked `ILGPU/` tree.

## 4.9.5-rc.8 (2026-05-04) — Wasm multi-view body-struct kernel param decomp fix

### Fix: Wasm dispatcher loses N-1 view buffers when kernel takes a multi-ArrayView container struct

`WasmKernelFunctionGenerator.IsViewType` was returning `true` for any `StructureType` whose first `DirectField` is `AddressSpaceType`. That's correct for `ArrayView<T>` / `ArrayView1D<T,Stride>` (single view-pointer + metadata fields), but ALSO matched multi-view containers like Tuvok's `VorbisPacketDecodeStaticInputs` (38 ArrayView fields) and any user struct-of-views. The dispatcher then routed the param through the single-view path which only registers the FIRST view's buffer in `uniqueBuffers`; the remaining N-1 view-pointers in the serialized struct memory never got their wasm offsets written, so the kernel read fields 1..N-1 as offset 0 of V0's buffer plus the IR struct's per-field byte offset.

Symptom on `Tests21.BodyStruct_12ArrayViewInt_PerFieldDiagnostic`: `V0[0]=100000 (correct), V1..V11=100004 (= V0[4], because the IR field offset for field 1 was 16 bytes and the kernel divided by 4 to index into V0)`. Tuvok's Vorbis Wasm path was producing silently-wrong PCM for the same reason.

### Fix surface

```csharp
// Before:
if (structType.DirectFields.Length > 0
    && structType.DirectFields[0] is AddressSpaceType)
    return true; // matches single-view AND multi-view

// After:
int viewPtrCount = 0;
foreach (var df in structType.DirectFields)
    if (df is AddressSpaceType) viewPtrCount++;
if (viewPtrCount == 1
    && structType.DirectFields.Length > 0
    && structType.DirectFields[0] is AddressSpaceType)
    return true; // only single-view
```

Multi-view structs (`viewPtrCount > 1`) now flow through the scalar-struct serialization path which already correctly registers each view's buffer via `ExtractBuffersFromStruct` and writes each view-pointer to its own field offset.

### Verification

- `Tests21.BodyStruct_12ArrayViewInt_CoalesceTest` Wasm: 256-element output matches the per-field reference sum across all 12 ArrayView fields. Was wrong on every output index pre-fix.
- `Tests21.BodyStruct_12ArrayViewInt_PerFieldDiagnostic` Wasm: each output[f] = refData[f][0] correctly. Was 11/12 wrong pre-fix.
- `Tests21.BodyStruct_MixedIntFloatCoalesceTest` Wasm: PASS.
- `Tests21.BodyStruct_VariableLengthCoalesceTest` Wasm: PASS.
- All Tests21 cases pass on every backend except WebGL (still gated with technical reason — task #26).

### What this unblocks

- **SpawnDev.Codecs Vorbis Wasm path** — `VorbisPacketDecodeStaticInputs` (36 `ArrayView<int>` + 2 `ArrayView<double>`) now works on Wasm. Tuvok's dual-path branch in `VorbisAudioDecoderGpu.DecodePacketAsync` can drop the v1 fallback arm entirely.
- **Opus SILK + CELT Wasm decoder primitives** — same struct-of-ArrayView pattern, browser-clean from day one on Wasm.
- **Possibly some of Data's Wasm async-mode race cluster** — DDPM / MoveNet / 5 Style transfers / SqueezeNet / ESPCN / DepthAnything. Some of these may have been hitting the multi-view-decomp bug rather than (or in addition to) a true async race. Recommend re-running the ML pipeline + reference suite on Wasm after rc.8 lands.

### Partial WebGL improvement

`GLSLKernelFunctionGenerator.IsMultiDim` got the same multi-view detection fix — only treats a struct as a view when `NumFields` matches a known ArrayView shape (3, 4, or 6). This is necessary for the WebGL fix but not sufficient: the GLSL kernel codegen still needs per-field sampler decomposition + struct-aware GetField + dispatcher binding multiple samplers per param. Tracked as task #26. Tests21 cases remain gated on WebGL with sharp technical reason ("samplers cannot be struct members in GLSL ES 3.0").

### Files changed

- `SpawnDev.ILGPU/Wasm/Backend/WasmKernelFunctionGenerator.cs` — IsViewType multi-view detection
- `SpawnDev.ILGPU/WebGL/Backend/GLSLKernelFunctionGenerator.cs` — IsMultiDim multi-view detection (partial WebGL fix)
- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.Tests21.CoalesceBindings.cs` — added per-field diagnostic test
- `SpawnDev.ILGPU.Demo/UnitTests/WasmTests.cs` — un-gated Tests21 cases (Wasm fix verified)
- `SpawnDev.ILGPU.Demo/UnitTests/WebGLTests.cs` — kept Tests21 gated with sharp technical reason for the remaining WebGL infrastructure work

### Four-package bundle: Fork stays at 2.0.4

No edits to the forked `ILGPU/` tree. Only `SpawnDev.ILGPU/Wasm/*` + `SpawnDev.ILGPU/WebGL/*` changed.

## 4.9.5-rc.7 (2026-05-04) — WebGL GLSL codegen: INT_MIN const + unsigned compare/shift

### Three GLSL codegen bugs fixed

Surfaced by the same Tests23 bisection that closed Tuvok's libopus Normalize loop on rc.6, all three issues had been silently corrupting WebGL kernel output for any code path that used `uint` values with the high bit set.

#### Bug 1 — `int.MinValue` constant emit substituted bit pattern 0x80000000 → 0x80000001

`GLSLCodeGenerator.GenerateCode(PrimitiveValue)` had been substituting `int.MinValue` with the literal `-2147483647` as a workaround for an ANGLE/ESSL3 parser issue (`-2147483648` parses as `-(2147483648)` and 2147483648 is not a valid signed-int literal). The substitution preserved sign but corrupted the LOW BIT — every constant that needed the exact 0x80000000 bit pattern silently shipped as 0x80000001. Affected: libopus range-coder constants (`EC_CODE_TOP = 1u << 31`), IEEE -0.0 bitcasts, uint shift overflow results that get constant-folded by ILGPU's IR construction.

Fix: emit as `int(2147483648u)` — uint-to-int bitcast preserves the exact bit pattern with no parser issue and no UB.

#### Bug 2 — `uint <= uintConst` evaluated as signed compare

`GenerateCode(CompareValue)` for unsigned integer comparisons (with `IsUnsignedOrUnordered` flag set) emitted bare `int op int` GLSL. ILGPU's IR uses `BasicValueType.Int32` for both signed and unsigned integers, with the unsigned-ness carried as a flag on the compare operation. The GLSL TypeGenerator maps Int32 → "int", so the operands were declared as `int` in the shader and the compare used signed semantics — values with the high bit set compared as negative. `0x80000000 <= 0x800000` returned TRUE on WebGL because `int(0x80000000) = -2147483648 < 8388608`.

Fix: when `IsUnsignedOrUnordered` is set on a `<= < > >=` comparison and the operand types are integer, emit `uint(left) op uint(right)` so GLSL uses unsigned semantics.

#### Bug 3 — Signed left-shift overflow into the sign bit produces undefined behavior on ANGLE

`GenerateCode(BinaryArithmetic)` for Shl emitted `int << int` directly. GLSL ES 3.0 spec marks left-shift of a signed integer where the result sets the sign bit as **undefined behavior**. ANGLE on Chrome produces inconsistent values (observed 0x80000001 instead of 0x80000000). Same UB applies to Shr when shifting a sign-bit-set value, but ILGPU IR does carry an `IsUnsigned` flag for `shr.un` IL, so signed-Shr's sign extension is preserved.

Fix: emit as `int(uint(left) << uint(right))` for Shl (always — no IR signal for shift signedness because IL `shl` has no `.un` variant). For Shr, only switch to unsigned when `IsUnsigned` is set on the IR node.

#### Bonus — `glWorker.js` TF readback path

Changed the WebGL Transform Feedback readback typed array from `Float32Array` to `Uint8Array`. The byte path is identical — `getBufferSubData` does a raw byte copy regardless of the destination's element type — but explicit `Uint8Array` is clearer documentation of intent and avoids any future driver-side type-conversion paths.

### Verification

- `BackendTestBase.Tests22.StaticStructReturnRefHelpers` — Tuvok's libopus regression: **14/14 PASS** post-fix (already PASSing on rc.6; confirmed unchanged).
- `BackendTestBase.Tests23.UintCompareInLoop` — 7 bisection cases × 7 backends = **49/49 PASS** including all WebGL cases (was 6 WebGL failing on rc.6).
- WebGL full test sweep (`FullyQualifiedName~WebGLTests`): **457 passed / 1 failed / 123 skipped**. The 1 failure is `LocalMemoryRepro_Int64_ShortByteViews` (30s timeout) — a preexisting WebGL architectural-varying-count limit, NOT caused by this fix.

### What this likely also fixes

Data's `data-to-captain-ml-sweep-summary-2026-05-04.md` listed 12 WebGL correctness failures across DistilBERT / GPT2 / WhisperEncoder / CLIPVision / DepthAnything / 5 Style transfers / YOLOv8 / TextGeneration. Many of those models use `uint` indexing or bitwise operations that go through the buggy compare/shift paths. Recommend re-running the ML pipeline + reference suite on WebGL after rc.7 lands; expect a meaningful chunk of the 12 failures to clear automatically.

### Files changed

- `SpawnDev.ILGPU/WebGL/Backend/GLSLCodeGenerator.cs` — PrimitiveValue Int32 INT_MIN bitcast + CompareValue unsigned-cast (base class path)
- `SpawnDev.ILGPU/WebGL/Backend/GLSLKernelFunctionGenerator.cs` — CompareValue unsigned-cast (kernel override) + BinaryArithmetic Shl/Shr unsigned-cast
- `SpawnDev.ILGPU/wwwroot/glWorker.js` — TF readback typed array (cosmetic)
- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.Tests23.UintCompareInLoop.cs` — added const-write diagnostic to Tests23_BareUintShift
- `SpawnDev.ILGPU.Demo/UnitTests/WebGLTests.cs` — removed all Tests23 gates (every case now passes on WebGL)

### Four-package bundle: Fork unchanged at 2.0.4

Changes are all in the `SpawnDev.ILGPU` wrapper (WebGL/* + wwwroot/glWorker.js); no edits to the forked `ILGPU/` tree. Fork stays at 2.0.4. Only the SpawnDev.ILGPU PackageReference bumps rc.6 → rc.7.

## 4.9.5-rc.6 (2026-05-04) — LoopUnrolling shift-induction trip-count fix

### Fix: `while (uintRng <= uintConst) rng <<= N` produced wrong output for N != 1 on every backend except CPU

Surfaced 2026-05-04 by Tuvok's libopus-style Normalize while-loop pattern (`while (rng <= 0x800000) rng <<= 8`) which silently produced `Rng=0` instead of `0x80000000` on every GPU + Wasm + WebGL backend. CPU bypassed because it doesn't run the IR-level unroller pass.

### Root cause

`ILGPU/IR/Analyses/LoopInfo.cs:944` (`TryGetTripCount`) computed the per-iteration multiplier for shift updates as:

```csharp
if (IsMultiplied2Update(UpdateKind)) update *= 2;
```

That formula only produces the correct multiplier for `<<= 1` (where 2*1 = 2 = 2^1). For `<<= N` with N != 1 the per-iteration multiplier should be `2^N`, not `2*N`. With shift_count=8 (libopus EC_SYM_BITS) the unroller computed update=16 instead of 256, producing trip count 5 instead of 3, so it emitted two extra `rng <<= 8` operations past the loop's intended exit. After the extra iterations rng=0 (high byte shifted off then again).

The bug had been latent since the loop unroller's introduction; it only surfaces when the kernel has a SINGLE induction variable that's a shift. Compound conditions like `while (cond && iter < N)` introduce a second induction variable and force the unroller to bail (`InductionVariables.Length != 1`) — which is why no existing algorithm tests caught it. Tuvok's `OpusRangeDecoderGpu.Init` was the first kernel in the codebase to use a bare-condition shift loop.

### Fix

```csharp
if (IsMultiplied2Update(UpdateKind))
{
    if (update < 1 || update >= 32)
        return null; // out-of-range shift — bail rather than emit garbage
    update = 1 << update;
}
```

Identical behavior for `<<= 1`. Correct for any other shift amount in 1..31.

### Verification

- `BackendTestBase.Tests22.StaticStructReturnRefHelpers` — Tuvok's regression test for the libopus Init/Normalize pattern. Was 12/14 failing pre-fix (CUDA / OpenCL / WebGPU / WebGPUNoSubgroups / Wasm / WebGL × bug+inline). **PASS 14/14 post-fix.**
- `BackendTestBase.Tests23.UintCompareInLoop` — 6 new bisection cases that pin down the unroll-path codegen. PASS on every backend except WebGL, which has a SEPARATE pre-existing GLSL signed-shift/compare bug (gated via `UnsupportedTestException`, tracked independently — not caused by this fix).
- Algorithm regression sweep (Reduce + Initialize + RadixSort + Sequence + Scan + Histogram + Algorithm*): **481 passed / 0 failed / 93 skipped** in 53m48s. Zero regressions.

### What this unblocks

- `SpawnDev.Codecs.OpusRangeDecoderGpu` — was CPU-only verified before; now works bit-exact on every backend.
- All upcoming Opus SILK / CELT / Vorbis decoder primitives that use libopus-style range-decoder normalize loops.

### Four-package bundle bumped to 2.0.4

Fix is in `ILGPU/IR/Analyses/LoopInfo.cs` (forked tree). Per the four-package bundle protocol, `ILGPU.csproj`, `ILGPU.Algorithms.csproj`, and the two `SpawnDev.ILGPU.Fork*` PackageReference lines in `SpawnDev.ILGPU.csproj` all bumped from 2.0.3 → 2.0.4. `_check-fork-version-sync.bat` passes.

## 4.9.5-rc.5 (2026-05-03) — WebGPU binding-count coalesce

### Fix: kernels with > 10 storage-buffer bindings on WebGPU

WebGPU spec `maxStorageBuffersPerShaderStage` = 10 (Chrome default). Every body-struct ArrayView field gets its own storage-buffer binding under the previous codegen, so a kernel taking a struct with many `ArrayView` fields would push the total over the limit and throw at dispatch time:

```
[WebGPU] Kernel 'Kernel_Run' requires 44 storage buffer bindings but this device only supports 10
```

Triggered by `SpawnDev.Codecs.Audio.Vorbis.VorbisPacketDecodeStaticInputs` (36 `ArrayView<int>` + 2 `ArrayView<double>`), which would also recur for the upcoming Opus SILK + CELT integration kernels and Vorbis v3 streaming decoder. Per `_DevComms/SpawnDev.ILGPU/tuvok-to-geordi-vorbis-v2-binding-count-2026-05-03.md`.

### Fix surface

`WGSLKernelFunctionGenerator.DecideCoalesceGroups` runs after `ScanBodyStructParams`. When the kernel's predicted raw binding count exceeds 10, it groups eligible body-struct ArrayView fields by element type and coalesces each multi-member group into a single shared `@binding(N) var<storage, read_write> ... : array<T>` declaration. Per-field accesses route through the existing `_scalar_params[ViewOffsetSlot]` channel (the same machinery sub-views already use for non-zero-offset element offsets); each member's offset within the coalesced buffer is stamped at dispatch time via a new `IsCoalesceFieldOffset` `ScalarPackingEntry` flag.

`WebGPUAccelerator` dispatch path: a coalesce pre-pass allocates one fresh GPU buffer per group, runs `CopyBufferToBuffer` for each member to concat their data at running offsets, binds the coalesced buffer once at the leader's binding slot, and skips non-leader members in Phase 1. The coalesced buffer is destroyed after the batch flushes (no scratch pool — sizes vary widely with kernel parameter shape).

### Eligibility (v1)

A body-struct ArrayView field qualifies for coalescing when ALL of:
- Element type is `i32`, `u32`, `f32`, `emu_i64`, `emu_u64`, or `emu_f64`
- Field is NOT atomic (atomic bindings need `atomic<T>` typing — separate path)
- Field is NOT sub-word (`i8` / `i16` / `Half` packed `atomic<u32>` — separate path)
- Field is NOT a packed-struct view (CPU-layout u32 packing — different stride per group)
- Body struct is flat (no nested struct fields with pointer recursion — defensive runtime check throws on unexpected shapes)

Trigger: kernel raw bindings > 10. Existing kernels with body structs of 1-9 view fields keep their current shape (no per-dispatch GPU→GPU copy overhead).

### What this unblocks

- **SpawnDev.Codecs Vorbis v2 browser path** — currently dual-path with `useV2Path = _accelerator.AcceleratorType is CPU or Cuda or OpenCL` in `VorbisAudioDecoderGpu.DecodePacketAsync`; flips to include WebGPU once Codecs bumps to rc.5.
- **Opus SILK + CELT integration kernels** — designed with the same struct-of-ArrayView pattern, browser-clean from day one.
- **Future high-parameter codec primitives** — Vorbis v3 streaming decoder, codec ML primitives, etc.

### Test coverage

New `BackendTestBase.Tests21.CoalesceBindings.cs`:
- `BodyStruct_12ArrayViewInt_CoalesceTest` — 12 independent `ArrayView<int>` fields, kernel sums all at idx, verify CPU reference match.
- `BodyStruct_MixedIntFloatCoalesceTest` — 11 `ArrayView<int>` + 1 `ArrayView<float>`, two coalesce groups (separate bindings per element type).
- `BodyStruct_VariableLengthCoalesceTest` — 12 fields with widely-varying lengths (4-768 elements), exercises per-field offset routing.

Result: **15/0/6 across CPU + WebGPU + WebGPUNoSubgroups + CUDA + OpenCL.** Wasm + WebGL are skipped via `UnsupportedTestException` for a pre-existing many-field body-struct decomposition limitation in those backends — NOT a regression from this work; tracked separately for a follow-up fix.

Regression sweep on Reduce + Initialize + RadixSort + Sequence (existing body-struct algorithm kernels, all with ≤ 9 view fields and well under the coalesce trigger): **332 passed, 0 failed, 63 documented skips** across all backends. Coalesce trigger does not fire spuriously on small body structs.

### Public API additions

- `WebGPUCompiledKernel.CoalesceManifest` — `IReadOnlyList<CoalesceGroupEntry>` describing the coalesce groups for this kernel; `HasCoalesceGroups` convenience flag.
- `CoalesceGroupEntry` (public class in `SpawnDev.ILGPU.WebGPU.Backend`) — `BodyStructParamIndex`, `ElementTypeKey`, `BindingName`, `BindingIndex`, `BindingWgslType`, `ElementWordsPerSlot`, `MemberFieldIndices`.
- `ScalarPackingEntry.IsCoalesceFieldOffset` + `CoalesceBodyStructParamIndex` + `CoalesceFieldIndex` — manifest entry kind for per-field coalesce-relative offsets.

## 4.9.4 (2026-05-03) — stable rollup of rc.1 + rc.2

Stable cut. Configurable Wasm linear-memory ceiling, end-to-end (host + module declared max agree). End-to-end verified by SpawnDev.ILGPU.ML's DA3-Small at `MaxLinearMemoryPages=32768`: op 93 `memory.grow` past 16384 pages succeeds, model runs 2m 28s past the rc.1 instant-instantiate-reject point (Data, 2026-05-03). Default consumers (16384) see byte-identical output vs 4.9.3.

`SpawnDev.ILGPU.P2P 4.9.4` ships in lockstep: closes `P2PSwarm.TwoTab_PeerDiscovery` regression via the new `Wire.SimplePeer.IsTransportDead` accessor in `SpawnDev.WebTorrent 3.2.3` stable. Both bridge filter sites updated. `LargeBuffer_100MB_DispatchedOverRealWebRtc_BitExact` PASS 3m 37s standalone (no regression).

See the rc.1 + rc.2 sections below for the full surface description.

## 4.9.4-rc.2 (2026-05-03) (superseded by 4.9.4 stable)

### Fixes the rc.1 kernel-module memory import maximum mismatch

rc.1 made the host-side `WebAssembly.Memory` `maximum` configurable via `WasmBackendOptions.MaxLinearMemoryPages`, but the compiled kernel module's WASM binary still hardcoded `maximum=16384` in its memory-import declaration. Per WebAssembly spec, the imported memory's max must be `<=` the import's declared max — when the host cap was raised above 16384, `WebAssembly.instantiate` rejected every kernel dispatch:

```
WebAssembly.instantiate(): Import #0 "env" "memory":
memory import has a larger maximum size 32768 than the module's declared maximum 16384
```

Discovered by Data on DA3-Small with `MaxLinearMemoryPages=32768`; first dispatch failed instantly, all Wasm tests in the consuming project failed.

`WasmBackend.CreateKernel` now reads `Options.MaxLinearMemoryPages` and threads it through `WasmModuleBuilder.ImportSharedMemory("env", "memory", 1, (uint)Options.MaxLinearMemoryPages)`. Both ends agree at any cap up to 65536 (4 GiB).

Default behavior unchanged — consumers at the 16384 default see byte-identical module output vs rc.1.

### SpawnDev.ILGPU.P2P 4.9.4-rc.2 (lockstep bundle)

P2P source unchanged from rc.1; bumped to keep the bundle versioned in sync. Same `P2PWebRtcBridge.wire.OnClose` phantom-alive filter, same SpawnDev.WebTorrent 3.2.3-rc.2 dep.

## 4.9.4-rc.1 (2026-05-03) (superseded by 4.9.4-rc.2)

### Configurable Wasm linear-memory ceiling

New `WasmBackendOptions.MaxLinearMemoryPages` knob (default 16384 / 1 GiB, configurable up to 65536 / 4 GiB). Threaded through `WasmAccelerator.Create` and the cached-memory `WebAssembly.Memory` `eval` strings. Default behavior unchanged. Required by SpawnDev.ILGPU.ML's DA3-Small graph executor where total live allocations exceed 1 GiB at op 93.

**KNOWN ISSUE (fixed in rc.2):** The kernel module's memory-import maximum was still hardcoded at 16384 in this version, so consumers raising the host cap hit `WebAssembly.instantiate` failures. Use rc.2 instead.

### SpawnDev.ILGPU.P2P 4.9.4-rc.1: TwoTab phantom-alive close

`P2PWebRtcBridge.wire.OnClose` now filters phantom-alive wires (where `Destroyed=false` but the underlying transport is dead) using the new `Wire.SimplePeer.IsTransportDead` accessor in SpawnDev.WebTorrent 3.2.3-rc.2. Catches the Chromium-under-Playwright bug where `connectionstatechange` doesn't propagate to `"failed"` on remote tab close, leaving the wire's `Destroyed` flag false and inflating the canonical wireSet count. Both bridge filter sites updated: the wireSet `RemoveWhere` in `wire.OnClose` and the `torrent.Wires` cross-check walk.

Verified: `P2PSwarm.TwoTab_PeerDiscovery` PASS in 1m 37s standalone (was failing 90s timeout in 4.9.2-rc.34); `LargeBuffer_100MB_DispatchedOverRealWebRtc_BitExact` PASS in 3m 37s standalone (no regression vs rc.34).

SpawnDev.WebTorrent dep bumped 3.2.2 -> 3.2.3-rc.2.

## 4.9.3 (2026-04-29)

### `ArrayView<T>.CopyToHostAsync()` partial-readback extension

New extension on `ArrayView<T>` (and `ArrayView1D<T, TStride>`) that does a real per-backend partial readback for the view's byte range. The data outside the view never crosses the device-host boundary.

```csharp
// AV1 YUV plane separation - one device buffer, three planes:
var y = await dRecon.View.SubView(0,            yLen ).CopyToHostAsync();
var u = await dRecon.View.SubView(yLen,         uvLen).CopyToHostAsync();
var v = await dRecon.View.SubView(yLen + uvLen, uvLen).CopyToHostAsync();
```

Per-backend dispatch (no full-buffer readback + CPU slice anywhere):

- **WebGPU** - `queue.CopyBufferToBuffer(srcBuf, srcOffset, staging, 0, byteCount)` -> `mapAsync` of just `[byteOffset, byteOffset+byteCount)`.
- **WebGL** - GL-worker `ReadbackAndGetUint8ArrayAsync(buf, sourceByteOffset, byteCount)` partial range path.
- **Wasm** - `Uint8Array(SharedBuffer, byteOffset, byteCount)` window onto exactly the slice's bytes; the rest of wasm linear memory is not touched.
- **CUDA / OpenCL / CPU** - ILGPU's native `view.CopyToCPU(target)` calls `cudaMemcpy` / `clEnqueueReadBuffer` / direct memcpy for just the view's range; the view's offset and length encode the partial copy.

Closes the `Buffer.BlockCopy` cardinal-rule violation in SpawnDev.Codecs decoder integration: consumers can now request per-channel / per-plane slices without the host iterating over codec data.

### WebGPU `Half` NaN/Inf bit-pattern codegen fix

WGSL multi-compare paths (`isNativeFloatUnordered`, `isNativeFloatEqualLike`) emit IEEE 754 bit-pattern checks for `IsNaN` / `IsInf` / `IsFinite`. The 4.9.2 codegen used the f32 mask constants (`0x7F800000` / `0x007FFFFF`) and `bitcast<u32>(operand)` directly on every operand type, including `f16`. WGSL rejects `bitcast<u32>(f16)` as an invalid bitcast, so any kernel with a multi-compare on `Half` operands failed shader validation. `Half` round-trip / arithmetic / min-max tests passed (single-compare path, no IR inversion), but `HalfNaNComparisonTest` failed.

`WGSLCodeGenerator` now routes f16 through `bitcast<u32>(vec2<f16>(x, 0.0h))` with the f16 mask constants (`0x7C00` exponent, `0x03FF` mantissa). f32 / f64 paths unchanged.

### `P2PDispatcher` test expectation alignment

`P2P_Dispatcher_Create` was asserting the historical `DispatchTimeoutMs == 30_000` default. The default was intentionally raised to `60_000` (per the doc comment on `P2PDispatcher.DispatchTimeoutMs`: 30s was too tight for >1MB result buffers and 10-way concurrent dispatch). Test updated to match the implementation.

## 4.9.2 (2026-04-29)

### OpenCL backend phi-binding-per-target fix

The OpenCL backend was emitting all phi bindings unconditionally before a conditional branch's terminator, even when the branch was about to take an exit edge that didn't need the back-edge update. When a non-phi SSA value `u` aliased to a loop's phi `v` (the C# pattern `u = v` inside `do { u = v; ...; v = compute(); } while (cond);`), the unconditional back-edge phi update stomped `u` on the path that exited the loop, producing wrong values for any `u - v` style read after the loop.

`CLCodeGenerator.Terminators.cs` now mirrors `PTXCodeGenerator`'s `BindPhis(target)` approach - phi bindings emit only on the edge actually being taken. `IfBranch` calls `BindPhis(trueTarget)` inside the if-block and `BindPhis(falseTarget)` after, with `ResetPhiBindingScope()` between blocks. `UnconditionalBranch` and `SwitchBranch` similarly per-target. CPU + CUDA were unaffected because their backend codegens don't share this aliasing pathology.

Diagnosed against SpawnDev.Codecs `Av1RangeCoderGpu_CdfQ15_RoundTrip_AllBackends` (was 1/3 FAIL on OpenCL: `sym[1]: input=1 decoded=0`) and `Av1CoefDecoderGpu_RoundTrip_*` (was 4/15 FAIL on OpenCL with `[decEob] Expected '1' but got '2'`). Same backend fix closes both - **18/18 PASS** post-fix across CPU + CUDA + OpenCL.

### Rolled up from 4.9.2-rc.X series

The 4.9.2 stable cut consolidates the rc.7 -> rc.30 series:

- **rc.30 (this release):** OpenCL phi-binding-per-target (above).
- **rc.29:** Respin to actually deliver Tuvok's signed `Div` fix - rc.28 bumped this csproj's version but kept the transitive dependency at `SpawnDev.ILGPU.Fork 2.0.1`, so consumers' resolved `ILGPU.dll` was still the unfixed Apr-23 build. rc.29 (now stable) bundles `SpawnDev.ILGPU.Fork 2.0.3` + `SpawnDev.ILGPU.Algorithms.Fork 2.0.3` with the corrected XML rewriter table. Also added: T4 drift CI guard + four-package version-sync CI guard.
- **rc.28:** IR signed `Div by pow2` no longer rewrites to `Shr` (rewrite was floor-toward-negative-infinity vs CLR / IL `div` truncate-toward-zero, off-by-one for every odd-negative dividend; gated on `(flags & ArithmeticFlags.Unsigned)`). Plus: Wasm `[NoInlining]` void helpers no longer silently dropped + `WasmAccelerator.WorkerCount` public read-only diagnostic.
- **rc.27:** Wasm wait/notify-free + worker headroom default. Removed last `memory.atomic.wait32`/`notify` call from in-kernel `EmitBarrier`; default `WasmBackendOptions.WorkerCount` changed from `hardwareConcurrency` to `Math.Max(2, hardwareConcurrency - 2)` (leaves 2 cores for browser UI / Mono / OS). Wasm RadixSort 18/18 PASS including 4M tests.
- **rc.26:** IEEE 754 NaN/Inf correctness across WGSL / GLSL / Wasm / OpenCL. Closes `clt+brfalse` -> `cge+brtrue [Unordered]` IR-inversion bug (4 backends ignored the unordered flag, silently corrupting NaN multi-compare flag-bit kernels).
- **rc.18:** Helper Method Fn-Definition Emission - Compile Cliff Fix. Tag a helper with `[MethodImpl(MethodImplOptions.NoInlining)]` and SpawnDev.ILGPU emits a real WGSL/GLSL `fn` definition + N call sites instead of N inline expansions. Avoids browser shader validator size limits (Tint rejecting `Invalid BindGroupLayout`).
- **rc.10:** `AcceleratorRequirements` capability-gating API + `UnsupportedKernelFeatureException` typed codegen errors + `LocalMemory<T>(N >= 32)` WGSL codegen 5-layer fix.
- **rc.7-9:** Float16 (Half) everywhere - native or emulated; i64 `Atomic.Add` on WebGPU lock-free CAS loop; WGSL break-in-loop codegen fix; mixed atomic/non-atomic buffer access fix; WGSL shader validation errors surfaced; implicit `SynchronizeAsync` before readback (parity with desktop backends); stream ordering verified correct; WebGL unsupported atomic guards; GLSL `IsReturnExit` defense-in-depth.

## 4.9.0

### Complete Sub-Word Data Type Support

Full `Int8`, `UInt8`, `Int16`, `UInt16`, and `Float16` (`ILGPU.Half`) buffer support across all 6 GPU backends. Sub-word types are stored packed and extracted with correct stride on every backend - no more data corruption from type promotion mismatches.

- **WebGPU** - Packed into `array<atomic<u32>>` storage buffers. Load via `atomicLoad` + shift + mask + sign-extend/zero-extend. Store via thread-safe `atomicAnd` + `atomicOr` for packed writes (prevents data races when threads write different halves of the same word). Float16 uses inline IEEE 754 f16-to-f32 conversion in WGSL.
- **Wasm** - Native `i32.load8_s`/`i32.load8_u`/`i32.load16_s`/`i32.load16_u`/`i32.store8`/`i32.store16` opcodes. Float16 via `EmitF16ToF32`/`EmitF32ToF16` for direct ArrayView load/store.
- **WebGL** - `texelFetch` from R32I texture with shift+mask extraction in GLSL. Float16 via `_f16_to_f32`/`_f32_to_f16` using `uintBitsToFloat`/`floatBitsToUint`.
- **OpenCL** - Float16 promoted to `float` compute type. `vload_half`/`vstore_half` for buffer access (handles 2-byte stride internally).
- **CUDA/CPU** - Native support, no changes needed.

### ILGPU.Half Intrinsics

- `Half.Abs`, `Half.Min`, `Half.Max`, `Half.Clamp` - GPU-accelerated half-precision math
- Implicit `System.Half` <-> `ILGPU.Half` conversion operators for seamless interop
- Use `ILGPU.Half` (not `System.Half`) in kernel signatures for correct transpilation

### CopyFromJS - Zero-Copy JS-to-GPU Transfer

New `IBrowserMemoryBuffer.CopyFromJS()` methods accept `TypedArray` or `ArrayBuffer` and write directly to GPU memory without .NET heap allocation. Available on all 3 browser backends (WebGPU, WebGL, Wasm).

```csharp
// Write JS data directly to GPU buffer - no .NET allocation
var jsArray = new Int16Array(data);
((IBrowserMemoryBuffer)buffer).CopyFromJS(jsArray);
```

## 4.8.0

### Worker Function Caching (3-4x Speedup)

Wasm backend now caches compiled `AsyncFunction` objects in the worker bootstrap. Previously, V8 recompiled each unique script string on every dispatch. Caching eliminates recompilation overhead - **3-4x faster** kernel dispatch on repeated calls.

### Full Worker Parallelism

Non-barrier Wasm workers uncapped from 2 to full `navigator.hardwareConcurrency`. Barrier-limited workers remain capped for synchronization correctness, but non-barrier kernels now use all available cores.

### Memory Leak Fixes

- `AllWasmBinaries` and `AllKernelInfos` collections gated behind debug dump flag - no longer accumulate in production
- `_dispatchLog` gated with `VerboseLogging` - eliminated unbounded log growth
- `ExtraImportCount` for correct Wasm function index calculation

### Barrier Count Auto-Correction

Automatic detection and correction of barrier count mismatches between the kernel's declared barrier count and the actual barriers found during compilation.

## 4.7.1

### GPU Test Verification (`GpuTestVerify`)

Shared utility for verifying test results on the GPU without CPU readback. Data stays on the accelerator - CPU reads back only a few bytes of violation counts.

- `VerifyDescendingSort` / `VerifyAscendingSort` - Sort order + index integrity + key-value tracking
- `CompareBuffers` - Float comparison returning `(meanAbsError, maxAbsError)`
- **10x+ faster** verification - 4M element RadixSort went from 120s timeout to 11s on CPU

### QR Code Library (`SpawnDev.ILGPU.QR`)

GPU-accelerated QR code encoder + decoder. Zero external dependencies.

- **Encoder** - All 40 QR versions, 4 EC levels, byte mode, 8 mask patterns with penalty scoring
- **Renderer** - GPU kernel for pixel rendering + CPU fallback + logo overlay (EC level H)
- **Decoder** - Grayscale -> binarize -> finder detection -> grid sampling -> unmask -> Reed-Solomon -> data decode
- **Round-trip verified** - Encode -> render -> decode = exact match, including with logo overlay

### CPU Default Optimization

CPU backend default changed from warp=4/warps=4 (group size 16) to **warp=8/warps=8 (group size 64)**, matching the Wasm backend's proven configuration. 4M element RadixSort: **TIMEOUT -> 11 seconds**. CPU is now faster than Wasm for the same workloads.

### DI Integration

- `AddPlatformCrypto()` - registers platform-appropriate `IPortableCrypto` (WebCrypto in browser, System.Security.Cryptography on desktop)
- `WebTorrentClient` registered as DI singleton with tracker discovery
- All test classes receive `IPortableCrypto` via constructor injection

## 4.6.0

### Wasm Fiber-Based Barrier Dispatch

Complete rewrite of the Wasm backend's barrier synchronization model. Kernels with barriers now use a **fiber-based phase dispatch** - each barrier becomes a yield point where the kernel saves state and re-enters at the next phase. A **Wasm-native phase dispatcher** handles the entire thread/phase loop inside WebAssembly, eliminating JS-Wasm boundary crossings between phases. Barriers use **pure spin synchronization** via `i32.atomic.load` loops for correct multi-worker execution at full `hardwareConcurrency`.

- **Full ILGPU Algorithms on Wasm** - All RadixSort variants (int, uint, float, pairs, descending, 100K-4M+ elements), Scan, Reduce, Histogram. Previously limited to <=64 elements.
- **Pure spin barriers** - Replaced `memory.atomic.wait32`/`memory.atomic.notify` with atomic load spin loops after discovering a [V8 Atomics.wait visibility bug](https://issues.chromium.org/issues/495679735) where `wait32` returning "not-equal" does not provide happens-before guarantees for third-party stores with 3+ workers. [Live interactive demo](https://lostbeard.github.io/v8-atomics-wait-bug/).
- **20+ bugs fixed** - fiber yield-per-phase, br depth miscalculation, scratch overflow, shared memory stomping, stale dispatch state, completion state persistence, shared memory alloca overlap (same-size dedup), IR address space aliasing (LowerStructures -> LowerArrays -> InferAddressSpaces chain), struct/scratch overlap, per-worker scratch isolation, atomic RMW opcode table, unsigned comparison, Float16, ViewSourceSequencer, subViewByteOffset, CopyFromBuffer, and more.
- **ShaderDebugService** - auto-dumps all generated WGSL, GLSL, and Wasm binaries to a local folder on every kernel compilation. Backend-organized subfolders. IDB persistence. Full metadata headers.
- **Test results writer** - `UnitTestsView` writes `latest.json` (live progress) and timestamped `test-run-*.json` (history) to the debug folder

## 4.4.0

### Capturing Lambda Kernels

Write GPU kernels as C# lambdas that capture local variables. Captured scalar values are automatically passed to the GPU at dispatch time - no boilerplate, no separate static methods.

```csharp
int multiplier = 5;
float offset = 0.5f;
var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(
    (index, buf) => { buf[index] = index * multiplier + offset; });
kernel((Index1D)length, buffer.View);
```

### DelegateSpecialization - Higher-Order GPU Kernels

Write one kernel that accepts different operations as parameters. The delegate is resolved at dispatch time and its body is inlined directly into the kernel via compile-time specialization - no function pointers, no overhead.

```csharp
static void MapKernel(Index1D index, ArrayView<int> buf,
    DelegateSpecialization<Func<int, int>> transform)
{
    buf[index] = transform.Value(buf[index]);
}

static int Negate(int x) => -x;
static int DoubleIt(int x) => x * 2;

var kernel = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<int>, DelegateSpecialization<Func<int, int>>>(MapKernel);

kernel(size, buffer, new DelegateSpecialization<Func<int, int>>(Negate));
kernel(size, buffer, new DelegateSpecialization<Func<int, int>>(DoubleIt));
```

## 4.0.0

- **WebGPU backend refactor** - `SharedMemoryResolver`, `UniformityAnalyzer`, per-function emulation trimming, dead variable elimination, i64 constant hoisting, WGSL pre-validation
- **WebGPU RadixSort** - All variants passing (4M+ elements, pairs, descending)
- **Device loss detection** - WebGPU `device.lost` promise, WebGL `webglcontextlost` event
- **Unified test infrastructure** - `PlaywrightMultiTest` runs all tests (desktop + browser) in a single `dotnet test` invocation
