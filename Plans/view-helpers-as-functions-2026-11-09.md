# View-taking helpers as real shader functions (WebGPU / WebGL / Wasm) - plan, 2026-11-09 (Data)

## Why

SpawnDev.Codecs' VP9 decoder lost the WebGPU device at kernel compile (Geordi, 5.4.21-local.1). Offline screen
(`ShaderCompiler.Generate`, WebGPUBaseline, Codecs 7c4fbce):

| kernel | WGSL | lines | generate (desktop) |
|---|---|---|---|
| Vp9ReconKernels.InterPassKernel | 33.4 MB | 314,089 | 10.7 s |
| Vp9ReconKernels.IntraWaveKernel | 33.5 MB | 315,494 | 13.8 s |
| Vp9LoopFilterKernels.WavefrontKernel | 10.9 MB | 142,626 | 24.3 s |
| Vp9CompressedHeaderKernels.CompressedHeaderKernel | 6.4 MB | 119,048 | 4.5 s |

The 10-26 measurement: a 66k-line kernel drove Chrome's GPU process (Tint, module step) to 16 GB. These are 2-5x that.
The Wasm module of WavefrontKernel declared 68,382 locals in one function (fixed in 5.4.21 by local reuse, but the
code is still ~1 MB for one kernel).

**Cause:** every helper that takes a view or pointer is inlined at EVERY call site on WebGPU and WebGL,
`[MethodImpl(NoInlining)]` or not (`InlineAddressParameterCalls`): the WGSL / GLSL function generators cannot take a
storage-buffer address as a parameter. The 17 `[NoInlining]` helpers of the VP9 decoder produced no shader function;
the only 49 WGSL `fn`s are the emulation library.

## Idea

Storage buffers (WGSL) and buffer textures (GLSL) are MODULE-SCOPE: a function can read the binding directly. A view
argument is therefore (binding, offset, length). Emit a `[NoInlining]` view-taking helper ONCE PER DISTINCT BINDING
TUPLE among its call sites (monomorphize on bindings), with each view parameter replaced by scalar `offset` (+ `length`
when used) parameters, and the body indexing the module-scope binding at `offset + i`. Most call sites pass views of
the same kernel parameters, so a helper becomes 1-3 functions instead of N inlined copies.

## Phases

1. **Measure first (offline, no GPU):** count call sites x inlined size per helper in the VP9 kernels (which helpers
   carry the bulk); confirm the call sites' views resolve to a kernel parameter (`TraceToParameter`-style) +
   an offset expression. If most do: the plan holds. If many are computed from loop-carried phis / selects of
   different buffers: those stay inlined (WebGPU already rejects a run-time choice between bindings).
2. **Wasm first** (lowest risk): views are i32 linear-memory addresses, so a `[NoInlining]` view helper can be a real
   Wasm function today without specialization. Check whether the Wasm backend inlines them anyway and why; measure the
   VP9 module size and V8 compile time before/after.
3. **WGSL:** specialization pass (IR or codegen): per call site, resolve each view argument to (kernel param binding,
   offset value, length value); group by binding tuple; emit `fn H_b<k>(offsets..., scalars...)`; body codegen maps
   the view param to the binding + offset. Must handle: sub-word / packed `array<atomic<u32>>` bindings, emulated
   i64 offsets, struct-element views, atomics through the view, helpers calling helpers (transitive), shared /
   local-memory views (stay inlined: function/workgroup address spaces), uniformity (a helper with barriers).
4. **GLSL:** the same specialization over buffer textures (samplers are uniforms).

## Gates

- naga over every ILGPU suite kernel + all Codecs / ML kernels (offline screen) before and after - 0 new failures.
- Size table above re-measured: target each VP9 kernel under ~30k lines.
- Tint memory of the VP9 kernels in isolated Chrome with `gpu_watchdog.ps1 -LimitGB 3` (never a bare run).
- Full PMT sweep; plus Codecs' WebGPU lane with the GiantShader fence lifted, behind the watchdog.
- Red-check: the old always-inline path must reproduce the size explosion in a new size-budget test
  (a kernel calling a view helper from many sites: WGSL size must stay ~linear in call sites x call cost, not body).

