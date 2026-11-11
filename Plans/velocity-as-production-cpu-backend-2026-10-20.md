# Velocity as the production CPU backend (2026-10-20)

TJ: the plain CPU backend is ILGPU's debugging backend, never meant for real work, yet ML runs its CPU lane and its heavy model tests on it (the CPU heavy group of an all-categories ML sweep took 2 h 13 min of 8 h 14 min).
The fork already contains ILGPU's SIMD CPU backend, **Velocity** (`ILGPU/Backends/Velocity`, `Scalar2` and `Vector128`; the Wasm SIMD backend was ported from it). Nothing used it. Direction: make Velocity a real backend, keep the plain CPU backend for debugging, and
measure before and after every step (AGENTS.md Rule 12, `D:\users\tj\Projects\_TestStats`).

## Baseline (before any change), ML kernels, 2 timed iterations, same machine
| Kernel | CPU backend | Velocity Scalar2 | Velocity Vector128 | CUDA |
|---|---|---|---|---|
| MatMul 512 x 512 x 512 | 93.6 ms (2.9 GFLOPS) | 105.5 ms | 42.6 ms (6.3 GFLOPS) | 0.1 ms |
| GEMV 1 x 2048 x 2048 | 4.72 ms | 4.78 ms | 1.90 ms | 0.10 ms |
| LayerNorm 1369 x 384 | 411.4 ms | 2.68 ms | 0.69 ms | 0.04 ms |
Identical results (checksum). The plain CPU backend is pathologically slow on group-per-row kernels (barriers between emulated threads); Velocity runs a group as one warp.
ML light suite, same 1027 tests: CPU lane 22:35 (+ 1:43 heavy), Velocity lane 18:43 (+ 1:03 heavy), but Velocity fails 150 of 1043 (below); the light suite is dominated by per-test context creation and kernel compile, so the kernel speedup shows in the heavy model tests, not there.

## Where Velocity fails today
ML suite on Velocity (1043 tests): 854 pass, 150 fail: group dimensions over the maximum 38, divide by zero 60, Half / BFloat16 / FP8 types 42, 4 of the new GPU mesh kernels (UV atlas, simplifier), 6 other.
ILGPU's own suite on Velocity (`VelocityTests`, 727 tests): 443 pass, 212 fail, 74 skipped:
- 27 group dimensions and 12 grid dimensions over the maximum: Velocity sets `MaxNumThreadsPerGroup = WarpSize` (4), so any kernel written for a 64 / 256 thread group cannot launch. Fix: multi-warp groups (the Wasm backend already runs several warps per group with barriers).
- about 70 for types: Half (`FloatN is not supported on this device`, 26), BFloat16 (`Specified method is not supported`, 18, `InvalidProgram` 9, no intrinsic 7), FP8/FP4 (no intrinsic, 10), packed 4-bit (9). Fix: codegen support in Velocity like the Wasm backend got.
- **WRONG ANSWERS, not crashes (most urgent):** `RadixSortDescending*` and `RadixSortBoundary*` (descending order violated, 5 tests), `ILGPUReduceUInt` (`Reduce<MaxUInt>` wrong, 2), `HelperCodegen_EarlyReturnSkipsLaterLoop_CallerBranches` (an early return runs the wrong branch), `F64Emulation_MathOperations` (`Round` differs, 2).
- `IndexOutOfRange` 12 (packed 4-bit loads), `Unknown SpawnJSInterop method` 2 (unrelated to Velocity: a JS-interop test run on a desktop lane).

## Done
- **Integer division and remainder in masked SIMD lanes (fixed).** A SIMD warp executes all four lanes of every instruction, including lanes whose result is unused (the tail of the last warp, lanes switched off by `if (divisor != 0)`); a zero divisor in any lane made the vector divide throw for the whole warp. 60 of the 150 ML failures. `NonZeroDivisor` in `Vec128Operations.tt` / `.cs` (zero divisor lanes become one). Test `SimdLaneDivision_TailLanesAndGuardedLanesDoNotThrow`: red on Velocity without the fix (`DivideByZeroException`), green with it, same answers on CPU and CUDA.
  `Scalar2` (`ScalarOperations.tt`) has the same shape and still needs the same guard.

- **A loop with more than one exit block lost the lanes of all but one of them (fixed).** `for (...) { if (c) return false; } return true;` has two exit blocks (the early return and the normal exit). Lanes that leave a loop wait in the mask of the exit block they leave to;
  when no lane is left in the loop the generated check jumped straight to the exit block of the LAST lane to leave, skipping the block where the earlier lanes waited: they never reached the merge block, never stored, and their output stayed uninitialised (garbage or 0).
  Found with a four-shape repro (one loop / nested loops, with and without a later loop; every shape failed on Vector128 and Scalar2), IR from `DEBUG_VELOCITY`. Fix: `VelocityMasks.GetFirstExit(loop)` (the exit generated first) and `BranchBuilder.RecordBranchTarget` continues there and flows through
  the other exit blocks (a block without active lanes does nothing; `return` only jumps when every lane is done). Test `LoopEarlyReturn_LanesLeavingThroughEitherExitStore` (all backends): red on Velocity without the fix ("thread 0 got 0, expected -1"), green with it.
