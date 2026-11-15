# Velocity: keep every emitted method under the JIT's MinOpts limits (2026-11-15, Data) - PARKED: no measured benefit

## Outcome (read first)

Built and verified (design A below): every Velocity method under the JIT limits, 0 MinOpts methods in the VP9 loop
filter bench, the whole Velocity suite green with chunks forced on every method (2,538 / 0 / 217). Then the A/B that
should have come FIRST, same DLL, `ILGPU_VELOCITY_CHUNKING=0` vs on:

| measure | MinOpts (off) | FullOpts chunks (on) |
|---|---|---|
| VP9 loop filter filtering, V256, ms/frame | 375.5, 360.6 | 365.8, 385.5 |
| 6,000-op straight-line kernel, V256, ms/launch (median) | 440.3, 503.1 | 462.3, 436.1 |
| same, V128 | 499.3, 521.4 | 467.1, 493.1 |

No difference. MinOpts is real (the counters below match the JIT exactly) but it is not why Velocity is slow; the loop
filter's gap to the CPU accelerator (362 vs ~8 ms of filtering per frame) has another cause. The implementation is
parked as `..elocity-chunking-parked-2026-11-15.patch` (outside the repo; 15 files: the chunked emission, the size
counters / `VelocityMethodSize` diagnostics, test LargeKernel_EmittedMethodsStayUnderJitLimits, `velocity-chunk-ab` CLI)
in case a kernel ever shows a MinOpts cost. Lesson: measure the payoff of removing a suspected cause before building
the fix.

## Problem (measured)

SpawnDev.Codecs VP9 loop filter, 1080p, ILGPU 5.4.33 (Geordi's `vp9-lf-bench`, Codecs 5cf37c4):

| device | filtering ms/frame | groups-inactive ms/frame |
|---|---|---|
| Velocity V128 | 760.8 | 36.5 |
| Velocity V256 | 377.9 | 19.1 |
| CPU accelerator | 0.54 | 1,058.7 (launch + barrier overhead, separate issue) |

`DOTNET_JitDisasmSummary=1` shows the kernel entry (IL 87,942 B) and the [NoInlining] lane helpers (IL 88,549 /
123,096 / 132,160 B) as **"Tier-0 switched MinOpts"**. Small Velocity methods are FullOpts.

The JIT's retail rule (`compSetOptimizationLevel`, DEFAULT_MIN_OPTS_*): a method with more than 60,000 IL bytes,
20,000 IL instructions, 2,000 basic blocks, 2,000 locals or 8,000 local references is compiled MinOpts. Proven with a
Reflection.Emit probe: a 92 KB method is MinOpts as a DynamicMethod, a TypeBuilder method, and with
`MethodImplAttributes.AggressiveOptimization`; the `DOTNET_JITMinOpts*` knobs are DEBUG-only. MinOpts does not inline,
so every `Vec256Operations` helper is a real call: hundreds of times slower.

Why so big: ILGPU inlines once per IR context (shared by all backends, before Velocity). AggressiveInlining and
ILGPU-internal helpers bypass the 2,048-source-IL budget, trivial helpers do not count, Velocity expands one IR value
into several IL instructions and local references, and a by-value struct is one vector local per field.

## Fix

1. **Measure** (no behaviour change): `ILEmitter` counts local references and instructions; Velocity records IL bytes,
   instructions, locals and local references per emitted method; the compiled Velocity kernel exposes the largest and
   whether any method exceeds the limits. Test: a kernel built like the VP9 one (an AggressiveInlining helper of a few
   hundred IR values called many times) gives the host's result AND has every method under the limits. Fails today.
2. **Split** an over-budget function so each emitted method is under the limits with margin. Two candidates:
   - (B, preferred if it holds up) a Velocity-only IR step before code generation that outlines single-entry /
     single-exit regions of an over-budget function into new methods (live-ins as parameters, live-outs as a returned
     structure), so the existing, tested Velocity call path (masks, barriers, struct values) does the rest.
   - (A) code generation emits the function's blocks as chunk methods sharing a frame struct (masks, phis, loop-exit
     locals, cross-chunk values in the frame; a cross-chunk branch returns the block id to a dispatcher loop).
   Size the regions from step 1's per-value cost, verified by the emitted counts (re-split if still over).
3. Red-check (disable the split: the test fails on the limits), all Velocity lanes, Geordi's bench before / after
   (V128, V256), then the full sweep before any stable.