## Not in scope

Cross-binding view choice (still unsupported on WebGPU / WebGL - documented, typed exception).

## Findings 2026-11-09 06:40 (first experiment)

- `InlineAddressParameterCalls(inlineBudgetSurvivors, keepGlobalViewCalls)` added (flag OFF everywhere): leaves a call to a
  non-Inline helper as a call when every address-carrying argument traces (sub-view / cast / align / NewView / GetField) to a
  method PARAMETER. At this stage view arguments are `View<T, Generic>` - address spaces are not inferred yet - so the origin
  is traced, not the type's address space. Turned on for WebGPU, the 16-call-site test kernel went 1482 -> 724 WGSL lines
  (1 call site: 589), i.e. the helper became ONE `fn`.
- The dormant WGSL fn-def path for view parameters (unreachable since 09-27) is wrong in three ways (naga: "the operand of
  `&` must be a reference"): (1) the body uses the caller-side value names (`v_0`..) instead of its `p_N` parameters,
  (2) `arrayLength(&p)` on a pointer parameter, (3) the call passes the whole binding: a sub-view's OFFSET and LENGTH are
  dropped (silently wrong results, not only a compile error).
- So Phase 3 = a view ABI for helper functions: `(ptr<storage, array<T>, rw>, offset, length)` per view parameter (offset /
  length in the kernel's index width, emulated i64 when needed), body indexing `(*p)[offset + i]`, `GetViewLength` ->
  length param; one function per distinct binding element type (plain / `array<atomic<u32>>` sub-word / emu i64); then
  enable the flag for WebGPU. Gate = `ViewHelper_ManyCallSitesWithSubViews_MatchesHost` (all lanes) + a size test
  (16 call sites <= ~1.5x one call site in WGSL lines) + naga over the whole suite + Codecs VP9 size table.

## Code map for Phase 3 (WGSL), read 2026-11-09 07:00

- Existing fn-def view ABI = "direct-param coalesce": `WGSLKernelFunctionGenerator.ScanCallSiteAddressSpacesInternal`
  (~line 490-560) stages `HelperParamCoalesceEntry` per (callee, argIdx) when the argument resolves
  (`ResolveToParameter`) to a kernel parameter in a coalesce group; `WGSLFunctionGenerator.GenerateHeaderStub` (~250)
  emits `p_<id>_leader : ptr<storage, array<T>, rw>` once per group + `p_<id>_offset : i32` per member;
  `EmitNonInlinedMethodCall` (~3450) passes `&<binding>` + `i32(_scalar_params[slot])` (the kernel parameter's BASE
  offset slot). Non-coalesced view args are passed as their Variable (the whole binding pointer).
- Gaps vs the plan: (1) a SubView computed in the kernel (`a.SubView(c * 64, 64)`) has a runtime offset that is NOT
  added - only the parameter's base slot; (2) no length parameter (`GetViewLength` in the body -> `arrayLength(&p)`
  on a pointer: invalid, and wrong for a sub-view anyway); (3) helper bodies reference caller value names (`v_0`)
  when the param mapping (`_paramIndexToLocalVar`, ~line 54/397-470) does not cover the case.
- Step 1 next session: read `WGSLCodeGenerator.GenerateCode(SubViewValue)` / `GetViewLength` to see how the kernel
  tracks a view's (binding, offset, length); then generalize the coalesce entry to EVERY parameter-origin view arg:
  `p_<id>_leader` + `p_<id>_offset` (+ `p_<id>_len` when the body reads the length), call site passes
  base slot + sub-view offset expression + length expression; body LEA = `(*leader)[offset + idx]`.
- Test kernel + gate already in master: `ViewHelper_ManyCallSitesWithSubViews_MatchesHost` (cca264d, 11/0 with inlining);
  flag `keepGlobalViewCalls` (8825b01, off). Offline probe: scratchpad vhprobe/vhprobe.cs (+ naga).
- Step 1 findings (07:10): the kernel never materializes a sub-view; LEA use sites walk the SubView chain and add each
  `SubViewOffsetExpr` (+ chosen-view `_vo`) to the index (WGSLKernelFunctionGenerator ~6260-6290). Coalescing
  (`_directParamCoalesceMembership`, groups built ~1380-1495 by element-type bucket, only past
  `CoalesceTriggerThreshold` bindings) is a binding-count reduction that happens to give helpers a leader+offset ABI.
- Step 2 (implement): for helper-ABI purposes give EVERY kernel view parameter reaching a surviving helper a
  single-member pseudo-group (its own binding name + `BindingWgslType` from the same bucket classification,
  `IsSubWord`/`SubWordElementByteSize`), stage entries for every parameter-origin view arg (not only coalesce members),
  and at the call site pass `base slot + SubView-chain offset` (reuse the LEA chain walk) and a length
  (`p_<id>_len`: the innermost SubView's Length, else the parameter's length) when the body reads the length.

## Design correction 2026-11-09 07:30 - do NOT pass typed storage pointers

The declared element type of a kernel binding (`param{N} : array<T>`) depends on signedness from the CLR type, packed
structs, sub-word storage (`atomic<u32>`, or plain `u32` for a read-only f16 - `IsPlainLoadF16`), and atomics - several of
which are decided DURING kernel body codegen (`_subWordParams` registration is path/order dependent), while helper fns are
generated in parallel with their own signatures. A `ptr<storage, array<T>>` parameter must match the binding type exactly
(Tint rejects a mismatch), so the coalesce-style typed-pointer ABI is fragile.

**Use monomorphization instead:** emit a surviving helper once per BINDING TUPLE (which kernel parameter each view
argument is) and have its body index the module-scope `param{N}` directly, with only the element offset (+ length) as
scalar parameters - every load / store then goes through the KERNEL's own parameter-aware paths (sub-word, f16, packed
struct, atomics), so the binding's type never has to be spelled in a signature. Implementation shape: generate the helper
body with the kernel generator's machinery (a "helper as kernel-fragment" emission), keyed by (method, binding tuple).

