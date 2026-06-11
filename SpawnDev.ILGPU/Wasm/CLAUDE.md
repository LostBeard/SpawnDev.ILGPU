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
> **KNOWN REMAINING (tracked):** a ~1/1000-dispatch late-spill tail in Playwright-bundled
> Chromium ONLY (Node clean at 4× oversub; canary: `GlobalInclusiveScanHighTrialTest`, now
> with full mismatch fingerprinting - decode: a fiber's output-phase restore reads the
> previous phase's carry spill for a few consecutive tiles, then self-heals). Planned fix:
> liveness-based spill reduction (spill only live locals per yield), which makes VERIFIED
> spills affordable. NOTE: `PMT_BROWSER_CHANNEL=chrome` (real Chrome) is environmentally
> incompatible with the Wasm test lane (deterministic iter-0 garbage - different failure
> class) AND poisons the shared Playwright profile dir for subsequent bundled-Chromium runs
> (schema upgrade → browser-lane enumeration silently dies → "2/2 passed" sweeps; fix:
> delete `%TEMP%\SpawnDev.ILGPU.PlaywrightProfile`).
> The 2026-06-09 Group.Barrier() attribution (entry barrier in ILGroupExtensions) was a
> REAL S11-class fix for the 5 non-Wasm backends but did NOT close the Wasm path; the
> entry-barrier-on-Wasm attempts made it worse (the scanResults copy was load-bearing
> reuse cushion). Forensic trail: `_DevComms/global/seven-*-2026-06-10.md` + the
> instruments in [`repro/wasm-scan-repro/`](repro/wasm-scan-repro/README.md)
> (`patch-pub-timing.mjs` DBG_KERNEL=3 gen-stamp rings, `patch-debug-ring.mjs` ring1b =
> the store-fate detector). Older write-ups
> ([`Notes/residual-sort-race-2026-05-25.md`](Notes/residual-sort-race-2026-05-25.md),
> [`RESEARCH-INDEX.md`](RESEARCH-INDEX.md)) are historical context.

## Key Files
- `Backend/WasmKernelFunctionGenerator.cs` — kernel codegen, parameter setup, helper functions
- `Backend/WasmCodeGenerator.cs` — base IR visitor, GetField, Store, Atomic handlers
- `Backend/WasmBackend.cs` — compilation orchestration, helper generation, `LastWasmBinary`
- `Backend/WasmModuleBuilder.cs` — Wasm binary format builder (sections, types, functions)
- `WasmAccelerator.cs` — dispatch to workers, buffer management, struct serialization
- `WasmMemoryBuffer.cs` — SharedArrayBuffer-backed memory, zero-copy sharing
- `WasmILGPUDevice.cs` — device config (`MaxNumThreadsPerGroup = 256`, `MaxGroupSize = (256,1,1)`). NOTE: an earlier version of this line said 64 — that was stale; the device has set 256 (verified `WasmILGPUDevice.cs:68-69` + offline compile dump 2026-06-09). RadixSortKernel1's `scanMemory` is `int[groupSize*UnrollFactor]` = `int[1024]` at groupSize 256, UnrollFactor 4.

## Offline compile dump (desktop, no browser) — `wasm-dump`

`SpawnDev.ILGPU.DemoConsole -- wasm-dump` compiles RadixSort kernels on the DESKTOP and prints the emitted shared-memory alloca table + flags any `GenerateCode(Alloca)` type+size fallback aliasing or offset overlap. Works because `WasmAccelerator.Create` wraps the `BlazorJSRuntime.JS` lookup in try/catch (defaults to 4 cores) and `CreateRadixSort*` compiles its kernels eagerly via `LoadKernel` BEFORE any dispatch — so the IL→wasm compile path runs fully offline (no workers, no Chromium, no dispatch). Reusable for any shared-memory layout audit. Source: `SpawnDev.ILGPU.DemoConsole/WasmCompileDump.cs`.

## Hard Constraints
- **Blazor WASM is single-threaded** — all async, no blocking. `stream.Synchronize()` is a no-op.
- **2D/3D groups are SUPPORTED (fixed 2026-06-07, commit `274b57c`).** A `KernelConfig` with `Index2D`/`Index3D` GroupDim works on Wasm like it does on CPU/CUDA/OpenCL/WebGPU. The kernel ABI carries the real per-dimension group sizes (`realGroupDimX`/`realGroupDimY`, in addition to `groupDimX` which is the TOTAL group size used by the barrier last-thread check + group-id math), and `Group.Idx`/`Grid.Idx`/`Group.Dim`/`Grid.Dim` decompose X-fastest (`Group.IdxX = l % GroupDim.X`, `Grid.IdxX = g % (dimX/GroupDim.X)`, ...). 1D launches pass `realGroupDimX = groupSize, realGroupDimY = 1`. **History:** before the fix the index model assumed 1D (`groupDimX == groupSize`), so a 2D group made `gridDimX = dimX/groupSize = 0` → `Grid.IdxX % 0` "remainder by zero" trap in `WasmAccelerator.DispatchToWorkers`. Verified by `BackendTestBase.Group2D` (CPU oracle). Tiled kernels may still prefer a 1D group + manual 2D index (like `MatMulKernel.TiledMatMulImpl`) for register/codegen simplicity, but it is no longer required for correctness.

## Async drain + readback — core virtuals (2026-05-29)

`stream.Synchronize()` / `accelerator.Synchronize()` CANNOT block on the single Blazor
thread, so on Wasm/WebGL they only reap completed tasks and on WebGPU only flush the
encoder — **none of them drain in-flight work.** Any code that does an immediate buffer
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
