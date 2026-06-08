# WebGL Backend

Transpiles ILGPU IR → GLSL ES 3.0 shaders. Uses Transform Feedback for output.

## Key Files
- `Backend/GLSLKernelFunctionGenerator.cs` — kernel codegen
- `Backend/GLSLEmulationLibrary.cs` — i64/f64/f16 emulation for GLSL
- `WebGLAccelerator.cs` — dispatch, context management, device loss handling
- `glWorker.js` — off-main-thread Web Worker for GL calls

## Hard Constraints
- **No shared memory, atomics, or barriers** — fundamentally limited by WebGL 2.0 / GLSL ES 3.0.
- **Transform Feedback** for kernel output — output data written via varying variables.
- **Context loss** — `glWorker.js` monitors `webglcontextlost`/`webglcontextrestored`. `IsContextLost` guards dispatch.
- **Output varying index** — `BuildOutputVaryingIndex` dictionaries for O(1) lookup.
- **i64/f64 emulation** — same as WebGPU but in GLSL syntax.

## GPGPU Scatter + RadixSort (2026-06-07)

Transform Feedback is **gather-only** — a vertex invocation writes only its own output slot, so reorder ops (sort, compaction) can't scatter `dst[dest[i]] = src[i]`. Solved with a **render-points-to-texture scatter**:
- `glWorker.js` `handleScatter`: renders one `GL_POINT` per element at `gl_Position` derived from the dest index (NDC center of the dest texel); the fragment shader writes the value into the **dst buffer's render-target texture** (FBO `COLOR_ATTACHMENT0`). Per-`glslType` program cache (`isampler`/`usampler`/`sampler`, `ivec4`/`uvec4`/`vec4` output — integer targets need a vec4-family output, not scalar). `EXT_color_buffer_float` is enabled so `R32F` is renderable; `R32I`/`R32UI` are core-renderable.
- **Zero-copy**: the result lives in the GPU texture (what the next op reads). The CPU mirror is marked `dataStale`; `ensureCpuFresh` does a single `readPixels` lazily, only on host readback (`handleReadbackBuffer`/`handleCopyBuffer`). Intermediate scatter passes never read back.
- **Renderable-format gotcha**: a buffer only ever used in scatter keeps the default `GlslType="float"` → allocated `R32F` → `FRAMEBUFFER_INCOMPLETE_ATTACHMENT` without the float ext. `WebGLAccelerator.Scatter` sets the real types (int→`R32I`) so scatter-only buffers are renderable.
- `WebGLAccelerator.Scatter(...)` + `IScatterProvider` (in ILGPU.Algorithms) bridge the algorithm layer (`SpawnDev.ILGPU → ILGPU.Algorithms`, so the interface lives in Algorithms and WebGLAccelerator implements it).

