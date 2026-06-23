# Wasm Backend

Compiles ILGPU IR → WebAssembly binary. Dispatches via Web Workers with SharedArrayBuffer.

> **✅ RESIDUAL LARGE-SORT RACE KILLED (2026-06-10/11, Seven) — VERIFIED ATOMIC STORES.**
> Root cause (ring-instrumented at instruction level, 21/21 events): under CPU
> oversubscription **V8 atomic stores in barrier kernels can silently fail to land** —
> the boundaries out-param copy was the proven victim (left field landed, right field
> vanished = the "window of consecutive stores vanishes"), leaving a fiber's tile carry
> one publication behind. Fix: **`EmitVerifiedAtomicStore`** (every atomic store →
> store → **RMW(+0) read-back** → retry until it sticks; the read-back must be an RMW —
> a plain-load read-back can be store-forwarded while the store never lands) + the
> dispatcher sense barrier hardened (savedGen via RMW, RMW-confirmed spin exits — a
> lagged gen load caused EARLY PHASE CROSSING) + Broadcast monotonic per-execution tags.
> **Gate: 0/120 × 3 consecutive at 48-worker 4× oversubscription (baseline: 7-15/120,
> and 1/30 even at 12 workers).** Perf: ~1-4% at ≤cores configs; +25% only under 4×
> oversubscription stress where the unfixed code corrupted. Commits `b0dfc5c` + `b6c558a`.
>
> **⚖ UNIFORM STORE REGIMES LAW (2026-06-11, bisect-proven BOTH directions - `81b585b`).**
> NEVER mix verified and plain stores on ordered/overlapping data: a verified store forces
> its landing NOW and OVERTAKES still-delayed plain stores → write-order inversion. A
> verified state-slot write + the plain spill of the same slot = the fiber resumed at the
> OLD continuation (4/120 @ 48w). Full-verified spills = correct but 2.7x at production
> configs (rejected). The shipped shape: verified DATA stores (uniform) + plain
> spills/state/yield-buffer (uniform, FIFO-benign). Convert ALL writers of an ordered set
> or NONE. See memory `feedback-uniform-store-regimes-never-mix-verified-plain`.
>
> **Also fixed 2026-06-11 (`d835547`):** the single-call helper path (phase-mode kernel →
> no-barrier helper, e.g. `FusedActivate`) had 3 chained bugs - dropped non-void returns
> (`if (_phaseMode) Drop`), the kernel's LIVE phase passed as the helper's phase (garbage
> restore), and the KERNEL's scratch base passed as the helper's scratch (completion-persist
> clobbered fiber spill slots). `WasmTests.FusedFFN_RegBlocked*` (the GEMM-core ticket) green.
>
> **✅ THE LAST TAIL IS DEAD (2026-06-11, `f0a6df1`) — LIVENESS SPILLS + CHECKSUM-GATED RESTORE.**
> The ~1/1000-dispatch bundled-Chromium corruption (a fiber restore reading the previous
> phase's spills) is fixed: (1) LIVENESS - locals touched in one state block never spill
> (kernel body 16.1KB→7.6KB); (2) CHECKSUM GATE - all stores stay PLAIN (every verified-
> yield-path variant leaked 1-4/120; all-plain gates 0/360), but the save block XORs the
> spill set tagged with the PHASE (a register param, immune to memory staleness) and the
> restore RE-READS until the checksum proves every spill landed. Validation: Node 48w
> 0/120 @ baseline speed; Chromium canary 3×3000 iterations ZERO failures (was 3-of-3
> failing); PMT WasmTests 510/510. Ops note: `PMT_BROWSER_CHANNEL=chrome` poisons the
> shared Playwright profile for bundled-Chromium runs (sub-second "2/2 passed" sweeps =
> browser lane never launched; delete %TEMP%SpawnDev.ILGPU.PlaywrightProfile).

## Key Files
- `Backend/WasmKernelFunctionGenerator.cs` — kernel codegen, parameter setup, helper functions
- `Backend/WasmCodeGenerator.cs` — base IR visitor, GetField, Store, Atomic handlers
- `Backend/WasmBackend.cs` — compilation orchestration, helper generation, `LastWasmBinary`
- `Backend/WasmModuleBuilder.cs` — Wasm binary format builder (sections, types, functions)
- `WasmAccelerator.cs` — dispatch to workers, buffer management, struct serialization
- `WasmMemoryBuffer.cs` — SharedArrayBuffer-backed memory, zero-copy sharing
- `WasmILGPUDevice.cs` — device config (`MaxNumThreadsPerGroup = 256`, `MaxGroupSize = (256,1,1)`). NOTE: an earlier version of this line said 64 — that was stale; the device has set 256 (verified `WasmILGPUDevice.cs:68-69` + offline compile dump 2026-06-09). RadixSortKernel1's `scanMemory` is `int[groupSize*UnrollFactor]` = `int[1024]` at groupSize 256, UnrollFactor 4.

## Persistent Shared Worker Pool (2026-06-13) — `s_sharedWorkerPool`

The Web Worker pool is **process-static** (`WasmAccelerator.s_sharedWorkerPool`), created ONCE
per tab and reused across every **default-WorkerCount** `WasmAccelerator` (the PMT/production
common case). It is **not** recreated/terminated per accelerator. An accelerator created with an
explicit `WasmBackendOptions.WorkerCount` (the oversubscription stress tests at 16/48/3×cores)
uses a **private** pool (`_ownPool`, old create-on-first-dispatch / terminate-on-Dispose
lifecycle) so its large worker count can't permanently inflate the shared pool for the rest of
the lane. Two leak guards keep the shared pool bounded at ≈`hardwareConcurrency-2`: (a) only
default accelerators touch it, and (b) on Dispose any worker still **checked out** (dispatch in
flight at an abnormal/fire-and-forget dispose) is **terminated + removed** (not stranded — a
stranded worker drains the available queue and the shortfall-grow path balloons the pool; this
was caught 32-vs-10 by the regression test below before the guards were added). **Why:** PMT creates a fresh accelerator per test (~569 in the Wasm lane). The old
per-accelerator pool called `Worker.terminate()` on its whole pool at Dispose, but
`terminate()` is an **asynchronous** browser signal — the OS thread + its SharedArrayBuffer/Wasm-
memory references wind down *after* Dispose returns. So the next test immediately spun up a fresh
`hardwareConcurrency` pool while the previous pool's threads were still dying → transient worker
**oversubscription** that compounds across a long sequential lane → the pure-spin barrier can't get
all workers scheduled in its spin window → compute-**heavy** tests starve and time out late in
Phase B (light tests still squeak in — a throughput tax, not a flat tax). Tuvok's full-sweep
report: `TurboQuant_*` pass 128/128 in ~4s scoped, time out at 30s late in the 569-test lane.

**Why cross-accelerator reuse is SAFE:**
- the worker-side module-cache key (`KernelCacheEntry.KernelId`) is the **process-static** monotonic
  `_nextKernelId`, so two different accelerators can never hand a worker the same id → never a
  stale/stomped module (same reason the GC-hash kernelId bug was fixed — see "kernelId MUST be a
  monotonic unique id");
- a memory-buffer change invalidates the worker's cached instances (`_instancesById = {}` in
  `WorkerPool.cs` when `d.memory.buffer` differs), so a worker reused by a new accelerator
  re-instantiates against the new accelerator's `WebAssembly.Memory`;
- each accelerator installs its OWN per-worker message handlers on adoption
  (`EnsurePersistentHandlers`) and **DETACHES** them on Dispose (`DisposeAccelerator_SyncRoot`:
  `worker.OnMessage -= state.MsgHandler`). Detach (not terminate) is what frees the accelerator —
  without it, each disposed accelerator's handler closures stay attached to the persistent workers
  (a managed leak that keeps the disposed accelerator alive) and pile up as dead no-op listeners.
  So a reused worker carries exactly one accelerator's handlers at a time.

The pool is **never** disposed on accelerator Dispose (it outlives accelerators by design); tear it
down only via `WasmAccelerator.DisposeSharedWorkerPool()` (test/shutdown) or tab teardown.
`WasmAccelerator.SharedWorkerPoolSize` is a diagnostic: a correct pool stays bounded
(≈ `hardwareConcurrency - 2`) no matter how many accelerators come and go — it does NOT grow per
accelerator. Locked by `WasmTests.Wasm_SharedWorkerPool_PersistsAndStaysBoundedAcrossAccelerators`
(bounded-size + correct-on-reuse). **Caveat (not the PMT case):** if two accelerators are *concurrently*
alive and both adopt the same worker, both install handlers — detach-on-dispose only cleans the
dominant sequential case (PMT disposes the previous accelerator before creating the next).

**Module-cache flush — bounds `_modulesById` accumulation (2026-06-14).** Persistent workers keep every
distinct kernel's compiled `WebAssembly.Module` (`_modulesById`) for the tab's life. Each per-test
accelerator's kernels get fresh ids, so across a long lane the cache grows UNBOUNDED (Tuvok's ML trace:
`TotalKernelsCompiled` 2→1057 monotonic; committed shared linear memory was flat@96 MiB — the module
cache, not the working set, drove late-heavy-test memory-pressure timeouts). Fix: when
`_nextKernelId - s_lastFlushKernelId >= WasmBackend.ModuleCacheFlushThreshold` (default 256, 0=off), the
host sets `clearModuleCache=true` on the worker messages of the **next fresh accelerator's FIRST
dispatch**; the worker drops `_modulesById`/`_instancesById` (+ nulls `_lastMemoryBuffer`) then recompiles
from the re-sent bytes. **SAFE only at a first dispatch** — the accelerator's worker-init tracking is empty
there so it re-sends its own kernels; the cleared modules are disposed accelerators' dead weight. NEVER
flush mid-accelerator (would orphan modules it already told workers it had → "module not cached").
Sequential-accelerator assumption (like the rest of the shared pool — concurrent live accelerators could
see a peer's flush). Short workloads never cross the threshold → never flush → stay fully warm (the ILGPU
library Wasm lane is unaffected). Guard: `WasmTests.Wasm_ModuleCacheFlush_DoesNotBreakCorrectness`
(threshold=1, CPU-oracle). Diagnostic: `WasmAccelerator.TotalKernelsCompiled`.

