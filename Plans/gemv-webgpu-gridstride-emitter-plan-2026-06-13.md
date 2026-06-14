# Plan: Fix the WebGPU GEMV grid-stride emitter bug (#4) — re-enable the fast grouped GEMV on WebGPU

## ✅✅ FIXED (Geordi, 2026-06-13) — TWO-PASS uniform-break (option 1). Full sweep 3408/0/223 + GEMV repro green WebGPU/Wasm/CUDA/OpenCL/CPU.
**The fix (`WGSLKernelFunctionGenerator.GenerateLoopConstruct`):** the synthetic uniform break + group/tile counter (init/increment) are emitted UNCONDITIONALLY for a thread-dependent loop, but the BREAK CONDITION is emitted as a unique sentinel (`/*__UFBRKn__*/`). After the loop body is fully emitted, scan the EMITTED body text for `workgroupBarrier(`/`storageBarrier(` (real text by then — incl. inlined cooperative-op-helper barriers, which the earlier IR/registry approaches could NOT see) and `Builder.Replace` the sentinel with the UNIFORM break (barrier present → keep uniform control flow) or the NATURAL thread-dependent break (barrier-free → the GEMV K-loop; fixes the grid-stride conflation). The unconditionally-emitted `_uf_break_*` let + `_uf_group_iter` are harmless dead code (written-but-unread) when the natural break wins. **Why it's safe:** for barrier loops the patch emits the SAME `breakConditionExpr` the original always emitted → byte-identical WGSL → scan/radix CANNOT regress (full sweep confirms: zero RadixSort/Scan failures). Only barrier-free thread-dependent loops change (→ natural break). The auto-tile fold (`Grid2D_ExceedsMaxWorkgroupsX`) uses a GROUP-uniform counter (`threadCounterName==null`) → transform never fires → untouched. **Files:** `WGSLKernelFunctionGenerator.cs` (sentinel + post-loop scan/patch, field `_ufBreakSentinelCounter`). Repro guard `BackendTestBase.GemvGroupReduce_MatchesCpuOracle` (gated off WebGL — in-kernel shared-mem reduction is structurally impossible there). Offline probe `DemoConsole subgroup-reduce ... gemv` (GEMV natural-break + scan-keeps-uniform). **REMAINING (ML lane, Tuvok):** remove the M==1 WebGPU GEMV fallback gate (`2752c3b`) so the fast cooperative GEMV runs on WebGPU + re-measure decode. Possible micro-opt (mine, follow-up): suppress the dead `_uf_*` counter/let when the natural break wins (one int-add/iteration in the hot loop; sentinel the init/increment too). **WebGL stays per-element (no shared mem).**

