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

**RadixSort on WebGL** (`RadixSortExtensions.CreateWebGLScatterRadixSort` + `...Pairs`): stable 1-bit split — per bit, extract bit → exclusive-scan flags (the multi-pass Hillis-Steele WebGL scan) → compute split dest **element-wise** (no in-kernel loop — a gather+binary-search hung the GL context on WebGL's vertex-shader loop codegen) → scatter. Pairs do a dual scatter (keys+values by the same dest). Works for 4-byte int/float keys (keys-only + pairs). **Open:** `ExtractRadixBits<uint>` drops bits 8-31 on WebGL (uint dynamic-shift codegen); 64-bit (long/double) + Half need a multi-texel scatter.

## Float16 (Half) — Emulated Only

`Capabilities.Float16 = true` on WebGL via emulation. `Capabilities.Float16Native = false` — WebGL 2.0 / GLSL ES 3.0 has no hardware Float16 path.

**How it works:**
- **Type mapping:** Half → `float` in GLSL (f32 arithmetic). See `GLSLTypeGenerator.cs:113`.
- **Storage:** 2 halves packed per `int` texel in R32I buffer textures (same layout as Int16 sub-word).
- **Load:** `texelFetch` the u32 word, bit-extract the u16 via shift+mask, call `_f16_to_f32(uint)` from `GLSLEmulationLibrary.F16Functions`.
- **Store:** call `_f32_to_f16(float)` on the f32 value, cast the returned uint to int, write to the Transform Feedback varying. Host-side readback reassembles the packed u16 stream into the original `Half[]` buffer.

**Algorithm-family Half tests (RadixSort/Scan/Reduce) still skip on WebGL** because they require shared memory + barriers — those limitations are structural to WebGL, not Half-specific. The 5 non-algorithm Half tests (`HalfBufferRoundTrip`, `HalfArithmetic`, `HalfMinMax`, `HalfEdgeCases`, `HalfMixedType`) run.

**Why emulation is lossless:** every f16 value is exactly representable as f32 (f16 is a strict subset of f32's encoding). The WGSL, GLSL, and Wasm emulation paths all match the same IEEE 754 bit conversion behavior so results on emulated WebGL and emulated WebGPU agree byte-for-byte on the same inputs. Denormals flush to signed zero, Inf/NaN propagate via mantissa preservation.