- **Float remainder was `x - x / y * y` (fixed, `.tt` and `.cs`).** No truncation of the quotient, so `-7.5 % 2` was 0 on Vector128 (Scalar2 already used `%` per lane). Now every lane computes the .NET `%` the CPU backend uses. `FloatRemTest`, `FloatRemainder_Truncates_*`, `Remainder_IsExact_*` went green.
- ILGPU suite on Velocity after both: **451 pass / 205 fail / 74 skipped** (was 443 / 212 / 74), nothing newly failing. Remaining 205: types 75, group operations 73 (scan, reduce, radix, shared memory, barriers: need multi-warp groups), group / grid dimensions 41, packed 4-bit index errors 7, SpawnJS 2 (not Velocity), 7 others.
- **NaN comparisons in branches (fixed).** `GenerateCode(CompareValue)` ignored `CompareFlags.UnsignedOrUnordered`: the optimizer inverts `if (x < y)` into an unordered `x >= y`, which Velocity computed ordered, so NaN took the `x < y` branch (flags 62 instead of 2 for NaN operands, as a value it was right). An unordered float compare is now the ordered compare OR IsNaN(left) OR IsNaN(right). `Float/DoubleNaNComparisonTest` green.
- **A `bool` kernel parameter arrived false on Vector128 (fixed).** The launcher broadcast `true` as 1, but a Velocity boolean lane is a mask (all bits set): selects are bitwise, so `flag ? a : b` gave b. Now negated to -1 when broadcast (`VelocityGenerationModule.BuildScalarParameterLoad`). `BoolScalarParam_TrueAndFalse_Arrive` green (Scalar2 was right already).
- ILGPU suite on Velocity now **454 pass / 202 fail / 74 skipped**.
- **Thread groups of several warps (done, 10-20 evening).** Before: a group had at most one warp (MaxNumThreadsPerGroup = 4), so every explicit group size above 4 was rejected ("Invalid group dimensions"), ILGPU.Algorithms group scan / reduce / radix sort returned zeros and Group.Broadcast reached only its own warp.
  Design, two cases chosen per kernel at launch (`usesGroupSynchronization`, set when a barrier is generated):
  1. **No barriers (any group up to 256 threads):** the kernel's own iteration loop covers the next `min(WarpSize, groupDim - globalIndex % groupDim)` threads of the current group per iteration, so a group of 64 is 16 consecutive warps on one thread; `Group.IdxX = (globalIndex % groupDim) + lane`. Cheap (no threads, no synchronisation).
  2. **Barriers (group of more than one warp):** `VelocityGroupTeamRunner`: persistent worker threads, one per warp, in teams (`teams = cores / warps`); each team runs its groups one after the other (`GroupEnd` barrier so the next group does not reuse shared memory early). `VelocityGroupBarrier` is a GPU-style barrier: a warp whose kernel returned `Leave`s and is not waited for (early `return` before a barrier does not deadlock), an exception in one warp aborts the others. Shared memory: one pool per team, every warp allocates in the same order from its own offset so addresses agree (`AllocateAt`). Predicate barriers (PopCount / And / Or) are a lane count summed over the warps of the group (`GroupSum`); `Group.Broadcast` goes through shared memory and two barriers (a shuffle reaches only one warp).
  Launch limit 256 threads (`MaxGroupSize`; 64 warps = 64 threads per barrier team, not the 1024 of a GPU); the size algorithms and tests pick (`MaxNumThreadsPerGroup` = `MaxNumThreadsPerMultiprocessor`, which Algorithms divide by each other) is 16: see Barrier performance below.
