# Tech-Debt Audit: `GetHashCode()` used as a persistent identifier

**Opened:** 2026-05-26 (Tuvok). **Status:** OPEN (preliminary library scan done; one HIGH finding fixed, one HIGH finding open).
**Trigger:** the Wasm worker module-cache `kernelId` was derived from `RuntimeHelpers.GetHashCode(wasmBytes)` — a non-unique, GC-recycling identity hash used as a worker-side cache key. Strong candidate root cause for the residual large-sort corruption. Fixed this session. TJ asked for a codebase-wide sweep for the same anti-pattern.

## The rule (why this matters)
`Object.GetHashCode()` / `RuntimeHelpers.GetHashCode()` is a **heuristic for hash-table bucketing, never a unique identifier**:
- Distinct objects CAN return the same value (collision probability is non-zero).
- An identity hash is tied to the live object; under GC the slot frees and a later allocation can reuse the same value (recycling).
- `string.GetHashCode()` under .NET Wasm Hybrid Globalization can be delegated to browser-native APIs → platform-dependent volatility + `PlatformNotSupportedException` risk.

When such a value rides in a dispatch message / cache key, a collision makes the receiver act on a lie: it reuses the wrong cached module / program / pipeline state → silent wrong output, no crash.

**Use instead:** monotonic counter (`Interlocked.Increment`), `Guid`, or a real content hash (SHA-256) when content-addressing is the actual intent.

**IMPORTANT scoping rule for the audit:** distinguish two very different uses of `GetHashCode`:
- ❌ **as an identity/cache/wire key** — the bug. Flag and fix.
- ✅ **a `public override int GetHashCode()` on a value type, paired with `Equals`** — correct and required for dictionary/set membership. Do NOT flag these.

## Preliminary scan — `SpawnDev.ILGPU/SpawnDev.ILGPU/` library source only
(Scanned the 3 browser backends + services. NOT yet scanned: `ILGPU/` fork core, `ILGPU.Algorithms/` fork, `SpawnDev.ILGPU.P2P/`, and sibling SpawnDev libraries.)

| # | Site | Use | Severity | Status |
|---|------|-----|----------|--------|
| 1 | `Wasm/WasmAccelerator.cs` kernelId (was ~1531) | worker module-cache key `_modulesById[kid]` | HIGH | **FIXED** 2026-05-26 → monotonic `_nextKernelId` on `KernelCacheEntry` |
| 2 | `WebGL/WebGLAccelerator.cs:544` `programId = compiledKernel.GLSLSource.GetHashCode().ToString("X8")` | glWorker GL **program cache key** (sent in dispatch msg) | **HIGH — OPEN** | Same bug class as #1. Collision → worker reuses wrong compiled program → wrong output. Also `string.GetHashCode()` = Wasm Hybrid-Globalization hazard. |
| 3 | `Wasm/WasmAccelerator.cs:667` `wasmBuf.GetHashCode()%1000` | `VerboseLogging` diagnostic string only | LOW | log-only, not identity. Optional cleanup. |
| 4 | `WebGPU/Backend/SharedMemoryResolver.cs` (201,215,245,296,330) | `VerboseLogging` diagnostic strings only | LOW | **VERIFIED**: matching logic uses element-type+array-size (Pass 1) / first-unassigned (Pass 2), NOT GetHashCode. Hashes are log-only — identity already decoupled. (TJ flagged this file; conclusion: logs only.) |
| 5 | `WebGPU/Backend/WGSLCodeGenerator.cs:411` `value.GetHashCode()` | `VerboseLogging` diagnostic string only | LOW | log-only, not identity. |

### Finding #2 (WebGL programId) — fix sketch when scheduled
`compiledKernel` should carry a stable monotonic id assigned at compile time (mirror the Wasm `KernelCacheEntry.KernelId` fix), and the dispatch message + `glWorker.js` program cache should key on that, not on `GLSLSource.GetHashCode()`. Verify `glWorker.js` keys its program cache by `programId`. This is a real correctness bug, currently OPEN — surfaced to TJ; not fixed in the kernelId session to respect the "track for near-future" scoping. Recommend WebGL lane owner picks it up (or pull into a focused session).

## Checklist (TJ's notes 2026-05-26, part 3)
- [x] Audit `SpawnDev.ILGPU` browser-backend modules for `GetHashCode` used as an identifier — done (table above).
- [ ] Fix finding #2 (WebGL programId) — HIGH, open.
- [ ] Scan `ILGPU/` fork core + `ILGPU.Algorithms/` fork (filter out legit value-type `GetHashCode` overrides).
- [ ] Scan `SpawnDev.ILGPU.P2P/`.
- [ ] Scan sibling SpawnDev libraries (BlazorJS, RTC, WebTorrent, Codecs, etc.) for hash-as-wire-id / hash-as-cache-key.
- [x] SharedMemoryResolver: confirmed identification logic is already decoupled from hash values (logs only).
