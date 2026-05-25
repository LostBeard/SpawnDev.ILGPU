# WebAssembly Threads Memory Model & Atomics — the rules that govern the Wasm fiber backend

> **Why this doc exists.** SpawnDev.ILGPU's Wasm backend runs barrier kernels (RadixSort, Scan, Reduce) across `hardwareConcurrency` Web Workers sharing one `SharedArrayBuffer`, using a **fiber-based phase dispatcher** with pure-spin generation barriers. You cannot reason about that dispatcher correctly without knowing the *exact* WebAssembly threads memory model — and that model is **not** C++'s. This document is the ground truth. Read it before touching `WasmBackend.GeneratePhaseDispatcher`, `WasmKernelFunctionGenerator` phase-splitting, or any atomic/barrier code. Getting this wrong has already cost us a wrong-spec bug report (we blamed TC39 #3800, syg schooled us) and a no-op "fix" (a release fence that did nothing but perturb timing).

## TL;DR — the five facts you must hold in your head

1. **All WebAssembly atomic accesses are sequentially consistent (seqcst).** There is no relaxed/acquire/release variant in wasm. `i32.atomic.load`, `i32.atomic.store`, `i32.atomic.rmw.*` are all seqcst.
2. **`sequenced-before ⊆ happens-before`.** Program order within a single thread orders **every** access — atomic *and* non-atomic — in happens-before. (Watt 2019 formal axioms.)
3. **The only cross-thread happens-before edge for memory is `synchronizes-with`, defined as: `W synchronizes-with R` iff both are seqcst, they affect *equal byte ranges* (same location/width), and `R` reads-from `W`.** A read-modify-write participates as both, so RMWs on the same location chain transitively.
4. **`atomic.fence` is semantically a no-op when your code already synchronizes through seqcst atomics.** Because wasm atomics cannot be reordered with respect to non-atomic accesses (stronger than C++), a seqcst store already cannot move above the non-atomic writes that precede it in program order, and the store→load synchronizes-with edge already publishes them. The C++→wasm toolchain compiles `std::atomic_thread_fence` to **zero** wasm instructions for exactly this reason.
5. **Non-atomic data races in wasm are NOT undefined behavior** (unlike C++). A racing read observes some *defined but non-deterministic* value (possibly torn for >1-byte non-atomic, never torn for seqcst). So a sync bug here is silent wrong data, not a trap.

**Operational corollary:** if a Wasm barrier kernel produces intermittent wrong results, **do not reach for a fence.** The bug is a *logic race* in our protocol — a place where a worker reads shared data before our protocol has established the `synchronizes-with` edge that would put the writer's write happens-before the reader's read. Fix the protocol (an actual barrier / sync point / counter logic), not the memory ordering.

---

## 1. The formal model (Conrad Watt, "Weakening WebAssembly", OOPSLA 2019)

The WebAssembly threads memory model is the JavaScript `SharedArrayBuffer` model, formalized by Watt, Rossberg & Pichon-Pharabod. The relevant axioms (paraphrased from the paper's formal section, with the exact predicates):

- An execution is a set of memory **actions** related by **reads-from (rf)**, **synchronizes-with (sw)**, **happens-before (hb)**, and a total order **tot** (the JS analogue of C++ `sc`).
- **`happens-before` is a strict partial order.** `sequenced-before` (program order within a thread) and `synchronizes-with` are **subsets of happens-before**, and happens-before is consistent with `tot`.
- **`synchronizes-with`:** `W synchronizes-with R` **iff both `R` and `W` are seqcst, and they affect equal byte ranges** (and `R` reads-from `W`). — This is the *only* mechanism that creates a happens-before edge **between** threads via memory.
- **Read consistency** (which write a read `R` may observe):
  - (1) It is not the case that `R` happens-before `W` (no reading from the future).
  - (2) There is no `W'` with `W happens-before W' happens-before R` and `W'` writes the same location (no reading something already overwritten in hb).
  - plus `tot`-based conditions for seqcst reads.
- **No-tear:** all seqcst accesses are tear-free (observed indivisibly). Non-atomic multi-byte accesses may tear under a race.
- **Non-atomic (unordered) races are defined:** "reads participating in or overlapping with the location of a data race may non-deterministically observe a number of different values." There is **no UB** (this is the key divergence from C++, where such a race is instant UB).

### Why this means fences are redundant in seqcst code

Take the canonical publish pattern (one thread writes data then sets a flag; another reads the flag then the data):

```
Producer thread A:                Consumer thread B:
  data = ...        (non-atomic)    g = atomic.load(flag)   (seqcst)
  atomic.store(flag, 1) (seqcst)    if (g == 1) use(data)   (non-atomic)
```

Happens-before chain that publishes `data`:

```
data-write(A)  --sequenced-before-->  flag-store(A)        (sb ⊆ hb; program order; NO fence needed,
                                                            and the seqcst store can't be reordered
                                                            above the non-atomic write)
flag-store(A)  --synchronizes-with-->  flag-load(B)         (both seqcst, same byte range, rf)
flag-load(B)   --sequenced-before-->   data-read(B)         (sb ⊆ hb)
∴ data-write(A) happens-before data-read(B)                (hb transitive) → B observes A's data.
```

No `atomic.fence` appears anywhere and none is needed. This is **provably** sufficient under the wasm model. An `atomic.fence` between `data-write` and `flag-store` would add nothing to happens-before — it would only emit a hardware fence that perturbs the VM's instruction scheduling/timing.

### Primary sources

- Conrad Watt, Andreas Rossberg, Jean Pichon-Pharabod. *Weakening WebAssembly* (Extended). OOPSLA 2019. <https://conrad-watt.github.io/papers/watt2019.pdf>
- WebAssembly threads proposal Overview (memory model section): <https://github.com/WebAssembly/threads/blob/main/proposals/threads/Overview.md>
- Formal relaxed-memory spec draft: <https://webassembly.github.io/threads/core/exec/relaxed.html>
- **WebAssembly/tool-conventions #59 "Atomic fence support"** — the spec + toolchain authors (Conrad Watt, aheejin, dschuff, sunfishcode, jfbastien, lars-t-hansen) deciding how to lower C++ fences to wasm. The decisive quotes:
  - aheejin: *"unlike in C++, atomic operations cannot be reordered with respect to non-atomic operations in wasm."*
  - Conrad Watt: *"Wasm definitely has stricter rules for atomic + non-atomic ordering."* and *"Since all same-location Wasm atomic actions Y reads-from X create a synchronize edge, ... if the compilation scheme turns all C++ atomics into Wasm SeqCst atomics, memory fences can be no-ops."*
  - jfbastien: *"the WebAssembly memory model is stronger than C++'s and LLVM's."*
  - Net result: C++ `std::atomic_thread_fence` → **zero** wasm instructions.
  <https://github.com/WebAssembly/tool-conventions/issues/59>

> **Note on `atomic.fence` existing at all.** wasm *does* have an `atomic.fence` opcode (`0xFE 0x03`). It exists to preserve the ordering of higher-level-language fences **relative to other non-atomic accesses in code that does *not* otherwise use seqcst atomics** (and to stop the wasm *producer* from reordering across it). In a dispatcher that already synchronizes via seqcst gen counters and seqcst arrival RMWs, it is inert. Don't add it expecting a correctness change.

---

## 2. How this maps onto our fiber phase dispatcher

`WasmBackend.GeneratePhaseDispatcher` implements a **generation barrier** (a.k.a. sense-reversing barrier using a monotonic generation counter) for both the **phase barrier** (between barrier-separated code regions of a kernel) and the **group barrier** (between workgroups, since the dispatcher iterates groups sequentially in a `g` loop). Layout of the fence region (`fenceBase`/`fenceSlot`):

| offset | field | accessed via |
|-------:|-------|--------------|
| 0 | phase arrival counter | `i32.atomic.rmw.add` / reset `i32.atomic.store` |
| 4 | phase generation | `i32.atomic.store` (producer) / `i32.atomic.load` (waiters) |
| 8 | global yield count (kernel phase-completion) | `i32.atomic.rmw.add` / reset |
| 12 | exit flag | `i32.atomic.store` / `i32.atomic.load` |
| 16 | group arrival counter | `i32.atomic.rmw.add` / reset |
| 20 | group generation | `i32.atomic.store` / `i32.atomic.load` |

### Why the barrier protocol is correct *by the model* (no fences required)

Publishing one worker's non-atomic kernel data writes to the other workers after the barrier:

```
Wk (non-last): data-write           (non-atomic)
            --sb--> arrival-RMW(Wk)  (seqcst, offset 0/16)
            --sw (RMW modification-order chain on the SAME counter)-->
                arrival-RMW(L)       (L = last worker; reads the value the chain produced)
            --sb--> gen-store(L)      (seqcst, offset 4/20)
            --sw (same gen location, rf)--> gen-load(waiter)   (seqcst)
            --sb--> data-read(waiter) (non-atomic)
∴ data-write(Wk) happens-before data-read(any waiter).
```

Every edge is either `sequenced-before` (program order, includes non-atomics) or `synchronizes-with` (two seqcst accesses, same location, reads-from). **No `atomic.fence` is part of this chain.** The RMW chain on the arrival counter is what carries each worker's writes into the last worker's view, and the gen store/load is what carries the last worker's (now-merged) view to the waiters.

Generation-skipping ("ABA") is prevented by the arrival count: the gen cannot advance to G+2 until *all* workers (including any that yielded back to JS mid-spin) have arrived for the G+1 phase, because the arrival counter cannot reach `workerCount` while one worker is still parked. So a slow/parked worker always observes exactly `savedGen+1`, never skips.

### Consequence for debugging

The dispatcher's existing `atomic.fence` instructions (in the phase producer before the gen store, after the exit-flag store, and the post-barrier "acquire" fence) are **semantic no-ops** under the wasm model — almost certainly cargo-culted from C++ release/acquire intuition. They are harmless but they are **not** load-bearing, and **adding more of them is not a fix.** If a barrier kernel races, the defect is in the *logic* (counter handling, last-worker detection, a missing phase-split/sync-point between two shared-memory regions in the kernel, a helper-call sync gap — see `WasmKernelFunctionGenerator` `_needsSyncYields` / post-helper sync), not in memory ordering.

---

## 3. Rules for writing/auditing atomic code in this backend

1. **Synchronize through seqcst atomics on a shared location, never through fences.** To publish data, write data (non-atomic) then `atomic.store` a flag; to consume, `atomic.load` the flag then read data. The flag store/load must be the **same byte range** on both sides — that equality is *required* for `synchronizes-with`.
2. **Never "fix" an intermittent wasm barrier/sort bug by adding `atomic.fence`.** It is a no-op for correctness and only changes timing — a band-aid that hides the real logic race and yields false confidence. (We did exactly this once; see the history note below.)
3. **A "barrier" is a sync *point* (the generation barrier / a phase split), not a memory fence.** The POST-HELPER BARRIER tribal rule (`Wasm/CLAUDE.md`) is about inserting an actual sync point (phase yield) after a barrier-using helper so a fast worker can't start the next helper while a slow worker is still reading the previous helper's shared output. That is logic, not ordering.
4. **RMWs on the same location chain transitively** — rely on that for arrival counters; don't assume an RMW on counter X publishes writes to a reader that only ever reads counter Y.
5. **Non-atomic multi-byte writes can tear under a race; seqcst cannot.** If two workers might touch the same word non-atomically (e.g. sub-word stores), use atomic RMW (this is already why WebGPU sub-word stores use atomicAnd/atomicOr).
6. **Verify the `synchronizes-with` edge actually exists at the moment of the read.** Most real bugs are "the reader read before the writer's flag store was observable in the protocol's logic," i.e. the protocol let the reader proceed too early — not a missing fence.

---

## 4. History / cautionary tales (so we don't repeat them)

- **2026-04-27 — blamed the spec, got schooled.** We filed a HIGHEST-PRIORITY report claiming TC39 ecma262 #3800 was the root cause of a wait/notify barrier race. Shu-yu Guo (`@syg`, spec editor + JSC engineer) explained the bug was in *our* barrier code (notify wakes by **index**, not value; the looped-spin wait is the prescribed mitigation and we already had it). Retracted with apology. **syg / Conrad Watt are ground truth for atomics; our own READMEs can be wrong.**
- **2026-05-25 — the no-op "fix".** While chasing intermittent large multi-group RadixSort corruption (±1 adjacent-value errors on heavy-duplicate keys, ~1.6%/sort), we noticed the pure-spin *group* barrier producer bumped its generation without a release `atomic.fence` before it, while the *phase* producer had one. We "fixed" it by mirroring the fence. Observed corruption dropped (~50%→~12% per run; magnitude 427-1047→≤9) — but per this document, **that fence is a no-op**; the improvement was a pure timing artifact (the hardware fence narrowed the race window). The real bug — a logic race — remained. **Lesson: a rate change from adding a fence is timing, not correctness. Keep hunting the logic race.**
- **wait/notify still races on V8 for our barrier — but that is also (most likely) our protocol, not V8.** We keep barriers PURE SPIN with a yield-to-JS escape; `WasmBackend.UseWaitNotifyBarriers` is a default-off re-test harness. See `Wasm/CLAUDE.md` "Barriers are PURE SPIN" and `Plans/wasm-waitnotify-still-races-2026-05-24.md`. Per the don't-blame-external rule, treat any "V8 bug" hypothesis as the last resort after exhausting our own protocol, with a minimal reproduction filed upstream — never as a shipping excuse.

---

*Maintained by the SpawnDev crew. If you change the Wasm barrier/dispatch atomics, update this doc. Ground-truth sources are linked in §1 — read them, don't paraphrase from memory.*
