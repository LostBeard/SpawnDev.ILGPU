# DRAFT: V8 upstream bug report - wasm atomic stores silently fail to land under CPU oversubscription (worker threads + SharedArrayBuffer)

Status: DRAFT for Captain review before filing (crbug / v8.dev). Prepared by Seven, 2026-06-11.
All evidence gathered on: Node v24.15.0 (V8 13.6), win32 x64, AMD Ryzen 5 7500F (6C/12T).
Reproduces in BOTH tiers (forced `--no-wasm-tier-up` Liftoff-only AND `--no-liftoff` TurboFan-only).

## Summary
In a multi-`worker_threads` wasm program sharing one `WebAssembly.Memory {shared: true}`,
under CPU oversubscription (workers >> cores, with `Atomics.wait` parking between work
phases), a **seq_cst `i32.atomic.store` occasionally never becomes visible in linear
memory** - not to other threads AND not to the storing thread itself:

- An `i32.atomic.load` of the same address, executed by the SAME thread a few
  instructions later (no intervening synchronization), returns the PREVIOUS value.
- The location remains at the previous value indefinitely (verified seconds later from
  the main thread after `Promise.all` settles).
- In a pair of back-to-back atomic stores to adjacent addresses (a struct copy:
  base+0 then base+4), we have instrumented events where the FIRST store landed and the
  SECOND vanished, and events where BOTH vanished - "a window of consecutive stores".
- Atomic RMW operations (`i32.atomic.rmw.add`) on the same memory NEVER exhibited the
  failure across all instrumentation (per-address RMW-based event counters stayed exact
  on every affected location, and an RMW(+0) read-back reliably detects the missing
  store - which is the basis of our application-level workaround).

Frequency: roughly 1 store in ~10^7-10^8 under 4x oversubscription (48 workers, 12
hardware threads); strongly correlated with the worker having recently crossed a
JS<->wasm boundary / `Atomics.wait` park-wake. Zero occurrences at <=cores... (note: we
have ALSO observed corruption at exactly-cores configurations - 1 event in 30 runs at 12
workers on 12 threads.)

## Repro
Standalone pure-Node repro (no frameworks): a real compiled kernel + driver that
replicates our dispatch loop is in
`SpawnDev.ILGPU/Wasm/repro/wasm-scan-repro/` (https://github.com/LostBeard/SpawnDev.ILGPU):
- `run-real-scan.mjs 16384 48 120` - fails 7-15/120 rounds on the pre-fix kernel
  (`00_kernel_1.wasm`, committed), each failure = exactly one fiber's carry one
  publication behind.
- `patch-debug-ring.mjs` builds an instrumented variant: per-thread ring records the
  store's input value, an immediate read-back of the destination, and the consuming
  read - the read-back shows the OLD value immediately after the store instruction in
  every captured event (ring1b "outBack" field).
- `patch-pub-timing.mjs` (DBG_KERNEL=3) stamps publication/consumption with a live
  generation counter: the store instruction provably executed at the expected point in
  the schedule, with the correct input value, and the destination still holds the old
  value at +2 phase generations.
- Synthetic minimal candidates (`wasm-store-vanish-repro/`: bare store windows + barrier
  protocol, no big kernel) do NOT reproduce - the failure appears to need substantial
  generated-code context around the store site.

## What we ruled out (instrumented)
- Our own memory layout/aliasing: every store site instrumented with per-address writer
  stamps - zero foreign writers, exact counts, owner-only access.
- Barrier protocol bugs: the inter-worker sense barrier was hardened to RMW-confirmed
  crossings; failures persisted until the STORE side was fixed.
- Read-side staleness: converting loads (even ALL loads) to RMW(+0) reads does not
  prevent the corruption - consistent with the store never landing rather than reads
  lagging.
- `Atomics.wait` itself: a busy-spin (NO_PARK) variant that never calls wait corrupts
  at a similar rate; both variants share JS<->wasm re-entry.

## Workaround (shipped in our compiler)
Every atomic store in multi-worker kernels is emitted as
`store -> rmw.add(addr, 0) read-back -> compare -> retry`. The RMW read-back is
essential (a plain atomic-load read-back was once observed to pass while the store
still did not land - presumed store-forwarding). With this, our stress gate went from
7-15/120 failing rounds to 0/360.

Possibly related: crbug 490434403 (FutexEmulation notify race) - same general area
(parked-worker wake paths interacting with wasm shared-memory accesses).

## Ask
Confirmation whether this is a known defect class in the wasm shared-memory
implementation around park/wake (store buffer drain on deschedule?), and whether a
smaller targeted repro would help - we can iterate on the instrumented harness.
