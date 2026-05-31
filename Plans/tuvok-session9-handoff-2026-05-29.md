# Tuvok Session 9 handoff — Wasm residual large-sort race (2026-05-29)

**READ FIRST on resume.** Lead editor: Tuvok (ILGPU + ILGPU.ML). Continues the residual
investigation in `SpawnDev.ILGPU/Wasm/Notes/residual-sort-race-2026-05-25.md`.

> ## ★★★ SESSION 10 DONE — the repro below RAN; verdict = barrier+read visibility is SOUND.
> The Session-9 "DECIDED NEXT STEP" (engine-vs-pattern repro) is **COMPLETE**. See the
> **SESSION 10 entry at the top of `Wasm/Notes/residual-sort-race-2026-05-25.md`** for full numbers.
> Headline: on TJ's real Chrome 148, ~18M oversubscribed fan-in park/wake reads on the production
> yield-park barrier → **ZERO stale**. The boundary-read visibility hypothesis (line-747 + scan
> boundary reads) is **REFUTED**. Exonerated by reading: barrier ordering, resume arrival-counting,
> shared-mem layout. CPU "fail" in a scoped sweep = a 600s TIMEOUT (FO76 starvation), NOT a sort bug ⇒
> the bug IS Wasm-specific.
>
> **FIX IMPLEMENTED (TJ approved "implement now") + NO-REGRESSION GREEN, uncommitted:** the residual was
> traced to a **fiber save/restore COUNT ASYMMETRY** — `EmitSaveAllLocals` was emitted INLINE at
> `_locals.Count`-as-of-then, but the restore prologue is DEFERRED to the FINAL 277-count, so a
> loop-carried local allocated late in codegen (set in a later phase of iter N, read at the top of an
> earlier phase in iter N+1) was NOT saved by the early-phase barrier → restored from stale scratch on
> resume → wholesale ~21% corruption, ONLY under yielding (= FO76). Fix: defer the saves exactly like the
> restore (record positions, build one full-count save block at end, InsertRange all in descending
> position order). `WasmKernelFunctionGenerator.cs`. Build green; `PMT_FILTER=Detector` 14/0/2 incl. the
> Wasm `GridStrideScanStateDetectorTest` loop-carried probe. **AWAITING TJ's FO76 2-concurrent sweep to
> confirm the residual is closed (UNPROVEN — bug is ~1/7 intermittent; a single run is inconclusive).**
> If it recurs: next suspects = helper-internal state, cross-group counter scan.
>
> **FO76 SWEEP RESULT (2026-05-29 ~16:00): the save-asymmetry fix did NOT close the residual** — confirmed
> in build (presort kernel grew 50,572→74,658 bytes) yet it still failed. Keep the fix as hardening; resume
> hunt. Failing instance: OddCount CATASTROPHIC (96% mismatch, wrong from index 0, values MISPLACED-VALID
> not stale) + SpawnScene mild + Sentinels 241s TIMEOUT + RepeatedResort ObjectDisposed cascade. Also
> exonerated since: dispatch-completion + worker→host visibility (counter scan is a GPU kernel = worker→
> worker, repro-proven sound; host reads only the FINAL output, post-Synchronize). Corruption = genuine
> GPU-side compute error (Kernel1 counts / scan / Kernel2 pos), intermittent, contention-only.
>
> **★★ NEXT SESSION STARTS HERE — per-pass COUNTER LOCALIZER built + wired + build GREEN (uncommitted):**
> `Demo.Shared/UnitTests/RadixCounterLocalizer.cs` + `BackendTestBase.RunTest`. Snapshots each radix pass's
> Kernel1 bucket counts via a stream-ordered COPY KERNEL — the only stream-ordered primitive on Wasm (TJ
> caught that `stream.Synchronize()` is a no-op and `CopyTo` is sync-immediate on Wasm, so the existing hook
> never worked there). On ANY Wasm test failure it appends a report to the error in
> `_tj_dump_local/latest.json`: either `ROOT: FIRST corrupted counter at pass#K` (→ read that pass's Kernel1
> emitted phase code — the COUNT is wrong) OR `ROOT: all counter sums CONSISTENT → bug is in SCAN or SCATTER`
> (→ pivot OFF Kernel1 to the scan kernel / Kernel2 pos). Wasm-gated; `Enabled=true`.
> **ACTION: re-run the FO76 2-concurrent sweep; read the localizer ROOT line in the failing test's error to
> pin the pass + kernel.** If the residual STOPS reproducing with it enabled (extra per-pass dispatches add
> serialization), that itself = inter-pass/dispatch-overlap race; A/B via `RadixCounterLocalizer.Enabled=false`.
>
> SEPARATE bug: Sentinels 241s TIMEOUT = group/phase-barrier livelock under extreme contention (cascades to
> the ObjectDisposed). Not the corruption; consider higher `WasmDispatchWatchdogSeconds` or a starvation fix.
> Full detail = SESSION 10 entry atop `Wasm/Notes/residual-sort-race-2026-05-25.md`.
> Everything below is the (now-completed) Session-9 plan, retained for context.

