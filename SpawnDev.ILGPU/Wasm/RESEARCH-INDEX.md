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
5. **`<outer>\wasm-barrier-repro\`** — the PURE-NODE repro harness (no browser). `run-scan-test.mjs` etc. The cheap, controllable way to hunt without raping the machine.

---

## Current best understanding (synthesis, 2026-06-09)

**The residual = ONE logic race in the scan/broadcast KERNEL protocol — not the barrier wait mechanism, not memory ordering, not V8.**

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

## Consolidation recommendation (TJ, 2026-06-09)

1. **Version-control the outer-tree research.** `_research/`, `wasm-barrier-repro/`, `wasm-crossdispatch-repro/` are outside git. They are valuable + hand-built — move (or symlink) them under `<repo>\SpawnDev.ILGPU\Wasm\repro\` and `<repo>\Research\` so they survive and are reviewable. (Decision needed: keep the hand-written `.wasm` blobs or regenerate via `.wat`?)
2. **This index is the single entry point.** Link it from `Wasm/CLAUDE.md` so any agent lands here before re-treading.
3. **Retire/relabel SUPERSEDED docs** (the cooperative-scheduling-plan model; the V8-BUG-DRAFT; the contested wait/notify verdict) with a one-line banner pointing to the current understanding, rather than deleting (history matters).
