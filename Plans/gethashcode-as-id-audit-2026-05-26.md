# Tech-Debt Audit: `GetHashCode()` used as a persistent identifier

**Opened:** 2026-05-26 (Tuvok). **Status:** OPEN (preliminary library scan done; BOTH HIGH findings now FIXED; lower-priority cleanups + broader-repo scan remain).
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
| 2 | `WebGL/WebGLAccelerator.cs:544` `programId = compiledKernel.GLSLSource.GetHashCode().ToString("X8")` | glWorker GL **program cache key** (sent in dispatch msg) | HIGH | **FIXED** 2026-05-26 → `WebGLCompiledKernel.ProgramId` monotonic `Interlocked` id (mirror of #1). Collision-proof. |
| 3 | `Wasm/WasmAccelerator.cs:667` `wasmBuf.GetHashCode()%1000` | `VerboseLogging` diagnostic string only | LOW | log-only, not identity. Optional cleanup. |
| 4 | `WebGPU/Backend/SharedMemoryResolver.cs` (201,215,245,296,330) | `VerboseLogging` diagnostic strings only | LOW | **VERIFIED**: matching logic uses element-type+array-size (Pass 1) / first-unassigned (Pass 2), NOT GetHashCode. Hashes are log-only — identity already decoupled. (TJ flagged this file; conclusion: logs only.) |
| 5 | `WebGPU/Backend/WGSLCodeGenerator.cs:411` `value.GetHashCode()` | `VerboseLogging` diagnostic string only | LOW | log-only, not identity. |

### Finding #2 (WebGL programId) — FIXED 2026-05-26
`WebGLCompiledKernel` now carries a stable `public int ProgramId { get; } = Interlocked.Increment(ref _nextProgramId);` assigned once at compile time (mirror of the Wasm `KernelCacheEntry.KernelId` fix). `WebGLAccelerator` sends `compiledKernel.ProgramId` as the dispatch-message `programId`; `glWorker.js` keys `programCache[programId]` on it. Confirmed by reading glWorker.js: `getOrCompileProgram(programId, source, ...)` returns the cached program when `programCache[programId]` exists — so two distinct shaders colliding on the old `GLSLSource.GetHashCode()` ran the WRONG program. The monotonic id is collision-proof; same compiled kernel → same id → correct cache hit; distinct kernels → distinct ids. `programId` is now an `int` (was an "X8" hex string); JS coerces it as an object key — no other consumer depended on the string form.

## Checklist (TJ's notes 2026-05-26, part 3)
- [x] Audit `SpawnDev.ILGPU` browser-backend modules for `GetHashCode` used as an identifier — done (table above).
- [x] Fix finding #2 (WebGL programId) — FIXED 2026-05-26 (`WebGLCompiledKernel.ProgramId` monotonic id).
- [ ] Scan `ILGPU/` fork core + `ILGPU.Algorithms/` fork (filter out legit value-type `GetHashCode` overrides).
- [ ] Scan `SpawnDev.ILGPU.P2P/`.
- [ ] Scan sibling SpawnDev libraries (BlazorJS, RTC, WebTorrent, Codecs, etc.) for hash-as-wire-id / hash-as-cache-key.
- [x] SharedMemoryResolver: confirmed identification logic is already decoupled from hash values (logs only).
