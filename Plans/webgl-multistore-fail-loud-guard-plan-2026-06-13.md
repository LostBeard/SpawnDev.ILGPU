# Plan: WebGL Transform-Feedback "fail loud on multi-store-per-thread" compile guard

**Status:** OPEN / BACK-BURNER (TJ, 2026-06-13). The selection-gate half shipped; the compile-time guard is a hard codegen-analysis problem and is deferred.
**Author:** Geordi (ILGPU). Consulted: Tuvok (ML, who found the bugs + audited the kernels).
**Origin:** Tuvok's `_DevComms/global/tuvok-webgl-multistore-audit-2026-06-13.md` ask #4, after TurboQuant `NormalizeImpl` + FWHT butterfly silently corrupted on WebGL.

---

## Goal
At WebGL kernel-compile time, throw `UnsupportedKernelFeatureException` when a kernel writes output in a way the WebGL Transform-Feedback model silently corrupts, instead of producing wrong results a consumer trusts. Mirror the existing WebGL guards for atomics (`GenericAtomic`/`AtomicCAS`) and in-kernel group/warp Scan/Reduce (all in `SpawnDev.ILGPU/WebGL/Backend/GLSLCodeGenerator.cs`).

## The TF contract (why this corrupts silently)
WebGL output is captured via Transform Feedback. The readback (`SpawnDev.ILGPU/wwwroot/glWorker.js`, ~line 790-817) places **store-slot `s` of vertex `v` at output index `v*storeCount+s`**. The GLSL codegen **ignores the kernel's actual store offset** and writes a positional TF varying (`tf_out_P` / `tf_out_P_s{slot}`), routed by source-emission order via `_currentStoreSlot` (`GLSLKernelFunctionGenerator.GenerateCode(Store)`, ~line 3096-3217). So a kernel is correct on WebGL **iff every store's intended offset equals `v*storeCount+slot`** (or `v` for single-store), AND the positional store **executes exactly once per vertex per slot**.

Two known real bugs (both fixed ML-side, see `SpawnDev.ILGPU.ML`):
- **`TurboQuantKernels.NormalizeImpl`**: one thread/vector looped `for(i=0;i<d;i++) output[offset+i]=...` → d distinct elements/thread → all collapse to the vertex's one TF slot. (Fixed: split to one-store-per-thread, commit `b953350`.)
- **`FWHTKernel` butterfly**: one thread wrote `data[i]` and `data[i+halfSize]` (2 non-adjacent elements) → positional readback relocates them to `v*2+0`/`v*2+1`. (Fixed: out-of-place ping-pong, one store/thread.)

## What was TRIED and BACKED OUT (2026-06-13)
A blunt IR pre-scan `GuardOneStorePerThread()` in `GLSLKernelFunctionGenerator` (called from `GenerateCode()` after `AnalyzeOutputBuffers`). Two signals, BOTH false-positived — PMT WebGL sweep went **374 pass / 40 FAIL / 114 skip** (was ~414/0/114). Removed cleanly; do NOT retry these signals:

### Signal A — `_outputStoreCount[p] > 1` → throw. **WRONG.** (34 of 40 fails)
- `storeCount > 1` is a NORMAL, CORRECT pattern: the positional multistore mechanism handles `GpuMatrix4x4` (16 floats/thread), `NoInliningIdct16Row*`, `ManyViews_11Views`, `IntBuffer_Pack*`, `Boids` — each thread writes K *consecutive* elements at `v*K+slot`. Throwing on `storeCount>1` broke all of them.
- `_outputStoreCount` (`AnalyzeOutputBuffers`, ~line 252-308) counts **distinct textual LEA expressions** `"{lea.Source}[{lea.Offset}]"`, so it OVER-counts a single element written through branchy SSA (different SSA index values in if/else arms): `DoubleInfinityArithmeticTest`, `FloatDivisionByZeroTest`, `Tests23_BareUintShift`, `Tests23_NormalizeShape_*` all tripped it while writing ONE element.
- **Takeaway:** `_outputStoreCount` ≠ "distinct runtime output elements per thread." Do not reuse it as that.

### Signal B — "store's offset value is defined in a loop block" → throw. **WRONG.** (5 of 40 fails)
- Killed by the **grid-stride loop idiom** — THE standard ILGPU kernel shape:
  `for (i = Grid.GlobalIndex.X; i < N; i += GridExtensions.GridStrideLoopStride) out[f(i)] = ...`
  The offset `i` is loop-variant, but on WebGL the dispatch sizes the grid to N vertices, so the loop runs **once per vertex** (i = gl_VertexID) → the store lands at the own slot, CORRECT.
