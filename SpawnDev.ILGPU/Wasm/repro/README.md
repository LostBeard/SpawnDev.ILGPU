# Wasm residual-race repro harnesses (HISTORICAL)

These pure-Node + browser repro harnesses were hand-built during the multi-session hunt for the
**residual Wasm large-sort race**. They were consolidated here (from the un-tracked outer working
tree `D:\users\tj\Projects\SpawnDev.ILGPU\`) on 2026-06-09 so they survive in version control.

> **The bug is SOLVED.** It was a missing `Group.Barrier()` — an unguarded write-after-read on the
> reused scan/reduce shared-memory region across tile iterations in
> `ILGPU.Algorithms/IL/ILGroupExtensions.cs` (`InclusiveScanImplementation` + `AllReduce`). It was
> found by **reading the protocol, not by these load harnesses** (load only widens the window; it was
> never required). See `../Notes/residual-sort-race-2026-05-25.md` **SESSION 11** and
> `../RESEARCH-INDEX.md` (RESOLVED banner) for the full write-up.
>
> These harnesses are kept for **reference / regression-model value**, not as the active hunt vehicle.
> They model the barrier *pattern*; they do NOT run the real generated kernel.

## Contents

| Folder | What it does |
|--------|--------------|
| `wasm-barrier-repro/` | The PRIMARY pure-Node `worker_threads` harness. Hand-written scan-barrier model; A/Bs **spin vs wait32**; counts violations; parameterized (workers/threads/phases/rounds). Also fiber/call/multi-helper/big-module barrier variants (`run-*.mjs` + matching `.wasm`/`.wat`). ⚠ Models the PATTERN, not the real ComputeTileScan/broadcast kernel. |
| `wasm-crossdispatch-repro/` | 2026-06-08 micro-repro: does `postMessage` carry happens-before for non-atomic SAB writes? RESULT: yes (2M handoffs, 0 stale) → cross-dispatch postMessage visibility RULED OUT. |
| `wasm-radix-repro/` | Radix-specific repro staging (real radix kernels pulled from `_dump`, pass1 dispatch decode). |
| `wasm-scan-repro/` | Scan-kernel-focused repro staging (the localized counter-scan multi-tile boundary carry). |

## ⚠ Resource note

`wasm-barrier-repro` spawns a `worker_threads` pool. Running it with worker count >> cores
oversubscribes and pegs every core. **Per TJ's standing directive: never run these unannounced, and
the residual race is now solved by reading — there is no reason to run a contention sweep for it.**
If ever re-run for regression modeling, cap workers at/below core count and announce first.
