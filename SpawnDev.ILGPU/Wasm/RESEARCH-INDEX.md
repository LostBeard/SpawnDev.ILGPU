# Wasm Backend — Research Index (the residual large-sort race + notify/wait)

**Maintained by:** the SpawnDev crew. **Last updated:** 2026-06-09 (Geordi).
**Purpose:** one place to find ALL the scattered research + repro tooling on the Wasm multi-worker barrier backend — the residual large multi-group sort corruption, the fiber phase dispatcher, and the notify/wait-vs-pure-spin question. The corpus is riddled across two trees (inside the git repo AND in the outer working dir). This index locates every piece, flags what is CURRENT vs SUPERSEDED, gives a reading order, and states the current best understanding so we stop re-treading.

> **Path note.** Two roots:
> - **INNER (git-tracked):** `D:\users\tj\Projects\SpawnDev.ILGPU\SpawnDev.ILGPU\` — call it `<repo>`.
> - **OUTER (NOT git-tracked):** `D:\users\tj\Projects\SpawnDev.ILGPU\` — call it `<outer>`. ⚠ The `_research/`, `wasm-barrier-repro/`, `wasm-crossdispatch-repro/`, `barrier_repro/` folders live here and are **outside version control** — a consolidation candidate (see bottom).

---

## START HERE — reading order for the residual race

1. **`<repo>\Research\01-wasm-memory-model-and-atomics.md`** — GROUND TRUTH. The exact wasm threads memory model (Watt 2019). The five facts; why `atomic.fence` is a no-op in seqcst code; why the residual is a *logic race in our protocol*, not ordering and (most likely) not V8. **Read this first; it overrides older docs that contradict it.**
2. **`<repo>\SpawnDev.ILGPU\Wasm\CLAUDE.md`** — current backend state: pure-spin barriers, group-fence fix, kernelId fix, yield-escape, the standing "Barriers are PURE SPIN" verdict.
3. **`<repo>\SpawnDev.ILGPU\Wasm\Notes\residual-sort-race-2026-05-25.md`** — THE running investigation log (now ~1065 lines). Every attempt, what was ruled out, the statistical discipline. Includes the 2026-06-09 (Geordi) entries: H8 alloca-overlap ruled out by measurement; race localized to the counter-SCAN kernel's multi-tile boundary carry.
4. **`<repo>\Plans\wasm-waitnotify-still-races-2026-05-24.md`** — the notify/wait verdict (see RECONCILIATION below — its "V8 bug" conclusion is walked back by doc #1).
5. **`<repo>\SpawnDev.ILGPU\Wasm\repro\`** — the consolidated repro harnesses (moved here 2026-06-09 from the outer tree, now version-controlled). `wasm-barrier-repro/` (PURE-NODE, no browser), `wasm-crossdispatch-repro/`, `wasm-radix-repro/`, `wasm-scan-repro/`. **HISTORICAL** — the bug was solved by reading (SESSION 11), not these load harnesses. See `repro\README.md`.

---

## ✅ RESOLVED (2026-06-09, Geordi) — ROOT CAUSE FOUND BY READING, FIX IN

**The residual was a MISSING `Group.Barrier()` — an unguarded write-after-read on the REUSED scan/reduce shared-memory region across tile iterations.** Found by reading the protocol (TJ directive: no contention). Full write-up: `Notes\residual-sort-race-2026-05-25.md` **SESSION 11** (top of file).

- **Where:** `ILGPU.Algorithms/IL/ILGroupExtensions.cs` — `InclusiveScanImplementation` (and its sibling `AllReduce`) reuse ONE shared region every call. The previous tile's results are READ by all threads (`sharedMemory[0]/[Size-1]/[LinearIndex]`) after the final barrier, with NO barrier before the next tile overwrites those slots (`ScanExtensions.ComputeTileScan` loop). A lapping worker clobbers a boundary slot a lagging worker is still reading → wrong tile carry → wholesale misplaced-but-VALID output.
- **Why Wasm-only:** lockstep SIMT (CUDA/OpenCL/CPU) masks it; the Wasm MIMD-preemptible worker pool exposes it. Load only WIDENS the window — never required (so the years of FO76/contention sweeps were the wrong instrument; the gap is structural).
- **Why inclusive only:** `ExclusiveScanNextIteration`'s `Group.Broadcast` accidentally supplied a barrier; `InclusiveScanNextIteration` did not, and the primary `CreateRadixSort` uses `ScanKind.Inclusive` → corrupted.
- **Fix:** `Group.Barrier()` at the entry of both reused-region primitives (before they overwrite). All-6-backend, deadlock-safe by construction, build green. **Validation:** correct-by-construction; normal all-backend PMT regression sweep in flight; contention re-sweep optional (window-widener, not a discoverer). This OBSOLETES the contention-hunt tooling below (kept as historical).
- **Payoff (still valid):** with the kernel-protocol race fixed, wait/notify barriers should also pass large sorts → workers can PARK instead of pure-spin → no more core-pegging during any future Wasm work.

---

## Current best understanding (synthesis, 2026-06-09) — SUPERSEDED by the RESOLVED block above

**The residual = ONE logic race in the scan/broadcast KERNEL protocol — not the barrier wait mechanism, not memory ordering, not V8.** *(This localization was correct in spirit — it pointed at the counter-SCAN kernel — but mis-attributed the carry to `Group.Broadcast`; the actual gap was the missing barrier in the inclusive tile-reuse loop. See RESOLVED above.)*

- **Localized (2026-06-09):** the only barrier kernel in a Wasm RadixSort is the **counter-SCAN** (`SingleGroupScanKernel`, launched `(1, 256)` = one group). A large sort makes a large counter array scanned in **many tiles** by one group, carrying the running total across tiles via `Group.Broadcast` (`ScanExtensions.ComputeTileScan`). The race is the **multi-worker, multi-tile boundary carry**. Fits every survivor: needs ≥2 workers (1 worker = sequential tid loop, clean), large input (many tiles), "contiguous run shifted by an offset" (one tile's `leftBoundary` wrong).
- **RECONCILIATION — spin and wait/notify fail the SAME way:** `wasm-waitnotify-still-races-2026-05-24.md` blamed V8 (chromium#490434403). But the later ground-truth `Research\01-...md` (§4) explicitly walks that back: *"wait/notify still races... but that is also (most likely) OUR protocol, not V8 — treat 'V8 bug' as last resort."* Both barriers show the identical signature ("woken/advanced worker proceeds, gen DID advance, but doesn't see the writes that happened-before the bump"). **That signature is a kernel-protocol logic race, exposed under both barrier mechanisms** (wait/notify worse only because its parking timing widens the window).
- **THE PAYOFF:** if the residual is a kernel-protocol race independent of the barrier wait, then **fixing it makes BOTH spin and wait/notify pass large sorts → wait/notify becomes viable → workers PARK instead of spin → the core-pegging that makes the machine unusable during hunts is solved at the root, in the product.** This is the through-line connecting the bug to the resource problem.
- **The memory-model rule for the fix:** a `synchronizes-with` edge requires the reader to *read-from* the writer's seqcst store on the **same byte range**. The bug is "a reader proceeds before our protocol established that edge to the data it then reads." DO NOT add `atomic.fence` (a no-op for correctness; the 2026-05-25 group-fence "fix" was a timing artifact, ~50%→~12%, not a real fix). Fix the protocol logic.
- **Candidate (NOT yet confirmed):** the `Group.Broadcast` tag guard uses `tag = linear group index`, which is constant `0` for the single-group scan across all tiles — so it cannot distinguish tile N's slot contents from tile N-1's, relying entirely on the phase barrier. Whether the phase barrier already covers this (it should, by the model) or there is a real gap needs OBSERVATION, not more reading.

**Next step:** instrument the scan kernel's per-tile `leftBoundary` carry and OBSERVE the first wrong tile against the CPU oracle — using the pure-Node harness (controllable, no PC-rape) or a TJ-approved targeted contention run. Confirm whether the broadcast publish (or a tile-loop shared-region reuse sync point) is the gap.

---

## Full inventory

### `<repo>\Research\` — distilled ground truth (community-doc quality)
| Doc | Purpose | Status |
|-----|---------|--------|
| `00-README.md` | Index of the Research folder; purpose statement. | CURRENT |
| `01-wasm-memory-model-and-atomics.md` | **The authoritative wasm threads memory model** applied to our dispatcher. Five facts, fence-is-no-op proof, history of our two self-inflicted wrong turns. | **CURRENT / AUTHORITATIVE** |

### `<outer>\_research\` — external atomics research (Trip+TJ, 2026-05-29)
| Doc | Purpose | Status |
|-----|---------|--------|
| `README.md` | Why the folder exists; the two atomics layers (wasm module atomics + JS host Atomics). | CURRENT |
| `00-spawndev-wasm-backend-mapping.md` | Ties external atomics research to in-tree behavior (BuildWasmWorkerScript, dispatcher). | CURRENT |
| `01-official-specification.md` | Links to normative wasm-threads + JS Atomics specs. | REFERENCE |
| `02-chrome-v8-issues-and-spawndev-investigation.md` | V8 issue history + TJ's public `v8-atomics-wait-bug` repro/article. | REFERENCE (see history caveat in Research/01 §4) |
| `03-correct-synchronization-patterns.md` | Spec-aligned generation-barrier + mutex patterns (notify-by-index, looped wait). | CURRENT |
| `04-reference-repos-demos-tools.md` | External repos/demos/tools. | REFERENCE |
| `05-sharedarraybuffer-coop-coep.md` | COOP/COEP requirements for SAB. | REFERENCE |
| `06-wasm-atomic-instructions-quickref.md` | Opcode quick-ref (wait32/notify/fence bytes, RMW table). | REFERENCE |
| `07-cloned-repos-nuances.md` | Notes on cloned reference repos. | REFERENCE |
| `08-browser-async-vs-ilgpu-sync.md` | Browser-async vs ILGPU CUDA-like sync semantics (Tuvok S10). | CURRENT |
| `atomics-sync/`, `threads/`, `v8-atomics-wait-bug/` | Cloned example repos + worker-barrier samples. | REFERENCE |

### `<repo>\SpawnDev.ILGPU\Wasm\Notes\`
| Doc | Purpose | Status |
|-----|---------|--------|
| `residual-sort-race-2026-05-25.md` | **THE investigation log.** All attempts/eliminations + the 2026-06-09 scan-kernel localization. | **CURRENT / PRIMARY** |
| `fiber-refactor-implementation-notes.md` | How the fiber phase dispatcher was built (v4.6.0). | HISTORICAL/REFERENCE |
| `wasm-sharedarraybuffer-growth.md` + `-research.md` | Hypothesis: memory-growth propagation lag across the worker pool as the full-sweep trigger; localizes failure to Phase 2 (Group Scan + Broadcast) — consistent with the scan localization. | CANDIDATE TRIGGER (unconfirmed) |
| `tuvoks-session-tail.md` | Session handoff tail. | HISTORICAL |

### `<repo>\SpawnDev.ILGPU\Wasm\Plans\`
| Doc | Purpose | Status |
|-----|---------|--------|
| `multi-worker-barrier-dispatch.md` | Plan that re-enabled multi-worker barrier dispatch (v4.6.0, COMPLETE). | HISTORICAL (done) |
| `divergent-barrier-plan.md` | Future: conditional/divergent barrier counts per thread. | FUTURE |

### `<repo>\Notes\` (top-level) — mostly WebGPU, one Wasm
| Doc | Purpose | Status |
|-----|---------|--------|
| `Wasm-CrossGroup-Cooperative-Scheduling-Plan.md` | 2026-03-15 cross-group visibility plan. ⚠ Its premise ("atomic store doesn't publish prior non-atomic writes to other locations") is **SUPERSEDED** by Research/01 (it does, via sb⊆hb + the store→load sw edge). Read for history, not as a model. | **SUPERSEDED (model)** |
| `WebGPU-*.md`, `final-plan-a.md`, `half-support-*.md`, `rendering-to-canvas.md`, `release-v4.0.0.md` | WebGPU backend refactor/scan work + roadmaps. Not Wasm-race relevant. | REFERENCE (other lane) |

### `<repo>\Plans\` (top-level)
| Doc | Purpose | Status |
|-----|---------|--------|
| `wasm-waitnotify-still-races-2026-05-24.md` | notify/wait re-test verdict. Conclusion ("V8 bug") **partially walked back** by Research/01 §4 — see RECONCILIATION above. The re-test procedure (§How to re-test) is still valid. | CURRENT (procedure) / verdict CONTESTED |
| `wasm-radixsort-values-corrupted-2026-05-03.md` | Earlier corruption investigation. | HISTORICAL |
| `wasm-cold-start-vs-warm-pool-timing-2026-04-28.md` | Cold-start vs warm-pool timing. | HISTORICAL |
| `wasm-backend-stable-gates.md` | The 4.9.2 stable-cut gate list (Wasm-blocked items). | HISTORICAL/REFERENCE |
| `gethashcode-as-id-audit-2026-05-26.md` | kernelId-as-GetHashCode audit (the fixed cache-collision bug). | HISTORICAL (fixed) |
| `rc11-*/rc14-*/rc16-*` , `opencl-*`, `webgpu-*`, `pmt-*`, `spawndev-interop-audit`, `accelerator-requirements-*`, `f16-emulation-*`, `PLAN-*`, `trip-*/tuvok-*-handoff` | Assorted RC-era codegen bugs, session handoffs, other-lane plans. | HISTORICAL / other lane |

### Repro tooling (all `<outer>`, pure Node + browser)
| Tool | What it does | Status |
|------|--------------|--------|
| `wasm-barrier-repro\run-scan-test.mjs` (+ `scan-barrier-test.wasm/.wat`, `barrier-worker.js`) | **PURE-NODE worker_threads** harness running a hand-written scan-barrier model; A/Bs **spin vs wait32**; counts violations; fully parameterized (workers/threads/phases/rounds). The cheap, no-Chromium repro vehicle. ⚠ Models the PATTERN, not the real generated kernel — extend toward the real ComputeTileScan/broadcast logic. | **PRIMARY REPRO TOOL** |
| `wasm-barrier-repro\run-{test,fiber,call,multi-helper,big-module}.mjs` + matching `.wasm/.wat` | Node runners for fiber/call/multi-helper/big-module barrier variants. | REPRO TOOLBOX |
| `wasm-barrier-repro\V8-BUG-DRAFT.md` | DRAFT V8 report (the "wait32 corrupts 275-local functions" theory — later DISPROVEN, dispatcher has ~38 locals and still raced). Unfiled. | SUPERSEDED |
| `wasm-crossdispatch-repro\` (`server.mjs`+`worker.js`+`main.js`+`RESULTS.md`) | 2026-06-08 (Geordi) micro-repro: does `postMessage` carry happens-before for non-atomic SAB writes? RESULT: yes (2M handoffs, 0 stale) → cross-dispatch postMessage visibility RULED OUT. | DONE (negative result) |
| `barrier_repro\` (`<outer>`) | (empty / scratch) | — |
| `<repo>\SpawnDev.ILGPU.DemoConsole\WasmCompileDump.cs` (`-- wasm-dump`) | OFFLINE desktop compile dump: emits a kernel's shared-mem alloca table + per-kernel info, no browser. Used 2026-06-09 to rule out H8 + attribute the scan kernel. | CURRENT TOOL |

---

## Consolidation status (started TJ 2026-06-09; executed Geordi 2026-06-09 after root cause)

1. ✅ **Repro harnesses version-controlled.** `wasm-barrier-repro/`, `wasm-crossdispatch-repro/`, `wasm-radix-repro/`, `wasm-scan-repro/` MOVED from the un-tracked outer tree into `<repo>\SpawnDev.ILGPU\Wasm\repro\` (+ a README marking them HISTORICAL, bug solved by reading). Hand-written `.wasm` blobs kept (small; the `.wat` sources sit alongside). **Still outer + un-tracked:** `_research/` (48M of cloned external reference repos — NOT pulled into the library git; regenerable from the URLs documented in `<repo>\Research\01-...md` and `<outer>\_research\01-official-specification.md`) and the transient PMT dump folders (`_dump/`, `_tj_dump_local*/`, `_ilgpudump/`, `_mldump/` — outputs, gitignored, not research).
2. ✅ **This index is the single entry point.** Linked from `Wasm/CLAUDE.md` (top RESOLVED banner points here).
3. ✅ **Root cause documented in canonical docs** — `Notes/residual-sort-race-2026-05-25.md` SESSION 11, this index's RESOLVED block, `Wasm/CLAUDE.md` (top banner + residual paragraph). Superseded synthesis/verdicts relabeled in place (history kept).

---

## Also in the corpus — Wasm design + notes scattered in other folders (added 2026-06-10, Geordi)

TJ flagged that Wasm research is spread across 6+ folders. The docs above cover the residual-race
investigation; these are the remaining Wasm-backend DESIGN + implementation docs, by folder, so the
map is complete:

| File | What | Status |
|------|------|--------|
| `<repo>\SpawnDev.ILGPU\Wasm\Plans\multi-worker-barrier-dispatch.md` | The multi-worker / fiber phase-dispatch design (origin of the in-Wasm phase dispatcher the residual lives in). | DESIGN (implemented) |
| `<repo>\SpawnDev.ILGPU\Wasm\Plans\divergent-barrier-plan.md` | Divergent-barrier handling design. | DESIGN |
| `<repo>\SpawnDev.ILGPU\Wasm\Notes\fiber-refactor-implementation-notes.md` | Fiber refactor implementation notes (the phase/yield state-save machinery — directly relevant to the barrier-at-helper-boundary codegen bug). | IMPLEMENTATION NOTES |
| `<repo>\SpawnDev.ILGPU\Wasm\Notes\wasm-sharedarraybuffer-growth.md` + `wasm-sharedarraybuffer-growth-research.md` | SharedArrayBuffer `memory.grow()` behavior + research (ruled out as a residual cause, see SESSION log). | RESEARCH (ruled out) |
| `<repo>\SpawnDev.ILGPU\Wasm\Notes\tuvoks-session-tail.md` | A prior session tail (Tuvok). | HISTORICAL |
| `<repo>\Notes\Wasm-CrossGroup-Cooperative-Scheduling-Plan.md` | Cross-group cooperative scheduling design for the multi-worker pool. | DESIGN |
| `<repo>\Research\00-README.md` | Entry note for the `Research/` ground-truth folder (memory model). | REFERENCE |

**For the active problem (barrier-at-the-scan-helper-phase-boundary codegen bug):** start with
`Wasm\Notes\fiber-refactor-implementation-notes.md` + `Wasm\Plans\multi-worker-barrier-dispatch.md`
(the phase/yield state machinery), then the SESSION 11/11b entries in `residual-sort-race-2026-05-25.md`,
then the geordi-to-seven handoff `_DevComms/global/geordi-to-seven-wasm-backend-is-yours-consolidated-findings-2026-06-10.md`.
