# SpawnDev Interop Audit — replace manual `JSRef` bypasses with class-level JSObject APIs

**Opened:** 2026-05-26 (Tuvok). **Status:** OPEN (preliminary ILGPU scan done; full audit pending).
**Trigger:** TJ's "Rules of Engagement: Proper API Usage" (welcome-back notes 2026-05-26, part 4). We own the SpawnDev.BlazorJS stack; our own code should use its strongly-typed class APIs, not reach around them with generic `JSRef.Call`/`Get`/`Set`. Reaching around turns a typed `JSObject` into a "bag of methods" and hides where the class API is incomplete.

## Rules of Engagement
1. **Class-First API usage.** Before any `JSRef` extension (`JSRef.CallVoid`, `JSRef.Get`, …), check whether the object (a `JSObject` subclass) already exposes the functionality as a typed method/property.
   - ✅ `zeroView.Fill(0);`  ❌ `zeroView.JSRef!.CallVoid("fill", 0);`
2. **Don't work around — improve the class.** If a needed method is missing from a `JSObject` class, ADD it to the SpawnDev.BlazorJS source. Don't build a manual `JSRef` bypass around the gap.
3. **Purposeful interop.** Direct `JSRef` is reserved for: (a) no class-level abstraction exists, or (b) a specific perf optimization (e.g. avoiding return-value serialization).
4. **Mandatory audit (this doc).** Find existing manual `JSRef` calls that an existing class-level method already covers, and refactor them.

## Preliminary scan — `SpawnDev.ILGPU` project only
`JSRef!?.(Call|Get|Set)` occurrences (NOT yet triaged into "legit per rule 3" vs "bypass to refactor"):

| File | Count | Notes (to triage) |
|------|-------|-------------------|
| `WebGL/WebGLAccelerator.cs` | 10 | GL context / worker postMessage paths — triage each against typed wrappers |
| `Wasm/WasmAccelerator.cs` | 10 | worker postMessage / memory / typed-array paths |
| `Services/GpuShareService.cs` | 7 | triage |
| `Wasm/WasmMemoryBuffer.cs` | 3 | SharedArrayBuffer / typed-array ops — check for `Uint8Array`/`ArrayBuffer` typed wrappers |
| `WebGL/WebGLDevice.cs` | 1 | triage |

**Total: 31 candidate sites in ILGPU.** Each needs: (1) is there a class-level API? if yes → refactor (rule 1). (2) if no → is a wrapper missing that should be added to SpawnDev.BlazorJS? (rule 2). (3) if it's a deliberate perf/no-abstraction case → annotate WHY (rule 3) so it's not re-flagged.

## Scope beyond ILGPU
This audit applies to ALL SpawnDev.BlazorJS consumers, not just ILGPU. Other lanes' projects (RTC, WebTorrent, Codecs, VoxelEngine, GameUI, SpawnWear Companion, …) should each get the same triage. Coordinate per-lane.

## Checklist
- [x] Preliminary scan of `SpawnDev.ILGPU` (count + per-file table above).
- [ ] Triage each of the 31 ILGPU sites → {refactor to class API / add missing wrapper to BlazorJS / annotate as purposeful}.
- [ ] Refactor the clear bypasses; add missing wrappers to SpawnDev.BlazorJS source (rule 2).
- [ ] Annotate the deliberate `JSRef` uses with a one-line WHY so future audits skip them.
- [ ] Extend the scan to other SpawnDev projects (per-lane).