- **ILGPU.Algorithms had no intrinsic mapping for Velocity (fixed).** `[IntrinsicImplementation]` methods (GroupExtensions / WarpExtensions Reduce, AllReduce, scans, radix sort building blocks) are registered per backend type (IL, PTX, OpenCL); with no Velocity registration the call compiled to nothing and returned zero. `ILContext.RegisterIntrinsicMapping` now also registers a `VelocityIntrinsic` redirect to the same IL-level implementations (shuffle based).
- Tests (all backends, red on Velocity before / green after, CPU and CUDA green as controls): `LargeGroups_ThreadGroupAndGridIndicesAreRight`, `GroupBarrier_SeveralWarps_SharedMemoryReductionPredicatesEarlyReturnBroadcast`.
- **ILGPU suite on Velocity now: 559 pass / 100 fail / the rest skipped (was 443 / 212).** The 100: types 75 (Half / BFloat16 / FP8 / FP4 / packed 4-bit), grid and group DIMENSIONS above 1D 12, other 13 (SpawnJS 2, F64-emulation Round x3, `NoInliningIdct16Row32...` zero-input math bug, packed 4-bit index errors 7).
- **Barrier performance (done, 10-20 evening).** Measured first (`velbench`, 12 logical processors): a barrier cost 0.8 us with 2 warps, 5 to 15 us with 8 to 16 warps and 20 to 58 us with 64 warps (more warp threads than processors: a barrier then waits for a thread the OS has not scheduled), and the radix sort of 1.4 M keys took 48 s on Velocity against 10 s on the CPU backend. Three changes:
  1. **Preferred group size 16** (`PreferredGroupThreads`: `MaxNumThreadsPerGroup` and `MaxNumThreadsPerMultiprocessor`, which algorithms and tests read) while launches up to 256 stay valid (`MaxGroupSize`). Radix sort of 1.4 M keys by group size: 8 threads 4.3 s (WRONG, sorted=False: the radix sort needs 16 threads, one per counter), 16 threads 8.7 s, 32 threads 10.5 s, 64 threads 40 s, 256 threads 48 s.
  2. **Lock-free barrier** (`VelocityGroupBarrier`): participants and arrived in one word, arriving and leaving are CAS loops; 0.5 to 0.8 us per barrier while the team fits on the processors (the lock-based one 0.8 to 4.2 us).
  3. **Adaptive wait:** spin a few microseconds then yield while the launch has no more warp threads than processors; yield at once when it has (`Oversubscribed`, set per launch): spinning in that case burnt the time slice of the very thread that was needed (52 to 345 us per barrier).
  Result, one process alone: radix sort of 1.4 M keys CPU backend 7.8 s, Velocity 6.7 s (it was 48 s); the 12 largest scan and radix tests 1 to 19 s each (53 to 190 s before; PMT limit 600 s); `GlobalInclusiveScanAtScaleTest` CPU backend 131 s, Velocity 1 s. Several test processes at once (PMT runs 4 CPU lanes in parallel) each want all processors, so they still slow each other down.
  Stress: the barrier-group repro (shared memory, tree reduction, predicates, early return, broadcast) 8 runs in a row without a failure.