**Host-write snapshot SABs MUST be Disposed (2026-06-14 leak fix).** `WasmMemoryBuffer.PrepareHostWrite`
allocates a FULL-buffer-size `new SharedArrayBuffer` (`WasmMemoryBuffer.cs:87`) when a host write lands
while a dispatch is in flight on that buffer (the lazy copy-out-race snapshot). This is a SECOND SAB path
distinct from the buffer's primary `SharedBuffer` — easy to miss. The original `CompleteDispatchIntent`
Remove()'d the snapshot from `_snapshotsByHWC` but NEVER `Dispose()`d the SAB (its doc lied: "that tier's
SAB is freed"), and the intents==0 path nulled the dict without disposing → every snapshot leaked a
full-buffer JS SharedArrayBuffer → ~1.5 GiB across the ML lane → late-test timeouts (root-caused by a
resident-memory trace; it was invisible to a primary-SAB byte counter). RULE: any `new SharedArrayBuffer`
(or JSObject) created here must be `.Dispose()`d on EVERY exit path (release + buffer dispose) —
`DisposeAllSnapshots()` does it now. Diagnostic `WasmMemoryBuffer.LiveSnapshotBytes`; guard
`WasmTests.Wasm_HostWriteSnapshot_DoesNotLeakSAB`. Lesson: a monotonic counter is not a memory proxy, and
a buffer's primary-SAB counter won't see a SECOND SAB path — measure the actual resident bytes per path.

## Process-static SHARED linear memory (2026-06-14) — `s_sharedWasmMemory`

