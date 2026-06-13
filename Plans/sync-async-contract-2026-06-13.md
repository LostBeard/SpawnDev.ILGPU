# Sync / Async Surface Contract — SpawnDev.ILGPU (2026-06-13)

**Status:** DRAFT for Captain review. Drives the completion of the "sync = desktop-only" work (Thread A).
**Author:** Geordi. **Trigger:** the sync `Synchronize()`/`Flush()` throw broke `Allocate1D(data)` / `CopyFromCPU` on all browser backends (clean-publish PMT: 20 fail). Prior-session "green" was build-only; the browser PMT was never run.

## GOVERNING PRINCIPLE (Captain-approved 2026-06-13)
**Async-only where it WAITS or OBSERVES; sync stays for fire-and-forget (dispatch / alloc / upload / flush-submit).**

- If an operation must wait for completion or observe a result (Synchronize, device→host readback, Reduce→scalar, "wait for a buffer value / flag") → it is **async-only**; the sync form **throws** on every backend that can't honor it (browser + P2P). This makes the silent-wrong-behavior class STRUCTURALLY IMPOSSIBLE — there is no sync twin left to misuse. A desktop-only-tested "portable" library fails LOUD on browser, never silently wrong.
- If an operation is fire-and-forget — kernel dispatch, allocation, host→device upload, Flush/submit — it does NOT wait, so it cannot lie. It **stays sync** on every backend it's honest on (P2P is the exception: even submit is an async network send → throws). Keeping these sync preserves the dispatch hot path (Rule 4); this is the standard GPU model (CUDA: launch async, synchronize explicitly).
- This single rule REPLACES per-method whack-a-mole: we no longer decide method-by-method whether to throw — the rule decides. The audit's job is to find EVERY wait/observe operation and guarantee each has an async sibling with the sync form throwing; and to confirm fire-and-forget ops are NOT throwing (the Flush + CopyFromCPU regression is exactly a fire-and-forget op wrongly throwing).

## 0. Why this doc exists
"Synchronize" and "Flush" were being treated as one thing and throwing identically on browser. They are DIFFERENT operations with DIFFERENT validity per backend, and internal library code overloads `Synchronize()` for a third purpose (copy-completion). Conflating the three is the root confusion. This maps every operation × backend so we implement to a spec, not by patching symptoms.

## 1. The operations, defined precisely
- **Flush / FlushAsync** — *submit* all pending/batched work to the device so it STARTS. Does NOT wait for completion.
- **Synchronize / SynchronizeAsync** — *wait until previously-submitted work has COMPLETED* (results host-visible, buffers safe to reuse). This is original ILGPU's meaning: "wait for the accelerator to finish what it's doing."
- **CopyToCPU** (device→host readback) — needs the producing work COMPLETED + the transfer done.
- **CopyFromCPU** (host→device upload) — needs the host source array CONSUMED before returning (so the caller may reuse/free it). NOT a full-completion wait.
- **MemSet** — device-side write; ordering vs other work matters, not host-completion.

## 2. Per-backend sync-validity matrix
"✓" = the SYNCHRONOUS form delivers correct desktop-equivalent semantics. "✗→throw" = cannot; must use the async sibling and the sync form should throw loudly (so a desktop-only-tested "portable" library fails LOUD on browser, not silently wrong).