- All 5 fails were RadixSort (`AlgorithmRadixSortNonPairs{Int,Float}Test`, `UintKeysOnlyRadixSortTest`, `RadixSort100KBenchmarkTest`, `RadixSortMinimalPatternsTest`). WebGL RadixSort routes to `CreateWebGLScatterRadixSortDispatch` (the `IScatterProvider` render-to-texture path, `RadixSortExtensions.cs:1226`); its helper kernels (extract-bit / scan / copy / compute-dest) use the grid-stride idiom. The actual reorder is the HOST render-points-to-texture scatter (`WebGLAccelerator.Scatter`), NOT an in-kernel store.
- Cannot distinguish a grid-stride loop (runs once/vertex, valid) from NormalizeImpl's unit-stride per-thread loop (`for i=0;i<d;i++`, runs d times/vertex, broken) by "offset is loop-variant" alone.

## The core difficulty
Correctness requires (a) offsets conform to `v*storeCount+slot`, AND (b) the positional store executes the right number of times per vertex. **(b) depends on the RUNTIME dispatch grid size** (a grid-stride loop runs once when grid=N, many times when grid<N), which the compile-time codegen cannot see. So a purely-compile-time guard must infer loop *structure* (grid-stride vs per-thread) and offset *conformance*, not just presence-of-loop or store-count.

## Candidate criterion (NOT yet validated — two parts)
1. **Loop part (catches NormalizeImpl / TopK):** flag a TF output store inside a loop that is NOT the grid-stride wrapper. Distinguish:
   - EXEMPT: loop whose increment == the grid stride (`GridStrideLoopStride` = `Grid.DimX * Group.DimX`) and whose induction var seeds from `Grid.GlobalIndex` — runs once/vertex on WebGL.
   - BROKEN: a unit/constant-stride per-thread loop bounded by a runtime per-thread count, where the body stores a different output element each iteration.
   Needs loop-increment-value analysis (ILGPU `Loops<>` analysis already available in `GLSLKernelFunctionGenerator._loops`).
2. **Unrolled part (catches FWHT):** for `storeCount>1`, verify the K offsets are EXACTLY `vertexIndex*storeCount + {0..K-1}` (the positional layout). `Idct16`/`Matrix4x4` (`out[base+0..K-1]`, base=v*K) conform; FWHT (`data[i]`/`data[i+halfSize]`) does not. Needs:
   - affine matching of each store offset against the vertex index value (gl_VertexID, `SetupIndexVariables`, ~line 1614-1628), AND
   - an ACCURATE distinct-runtime-element count (not the over-counting `_outputStoreCount`).

## Recommended detection point
NOT a blunt IR pre-scan. Do it **inside the codegen where positional varyings are allocated and stores are routed** — `GLSLKernelFunctionGenerator.EmitOutputVaryings` (~line 1429) + the `Store` override (~line 3096) / `_currentStoreSlot` — where the code already KNOWS the slot it's about to assume; there it can compare the store's intended offset GLSL/IR expression against the positional `v*storeCount+slot` it's about to emit, and the loop nesting of the store site is in hand.

## Open questions (sent to Tuvok, `geordi-to-tuvok-multistore-guard-BACKED-OUT-need-correct-criterion-2026-06-13.md`)
1. Do ALL observed ML bugs fit {non-grid-stride per-thread loop store} ∪ {unrolled non-conforming multi-store}? Any other shape?
2. A cleaner author-facing contract/attribute instead of codegen inference?
3. Would a DEBUG-mode-only assertion (vs always-on throw) be acceptable? That widens the affordable analysis.

## What DID ship (keep)
`AcceleratorRequirements.RequiresScatterStores` (WebGL=false) — selection gate so consumers filter WebGL up front for kernels needing in-kernel scatter / >1 output element per thread. + `Satisfies`/`HasScatterStores`/`Describe` + 3 desktop tests (`AcceleratorRequirementsTests`). This is the user-facing half and carries the value with zero codegen risk. The compile-time fail-loud guard is the remaining (deferred) authoring-time nicety.

## Key file references
- `SpawnDev.ILGPU/WebGL/Backend/GLSLKernelFunctionGenerator.cs` — `AnalyzeOutputBuffers` (~252), `EmitOutputVaryings` (~1429), `SetupIndexVariables` (~1614), `GenerateCode(Store)` (~3096), `_loops`/`_outputStoreCount`/`_outputParamIndices`/`_currentStoreSlot` fields.
- `SpawnDev.ILGPU/WebGL/Backend/GLSLCodeGenerator.cs` — existing guards (atomics ~2174, Scan/Reduce ~2088); `UnsupportedKernelFeatureException` ctor shape.
- `SpawnDev.ILGPU/wwwroot/glWorker.js` — TF readback `v*storeCount+slot` (~790-817).
- `ILGPU.Algorithms/RadixSortExtensions.cs` — WebGL scatter routing (~1226), `RadixSortKernel2` grid-stride store (~879-939).
- `SpawnDev.ILGPU/AcceleratorRequirements.cs` — `RequiresScatterStores` (shipped half).
- Lesson memory: `feedback-webgl-multistore-guard-blunt-ir-signals-false-positive`.