The memory analog of the worker pool, and the **second half** the pool fix unmasked. A
`new WebAssembly.Memory({ shared: true })` reserves its FULL `maximum` (default 16384 pages = 1 GiB)
of virtual address space at construction and can **never relocate**, so each accelerator that built
its own memory burned a full 1 GiB reservation. Before the persistent pool, `Worker.terminate()` per
accelerator Dispose dropped the workers' references so the old reservation was freed/GC'd per test;
with the persistent pool the workers **pin** the last memory they instantiated against
(`_lastMemoryBuffer` + `_instancesById`, `WorkerPool.cs`) until they next swap. Across the ~569-test
lane, per-accelerator memories accumulated up to `workerCount` live 1 GiB reservations (plus JS-GC lag)
until V8's address-space cap was hit and the **`new WebAssembly.Memory(...)` CONSTRUCTOR** threw
`could not allocate memory` (Tuvok's 88 `RangeError`s, the memory half). NB: the constructor failing
(not `grow()` → our `OutOfMemoryException`) is the tell — accumulation, not single-memory high-water.

**Fix:** default accelerators share **ONE** process-static `s_sharedWasmMemory` per tab (grown to the
lane high-water, never re-created → exactly one reservation). The linear memory is per-dispatch
**transient** working/staging memory (zero region → copy-IN → run → copy-OUT; no cross-accelerator
state), so sharing is correct. Bonus: with persistent workers AND one persistent memory the buffer
only changes on `grow()`, so after high-water the workers **stop re-instantiating kernels entirely**
(per-test new-memory→instance-cache-clear churn gone).

- **Routing:** `UsesSharedMemory` = `_useSharedPool` (any default-WorkerCount accel). The shared memory
  is keyed by `MaxLinearMemoryPages` in `s_sharedByMaxPages` (Dictionary<int, SharedMemEntry>) — ONE
  shared memory PER distinct max. The create/grow/reuse block + Dispose read/write through
  `CachedWasmMemory`/`CachedMemoryBuffer`/`CachedWasmPages` → `SharedEntry` (this accel's max-group) when
  shared, else the private `_cachedWasmMemory`. Keyed by max because the kernel module declares its
  import max = its own MaxLinearMemoryPages and the spec needs supplied-max == module-declared-max — so
  all 16384 accels share a 16384 memory, all 32768 (ML DA3) share a 32768 memory. Only explicit-
  `WorkerCount` accels (want worker isolation) keep PRIVATE memory. **History:** originally only the
  default 16384 was shared (`== 16384` gate), which missed the ML lane's ~569 accels at a custom 32768 max
  — they took the private path and re-accumulated the leak at 2 GiB each (Tuvok's full ML sweep on
  local.4: 88→91, unchanged); generalized to per-max 2026-06-14.
- **Concurrency:** `s_sharedMemoryGate` (a `SemaphoreSlim(1,1)`) serializes the shared-memory dispatch
  window (acquire→zero→copy-IN→exec→copy-OUT) across accelerators — within one accelerator dispatches
  already serialize via `_pendingWork`; this extends that to two concurrently-alive default
  accelerators sharing the one memory (overlapping region-[0..) writes would corrupt). Uncontended /
  zero-cost in the sequential PMT/production case.
- **Lifetime:** never disposed on accelerator Dispose (shared accelerators leave the instance handles
  null; private accelerators dispose their own); torn down only via `WasmAccelerator.DisposeSharedWasmMemory()`
  (also called by `DisposeSharedWorkerPool()`) or tab teardown. Diagnostics: `SharedWasmMemoryCreateCount`
  (stays 1 across the lane), `SharedWasmMemoryPages` (high-water). Locked by
  `WasmTests.Wasm_SharedLinearMemory_PersistsAndStaysBoundedAcrossAccelerators` (≤1 construction across
  K accelerators + correct-on-reuse + explicit-count isolation).

## SIMD128 (v128) — emitter foundation (Phase 1, 2026-06-14)

Phase 1 of the Wasm SIMD128 port (`Plans/wasm-simd128-velocity-port-plan-2026-06-12.md`). **Additive
only — no production kernel emits v128 yet; the scalar path is byte-identical.** What landed:
- **`WasmOpCodes`:** `V128` value type (0x7B) + the `Simd*` sub-opcode set. **CRITICAL:** SIMD uses the
  `0xFD` prefix followed by a **u32 LEB128** sub-opcode (NOT a single byte like the 0xFE atomic
  sub-opcodes). Sub-opcodes ≥ 128 (most arithmetic — `f32x4.add`=228) are multi-byte LEB. Stored as
  `uint`; always emit via `EmitU32Leb128`. Values spec-verified (BinarySIMD.md, 2026-06-14).
- **`WasmModuleBuilder`:** `EmitSimd` (no-immediate ops), `EmitSimdMem` (v128.load/store memarg — same
  align/offset format as scalar; natural align for 16 bytes is 4=log2(16)), `EmitSimdLane`
  (extract/replace_lane, 1-byte lane immediate), `EmitV128Const`/`EmitF32x4ConstSplat` (16 immediate
  bytes), `EmitI8x16Shuffle` (16 lane-index bytes). v128 locals/func-types just use the 0x7B byte in
  the existing encoders — no encoder change needed.
- **Detection (per Captain):** `WasmBackend.RuntimeSupportsWasmSimd` = `System.Runtime.Intrinsics.Wasm.PackedSimd.IsSupported`.
  If the running Blazor WASM build has SIMD enabled, the browser (and its workers — same engine) accept
  v128; the compat build / SIMD-less browser / desktop CLR report false → scalar path. In-process
  equivalent of the app-level `wasmFeatureDetect.simd` build-picker in `BlazorWASMSIMDDetectExample`.
  Overrides: `ForceScalar` (off, mirrors `WebGPUBackend.ForceEmulatedF16`), `ForceSimd` (on, for
  offline codegen verification on desktop). `EffectiveWasmSimd` = `!ForceScalar && (ForceSimd || RuntimeSupportsWasmSimd)`.
  Surfaced as `WasmCapabilityContext.WasmSimd` + `WasmAccelerator.SupportsSimd`.
- **HARD REQUIREMENT (Captain):** non-SIMD devices stay FIRST-CLASS forever — the scalar path is a
  supported mode, never a deprecated fallback (AMD Phenom II X6, Firefox-on-Android-9, etc.).
- **Offline verify:** `DemoConsole -- wasm-simd-probe` (`WasmSimdProbe.cs`) hand-builds a v128 module →
  `wasm-validate` (clean) + `wasm2wat` (decodes to the intended instructions). SIMD is default-on in
  wabt 1.0.39 (no `--enable-simd` flag; use `--enable-threads` for the shared-memory import).

### SIMD128 codegen — SHIPPED coverage (Phase 3, 2026-06-20)
The additive `kernel_simd` emitter (`WasmSimdKernelEmitter.cs`, generated AFTER the scalar kernel in a
saved/restored context → scalar byte-identical; bails to scalar on anything out-of-class → zero
regression) now vectorizes a broad class:
- **Shapes:** single-block · canonical counted while-loops (v128 accumulator phi, via ILGPU `LoopInfo`) ·
  divergent if-DIAMONDs (`cond?x:y` → compare-mask + `v128.bitselect`, sides must be pure).
- **f32x4:** +/-/*/÷ min max neg abs sqrt floor ceil, all 6 compares.
- **i32x4:** +/-/* neg abs min max, v128 and/or/xor/not, shifts (UNIFORM count only), all compares.
- **i32→f32 convert** (cross-mode exact) · **GATHER** (`src[idx[i]]` → per-lane extract→scalar-load→
  replace). Lane-variant→v128, lane-invariant→scalar+splat. By-4 dispatch + scalar tail.
- **Gates:** `WasmTests.Wasm_Simd128_*` (15) each assert kernel_simd IS emitted + BIT-EXACT
  scalar==simd==CPU-reference. **ALWAYS run the FULL WasmTests after any coverage change** — SIMD is
  on-by-default in the browser, so a generalization can mis-vectorize an existing suite kernel.
- **Dual-mode CI (Stage 3d):** `PMT_WASM_SIMD=off` appends `?wasmsimd=off`; the Demo's Program.cs forces
  `WasmBackend.ForceScalar=true` for the whole sweep (scalar = cross-mode oracle). Marker
  `[Wasm-SIMD-Mode] ... scalar-mode sweep engaged` lands in `browser_console.log` (proof it engaged).
- **Cross-mode determinism is LAW:** never an op whose v128 form differs from scalar (no fused FMA; f32→i32
  stays scalar — wasm SIMD only has saturating trunc_sat, scalar traps).

NEXT (the hard tail): i64x2/f64x2 2-lane (double-pump the by-4 dispatch), scatter/masked stores + general
3b divergence, real `Warp.Shuffle` via `i8x16.shuffle`. Honest ceiling: ~1.5-3× on ALU-dense kernels (the
fixed dispatch/memory floor caps it; light/elementwise kernels are dispatch-bound, gain little).

## Offline compile dump (desktop, no browser) — `wasm-dump`

`SpawnDev.ILGPU.DemoConsole -- wasm-dump` compiles RadixSort kernels on the DESKTOP and prints the emitted shared-memory alloca table + flags any `GenerateCode(Alloca)` type+size fallback aliasing or offset overlap. Works because `WasmAccelerator.Create` wraps the `BlazorJSRuntime.JS` lookup in try/catch (defaults to 4 cores) and `CreateRadixSort*` compiles its kernels eagerly via `LoadKernel` BEFORE any dispatch — so the IL→wasm compile path runs fully offline (no workers, no Chromium, no dispatch). Reusable for any shared-memory layout audit. Source: `SpawnDev.ILGPU.DemoConsole/WasmCompileDump.cs`.

## Hard Constraints
- **Blazor WASM is single-threaded** — all async, no blocking. **Sync `stream.Synchronize()` / `accelerator.Synchronize()` are DESKTOP-ONLY and now THROW `NotSupportedException` on Wasm** (2026-06-13, "sync = desktop-only" contract — were previously a silent no-op/reap that returned before workers finished). Use `await SynchronizeAsync()` (the real `await Task.WhenAll(_pendingWork)` drain) to wait. **Sync `Flush()` stays valid on Wasm** — it is a fire-and-forget SUBMIT (a no-op here; nothing is batched), not a wait, so it does NOT throw (submit is synchronous on every backend, so there is no async `Flush` twin). Locked by `BackendTestBase.SyncSynchronizeContractTest` / `SyncFlushContractTest`.
- **2D/3D groups are SUPPORTED (fixed 2026-06-07, commit `274b57c`).** A `KernelConfig` with `Index2D`/`Index3D` GroupDim works on Wasm like it does on CPU/CUDA/OpenCL/WebGPU. The kernel ABI carries the real per-dimension group sizes (`realGroupDimX`/`realGroupDimY`, in addition to `groupDimX` which is the TOTAL group size used by the barrier last-thread check + group-id math), and `Group.Idx`/`Grid.Idx`/`Group.Dim`/`Grid.Dim` decompose X-fastest (`Group.IdxX = l % GroupDim.X`, `Grid.IdxX = g % (dimX/GroupDim.X)`, ...). 1D launches pass `realGroupDimX = groupSize, realGroupDimY = 1`. **History:** before the fix the index model assumed 1D (`groupDimX == groupSize`), so a 2D group made `gridDimX = dimX/groupSize = 0` → `Grid.IdxX % 0` "remainder by zero" trap in `WasmAccelerator.DispatchToWorkers`. Verified by `BackendTestBase.Group2D` (CPU oracle). Tiled kernels may still prefer a 1D group + manual 2D index (like `MatMulKernel.TiledMatMulImpl`) for register/codegen simplicity, but it is no longer required for correctness.

## Async drain + readback — core virtuals (2026-05-29)

`stream.Synchronize()` / `accelerator.Synchronize()` CANNOT block on the single Blazor
thread. Historically they were a silent no-op on Wasm/WebGL (reap completed tasks) and an
encoder-flush on WebGPU — **none drained in-flight work**, so a caller expecting completion
read stale/empty data. As of 2026-06-13 the sync WAIT forms (`Synchronize`) are
**desktop-only and THROW `NotSupportedException`** on all three browser backends;
`await SynchronizeAsync()` (the real drain) is the portable wait. Sync `Flush()` (a
fire-and-forget submit) stays valid on browser — only the WAIT is async-only. Any
code that does an immediate buffer
op (`CopyTo`/`CopyToCPU`/`MemSet`/sync `CopyToHost`) right after an unawaited dispatch
races the workers (Wasm reads stale; WebGPU sync GPU→CPU `CopyTo` throws). `CopyFromAsync`
was the first patch of one instance of this class.

The real fix is a pair of overridable core async primitives (so the algorithm layer in
`ILGPU.Algorithms`, which references only `ILGPU` core, can reach a true drain without
seeing backend types):

- **`Accelerator.SynchronizeAsync()` / `AcceleratorStream.SynchronizeAsync()`** — now
  `virtual` in core (`Accelerator.cs`, `AcceleratorStream.cs`). Default = run sync
  `Synchronize` + completed task (correct for CUDA/OpenCL/CPU). `WasmAccelerator` /
  `WasmStream` override to `await Task.WhenAll(_pendingWork)`; WebGPU/WebGL override to
  their real async waits. The core `AcceleratorStream.SynchronizeAsync` used to be a
  NON-virtual `Task.Run(synchronizeAction)` — fake on Wasm (ran the no-op on a thread).
- **`MemoryBuffer.CopyToRawAsync(stream, offsetBytes, lengthBytes)`** — `virtual` in core,
  returns `Task<byte[]>`. Default = drain via `SynchronizeAsync` then sync `CopyTo`.
  `WasmMemoryBuffer` overrides = drain then read the `SharedArrayBuffer` slice;
  `WebGPUMemoryBuffer` = `CopyBufferToBuffer` + `mapAsync`; `WebGLMemoryBuffer` = GL-worker
  readback. Exposed to consumers as the core extension **`ArrayView<T>.CopyToCPUAsync(stream)`**.

**Rule:** algorithm/consumer code that needs a host-visible scalar/array after a dispatch
must `await accelerator.SynchronizeAsync()` + `view.CopyToCPUAsync(...)` (or SpawnDev's
`CopyToHostAsync` / `CopyFromAsync` / `MemSetToZeroAsync`) — NEVER the synchronous
`CopyToCPU`/`Synchronize`/`MemSet`, which silently do nothing on these backends.
`ReductionExtensions.ReduceAsync` was the canonical victim (was `Task.Run(sync Reduce)` →
threw on WebGPU / stale on Wasm); it now uses these. The synchronous `Reduce`→scalar
overloads throw a clear `NotSupportedException` on Wasm/WebGL/WebGPU instead of returning
stale data.

**Race detector (opt-in):** `WasmMemoryBuffer.DetectHostBufferRaces` (default false). When
true, the synchronous host ops (`MemSet` / `CopyTo` / `CopyToHost`) throw if the buffer has
an in-flight dispatch (`_pendingSnapshotIntents > 0`, incremented synchronously at queue
time in `RunKernel`). `CopyFrom*` are NOT guarded — the lazy snapshot mechanism
(`PrepareHostWrite`) protects them by design. Enable it in a PMT sweep to ENUMERATE any
remaining sync-readback race sites that the async APIs replace; a properly-drained path
never trips it. Locked by `WasmTests.DetectHostBufferRaceTest` (sync read on the same JS
turn as an unawaited dispatch deterministically throws; succeeds after `SynchronizeAsync`).
- **Serialized dispatch** — `RunKernelAsync` awaits `_pendingWork` before each dispatch.
- **Struct-with-view serialization** — CLR layout ≠ IR layout. Use `WasmParamInfo.StructFields` + `FlattenCLRStruct()` for manual IR-layout serialization. See SKILL.md for details.
- **`IsViewType()` distinguishes views from struct-with-view** — checks if `DirectFields[0] is AddressSpaceType`.
- **Empty struct padding** — `Stride1D.Dense` has no CLR fields but IR adds Int8 padding.
- **`SpecializedValue<T>` unwrapping** — IR lowers to PrimitiveType; dispatch must extract inner value.
- **`LongIndex1D` as first param** — it's extent (loop bound), not thread index. Don't map to `_globalIdxLocal`.
- **Buffer deduplication** — SubViews of same buffer share one copy in Wasm memory.
- **NativePtr patching** — set to Wasm offset before struct serialization, restore to 0 after.
- **Multi-pass algorithms** — route to `CreateSingleGroupScan` (ScanExtensions.cs, AcceleratorType.Wasm).

## Tribal Knowledge: GetField View Field Mapping (March 2026)

**FIELD MAPPING RULE**: In the `GetField` handler for view parameters, field 1 is context-sensitive:
- **StructureType views** (ArrayView1D): field 1 = **Extent (Length)** → return `locals[1]`
- **AddressSpaceType views** (ArrayView): field 1 = **Index/Offset** → return 0

This was hardcoded to 0 for ALL views, which broke `view.Length` for ArrayView1D params.
The fix checks `param.Type is StructureType`. Current: 249 pass / 0 fail / 3 skip (v4.6.0). Full `hardwareConcurrency` multi-worker barrier dispatch with pure-spin generation barriers (wait/notify races on V8 — see the "Barriers are PURE SPIN" note below). In-Wasm phase dispatcher eliminates JS-Wasm boundary crossings between phases.

**TRACE RULE**: Both `GetViewLength` and `GetField` must trace the view source back to
the kernel Parameter through GetField/NewView/AddressSpaceCast chains (via `TraceToParameter()`).
ArrayView1D's BaseView access creates a GetField indirection that breaks direct Parameter lookup.

## Kernel Function Signature
9 system params + user params:
`kernel(globalIdx, dimX, dimY, scratchBase, groupDimX, threadIdX, sharedMemBase, barrierBase, dynamicSharedLen, ...userParams)`

## Barrier Dispatch (Fiber-Based Phase Model)
- Each Web Worker = one thread within a workgroup.
- **Fiber refactor (March 2026):** Kernels with barriers are compiled into a phase-based dispatch model. Each barrier becomes a yield point — the kernel saves its state (locals + phase counter) to scratch memory and returns. A **Wasm-native phase dispatcher** handles the entire thread/phase/group loop inside WebAssembly, eliminating JS-Wasm boundary crossings between phases.
- **Dynamic block splitting:** Barrier-separated code blocks are split into phases automatically. Helper function calls (scan, sort) each get their own phase with yield points before and after.
- **Completion state persist:** The kernel saves its exit state to scratch so the worker knows when all phases for a group are done before advancing to the next group.
- **Barriers are PURE SPIN — wait/notify races on V8 (verdict re-confirmed 2026-05-24).** The dispatcher phase barrier and group barrier (`GeneratePhaseDispatcher` in `WasmBackend.cs`) use a pure `i32.atomic.load` spin loop on a generation counter, with a yield-to-JS escape after a spin threshold (phase barrier) to survive CPU oversubscription. The in-kernel `EmitBarrier` path is also pure-spin (and bypassed entirely in phase mode). **Do NOT switch to `memory.atomic.wait32`/`notify`.** History: April 2026 briefly shipped wait/notify ("fixed spurious-wakeup with a `while` loop") but it was reverted to spin in rc.25/rc.27 because large sorts produced non-deterministic corruption. Re-tested 2026-05-24 behind the default-off `WasmBackend.UseWaitNotifyBarriers` flag on current Chrome + current backend: **wait/notify STILL races** — large multi-group RadixSorts fail with sort-order violations / value duplicates (1.4M: 1067 violations, 500K: 187, 1M: duplicate keys); small single-group sorts pass. Our codegen is seq_cst-correct (fence before gen store; seq_cst gen load in waiter synchronizes-with it), so this is a V8 linear-memory wait/notify ordering bug (chromium#490434403 family). The April "275-local spill" theory is disproven — the barrier is in the ~38-local dispatcher and still races, so reducing locals can't dodge it. The flag stays only as a one-flip re-test harness for when a future V8 ships a fix. Full log: `Plans/wasm-waitnotify-still-races-2026-05-24.md`.
- **Pure spin was NOT fully correct either — GROUP-barrier release fence was missing (fixed 2026-05-25).** The standing "pure spin is correct, wait/notify races" verdict was incomplete. The PHASE-barrier producer always had a release `atomic.fence` immediately before its gen `i32.atomic.store` (offset 4) — but the GROUP-barrier producer bumped its gen (offset 20) with **no preceding fence**. Proven in the emitted dispatcher binary: phase gen store had a preceding `atomic.fence`, group gen store did not. On V8's wasm linear-memory ordering path a waiter could observe the advanced group gen via its seq_cst load yet read stale group data → **intermittent sort-order violations on large multi-group RadixSorts** (1.4M: 427, 1.5M odd-count: 1047; fired ~1-2 of every 3 runs, large multi-group only, load-dependent). This was a real codegen bug, NOT the V8 race — it reproduced on the pure-spin path with `UseWaitNotifyBarriers=false`. Fix: emit `AtomicFence` before the group gen store, mirroring the phase barrier (one fence suffices on the group path because its exit-flag reset and gen store are adjacent, covering both the publish-resets and release-before-gen-bump roles the phase path uses two fences for). Impact: this fixed the DOMINANT corruption mode — violation magnitude collapsed from 427-1047 down to ≤9, and per-run failure rate dropped from ~1-2/3 (~50%) to ~1/7 (~14%, observed: 6 consecutive `PMT_FILTER=WasmTests` GREEN then run 7 tripped `RadixSortDescendingWithSentinelsTest` with 9 order violations under accumulated load). **A rarer RESIDUAL race** [**✅ ROOT-CAUSED + FIXED 2026-06-09 — see the RESOLVED banner at the top of this file**]: heavy-duplicate multi-pass sorts (`RadixSortDescendingWithSentinels`, ~15 dups/key, 2-pass NumBits=4 already trips), ±1 adjacent-value errors scattered across groups, load/yield-correlated. **Cause: missing entry `Group.Barrier()` in `ILGroupExtensions.InclusiveScanImplementation`/`AllReduce` (reused-shared-region write-after-read across scan tiles). The ±1 adjacent-value errors are exactly the adjacent-thread slot collision (`sharedMemory[LinearIndex]` write vs neighbor's `[LinearIndex-1]` read); the "block displacement" is the clobbered tile RightBoundary. Fixed.** The phase barrier (scan↔scatter sync) already carries all three expected fences and the resume path reads as correct on inspection, so the residual is suspected to be either the V8 pure-spin linear-memory ordering bug surfacing under heavy yielding, or a kernel-side fiber state-save gap — do NOT assume the group-fence fix closed the Wasm large-sort corruption entirely. So `GeneratePhaseDispatcher` now has the release fence before BOTH the phase and group gen stores; the wait/notify V8 race verdict above is unchanged and independent.
- **Residual large-sort corruption — STRONG candidate root cause found + fixed: the worker module-cache `kernelId` was derived from `RuntimeHelpers.GetHashCode(wasmBytes)` (fixed 2026-05-26).** The worker-side module/instance cache is keyed by `kernelId` (`_modulesById[kid]` / `_instancesById[kid]`, `WorkerPool.cs`). `WasmAccelerator` derived that id from `RuntimeHelpers.GetHashCode(wasmBytes)` — an **object identity hash**, which is a heuristic that (a) does NOT guarantee distinct values for distinct objects and (b) RECYCLES freed slots under Mono/Wasm GC. So two distinct LIVE kernels could collide on one id: each tracks "have I sent my bytes to this worker?" in its OWN set, so kernel A can skip re-sending while the shared `_modulesById[kid]` slot actually holds kernel B's stomped module → the worker runs the WRONG cached module → silent sort corruption. This matches the residual's exact profile (full-sweep-only, multi-kernel churn, load-correlated — a scoped warm loop reuses ONE kernel so it never collides). **Fix:** `kernelId` is now a process-unique monotonic `Interlocked.Increment(ref _nextKernelId)` carried on a per-kernel `KernelCacheEntry` (`WasmAccelerator.cs`); unique ids cannot collide, so no worker can ever be handed the wrong module. This is a correct fix **regardless** of the residual race — using a non-unique identity hash as a persistent cache key is a textbook bug (see the "kernelId MUST be a monotonic unique id" section below). **HONESTY:** it is a strong, mechanism-matched candidate for the residual corruption but was NOT proven to be the sole cause (no expensive collision-repro campaign was run — the fix is justified by correctness alone). Watch future natural sweeps: if the heavy-dup ±1 residual recurs after this, the kernelId collision was not the whole story.
- **GROUP barrier now has a yield-to-JS escape, mirroring the PHASE barrier (fixed 2026-05-26).** The PHASE-barrier waiter yields to JS after `YIELD_SPIN_THRESHOLD` spins (saving state, returning, resuming via `resumeMode=1`) so a descheduled worker can't starve the pool under CPU oversubscription. The GROUP-barrier waiter had **no** such escape — it spun forever, so under worker oversubscription (workers >> cores, e.g. SpawnScene under heavy multitasking) the group-barrier waiters burned every core and the not-yet-arrived worker never got scheduled → **livelock/hang** (reproduced earlier: 2 PMT timeouts at 2× cores). Fix: the group waiter now uses the same spin-count + threshold + save-state + `return` escape, with a distinct **`yieldFlag=2`** (vs the phase barrier's `1`) in the per-worker yield buffer. The resume prologue routes `yieldFlag==2` down a **GROUP-RESUME** path that skips the phase loop + group-arrival (already done before the yield — re-arriving would double-count) and re-enters the group spin with the restored group `savedGen`; the JS park (`Atomics.wait`) waits on the **group** gen slot (`fenceBase+20`) for a group yield vs the phase gen slot (`+4`) for a phase yield. Verified: an oversubscribed (≥3× cores) multi-group barrier kernel that previously **livelocked** now **completes** in ~4s with correct output; full `PMT_FILTER=WasmTests` sweep stays green (no regression to the normal non-oversubscribed path). See `GeneratePhaseDispatcher` in `WasmBackend.cs` (group waiter + prologue + group-arrival wrap) and the JS resume loop in `WasmAccelerator.cs`.

## Tribal Knowledge: kernelId MUST be a monotonic unique id — NEVER GetHashCode (2026-05-26)

**RULE: never use `Object.GetHashCode()` / `RuntimeHelpers.GetHashCode()` as a persistent identifier, cache key, or wire/worker id.** It is a *heuristic*, not a unique identifier:
- **Identity collisions:** the probability that two distinct objects return the same hash is non-zero. A hash is for bucketing in a hash table (where collisions are handled), NOT for identity.
- **GC recycling:** an object identity hash is tied to the live object; when that object is collected its hash-slot frees and a later allocation can be handed the SAME value. Across a long-running session this is not rare — it is expected.
- **Messaging corruption:** when the id rides in a dispatch message (here, the worker-side Wasm module cache key `_modulesById[kid]` in `WorkerPool.cs`), a collision makes the receiver act on a *lie* — it reuses the wrong cached module / pipeline state, silently producing wrong results with no crash.
- **Blazor/Wasm hazard:** under .NET Wasm Hybrid Globalization, `string.GetHashCode()` is increasingly delegated to browser-native APIs → platform-dependent volatility and `PlatformNotSupportedException` risk. A monotonic `Interlocked.Increment` decouples our protocol from all of that.

**For identity, use one of:** a monotonic counter (`Interlocked.Increment` — what `WasmAccelerator._nextKernelId` now does), a `Guid`, or an actual content hash (SHA-256) when you need content-addressing. Never the object hash.

**Where this bit us:** `WasmAccelerator` keyed the worker module cache with `RuntimeHelpers.GetHashCode(wasmBytes)` — a strong candidate root cause for the residual large-sort corruption (see the kernelId bullet in "Barrier Dispatch" above). Fixed 2026-05-26 with a per-kernel `KernelCacheEntry { int KernelId = Interlocked.Increment(ref _nextKernelId); HashSet<Worker> Workers; }`. **Follow-up (tracked):** a codebase-wide audit for other `GetHashCode`-as-identifier misuse across all backends — see `Plans/gethashcode-as-id-audit-2026-05-26.md`.

## Tribal Knowledge: GridIndex vs BucketIndex Bug (March 2026)

**RADIX RULE**: All atomic/store writes to shared histograms MUST verify the bucket index multiplier in the address computation. When the codegen unrolls a per-bucket loop, each iteration must use a DIFFERENT counter address — `counter_base + (numGroups * bucket + gridIndex) * sizeof(int)`. If the unrolled writes all share the same `local` for the index (e.g. `gridIndex` which is 0 for single-group), they all hit `counter[0]` and the histogram is silently wrong. The data appears unchanged (no crash, no trap) because the scan of an all-zero histogram produces all-zero offsets, so the scatter is a no-op.

**Current status**: RESOLVED. Counter addresses were correct. The real issues were: local alloca at address 0, and missing post-helper barriers. See rules below.

## Tribal Knowledge: Local Alloca Must Use Scratch (March 2026)

**LOCAL ALLOCA RULE**: The base `Alloca` handler in `WasmCodeGenerator.cs` sets local alloca addresses to `i32.const 0`. This causes the kernel to write to Wasm memory address 0 (the data buffer region). The `WasmKernelFunctionGenerator` MUST override this for non-shared allocas to allocate scratch memory (`scratchBaseLocal + offset`). Without this fix, the ExclusiveScan helper's output struct gets written to address 0, corrupting sorted data between RadixSort passes.

## ✅ FIXED: device-local `new T[N>32]` dynamically-indexed reads 0 (fixed 2026-06-23, 4.16.0)

**A kernel-local `var acc = new T[N]` with N > 32 that is WRITTEN+READ by a RUNTIME index used to return 0 on Wasm** (correct on the other 5 backends). FIXED in `ILGPU/IR/Transformations/LowerArrays.cs` - now correct on all 6 backends; `new T[]` and `LocalMemory.Allocate<T>(N)` are both fine.

**Root cause (the real one, after several wrong theories):** the rewriter positions new values at `value.Index + 1`, so the alloca/view/defaultElement created while lowering `NewArray` landed AFTER `value`. `SplitBlock(value)` moves everything after the split point into the loop EXIT block (and removes the split point itself), so that setup - including the array's **view pointer** - got pushed into the loop exit. The view was therefore DEFINED in the loop exit but USED in the loop body (and the struct): an SSA dominance violation. GPU backends rematerialize the *static* alloca address at each use and tolerated it; the Wasm state machine executes the literal block order, so the zero-init loop ran with an unset (0) view local and wrote zeros to low linear memory instead of the array (read back 0). Diagnosed by correlating the `[Wasm-SM] Block` map with the emitted `local.set _state` sequence (`DemoConsole -- local-array-dump` + `wasm2wat`).

**Fix:** create the setup BEFORE `value` (`builder.SetupInsertPosition(value)` at the top of the `NewArray` lowering) so it stays in the dominating current block while `SplitBlock(value)` exports only the trailing user code. Result-neutral for the 5 GPU backends; unroll path (N <= 32) byte-identical. **NOT** `SplitBlock(defaultElement)` - that orphaned the defaultElement constant (`SplitBlock` removes its split point) and regressed the PTX/OpenCL fixpoint analysis (`KeyNotFound`); caught by the full PMT sweep.

**Ruled out earlier (history, do not re-try):** alloca addressing (disasm-correct), the array-impl struct `{view,length}` slot, CFG canonicalization, full struct-lowering - none were the cause; it was always the view-def block placement. Gate: `BackendTestBase.LocalArray_DynamicIndex_MatchesCpuOracle` (N=64, runtime index, CPU oracle) passes on all 6 backends (Wasm skip removed). Full PMT sweep 4006/0/260. Trail: `_DevComms/SpawnDev.ILGPU/geordi-localarray-dynindex-fixed-wasm-scratchoffset-open-2026-06-22.md`.

## Tribal Knowledge: Post-Helper Barrier (March 2026)

**POST-HELPER BARRIER RULE**: After every helper function call that uses barriers, the codegen
MUST emit an additional barrier. Without it, a fast worker can start the next helper call while
a slow worker is still completing the previous one. Since helpers use shared memory at fixed
offsets, overlapping calls corrupt scan results, causing non-deterministic duplicate values in
the RadixSort presort. The fix is in `GenerateCode(MethodCall)` — after advancing the barrier
counter for the helper's barriers, emit one more `EmitBarrier(_barrierCounter++)`.

**Canary test**: `AlgorithmRadixSortNonPairsIntTest` sorts [32,31,...,1] → [1,2,...,32].
If this test produces duplicates or non-deterministic results, the post-helper barrier is broken.

## Fiber Refactor Status (March 2026) — COMPLETE

**Test results: 249 pass / 0 fail / 3 skip** (up from 49/10/17 pre-refactor). All RadixSort (including SpawnSceneSimulation 1.4M multi-frame), scan, barrier, sort, and large sort (260K-4M) tests pass on the Wasm backend at full `hardwareConcurrency`.

**Multi-worker:** Full `hardwareConcurrency` barrier dispatch with pure-spin generation barriers (wait/notify races on V8 — see the "Barriers are PURE SPIN" note above), `atomic.fence` at 3 sync points, float atomic stores via reinterpret, broadcast atomic store/load. In-Wasm phase dispatcher eliminates JS-Wasm boundary crossings. `hardwareConcurrency` workers for both barrier and non-barrier kernels.

The fiber refactor resolved the multi-group barrier dispatch limitation. 20+ bugs were fixed collaboratively:

1. **Fiber yield-per-phase** — dynamic block splitting with yield points at each barrier
2. **br depth +1** — helper if-nesting depth fix for branch target calculation
3. **Scratch overflow** — ScratchPerThread set after phase state computed, not before
4. **Completion state persist** — kernel saves exit state for worker re-entry
5. **Shared memory dedup** — prevent inflation from multiple SetupSharedAllocations calls
6. **TryGetValue bool flag** — prevent calling Math.sin instead of helper function
7. **Sync yield after helper done** — prevent shared memory stomping between sequential helper calls
8. **Scratch zeroing** — zero from scratchBase (not 0) to prevent stale data between dispatches
9. **Struct/scratch overlap** — struct body params placed AFTER per-thread scratch
10. **Generation barrier** — landed pure spin; April 2026 wait/notify attempt (wait32 + while-loop spurious-wakeup defense) reverted to spin in rc.25/rc.27, race re-confirmed on V8 2026-05-24 (see "Barriers are PURE SPIN" note)
11. **Shared memory alloca overlap** — same-size allocas deduped to same offset; fixed with distinct sizes
12. **IR address space aliasing** — InferAddressSpaces guards for phi/predicate/general values with Shared sources
13. **Zero region race** — between-group zeroing loop excluded fence slots to prevent deadlock
14. **Per-worker scratch** — eliminated intermittent corruption from shared scratch regions
15. **WorkerPool re-instantiation** — compare `.buffer` instead of Memory object identity

The skipped tests are intentional backend capability skips (e.g., features not applicable to Wasm), not failures.

## Tribal Knowledge: Struct Body Placement (March 2026)

**STRUCT REGION RULE**: Struct parameters serialized to scratch (e.g., `ReductionImplementation` in `GridStrideLoopKernel`) must be placed in the `structRegionBase` area, which is AFTER all per-thread scratch regions. The per-thread scratch at `scratchBase + tid * scratchPerThread` is used for state save/restore during barrier yields. If a struct is placed at `scratchBase + 0`, thread 0's state save overwrites the struct fields, causing subsequent threads to read corrupted data (wrong ReducedValue, pointer values, etc.). The fix ensures `structRegionBase = scratchBase + scratchSize` (8-byte aligned).

## Debugging
- `WasmBackend.LastWasmBinary` — capture last compiled kernel
- `WasmBackend.AllKernelInfos` — compilation summaries
- Disassemble: `wasm2wat --enable-threads kernel.wasm` (MUST use --enable-threads)
- Do NOT use LINQ in Blazor WASM logging — silently fails. Use for-loops.

## Tribal Knowledge: Struct Load Must Copy (March 2026)

**STRUCT LOAD RULE**: When `Load` is called with a `StructureType`, the codegen MUST copy the struct data from the source address to a scratch slot. Returning the source address directly creates an ALIAS — subsequent writes to the array (e.g., in-place RadixSort pre-sort `view[pos] = value`) overwrite the "loaded" value. Primitive Loads are safe because they copy to Wasm locals (immutable). Struct Loads use SSA-keyed scratch slots (`_structLoadSlots`) to minimize scratch usage while ensuring snapshot semantics.

## Tribal Knowledge: Unsigned Comparison (March 2026)

**UNSIGNED RULE**: Both `CompareValue` and `GenericAtomic` (Min/Max CAS loop) must check for unsigned flags (`IsUnsignedOrUnordered` / `IsUnsigned`) and emit `i32.lt_u`/`i64.lt_u` instead of signed variants. Without this, `MinUInt32`/`MinUInt64` reductions return the identity value because the signed comparison treats large unsigned values as negative.

## Tribal Knowledge: Atomic RMW Opcode Table (March 2026)

**OPCODE RULE**: The Wasm threads spec interleaves sub-word variants (rmw8, rmw16, rmw32) between each full-word RMW operation. The opcode numbering is NOT sequential per-operation type. Each operation group has 7 opcodes: i32.rmw, i64.rmw, i32.rmw8_u, i32.rmw16_u, i64.rmw8_u, i64.rmw16_u, i64.rmw32_u. So `i32.atomic.rmw.add` = 0x1E but `i32.atomic.rmw.and` = 0x2C (not 0x22!). The CmpXchg constants (0x48/0x49) were correct because they were at the END of the sequence. Add (0x1E/0x1F) was correct because it's at the START. Everything in between (Sub, And, Or, Xor, Xchg) was wrong and caused "invalid alignment" validation errors.