| Sync op | CPU/CUDA/OpenCL | WebGPU | WebGL | Wasm | P2P (remote) |
|---|---|---|---|---|---|
| **Flush** (submit) | ✓ (eager/noop) | ✓ (encoder submit) | ✓ (noop) | ✓ (noop) | ✗→throw (async net send) |
| **Synchronize** (wait completion) | ✓ (block) | ✗→throw (can't block) | ✗→throw | ✗→throw | ✗→throw (remote round-trip) |
| **CopyToCPU** (readback) | ✓ | ✗→throw (mapAsync) | ✗→throw | ✗→throw (reads stale SAB) | ✗→throw |
| **CopyFromCPU** (upload) | ✓ (block DMA) | ✓ (writeBuffer sync-consumed) | ✓ (backing array) | ✓ (SAB memcpy) | ✗→throw (async net send) |
| **MemSet** | ✓ | ✓ (encoder) | ? (deferred upload) | ✓ (SAB) | ✗→throw |

### Key reads from the matrix
1. **sync Flush is VALID on browser** (it is only a submit). The current code WRONGLY throws on browser Flush. Only P2P should throw.
2. **sync Synchronize is invalid on every async backend** (browser + P2P). Throwing is correct. (P2P currently no-ops — should throw for consistency once P2P uses the stream model.)
3. **sync CopyToCPU (readback) SHOULD throw on browser+P2P** — genuinely impossible; the throw surfaces real misuse (it returned stale data before). This is a GOOD consequence of the change.
4. **sync CopyFromCPU (upload) is VALID on browser** (upload is synchronously consumed). It must NOT throw. Its internal `stream.Synchronize()` is the bug — see §4.

## 3. Internal caller audit (forked core `ILGPU/Runtime/ArrayViewExtensions*.cs`)
~16 internal sync `.Synchronize()` callers. Classified:

| Site(s) | Method | Direction | Browser verdict |
|---|---|---|---|
| 1259, 1743 | `CopyFromCPU` | upload | **WRONGLY breaks** — must be a browser no-op (upload sync-consumed) |
| 930, 1712 | `CopyToCPU` | readback | throw is CORRECT (loud surfacing of impossible sync readback) |
| 1891, 1928, 2138, gen 2516/2565/2614 | `*PageLocked*` | CUDA pinned mem | desktop-only feature; never runs on browser — unaffected |
| MemoryPressure.cs:72 | best-effort flush | already `try/catch` | tolerant — unaffected |
| Cuda/OpenCL ProfilingMarker, CLStream | desktop | desktop-only — unaffected |

**Conclusion: the live regression is exactly the 2 `CopyFromCPU` upload sites.** Everything else is either correct-to-throw (readback) or desktop-only (page-locked, profiling).

## 4. The one real implementation decision — how `CopyFromCPU` ensures upload-completion
After an upload, the host array must be safe to reuse. Required behavior:
- **Desktop:** wait for the DMA (today: full `Synchronize()` — overkill but correct).
- **Browser:** nothing (writeBuffer/SAB/backing-array consume the source synchronously).
- **P2P:** the send is async → sync upload is impossible → throw (use `CopyFromAsync`).

This is NEITHER the public `Synchronize` (which must throw on browser for consumer safety) NOR `Flush` (submit, no host-safety guarantee on desktop). Options:

- **(A) Backend hook.** Add a protected `EnsureUploadConsumed()` (or reuse an internal): desktop=block, browser=noop, P2P=throw. Public `Synchronize` keeps throwing on browser. CopyFromCPU calls the hook. Cleanest separation of "internal copy-completion" from "user wait".
- **(B) Type-switch in core.** In generic `CopyFromCPU`, `if (acceleratorType is desktop) stream.Synchronize();` else rely on the backend's sync-consumed upload. Minimal change, but bakes backend knowledge into generic core.
- **(C) Backend-virtual sync copy.** Make `CopyFromCPU` completion the buffer's responsibility (each `MemoryBuffer` knows if its upload is sync-consumed). Most correct long-term; largest change.

## 5. Resulting code changes (once a §4 option is chosen)
1. **Un-throw sync `Flush()` on browser** (WebGPU/WebGL/Wasm streams + accelerators): browser Flush = submit (the old `FlushPending`/encoder-submit / no-op). Keep `Synchronize` throwing. Keep P2P Flush throwing.
2. **Fix `CopyFromCPU` upload-completion** per §4 (so `Allocate1D(data)` works on browser again).
3. **Keep** sync `Synchronize` + sync `CopyToCPU` throwing on browser (correct).
4. **P2P column:** make `P2PStream.Synchronize`/`Flush` throw (currently no-op) for contract consistency, IF/when P2P routes through the stream model (today it uses the separate `DispatchAsync` swarm API).
5. **Algorithm builders (`CreateScan`/`RadixSort`/`Pairs`) are FIRE-AND-FORGET → they must NOT throw on browser** (finding 2026-06-13). Evidence: `CreateWebGPUMultiPassScanAsync` (ScanExtensions.cs:1392-1451) does ONLY `await stream.FlushAsync()` between passes — a SUBMIT, no completion-wait, no device→host readback (the inter-pass `CopyFrom` is GPU→GPU, fire-and-forget). So the multi-pass scan/sort genuinely only DISPATCHES + SUBMITS; per the governing principle it is sync-valid on browser. The sync builders throw today ONLY because the legacy sync multi-pass path used `Synchronize()` (now throwing) as its inter-pass barrier where it should use `Flush()` (submit, now valid on browser). **Correct fix = route browser sync `CreateScan`/`CreateRadixSort`/`Pairs` to a sync multi-pass variant that uses `Flush()` between passes (not `Synchronize()`); then REVERT the builder throws (ScanExtensions.cs:1536 / RadixSortExtensions inner-scan) AND revert the test conversions (Tests9 scan, the ~35 radix/pairs sites).** This is LESS churn than converting 35 tests and is what the principle demands (fire-and-forget stays sync). The `*Async` builders remain as conveniences but are not REQUIRED for browser. (Caveat to verify per backend: Wasm's dispatch serialization already orders passes — the async path even skips the inter-pass flush on Wasm, ScanExtensions.cs:1430 — confirm the sync path orders correctly there.)
6. **Contract tests** (`SyncSynchronizeContractTest`/`SyncFlushContractTest`) must be updated: sync Flush should NOT throw on browser per §2.1.

## PROGRESS (2026-06-13, verified by clean-publish PMT)
- **Phase 1 DONE + GREEN.** Core `AcceleratorStream.EnsureHostCopyConsumed()` hook (default=Synchronize for desktop wait; WebGPU/WebGL/Wasm override = no-op since host->device upload is sync-consumed) + routed the 2 `CopyFromCPU` upload sites to it. Un-threw browser sync `Flush()` (WebGPU=`FlushPending` submit; WebGL/Wasm=no-op). Sync `Synchronize()` + sync readback STILL throw on browser (correct). PMT: `Contract` 16/16, `GlobalInclusiveScan` 37/37.
- **Phase 2 scan DONE + GREEN.** Public sync `CreateScan` browser cases re-routed from throw → existing sync builders (WebGPU `CreateMultiPassScan` [CopyFrom+Flush], WebGL `CreateWebGLHillisSteeleScan`, Wasm `CreateSingleGroupScan`). No new algorithm code. Reverted the Tests9 scan async-conversion back to sync (now tests the sync browser path). PMT: `GlobalInclusiveScan` 37/37 (incl. Wasm large n=16384 via single-group). Zero test conversions kept.
- **Phase 2 radix: VERIFYING.** Sync `CreateRadixSort`/`Pairs` use the inner sync scan (now working on browser) + stream ordering; the only direct sync `Synchronize()` calls are diagnostic-only (`if (PerPassHook != null)`, null in normal runs — and per the principle those correctly throw on browser since the hook observes GPU counters). Expect the ~30 radix/pairs tests to pass on browser with NO test conversion. PMT running.
- **Remaining:** full cross-backend PMT sweep (release gate); commit; fork-publish coherence (the new core virtuals `Flush`/`FlushAsync`/`EnsureHostCopyConsumed` live in the ILGPU fork → publish forks 2.0.16 WITH them before SpawnDev.ILGPU, else packed-nupkg MissingMethodException vs on-feed 2.0.15).

## 6. Open questions for Captain
- §4: which option (A backend-hook / B type-switch / C backend-virtual)? (Geordi leans A — clean separation, modest size, P2P-ready.)
- Is `Synchronize` strictly "wait for completion" with NO submit duty, or should it also imply a flush on the backends where it doesn't throw? (Desktop Synchronize implicitly flushes since work is eager; defining it as "flush + wait" is harmless and matches intuition.)
- P2P: confirm intent that every sync GPU-touching call throws on P2P and the async API is the only portable path.
