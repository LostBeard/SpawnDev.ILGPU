# Wasm Backend

Compiles ILGPU IR → WebAssembly binary. Dispatches via Web Workers with SharedArrayBuffer.

## Key Files
- `Backend/WasmKernelFunctionGenerator.cs` — kernel codegen, parameter setup, helper functions
- `Backend/WasmCodeGenerator.cs` — base IR visitor, GetField, Store, Atomic handlers
- `Backend/WasmBackend.cs` — compilation orchestration, helper generation, `LastWasmBinary`
- `Backend/WasmModuleBuilder.cs` — Wasm binary format builder (sections, types, functions)
- `WasmAccelerator.cs` — dispatch to workers, buffer management, struct serialization
- `WasmMemoryBuffer.cs` — SharedArrayBuffer-backed memory, zero-copy sharing
- `WasmILGPUDevice.cs` — device config (MaxNumThreadsPerGroup=64)

## Hard Constraints
- **Blazor WASM is single-threaded** — all async, no blocking. `stream.Synchronize()` is a no-op.
- **Serialized dispatch** — `RunKernelAsync` awaits `_pendingWork` before each dispatch.
- **Struct-with-view serialization** — CLR layout ≠ IR layout. Use `WasmParamInfo.StructFields` + `FlattenCLRStruct()` for manual IR-layout serialization. See SKILL.md for details.
- **`IsViewType()` distinguishes views from struct-with-view** — checks if `DirectFields[0] is AddressSpaceType`.
- **Empty struct padding** — `Stride1D.Dense` has no CLR fields but IR adds Int8 padding.
- **`SpecializedValue<T>` unwrapping** — IR lowers to PrimitiveType; dispatch must extract inner value.
- **`LongIndex1D` as first param** — it's extent (loop bound), not thread index. Don't map to `_globalIdxLocal`.
- **Buffer deduplication** — SubViews of same buffer share one copy in Wasm memory.
- **NativePtr patching** — set to Wasm offset before struct serialization, restore to 0 after.
- **Multi-pass algorithms** — route to `CreateSingleGroupScan` (ScanExtensions.cs, AcceleratorType.Wasm).

## Tribal Knowledge: GetField View Field Mapping (March 2026)

**FIELD MAPPING RULE**: In the `GetField` handler for view parameters, field 1 is context-sensitive:
- **StructureType views** (ArrayView1D): field 1 = **Extent (Length)** → return `locals[1]`
- **AddressSpaceType views** (ArrayView): field 1 = **Index/Offset** → return 0

This was hardcoded to 0 for ALL views, which broke `view.Length` for ArrayView1D params.
The fix checks `param.Type is StructureType`. Current: 249 pass / 0 fail / 3 skip (v4.6.0). Full `hardwareConcurrency` multi-worker barrier dispatch with pure-spin generation barriers (wait/notify races on V8 — see the "Barriers are PURE SPIN" note below). In-Wasm phase dispatcher eliminates JS-Wasm boundary crossings between phases.

**TRACE RULE**: Both `GetViewLength` and `GetField` must trace the view source back to
the kernel Parameter through GetField/NewView/AddressSpaceCast chains (via `TraceToParameter()`).
ArrayView1D's BaseView access creates a GetField indirection that breaks direct Parameter lookup.

## Kernel Function Signature
9 system params + user params:
`kernel(globalIdx, dimX, dimY, scratchBase, groupDimX, threadIdX, sharedMemBase, barrierBase, dynamicSharedLen, ...userParams)`