---

## DECIDED NEXT STEP (TJ approved, Option 1) — ✅ COMPLETED in Session 10

**Extend the existing `LostBeard/v8-atomics-wait-bug` repro to settle ENGINE-vs-PATTERN for the
pure-spin path, because the fix SHAPE depends on the answer.**

- Clone `https://github.com/LostBeard/v8-atomics-wait-bug.git` to a scratch dir.
- It already has 4 tests for the **wait/notify** barrier (Test 4 = loop fix = 0 stale on all
  engines incl ARM — resolved as a PATTERN bug per Shu-yu Guo, TC39 #3800).
- **ADD a new test for the pattern we actually run on the default path:** a seq_cst
  **generation-counter SPIN barrier** (`while(Atomics.load(gen)===myGen){}` + `atomic.fence`),
  then a **single `Atomics.load` of a DATA slot that is REUSED across "groups"**, N workers,
  under CPU contention, looped millions of times. Detect any stale data read (read != current
  generation's written value).
- Serve with COOP/COEP (`npx serve` per repo README) and drive via **Playwright/headless Chrome**
  (PMT already uses Playwright) on TJ's current Chrome. Record `chrome://version` V8 build.
- **Outcome decides the fix:**
  - **Stale read reproduces (ENGINE)** → seq_cst fence not honored for the data read under
    pressure → need per-read re-validate loops (heavy) OR a stronger barrier mechanism; the
    broadcast tag-spin is the correct mitigation pattern; file/annotate upstream (chromium
    490434403 / 495679735).
  - **No stale read (PATTERN)** → a clean single-point re-validate fix exists in our codegen;
    hunt the specific missing re-validate (most likely the scan/scatter boundary read path).

Then apply the right fix to the scan + radix scatter cross-worker reads and validate with the
detector + TJ's FO76 2-concurrent sweeps.

---

## WHAT IS SOLID (this session, evidence-based — do NOT re-litigate)

Diagnosis of TJ's runs: both the 12-14 (corruption) and 14-24 (timeouts) FO76 runs are the known
churn/contention **residual**, NOT a regression from the uncommitted local.13/14/15 working tree
(which builds clean and does not touch the radix scan/broadcast/barrier core path — it's the
documented TensorView/Float16/GridStride ML-enablement work).

**Three deterministic codegen hypotheses RULED OUT (binary + source reading):**
1. **Missing/misplaced barrier fence** — RULED OUT. Disassembled the actual failing presort kernel
   `_tj_dump_local/2026-05-29_12-14-20/wasm/018_kernel_6.wasm` (helpers=1 barriers=20 47KB).
   func 26 = phase dispatcher (all 6 fences correctly placed: release before arrival RMW @22041,
   release before phase-gen store @22080, acquire post-spin @22130, group release @22196, group
   acquire @22241). Happens-before chain complete per wasm memory model. func 24 = kernel phase
   code, func 25 = scan helper.
2. **Cross-worker reads not atomic** — RULED OUT. `WasmKernelFunctionGenerator.GenerateCode(Load)`
   lines 1852-1878 emit `i32/i64/f32/f64.atomic.load` for ALL types when `_hasBarriers`
   (+ struct/Float16 paths 1576/1650-1707). The 295 plain loads in the binary are per-thread
   private scratch/state, not cross-worker. **So "make the reads atomic" is a NON-FIX — they are.**
3. **Loop-carried local dropped across yield** — RULED OUT. `EmitSaveAllLocals` (4156) /
   `EmitRestoreAllLocalsTo` (4200) are symmetric (same `_locals` list, same type-stepped offsets),
   and the restore prologue is emitted AFTER all locals are known (line 1359 comment + InsertRange
   at 1395). Complete coverage.

**Therefore:** every cross-worker read is already atomic, fences are correct, state save/restore is
complete. The broadcast (ca20808) is the ONLY cross-worker read with a re-validate LOOP (spin on a
group tag). The residual lives in the SINGLE-read cross-worker accesses that lack that loop — most
likely the **scan boundary reads** (`WasmGroupExtensions.cs` InclusiveScanWithBoundaries reads of
`scanResults[0]`/`scanResults[DimX-1]`) and the **radix scatter direct boundary read**
`scanMemory[groupSize*j-1]` at `RadixSortExtensions.cs:747` (NOT a Group.Broadcast → not covered by
the tag handshake; prime suspect for the "block displacement by k" signature).

**Routing confirmed:** RadixSort `GroupExtensions.ExclusiveScan<int,AddInt32>`
(RadixSortExtensions.cs:724) → `WasmAlgorithmContext` redirect → `WasmGroupExtensions.ExclusiveScan`
→ InclusiveScanImplementation. The scan is a THREAD-0-SERIAL design (thread 0 writes all, every
thread reads back cross-worker) — cross-worker reads are inherent, can only be made fresh, not removed.

## RESEARCH (TJ + Trip compiled) — `D:\users\tj\Projects\SpawnDev.ILGPU\_research\`
- README + 02 + 03 are the key files. Takeaways:
  - April wait/notify "engine bug" = OUR pattern (missing while-loop gen re-check), per Shu-yu Guo,
    TC39 #3800. Fix = loop/re-validate. Repo `LostBeard/v8-atomics-wait-bug` Test 4 = loop = 0 stale.
  - In-tree verdict: dispatcher `memory.atomic.wait32` STILL races large sorts on current Chrome →
    default stays **pure spin + atomic.fence**. Residual on spin path = "memory-VISIBILITY failure
    after gen advanced despite seq_cst fences" (matches my findings).
  - Chromium trackers behind sign-in: **490434403**, **495679735** — TJ to read with his account.
  - Reference repos TJ flagged: `WebAssembly/threads` (spec), `slavamuravey/atomics-sync`
    (mutex/semaphore/barrier/condvar on SAB — canonical patterns).
  - Guidance: don't call it an engine bug without running `CrossGroupScanReuseDetectorTest` (done,
    clean) + quiet scoped radix.

## WORK PRODUCT THIS SESSION (UNCOMMITTED working-tree additions, build green)

Added two self-verifying detectors (real production scan/broadcast paths, atomic error counters,
pass silently when clean → safe permanent regression guards). **Verified clean scoped on all 8
capable backends ×2; WebGL skipped.** Both are LOW-YIELD for catching the residual (same ~1.6%
trip rate as a single sort) but are good guards + localizers IF they fire.

- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.Tests9.cs`:
  - `CrossGroupScanReuseDetectorTest` + `CrossGroupScanReuseDetectorKernel` — 16384 groups,
    self-verifies scan [0], Group.Broadcast boundary [1], DIRECT shared boundary read [2]
    (the line-747 pattern).
  - `GridStrideScanStateDetectorTest` + `GridStrideScanStateDetectorKernel` — **256 groups**
    (was 16384 — TIMED OUT at 30s under FO76 and CASCADED into ObjectDisposed; RESIZED, now ~1s),
    64-iter in-loop scan, verifies per-iter scan [0] + loop-carried accumulator [1].
- `SpawnDev.ILGPU.Demo/UnitTests/WebGLTests.cs`: WebGL skip overrides for both.
- `SpawnDev.ILGPU/Wasm/Notes/residual-sort-race-2026-05-25.md`: SESSION 9 entry (top).

**FO76 14-24 run result (with detectors):** detector A clean; detector B (pre-resize) timed out +
cascaded; sorts failed with TIMEOUTS (SpawnSceneSim 240s) + ObjectDisposed cascade, NOT the 12-14
corruption. The contention timeouts MAY be a separate group-barrier-livelock-under-oversubscription
robustness issue (what SpawnScene users hit while multitasking) — worth its own look later.

## ENVIRONMENT / MECHANICS
- Git repo root: `D:\users\tj\Projects\SpawnDev.ILGPU\SpawnDev.ILGPU` (master). HEAD = `ca20808`
  "Wasm: harden broadcast sync and release 4.9.10". Working tree = local.13/14/15 + my detectors,
  all uncommitted. Version `4.9.10-local.15`. Build: `dotnet build SpawnDev.ILGPU/SpawnDev.ILGPU.csproj
  -c Release` (green, 0 errors; ~900 XML-doc warnings are pre-existing noise).
- PMT: `PMT_FILTER='<substring>' dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj -c Release`.
  **`PMT_FILTER` is a SUBSTRING match, NOT regex — `|` does NOT work.** `--filter` no longer scopes
  execution (parallel scheduler). PMT hard 30s/test timeout (240s for some). Pre-flight every run:
  `Get-Process testhost | ? { $_.Path -like '*SpawnDev.ILGPU*' }`.
- Disassembly: `wasm2wat --enable-threads file.wasm` (on PATH at
  `/c/Users/TJ/AppData/Roaming/npm/wasm2wat`). Scratch wat at `SpawnDev.ILGPU/_scratch_k6.wat`
  (delete when done — not needed).
- Dumps: TJ's runs write `_tj_dump_local/<ts>-latest.json` + `_tj_dump_local/<ts>/wasm/*.wasm`.
  Parse latest.json: `node -e "const d=JSON.parse(require('fs').readFileSync('F','utf8'));
  (d.tests||[]).filter(t=>t.result==='Error').forEach(t=>console.log(t.className+'.'+t.method,t.error))"`.

## OPEN TODO AFTER THE REPRO
1. Run the spin/data-read repro → engine-vs-pattern verdict.
2. Apply the right fix to scan boundary reads + RadixSortExtensions.cs:747.
3. Validate: detector + quiet `PMT_FILTER=WasmTests` + TJ FO76 2-concurrent (the real repro).
4. (Deferred) CHANGELOG entries for local.13/14/15; ML csproj bump local.12→local.15 + ML PMT.
5. (Deferred) full quality review of the 1050-line uncommitted local.13/14/15 Wasm diff before commit.
6. (Maybe) the FO76 contention-timeout / group-barrier-livelock robustness question.