## ⛔ (historical) FIRST ATTEMPT — barrier-gating, REVERTED — the WRONG approach; the two-pass above superseded it
**What I tried (option b, two variants) and why it FAILED — do NOT repeat:**
- Gated the synthetic-uniform-break transform on the loop containing a barrier (`WGSLKernelFunctionGenerator.cs` ~8115 pre-loop block + ~8239 break-rewrite, using `loopHasIRBarriers` already computed at 8017). Hypothesis: the transform exists only to keep in-loop barriers uniform, so a barrier-free loop (GEMV K-tile loop) should keep its natural `k < K` break.
- Offline this looked perfect: GEMV probe → broken `(_uf_group_iter * workgroup_size.x) < K` GONE, natural `v_6 < v_0` restored; and a grid-stride-scan-with-helper-barrier probe kept the transform.
- **But the FULL SWEEP found 72 regressions: ALL RadixSort* + GlobalInclusiveScan* on WebGPU + WebGPUNoSubgroups** (`[WebGPU] 115 GPU errors during dispatch` = WGSL uniformity validation: barrier in non-uniform control flow). GEMV itself was FIXED (passed all backends), but scan/radix broke.
- **Root reason (the architectural wall):** cooperative-op barriers (`GroupExtensions.Scan/Reduce`, and RadixSort's barrier mechanism) are emitted by the WGSL INTRINSIC HANDLER at codegen time — they are NOT IR `MemoryBarrier` values, and the callee IR bodies at this point are just thin intrinsic wrappers (verified: `InclusiveScan_15 blocks=1`). So NO pre-emission barrier signal is reliable: direct IR check misses them; transitive IR recursion misses them (no body); a cooperative-type-list (`GroupExtensions`/`WarpExtensions`) caught the scan probe but STILL missed RadixSort's barrier source (62 RadixSort failures remained). 
- Tile-counter path (option a) is ALSO wrong for the GEMV: it forces all threads to thread-0's trip count → out-of-bounds reads when `K % step != 0` (the natural per-thread `k < K` is the only correct break, and it's barrier-free-safe).
- **REVERTED** both codegen files to `63e288d` (green). Repro test removed (it fails until #4 is properly fixed). No regression shipped. ML fallback (master `2752c3b`) keeps WebGPU GEMV correct → #4 stays non-urgent.

**The CORRECT fix needs a reliable "this loop will contain a barrier after inlining" signal** — which the current architecture lacks (barriers materialize at emission). Candidate directions for a fresh, focused session: (1) TWO-PASS loop emission — emit the loop body first, detect emitted `workgroupBarrier()`, then choose the break (biggest, cleanest, but a refactor); (2) have the cooperative-op intrinsic REGISTRATIONS declare "emits barrier" so a pre-pass can query the registry (single-source, no name-list drift) and find them transitively through the call graph including RadixSort's kernels; (3) compute per-loop trip-count-uniformity + barrier-presence together (the transform is needed iff non-uniform-trip AND barrier-inside). The mechanism + the offline probe harness (`DemoConsole subgroup-reduce ... gemv`) are reusable. The minimal repro to re-add when fixing: explicitly-grouped, `Grid.IdxX`-column + inner `k=tid;k<K;k+=64` + shared-mem reduce, CPU oracle (K=512/N=96) — passes on CPU/CUDA/OpenCL/Wasm, was the WebGPU failure.

**Status (orig):** OPEN. Not urgent — Tuvok shipped a correct fallback (`SpawnDev.ILGPU.ML` master `2752c3b`); WebGPU GEMV currently routes to the per-element kernel. This plan re-enables the fast cooperative GEMV on WebGPU.
**Author:** Geordi (ILGPU). Diagnosis by: Tuvok (`_DevComms/global/tuvok-to-geordi-GEMV-wgsl-reduction-is-CORRECT-bug-is-emitter-gridstride-2026-06-13.md`).
**Priority:** TJ's order #2(done) → #1(planned) → **#4 (this)** → #3.

## Symptom
The WebGPU dequant-matmul M=1 GEMV (`FusedDequantMatMul` M==1 path) returns values **~34× too small** = a PARTIAL per-thread accumulation. CUDA/OpenCL/Wasm/CPU are correct; only the WGSL path is short.

## Root cause (Tuvok verified against the WGSL dump — NOT the reduction)
Dump: `SpawnDev.ILGPU.ML/_mldump/2026-06-13_12-16-46/wgsl/007_GemvDequantQ4_KImpl.wgsl`.
- `@workgroup_size(64)` + `var<workgroup> shared_0 : array<f32,64>` — group size correct.
- The shared-mem tree reduction (lines ~1462-1575: `shared[tid]=partial`, 6 steps stride 32→1 with uniform `workgroupBarrier()`, `if (tid==0) output[n]=shared[0]`) is **textbook-correct**. So `GroupExtensions.Reduce` is NOT the fix — it would replace a correct reduction.
- The bug is the **per-thread K-loop accumulation is short**. The emitter wrapped the kernel body in a synthetic GROUP grid-stride loop and **conflated that group iterator with the inner K-loop**:
  - `1089: var _uf_group_iter : i32 = i32(group_id.x + group_id.y * num_workgroups.x);  // = column n`
  - `1092: _uf_break_v_329 = (_uf_group_iter * i32(workgroup_size.x)) < v_4;`  ← the K-loop break uses `_uf_group_iter * workgroup_size`, conflating the group iterator with K
  - `1457: _uf_group_iter = _uf_group_iter + i32(num_workgroups.x * num_workgroups.y);  // grid-stride increment`

## ✅ MECHANISM CONFIRMED from the dump (Geordi, 2026-06-13)
Read `007_GemvDequantQ4_KImpl.wgsl` end-to-end. The inner K-loop induction `v_40` (= `k`, init `tid`=v_8, `v_40 = v_40 + 64` at line 1454-1455, accumulate `v_41` at 1452/1456) is INTACT and correct. The bug is purely the **break + a stray counter**:
- line 1091: `v_329 = v_40 < v_4;` ← the CORRECT condition (`k < K`, v_4=K) is computed... and then DISCARDED.
- line 1092-1093: break uses `_uf_break_v_329 = (_uf_group_iter * workgroup_size.x) < v_4;` ← the WRONG grid-stride break.
- line 1089: `var _uf_group_iter = group_id.x + group_id.y*num_workgroups.x;` (= column n) and line 1457: `_uf_group_iter += num_workgroups.x*num_workgroups.y;` ← a stray grid-stride counter fused into the K-loop. So the loop runs while `_uf_group_iter*64 < K` (starts at n, strides by #groups) → for n≥1 it breaks almost immediately → ~34× short accumulation.

**Why the wrong path is taken (`WGSLKernelFunctionGenerator.cs` ~8133-8186 + `UniformityAnalyzer.cs`):** the transform has a CORRECT tile-loop path (`_uf_tile_iter`, DIRECT comparison `iter compOp limit`, no `*workgroup_size`) but the GEMV K-loop misses it twice:
1. `ClassifyLoopType` (`UniformityAnalyzer.cs:193-242`) returns `TileLoop` only when the loop STEP `stepTrace == GroupDimension`. The GEMV steps by the **literal `GemvGroupSize` (64)**, not `Group.DimX`, so it classifies as **GridStrideLoop** → grid-stride break path.
2. Even forcing the tile path, the tile-counter init decomposition bails: the phi init is `k = tid` = PURE `Group.IdxX`, so `TryRemoveGroupIndex` returns `""` (`UniformityAnalyzer.cs:323-324`); the transform guard `if (uniformInit != null && uniformInit != "")` (`WGSLKernelFunctionGenerator.cs:8144`) then FALLS BACK to `_uf_group_iter`. A pure-GroupIndex init should decompose to `"0"` (thread 0's start), not `""`.

**Also note:** the GEMV K-loop has NO barrier inside it (the reduction's `workgroupBarrier` is AFTER the loop), so a non-uniform break (`k < K`) is actually LEGAL WGSL here — the uniform-break transform arguably should not fire for a barrier-free loop at all. Three candidate fixes to weigh (verify with the repro, don't regress the auto-tile fold): (a) make `TryRemoveGroupIndex` return `"0"` for pure-GroupIndex + make `ClassifyLoopType` recognize a constant step equal to the (specialized) workgroup size as a TileLoop; (b) only apply the synthetic-uniform-break transform to loops that CONTAIN a barrier; (c) when the original condition is already uniform-safe (no barrier in body), keep the original `k < K` break. Option (b)/(c) are the most principled (the transform exists to satisfy barrier uniformity); (a) is the most localized.

## The kernel shape that triggers it
An **explicitly-grouped** kernel (`LoadStreamKernel` + `KernelConfig(N, 64)`) where:
- `Grid.IdxX` = the output column `n` (used as an OUTPUT INDEX), and
- `Group.IdxX` = `tid`, and
- an **inner strided K-loop** `for (int k = tid; k < K; k += GemvGroupSize) partial += input[k] * Decode(...);` (step = GroupDimension = 64).

This is the same emitter family as the attention `u_param8` skip (both grouped-kernel WGSL codegen bugs surfaced by the same browser run).

## Suspected location
`SpawnDev.ILGPU/WebGPU/Backend/UniformityAnalyzer.cs` — the loop classifier (`LoopType.TileLoop` step=GroupDimension vs `LoopType.GridStrideLoop` step involves `num_workgroups`, lines ~64/183-242) and the uniformity transform that synthesizes `_uf_group_iter` / `_uf_tile_iter` (detected at `WebGPUBackend.cs:1222`). The inner K-loop steps by GroupDimension (64) so it should be a **TileLoop**, but the transform appears to (a) wrap the whole grouped kernel in a synthetic group grid-stride counter `_uf_group_iter` AND (b) reuse/conflate that counter in the inner K-loop's break (`_uf_group_iter * workgroup_size.x < K`). Either the classification mislabels the inner K-loop, or the synthetic group-iter counter leaks into the inner loop's induction/break.

NOTE: `FixGridStrideLoopUniformity` was DELETED in Phase 0.4 (`WGSLKernelFunctionGenerator.cs:3569`); the current transform replaced it. Understand the CURRENT transform before editing.

## Fresh-session steps
1. **Read the dump end-to-end** (`007_GemvDequantQ4_KImpl.wgsl`) — map `_uf_group_iter`, `_uf_break_v_329`, the K-loop body (`v_219` = partial), and how the inner loop's break/induction reference `_uf_group_iter`. Confirm exactly how the conflation happens.
2. **Trace the emission** back to `UniformityAnalyzer` (classification) + the uniformity transform (counter synthesis). Determine whether the bug is misclassification (inner K-loop tagged grid-stride) or counter-leak (group iter reused in the inner loop).
3. **Minimal repro** in SpawnDev.ILGPU's own test suite: an explicitly-grouped kernel (`KernelConfig(N,64)`, `Grid.IdxX` output index + inner `for k=tid;k<K;k+=64` accumulate + shared-mem reduce) with a CPU-reference oracle — reproduces the short accumulation on WebGPU, passes on CUDA/OpenCL/Wasm/CPU. (This kernel family had NO ILGPU-side regression test; add one.)
4. **Fix the transform** so the inner K-tile-loop keeps its own induction (`k`, step=GroupDimension) and is NOT conflated with the implicit group grid-stride wrapper. Keep the group-level grid-stride correct for the >65535-column auto-tile case (don't regress `Grid2D_ExceedsMaxWorkgroupsX_FoldsCorrectly`).
5. **Verify** cross-backend (CPU oracle) + the full WGSL uniformity-sensitive suite (grid-stride reductions, tile loops, the auto-tile fold). Then in ML, remove Tuvok's WebGPU fallback gate so the fast grouped GEMV runs on WebGPU, and re-measure decode.

## Test rigor
- The minimal repro (step 3) becomes the regression guard — explicitly-grouped kernel with Grid.IdxX-output + inner GroupDimension-strided loop, CPU-referenced, all 6 backends.
- Don't regress: `Grid2D_ExceedsMaxWorkgroupsX_FoldsCorrectly` (the >65535 auto-tile fold uses the free Z dim), grid-stride `Reduce`/`Scan`, tile-loop kernels.
- Coordinate with Tuvok: he has `Gemv_M1_Quantized{Q4K,Q6K,Q8_0,Q4_0}_MatchesOracle` (WebGPU was gated by the sync-Synchronize throw, now resolved) — those become the ML-side end-to-end check after the fix + fallback removal.

## Key file references
- `SpawnDev.ILGPU/WebGPU/Backend/UniformityAnalyzer.cs` (loop classification + `LoopType`).
- `SpawnDev.ILGPU/WebGPU/Backend/WGSLKernelFunctionGenerator.cs` (uniformity transform, `_uf_group_iter`/`_uf_tile_iter`; index setup ~3972; `FixGridStrideLoopUniformity` deleted note ~3569).
- `WebGPUBackend.cs:1222` (`UniformityTransformApplied` detection).
- Dump: `SpawnDev.ILGPU.ML/_mldump/2026-06-13_12-16-46/wgsl/007_GemvDequantQ4_KImpl.wgsl`.
- ML fallback: `SpawnDev.ILGPU.ML` master `2752c3b`; GEMV decode context: memory `project-gemma4-decode-gemv-4.5x-2026-06-13`.

## Bottom line
A targeted WGSL uniformity-transform fix for explicitly-grouped kernels that use `Grid.IdxX` as an output index plus an inner GroupDimension-strided loop. The reduction and group size are correct; only the inner-K-loop / group-grid-stride conflation is wrong. Fallback keeps ML correct meanwhile, so this is a perf-recovery, not a correctness emergency.
