# CPU backend SIMD vectorization

**Status: PLANNED.** Captain 2026-09-08: *"CPU backend vectorization is a good feature to add, but I agree
not right now so log it as planned where we will not miss it... and we'll do it soon."*

## The gap, measured

The CPU accelerator is the **only backend with no vectorization at all**. Every other backend either runs
on real GPU silicon or, on Wasm, emits 4-wide `v128` SIMD.

MEASURED 2026-09-08, RMBG-1.4 (U2Net, 491 executor nodes), identical model and input, 256x256 versus its
native 1024x1024 - **16x the pixels**:

| backend | 256x256 | 1024x1024 | growth |
|---|---|---|---|
| OpenCL | 5.9 s | 5.7 s | flat |
| CUDA | 5.2-7.2 s | 6.7 s | flat |
| WebGL | - | 26.3 s | - |
| WebGPU | - | 32.3 s | - |
| **Wasm** | 74 s | **89 s** | **1.2x** |
| **CPU** | 27 s | **336 s** | **12.4x** |

Flat or 1.2x growth against 16x the work means the number is dominated by FIXED cost (a 170 MB model load
plus graph compile), not compute. **12.4x means compute-bound and roughly linear in element count** - the
signature of scalar execution.

⚠️ The CPU-vs-Wasm ranking INVERTS with resolution: CPU is ~3x faster at 256x256 (27 s vs 74 s) and ~3.8x
slower at 1024x1024 (336 s vs 89 s). Never state that comparison from a single resolution; their
fixed-versus-marginal cost profiles are opposite.

## The cause, from source

**Wasm vectorizes.** `SpawnDev.ILGPU/Wasm/Backend/` carries `WasmSimdAnalysis.cs` (decides when a kernel is
vectorizable from the IR) and `WasmSimdKernelEmitter.cs`, emitting real `v128.*` opcodes (`v128.const`,
`v128.bitselect`) with `SimdLane` / `SimdKernelCode` / `SimdKernelLocals` plumbing - 4-wide f32 - on top of
its worker-thread pool.

**CPU does not.** `CPUAccelerator` initialises with `context.DefautltILBackend` (upstream's typo), the IL
backend, which emits SCALAR IL executed per work-item on managed threads. A grep for `Vector<T>` /
`Vector128` / `Vector256` / `System.Runtime.Intrinsics` / `Simd*` across `ILGPU/Runtime/CPU` and
`ILGPU/Backends/IL` returns **zero hits**.

## What is NOT the problem (already checked, do not redo)

- **Not the device config.** `CPUDevice.Default` is already tuned: warp 8 x 8 warps = 64-thread groups,
  `numMultiprocessors = Environment.ProcessorCount`. Its own comment records that it used to be a hardcoded
  1, which crammed all parallelism into one oversubscribed group and caused in-kernel `Group.Barrier`
  thrash. **The `CPUDevice.Nvidia` / `AMD` / `LegacyAMD` / `Intel` presets are GPU-shape SIMULATORS for
  debugging, all `numMultiprocessors: 1` - they would be SLOWER.** Changing the preset is not the lever.
- **Not thread-level parallelism.** The CPU lane already runs `ProcessorCount` groups.
- **Not contention.** The slow measurement was taken with `PMT_PARALLEL=off`, sequential, no sibling
  competition.

## Approach

1. **Reuse the analysis, do not write a second one.** `WasmSimdAnalysis` decides vectorizability from the
   ILGPU IR, and that reasoning is not Wasm-specific. Lift it to a shared location so both backends consume
   one implementation. Two analyses for one job is worse than either - whichever is authoritative would
   drift.
2. **Emit `System.Runtime.Intrinsics.Vector128<T>`** from the IL backend for the kernels the analysis
   accepts, keeping the existing scalar path as the fallback for everything it rejects.
3. **Vectorize ACROSS work-items** (the Wasm model: lanes are consecutive work-items), not within a single
   work-item's expression tree - that is what makes elementwise and Conv-shaped kernels vectorize cleanly.
4. Consider `Vector256<T>` behind a capability check once `Vector128` is correct. Correctness first.

## Verification

- **CPU oracle parity is the floor**: every vectorized kernel must match the scalar CPU path per element,
  not by RMS or absMax (an aggregate can match while per-element values are wrong).
- Gate with the existing operator suite (`PMT_FILTER=Operator`, 97/97) plus the group the change touches.
- The headline regression check is the measurement above: re-run
  `Pipeline_BackgroundRemoval_ProducesMask` (RMBG at 1024x1024, `HeavyModel,HeavyCpu`, 900 s budget) and
  compare the CPU figure against the recorded **336,354 ms** baseline. It passes today, so this is a
  before/after, not a fix-the-red.
- ⚠️ **Do not predict a 4x from a 4-lane width.** A lane count is not a cost until multiplied by a measured
  per-op price; memory bandwidth and the per-node .NET orchestration will take their share. Measure the
  end-to-end number and report that.

## Why it matters beyond the CPU lane

The CPU accelerator is the correctness ORACLE the other five backends are checked against, and it is the
backend most often used while iterating. Its speed is developer throughput, not just a benchmark row.

Related memory: `reference-the-cpu-backend-is-the-only-one-with-no-simd`.
