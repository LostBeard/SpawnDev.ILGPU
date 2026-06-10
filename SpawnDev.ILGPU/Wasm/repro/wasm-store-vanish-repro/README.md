# Wasm store-vanish minimal repro (Seven, 2026-06-10)

Standalone bisect harness for the Wasm residual large-sort anomaly, root-caused to a
**same-thread store-visibility loss** under CPU oversubscription (a short window of stores -
plain or atomic - by one worker thread intermittently never becomes visible to anyone,
including the storing thread, leaving the slot one publication behind). See
`_DevComms/global/seven-ROOT-CAUSE-EVIDENCE-*` + `seven-ADDENDUM-*` + `seven-DISCRIMINATOR-VERDICT-*`.

These modules contain **zero ILGPU machinery** - they exist to test whether the anomaly
reproduces in a minimal synthetic shape (it does NOT - it needs the big-kernel context;
the real-kernel repro in `../wasm-scan-repro/` is the upstream payload).

## Files (source of truth)
- `vanish.wat`  - flat: sentinel-bracketed runs of 40 plain + 40 atomic stores per iteration
  per worker; immediate + post-barrier same-thread verify; production-shape generation barrier
  (arrival rmw, seq_cst gen spin, yield-to-JS escape, park/resume).
- `vanish2.wat` - + the production CALL STRUCTURE (publish inside a leaf before return) + a
  64-store save-block in a mid function.
- `vanish3.wat` - + the EXACT failing instruction shape (plain-fill temp, atomic mem->mem copy
  publish) + a 6-tid loop between barriers (production tid loop).
- `run-vanish.mjs` - Node worker_threads driver. Mirrors the production worker loop.

`.wasm` files are GENERATED (`wat2wasm --enable-threads <f>.wat -o <f>.wasm`) and gitignored.

## Run
```bash
wat2wasm --enable-threads vanish.wat  -o vanish.wasm
node run-vanish.mjs 48 400 60                     # workers iters rounds
WASM=vanish2.wasm node run-vanish.mjs 48 400 60
WASM=vanish3.wasm TIDS=6 node run-vanish.mjs 48 400 60
NO_PARK=1 node run-vanish.mjs 48 400 60           # busy-spin instead of Atomics.wait
node --no-wasm-tier-up --no-wasm-dynamic-tiering run-vanish.mjs 48 400 60   # Liftoff only
node --no-liftoff run-vanish.mjs 48 400 60                                  # TurboFan only
```
PASS = zero imm/post counts. **All three increments: PASS 0/60 @48 workers** (~900K parks each).

> ⚠️ Oversubscribed (pegs cores). Announce + get the Captain's go before running on the shared machine.
