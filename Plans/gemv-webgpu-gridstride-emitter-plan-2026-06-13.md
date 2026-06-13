# Plan: Fix the WebGPU GEMV grid-stride emitter bug (#4) — re-enable the fast grouped GEMV on WebGPU

**Status:** OPEN. Not urgent — Tuvok shipped a correct fallback (`SpawnDev.ILGPU.ML` master `2752c3b`); WebGPU GEMV currently routes to the per-element kernel. This plan re-enables the fast cooperative GEMV on WebGPU.
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