## Measured shapes (step 1 counters, Codecs VP9 decode on Vector256)

| method | IL bytes | instr | locals | local refs | blocks |
|---|---|---|---|---|---|
| AdaptKernel | 249,544 | 59,884 | 7,043 | 36,639 | 1,839 |
| ParseBlock | 185,374 | 44,447 | 4,064 | 27,213 | 912 |
| InverseTransformAdd (x2) | 132,160 | 31,372 | 1,535 | 19,109 | 386 |
| PredictBlockPlaneRegion | 123,096 | 29,995 | 3,758 | 18,833 | 940 |
| CompressedHeaderKernel | 89,541 | 22,256 | 2,268 | 14,128 | 531 |
| SuperblockRowsLane | 88,549 | 21,882 | 1,741 | 13,804 | 467 |
| WavefrontKernel | 87,942 | 21,381 | 1,905 | 13,510 | 347 |
| Predict | 65,320 | 16,224 | 1,068 | 9,762 | 267 |
| SuperblockColumnsLane | 49,650 | 12,638 | 1,102 | 8,008 | 277 |

The counters match the JIT exactly (LargeKernel test: ILBytes 92,495 = the JIT's "IL size=92495", MinOpts). Local
references are the binding limit (8,000) almost everywhere; these are multi-block CFGs (hundreds to ~1,800 blocks,
loops, switches), so region outlining (B) would need SESE regions of the right size that codec CFGs may not have.
**Decision: (A)** chunked emission: the method becomes a dispatcher holding a frame struct; chunk methods
`int Chunk(ref Frame, int block)` run a contiguous part of the emission order and return the next block (or -1 to
return). Works for any CFG; within a block a chunk boundary is a split point too (the LargeKernel test kernel is one
4-label block of 92 KB).

## Implementation design (A)

Everything goes through the emitter interface (`IILEmitter`): Velocity has no localloc, pinned locals, exception
blocks or tail calls, the specializer is emitter-generic, and no Velocity code touches the ILGenerator directly.

- **Cut points**: the generator announces them: start of GenerateCodeInternal, each block start, after each value,
  end of GenerateCodeInternal. The IL stack is empty there. Code before the start / after the end (function prologue
  and epilogue, the kernel's iteration loop) is the dispatcher region D; everything between goes to chunks.
- **Recording pass**: the method is generated once with `RecordingILEmitter` (no IL): per segment between cut points,
  instructions, estimated IL bytes, each local reference (index + load/store/address), labels marked and referenced.
  The same generator run is deterministic, so the real pass declares the same locals / labels in the same order.
- **Decision**: totals under ~80% of every JIT limit: normal emission (unchanged). Otherwise partition.
- **Partition**: greedy over segments with a budget; then the exact pass-2 cost per chunk (a frame load +1
  instruction; a frame store +3 instructions +2 references; entry switch; trampolines); any chunk over 90% of a limit:
  halve the budget and redo.
- **Persistent**: a local referenced in more than one region (D counts as one), and every method argument, live in a
  frame struct (runtime-defined, one field each). Other locals are real locals of their chunk.
- **Chunk method**: `int Chunk(ref Frame f, int id)`: `switch (id)` to its entry labels, then its code. A branch to
  a label of another region goes to a local trampoline `ldc.i4 id; ret`; falling through a cut point ends the chunk
  with `ldc.i4 id(cut); ret`.
- **Dispatcher D** (the declared method, so callers are unchanged): copies the arguments into its frame local, runs
  its own code; at the start cut point `ldc.i4 id; br DISPATCH`; DISPATCH (emitted at the end): `switch (id)`: a chunk
  id calls that chunk (`ldloca frame; ldloc id; call`) and loops, a D label id branches to the label (the kernel's
  local exit, the function's return label).
- **Sizes**: D and every chunk are measured (VelocityMethodSize, "Name#dispatch" / "Name#chunkK"); the LargeKernel
  test checks all of them.
- **Testing**: a backend knob forces chunking at a tiny budget; the whole Velocity suite runs once that way (every
  cross-chunk path exercised), plus the LargeKernel test at the real budget, plus red-check (knob off: test red).

## Follow-up (separate)

CPU accelerator: 17 ms per launch of mostly idle 64-lane groups with two barriers (launch + barrier cost, not work).
