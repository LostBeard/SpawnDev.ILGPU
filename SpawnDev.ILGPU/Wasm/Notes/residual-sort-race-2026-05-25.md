# Residual Wasm large-sort race — investigation notes (2026-05-25/26, Tuvok)

## ★★★ SESSION 10 (2026-05-29 PM, Tuvok, lead) — engine-vs-pattern REPRO ran on real Chrome; barrier+fan-in read visibility is SOUND → boundary-read hypothesis REFUTED

**This settles the decided Session-9 question.** Ran the approved repro on TJ's **actual Chrome
148.0.7778.216 (HeadlessChrome, V8 ~14.8)** — the live engine where the residual fires — driven by
a zero-dep CDP harness, NOT Node (whose V8 13.6 is irrelevant).

**Repro design (faithful to RadixSortExtensions.cs:747 + WasmGroupExtensions.cs:98-102):** ONE writer
(thread 0) writes a SINGLE reused shared slot BEFORE a generation barrier; every other worker does
exactly ONE `Atomics.load` of that same slot AFTER the barrier, **no re-validate loop** — the exact
fan-in shape of the radix scatter boundary read and the scan boundary reads (both are
thread-0-serial-write → barrier → many-thread single-read; confirmed identical shape by reading both).
Value rotates per iteration so any stale (prior-generation) read is detectable; double-barrier brackets
slot reuse. Reads are `Atomics.load` (seq_cst) to match our `_hasBarriers` codegen.
Files: `_research/v8-atomics-wait-bug/{spin_single_reused_slot.mjs, fanin.html, fanin-worker.js, run-fanin-chrome.mjs}`.

**Three barrier variants tested, the decisive one being the PRODUCTION path under oversubscription:**
- `spin` = bare seq_cst spin (our default at concurrency ≤ cores).
- `yield-park` = **faithful production path**: spin then `Atomics.wait`-park (no timeout), OUTER while
  re-checks gen on wake (WasmBackend dispatcher spin + WasmAccelerator Variant C park). This is what
  actually runs when workers >> cores — i.e. Fallout76 multitasking, the exact residual condition.
