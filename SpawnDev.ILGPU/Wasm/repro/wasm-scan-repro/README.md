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

## VERDICT (final, 2026-06-10)
Root cause PINNED at instruction level: a **same-thread store (plain or atomic) intermittently
loses visibility to everyone including the storing thread** under CPU oversubscription, leaving
the slot one publication behind. Fires in BOTH V8 tiers (Liftoff `--no-wasm-tier-up`, TurboFan
`--no-liftoff`); oversubscribed-only; victim is always the worker's last tid. Exonerated by
evidence: emitted-vs-source 1:1, `Atomics.wait` park (NO_PARK also corrupts), fiber save/restore,
dispatcher savedGen/phase invariants. **Aliasing REFUTED with positive evidence** - the
writer-stamp discriminator (`DBG_KERNEL=2`) instrumented all 1,605 store sites: ZERO foreign
writers + totalWrites exact on every victim. Full trail:
`_DevComms/global/seven-ROOT-CAUSE-EVIDENCE-*`, `seven-ADDENDUM-*`, `seven-DISCRIMINATOR-VERDICT-*`.

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