**RadixSort on WebGL** (`RadixSortExtensions.CreateWebGLScatterRadixSort` + `...Pairs`): stable 1-bit split - per bit, extract bit -> exclusive-scan flags (the multi-pass Hillis-Steele WebGL scan) -> compute split dest **element-wise** (no in-kernel loop - a gather+binary-search hung the GL context on WebGL's vertex-shader loop codegen) -> scatter. Pairs do a dual scatter (keys+values by the same dest). Works for int/uint/float/long/double keys (keys-only + pairs); 64-bit (long/double) uses a multi-texel scatter (`cpe=2`). **Half** (16-bit, packed 2-per-texel) sorts via an UNPACKED f32 working representation (`CreateWebGLScatterRadixSortHalf` / `...PairsHalfKey`): the whole-texel scatter can't move a sub-texel Half, so copy-in widens each Half to f32 (lossless), scatter the f32 (the proven R32F path), derive the radix bit via the canonical `ExtractRadixBits<Half>`, narrow back on copy-out. **All key types now pass keys-only + pairs on WebGL** (Half added 2026-06-08, which also fixed a cross-backend sub-word signed-reinterpret sign-extension bug - see below).

**Host scan/reduce work; IN-KERNEL group scan/reduce do NOT.** `accelerator.CreateScan(...)` (Hillis-Steele) and `CreateReduce(...)` (multi-pass) run on WebGL - they orchestrate MULTIPLE dispatches with the draw-call boundary as the barrier + global ping-pong buffers (this is what RadixSort uses). But IN-KERNEL group/warp ops - `GroupExtensions.ExclusiveScan / InclusiveScan / AllReduce / Reduce`, `WarpExtensions.*` - require the group's threads to share memory within ONE dispatch, which WebGL's Transform-Feedback vertex model cannot do (no shared workgroup memory, no barriers). These are STRUCTURALLY impossible in-kernel. Until 2026-06-08 the WebGL codegen silently emitted them as `= 0` (the "Unmapped" method-call stub in `GLSLCodeGenerator`) - a consumer got silent wrong results. The codegen now **throws `UnsupportedKernelFeatureException`** for any unmapped intrinsic whose name contains `Scan`/`Reduce` (message points at the host `CreateScan`/`CreateReduce` + `RequiresSharedMemory`), mirroring the atomic/barrier guards' "no silent garbage" rule. Locked by `WebGLTests.WebGLGroupScanThrowsUnsupportedTest`; the `Algorithm{Exclusive,Inclusive}Scan* / AllReduce* / GroupReduce*` tests stay skipped (the one allowed skip - capability genuinely impossible on the backend).

## Float16 (Half) — Emulated Only

`Capabilities.Float16 = true` on WebGL via emulation. `Capabilities.Float16Native = false` — WebGL 2.0 / GLSL ES 3.0 has no hardware Float16 path.

**How it works:**
- **Type mapping:** Half → `float` in GLSL (f32 arithmetic). See `GLSLTypeGenerator.cs:113`.
- **Storage:** 2 halves packed per `int` texel in R32I buffer textures (same layout as Int16 sub-word).
- **Load:** `texelFetch` the u32 word, bit-extract the u16 via shift+mask, call `_f16_to_f32(uint)` from `GLSLEmulationLibrary.F16Functions`.
- **Store:** call `_f32_to_f16(float)` on the f32 value, cast the returned uint to int, write to the Transform Feedback varying. Host-side readback reassembles the packed u16 stream into the original `Half[]` buffer.

**Half RadixSort now RUNS on WebGL** (2026-06-08) via the scatter path (no shared memory/barriers - see RadixSort section above). Half **Scan/Reduce** still skip (those algorithm families genuinely need shared memory + barriers, structural to WebGL). The 5 non-algorithm Half tests (`HalfBufferRoundTrip`, `HalfArithmetic`, `HalfMinMax`, `HalfEdgeCases`, `HalfMixedType`) run.

**Sub-word signed reinterpret + sign-extension (2026-06-08).** `(short)someUshort` / `(sbyte)someByte` are signedness-reinterprets that the core IR ELIDES (short and ushort share `BasicValueType.Int16`, so the convert looks like identity). On WebGL (and WebGPU/Wasm) sub-word values live in a 32-bit register, so the dropped sign-extension corrupts the high bits - this silently broke `AscendingHalf.ExtractRadixBits`'s `(short)bits >> 15` ones-complement mask for NEGATIVE Halves. Fix: `GLSLKernelFunctionGenerator.GenerateCode(ConvertValue)` (AND the base `GLSLCodeGenerator`) now re-extend per `SourceUnsigned` when the convert WIDENS a sub-word source to int, not just when it narrows to a sub-word target. **Sign-extension uses `SignExtend16/8` = `((x & 0xFFFF) ^ 0x8000) - 0x8000`, NOT `(x << 16) >> 16`** - the latter is GLSL ES 3.0 UNDEFINED BEHAVIOR when bit 15 is set (`0x8000 << 16` overflows the sign bit; ANGLE returned 0). Also `FloatAsInt(Half)` must compress to the 16-bit f16 pattern via `_f32_to_f16` (parallel to the f64 `f64_to_ieee754_bits` fix), not `floatBitsToInt` of the widened f32.

**Why emulation is lossless:** every f16 value is exactly representable as f32 (f16 is a strict subset of f32's encoding). The WGSL, GLSL, and Wasm emulation paths all match the same IEEE 754 bit conversion behavior so results on emulated WebGL and emulated WebGPU agree byte-for-byte on the same inputs. Denormals flush to signed zero, Inf/NaN propagate via mantissa preservation.
