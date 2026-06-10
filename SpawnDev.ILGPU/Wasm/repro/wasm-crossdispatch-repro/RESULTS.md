# Cross-dispatch SAB-visibility micro-repro — RESULT (2026-06-08, Geordi)

## Question
The residual Wasm multi-group RadixSort race is **inter-worker** (workerCount=1 is 100% clean) and
**cross-group** (only large multi-group sorts fail). Radix coordinates across groups via a global
`counter[]` written by Kernel1 and read by the scan/scatter — as **separate Wasm dispatches**,
handed off via `postMessage` (worker posts `{done}` with NO release fence at WasmAccelerator.cs:2107;
next dispatch started via `postMessage` with no acquire). The corpus
(`Wasm/Notes/residual-sort-race-2026-05-25.md`) argued this boundary away but **never hardware-tested
it** (the Session 10 "18M reads, 0 stale" repro was *intra-dispatch* only).

**Hypothesis under test:** `postMessage` does NOT establish happens-before for non-atomic
SharedArrayBuffer writes, so dispatch N+1's workers can read STALE `counter[]` entries written by
dispatch N's workers → misplaced-valid block displacement (the measured `gpu[i]=cpu[i+1]` signature).

## Method
Minimal harness (this dir): worker A writes a 256 KB SAB region NON-ATOMICALLY → posts `{done}` to
main (no fence) → main `postMessage`s a **different** worker B → B reads the region and counts slots
that don't show the fresh epoch. Region never zeroed between iters, so a stale slot shows the prior
epoch. Pool of 4 workers, writer/reader always distinct. Run on **real Chrome** (`headless=new`,
same V8) **under Fallout 76 contention**.

## Result
| condition | iterations | slot reads (~) | staleIters | staleSlots |
|---|---|---|---|---|
| no load (smoke) | 108,608 / 12s | ~7.1 B | **0** | **0** |
| **under FO76, ~3.75 min** | **2,011,022** | **~131 B** | **0** | **0** |

## Conclusion
**Hypothesis REFUTED by hardware.** `postMessage` *does* carry happens-before for non-atomic
SharedArrayBuffer writes on real Chrome under heavy contention. The cross-dispatch dispatch-boundary
handoff is **clean** — it is NOT the residual race. The last open seam in the corpus's coverage is
now closed by measurement, not argument.

**Corollary (important):** the `Wasm-CrossGroup-Cooperative-Scheduling-Plan.md` stakes its entire
correctness on exactly this assumption ("the postMessage chain guarantees visibility of ALL writes,
atomic and non-atomic ... no additional fencing needed"). **That assumption is now hardware-VALIDATED.**
The coop-scheduling plan is sound on the visibility axis.

## Where that leaves the hunt
Ruled out this session: group-barrier fence (A/B, no rate change), kernel logic (single-worker clean),
cross-dispatch postMessage visibility (this repro). Remaining un-audited corpus suspect: **H8 —
shared-memory alloca slot lifetime / overlap** (two distinct allocas colliding on an offset; fiber
re-entry clobbering a slot), flagged as a top redirect suspect in residual-sort-race-2026-05-25.md
but never fully audited.