## Barrier Dispatch (Fiber-Based Phase Model)
- Each Web Worker = one thread within a workgroup.
- **Fiber refactor (March 2026):** Kernels with barriers are compiled into a phase-based dispatch model. Each barrier becomes a yield point — the kernel saves its state (locals + phase counter) to scratch memory and returns. A **Wasm-native phase dispatcher** handles the entire thread/phase/group loop inside WebAssembly, eliminating JS-Wasm boundary crossings between phases.
- **Dynamic block splitting:** Barrier-separated code blocks are split into phases automatically. Helper function calls (scan, sort) each get their own phase with yield points before and after.
- **Completion state persist:** The kernel saves its exit state to scratch so the worker knows when all phases for a group are done before advancing to the next group.
- **Barriers are PURE SPIN — wait/notify races on V8 (verdict re-confirmed 2026-05-24).** The dispatcher phase barrier and group barrier (`GeneratePhaseDispatcher` in `WasmBackend.cs`) use a pure `i32.atomic.load` spin loop on a generation counter, with a yield-to-JS escape after a spin threshold (phase barrier) to survive CPU oversubscription. The in-kernel `EmitBarrier` path is also pure-spin (and bypassed entirely in phase mode). **Do NOT switch to `memory.atomic.wait32`/`notify`.** History: April 2026 briefly shipped wait/notify ("fixed spurious-wakeup with a `while` loop") but it was reverted to spin in rc.25/rc.27 because large sorts produced non-deterministic corruption. Re-tested 2026-05-24 behind the default-off `WasmBackend.UseWaitNotifyBarriers` flag on current Chrome + current backend: **wait/notify STILL races** — large multi-group RadixSorts fail with sort-order violations / value duplicates (1.4M: 1067 violations, 500K: 187, 1M: duplicate keys); small single-group sorts pass. Our codegen is seq_cst-correct (fence before gen store; seq_cst gen load in waiter synchronizes-with it), so this is a V8 linear-memory wait/notify ordering bug (chromium#490434403 family). The April "275-local spill" theory is disproven — the barrier is in the ~38-local dispatcher and still races, so reducing locals can't dodge it. The flag stays only as a one-flip re-test harness for when a future V8 ships a fix. Full log: `Plans/wasm-waitnotify-still-races-2026-05-24.md`.

## Tribal Knowledge: GridIndex vs BucketIndex Bug (March 2026)

**RADIX RULE**: All atomic/store writes to shared histograms MUST verify the bucket index multiplier in the address computation. When the codegen unrolls a per-bucket loop, each iteration must use a DIFFERENT counter address — `counter_base + (numGroups * bucket + gridIndex) * sizeof(int)`. If the unrolled writes all share the same `local` for the index (e.g. `gridIndex` which is 0 for single-group), they all hit `counter[0]` and the histogram is silently wrong. The data appears unchanged (no crash, no trap) because the scan of an all-zero histogram produces all-zero offsets, so the scatter is a no-op.

**Current status**: RESOLVED. Counter addresses were correct. The real issues were: local alloca at address 0, and missing post-helper barriers. See rules below.

## Tribal Knowledge: Local Alloca Must Use Scratch (March 2026)

**LOCAL ALLOCA RULE**: The base `Alloca` handler in `WasmCodeGenerator.cs` sets local alloca addresses to `i32.const 0`. This causes the kernel to write to Wasm memory address 0 (the data buffer region). The `WasmKernelFunctionGenerator` MUST override this for non-shared allocas to allocate scratch memory (`scratchBaseLocal + offset`). Without this fix, the ExclusiveScan helper's output struct gets written to address 0, corrupting sorted data between RadixSort passes.

## Tribal Knowledge: Post-Helper Barrier (March 2026)

**POST-HELPER BARRIER RULE**: After every helper function call that uses barriers, the codegen
MUST emit an additional barrier. Without it, a fast worker can start the next helper call while
a slow worker is still completing the previous one. Since helpers use shared memory at fixed
offsets, overlapping calls corrupt scan results, causing non-deterministic duplicate values in
the RadixSort presort. The fix is in `GenerateCode(MethodCall)` — after advancing the barrier
counter for the helper's barriers, emit one more `EmitBarrier(_barrierCounter++)`.

**Canary test**: `AlgorithmRadixSortNonPairsIntTest` sorts [32,31,...,1] → [1,2,...,32].
If this test produces duplicates or non-deterministic results, the post-helper barrier is broken.

## Fiber Refactor Status (March 2026) — COMPLETE

**Test results: 249 pass / 0 fail / 3 skip** (up from 49/10/17 pre-refactor). All RadixSort (including SpawnSceneSimulation 1.4M multi-frame), scan, barrier, sort, and large sort (260K-4M) tests pass on the Wasm backend at full `hardwareConcurrency`.

**Multi-worker:** Full `hardwareConcurrency` barrier dispatch with pure-spin generation barriers (wait/notify races on V8 — see the "Barriers are PURE SPIN" note above), `atomic.fence` at 3 sync points, float atomic stores via reinterpret, broadcast atomic store/load. In-Wasm phase dispatcher eliminates JS-Wasm boundary crossings. `hardwareConcurrency` workers for both barrier and non-barrier kernels.

The fiber refactor resolved the multi-group barrier dispatch limitation. 20+ bugs were fixed collaboratively:

1. **Fiber yield-per-phase** — dynamic block splitting with yield points at each barrier
2. **br depth +1** — helper if-nesting depth fix for branch target calculation
3. **Scratch overflow** — ScratchPerThread set after phase state computed, not before
4. **Completion state persist** — kernel saves exit state for worker re-entry
5. **Shared memory dedup** — prevent inflation from multiple SetupSharedAllocations calls
6. **TryGetValue bool flag** — prevent calling Math.sin instead of helper function
7. **Sync yield after helper done** — prevent shared memory stomping between sequential helper calls
8. **Scratch zeroing** — zero from scratchBase (not 0) to prevent stale data between dispatches
9. **Struct/scratch overlap** — struct body params placed AFTER per-thread scratch
10. **Generation barrier** — landed pure spin; April 2026 wait/notify attempt (wait32 + while-loop spurious-wakeup defense) reverted to spin in rc.25/rc.27, race re-confirmed on V8 2026-05-24 (see "Barriers are PURE SPIN" note)
11. **Shared memory alloca overlap** — same-size allocas deduped to same offset; fixed with distinct sizes
12. **IR address space aliasing** — InferAddressSpaces guards for phi/predicate/general values with Shared sources
13. **Zero region race** — between-group zeroing loop excluded fence slots to prevent deadlock
14. **Per-worker scratch** — eliminated intermittent corruption from shared scratch regions
15. **WorkerPool re-instantiation** — compare `.buffer` instead of Memory object identity

The skipped tests are intentional backend capability skips (e.g., features not applicable to Wasm), not failures.

## Tribal Knowledge: Struct Body Placement (March 2026)

**STRUCT REGION RULE**: Struct parameters serialized to scratch (e.g., `ReductionImplementation` in `GridStrideLoopKernel`) must be placed in the `structRegionBase` area, which is AFTER all per-thread scratch regions. The per-thread scratch at `scratchBase + tid * scratchPerThread` is used for state save/restore during barrier yields. If a struct is placed at `scratchBase + 0`, thread 0's state save overwrites the struct fields, causing subsequent threads to read corrupted data (wrong ReducedValue, pointer values, etc.). The fix ensures `structRegionBase = scratchBase + scratchSize` (8-byte aligned).

## Debugging
- `WasmBackend.LastWasmBinary` — capture last compiled kernel
- `WasmBackend.AllKernelInfos` — compilation summaries
- Disassemble: `wasm2wat --enable-threads kernel.wasm` (MUST use --enable-threads)
- Do NOT use LINQ in Blazor WASM logging — silently fails. Use for-loops.

## Tribal Knowledge: Struct Load Must Copy (March 2026)

**STRUCT LOAD RULE**: When `Load` is called with a `StructureType`, the codegen MUST copy the struct data from the source address to a scratch slot. Returning the source address directly creates an ALIAS — subsequent writes to the array (e.g., in-place RadixSort pre-sort `view[pos] = value`) overwrite the "loaded" value. Primitive Loads are safe because they copy to Wasm locals (immutable). Struct Loads use SSA-keyed scratch slots (`_structLoadSlots`) to minimize scratch usage while ensuring snapshot semantics.

## Tribal Knowledge: Unsigned Comparison (March 2026)

**UNSIGNED RULE**: Both `CompareValue` and `GenericAtomic` (Min/Max CAS loop) must check for unsigned flags (`IsUnsignedOrUnordered` / `IsUnsigned`) and emit `i32.lt_u`/`i64.lt_u` instead of signed variants. Without this, `MinUInt32`/`MinUInt64` reductions return the identity value because the signed comparison treats large unsigned values as negative.

## Tribal Knowledge: Atomic RMW Opcode Table (March 2026)

**OPCODE RULE**: The Wasm threads spec interleaves sub-word variants (rmw8, rmw16, rmw32) between each full-word RMW operation. The opcode numbering is NOT sequential per-operation type. Each operation group has 7 opcodes: i32.rmw, i64.rmw, i32.rmw8_u, i32.rmw16_u, i64.rmw8_u, i64.rmw16_u, i64.rmw32_u. So `i32.atomic.rmw.add` = 0x1E but `i32.atomic.rmw.and` = 0x2C (not 0x22!). The CmpXchg constants (0x48/0x49) were correct because they were at the END of the sequence. Add (0x1E/0x1F) was correct because it's at the START. Everything in between (Sub, And, Or, Xor, Xchg) was wrong and caused "invalid alignment" validation errors.
