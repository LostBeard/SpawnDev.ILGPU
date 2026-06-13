# Plan: Subgroup/warp reduction intrinsics for the ML reduction family (#1 — Tuvok's highest-value perf lever)

**Status:** OPEN. Recommended to START ON A FRESH CONTEXT (full quota) — this plan tees it up.
**Author:** Geordi (ILGPU). Requested by: Tuvok (`tuvok-to-geordi-ilgpu-perf-asks-from-ML-2026-06-13.md` ask #1).
**Priority:** TJ's order is #2(done, non-fix) → **#1 (this)** → #4 (GEMV WebGPU) → #3 (Wasm SIMD128).

## Goal (Tuvok's words)
Let ML write a cooperative reduction as a one-liner (`WarpReduce.Add/Max`, `GroupReduce`, or raw `Warp.ShuffleDown`) that lowers to the fast register-only subgroup path where available, instead of hand-rolling `SharedMemory.Allocate` + tree loop + `Group.Barrier()` per kernel. The reduction/RMSNorm/LayerNorm/softmax/attention-score/dequant-matmul-GEMV family is the prime beneficiary. Tuvok will convert that family and re-measure once it's confirmed.

## ⚠ CRITICAL: most of this ALREADY EXISTS — survey/verify FIRST, do not rebuild
A survey on 2026-06-13 found the infrastructure largely in place. **Step 1 of the fresh session is to empirically confirm what works, not to build from scratch** (see the "duplicate-a-registry" lesson — don't re-implement existing gear).

### Existing intrinsics (the fork, `ILGPU/`)
- `Warp.cs`: `Warp.Shuffle` / `ShuffleDown` (`WarpIntrinsicKind.ShuffleDown`/`SubShuffleDown`) / `ShuffleXor` / `WarpSize` / `LaneIdx` / `IsFirstLane`. The raw shuffle API exists.

### Existing high-level reduce/scan (the fork, `ILGPU.Algorithms/`)
- `GroupExtensions.cs` (+ `PTX/PTXGroupExtensions.cs`, `CL/CLGroupExtensions.cs`, `IL/ILGroupExtensions.cs`): `Group.Reduce` / `AllReduce` / scan.
- `WarpExtensions.cs` (+ `PTX/PTXWarpExtensions.cs`, `CL/CLWarpExtensions.cs`, `IL/ILWarpExtensions.cs`): warp-level reduce/scan.
- `ReductionExtensions.cs`, `IScanReduceOperation.cs` (the `AddFloat`/`MaxFloat`/... ops).

### Existing per-backend lowering
- **WebGPU/WGSL — ALREADY emits subgroup intrinsics WITH fallback.** `WGSLCodeGenerator.cs:3561` ("Emits subgroupMax/Min/Add when subgroups are available, otherwise falls back"), `:3199` `subgroupBroadcastFirst`, `:2588` (guards: subgroupMax/Min/Add do NOT work on emulated vec2<u32> 64-bit — f32/i32 only), `:2634` `_num_sgs = workgroup_size.x / subgroup_size`. `WebGPUBackend.cs:1130-1140` auto-injects `enable subgroups;` when the WGSL uses them; `:770`. `WebGPUBackendOptions.cs:25` `DisableSubgroups` toggle. `SpawnDev.ILGPU/WebGPU/Algorithms/WebGPUWarpExtensions.cs` exists.
- **CUDA/PTX:** `PTXWarpExtensions`/`PTXGroupExtensions` (upstream ILGPU — `__shfl_down_sync` etc.).
- **OpenCL/CL:** `CLWarpExtensions`/`CLGroupExtensions` (device subgroup ext, gated).
- **Wasm:** EMULATED — `WarpSize=8`, `Warp.Shuffle`/`SubWarpShuffle` lower to a shared-mem exchange (`WasmKernelFunctionGenerator.EmitWarpShuffle`, `WasmWarpExtensions`); verified vs CPU oracle (`116c789`, 2026-06-07).
- **WebGL:** structurally impossible (no shared memory/barriers) — already throws `UnsupportedKernelFeatureException` for in-kernel Scan/Reduce (`GLSLCodeGenerator.cs:2088`); gated by `RequiresSubGroups`/`RequiresSharedMemory`.
- **CPU/IL:** `ILWarpExtensions`/`ILGroupExtensions`.

### Existing capability gate
`AcceleratorRequirements.RequiresSubGroups` (rules out WebGL, Wasm, CPU; WebGPU w/o adapter feature; OpenCL w/o ext) — selection-time filter already wired.

## So what's the ACTUAL gap? (resolve in the fresh session — likely small)
Hypotheses, in order of likelihood:
1. **ML simply isn't USING `Group.AllReduce`/`WarpExtensions.Reduce`** — it hand-rolls shared-mem trees because the author didn't know the gear exists or that WGSL already takes the subgroup fast path. → The "fix" is mostly: confirm the existing API performs, then help Tuvok adopt it (he converts the family + re-measures). LOW code.
2. **A coverage/perf gap** in the existing reduce — e.g. only certain element types take the subgroup path, the f32/i32-only subgroup restriction (`WGSLCodeGenerator.cs:2588`) forces fallback for some ML dtypes, or the group-level (vs warp-level) reduce doesn't compose the subgroup partial-reduce + shared-mem cross-subgroup step optimally. → Targeted codegen work.
3. **A genuinely missing primitive** Tuvok needs (e.g. `subgroupShuffleDown`-based segmented reduce, or a fused reduce-broadcast). → New intrinsic.

## Fresh-session steps
1. **Survey/verify (no code):** write a tiny kernel using `Group.AllReduce<float, AddFloat>(x)` (and `WarpExtensions.Reduce`); dump the WGSL (set the ShaderDebugService dump folder first — it is NOT set in SpawnDev.ILGPU) and CONFIRM `subgroupAdd` + the fallback are emitted; confirm PTX uses `__shfl`; run on CPU/CUDA/OpenCL/Wasm/WebGPU with a CPU-reference oracle. Measure subgroup-reduce vs hand-rolled shared-mem tree on a representative reduction (Tuvok's GEMV/RMSNorm shape).
2. **Pin the gap** against the three hypotheses above using the survey result + Tuvok's actual kernel code.
3. **Close the gap** (whichever it is) — keep changes in the codegen / algorithm layer, one backend at a time, cross-backend equivalence test vs CPU oracle EACH (the `feedback-...-cross-backend-equivalence-test` discipline — CUDA-only ships Wasm/WebGPU-broken).
4. **Hand Tuvok the confirmed API** + a worked example (one converted ML reduction kernel) so he converts the family and re-measures.
5. Gate anything subgroup-only behind `RequiresSubGroups`; ensure the non-subgroup backends (Wasm emulated, WebGL throw) stay correct.

## Test rigor
- CPU reference comparison is the floor for every reduce (`SubgroupShuffleTest`, `ReduceMinMaxTest` are existing models).
- Cross-backend equivalence test per op (all 6 backends) — a subgroup reduce that's right on CUDA can be wrong on Wasm emulation / WGSL fallback.
- Use `GpuTestVerify` (GPU-side) where possible — data stays on GPU (Rule 4/5).
- Verify the f32/i32-only subgroup restriction (`WGSLCodeGenerator.cs:2588`) doesn't silently fall back for the ML dtypes that matter (does the fallback produce correct results? it must).

## Key file references
- Fork intrinsics: `ILGPU/Warp.cs`, `ILGPU/Group.cs`.
- Algorithm layer: `ILGPU.Algorithms/{GroupExtensions,WarpExtensions,ReductionExtensions,IScanReduceOperation}.cs` + `{PTX,CL,IL}/*.cs`.
- WGSL subgroup codegen: `SpawnDev.ILGPU/WebGPU/Backend/WGSLCodeGenerator.cs` (~2328 subgroup_size, ~2588 64-bit guard, ~3199 broadcastFirst, ~3561 reduce-with-fallback), `WebGPUBackend.cs:770/1123-1140`, `WebGPUBackendOptions.cs:25`, `WebGPU/Algorithms/WebGPUWarpExtensions.cs`.
- Wasm emulation: `WasmKernelFunctionGenerator.EmitWarpShuffle`, `WasmWarpExtensions`, `WasmBackend.WasmWarpSize`.
- WebGL throw: `WebGL/Backend/GLSLCodeGenerator.cs:2088`.
- Capability gate: `SpawnDev.ILGPU/AcceleratorRequirements.cs` `RequiresSubGroups`.
- Tuvok's ask + motivation (the M=1 GEMV 6.1x measurement): `_DevComms/global/tuvok-to-geordi-ilgpu-perf-asks-from-ML-2026-06-13.md`.

## Bottom line
This is probably MUCH smaller than "build subgroup intrinsics" — the gear largely exists, including the WGSL subgroup fast path with fallback. The fresh session should VERIFY-then-adopt (with Tuvok), not rebuild. The biggest risk is a silent dtype fallback or a CUDA-only-correct reduce; cross-backend CPU-oracle tests are the guard.
