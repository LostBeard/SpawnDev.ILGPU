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