- **Other-backend findings from the new tests (not fixed):** CPU backend: `Group.BarrierAnd(p)` after other predicate barriers returned true for one thread per group where Velocity and CUDA are right. OpenCL: `Group.BarrierAnd` returned false for a group of 8 threads where every thread satisfies it (the predicate part of the shared test is skipped on OpenCL). WebGPU / Wasm: a barrier after a divergent `return` is invalid (WGSL needs uniform control flow) or deadlocks (Wasm: the three tests after it timed out), so that case is its own test, run on CPU / CUDA / Velocity only.
- **The full ILGPU sweep (default lanes, 5267 tests) takes 32 min of wall time (Phase A 22:04), NOT the 3.5 min of the `dotnet test` "Duration" line** (that line is only the end-of-run reporting; PMT runs the tests in its setup). Use the "All scheduled tests complete in" line. 10-20 run: 4841 passed, 9 failed: 6 were the new test before the split (WebGPU x2, Wasm x1 + 3 `KernelArity` timeouts it caused, OpenCL), CUDA `Tests_MemoryPressure_...` (known: the browser lanes hold GPU memory), CPU `GlobalExclusiveScanAtScaleTest` (409 s alone, over PMT's 600 s per-test limit under the parallel load: the CPU backend again, Velocity does it in 7 s).
- **Low precision types (done, 10-20 night).** Half, BFloat16, FP8 (E4M3, E5M2), FP4 (E2M1) are float lanes that always hold a value of the narrow type (the f32-register model of the Wasm backend): decoded from / encoded to their bits at load, store, kernel argument, constant and `FloatAsInt` / `IntAsFloat`, rounded to the narrow type after every arithmetic operation and conversion (`ConvertValue` converts through the lane type, `VelocityNarrowTypes`, `*Operations.NarrowTypes.cs`). The conversions are the C# structs applied per lane, so results equal the CPU backend. QInt4 / QUInt4 are raw nibbles in integer lanes (extended on conversion by the arithmetic type). Views of 4-bit types are packed two per byte: the element address counts nibbles (LEA gives `base * 2 + index`), the load reads the nibble, the store replaces it with a compare-exchange on the containing 32-bit word; the fields of structures (radix sort pairs) are one byte each. The type table had no QInt4 entry (the `IndexOutOfRange`). About 110 tests, all green on Vector128; a sample of 129 green on Scalar2.
- **Grids and groups of up to three dimensions (done).** The execution context holds the launch dimensions (grid, group, extent of an implicitly grouped kernel); Grid / Group index and dimension values decompose the linear thread number (a one dimensional launch takes a branch that skips the division). Implicit Index2D / Index3D kernels get their index structure from grid index * group size + thread index and mask the coordinates beyond the extent. Device limits: grid (int.MaxValue, 65535, 65535) like the CPU backend, group up to 256 threads in total (checked at launch). 12 tests.
- **Buffers are zeroed (done).** `VelocityMemoryBuffer` handed out `AlignedAlloc` memory; every other backend (and the CPU backend) hands out zeroed buffers, and two tests read slots nobody wrote (`NoInliningIdct16Row32...`, `SubViewRange_HighDispatchCount`: garbage / NaN).
- **`XMath.RoundToEven` / `RoundAwayFromZero` / `IEEERemainder` on Velocity (done).** They are `[IntrinsicImplementation]` whose managed body calls `MathF.Round(.., mode)`, which the Algorithms remapping sends back to `XMath.Round`: a cycle that returned 0 on a backend without its own implementation (PTX and OpenCL have one). `ILMath` (Algorithms) + a `VelocityIntrinsic` redirect (`ILContext.EnableILMathAlgorithms`). That was the `Round` failures, not the test context.
- **External methods now fail loudly on Velocity.** A method without a body (an internal call such as `ModF`) compiled to a function that returned the wrong value; `VelocityBackend` now throws `NotSupportedException` naming it. `MathF.Truncate` / `Math.Truncate` (internal call `ModF`) remain unsupported without ILGPU.Algorithms (`XMath.Truncate` via the remapping works): a general fix needs a core `TruncateF` operation for every backend (CUDA fails at PTX JIT).
- **Test lane gates:** the JS stream tests skip on Velocity (browser only); the fused FFN tests gate on what a backend can launch (`MaxGroupSize.X` on Velocity: it prefers 16-thread groups but launches 256), so they run; `VelocityScalar2Tests` (not registered) runs the same tests on the two-lane scalar device.
  `NoInliningIdct16Row32CallsZeroInput...` (garbage under zero input). The three `Round` failures
  (`FloatRoundToEven_And_LongMinMax`, `F64Emulation_*`) come from the F64-emulation test context, not from Round itself: the same kernel is right on Velocity in a plain context (Velocity has native doubles; those tests need not run on it).

## Open work (possible, not scheduled)
- **Velocity on 32-bit processes (x86, ARM32).** Tested 10-20 with a win-x86 repro on the 32-bit .NET 10 runtime after bypassing the `VelocityPlatform64` guard in `Velocity()`: even the plain vector-add kernel dies with `InvalidProgramException` in the generated launcher (`ILGPULauncher`). Cause: the IR pointer type is `Int32` (4 bytes) on a 32-bit target (`IR/Types/PointerTypes.cs`) while Velocity models every pointer as a 64-bit lane (`VectorizedTypeProvider.GetPointerType`, `Load*`/`Store*`/atomics/LEA take 64-bit address warps), so the generator mixes 32-bit and 64-bit operations on the same pointer values. Plan: keep pointer lanes 64-bit and make the generator treat every pointer-typed value as 64-bit regardless of the IR width (`IsTreatedAs32Bit` for values whose type is a `PointerType`, about 24 sites, plus phi / select / `ConditionalSelect`); `IntAsPointerCast` widens, `PointerAsIntCast` narrows when the IR integer is 32-bit; pointers stored in memory (structure fields, pointer-to-pointer) load as 4 bytes and widen, store by narrowing (`CreateNonStructureLoad` / `GenerateNonStructureStore`); `BuildScalarParameterLoad` / `VelocityArgumentMapper` for the host `IntPtr` fields; then drop the guard (keep it for platforms without `Vector128` software fallback if any) and let `AllAccelerators()` register Velocity on 32-bit too. Test: the same `VelocityTests` run under the x86 runtime (repro: a vector add, 2D index, group barrier reduce, atomic, Half, long / double math, radix sort; first failure is the launcher). Estimate: a few hours. Value: low (only 32-bit Windows / ARM, where `AllAccelerators()` now falls back to the plain CPU accelerator).
- **`MathF.Truncate` / `Math.Truncate` (done 10-20):** remapped in core (`RemappedIntrinsics.Truncation`: ceiling for negative values, floor otherwise) so every compiled backend compiles them without ILGPU.Algorithms; tests `MathTruncate_*`. Found on the way: with ILGPU.Algorithms enabled, CUDA's `MathF.Ceiling(-0.5f)` returns +0 instead of -0 (so Truncate too); without Algorithms CUDA is right. Algorithms' PTX Ceiling loses the sign of zero (not fixed).
- **Velocity in the browser:** never run there (Reflection.Emit under the Mono interpreter, no threads); `AllAccelerators()` registers no CPU accelerator in the browser.
- **ML:** Tuvok adds a Velocity lane to ML's gate, handles the `AcceleratorType.CPU` sites (23 in ML) and re-measures the ML heavy CPU group on Velocity against the plain CPU backend (Rule 12).
- **Wider SIMD (done 10-20 night):** `Vector256` (8 lanes) and `Vector512` (16 lanes) device types. The Vec128 operations template became a shared include (`VecOperations.ttinclude`, width parameter) with `Vec128/256/512` wrappers, likewise the specializer, type generator, extension and narrow-type operations; `Velocity128Config.VectorName` rewrites the XML expressions. All 733 tests pass on both new widths. MEASURED: compute-bound 38 -> 22 -> 18 ms (128 -> 256 -> 512), barrier kernel 4.9 -> 2.8 -> 2.1 ms, elementwise flat, matmul 29.5 -> 30.1 -> 42.8 ms (gathers). `Vector128` stays the default. Found on the way: shared memory of groups of at most one warp was allocated again for every group of a chunk (2 MB pool exhausted, access violation after about 16000 groups on the 512-bit device); `BeginIteration` frees it per group. Possible next step: contiguous-address detection for vector loads and stores (per-lane scalar loads are the limit).

## Order of work
1. Wrong answers first (radix sort descending, Reduce MaxUInt, early-return helper, F64 round): find each root cause in Velocity codegen, red/green tests.
2. Multi-warp groups (removes the group/grid dimension failures and most remaining ML failures).
3. Half / BFloat16 / FP8 / FP4 codegen.
4. Re-measure ML kernels and the ML heavy CPU group on Velocity against the plain CPU backend; log both (Rule 12).
5. Wider SIMD (`Vector256`/`Vector512` device types) only after 1-4, measured.
6. Register `VelocityTests` in `SpawnDev.ILGPU.DemoConsole/Program.cs` (DONE 10-20: all 731 pass or skip, per test and in PMT) and add a `VelocityTests` lane to ML's gate (Tuvok).

Keep the plain CPU backend as the debugging backend; Velocity as the one that does the work. ML should create its CPU accelerator through one place so the choice is a flag, not an edit in many files.

## VP9 on Velocity, 5.4.23-local.2 (Geordi, 2026-11-09 17:10; ComfyUI 252 frames, bit-exact, ms/frame)

| Stage | before | local.2 | CPU accelerator |
|---|---|---|---|
| Tile parse (1-few threads) | 1,104.7 | 4.39 (small-launch host path) | ~2.6 |
| Reconstruction (~15k threads since Codecs S2) | 115.6 | 127.1 | 4.4 |
| Loop filter (grouped, barriers) | 114 | 621 (?) | 351 |

Caveat: Tuvok's WebGPU bench overlapped part of the run (upper bounds). CORRECTION (Geordi 17:15): the 114 ms loop
filter was the OLD one-thread-per-superblock kernel (before Codecs S1); 621 is the S1 grouped barrier kernel - different
kernels, not an ILGPU regression. A clean A/B (S1 kernel on 5.4.21 vs local.2, quiet) is queued with Geordi. The S1 LF
on Velocity (621) vs CPU (351) is Velocity's own barrier path.
Clean A/B (Geordi 17:30, Codecs master with S1, same 252 frames, bit-exact): 5.4.21 LF 471.3 / recon 125.6 ms/frame vs
5.4.23-local.2 LF 451.9 / recon 108.9 - NO regression (621 was load overlap). Open perf item: Velocity's barrier path
(S1 grouped LF ~460 vs the old one-thread kernel 114; CPU accelerator 351).
Recon on Velocity: per-op helper-call cost dominates wide serial kernels (35x CPU) - the next Velocity perf item.

## Barrier cost measured (Data, 2026-11-09 18:35; scratchpad velbarrier.cs, 12 processors, ns per two-barrier wavefront step)

Grouped kernel, shared-memory wavefront, 64 groups x G threads x 64 steps (results identical everywhere):

| Device | warp | G=16 | G=32 | G=64 | 256 groups, G=64 |
|---|---|---|---|---|---|
| CPU accelerator | 8 | - | 84,764 | 163,489 | 139,709 |
| Velocity Scalar2 | 2 | - | 11,608 | 33,414 | 18,192 |
| Velocity Vector128 (production default) | 4 | 398 | 1,438 | 9,875 | 16,046 |
| Velocity Vector256 | 8 | - | 554 | 1,566 | 1,040 |

The cliff is warps per group > processors (one OS thread per warp; G=64 on Vector128 = 16 warps on 12 cores). Vector256
halves the warps: 6-15x faster barriers at G=64, and twice the lanes per operation everywhere. Velocity is already 5x
faster than the CPU accelerator at barriers.

PROPOSAL (5.4.23): production picks the widest HARDWARE-ACCELERATED device up to Vector256 (Vector256.IsHardwareAccelerated
-> Vector256, else Vector128) in Context.Builder.HostCompute / AllAccelerators(predicate), and `builder.Velocity()` registers
it too (its doc already says "the maximum vector length supported"). Gate first: the VelocityVector256Tests lane (commented
out of DemoConsole/Program.cs, not run lately) must be green; then velbench / velbarrier / Geordi's VP9-on-Velocity table.
Vector512 stays opt-in (AVX-512 is rare and its lane is untested).

## Wide-launch path choice and Vector256 per kernel shape (Data, 2026-11-09 23:30; scratchpad velwide.cs, 4096 threads, median ms/launch, noisy machine)

| Kernel | CPU accelerator | Velocity V128 | Velocity V256 |
|---|---|---|---|
| Divergent serial loop (3000 it) | 5.3 | 3.3-3.5 | 2.3-2.4 |
| 64-bit addressing (2000 it) | 4.0 | 2.5 | 1.7 |
| LocalMemory scratch, data-dependent indices (1000 it) | 6.9-10.7 | 6.8-8.1 | 11.1-12.3 (WORSE) |
| Barrier wavefront step at G=64 (us, velbarrier) | 163 | 9.9 | 1.6 |

- An ADAPTIVE wide-launch path choice (time Velocity vs the host IL accelerator per kernel, keep the faster) was
  implemented and SHELVED (reverted, never committed): no wide kernel above was faster on the host, and one early sample per
  path picked the wrong path on noise (+36-50%). CORRECTION (Geordi, 2026-11-10): there was no measured "25x recon gap".
  The 4.4 ms/frame was tile PARSE after the small-launch host path (1,105 -> 4.4), not reconstruction on the CPU
  accelerator; recon on Velocity was 113 (Vector128) / 76 (Vector256) ms/frame with no CPU-accelerator recon number beside
  it. Divergence, local memory and 64-bit addressing alone do not explain a gap anyway. Next: a per-stage table (CPU
  accelerator vs Velocity 128/256, VP9_STAGE_TIMES on ComfyUI files, Codecs 81d99dd after the one-call-site rewrites,
  Geordi), then a profile of the stage with the worst Velocity/CPU ratio through its DSP test.
- MEASURED (Geordi, 2026-11-10 17:00): ComfyUI files (252 frames), mean ms/frame, Codecs 676a456, ILGPU 5.4.25, bit-exact:

  | | parse | recon | loop filter |
  |---|---|---|---|
  | CPU accelerator | 2.6-2.8 | 5.8-6.1 | 350-355 |
  | Velocity Vector128 | 3.2 | 215-219 | 114-115 |
  | Velocity Vector256 | 3.2-3.4 | 129-131 | 82-83 |

  Velocity recon jumped 74 -> 130 at Codecs 5e76f1e (one Convolve body choosing copy/horiz/vert/2-D per pixel = divergent
  lanes); Geordi is splitting it back into four one-call-site loops. After that the recon gap is ~74 (Velocity 256) vs ~6
  (CPU accelerator) = the one to profile. The loop filter is the reverse: the CPU accelerator's barrier path (350) is the
  slow one, Velocity 82. PRIORITY (Data): the CPU-accelerator barrier rework is LOW - production desktop registers Velocity,
  not the CPU accelerator (ILGPU's debugging backend), and Velocity's loop filter is already 4x faster. The Velocity recon
  gap (one thread per pixel row of serial code, per-op helper calls) is the item with user-visible value.
- OPEN: Vector256 (the 5.4.23 default where AVX2 runs it) is 1.5-1.8x SLOWER than Vector128 on the LocalWork kernel. Not a
  local-memory bug: Vec128 and Vec256 Load32/Store32 are the same per-lane scalar access (4 vs 8 lanes). It is SIMT
  divergence: its expensive arm (an 8-iteration inner loop, taken by 1 lane in 4) runs when ANY lane of the warp needs it,
  1-(3/4)^4 = 68% of the time with 4 lanes vs 1-(3/4)^8 = 90% with 8. Wider warps win on uniform / barrier code and lose on
  divergent code. Geordi's earlier VP9 Velocity numbers were all Vector128 (VelocityCodecsTests pins it); he will run
  V128 vs V256 per stage on 5.4.23. If divergent workloads lose, the default goes back to Vector128 (or a per-kernel choice).
  Possible general speedup either way: AVX2 gathers (Avx2.GatherMaskVector128 with 64-bit addresses) in Vec256 Load32.
- DECIDED (Geordi 23:50, VP9 ComfyUI 252 frames on 5.4.23, bit-exact, quiet, ms/frame V128 -> V256): compressed header
  0.23 -> 0.22, tile parse 3.34 -> 3.05 (host path), reconstruction 112.8 -> 76.0 (-33%), loop filter 466 -> 266 (-43%).
  Vector256 STAYS the default; the divergence penalty is real for some kernels (LocalWork) but not for this workload.

## VP9 on Velocity profiled (Data, 2026-11-10 18:15; Codecs ab21440, ILGPU 5.4.25, Vector256, ComfyUI 252 frames)

dotnet-trace (dotnet-sampled-thread-time, Speedscope, CPU_TIME credited to the enclosing managed frame), whole decode:
- 57% of CPU in barrier kernels (`VelocityGroupTeamRunner.RunWarp`), and 31% of ALL CPU is spinning in
  `VelocityGroupBarrier.SignalAndWait` (warps of the loop-filter / intra wavefront kernels waiting for each other).
- 31% in non-barrier kernels (`VelocityChunkRunner`). The `Vec256Operations` helpers total under 1% (mostly inlined):
  NOT the per-op helper-call cost this plan assumed.
- Kernel compilation ~1 s total; 6% on the finalizer thread (unexplained, small).

MEASURED, untraced, ms/frame (bit-exact both):

| Velocity Vector256 | parse | recon | loop filter |
|---|---|---|---|
| default (`HostLaunchMaxThreads` = 64) | 3.25 | 51.2 | 82.8 |
| every non-barrier launch on the host IL path (`HostLaunchMaxThreads` = int.MaxValue, temporary env override, reverted) | 3.29 | **16.1** | 82.1 |

So the VP9 reconstruction kernels (one thread per 4x4 unit of branchy serial code: inter prediction, inter residual) run
3.2x FASTER as the kernel's own IL on the worker threads than as Velocity SIMD. This contradicts the shelved wide-launch
experiment above, whose synthetic kernels never showed a host win; real decoder kernels do. The CPU accelerator does recon
in 5.3, so the host path itself still has ~3x of overhead to find (it is a CPU accelerator shaped like the Velocity device:
warp size 8, max group).

NEXT (design, not started): a per-kernel path choice for kernels without group synchronization or shared memory.
Options: (a) an explicit opt-in per kernel (a load-time option or attribute: "run on the host IL path"); (b) adaptive:
time both paths over several launches per kernel, switch only on a large, repeated margin (the shelved version used one
sample per path and picked wrong on noise). Then: why the host path is 3x slower than the CPU accelerator, and the barrier
spin (yield sooner when the waited-for warps are far behind).

### Host-path variants (18:20, same files, temporary EXPERIMENT_* env overrides in VelocityAccelerator, all reverted)

| recon ms/frame (bit-exact) | |
|---|---|
| plain CPU accelerator (CpuCodecsTests, default device, Parallel) | **4.97** (LF 367.8) |
| Velocity Vector256 default (SIMD; host path only for <= 64 threads) | 51.2 |
| Velocity, every non-barrier launch on its host path (Sequential host, 1 multiprocessor = ONE thread) | 16.1 |
| Velocity, host path Parallel, 12 multiprocessors x 32 warps (3,072 OS threads) | 38.0 |
| Velocity, host path Parallel, 12 multiprocessors x 8 warps (the default CPU device shape) | 37.1 |
| Velocity, small launches only (<= 64) on a Parallel host | 67.2 (small launches pay the parallel wake-up) |

Reading: the plain CPU accelerator runs these kernels 10x faster than Velocity's SIMD and 3x faster than Velocity's own
(single-threaded) host path, yet a PARALLEL host accelerator behind Velocity is slower than the sequential one. Something on
the host path itself costs: candidates are the boxed `LaunchOnHost(object dimension, object[] args)` launch, a synchronize
per launch, the host kernel compiled under the Velocity context's settings (optimization level?), and the intra wavefront's
~30 tiny launches per frame. To find before designing the per-kernel choice: profile the host path at full width.

### FOUND (18:40): the host accelerator's SHAPE

| Velocity Vector256 (bit-exact) | parse | recon | LF |
|---|---|---|---|
| default | 3.25 | 51.2 | 82.8 |
| every non-barrier launch on a host accelerator in Sequential mode with ONE MULTIPROCESSOR PER LOGICAL CORE (the default CPU device's shape: groups in parallel, cooperative scheduling inside a group) | 3.66 | **7.58** | 87.0 |
| same host shape, small launches only (<= 64 threads) | 3.57 | 52.4 | 85.5 |

The host accelerator was `CPUDevice(WarpSize, MaxGroupThreads / WarpSize, 1)`: ONE multiprocessor, so a wide launch ran on one
core (16.1); Parallel mode makes every kernel thread an OS thread (37). Sequential x ProcessorCount gets within 1.5x of the
plain CPU accelerator (4.97; the mean includes host-kernel compilation in the worst frames).

DESIGN (to implement):
1. Host accelerator: `CPUDevice(WarpSize, MaxGroupThreads / WarpSize, Environment.ProcessorCount)`, Sequential (small launches
   unchanged within noise, measured above).
2. Per-kernel path choice for barrier-free, shared-memory-free kernels on launches above `HostLaunchMaxThreads`: adaptive.
   After the first launch of each path (compilation), time K launches on each path (normalized per thread), pick the host
   only when its median is faster by a clear margin (>= 1.25x), and re-check after N further launches (data can change).
   The shelved version used ONE sample per path and picked wrong on noise; medians of K per path are the fix.
   An explicit override stays (`HostLaunchMaxThreads`), plus a diagnostic of the choice per kernel.
3. Gates: VelocityTests / VelocityVector256Tests lanes; the HostLaunch test extended (a wide divergent kernel picks the host,
   a wide uniform elementwise kernel stays on SIMD, same results either way); velbench/velwide (no regression on SIMD-friendly
   kernels); Geordi's VP9 stage table (recon target ~8 ms/frame on Vector256, bit-exact).

DONE (f3a4074, local feed 5.4.26-local.1 fd55597): Geordi's gate on Codecs ab21440, interleaved, bit-exact, no regression:
Velocity V256 recon 50.4/50.7 -> 9.7/8.9, V128 70.4/70.6 -> 10.2/10.3; LF, parse, CPU accelerator and CUDA unchanged.

### Loop filter on Velocity (84 ms/frame V256) - what the "31% barrier spin" is (Data, 2026-11-10 21:40, code reading)

The LF WavefrontKernel launches `KernelConfig(rows, 64)`: on Vector256 a group is 8 warps, so `teams = cores / warps` =
12 / 8 = ONE team of 8 warp threads (4 of 12 cores idle) running the groups one after another; not oversubscribed, so a
waiting warp spins ~200 x SpinWait(8) and then yields. Barrier COST is small: ~1.6 us per two-barrier step (velbarrier, G=64
on Vector256) x ~2 per superblock x ~500 superblocks = ~1.5 ms of the 84. So the 31% "spin" is warps waiting for the
slowest warp of their group (per-pixel filter branches = divergent SIMD lanes and uneven work across warps): load
imbalance + divergence, not barrier overhead. Levers, in order: (1) more teams when a group is smaller than the machine
(today 4 cores idle); (2) the LF math's divergence on SIMD (Codecs-side kernel shape; a scalar path for barrier kernels
would need the CPU accelerator's cooperative barrier, 352 ms/frame today, so no); (3) park sooner when the waited-for
warps are far behind (saves CPU, not wall time).

MEASURED lever (1) (2026-11-11 04:00, temporary EXPERIMENT_VELOCITY_TEAMS_CEIL, reverted): teams = ceil(cores / warps)
instead of floor (slight oversubscription; waits then yield at once):
- VP9 LF (Codecs ab21440, ILGPU 5.4.26, bit-exact): Vector256 84.1 -> **64.3 ms/frame** (1 -> 2 teams, 16 warp threads on
  12 cores); Vector128 unchanged (16 warps/group > 12 cores: 1 team either way); recon unchanged.
- velbarrier (64 groups x G threads x 64 two-barrier steps, little work per step), Vector256 ns per step: G=24 265 / 317
  (same team count, noise), G=40 627 -> 501 (2 -> 3 teams, better), G=64 1,206 -> **2,718** (1 -> 2 teams, 2.25x WORSE).
So ceil wins when work between barriers dominates (VP9 LF) and loses when barriers dominate. Same shape as the SIMD vs
host choice: per-kernel ADAPTIVE (time both team counts over a few launches, keep the faster, re-check). NEXT: implement
in VelocityGroupTeamRunner (a per-kernel state, like VelocityKernel.ChooseWidePath), gate = velbarrier (no regression at
G=64) + VP9 LF (~64 ms/frame on V256) + Velocity lanes.

2026-11-11, adaptive team count DONE (285d02b; local feed 5.4.27-local.1, forks 2.4.26-local.1): per (kernel, warps,
gridSize) the runner times both counts (2 warm + 3 timed launches each), keeps the faster by a 1.1 margin (ties keep
the previous choice), re-checks every 1,024 launches; `VelocityAccelerator.AdaptiveTeamCount` (default on),
`TeamCountDecisions`. Feed-DLL A/B, Codecs ab21440 EveryFrame comfyui (252 frames, bit-exact every run), alternating:
Vector256 LF 81.0/81.5 -> 66.6/67.5 ms/frame; recon 8.5-9.0 -> 9.9-10.4 (watch); Vector128 LF unchanged (~115).
OPEN: the recon +1 ms. Hypothesis: a barrier kernel whose group count changes per launch re-measures for every new
gridSize and never settles. Test = TeamCountDecisions after the run (Geordi's gate). Fix if so: key by a log2 bucket
of gridSize. Gate for release: Geordi's interleaved stage table (V256/V128/CPU/CUDA) + V256 EveryFrame, then full sweep.
