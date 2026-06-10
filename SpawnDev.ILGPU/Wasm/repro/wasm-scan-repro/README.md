# Wasm real-kernel scan repro + instrumentation (Seven, 2026-06-09/10)

Pure-Node repro of the REAL emitted `SingleGroupScanKernel` (the kernel RadixSort uses for
its counter scan) over `worker_threads` + a shared `SharedArrayBuffer`, replicating
`WasmAccelerator`'s barrier dispatch. This is the standalone reproduction of the Wasm residual
large-sort anomaly and the **upstream-reportable payload** (fires 7-11/120 @48 workers; the
synthetic minimal variants in `../wasm-store-vanish-repro/` do NOT fire).

## Source of truth (committed)
- `00_kernel_1.wasm` + `manifest.json` - the emitted kernel + its layout metadata. Re-emit with
  `dotnet run --project SpawnDev.ILGPU.DemoConsole -- scan-emit <thisDir>`.
- `run-real-scan.mjs` - driver. Reads layout LIVE from `manifest.json` (NEVER hardcode -
  a stale 2376-vs-2392 stride once caused a self-inflicted 16-byte overlap). Has yield-log,
  fiber-state, ring, writer-stamp, and pattern-analysis instrumentation; `NO_PARK=1` and tier
  flags pass through.
- `patch-debug-ring.mjs` - rebuilds `00_kernel_1_dbg.wasm`: per-tid debug rings bracketing the
  boundary publication (rb at read / temp / out-param / consumption). `DBG_KERNEL=1`.
- `patch-writer-stamps.mjs` - rebuilds `00_kernel_1_dbg2.wasm`: wraps all 1,605 store sites with
  a per-address writer stamp on the boundaries region - the ALIASING DISCRIMINATOR (proves
  owner-only writers; zero foreign writers). `DBG_KERNEL=2`; emits `sitemap.json` (siteId->WAT line).

Generated `*_dbg*.wasm/.wat` + `sitemap.json` are gitignored - regenerate with the patchers.

## Run
```bash
node run-real-scan.mjs 16384 48 120                       # raw: N workers rounds (no offsets needed)
node patch-debug-ring.mjs && DBG_KERNEL=1 node run-real-scan.mjs 16384 48 120   # ring evidence
node patch-writer-stamps.mjs && DBG_KERNEL=2 node run-real-scan.mjs 16384 48 120 # aliasing discriminator
STAMP_CHECK=1 DBG_KERNEL=2 node run-real-scan.mjs 16384 4 2                       # stamp positive control
NO_PARK=1 node run-real-scan.mjs 16384 48 120                                     # Atomics.wait exoneration
```
Input is per-tile PSEUDORANDOM so a stale consumed value fingerprints WHICH tile it held
(linear input collapses every event to delta -GROUP_SIZE - the Captain's "always -256" tell).

> ⚠️ 48 workers oversubscribes (pegs cores). Announce + get the Captain's go before running.

## VERDICT (FINAL - KILLED 2026-06-10/11, commits `b0dfc5c`/`b6c558a`)
Root cause: **V8 atomic stores in this workload can silently fail to LAND** under CPU
oversubscription - the boundaries out-param copy was the ring-proven victim (ring1b: the
store instruction executed with the correct value; the immediate same-thread read-back
returned the previous value; in paired-field events the left store landed while the right
vanished = "a window of consecutive stores vanishes"). Fires in BOTH V8 tiers; victim is
the worker's last tid's publication (the last store of the phase). Aliasing REFUTED
(writer stamps: 1,605 sites, zero foreign writers, exact counts). Read-side staleness
models FALSIFIED (RMW-ifying loads doesn't help - nothing can read a store that never
landed). **Fix: `EmitVerifiedAtomicStore`** - every atomic store = store -> RMW(+0)
read-back -> retry (the read-back must be RMW; a load read-back can be store-forwarded
while the store still doesn't land) + RMW-confirmed dispatcher sense barriers.
**This harness: 7-15/120 failing rounds pre-fix -> 0/120 x3 consecutive post-fix.**
The committed `00_kernel_1.wasm` is the PRE-FIX kernel (preserved as the failing baseline
+ upstream-report payload); re-emit via `scan-emit` for a current-build kernel.
Full trail: `_DevComms/global/seven-*-2026-06-10.md`;
upstream draft: `../../Notes/v8-atomic-store-vanish-upstream-report-draft.md`.

## What this repro does and does NOT validate
This repro exercises the **multi-tile `ComputeTileScan` path, which is DEAD on production Wasm**
(`CreateScan` hybrid routes >256 elements to `CreateWebGPUMultiPassScan`; see
`seven-CORRECTION-fix-target-wasm-local-*`). It validates the vanishing-store MECHANISM and is
the upstream-reportable payload - it is NOT the production-residual gate.
- **Item 2** (Wasm radix no-boundaries single-value scan, the production fix) does NOT change
  this repro's path: 8/120 unchanged after item 2 is CORRECT (no-regression check, not a failure).
  Item 2's true closure gate = **radix PMT under oversubscription**
  (Sentinels / OddCount / SpawnSceneSimulation).
- **Item 1** (ScanTile - folds the carry in-phase, removes the struct out-param publication this
  repro brackets) is what would take this repro to **0/120** (x3 runs, after `scan-emit` re-emit;
  the DBG patchers re-read offsets from the fresh WAT for ring confirmation).
