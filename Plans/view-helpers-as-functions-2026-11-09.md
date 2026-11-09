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