IR side committed and inert (flag off): `KeepsAsFunction` = call in the kernel, all view args kernel-parameter views of
plain / sub-word element types, helper free of atomics / barriers / warp ops / shared memory, kernel free of atomics.
VP9 decoder kernels use no atomics, so v1 covers them.

## Implementation insight 2026-11-09 07:50 - reuse the chosen-view machinery

`WGSLKernelFunctionGenerator` already represents "a view = kernel-parameter ROOT + runtime element offset `<name>_vo` +
length `<name>_vl`" for views chosen at run time (`_chosenViewRoots`, `ChosenViewVar`, `ChosenViewOffsetExpr`,
`ChosenViewLengthExpr`, the LEA chain walk ~6260-6290 and `GetViewLength` ~8734). Every LEA / Load / Store / Length path
(sub-word atomic<u32>, plain-load f16, packed structs, emulated 64-bit) already handles a chosen view by adding `_vo` and
continuing with the root parameter's own binding.

So a monomorphized helper `fn H_<tuple>(<view_k>_vo : i32, <view_k>_vl : i32, ..., scalars...)` can be generated BY THE
KERNEL GENERATOR (save/restore its builder + per-function state, like WasmSimdKernelEmitter's saved context) with each view
parameter registered in `_chosenViewRoots` -> its kernel parameter; the call site passes `ChosenViewOffsetExpr(arg)` /
the arg's length expression (SubView chain + chosen-view vars of the caller). The binding element type is never spelled.
Per-function state to isolate: valueVariables / declared names, hoisting, PHASE 5 var->let, current_block, uniformity info.
Emission: when the kernel walker meets a surviving call, key (callee, tuple of kernel-param roots); generate the fn once into
a module-scope buffer emitted before the kernel entry.

## First implementation task (08:55)

`WGSLKernelFunctionGenerator` has ~70 private state fields (+5 in `WGSLCodeGenerator`: `valueVariables`,
`declaredVariables`, `_localAllocaVarNames`, ...). Before emitting a monomorphized helper body with the kernel generator,
classify every field as KERNEL-WIDE (binding / scan results: `_directParamCoalesceMembership`, `_subWordParams`,
`_viewOffsetScalarSlots`, `_chosenViewRoots` roots of the kernel, atomics sets, ... - shared, read-only during the nested
emission) or PER-FUNCTION (`valueVariables`, `declaredVariables`, `_hoistedPrimitives`, `_localAllocaVarNames`, structured
walker / block state, builder + indent - saved, reset, restored around it). Write the classification as a table here first;
a field missed in the per-function set leaks names between the helper and the kernel (naga catches most: redeclaration /
undeclared), a kernel-wide field wrongly reset loses binding info (silent wrong indexing: the ViewHelper test catches it).

### State classification - first pass (09:05, by name/role; verify each when touched)

| class | fields |
|---|---|
| KERNEL-WIDE, read-only in the helper | `_generatorArgs`, `_atomicParameters` / `_floatAtomicParameters` / `_unsignedAtomicParameters`, `_emulatedF64Params` / `_emulatedI64Params`, `_subWord*Params` (all), `_plainLoadFloat16Params`, `_storedSubWordParams`, `_subWordBodyStructBindingNames`, `_scalarManifest`, `_viewOffsetScalarSlots` / `_viewCountScalarSlots` / `_scalarSlotsAfterScan` / `_coalesceCountSlots`, `_coalesceGroups`, `_directParamCoalesce*`, `_directParamOutputs`, `_packedStructLayouts`, `_bodyStructParams`, `_dynamicSharedOverrides`, `_atomicSharedAllocas`, `_isScanKernel`, `_shouldLinearizeGridX`, `_kernelUsesGridIdxZ`, `_indexParameterAddress` |
| KERNEL-WIDE, ACCUMULATED by the helper (OR / union back) | `_uses*` flags (Broadcast / PredicateBarrier / Subgroups / Barriers / GroupReduce), `_broadcastType`, `_bodyReferencesScalarParams`, `_lengthReadParams`, `_paramPassedToHelper`, `_chosenViewNeedsLength` |
| GLOBAL MONOTONIC counters (never reset: unique names across kernel + helpers) | `_inlineCounter`, `_ufBreakSentinelCounter`, `_loopExitFlagCounter`, `_declaredSyntheticCounters` |
| PER-FUNCTION (save, reset, restore) | base: `valueVariables`, `declaredVariables`, `_localAllocaVarNames`, Builder + indent; `_hoistedPrimitives`, `_viewPointerVarNames`, `_subWordLEAVars`, `_crossBlockPointerExprs` / `_crossBlockElementPointers` / `_crossBlockPointers`, `_emittedLetBindings`, `_cfg` / `_postDominators` / `_loops`, `_deferredVarDeclarations` / `_useDeferredDeclarations`, `_loopExitStack` / `_returnMerge` / `_currentLoopHeaderExitTarget` / `_loopExitInfo`, `_hoistedIndexFields`, `_visitedBlocks`, `_boolLocalCastPtrs`, `_boolSharedPtrs` |
| SPECIAL: `_chosenViewRoots` | kernel entries kept; the helper ADDS its view params (param -> kernel root) for its body, removed after |

Consumer-side update (Geordi, S2 branch): splitting the inter path out of the recon kernels took IntraWaveKernel 33.7 ->
6.6 MB WGSL; the two new inter kernels are 5.7 and 4.5 MB. Still multi-MB per kernel (the library fix remains the way to
take them to sub-MB), but below the 10-26 OOM class.

## Status 2026-11-09 14:20 (Data) - WGSL implemented, uncommitted, browser lane pending

Done (builds; offline WGSL validates with naga):
- [x] IR: `InlineAddressParameterCalls(keepGlobalViewCalls: true)` on WebGPU keeps a call to a NON-Inline helper whose view
  arguments are sub-views (through address-space casts) of a parameter: of the kernel, or of the calling helper
  (transitive), or a view field of a kernel struct parameter. Target: no atomics / barriers / warp / broadcast / grid
  indices / shared memory; kernel: no atomics.
- [x] Codegen (`WGSLKernelFunctionGenerator.ViewHelpers.cs`): one `fn <helper>_m<k>` per binding tuple; view params =
  `hv{i}_vo` (+ `hv{i}_vl` when the helper, or a nested helper given it whole, reads the length); body through the kernel
  pipeline (cross-block hoist, structured walker, scoping post-process, phases 4 / 5, scalar-slot hoist). Walker returns
  carry the value (`ReturnExitStatement`). The function generator emits nothing for view-taking methods.
- [x] Pre-scans: body-struct output scan maps helper params to kernel params per call site (was latent-wrong);
  `ParamLengthIsRead` sees lengths read inside kept helpers (count slots for sub-word views).
- [x] Tests: size gate in `ViewHelper_ManyCallSitesWithSubViews_MatchesHost` (651 vs 516 lines; flag off 1,481 vs 146: the gate fails, red-check done), new
  `ViewHelper_BindingShapesThroughHelpers_MatchHost`.

Open:
- [x] Red-check: flag off -> 1,481 vs 146 lines, the size gate fails.
- [x] PMT: ViewHelper tests 19/20 (WebGL refuses the 8-byte scatter loudly -> test skips WebGL); WebGPU lanes 1712/1/91 (the 1 = InvalidShader error-order flake, test fixed). [ ] naga over the suite. [ ] PMT the RefStateHelpers test.
- [ ] VP9 size table with Codecs 64a88db (`scratchpad/vp9screen2`): its `[NoInlining]` helpers (PredictBlockPlane,
  Predict, InverseTransformAdd, loop-filter lanes) are called from inside other helpers, which the nested support covers.
  Codecs 7c4fbce (no NoInlining) moved only 314k -> 280k lines (one kept call per kernel).
- [x] VP9 table (Codecs 64a88db, 5.4.21 -> this build, naga-valid): InterResidual 67,308 -> 44,093, IntraWave
  72,122 -> 48,719, InterPredict / Wavefront / CompressedHeader ~same, Parse 821,782 unchanged.
- [x] v2 (implemented 14:50): `ref` to a LOCAL structure as a `ptr<function, T>` parameter. VP9 64a88db with v2 (15:25,
  all six kernels naga-valid): Parse 821,782 -> 136,945 lines (87 MB -> 10.7 MB, generate 196 s -> 6 s), InterResidual
  44,093, IntraWave 48,719, others unchanged. Found on the way: bool XOR emitted `^` (FIXED, pre-existing, any kernel);
  a caller rule that excluded Inline-flagged callers duplicated InverseTransformAdd (fixed: only Inline + shared-memory
  callers are excluded). Parse's helpers (DecodeCoefs, ReadCoeff,
  DecodeBlockTokens, ReadMv*, ReadInterMode, ...) all take `ref Vp8BoolDecoderGpuState r`; WGSL core allows
  ptr<function> parameters. Needs: IR keep rule (pointer to an Alloca of the caller or a ptr param of a kept helper),
  call site `&var`, accesses through `(*p)` in the body (the local-alloca value-name handling must not apply).
