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
- **Found, not fixed:** `MathF.Truncate` compiles to a call of the external `ModF`; Velocity compiles that as a stub (returns 0, writes nothing) so the value comes back unchanged (CUDA fails at PTX JIT instead): needs a core `TruncateF` operation for every backend.
  `NoInliningIdct16Row32CallsZeroInput...` (garbage under zero input). The three `Round` failures
  (`FloatRoundToEven_And_LongMinMax`, `F64Emulation_*`) come from the F64-emulation test context, not from Round itself: the same kernel is right on Velocity in a plain context (Velocity has native doubles; those tests need not run on it).

## Order of work
1. Wrong answers first (radix sort descending, Reduce MaxUInt, early-return helper, F64 round): find each root cause in Velocity codegen, red/green tests.
2. Multi-warp groups (removes the group/grid dimension failures and most remaining ML failures).
3. Half / BFloat16 / FP8 / FP4 codegen.
4. Re-measure ML kernels and the ML heavy CPU group on Velocity against the plain CPU backend; log both (Rule 12).
5. Wider SIMD (`Vector256`/`Vector512` device types) only after 1-4, measured.
6. Register `VelocityTests` in `SpawnDev.ILGPU.DemoConsole/Program.cs` (the class exists; the line is commented out until the lane is green) and add a `VelocityTests` lane to ML's gate.

Keep the plain CPU backend as the debugging backend; Velocity as the one that does the work. ML should create its CPU accelerator through one place so the choice is a flag, not an edit in many files.