- `wait-loop` = pure wait/notify with the correct while-loop (TC39 #3800 Test-4 form), for contrast.

**RESULTS (TJ's Chrome 148, two independent runs):**
| config | workers | oversub | reads | stale | verdict |
|--------|---------|---------|-------|-------|---------|
| spin | 12 (=cores) | 1x | 3,300,000 | **0** | fresh (run A); run B timed out = bare-spin STARVATION, not visibility |
| yield-park | 24 | 2x | 1,380,000 + 4,600,000 | **0** | fresh |
| yield-park | 48 | 4x | 1,410,000 + 4,700,000 | **0** | fresh |
| wait-loop | 24 | 2x | 1,380,000 + 4,600,000 | **0** | fresh |

**~18 MILLION oversubscribed fan-in park/wake reads on the production barrier, ZERO stale.**

**VERDICT — the boundary-read visibility hypothesis is REFUTED.** A single `Atomics.load` of a reused
slot after the production yield-park barrier is FRESH on Chrome 148 even at 4x oversubscription. The
Session-9 prime suspect (RadixSortExtensions.cs:747 direct boundary read + the scan boundary reads
lacking a re-validate loop) is **NOT** the residual cause — those reads do not need hardening; the
barrier already establishes the happens-before. Corollary: the ca20808 broadcast tag-handshake almost
certainly did NOT fix anything for the residual (its benefit was already noted statistically unproven,
~1.6% unchanged) — consistent with the read never having been the problem.

**Magnitude argument that makes 0-stale dispositive (not just "low trials"):** TJ's failing sweeps show
~21% mismatch (run1 `RadixSortDescending4M`: 2373 viol / 874,509 mismatches). A barrier-visibility
failure at that magnitude would have lit up an 18M-read oversubscribed test thousands of times. Zero
fires ⇒ the residual is a CATASTROPHIC-magnitude logic bug, not a sprinkle of read staleness.

**REDIRECT — next suspects are KERNEL-SIDE / DISPATCH-SIDE (a wholesale-corruption mechanism), in order:**
1. **Shared-memory slot lifetime / overlap.** `SetupSharedAllocations` (WasmKernelFunctionGenerator.cs:3680)
   dedups by `GetValueKey(alloca)` SSA identity. The scan deliberately allocates 1024 vs 256 (WasmGroupExtensions.cs:90-92
   comment) to stop the IR coalescing two distinct workspaces into one aliased alloca. AUDIT: do any two
   distinct shared allocas in the radix presort kernel (scanMemory, scan workspace 1024, scanResults 256,
   broadcast slots) collide on offset, or does fiber-phase re-entry reuse a slot a later phase already
   clobbered? Wholesale offset overlap → ~21% wholesale corruption — fits far better than a read race.
2. **Fiber state-save/restore across the scatter phases.** `pos` (Index1D) is recomputed after barrier
   739 from shared reads; locals `bits/value/inRange/gridIdx/i` computed before the barrier must survive
   a yield. S9 read EmitSaveAllLocals/RestoreAllLocalsTo as symmetric — but re-verify the SPECIFIC
   presort kernel's phase boundaries (the `_dump`'d 018_kernel_6.wasm) don't drop or mis-offset a local
   that feeds `pos`/`view[pos]=value` after a group yield under oversubscription.
3. **kernelId collision is already fixed (monotonic, in TJ's build via 5647870 < ca20808) yet residual
   persisted on 2026-05-29 — so it was NOT the whole story (the memory note predicted this).**

**ARBITER ON THE REAL KERNEL:** the Session-9 detector `CrossGroupScanReuseDetectorTest` splits scan-read
[0] / broadcast [1] / DIRECT-boundary-read [2] into separate atomic counters. Given THIS repro proves the
engine doesn't return stale for any of those read shapes, if counter [2] ever fires on the real kernel under
a contended sweep it means the slot was CLOBBERED (logic), not read-stale (engine) — pointing straight at
suspect #1/#2. Run it in TJ's FO76 2-concurrent sweep.

**Working tree:** repro files added under `_research/` (research artifacts, not library). No library/codegen
change — Rule 4b: the proven racing read was the hypothesis under test, and it is now REFUTED, so there is
nothing to "fix" on the read path. Detectors from S9 remain uncommitted as regression guards.

### SESSION 10 follow-up — scoped PMT under live FO76 + fiber save/restore ASYMMETRY found

**Empirical run (scoped PMT `RadixSortDescendingWithSentinels`, all backends, under TJ's live FO76 100%-core contention):**
`_ilgpudump/playwright-latest.json` —
- **Wasm: PASS but 144,252 ms** (normally seconds). The 144s = workers yielding/parking *heavily* under
  FO76 → the fiber save/restore path was exercised MAXIMALLY and still produced correct output. So the
  residual is rare even under heavy yielding (~1/7), not caught in one run.
- **CPU: "Fail" but durationMs=600,203 = the 10:00 Phase-A cap → it TIMED OUT, starved by FO76 (CPU ILGPU
  spawns threads that never got scheduled). NOT a sort violation.** Confirms the bug is **Wasm-specific**,
  not in the shared radix algorithm. (Generic "Test run failed" w/ no assertion = lost-output/timeout
  signature per PMT CLAUDE.md, not a real failure.)
- WebGPU / WebGPUNoSubgroups / CUDA / OpenCL: PASS. WebGL: skip.

**Conclusion:** chasing the intermittent Wasm residual by looping PMT is low-yield (≤1/7) and fights FO76;
TJ's 2-concurrent FO76 sweep is the reliable repro. Pin the mechanism by reading instead.

**FIBER SAVE/RESTORE COUNT ASYMMETRY (structurally confirmed by reading — the lead non-barrier suspect):**
- `EmitSaveAllLocals` (WasmKernelFunctionGenerator.cs:4156) is emitted **INLINE** at each barrier/yield
  site (call sites 2481, 3077, 3114, 4321) and loops `for i in 0.._locals.Count` — i.e. however many
  locals exist *at that point in the IR walk*.
- Locals are allocated **lazily mid-walk** (e.g. `helperPhaseLocal`/`helperScratchBaseLocal` at 3017/3022).
- The **restore prologue is DEFERRED**: built once at the END with the FINAL `_locals.Count`
  (`EmitRestoreAllLocalsTo(prologueCode)` at 1368) and `InsertRange`'d at each phase entry (1395).
- ⇒ A save at an EARLY barrier writes FEWER slots than a later phase's restore READS. Any local allocated
  after that barrier is restored from a slot the early save never wrote (stale from a prior group's
  later-phase save, or zero-init scratch). `_phaseStateOffset` is fixed so per-local offsets are stable;
  the bug is purely the COUNT mismatch.
- **Live-or-benign?** For radix's specific allocation order it *appears* benign (late locals like `pos`
  are recomputed in-phase; loop-carried `gridIdx` is allocated before all barriers, so every save includes
  it; `helperPhaseLocal` is used only in its own later phase). I could NOT prove it's the active residual
  by reading — but it IS a textbook latent correctness bug: save/restore must be symmetric **by
  construction**, not by accident of allocation order. Under heavy yielding (FO76) a single late-allocated
  loop-carried local read at a phase top would corrupt wholesale — matching the residual's profile.

**FIX IMPLEMENTED (TJ approved 2026-05-29, "implement now"):** `WasmKernelFunctionGenerator.cs` — saves are
now DEFERRED exactly like the restore prologue. `EmitSaveAllLocals()` no longer emits inline; it records the
current `Code.Count` in `_saveAllLocalsInsertPoints`. The actual bytes are produced by new
`EmitSaveAllLocalsTo(target)` (mirror of `EmitRestoreAllLocalsTo`). At end-of-body-generation (after the
final local count is known) a single full-count save block is built and `InsertRange`'d at every recorded
save site; the restore prologue + all save blocks are inserted in ONE pass sorted by DESCENDING byte
position so indices stay valid (inserting back-to-front only shifts higher positions). `_saveAllLocalsInsertPoints`
is `Clear()`ed at the start of each function's state-machine generation (kernel + each helper share the
generator's fields). Net effect: every save writes the SAME 277 locals at the SAME offsets the restore reads
→ symmetric by construction. Wasm structured control flow (br depth, not byte offsets) makes byte insertion
safe — the existing restore-prologue InsertRange already relies on this.

**Why this is a no-regression-by-construction change:** deferred save emits IDENTICAL bytes at the IDENTICAL
byte position as the old inline save, only with the final count instead of the partial count. Stores are
stack-neutral; scratch was already reserved for the full count (`ComputePhaseStateSize`), so writing the
full set uses exactly the reservation (the old inline saves UNDER-wrote it). **Build: green (exit 0).**

**VALIDATION:**
- (1) **no-regression — GREEN** (`PMT_FILTER=Detector`, under live FO76 contention): 14 pass / 0 fail / 2
  WebGL-skip. **`WasmTests.CrossGroupScanReuseDetectorTest` PASS + `WasmTests.GridStrideScanStateDetectorTest`
  PASS** (the latter is purpose-built for loop-carried-local survival across barriers — the exact path this
  fix changes). All 6 backends green. The deferred-save fix did NOT break the fiber path.
- (2) **residual — FIX DID NOT CLOSE IT.** TJ's FO76 2-concurrent sweep (2026-05-29 ~16:00) with the fix
  CONFIRMED in the build (presort kernel grew **50,572 → 74,658 bytes** = the deferred full-count saves) STILL
  failed. So the save/restore asymmetry was a real latent bug but **NOT the residual cause.** Keep the
  hardening; resume the hunt. Failing instance (other concurrent instance passed 916/0/8):
  - `RadixSortDescendingWithSentinels` — **TIMEOUT 241s** (livelock/starvation under contention).
  - `RadixSortRepeatedResort` — **ObjectDisposed CASCADE** (disposed mid-dispatch, downstream of the timeout).
  - `RadixSortDescendingOddCount` — **CATASTROPHIC CORRUPTION**: 1216 viol / 1.44M mismatches (96%), wrong from
    **index 0 / group 0 / localPos 0**, the true-max values (9999976…) DISPLACED out of the top. magnitude >16
    dominant (1.29M). ROOT=gpu.
  - `RadixSortSpawnSceneSimulation` — MILD localized corruption (6 viol, group ~235, ±small, distinct256Groups=6).

### SESSION 10 REDIRECT (post-FO76) — DISPATCH-LIFECYCLE race, NOT kernel-internal

Exonerated so far (visibility/ordering/arrival/layout/save-asymmetry — all by reading + repro + the now-failed
fix). The **ObjectDisposed-mid-dispatch** error + **wholesale corruption from index 0** point to a NEW layer:
**inter-dispatch completion synchronization in `WasmAccelerator.DispatchToWorkers` / `RunKernelAsync`**
(WasmAccelerator.cs ~1506/1863). RadixSort is MANY dispatches (per pass: Kernel1 count+presort → host scan of
counter → Kernel2 scatter). If completion detection is racy under contention — host considers dispatch N done
while a worker is still finishing (e.g. a heavily-yielded worker, or the "all workers posted done" count
mis-fires) — then dispatch N+1 starts writing/reading buffers dispatch N hasn't drained → wholesale
displacement from index 0. The ObjectDisposed cascade is the same lifecycle race surfacing as a teardown bug.
This fits intermittent + contention-only + Wasm-only + wholesale better than any kernel-internal read race
(which the repro proved sound). **NEXT: read `DispatchToWorkers` completion/await + the worker `done`/yield
accounting in the JS worker loop (WasmAccelerator.cs ~2039) for a count/early-complete race.**
The Sentinels TIMEOUT is a SEPARATE liveness issue (group/phase barrier starvation under extreme contention)
that also produces the ObjectDisposed cascade on teardown.

**UPDATE — dispatch-completion + worker→host-visibility BOTH largely EXONERATED:**
- Host completion-await (`DispatchToWorkers` ~1832-1866) drains ALL worker tasks via WhenAny before returning;
  dispatches serialized via `_pendingWork`. Persistent-handler stale-message guard (`if (tcs==null) return;` +
  `CurrentTcs=null` after handling, lines 1545-1547) is correct for one-terminal-post-per-dispatch (the worker
  JS loop posts done exactly once at yieldFlag==0 / on error; yields post nothing).
- Worker→host SAB visibility: there IS a spec-level gap (worker posts `done` with NO release fence — line 2093;
  host `CopyToHost` has NO acquire — WasmMemoryBuffer.cs:305). BUT it does NOT explain the corruption: (a) the
  data is MISPLACED-VALID, not stale-garbage; (b) the `counter` is scanned by a GPU KERNEL on Wasm (scan routes
  to CreateSingleGroupScan), so the host NEVER reads `counter` mid-sort — it's worker→worker (repro-proven
  sound); (c) the host reads only the FINAL output, after `CopyToHostAsync`→`SynchronizeAsync` drains all
  dispatches. So worker→host visibility is not the residual. (postMessage in V8 also acts as a fence in
  practice.) NOT pursuing a fence fix here — no evidence it's the bug.

**HONEST STATE: residual NOT root-caused.** Exonerated by reading/repro/the failed fix: barrier
visibility/ordering/arrival, shared-mem layout, fiber save-asymmetry (fixed, didn't close it), dispatch
completion, worker→host visibility. The corruption is a GENUINE GPU-side compute error in the multi-pass
sort under contention (Kernel1 per-group counts, the scan kernel, or Kernel2 pos), intermittent, misplaced-valid.

**DECISIVE NEXT STEP (uses EXISTING infra): per-pass counter localization.** RadixSortExtensions.cs:308 has a
diagnostic hook "called after each radix sort pass with (bitIdx, counterView); counterView has 4*numGroups
per-group bucket counts from Kernel1." Identifies the FIRST pass whose counter diverges → narrows the bug to a
single pass + Kernel1-counts-vs-scan-vs-scatter.

### SESSION 10 — PER-PASS COUNTER LOCALIZER BUILT + WIRED (uncommitted, build GREEN)

**TJ caught a critical flaw mid-design:** the existing `PerPassHook` calls `stream.Synchronize()` which is a
**NO-OP on Wasm** (single-threaded async; only `await SynchronizeAsync()` is a real barrier, which a sync
`Action` hook can't call). And Wasm `CopyTo` (`WasmMemoryBuffer.cs:356`) is a **synchronous immediate SAB read**
that does NOT serialize through `_pendingWork` — so any host/device copy from the hook races the still-queued
worker dispatches. **The ONLY stream-ordered primitive on Wasm is a KERNEL dispatch** (serializes via
`_pendingWork`). So the existing hook only ever worked on CPU/CUDA/OpenCL, never Wasm.

**Implementation** (`SpawnDev.ILGPU.Demo.Shared/UnitTests/RadixCounterLocalizer.cs` + wired into
`BackendTestBase.RunTest`):
- The hook snapshots each pass's `counterView` with a tiny **copy kernel** (`dst[i]=src[i]`) into a fresh
  per-pass GPU buffer — queues after that pass's Kernel1, before the next pass overwrites it.
- After the sort's own `SynchronizeAsync`, `AnalyzeAsync` reads every snapshot and checks the **reference-free
  invariant**: every pass's bucket-count SUM must be CONSTANT (== element count). First deviating pass ⇒
  Kernel1 miscounted there (localized). All sums equal but sort still wrong ⇒ counts FINE, bug is in
  SCAN or SCATTER (pos), not the count. Negative/absurd entries flag a corrupted shared-mem scan directly.
- **Wasm-gated** (`ShouldInstrument` checks accelerator type contains "Wasm") so it's a NO-OP on
  WebGPU/CUDA/OpenCL (avoids re-entrant mid-sort kernel launches there). `Enabled=true` by default.
- Wired into `RunTest`: installs before every test body, and on ANY failure appends the localizer report to
  the exception message → lands in `latest.json`'s error field that TJ reads. `Uninstall` in finally.
- **Build GREEN** (`dotnet build SpawnDev.ILGPU.Demo.Shared -c Release`, exit 0). NOT yet run under PMT/FO76.

**HOW TO READ THE NEXT SWEEP:** when a Wasm radix test fails, its error in `_tj_dump_local/latest.json` now ends
with `[RadixCounterLocalizer] N pass-snapshots ... ROOT: ...`. Two outcomes:
- `ROOT: FIRST corrupted counter at pass#K` → read that pass's emitted Kernel1 phase code (the count is wrong).
- `ROOT: all per-pass counter sums CONSISTENT → bug is in SCAN or SCATTER` → the count is fine; pivot to the
  scan kernel or Kernel2 `pos` computation.
- **If the residual STOPS reproducing with the localizer enabled** (the extra per-pass copy-kernel dispatches
  add serialization), that is itself a signal: the bug is an INTER-PASS / dispatch-overlap race, not within a
  single pass. (Set `RadixCounterLocalizer.Enabled=false` to A/B this.)

**OPEN (separate from corruption):** the `RadixSortDescendingWithSentinels` 241s TIMEOUT = group/phase-barrier
livelock under extreme FO76 contention; it cascades into the `RepeatedResort` ObjectDisposed. Distinct liveness
bug; may warrant a higher `WasmDispatchWatchdogSeconds` or a starvation fix, but is NOT the corruption.
- **HONESTY:** this fix removes a real latent asymmetry and is no-regression-verified, but whether it CLOSES
  the residual is UNPROVEN until the FO76 sweep runs clean across many trials. If the residual recurs after
  this, the asymmetry was not the (whole) cause and the hunt continues (next: helper-internal state, cross-
  group counter scan). **Mechanism note:** the fix specifically corrects loop-carried late-allocated locals —
  a local set in a later phase of iteration N and read at the top of an earlier phase in iteration N+1 was
  NOT saved by the early-phase barrier (allocated after that barrier's inline save) → restored from stale
  scratch on resume → wholesale corruption, ONLY under yielding (FO76). This matches the residual's profile.

---

## ★★ SESSION 9 (2026-05-29, Tuvok, lead) — binary trace + reads-already-atomic refutation + high-trial detector built

**Inputs:** TJ's 2 concurrent FO76-contended sweeps (2026-05-29 12-14): each **1695 pass / 1 fail**,
complementary large sorts (run1 `RadixSortDescending4M` 2373 viol / 874509 mismatches ~21% under
contention; run2 `RadixSortAscending1_4M` 192 viol). Same residual signature; NOT a regression from
the uncommitted local.13/14/15 working tree (which builds clean).

**NEW grounded findings (emitted-binary level, not source-reading):**
1. **Disassembled the actual failing presort kernel** (`_tj_dump_local/2026-05-29_12-14-20/wasm/018_kernel_6.wasm`,
   helpers=1 barriers=20 47KB — the RadixSort presort). func 26 = phase dispatcher, func 24 = kernel
   phase code, func 25 = scan helper. Traced func 26: **all phase/group barrier fences are present and
   correctly placed in the binary** — release fence before arrival RMW (22041) and before phase-gen
   store (22080); acquire fence post-spin (22130); group release fence before group-gen store (22196),
   group acquire (22241). Happens-before chain complete per the wasm memory model. **RULES OUT a
   missing/misplaced-fence codegen bug** — the prior "correct per spec" verdict now holds at binary level.
2. **Cross-worker shared reads are ALREADY atomic loads** in barrier kernels. `WasmKernelFunctionGenerator.GenerateCode(Load)`
   emits `i32.atomic.load` (and i64/f32/f64 atomic variants) for ALL types when `_hasBarriers`
   (lines 1852-1878; struct + Float16 paths at 1650-1707 / 1576). func 24/25 both show atomic loads
   present (the 295 plain loads are per-thread private scratch/state, not cross-worker). **So
   "make the scan reads atomic" is a NON-FIX — they already are.** This refutes the easy hypothesis.
3. **Sharpened mechanism:** before ca20808 the broadcast already did atomic-store → barrier → SINGLE
   atomic-load. The tag handshake added a SPIN-RETRY atomic-load-until-tag-fresh. The ONLY way that
   helps is if **a single atomic read after a spec-correct seqcst barrier can still return stale on V8**
   (chromium#490434403 family) and retrying eventually sees fresh. UNPROVEN — and note Trip's S8 caveat
   that the handshake's benefit is itself statistically unproven (residual unchanged at ~1.6%).

**Routing confirmed:** RadixSort `GroupExtensions.ExclusiveScan<int,AddInt32>` (RadixSortExtensions.cs:724)
→ `WasmAlgorithmContext` redirect → `WasmGroupExtensions.ExclusiveScan` → InclusiveScanImplementation.
The scatter ALSO reads cross-bucket boundary totals via a DIRECT shared read `scanMemory[groupSize*j-1]`
(RadixSortExtensions.cs:747) — NOT a Group.Broadcast, NOT covered by the tag handshake. Prime suspect
for the "block displacement by k" signature.

**HIGH-TRIAL DETECTOR BUILT** (`BackendTestBase.Tests9.cs` `CrossGroupScanReuseDetectorTest` +
`CrossGroupScanReuseDetectorKernel`; WebGL skip in WebGLTests.cs). Runs the REAL scan + broadcast path
at 16384 groups (4M-scale cross-group reuse) self-verifying per group (v=(g%251)+1, scanned must =
v*(IdxX+1)), splitting **three** read patterns into separate atomic counters:
`[0]` scan cross-worker read, `[1]` Group.Broadcast (tag-hardened), `[2]` DIRECT shared boundary read
(the radix line-747 pattern). Passes silently when clean (safe permanent regression guard).
**Verified clean scoped on all 8 capable backends ×2 (logic correct → any sweep fire is unambiguous).**
Decisive interpretation when it fires in a contended/full sweep:
- `[2]>0, [1]==0` → direct shared boundary read is the residual; broadcast handshake doesn't cover it
  → fix = route radix's line-747 boundary reads through the hardened broadcast (or harden direct reads).
- `[0]>0` → scan internal cross-worker read stale → fix the scan read path.
- `[1]>0` → tag-hardened broadcast STILL races → deeper V8 single-atomic-read staleness.
- all clean in many contended sweeps while sorts still fail → mechanism is elsewhere (redirect).

**IN FLIGHT:** full `PMT_FILTER=WasmTests` sweep (detector included) on Tuvok's machine; TJ's FO76 +
2-concurrent setup is the stronger repro. Tree: detector test added (additive, low-risk); no library
change yet — waiting on detector evidence before touching codegen (Rule 4b: prove the racing read first).

## ★ SESSION 8 UPDATE (2026-05-27, Trip) — Variant C contention fix shipped; residual still ~1.6%/large-sort

**Trip's lane (Variant C JS Atomics.wait+notify shim + always-on yield gate):** unrelated to this
residual; built to fix a DIFFERENT bug (Tuvok-S7 50us-poll dispatcher livelock under
Fallout76-class CPU contention — workersCompleted=0/10 watchdog at 120s). Variant C work lives
in `D:\users\tj\Projects\SpawnDev.ILGPU-Trip\` (copy of Tuvok's tree). Specifically:
- `WorkerPool.cs` — added `env.notify` JS shim closure imported into the WASM dispatcher.
- `Wasm/Backend/WasmBackend.cs` — emit `call $notify(fenceBase+4, int.MaxValue)` after phase-gen
  store and `call $notify(fenceBase+20, int.MaxValue)` after group-gen store. Plumbed
  `enableYieldEscape` through `GeneratePhaseDispatcher`; gated the yield-park / state-save /
  resume paths behind it. Default-on (changed from `WorkerCount > hwConcurrency - 2` heuristic
  after observing the heuristic mis-classifies typical configs and falls into pure-spin
  livelock under external contention).
- `Wasm/WasmAccelerator.cs` — JS-side `Atomics.wait(..., savedGen)` is now `Infinity` (was 0.05s
  poll); `MAX_YIELD_ITERS` tightened 1M → 10K. New `WasmBackendOptions.EnableYieldEscape`
  (nullable bool) override.
- `Wasm/Backend/WasmKernelFunctionGenerator.cs` — `GeneratorArgs.ExtraImportCount` to reserve
  the `env.notify` import slot in function-index space (codegen now offsets kernel/dispatcher
  funcIdx by 1).
- Engine quirk caveat (added to the shim comment): in V8, `Atomics.notify(view, idx, -1)`
  passed through a WASM-import signed-i32 conversion did NOT wake parked waiters in our
  oversub repro. Use `int.MaxValue` (positive wake-all) for the count; the spec-equivalent
  negative form has at least one engine-specific failure mode through the wasm-host boundary.

**Verification (Trip's machine, no contention):**
- `WasmGroupBarrierOversubscriptionTest` (24-worker oversub, forces gate=ON) — PASSES with
  Variant C; previously hung at watchdog under Tuvok-S7 dispatcher.
- `WasmTests.RadixSortDescending4MTest` STANDALONE on healthy machine — PASSES.
- Full WasmTests sweep on healthy machine (Variant C-on default), 2026-05-27: **458 PASS / 1
  FAIL / 4 SKIP / 463 total**, 8m28s wall. Single fail = `RadixSortRepeatedResortTest`
  Frame 2, 48 mismatches in 500K, classic residual signature (diff buckets 0/5/43 across
  |diff|==1 / 2..16 / >16, span 137703..137752, distinct256Groups=2). Rate is consistent with
  Tuvok's prior observation (~12.5%/sweep at base, ~1.6%/large-sort) — **Variant C is benign
  vs the residual: same rate, same signature.**

**Verification (TJ's machine, Fallout76 + 2 concurrent sweeps, 2026-05-27 23:41):**
- Run 1 (`_tj_dump_local`): 1663 PASS / **1 FAIL** / 149 SKIP / 1813 total, RunState=Done,
  ~45 min wall. Failure = `RadixSortDescending1_4MTest` (350K mismatches in 1.4M, 70.7s).
- Run 2 (`_tj_dump_local_2`): 1662 PASS / **2 FAIL** / 149 SKIP / 1813 total, RunState=Done,
  ~45 min wall. Failures = `RadixSortDescendingOddCountTest` (204K mismatches in 1.5M, 74s)
  and `RadixSortDescending4MTest` (4076623 mismatches in 4M — 96% wrong, 234s).
- **The 4M descending under Fallout76 + 2 concurrent sweeps is the WORST observation of this
  residual ever recorded (96% mismatches with diff>16 dominating: 4017847 entries).** Compare
  with Tuvok-S6 4M observation: 68493 mismatches (1.6%). Heavy CPU contention does NOT cause
  a different bug — same signature class — but it **amplifies the per-pass corruption
  magnitude enormously**. Mechanism guess (unverified): under contention, multiple radix passes
  may each tip the race, cascading.

**Conclusion: Trip's Variant C work is unblocked + ships the contention fix (no more
0/10-workersCompleted watchdog hangs). The residual is unchanged and unfixed.** The
"4 prior sessions" bug list below is still open. Note that the diary's prior conclusion
"only fires in full-sweep accumulation, not in any scoped repro" is reinforced here: Trip's
single-test `RadixSortDescending4MTest` on healthy machine passed, but the full sweep fires
the residual on an unrelated test (RepeatedResort Frame2). The pattern of "ANY 1 of 8 large
sorts per sweep, ~12.5% rate" still holds.

**Trip's audit on the broadcast/scan codegen path (per Tuvok's Session 6 strongest lead):**
- `WasmKernelFunctionGenerator.GenerateCode(Broadcast)` (:3588-3733): atomic store + barrier
  + atomic load + barrier pattern. Atomic ops are SeqCst on a per-call unique slot offset
  (`broadcastSlotOffset = _sharedMemorySize; _sharedMemorySize += (slotSize+3) & ~3`).
  Two broadcasts in the same kernel get DISTINCT slots. Per-spec correct.
- `ILGroupExtensions.InclusiveScanImplementation` (:134-163): `sharedMemory[LinearIndex] =
  value; Group.Barrier(); if (IsFirstThread) serial-scan; Group.Barrier();`. The first-thread
  serial scan reads other threads' writes — visibility comes from the wasm dispatcher's
  phase barrier (release fence before gen bump, acquire fence after spin exit). Per-spec
  correct.
- Wasm dispatcher's phase + group barriers (`WasmBackend.GeneratePhaseDispatcher`): release
  fence at producer-side gen bump (lines 1080-1099 / 1416), acquire fence at waiter-side
  post-spin (line 1280 / 1539). Per-spec correct release-acquire pair.
- Cross-group shared-memory zeroing: worker0-only loop (lines 1305-1339) covering
  `[sharedMemBase .. fenceSlot)` (= sharedMemSize + barrierSize bytes). Sequenced
  before worker0's group-barrier arrival, published to other workers via release fence
  → gen bump → acquire fence. Per-spec correct.
- **Trip could not find a logic race by reading either** — matches Tuvok-S6's finding. The
  bug is either an emitted-binary issue (codegen vs source-read divergence) or a genuine
  V8-engine memory-ordering hole that fires under full-sweep state accumulation. Tuvok's
  next-step list still stands: (1) disassemble emitted WASM of a captured failing kernel,
  (2) in-dispatcher stale-read detector, (3) minimal V8 multi-worker seqcst repro.

**Open question for next session:** is there a way to capture WHICH SHARED-MEMORY SLOT shows
the stale read in a failing sweep? An in-kernel detector that compares the broadcast load
against a known-correct value would localize the slip — but adding it without changing
timing enough to mask the bug is the trick.

---

## ✅ FIX LANDED (2026-05-26, Tuvok) — monotonic kernelId; GetHashCode removed

The `kernelId` fix below is DONE. `WasmAccelerator` no longer derives the worker module-cache id from
`RuntimeHelpers.GetHashCode(wasmBytes)`; it uses a process-unique `Interlocked.Increment(ref _nextKernelId)`
carried on a per-kernel `KernelCacheEntry { int KernelId; HashSet<Worker> Workers; }`. Build green, 0 errors.

**Framing (per TJ, and honest):** this is a correct fix REGARDLESS of the residual race — using a
non-unique, GC-recycling identity hash as a persistent cache key is a textbook bug (`GetHashCode` does not
guarantee uniqueness). We did NOT run an expensive collision-repro campaign to "watch it fail" first —
the correctness argument stands on its own, and clean-sweep streaks at ~12.5%/sweep are statistically weak
anyway (see [[feedback-probabilistic-bug-need-enough-trials]]). It IS a strong, mechanism-matched candidate
for the residual corruption (full-sweep-only / multi-kernel-churn / load-correlated all fit), but is NOT
proven to be the sole cause. **Watch future natural sweeps:** if the heavy-dup ±1 residual recurs after
this, the kernelId collision wasn't the whole story and the V8-pure-spin / fiber-state-save leads below
are back in play.

Docs updated: `Wasm/CLAUDE.md` (kernelId bullet in "Barrier Dispatch" + new "kernelId MUST be a monotonic
unique id" section). Audit tracked: `Plans/gethashcode-as-id-audit-2026-05-26.md` (found + FIXED a SECOND
instance — `WebGLAccelerator.cs:544` programId from `GLSLSource.GetHashCode()`, same bug class →
now `WebGLCompiledKernel.ProgramId` monotonic id).
The diag flags (`ForceGrowEachDispatch`, `PreGrowPages`) are RETAINED default-off (TJ call 2026-05-26):
the grow/SAB-resize hypothesis is disfavored but NOT definitively killed, so the tooling stays ready to
re-test grow if the residual recurs after this fix — rather than rebuild it later.

---

## ★★★★★ ORIGINAL LEAD + FIX PLAN (2026-05-26, TJ-confirmed) — now implemented above

**LEAD (architect-confirmed, strong): the worker module-cache `kernelId` is derived from
`RuntimeHelpers.GetHashCode(wasmBytes)` (WasmAccelerator.cs ~line 1506) — an IDENTITY hash that
RECYCLES under Mono/Wasm GC.** When a dead kernel's `byte[]` is collected, its identity-hash slot
frees and the NEXT kernel allocation can get the SAME value. The worker caches modules by this id
(`_modulesById[kid]`, WorkerPool.cs ~104-128); a recycled/colliding id → a worker runs the WRONG
cached module → corruption. This is FULL-SWEEP-SPECIFIC by nature (needs many kernels + GC churn to
recycle a slot) — exactly the residual's trigger profile, and why NO scoped repro ever worked.
WHY ZERO-GROW SWEEPS WERE CLEAN (not luck): `_initializedWorkersByKernel` (Dict<byte[],…>,
WasmAccelerator.cs:104) holds a STRONG ref to every kernel's bytes → they're GC-eligible only after
`.Clear()`, which fires ON `memory.grow` (lines ~856/873/893/906). Natural sweep: grow→clear→GC→
hash recycles→collision→corrupt. Zero-grow (PreGrowPages): no clear→bytes pinned→no recycle→CLEAN.
Coheres with ALL evidence (full-sweep-only, fence-reducible=timing, force-grow-single-kernel clean).

**FIX (do this FIRST — correct regardless, it's the kill shot per TJ+Gemini):** replace the
GetHashCode kernelId with a STABLE MONOTONIC unique id. Cleanest: change
`_initializedWorkersByKernel` value from `HashSet<Worker>` to a small class
`{ int KernelId; HashSet<Worker> Workers; }`; in the TryGetValue block (~WasmAccelerator.cs:1499)
assign `KernelId = Interlocked.Increment(ref _nextKernelId)` (new `static int _nextKernelId`) on
entry creation; set `int kernelId = info.KernelId;` (DELETE the GetHashCode line ~1506); update the
two `kernelInitSet.Add(worker)` sites (~1551, ~1598) to `info.Workers.Add(worker)`. Unique ids can't
collide; per-kernel id is stable between clears (worker cache still hits); after a clear a kernel
gets a fresh id + re-sends bytes = SAME re-send behavior as today (no perf change). Old
`_modulesById[oldId]` entries linger harmlessly (bounded by session kernel count).

**VERIFY:** run NATURAL sweeps (PreGrowPages OFF / 0 — grows MUST happen to exercise the trigger):
`PMT_FILTER=WasmTests`. Was ~12.5%/sweep; fix → expect 0. Roll several (stats: need a few clean to
be confident, but fix is collision-PROOF by construction + architect-confirmed mechanism = strong).
OPTIONAL proof the old scheme collided: keep a detector computing the OLD GetHashCode id into a
`Dictionary<int,(WeakReference<byte[]>,int len)>`; on same-id-but-dead-or-different-bytes → log/throw
"KERNELID COLLISION". One natural sweep firing it = mechanism PROVEN. (I was mid-implementing this.)

**TREE STATE (uncommitted, 2026-05-26):** WasmBackend.cs = +2 default-off diag flags
(ForceGrowEachDispatch, PreGrowPages); WasmAccelerator.cs = their wiring (force-grow bump ~826, pre-grow
in both init branches); BackendTestBase.cs = enhanced VerifyDescendingSort diag (KEEP); WasmTests.cs =
BACK TO MASTER (temp tests removed); Tests9.cs = master. Group-barrier band-aid 7bfc364 in master =
KEEP. Build is green (0 errors). Decide whether to keep the diag flags after the fix lands.

**ALSO FOUND (separate robustness bug, Rule 1) — FIXED 2026-05-26:** the Wasm GROUP barrier had NO
anti-starvation yield escape (the PHASE barrier does) → worker oversubscription (2× cores) HANGS/livelocks
(confirmed, 2 PMT timeouts). FIXED in `GeneratePhaseDispatcher`: the group waiter now has the same
spin-count + threshold + save-state + return escape (distinct `yieldFlag=2`), with a GROUP-RESUME prologue
path (skips phase loop + re-arrival, restores group savedGen) and the JS park waits on the group gen slot.
VERIFIED: an oversubscribed (≥3× cores) multi-group kernel that previously timed out now COMPLETES in ~4s
correct; full WasmTests sweep green. Documented in Wasm/CLAUDE.md.

---


## ★★★★ SESSION 3 (2026-05-26 PM, Tuvok) — SharedArrayBuffer-growth-lag hypothesis (TJ+Gemini)

**HYPOTHESIS (TJ brainstormed w/ Gemini):** the residual corruption is `WebAssembly.Memory.grow`
propagation lag — host grows the shared memory + immediately dispatches; a lagging worker
(re-instantiating against the new buffer) reads stale scan/broadcast data near the grown
boundary. Fits the evidence shape: warm-clean (no grow), scoped-clean (1 grow then stable),
sweep-corrupts (different-sized kernels force grows), churn-correlated, fence-reducible. Notes:
`wasm-sharedarraybuffer-growth.md`, `wasm-sharedarraybuffer-growth-research.md`.

**CODE READ (grounded, not guessed):** grow path = WasmAccelerator.cs ~859-875 (grow → dispose
old buffer ref → re-get `.buffer` → `_initializedWorkersByKernel.Clear()`, all main-thread,
workers idle via `_pendingWork`). Worker side = WorkerPool.cs 104-128: dispatch re-sends
`wasmBytes` (forced by the init-clear → `firstTimeOnWorker`) → recompile + null instance; PLUS
`_lastMemoryBuffer !== d.memory.buffer` → clear all instances → re-instantiate against grown
`d.memory`. Two redundant re-instantiation triggers. `memory` is sent EVERY dispatch; copy-back
(1704-1753) reads via the post-grow `memoryBuffer` after `Task.WhenAll`. **Path is logically
correct on inspection** — any bug would be engine-level cross-agent grow-visibility.
KEY FACT: the accelerator (1 `WebAssembly.Memory`) is CACHED + reused across ALL tests in a class
(BackendTestBase.cs:44,55-64), invalidated only on a test failure → memory grows monotonically
across the sweep, so cross-test grow accumulation is real & testable.

**TOOLS ADDED (library, default-off, REMOVE after root-cause):**
- `WasmBackend.ForceGrowEachDispatch` (bool) — forces a real 1-page grow + re-instantiation on
  EVERY dispatch (WasmAccelerator.cs ~826 bumps wasmPages). AMPLIFY test.
- `WasmBackend.PreGrowPages` (int) — pre-reserves N initial pages so the grow branch is
  UNREACHABLE (WasmAccelerator.cs both init branches). REMOVE test.
- `WasmTests.CreateAcceleratorAsync` sets `PreGrowPages=8192` (TEMP) — 512MiB, 7× the 4M sort's
  ~1120-page max, so wasmPages<=8192 always → zero grows the whole sweep.

**FINDING #1 (force-grow AMPLIFY, scoped):** scoped harness looping ScanBroadcastIsolationKernel
(the localized failure point) WITH ForceGrowEachDispatch=true → **0 failures / 750 trials**
(`RESULT[forceGrow=True]: failedTrials=0/750`). P(0/750|1%)≈5e-4. The STRONG form of the
growth-lag hypothesis (grow immediately-before scan/broadcast → stale read) is **DISCONFIRMED**.
CAVEAT: this kernel only ever failed ONCE, in a full sweep — it may be immune scoped regardless,
so this doesn't fully kill grow's role under sweep accumulation. → motivated the REMOVE test.

**FINDING #2 (PreGrow REMOVE, full sweep):** `PMT_FILTER=WasmTests` with PreGrowPages=8192
(zero grows guaranteed). Sweeps #1, #2 = **458/0/4 CLEAN** (8m20-25s each). **STATS CAVEAT (do
NOT over-read):** at ~12.5%/sweep base rate, P(clean | grow irrelevant) ≈ 0.875, so a clean
zero-grow sweep is the EXPECTED outcome even if grow has nothing to do with it. 2 clean = P 0.77
= weak. The DECISIVE outcome is a FAILURE under zero-grow (= instant kill of grow); clean streaks
are nearly uninformative and the "implicate grow" direction needs ~20+ clean (slow + weak — the
[[feedback-probabilistic-bug-need-enough-trials]] trap). So: roll zero-grow sweeps stop-on-failure
for the fast kill, but DON'T burn many chasing the weak direction. [sweep #3 in flight.]

**PIVOT (the better, FASTER test): WORKER OVERSUBSCRIPTION scoped repro.** Leading hypothesis =
pure-spin seqcst cross-worker visibility failure under SCHEDULING PRESSURE (only fires in full
sweeps because sustained load deschedules the pool). `WasmBackendOptions.WorkerCount` is
configurable (WasmAccelerator.cs:252; barrier dispatch uses Min(_workerCount, groupSize=256)), and
the WorkerCount doc (WasmBackend.cs:1387-94) ALREADY notes oversubscription deschedules workers
past the spin threshold. So set WorkerCount = 3x hardwareConcurrency → workers contend/deschedule
while spinning, WITHOUT external main-thread starvation (the confound that HUNG repro #4 — there
external load starved the Blazor main thread draining worker 'done' msgs; here only workers
oversubscribe). Added TEMP `WasmOversubscribedSortRaceHarnessTest` (WasmTests.cs, creates its OWN
36-worker zero-grow accelerator, loops 500K heavy-dup sentinel sort, counts corruptions, ~24s
budget, throws RESULT). If it reproduces where normal-worker scoped is clean → FAST repro +
hypothesis confirmed + grow-independent (it's zero-grow). If clean → scheduling pressure alone
(scoped) isn't the trigger. Run SCOPED: PMT_FILTER=WasmTests.WasmOversubscribedSortRaceHarness.
**REMOVE this test before commit (it throws RESULT always + spins 36 workers → contaminates sweeps).**

**FINDING #3 (oversubscription = DEAD END for corruption repro): worker oversubscription HANGS.**
Ran the harness at 2× cores (zero-grow). TIMED OUT (PMT 30s kill, no RESULT) TWICE — even with the
budget clock started before accelerator creation + 15s budget + n=250K. So a single oversubscribed
sort dispatch HUNG; the 15s budget check never fired. CONFIRMED (Rule 4b, 2 runs): **worker
oversubscription → group-barrier LIVELOCK** (the group barrier has NO yield escape — a descheduled
worker stalls the whole group while others spin forever; matches repro #3/#4). So oversubscription
CANNOT be used to repro the corruption (it hangs first). **Separate real robustness bug worth
fixing independently: the Wasm GROUP barrier has no anti-starvation yield escape (the PHASE barrier
does), so SpawnScene under heavy CPU oversubscription/multitasking can hang.** Temp test + the
WasmTests PreGrowPages=8192 line BOTH REMOVED (suite back to natural master). Library flags
(ForceGrowEachDispatch, PreGrowPages) KEPT — default-off, harmless, useful for future runs.

## ★★★★ SESSION 3 VERDICT (2026-05-26 PM) — grow strongly disfavored; bug resists ALL scoped repro
**GROW HYPOTHESIS: strongly disfavored (not 100% killed).** Force-grow amplify = 0/750 on the
localized kernel (strong); zero-grow remove = 3 clean full sweeps (weak per stats, P=0.67); grow
path logically correct on inspection; both producer fences verified present. The corruption is
NOT grow-driven by the weight of evidence.
**The bug resists EVERY scoped reproduction lever tried across all sessions:** warm loops (clean),
force-grow scoped (0/750), zero-grow sweeps (clean), worker oversubscription (HANGS), yields (ruled
out prior, never-yield also corrupts). **It ONLY reproduces in the full natural 462-test sweep
(~12.5%/sweep).** This points hard at FULL-SWEEP ACCUMULATION as the trigger — diverse kernel
compiles/JIT/deopt, GC across 460 tests, cumulative browser/worker state — NOT any single isolable
factor. **NEXT (tractable, evidence-based, not yet exhausted):**
1. **Disassemble the ACTUAL emitted Wasm of a captured failing heavy-dup kernel from `_dump`** and
   check the phase-split / barrier-count / state-save structure against the protocol — the prior
   "protocol proven correct" was from reading SOURCE, not the emitted binary for the failing shape.
2. In-dispatcher **stale-read detector** to catch the racing read during a natural sweep.
3. Minimal **Chrome** V8 multi-worker seqcst-spin repro (prior Node V8 12.4 attempts failed).
4. Fix the **group-barrier no-yield-escape livelock** (Finding #3) independently — real robustness bug.

**PMT GOTCHA (verified):** PMT enforces a **HARD 30s per-test timeout** (`ProjectTest.cs:85`
`WaitForSelectorAsync Timeout=30000`); the `[TestMethod(Timeout=)]` attribute is NOT honored.
Any diagnostic harness must finish < 30s. (Corrects the old "~240s cap" note in this file.)

**TREE STATE (session 3, all UNCOMMITTED):** WasmBackend.cs (+2 flags), WasmAccelerator.cs
(force-grow + pre-grow wiring), WasmTests.cs (PreGrowPages=8192 TEMP), BackendTestBase.cs
(enhanced diag from session 2, KEEP). Tests9.cs back to master (harness added then removed).
Group-barrier band-aid `7bfc364` still in master, still KEEP. **REMOVE all 3 diag flags + the
WasmTests PreGrowPages line before any commit.**

---


## ★★★ FRESH-SESSION RESUME STATE (2026-05-26, ~90% quota) — READ THIS FIRST
**SOLID (proven):**
- Bug is REAL: large multi-group sorts corrupt in full WasmTests sweeps ~12.5%/sweep (block
  displacement, widespread, ±1-to-large, gpu-below-cpu). Signatures detailed below.
- **Yields / resume / JS `Atomics.wait` are NOT the cause** — never-yield (threshold 2e9) ALSO
  corrupts (NY2ROLL3 failed). Don't re-chase the yield path. The setTimeout fix was WRONG, reverted.
- **Localized to the cross-worker BARRIER SYNC around the GROUP SCAN + BROADCAST** — the dedicated
  `ScanBroadcastIsolationTest` failed (never-yield) with "16/16 wrong left boundaries, bug in pass 2
  (group scan + broadcast)", garbage values (stale reads). Also hits RadixSort's scatter (scanMemory
  offset). The phase-barrier protocol is provably correct PER SPEC (engine-ordering OR a subtle gap).
- **Trigger = FULL-SWEEP CONTEXT (cross-test accumulation), NOT within-test churn**: scoped scan
  harness 0/300, warm sorts clean. Needs the 462-test session state (grown memory / cross-kernel /
  GC / worker state).
**STATS DISCIPLINE (I made this mistake twice — see [[feedback-probabilistic-bug-need-enough-trials]]):**
per-large-sort rate ≈1.6%; clean streaks (warm×60, never-yield×5, fix×6) are ALL inconclusive. Only
FAILURES are solid. Need within-run failure-COUNTING with enough n; don't trust short clean streaks.
**CURRENT TREE STATE (uncommitted):**
- `WasmBackend.cs` = MASTER (threshold 1M, band-aid 7bfc364 present — KEEP it). No fix landed.
- `BackendTestBase.cs` = enhanced `VerifyDescendingSortOnGpu` diagnostic (localPos hist + diff buckets
  + span) — a genuine improvement, KEEP.
- `Tests9.cs` = `RadixSortHighTrialCountTest` repurposed into a TEMP "mini-sweep" harness (1.4M sort
  churn + scan/broadcast detector, counts failures). **It's in the base class so it RUNS + THROWS in
  every full sweep — REMOVE/gate before committing anything.** Original RadixSort tests already reverted.
- Notes (this file) untracked. Nothing committed this session.
**MINI-SWEEP RESULT (resolved):** sort-churn + scan detector, 25 trials = **scanFail=0/25** (~15s/
trial; 1.4M sort w/ fresh-buffer alloc is slow). INCONCLUSIVE (P(0/25|4%)=0.36) but suggests
sort-churn + grown-memory alone does NOT cheaply reproduce → **the trigger needs the FULL 462-test
session accumulation** (diverse kernels / GC / browser memory pressure / cumulative worker state),
which doesn't recreate in a small harness. Scoped scan 0/300 + mini-sweep 0/25 both point the same way.
**SO: the only reliable repro is the full WasmTests sweep (~12.5%/sweep). The within-run harness
approach hit a wall — the trigger isn't packable into a few kernels.**
**NEXT STEPS (fresh session):**
(1) **Confirm WHICH op fails most under full sweeps** — run a few full `PMT_FILTER=WasmTests` sweeps
    and tally which tests fail (RadixSort 1.4M/2M/4M vs ScanBroadcastIsolation vs AllReduce vs DualScan).
    The scan-isolation tests (Tests9.cs) are the most DIAGNOSTIC failures — if ScanBroadcastIsolation /
    GroupBroadcastDiag fail reliably in sweeps, that's the tightest pointer.
(2) **Audit the phase-barrier publish of the broadcast slot + scan sharedMemory** (WasmKernelFunctionGenerator
    Broadcast codegen :3588-3733 — store→barrier→load→barrier; group scan ILGroupExtensions:134-163
    first-thread-serial). Look for a context-dependent sync gap that only bites under full-session memory state.
(3) **Try a within-run harness that runs MANY DIFFERENT kernels** (sort + scan + reduce + broadcast +
    different sizes) per trial to better mimic the 462-test diversity — needs to be FAST (avoid the
    1.4M-sort 15s/trial cost; use smaller multi-group sizes). Count failures over 100s of trials.
(4) Consider: instrument the dispatcher to LOG when a worker observes a gen advance but reads a value
    that doesn't match (a stale-read detector) — would directly catch the racing read in the act.
(5) **CLEAN UP: remove `RadixSortHighTrialCountTest` from Tests9.cs before any commit** (it throws in
    full sweeps). Keep the BackendTestBase.cs diagnostic. Decide band-aid disposition (KEEP — it helps).

---



Continuation of the pure-spin corruption hunt. Symptom: intermittent sort-order
violations on large multi-group `RadixSort`, heavy-duplicate keys show as **±1
adjacent-bucket errors** (`gpu[i] - cpu[i] == ±1`), load/yield-correlated. Canary:
`WasmTests.RadixSortDescendingWithSentinelsTest` (n=1,393,167, ~30% int.MinValue
sentinels, ~70% in [0,65534] → ~15 dups/key).

## VERIFIED by reading (not guesses)

### 1. The group-barrier protocol is correct WITHOUT any fence — proven happens-before
`GeneratePhaseDispatcher` (WasmBackend.cs). Group barrier: `fenceBase+16`=arrival
(seqcst RMW-add), `fenceBase+20`=group gen (seqcst store/load). For worker `w_k`
(not last), its group-g data writes reach the waiters via this chain, all seqcst,
no fence needed:

```
w_k data-write →(sequenced-before) w_k arrival RMW (offset 16)
  →(synchronizes-with, RMW reads-from chain on the SAME location) last-worker arrival RMW
  →(sequenced-before) last-worker group-gen store (offset 20)
  →(synchronizes-with, same location) waiter group-gen load
  →(sequenced-before) waiter's group-(g+1) reads
```

Under the wasm model (`sequenced-before ⊆ happens-before`; seqcst W synchronizes-with
seqcst R that reads-from it on equal byte range; RMWs form an unbroken reads-from
chain on their location) this edge is COMPLETE. So the band-aid release fence added
in `7bfc364` (before the group-gen store) is **semantically a no-op** — it only
perturbs V8 scheduling, narrowing the race window. Its observed 50%→14% / mag 427→9
effect is a TIMING artifact, not a correctness fix. See
[[feedback-wasm-atomics-fences-are-noops-find-logic-race]] + `Research/01-wasm-memory-model-and-atomics.md`.
**The band-aid is disabled in the working tree during this hunt (commented at ~WasmBackend.cs:1236).**

### 2. `_needsSyncYields = helperCallCount >= 1` is CORRECT; the inline comments are STALE
`WasmKernelFunctionGenerator.cs`. In phase mode the explicit post-helper barrier is
skipped (line ~2878) and replaced by a "sync yield" (an extra phase boundary = an extra
cross-worker phase barrier) emitted after the helper's final phase, gated by
`_needsSyncYields`. The authoritative **Post-Helper Barrier Rule** (Wasm/CLAUDE.md) says
a sync is required after EVERY barrier-bearing helper call (so the cross-worker barrier
fires before the scan results are consumed) — which is exactly `>= 1`. The comments at
~1078-1083, ~2806-2808, ~2882-2885 saying "only 2+ calls need it / single-helper excludes
it" CONTRADICT both the code and the rule; they are stale and should be corrected (the
dead branch at 1082-1083 never executes because `_needsSyncYields` is true when
helperCallCount==1). This was my handoff's "prime suspect" — reading shows it is NOT the
bug. (Comments need a cleanup pass regardless.)

### 3. In-kernel `EmitBarrier` spin is fully bypassed in phase mode
`WasmKernelFunctionGenerator.cs:~3890`. Phase-mode barriers just save all locals + a
STATIC continuation-block index and return; the dispatcher owns cross-worker sync.
Barrier-count divergence across tids is handled per-tid (each resumes from its own saved
`_stateLocal`). A barrier inside data-dependent control flow WOULD desync, but RadixSort
scan/histogram barriers are at fixed log-step iterations (uniform) — weak hypothesis.

## Kernel topology (from _dump 2026-05-25_22-53-05)
Barrier-bearing kernels: `kernel_6` (helpers=1, barriers=20, 47KB — the big presort),
`kernel_8` (**helpers=2**, barriers=14), `kernel_7` (h=1,b=6), `kernel_4` (h=1,b=8).
Many tiny 0-barrier scatter/scan-level kernels (kernel_9..40+). ng=12, gs=256, wc=12.

## EMPIRICAL: tight single-sort loop does NOT reproduce (2026-05-25)
Repro #2: `RadixSortDescendingWithSentinelsTest` body replaced with a tight loop of the
SAME 1.39M DescendingInt32 sort + GPU verify, throw-on-first, **band-aid fence DISABLED**,
~60 iters before 240s timeout. Result: **NO corruption** (timed out clean, never threw).
~60 sorts × ~20 barriers × hundreds of grid-stride re-executions = millions of barrier
crossings with zero violations. **This RULES OUT a simple per-barrier memory-ordering race**
— if the barrier itself raced on every crossing, the tight loop would have caught it. The
bug needs a condition the warm tight loop lacks. Two candidates:
1. **Dispatcher spin-yield/resume path** — in a tight warm loop the 12 workers stay
   scheduled, the phase-barrier spin exits before the 1,000,000-iter YIELD_SPIN_THRESHOLD,
   so the yield→JS→`Atomics.wait`→`resumeMode=1` resume path NEVER executes. Full-sweep
   scheduling churn forces yields → "load/yield-correlated" signature. **TESTING NOW (repro #3):
   dropped YIELD_SPIN_THRESHOLD 1_000_000→200 to force yields on nearly every barrier wait,
   REPRO_ITERS=8, test Timeout=600000.** If this corrupts → bug is in the resume path.
2. Cross-test accumulated state (full WasmTests sweep) — Plan C if forced-yield is clean:
   revert the loop/threshold, run the natural full sweep (band-aid off → ~50% dominant-mode
   rate), capture whichever large sort trips.

Resume path traced microscopically (WasmBackend.cs:725-1147 + WasmAccelerator.cs:1805-1845
JS loop): a parked worker can lag AT MOST one phase (the next phase barrier blocks on it),
so arrival-counter misattribution across phases is impossible; saved/restored state
(g, phase, savedGen) looks complete; JS `Atomics.wait` is on the phase gen (fenceSlot+4),
correct since only phase barriers yield. No resume bug found by READING — repro #3 decides.

## EMPIRICAL: forced-yield (threshold=200) HANGS, does not corrupt (repro #3)
Dropped YIELD_SPIN_THRESHOLD 1M→200, REPRO_ITERS=8, Timeout=600s. Result: **watchdog
hang** at 120s — `Kernel_RadixSortKernel1 disp=3 workersCompleted=0/10 items=3072
workerCount=10`. NOT corruption. threshold=200 forces a yield after ~1us of spinning,
so workers yield even on legitimately-short barrier waits → constant yield/park(50us)/
re-dispatch thrash → livelock (or watchdog-too-aggressive). The group barrier has no
yield escape, compounding it. **Interpretation:** extreme yielding produces a HANG, not
±1 corruption — if corruption scaled with yield frequency it would have appeared before
120s. So the trigger is NOT merely "yields happen"; it's more specific (a particular
skew/timing window, or kernel-shape/recompilation-dependent). Yield-correlation hypothesis
is WEAKENED but not dead. Both experiment edits REVERTED (threshold back to 1M, sentinel
test restored via git checkout). Band-aid stays OFF.

## PLAN C IN FLIGHT: natural full WasmTests sweep (band-aid OFF, threshold 1M)
The faithful repro = how it was originally observed. Full sweep's kernel diversity /
recompilation / GC / memory pressure creates the real OS-descheduling that drives
occasional yields at the real threshold. Band-aid OFF → ~50% dominant-mode rate. Capture
whichever large sort trips + its verify diag (ROOT displacements w/ group~i/256, localPos).
Re-run if a given run comes up clean (~50%).

## EMPIRICAL: oversubscription (real CPU load) HANGS at disp=3, does not corrupt (repro #4)
6 busy threads (ProcessorCount=12, so ProcessorCount/2) via `_scratch/cpuload.cs` +
scoped sentinel loop. Result: IDENTICAL to forced-yield — **watchdog hang at 120s,
`Kernel_RadixSortKernel1 disp=3 workersCompleted=0/10 workerCount=10`**. NOT corruption.
- Notable: it consistently hangs at disp=3 (a RadixSortKernel1), NOT disp=0. If it were
  generic group-barrier starvation it'd hang at the first group barrier. So either disp0-2
  lack the triggering property, or state accumulates. `_dispatchCount` is a GLOBAL static
  (WasmAccelerator.cs:338/351), so disp=3 = 3rd kernel dispatch of the first sort.
- **CONFOUND (unresolved):** my external CPU load may starve the Blazor MAIN thread that
  processes worker "done" postMessages → false workersCompleted=0/10 watchdog hang rather
  than a real dispatcher deadlock. So the "hang" is NOT confirmed to be the real bug; it may
  be an artifact of too-aggressive repro. (threshold=200 hang is likewise a yield-thrash
  livelock artifact.) Distinguishing needs a worker-progress diagnostic. Both repro #3 and #4
  hung at the same disp=3 — suspicious enough to investigate, but treat as unconfirmed.

## LOGIC ANALYSIS EXHAUSTED — protocol is provably correct; smells like a hardware-ordering issue
Traced the ENTIRE path: dispatcher (WasmBackend.cs:653-1338, phase + group barriers + spin-
yield + JS resume loop WasmAccelerator.cs:1805-1845), RadixSortKernel1 (the in-place presort
w/ scanMemory + 4 Group.Barrier + the ExclusiveScan helper), RadixSortKernel2 (barrier-free
global scatter), ILGroupExtensions.InclusiveScanImplementation (write→barrier→first-thread
serial scan→barrier). EVERY cross-worker publication has a complete seqcst happens-before
chain (data-write →sb→ arrival-RMW →sw(RMW chain, same loc)→ last-worker gen-store →sw→
waiter gen-load →sb→ read). The resume path can lag AT MOST one phase (next barrier blocks
the leader); saved/restored state is complete; JS Atomics.wait is on the correct (phase) gen.
**I could not find a logic race by reading.** Yet: (a) corruption is yield/scheduling-
correlated (warm loop never corrupts; isolated-sweep churn ~14%); (b) the band-aid HARDWARE
fence demonstrably reduces it. (a)+(b) are the classic signature of a HARDWARE memory-ordering
issue (V8 wasm seqcst atomics under scheduling pressure — chromium#490434403 *family*), masked
by a real fence — NOT a logic bug. Caveat per [[feedback-wasm-atomics-fences-are-noops-find-logic-race]]:
fences are no-ops PER SPEC, and prior pure-Node V8 could not repro, so "V8 bug" is still
UNPROVEN. Do not conclude it without a minimal repro.

## REPRO STATUS (this session, 7 PMT runs)
- Tight warm loop (band-aid off, 60 sorts): CLEAN (0 yields → 0 corruption).
- Forced-yield threshold=200: HANG (livelock artifact).
- Isolated full sweep, band-aid OFF, ×3: CLEAN, CLEAN, [#3 in flight]. (rate ≈14%, not 50%.)
- Oversubscription (6 threads) + scoped loop: HANG at disp=3 (confound w/ main-thread starvation).
- **The ONLY clean repro is the isolated full sweep at ~14%.** Catching the ±1 signature needs
  patience (roll ~7 sweeps) OR a cleaner forced-yield mechanism that doesn't hang/confound.

## CANDIDATE NEXT MOVES (next session)
1. **Roll isolated full sweeps** (quota-cheap background) until one trips; read the verify
   diag's ROOT displacements (group~i/256, localPos, diff) → localize the failing pass/group.
2. **Add a worker-progress diagnostic** to disambiguate the disp=3 hang (real deadlock vs
   main-thread-starvation confound). If REAL, the group barrier's no-yield-escape starvation
   is a genuine bug worth fixing (SpawnScene under multitasking) AND would unblock
   oversubscription-based corruption repro.
3. **Minimal V8 seqcst-ordering repro** (pure wasm, no Mono) to confirm/deny the hardware-
   ordering hypothesis. If confirmed, the band-aid fence is the *correct* mitigation (not a
   band-aid) and should STAY — re-frame the docs.
4. Decide band-aid disposition based on (3): if hardware-ordering is real, KEEP the fence
   (it's a legitimate workaround, like the group-barrier fence) rather than reverting.

## WHEN IT FIRES — interpretation guide (TJ chose: enhance diag + roll, 2026-05-26)
Enhanced `VerifyDescendingSortOnGpu` (BackendTestBase.cs) now reports, on orderViolations>0:
total mismatches + diff buckets (|diff|==1 / 2..16 / >16), span (first/last/distinct256Groups),
top-2 localPos(i%256) buckets, plus ROOT/ANY/ORDER samples. Rolling natural sweeps (loop
b61zd76k9, up to 8, stop-on-first-failure) at master config (band-aid ON, threshold 1M — lower
thresholds livelock large sorts and MASK the corruption with hangs). Interpretation:
- **diff buckets:** |diff|==1 dominant → the residual ±1 (one-bucket-off scatter). >16 present →
  the large-magnitude "dominant" mode (would imply band-aid not fully masking).
- **localPos(i%256) clustering** (heuristic — output pos ≈ thread idx within a bucket for heavy
  dups): strong peak at **0** → first-thread-does-serial-scan (`ILGroupExtensions.InclusiveScanImplementation:150`);
  peak at **255** (=DimX-1) → last-thread-writes-global-counters (`RadixSortExtensions.cs:728`);
  uniform → general cross-worker barrier sync, not a thread-role special-case.
- **GpuTestVerify counts** (GpuTestVerify.cs:20-64): orderViolations = descending broken;
  **duplicates>0 = SCATTER COLLISION** (two elements → same output slot; Exchange-on-seen[v]) →
  points DIRECTLY at the pos computation: Kernel1 in-place presort `view[pos]=value` (RadixSortExtensions.cs:742-753)
  or Kernel2 global `output[pos]=value` (RadixSortExtensions.cs:918-934). trackingErrors =
  key/value pair broken. The duplicates path has its own diag block (BackendTestBase.cs:232+).
- **distinct256Groups small + tight span** → corruption localized to one group/pass (good — read
  that pass's emitted phase). **spread across many groups** → systemic sync/ordering.

## ★ SIGNATURE CAPTURED (2026-05-26, roll 1 of natural sweep, band-aid ON)
`RadixSortDescending4MTest` (4M) FAILED; 1.4M and 2M in the SAME sweep PASSED.
- 528 order violations; **68,493 total mismatches (~1.6% of 4M)** — SERIOUS, = SpawnScene
  ">4M holes" bug, NOT a tiny residual. Band-aid is NOT effectively suppressing 4M.
- diff buckets: |diff|==1:23,300 / 2..16:44,594 / >16:599 (mostly small, some cascade, few large).
- Span first=1888 last=4,193,458 **distinct256Groups=834** (~5% of groups, whole array).
- localPos(i%256) **near-uniform** (top [164]=293 vs ~268 avg) → NO thread-special-case. (Note:
  output-position%256 ≠ thread index for a global scatter, so this heuristic is weak anyway.)
- Pattern: contiguous block shifted by a small offset (value 9995588 displaced ~96 positions →
  ORDER break at i=1984 + ±1 cascade through 1888-1983). Block-level scatter-offset slip.
- **KEY: only 4M failed, not 1.4M/2M → SIZE-correlated, not pure sweep-load.** 4M has the most
  grid-stride iterations (4M/(numGroups·256)). Points to a per-grid-stride-iteration accumulating
  issue (more iterations → bug fires), which would be LOCALIZABLE — contradicts pure hardware-
  ordering. DECISIVE TEST IN FLIGHT: does 4M corrupt SCOPED/alone (no sweep churn)? If yes →
  size-driven logic bug in the grid-stride loop's barrier/scan/scatter handling (read RadixSortKernel1
  697-757 grid-stride + phase-state-machine across the loop's 4 barriers). If clean alone → needs
  churn (load) after all.

## ★ 4M WARM LOOP = CLEAN → load/churn-correlated CONFIRMED (2026-05-26)
4M in a WARM scoped loop (8 iters, no inter-test churn, throw-on-first) ran ~6-8 sorts and
TIMED OUT with NO corruption (would have thrown the corruption diag if any iter failed). Plus
1.4M warm (60 iters) clean. Yet 4M FAILS in a natural sweep. Conclusion: **the bug needs the
sweep's inter-test churn (kernel compiles / GC / memory growth → dispatcher spin-yields); warm
loops (no churn → no yields) don't corrupt.** Size (4M > 1.4M) AMPLIFIES (more barriers = more
chances per churn event) but is not sufficient alone. So it is YIELD/LOAD-correlated, NOT a pure
size-driven logic bug. (Note: the [TestMethod(Timeout=)] attribute seems capped at 240s by the
SpawnDev.UnitTesting framework — 8×4M didn't fit; not a blocker, warm-clean is the result.)

## ★ DECISIVE TEST IN FLIGHT: never-yield (threshold 2e9) sweep rolls (loop b7oqzwxn4)
If yields cause it, eliminating them should make the sweep clean. Isolated Wasm sweep (10
workers/12 cores) has no oversubscription, so pure-spin (never yield) can't starve. Rolling up
to 5 sweeps, stop-on-failure:
- ALL CLEAN → the spin-yield/resume path IS the culprit → localized; fix = make resume not lose
  visibility, OR don't yield unless genuinely oversubscribed (raise threshold / detect oversub).
- ANY corrupts → scheduling pressure itself (descheduling → stale reads even w/o JS yield) →
  hardware-ordering, harder; the yield is just the most common pressure source.
Either outcome is a big step. (If clean, next: read EmitSaveAllLocals for a missed loop-carried
local i/gridIdx across the yield, and the JS Atomics.wait/re-dispatch round-trip vs V8 ordering.)

## ★★★ ROOT CAUSE LOCALIZED (2026-05-26): the JS-side Atomics.wait in the spin-yield resume loop
**Never-yield (YIELD_SPIN_THRESHOLD=2e9 = effectively never return to JS): 5/5 sweeps CLEAN**
(vs corruption caught in 1 roll with the 1M threshold). Decisive: **the dispatcher spin-yield-
to-JS / resume path causes the corruption.** Pure spin (never yield) is correct AND fast — the
never-yield sweeps ran the same 17s test duration, so yielding is NOT needed when the Wasm suite
isn't CPU-oversubscribed; it fires anyway on churn-induced >5ms stalls (GC/kernel-compile) and corrupts.

**PRIME SUSPECT (sharp): `WasmAccelerator.cs` JS resume loop ~line 1843:
`Atomics.wait(yMem32, genIdx, savedGen, 0.05)`.** This is a FUTEX op — the SAME V8 FutexEmulation
linear-memory ordering bug (chromium#490434403 family, see [[project-wasm-race-v8-finding-2026-04-28]])
that the pure-spin WASM barrier was specifically built to AVOID. We removed wait/notify from the
wasm barrier but the JS-side yield-park still calls `Atomics.wait`, re-introducing the buggy futex
path. When a worker parks here and re-dispatches, the post-wait cross-worker memory view can be
stale → the group scan's first-thread read (ILGroupExtensions:150) gets a stale scanMemory value →
±1 scatter offset. Fits ALL evidence: yield-correlated, size-amplified (more barriers→more yields),
warm-clean (no yields), never-yield-clean (no Atomics.wait), fence-reducible (hw fence narrows it),
protocol provably correct per spec (it's the engine futex, not our logic).

**FIX DIRECTION (designing):** the yield-to-JS exists ONLY for anti-starvation under genuine CPU
oversubscription; pure-spin is correct + fast otherwise. Options, best-first:
1. Replace the JS `Atomics.wait` park with a NON-futex event-loop yield (self-postMessage or
   `await setTimeout(0)` if the worker handler can be async) — preserves anti-starvation reschedule
   WITHOUT the futex bug. Need to read the worker-script structure (WasmAccelerator.cs ~1700-1845).
2. Raise YIELD_SPIN_THRESHOLD dramatically (e.g. 50M ≈ 250ms) so yields fire only under SEVERE
   prolonged starvation, not churn stalls — REDUCES corruption to near-zero in practice but doesn't
   eliminate it under real oversub (mitigation, not full fix).
3. Keep an explicit hw-fence after the wait (band-aid; may not fully fix if the futex corrupts the
   line the later seqcst load also reads).
VERIFY any fix by rolling the sweep (caught 4M in 1 roll at threshold 1M) several times → must stay clean.
Current tree: WasmBackend.cs has the TEMP 2e9 threshold (revert to 1M); BackendTestBase.cs has the
enhanced diag (KEEP — it's a genuine improvement); notes untracked.

## ★★★ FIX APPLIED (2026-05-26) — replace JS Atomics.wait yield-park with non-futex setTimeout
`WasmAccelerator.cs` BuildWasmWorkerScript resume loop (~line 1842): the futex
`Atomics.wait(yMem32, genIdx, savedGen, 0.05)` → `if (Atomics.load(yMem32, genIdx) === savedGen)
{ await new Promise(r => setTimeout(r)); }`. The worker script runs as a `new AsyncFunction`
(WorkerPool.cs:131 WasmBootstrapScript), so `await` is valid. Rationale: cross-worker memory
ordering is re-established by the dispatcher's seqcst gen-load on re-entry, NOT by the JS park —
so the park only needs to be a SCHEDULING yield (let the OS reschedule a descheduled worker).
`setTimeout` yields to the event loop with NO futex → avoids the V8 FutexEmulation ordering bug.
Atomics.load gen-fast-check (atomic load, not the buggy wait/notify path) preserves the original
zero-overhead fast path when gen already advanced. YIELD_SPIN_THRESHOLD restored to 1M.
- Tradeoff: the setTimeout park is coarser (~0-4ms) than Atomics.wait (50us), but yields are rare
  (only on >5ms stalls / genuine oversub) and pure-spin is correct+fast otherwise (never-yield
  sweeps ran the SAME 17s), so the latency is immaterial. Anti-starvation under oversub preserved
  (the thread idles ~ms, freeing its core for the descheduled worker).
- **VERIFICATION IN FLIGHT (loop b464k36tf): roll up to 8 sweeps at threshold 1M (yields FIRE,
  exercising the new park), stop-on-failure.** Old code caught 4M corruption at ~14-25%/sweep, so
  8 clean = solid confirmation (P(8 clean|old rate)≈0.17) ON TOP of the clean mechanism. If ANY
  roll fails → fix incomplete (the resume logic itself, not just the futex). Pending result.
- Tree: WasmAccelerator.cs (fix) + BackendTestBase.cs (enhanced diag, KEEP) modified; WasmBackend.cs
  back to master (band-aid + 1M threshold); notes untracked. The group-barrier band-aid fence
  (7bfc364) is INDEPENDENT and stays. TODO after verify: update the stale Atomics.wait comment at
  WasmAccelerator.cs:1793-1805 + Wasm/CLAUDE.md "Barriers are PURE SPIN" note + memory; ONE commit.

## ✗✗✗ FIX FAILED + CORRECTED STATISTICS (2026-05-26) — READ THIS, it corrects the above
The setTimeout fix did NOT work: 6 clean rolls then FIXROLL 7 FAILED (SpawnSceneSim 1.4M, SAME
signature: 437 order violations, 16499 mismatches, block displacement, widespread, ±1-to-large).
**Honest Rule-4c correction:** across ALL sweep configs this session the fail rate is ~12.5%
(≈14 clean / 2 fail), INDEPENDENT of: band-aid on/off, never-yield (2e9), setTimeout park. So:
- The "never-yield 5/5 CLEAN = yield path localized" conclusion was a STATISTICAL ERROR — 5 clean
  at a 12.5% base rate is ~51% by chance. I over-weighted it and "fixed" the wrong thing (the JS
  Atomics.wait park). The setTimeout fix (6 clean) is likewise consistent with the SAME 12.5%
  rate, i.e. NO demonstrated improvement. Both reverted.
- **What IS statistically solid: warm 1.4M loop ×60 iters = 0 fails** (P≈3e-4 if rate were 12.5%).
  So warm repetition (same kernel, reused buffers, no inter-test churn) does NOT corrupt; the
  CHURNING sweep does (~1.6%/large-sort). The differentiator is CHURN, not the yield mechanism
  and not warm repetition.
- So the bug is **CHURN-correlated**: something about running MANY DIFFERENT kernels in sequence
  (recompile / re-instantiate / wasm-memory growth + SAB swap / cross-kernel cached-memory reuse /
  worker-pool re-init), NOT the spin-yield/resume park. Yields may still be a contributor (churn
  causes descheduling→yields) but are NOT clearly the cause.

## RE-TEST IN FLIGHT (loop b89gctq6f): never-yield (2e9) ×8 rolls, stop-on-failure
Decisive on yields-vs-churn: if ANY fails → bug occurs WITHOUT JS yields → yields RULED OUT →
it's churn/scheduling/memory. If 8 clean (×13 total never-yield rolls w/ the prior 5; P≈0.18 by
variance) → yields more strongly implicated after all. EXPECTATION (from the 12.5%-all-configs
data): likely fails within 8 → redirect to churn/memory.

## STRONGEST LEADS for churn-correlation (next session, if yields ruled out)
1. **Cross-kernel wasm-memory reuse / growth.** WasmAccelerator caches `_cachedWasmMemory` and
   reuses/grows it across dispatches (~826-859). Warm loop = same kernel = stable layout. Sweep =
   different kernels/sizes → memory grow (new SAB) + re-instantiate (WorkerPool.cs:118-121 clears
   `_instancesById` on `.buffer` change). Audit: is the barrier/fence region + scratch correctly
   re-zeroed/initialized when memory is REUSED for a DIFFERENT-shaped kernel, or after a grow?
   A stale barrier counter or scratch slot from a prior kernel → wrong "last worker" / stale scan.
2. **Per-kernel instance cache vs memory swap** (WorkerPool.cs:104-128): module/instance caching
   across kernels; verify a stale instance can't survive a memory swap.
3. Re-examine the dispatcher's between-DISPATCH state (does anything assume zeroed fence region at
   dispatch start? gen counters are change-relative so OK, but arrival/exit-flag must start clean).
Tools: enhanced diag is in place (BackendTestBase.cs, KEEP). Sweep reproduces ~12.5%; roll to catch.

### Churn lead #1 PARTIALLY RULED OUT by reading (2026-05-26)
The dispatcher ZEROES the entire dispatch region `[0..totalWithBarriers)` (buffers+scratch+shared+
barrier+fence+yieldState) on the MAIN thread at the START of every dispatch (WasmAccelerator.cs:916-921),
before copy-in, before worker dispatch (dispatches serialized via `_pendingWork`). So **simple stale
barrier-counter / scratch state carried from a prior different-shaped kernel is RULED OUT** — it's
zeroed each dispatch. So if churn is the cause, it's subtler than leftover state. Remaining churn
suspects: (a) `WebAssembly.Memory.grow` on shared memory + the workers re-getting `.buffer` and
re-instantiating (WorkerPool.cs:118-121, 104-128) — a race or stale view across the grow; (b) the
per-kernel module/instance cache surviving a memory swap incorrectly; (c) something in copy-IN /
NativePtr patching under varying buffer layouts. NOTE: also re-verify the CHURN premise itself — the
"warm clean vs sweep corrupt" delta could ALSO be explained by the sweep simply running MORE large
sorts (more trials) rather than churn per se; a controlled within-run trial-count A/B would settle it
(per [[feedback-probabilistic-bug-need-enough-trials]]).
Tree after re-test: restore threshold to 1M (WasmBackend.cs), keep diag, notes untracked, NO fix yet.

## ⚠️ STATISTICAL HONESTY CHECK (2026-05-26) — what is ACTUALLY established
Per-large-sort failure rate ≈ **1.6%** (~12.5%/sweep ÷ ~8 large sorts/sweep). At that rate, ALL my
"clean streak" conclusions this session are INCONCLUSIVE (I repeatedly used the wrong base rate):
- warm 1.4M ×60 clean: P(0 fails | 1.6%) = 0.984^60 ≈ **0.38** → NOT proof warm is safe / churn-required.
- never-yield ×5 clean: P(0 | 12.5%/sweep) ≈ **0.51** → NOT proof yields are the cause.
- setTimeout fix ×6 clean then fail: consistent with the SAME base rate → NO demonstrated effect.
**SOLID facts = only the observed FAILURES:** the bug is real, reproduces in churning sweeps,
rich signatures captured (4M ~1.6% mismatches / SpawnSceneSim 1.4M; block displacement, widespread,
±1-to-large magnitude, near-uniform localPos, gpu-below-cpu shift). Everything else (warm-vs-sweep,
yields-vs-churn, band-aid effect, the failed fix) is UNPROVEN — short clean streaks at a ~1.6% rate.
**Method correction (per [[feedback-probabilistic-bug-need-enough-trials]]):** stop rolling 8-min
sweeps to chase a 1.6% bug — it's both slow AND statistically weak. Build a WITHIN-RUN high-trial
design: one PMT test that runs many (100s) large sorts — ideally with churn (interleave a few
different-sized sorts / a scan, to recreate sweep conditions) — and COUNTS failures (not throw-on-
first), so a single run yields hundreds of trials. Then A/B one variable (e.g. never-yield) by
comparing failure COUNTS with enough n to beat the base rate (~22+ clean to rule a 1.6%... wait,
to rule OUT at 95% you need n where 0.984^n<0.05 → n≈186 trials clean). So: count failures over
many trials per config and compare RATES, don't chase zero-fail streaks.

## HIGH-TRIAL HARNESS BUILT (2026-05-26, TJ chose Option 1)
Added `RadixSortHighTrialCountTest` to BackendTestBase.Tests9.cs (TEMP, remove after root-cause):
alternates 1.4M/2M DescendingInt32 sorts, FRESH per-trial allocation (new arrays+buffers+GC+memory-
growth = the sweep's churn a warm loop lacks), COUNTS failures (orderViol/dups/oor) over TRIALS=40,
throws with the COUNT so a rate can be read. Run scoped `PMT_FILTER=WasmTests.RadixSortHighTrialCount`.
Why 1.4M/2M not 4M: framework caps tests at ~240s (the Timeout= attribute seems ignored), 4M is
~30s/sort (only ~8 trials) but 1.4M ~3.75s (~50 trials) — need trials for power. PLAN:
1. MEASURE baseline rate at threshold=1M (yields on). If usable (e.g. ≥5%/some-config), proceed.
2. A/B: rebuild at threshold=2e9 (never-yield) vs 1M, compare failure RATES over many trials.
3. Also A/B band-aid on/off if time. Compare RATES (≥~186 clean to rule a 1.6% config "fixed").
Sequencing: never-yield ×8 SWEEP re-test (b89gctq6f) finishing first (66% chance it FAILS → solidly
rules out yields, which would refocus the harness on churn factors). Then set threshold per A/B, build, run harness.

## SIGNATURE MECHANISM (block displacement → per-bucket scan offset)
The signature is a CONTIGUOUS BLOCK shifted by a small offset k (e.g. value 9995588 displaced ~96
positions, with a ±1 cascade through the gap). Mechanism: in RadixSortKernel1 the scatter pos
(RadixSortExtensions.cs:741-749) = gridSize + scanMemory[bucket] + cross-bucket prefix. If the group
scan produces a per-bucket offset that's wrong by k, EVERY element in that bucket lands k off → a
displaced BLOCK of size = that bucket's count. The scan (ILGroupExtensions.cs:150) is first-thread-
serial over scanMemory (all workers' shared writes, published by the barrier at :147). A single stale
cross-worker read there → one bucket boundary wrong → block shift. Consistent w/ churn/scheduling
pressure causing an occasional stale read despite the seqcst barrier (engine-ordering OR subtle
barrier gap; protocol proven correct per spec). This is WHY the magnitude is small-ish (adjacent
bucket boundaries differ by small key deltas in dense data) and widespread (any group's scan can slip).

## ★★★ 2026-05-26 SOLID: never-yield CORRUPTS → yields RULED OUT; localized to SCAN+BROADCAST
NY2ROLL 3 (threshold=2e9, NEVER-YIELD): **`ScanBroadcastIsolationTest` FAILED** — "16/16 wrong left
boundaries. Bug is in pass 2 (group scan + broadcast)." Values wildly wrong (got=1621203 exp=0;
got=48 exp=256; ...). So corruption occurs with PURE SPIN (no JS yield at all) → the yield mechanism,
resume path, and Atomics.wait are DEFINITIVELY NOT the cause (8 never-yield rolls: 1 fail, ~same rate
as yields-on; the earlier 5/5 clean was variance, now confirmed). **The bug is in the pure-spin
cross-worker barrier sync around the GROUP SCAN + BROADCAST (multi-pass scan "pass 2", the LEFT
BOUNDARY broadcast).** This is a dedicated isolation test (ScanBroadcastIsolationTest) that pinpoints
it — a far more DIRECT + likely faster repro than RadixSort. NEW PLAN: pivot the repro/A-B to
ScanBroadcastIsolationTest (loop IT for high-trial counting); investigate the broadcast mechanism
(Group.Broadcast → atomic store/load sync; Wasm/CLAUDE.md notes "broadcast atomic store/load" + the
codegen counts Broadcast as 2 barrier slots). The "wrong LEFT boundary" = the cross-group/cross-worker
broadcast of the scan's boundary value is stale/racy on the pure-spin path.
CAVEAT (per [[feedback-probabilistic-bug-need-enough-trials]]): ONE occurrence so far — confirm
ScanBroadcastIsolationTest reproduces reliably (loop it) before over-committing, but it's a strong lead.
ALSO: my RadixSortHighTrialCountTest (added to base class) TIMED OUT (600s) in the sweep + its
Console output isn't captured in PMT logs → it CONTAMINATES full sweeps. REMOVE it from base-class
sweep runs (or gate/scope/shrink it); pivot to looping ScanBroadcastIsolationTest instead.

## 2026-05-26: scoped scan/broadcast harness = 0/300 → trigger is SWEEP CONTEXT, not within-test
Repurposed the harness to loop the tiny ScanBroadcastIsolationKernel (16×256, fast — 300 trials in
14s) with fresh-alloc per trial: **0/300 corrupted**. So fresh-alloc churn ALONE doesn't repro the
scan/broadcast bug. Same as RadixSort (warm clean, sweep corrupts). **The trigger is the FULL-SWEEP
cross-test accumulation** (462 tests in one browser session): grown wasm memory, cross-kernel
instance-cache churn, GC/browser pressure, cumulative dispatch/worker state — NOT within-test
repetition. (0/300 at the scan kernel also means its scoped per-trial rate is <~1%, P(0/300|1%)=0.05.)
IN FLIGHT: "mini-sweep" harness (b6zlj864e) — per trial: a fresh 1.4M sort (memory growth +
cross-kernel churn) THEN the scan/broadcast detector, 40 trials, counts both. Tests whether
grown-memory + cross-kernel state triggers it. If scanFail>0 → narrowed + fast repro; if 0 → the
trigger is deeper full-session accumulation (impractical to recreate; full sweep stays the repro).

## Working hypothesis (signature-driven)
±1 = an element landed one BUCKET off → adjacent-bucket prefix-sum OFFSET is wrong under
contention. Points at the SCAN helper's cross-worker offset publication or the
scan↔scatter phase boundary, not the group barrier and not memory ordering. Confirm with
the throw-on-first repro's ROOT-displacement locations (group~i/256, localPos) before
touching code. Do NOT add fences (Rule: find the logic race).

## 2026-06-09 (Geordi, attempt #9): H8 (shared-mem alloca slot overlap) RULED OUT by direct measurement
H8 was the last un-audited corpus suspect from attempt #8's handoff: "two distinct allocas
colliding on an offset, OR fiber re-entry clobbering a live shared-mem slot." Audited it to ground.

**Built an OFFLINE compile harness** (`SpawnDev.ILGPU.DemoConsole -- wasm-dump`,
`WasmCompileDump.cs`): compiles the RadixSort kernels on the DESKTOP (no browser, no workers, no
dispatch — `WasmAccelerator.Create` try/catches the JS lookup; `CreateRadixSort*` compiles eagerly
via LoadKernel) with `WasmBackend.VerboseLogging=true`, captures the emitted `[Wasm-SharedMem]`
alloca table, and flags any `GenerateCode(Alloca)` type+size FALLBACK aliasing or offset overlap.

**Measured result (RadixSortKernel1, the barrier kernel, groupSize 256 / UnrollFactor 4):**
- Exactly **2 distinct shared allocas**: `scanMemory` int[1024] @offset 0 (4096B); group-scan
  scratch int[256] @offset 4096 (1024B). **Non-overlapping.**
- **ZERO fallback matches** — every shared alloca resolves by its primary key `v_{Value.Id}` to a
  distinct offset. The type+size fallback (the aliasing mechanism) never fires for these kernels.
- The unrolled `GroupExtensions.ExclusiveScan` calls share ONE deduped scratch (int[256]), not
  UnrollFactor copies. The "UnrollFactor inlined int[2048] scratches collide" sub-theory was WRONG.

**Also ruled out by code reading this pass (same attempt):**
- **Between-group shared zeroing race**: `zeroRegionSize = fenceSlot - sharedMemBase` covers the
  FULL shared region + barrier slots (NOT under-counted), and the phase barrier gates EVERY phase
  incl. the last, so worker-0's zeroing can't stomp a slow worker still in the final phase body.
- **Per-thread scratch overflow**: each tid's scratch slot is owned by exactly one worker; an
  undersized slot would corrupt the WorkerCount=1 path too — but that path is 4/4 clean.

**Conclusion: the residual race is NOT a shared-memory alloca-layout bug.** H8 is dead. The standing
strongest live lead remains the SCAN/BROADCAST cross-worker boundary publication (2026-05-25/26
entries above: "wrong LEFT boundary" in ScanBroadcastIsolationTest), whose trigger is FULL-SWEEP
cross-test accumulation, not within-test repetition.

**Doc fix landed**: Wasm/CLAUDE.md said `MaxNumThreadsPerGroup=64`; the device actually sets 256
(`WasmILGPUDevice.cs:68-69`, confirmed by the dump). The stale 64 had misled part of the H8 analysis.

## 2026-06-09 (Geordi, cont.): the barrier kernel in a Wasm RadixSort is the COUNTER SCAN, not kernel1 — race is its multi-tile boundary carry
Used the offline `wasm-dump` harness (now prints per-kernel info lines) to attribute shared
allocas to specific kernels. For `CreateRadixSort<int,DescendingInt32>`, THREE kernels compile:
- kernel1 (presort): `params=14, sharedMem=0, barriers=0, hasBarriers=False` — Wasm uses the
  CPU-STYLE no-barrier `CPURadixSortKernel1` (single-thread-per-group; its int[UnrollFactor]
  scanMemory/addMemory are NOT shared allocas here). **No barriers, no shared mem.**
- **scan (of the counter): `params=20, sharedMem=5120, barriers=8, hasBarriers=True, helpers=1`**
  — the ONLY barrier kernel (production numbers, reverted build). 5120 = scan workspace int[1024]
  @0 (4096B) + scanResults int[256]@4096 (1024B), exactly. TWO named shared allocas, distinct
  sizes (1024 vs 256), **no overlap, no fallback**. The 8 barriers = pre-loop Scan (3: two
  InclusiveScanImpl barriers + one scanResults-copy barrier) + tile-loop body {NextIteration
  Broadcast (2) + Scan (3)} = 3+5. (Broadcast value+tag slots log as `[Wasm-Broadcast]`, not
  `[Wasm-SharedMem]`, and live in the helper.)
- kernel2 (scatter): `params=27, sharedMem=0, barriers=0` — no barriers, no shared mem.

**Proof the int[1024] alloca is the SCAN WORKSPACE (not the radix scanMemory):** a controlled
probe changed `InclusiveScanImplementation`'s `Allocate<T>(1024)` → 2048; the dump's first alloca
tracked 1024→2048 and NO separate int[1024] appeared. So the radix `scanMemory` is absent from
every barrier kernel. (Probe reverted.)

**`SingleGroupScanKernel` launches as `(1, MaxNumThreadsPerGroup)` = ONE group of 256 threads
(`ScanExtensions.cs:516`).** So "multi-group sort fails" does NOT mean cross-group shared memory.
A LARGE sort makes a LARGE counter array (UnrollFactor*numRadixGroups), which this single group
scans in MANY TILES (`ComputeTileScan`, `ScanExtensions.cs:388-403`), carrying the running total
across tiles via `groupScan.NextIteration` → `ExclusiveScanNextIteration` →
`Group.Broadcast(currentValue, DimX-1)`. **The race is the multi-WORKER, multi-TILE boundary
carry within one group.** Fits every survivor: needs ≥2 workers (1 worker = no cross-worker
broadcast/barrier race); large input = many tiles = many boundary carries = more race windows;
"contiguous runs shifted by an offset" = one tile's `leftBoundary` carried wrong.

**Broadcast tag is useless tile-to-tile:** tag = linear group index = constant 0 for a single
group, so it can't distinguish tile N's broadcast slot contents from tile N+1's. Correctness
across tiles rests ENTIRELY on the phase barriers (broadcast barrier1/barrier2 + the scan's
internal barriers). So the residual is either (a) V8 pure-spin PHASE-barrier linear-memory
ordering under heavy yielding, or (b) a fiber state-save/restore gap on the MULTI-WORKER
spin-yield path (single-worker also saves state every phase and is clean, so a plain state-save
gap is excluded — it must be specific to the ≥2-worker yield/resume path under contention).

**NEXT (needs full-sweep PMT + FO76 = TJ go):** instrument the SCAN kernel's per-tile
`leftBoundary` carry — dump (tileIndex, leftBoundary, localBoundaries.RightBoundary, broadcast
value) per tile to a buffer and compare a corrupted run's first wrong tile against the CPU
oracle. ScanBroadcastIsolationTest is the fast within-test probe but is 0/300 scoped — the
trigger is full-sweep cross-test accumulation, so the instrumented run must be the full sweep.

## 2026-06-09 (Geordi): barrier MECHANISM cleared by pure-Node harness — bug is kernel logic, and wait/notify is NOT the V8 culprit
Ran the existing pure-Node harness `<outer>\wasm-barrier-repro\run-scan-test.mjs` (hand-written
scan-barrier MODEL, read-compute-write-barrier cycles; worker_threads + SAB; A/Bs spin vs wait32;
no Chromium). Config 12 workers × 64 threads = 768, 150 phases, 4 rounds, oversubscribed on a
12-core box:
- **spin: PASS (0 violations / 4 rounds). wait32: PASS (0 violations / 4 rounds).**

**Implications (evidence, not theory):**
1. The barrier PATTERN itself is correct under BOTH spin and wait32, even oversubscribed with many
   phases — matches `Research\01-wasm-memory-model-and-atomics.md` (the gen-barrier is correct by the
   model). So the residual is NOT the barrier wait mechanism.
2. **wait32 is NOT inherently broken on V8.** It passes a correct pattern in Node V8. So the
   `wasm-waitnotify-still-races-2026-05-24.md` "V8 bug" verdict is very likely MIS-ATTRIBUTED — the
   real-kernel wait/notify failures were the SAME kernel-protocol race, exposed worse by wait/notify
   timing. This is the through-line: **fix the kernel-logic race → both barriers pass → wait/notify
   viable → workers PARK instead of spin → the spin core-burn (the "rapes my PC" problem) is solved.**
3. The model harness does NOT reproduce the bug because it does NOT model the buggy logic — the
   multi-tile `ComputeTileScan` boundary carry via `Group.Broadcast`. **Next: extend the Node harness
   to run the REAL generated scan kernel** (offline-compiled wasm + replicated single-group dispatch),
   scan a large counter array, diff vs a JS prefix-sum oracle, oversubscribed — a cheap, controllable,
   no-Chromium repro of the ACTUAL race. Then instrument per-tile leftBoundary to find the protocol gap.

Full index of the corpus + reading order: `<repo>\SpawnDev.ILGPU\Wasm\RESEARCH-INDEX.md`.

## 2026-06-09 (Geordi): REAL-kernel pure-Node repro BUILT — isolated scan kernel is CLEAN; redirect to pipeline/growth
Built a pure-Node worker_threads harness (`<outer>\wasm-scan-repro\run-real-scan.mjs`) that runs the
ACTUAL generated `SingleGroupScanKernel` (emitted offline via `DemoConsole -- scan-emit`; byte-identical
to RadixSort's counter scan: sharedMem=5120, barriers=8, scratchPerThread=2376). Faithfully replicates
WasmAccelerator's barrier dispatch: full memory layout, per-worker fiber ranges (`fibersPerWorker =
ceil(256/W)`), the dispatcher arg list, and the spin-yield/park(`Atomics.wait`)/resume loop. No Chromium,
no Blazor, no FO76 — controllable worker count.

**VALIDATION GATE PASSED:** 1 worker, N=256 (1 tile) and N=512 (2 tiles, exercises the broadcast
boundary carry) → correct inclusive scan. So the harness is a faithful replica (a layout/ABI bug would
corrupt even 1-worker output). (One bug found+fixed during bring-up: `gridDimX` is the TOTAL extent
`numGroups*groupSize`, NOT the group count — passing 1 trapped `remainder by zero` in `Grid.IdxX %
(dimX/realGroupDimX)`, the documented 2D-group trap.)

**RESULT — the isolated scan kernel does NOT reproduce the race:**
| N (tiles) | workers | rounds | JS-yields | violations |
|-----------|---------|--------|-----------|-----------|
| 16384 (64) | 8 | 30 | — | 0 |
| 16384 (64) | 16 (oversub) | 30 | — | 0 |
| 65536 (256) | 24 (2× oversub) | 40 | 495,184 | 0 |
| 16384 (64) | 48 (4× oversub) | 100 | 951,703 | 0 |
| 262144 (1024) | 16 | 30 (~30K tile carries) | 933,306 | **0** |

Heavy contention, the spin-yield/park/resume path hammered (~1M yields), oversubscribed past cores —
**0 violations across all configs.** A ~1.6%/sort intra-scan bug would have fired hundreds of times.

**CONCLUSION (honest correction of the 2026-06-09 earlier entry):** the scan kernel is the only BARRIER
kernel in a Wasm RadixSort, but its INTRA-DISPATCH scan/broadcast logic is **CLEAN** — it is NOT the
residual bug. The "localized to the scan kernel" claim is **withdrawn**: being the only barrier kernel ≠
being the bug. The residual requires the radix PIPELINE context that this isolated single-kernel,
fixed-memory harness does NOT exercise:
1. **Cross-dispatch counter handoff** — kernel1 (non-barrier presort) writes `counter[]`, the scan reads
   it, kernel2 (non-barrier scatter) reads the scanned counter — across SEPARATE Wasm dispatches on a
   reused worker pool. (My 2026-06-08 cross-dispatch micro-repro cleared *postMessage SAB visibility*,
   but not the real multi-kernel buffer handoff.)
2. **Memory-growth propagation lag** — `wasm-sharedarraybuffer-growth.md`: host grows the SAB between
   dispatches; a reused worker lags seeing the new buffer. My harness uses FIXED memory + fresh workers
   per round → does not exercise growth. **This is now the leading suspect.**

**NEXT:** extend the harness to (a) persistent workers across multiple dispatches with a `memory.grow`
between them (the growth-lag scenario), and/or (b) the full kernel1→scan→kernel2 counter handoff. Tools:
`DemoConsole -- scan-emit` (emit kernels), `<outer>\wasm-scan-repro\run-real-scan.mjs` (extend).

## 2026-06-09 (Geordi): memory-GROWTH-LAG hypothesis ELIMINATED on the real scan kernel
Extended the Node repro to `<outer>\wasm-scan-repro\run-persistent-scan.mjs`: PERSISTENT message-driven
workers (faithful port of `WorkerPool.WasmBootstrapScript` — per-kernel module cache, re-instantiate on
`memory.buffer` swap), and a host loop of VARYING-N scan dispatches that force the shared
`WebAssembly.Memory` to `grow()` between dispatches (re-sending wasmBytes, exactly like WasmAccelerator
clearing `_initializedWorkersByKernel`). Oversubscribed (W=16 on 12 cores).
- **200 dispatches, 3 real grows, ~520K JS-yields, module re-instantiation on every grow → 0 violations.**

**GROWTH-LAG IS ELIMINATED** (the corpus's leading-but-unconfirmed suspect, `wasm-sharedarraybuffer-growth.md`).
And it confirms the first-principles objection: SHARED memory grows IN PLACE (existing instances see it),
dispatches are SERIALIZED (`RunKernelAsync` awaits `_pendingWork`), so a grow NEVER happens while a worker
is mid-flight — there is no lag window to race. The Gemini-conversation "post-grow handshake" remediation
is **not needed** (no bug to fix there).

(Harness note: a varying-N sequence that SHRINKS must zero the reused working region `[scratchBase, end)`
per dispatch — else a smaller dispatch's fence/arrival slots land on a larger dispatch's stale scratch and
the barrier DEADLOCKS. That is a harness-fidelity artifact, not the backend bug; the backend zeroes scratch
between dispatches (fiber-refactor note #8). Worth a separate check that the backend zeroes the fence/arrival
region when reusing cached memory across DIFFERENT-layout dispatches — kernel1/scan/kernel2 in one sort have
different fenceSlots over the same buffer base.)

## STATUS: cheap-reproducible suspect space is EXHAUSTED CLEAN
Ruled out by cheap Node/offline repro (no Chromium, no FO76): barrier mechanism (spin AND wait32),
isolated scan kernel (1024 tiles / 48 workers / ~1M yields), memory-growth-lag, H8 alloca overlap,
cross-dispatch postMessage SAB visibility (attempt 8). Plus prior: kernelId collision (fixed), group-fence
(no-op). **The residual lives in full-pipeline INTEGRATION that component repros don't capture** — the only
remaining realistic suspects are the multi-kernel kernel1→scan→kernel2 counter handoff under the REAL
host orchestration (8 passes, temp ping-pong) and/or the fence-region-reuse-across-different-layouts noted
above. Next: either the full radix pipeline in Node (emit 3 kernels + replicate host orchestration) OR a
single targeted INSTRUMENTED real-backend scoped+oversubscribed run (TJ-sanctioned "when we really need
contention") to catch the first wrong value + the worker/phase state at that point.