- [ ] Commit core + wrapper, drift check, -local package for Geordi.
- [x] 5.4.23-local.2 (741b3ec, forks 2.4.22-local.2) in the local feed, bits verified; PMT WebGPU 1717/0/91, WebGL
  693/0/194; drift check clean (local + CI).
- [x] v3 (implemented 15:45; LocalViewHelpersKernel naga-valid; Codecs 31a1073 with S3, all six VP9 kernels naga-valid: Parse 684,927 -> 113,564 lines, 72.9 -> 9.0 MB; PMT pending): LocalMemory views (Geordi's S3: token cache / contexts / partition stack in LocalMemory).
  Design: a view argument whose root is a LOCAL array Alloca (NewView of it, sub-views) becomes
  `hv{i} : ptr<function, array<T, N>>` + `hv{i}_vo` (+ `_vl`); N and T join the specialization key. Inside the helper the
  LEA cannot redirect to the root (a kernel-scope var the fn cannot see): new branch - a helper view param marked
  local -> `&(*hv{i})[FunctionArrayIndex(vo + idx)]`. Call site: `&v_arr`, offset, length; a nested helper forwards
  `hv{i}` with its own offset sum. IR: OriginatesFromKernelParameter also accepts NewView(local array Alloca) and a
  helper's local-view param (same caller rules).
- [ ] Wasm / GLSL phases unchanged (not started).

## Queue (2026-11-09 evening)
- [ ] (Tuvok) `if (default(TStruct).Flag)` - a struct type param whose property returns a constant is NOT folded before
  OpenCL codegen: the untaken branch's Warp.ShuffleXor reached CLCodeGenerator ("Subgroup shuffle requires SubGroupShuffle
  capability"). Repro shape: ML commit 50270de5's parent, GgufBatchDecoder IGqaReduce. Offline repro via the OpenCL
  generator (no GPU), then find why the property never becomes a constant (getter on a default-initialized struct value).
- [ ] Budget-survivor keep (WebGPU, uncommitted): gate = WebGPU lanes + per-kernel Chrome/Tint table + ML WGSL naga screen.
- [ ] Velocity Vector256 default (uncommitted): gate = VelocityVector256Tests lane + benches.

## Tint OOM on VP9 at local.3 - measured and fixed (2026-11-09 20:00)

Geordi's first real VP9 WebGPU trial on local.3 (Codecs 25f0cdc): GPU process 0.39 -> 4.16 GB in 28 s, device lost.
Per-kernel isolated installed Chrome (scratchpad tintcompile: own profile per kernel, tintwatch.ps1 watchdog scoped to it,
3 GB kill; peak / Tint module ms / DXC pipeline ms):

| Kernel | local.3 | survivor-keep build |
|---|---|---|
| CompressedHeader | KILLED >3 GB in 4.6 s | 1.16 GB / 863 / 39,439 |
| Parse | KILLED >3 GB in 6.4 s | 1.04 GB / 816 / (harness hit 8-buffer default limit; fixed) |
| InterPredict | 1.24 GB / 1,027 / 85,583 | 1.24 GB / 984 / 65,986 |
| InterResidual | 1.24 GB / 313 / 14,478 | 1.21 GB / 208 / 12,197 |
| IntraWave | 1.32 GB / 357 / 19,086 | 1.27 GB / 238 / 15,907 |
| Adapt | 1.11 GB / 72 / 10,463 | 1.12 GB / 56 / 8,050 |
| Wavefront | 2.03 GB / timeout 120 s | 2.03 GB / 1,115 / 101,959 |

Cause: the Inliner's cumulative IL budget leaves late calls as survivors, and the WebGPU pipeline re-inlined every
Inline-flagged survivor (InlineAddressParameterCalls), so CompressedHeader got ~30 inlined DiffUpdate copies in unrolled
loops: one function of 8,118 vars x 7,483 branches (Tint's module cost is vars-in-scope x branches). Fix (uncommitted):
a survivor that KeepsAsFunction stays a call and the kernel generator emits it as a function; [AggressiveInlining]
(ForceInline) still inlines. Offline: CompressedHeader 119,048 -> 39,415 lines (score 60.7M -> 1.9M), Parse 113,564 ->
53,152, InterResidual 44,093 -> 28,274, IntraWave 48,719 -> 32,796; naga-valid.
Remaining cost: DXC pipeline time on first use (Wavefront ~100 s, InterPredict ~66 s, CompressedHeader ~39 s).

### Same harness on Codecs dae4f90 (Geordi's [NoInlining] on DiffUpdate/MvUpdate) + survivor-keep build (20:30)

| Kernel | peak | Tint module ms | DXC pipeline ms |
|---|---|---|---|
| CompressedHeader (6,970 lines) | 2.98 GB | - | TIMEOUT >240 s |
| Parse (53,152 lines) | >3 GB KILLED | 1,620 | killed at 26,587 |
| InterPredict | 1.24 GB | 958 | 66,624 |
| Wavefront | 2.03 GB | 1,062 | 98,653 |
| InterResidual / IntraWave / Adapt | 1.2-1.3 / 0.46 GB | 202 / 238 / 65 | 11,482 / 14,798 / 989 |

FINDING: on D3D12 Chrome, DXC inlines every HLSL function into the entry point, so WGSL functions move the expansion from
Tint to DXC. Tint OOM is fixed (module steps <= 1.6 s), but what D3D pays is total code AFTER inlining (helper body x call
sites). CompressedHeader with all DiffUpdate calls as functions is WORSE for DXC (>240 s) than with only budget survivors
as functions (39 s, 25f0cdc). Parse now exceeds 3 GB in DXC. Remaining lever: fewer call sites at the source (Codecs), or
automatic call-site merging in ILGPU (one call site in a dispatch loop) - a research item, not started.

### Codecs 3a20c7c (Geordi: compressed header table-driven, ONE DiffUpdate call site) on the local.4 candidate (21:15)

| Kernel | peak | Tint ms | DXC ms |
|---|---|---|---|
| CompressedHeader (7,739 lines, all inlined) | 0.44 GB | 82 | 880 |
| Parse (59,418) | >3 GB KILLED | 1,298 | killed at 31,607 |
| InterPredict | 1.24 GB | 908 | 67,418 |
| Wavefront | 2.03 GB | 1,051 | 98,281 |
| InterResidual / IntraWave / Adapt | 1.15 / 1.22 / 1.02 GB | 171 / 210 / 82 | 10,693 / 14,127 / 7,293 |

CONFIRMED: D3D cost = total code after full inlining; one call site in a table/state-driven loop takes CompressedHeader
from 39 s / >240 s to 0.9 s. Parse is the blocker (Geordi rewrites it the same way next).

## Short-circuit body dedup (2026-11-10 00:10, Data; Geordi's finding)

Release IL compiles `if (a || b) body` to two branches into the SAME body block; the WGSL walker's
`UnvisitSharedTargets` re-emitted that block (loops included) in every arm, multiplied by inlining. My first ten probes
said "no repro" because `dotnet run probe.cs` builds Debug IL (Roslyn turns `||` into a bool local there); `-c Release`
reproduced with the simplest probe. Fix: `TryEmitShortCircuitAsFlag` (flag lowering; else arm emitted inside the chain;
looks through code-free edge-split blocks; a loop header as the shared block keeps its back edges). Offline, naga-valid:

| Codecs 3a20c7c kernel | lines before | after | loops before -> after |
|---|---|---|---|
| InterPredict | 78,067 | 33,545 | 164 -> 68 |
| Wavefront | 115,219 | 73,533 | 25 -> 19 |
| Parse | 59,417 | 42,863 | 487 -> 252 |
| CompressedHeader | 7,738 | 7,309 | 44 -> 26 |

Geordi's next Codecs change (long -> int offsets, removes emu_i64 taps) stacks with this; Tint/DXC table of that commit
+ this build is the next browser measurement.
- [ ] PMT ShortCircuit (all lanes) then full sweep (waits for Tuvok's ML sweep).
- [x] GLSL walker had the same re-emission (acyclic `EmitIfBranch` and in-loop `EmitLoopIfBranch` Case 4): ported
  (`GLSLCodeGenerator.TryEmitShortCircuitAsFlag`); every test shape 2-4 -> 1-2 loops, red-checked. naga cannot parse
  GLSL ES 300; the PMT WebGL lane is the compile check.
- [ ] Geordi's second finding (early return in an inlined helper duplicating its tail): no Release-IL repro yet.
