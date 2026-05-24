# Wasm wait/notify barrier STILL races on V8 — 2026-05-24

## Status: VERDICT REACHED. Spin-wait stays. wait/notify kept as a default-off re-test harness.

Author: Tuvok. Re-validating the rc.25/rc.27 decision to fall back from
`memory.atomic.wait32`/`notify` barriers to pure spin-wait in the Wasm backend.

## Question

The Wasm backend uses pure-spin generation barriers. April 2026 briefly shipped
wait/notify and then reverted it (rc.25/rc.27) because large sorts produced
non-deterministic corruption — but that April verdict rested on a repro that was
**never reproduced standalone** (pure-Node V8 couldn't trigger it), partial
comparator false-positives, and a believed V8 bug on April code + April Chrome.
Captain's question: does the non-determinism still reproduce on current code +
current Chrome, now that the backend has many fixes and V8 advanced? If wait/notify
works now, sleeping workers fix the CPU-oversubscription starvation pure-spin suffers.

## Method

Added `WasmBackend.UseWaitNotifyBarriers` (default false). When ON it converts the
**dispatcher** phase barrier (gen at `fenceBase+4`) and group barrier (gen at
`fenceBase+20`) in `GeneratePhaseDispatcher` (`WasmBackend.cs`) to:

- last worker: seq_cst `i32.atomic.store` gen+1, then `memory.atomic.notify(gen, int.MaxValue)`
- waiters: `Block { Loop { if load(gen) != savedGen br exit; wait32(gen, savedGen, 1ms); drop; br loop } }`
  (1ms self-healing timeout + spurious-wakeup defense; **no yield-to-JS**, since
  wait32 OS-parks the worker so spin-starvation can't occur)

### False start (caught by the dump)

First attempt toggled the **in-kernel** `EmitBarrier` path
(`WasmKernelFunctionGenerator.cs`). Phase-mode kernels (RadixSort/Scan/Reduce)
**bypass that path** — their barriers are yield points handled by the dispatcher.
So the toggle was dead code for the kernel under test and the first three "32/32
pass" runs were actually pure-spin in disguise. The Wasm dump (`wasm2wat
--enable-threads`) showed **zero** `wait32`/`notify` ops, which caught the false
positive before any victory was declared. Re-targeted to the dispatcher barriers.

### Verification gate

Confirmed wait/notify was genuinely live before trusting any result: the dump
`_dump/2026-05-24_19-12-57/wasm/000_kernel_4.wasm` disassembles to **2 `wait32`
+ 2 `notify`** — one pair at `offset=4` (phase barrier), one at `offset=20`
(group barrier). Only then did the canaries count.

## Result — wait/notify STILL races

Full `WasmTests` sweep with wait/notify confirmed live:

| Test | Scale | Result |
|------|-------|--------|
| `AlgorithmRadixSortNonPairsIntTest` | 32 elem, 1 group | PASS |
| `RadixSortThresholdProbeTest` | (small) | PASS (59s) |
| `RadixSortDescendingWithSentinelsTest` | 1.4M | **FAIL — 1067 sort-order violations** |
| `RadixSortRepeatedResortTest` | 500K | **FAIL — 187 sort-order violations** |
| `RadixSortHeavyDuplicateKeysTest` | 1M | **FAIL — value duplicates** |

**Small/single-group sorts pass; every large multi-group sort fails.** Three
different sort patterns broke in a single run — reliably triggered by scale (more
phases = more cross-worker barrier crossings), not a rare flake. Run aborted after
3 confirming failures to avoid burning cores on the 2M/4M/SpawnScene tail; the
verdict was already decisive. Failure map preserved at
`_dump/_waitnotify_run1_FAILMAP_190405.json`.

Compare: the April hypothesis predicted "346-1016 sort-order violations out of 1.4M
with wait32 present." We got 1067/1.4M. Match.

## Root cause — refined, and better than April's

1. **It is a memory-VISIBILITY failure, not a timeout-logic bug.** The
   spurious-wakeup defense re-checks the generation on every wake and only exits when
   the gen genuinely advanced, so a missed/late notify causes a re-loop (slowness),
   never an incorrect early exit. The order violations mean a woken worker proceeds
   (gen DID advance) but does not see the data writes that happened-before the gen
   bump.

2. **Our codegen is seq_cst-correct.** Last worker: writes → `atomic.fence` → seq_cst
   gen store → notify. Waiter: wakes → seq_cst `atomic.load(gen)` reads the advanced
   value, which synchronizes-with the gen store and must make all prior writes
   visible. Wasm atomics are seq_cst. So the violation is V8 not honoring that across
   its `wait32`/`notify` path — a V8 linear-memory wait/notify ordering bug
   (chromium#490434403 family).

3. **The April "275-local spill" theory is DISPROVEN.** April blamed the kernel
   function's ~275 locals (wait32 forcing a FutexEmulation C++ call that spills them).
   But the barrier lives in the **dispatcher** function (func 25 in the dump): 28
   params + 10 locals = **~38 locals**, and it still races. Reducing local count
   cannot dodge this. It is purely a V8 platform bug.

4. **wait/notify is also slower here**, not faster: `WithSentinels` ran 61s vs the
   ~17s spin-era baseline (workers timing out on the 1ms wait when notifies are
   effectively lost). The "sleeping barriers are faster" hope does not survive contact
   with large sorts.

## Decision

- **Spin-wait stays.** It is correctness routing around a V8 platform bug, not a
  Rule 1 compromise. Pure `atomic.load` spin never touches the buggy futex path.
- **`UseWaitNotifyBarriers` kept, default false, as a one-flip re-test harness.** When
  a future Chrome/V8 ships a FutexEmulation fix, flip it ON and re-run the WasmTests
  RadixSort canaries. If the large sorts pass, sleeping barriers become viable (fixes
  oversubscription starvation + core-burn) and we can promote it.
- In-kernel `EmitBarrier` toggle removed — the flag now controls **only** the
  dispatcher barriers (the path that actually matters). The in-kernel path stays
  pure-spin unconditionally.

## How to re-test (future V8)

1. `WasmBackend.UseWaitNotifyBarriers = true;` in the demo `Program.cs`.
2. `dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj --filter "FullyQualifiedName~WasmTests"`
3. Confirm wait/notify is live: `wasm2wat --enable-threads` the latest `_dump/<ts>/wasm/*_kernel_*.wasm`,
   grep for `atomic.wait32` / `atomic.notify` (expect 2 + 2 for a phase-mode kernel).
4. Watch the large sorts (Sentinels 1.4M, RepeatedResort 500K, HeavyDuplicate 1M).
   All pass = bug fixed upstream; promote. Any order-violation = still broken; keep spin.
5. REVERT the `Program.cs` toggle afterward.
