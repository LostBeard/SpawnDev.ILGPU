# CLAUDE.md — Master Map

SpawnDev.ILGPU extends ILGPU with three browser GPU backends. It transpiles .NET IL into GPU shader languages (WGSL, GLSL, Wasm binary) at runtime.

## Build Commands

```bash
dotnet build SpawnDev.ILGPU/SpawnDev.ILGPU.csproj   # Main library (~2s)
dotnet build SpawnDev.ILGPU.slnx                     # Full solution
dotnet run --project SpawnDev.ILGPU.DemoConsole       # Desktop tests (CUDA, OpenCL, CPU)
dotnet run --project SpawnDev.ILGPU.Demo              # Browser tests (Blazor WASM → /tests)
```

Target: **net10.0**. `PublishTrimmed` and `RunAOTCompilation` must remain **false** — ILGPU relies on IL reflection at runtime.

## Context Map

Detailed constraints live in each directory's own `CLAUDE.md`. Read the relevant one when working in that area.

| Directory | What | Context File |
|-----------|------|-------------|
| `SpawnDev.ILGPU/WebGPU/` | WGSL transpiler, dispatch, buffers | [`WebGPU/CLAUDE.md`](SpawnDev.ILGPU/WebGPU/CLAUDE.md) |
| `SpawnDev.ILGPU/Wasm/` | Wasm binary compiler, worker dispatch | [`Wasm/CLAUDE.md`](SpawnDev.ILGPU/Wasm/CLAUDE.md) |
| `SpawnDev.ILGPU/WebGL/` | GLSL transpiler, Transform Feedback | [`WebGL/CLAUDE.md`](SpawnDev.ILGPU/WebGL/CLAUDE.md) |
| `ILGPU/` | Forked ILGPU core (IR, types, runtime) | [`ILGPU/CLAUDE.md`](ILGPU/CLAUDE.md) |
| `ILGPU.Algorithms/` | Forked algorithms (Scan, RadixSort) | [`ILGPU.Algorithms/CLAUDE.md`](ILGPU.Algorithms/CLAUDE.md) |
| `SpawnDev.ILGPU.P2P/` | Distributed GPU compute via WebRTC | [`SpawnDev.ILGPU.P2P/CLAUDE.md`](SpawnDev.ILGPU.P2P/CLAUDE.md) |
| `PlaywrightMultiTest/` | Unified test runner | [`PlaywrightMultiTest/CLAUDE.md`](PlaywrightMultiTest/CLAUDE.md) |
| `.claude/skills/ilgpu_transpiler/` | Hard-won transpiler mapping rules | [SKILL.md](.claude/skills/ilgpu_transpiler/SKILL.md) |

## Architecture Overview

### Backends (6 total)

| Backend | Target | Shader Language | Key Constraint |
|---------|--------|----------------|----------------|
| **WebGPU** | Browser | WGSL | 4-byte alignment, uniformity analysis |
| **WebGL** | Browser | GLSL ES 3.0 | No shared memory/atomics/barriers |
| **Wasm** | Browser | WebAssembly binary | SharedArrayBuffer + multi-worker dispatch |
| CUDA | Desktop | PTX | Via upstream ILGPU |
| OpenCL | Desktop | OpenCL C | Via upstream ILGPU |
| CPU | Desktop | .NET | Via upstream ILGPU |

### Test Infrastructure

Tests in `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase*.cs` (~211 tests, Tests1-10). Backend-specific classes inherit and override unsupported tests. See `PlaywrightMultiTest/CLAUDE.md` for running tests.

**Current version: 4.9.2-rc.10** (April 2026, locally published at `D:\users\SpawnDevPackages`; nuget.org has 4.9.2-rc.9 top). **rc.10 headline**: LocalMemory<T>(N>=32) WGSL codegen 5-layer fix (unblocks Tuvok's Vp9Idct8x8Kernel + 16x16/32x32/iADST/iHT kernel family on WebGPU bit-exact), `AcceleratorRequirements` capability-gating API + extension methods, `UnsupportedKernelFeatureException` typed exception wired at WebGL GenericAtomic + AtomicCAS codegen sites. Regression test `LocalMemoryRepro_Int64_ShortByteViews` locks down both the WebGPU fix and the WebGL architectural varying-count ceiling. rc.10 docs landed in README "What's New in 4.9.2." **Current P2P sibling: 4.9.2-rc.22** (WebTorrent 3.1.4, full 6-gap audit closed, binary wire framing for BufferSend/BufferData via `P2PBinaryFrame` + ConfigureHighThroughputSctp helper - 10MB WebRTC dispatch passes in 25s). **Pending at HEAD (historical context, shipped in rc.7+): f16 emulation Phases 1 + 2 + 3** - `Capabilities.Float16` always true across WebGPU / WebGL / Wasm / OpenCL; `Capabilities.Float16Native` distinguishes native-vs-emulated on backends where both paths exist (WebGPU, OpenCL); `WebGPUBackend.ForceEmulatedF16` test flag. Emulation paths: WebGPU WGSL `_f16_to_f32` / `_f32_to_f16` helpers + packed u16 storage; WebGL GLSL helpers + Transform Feedback uint output; OpenCL `vload_half` / `vstore_half` built-ins (no extension required) + f32 arithmetic. Full `hardwareConcurrency` multi-worker barrier dispatch with pure-spin generation barriers (wait/notify races on V8 — see Wasm/CLAUDE.md "Barriers are PURE SPIN") and in-Wasm phase dispatcher (no JS-Wasm boundary crossings between phases). All large sort tests (260K-4M) passing including SpawnSceneSimulation (1.4M elements, multi-frame). rc.7 key fixes: WGSL spinlock-key refactor (tuple-keyed `array<atomic<u32>>` for f64 Min/Max/Exchange), Wasm cascade-safe Dispose (per-worker TCS fault on dispose), wait/notify barrier with wakeup loop (replaced pure spin after diagnosing spurious wakeup bug - not a V8 bug), shared memory alloca overlap (same-size dedup), IR address space aliasing (InferAddressSpaces guards), struct/scratch overlap, multi-pass scan, Float16, unsigned ops, 256 threads, memory.grow(), ViewSourceSequencer, subViewByteOffset, atomic RMW opcode table, CopyFromBuffer, onesComplementMask .tt template, per-worker scratch, atomic.fence at 3 sync points, float atomic stores, broadcast atomic store/load, barrier counter zeroing between groups.

## Debugging Pipeline — ShaderDebugService

Every kernel compilation auto-dumps generated code to a local folder via `ShaderDebugService` (registered in the demo's `Program.cs`). **Use this — do NOT ask TJ to manually run tests or capture output.**

### Setup
1. Run the demo, go to `/tests`
2. Click **"Set Debug Folder"** → pick a local folder (e.g., `_debugdump`)
3. Folder persists in IndexedDB across sessions — set once, works forever

### What Auto-Dumps (organized by backend)
```
debugfolder/
├── _DEBUG_README.md
├── latest.json                         ← live test results (updated each test)
├── test-run-YYYY-MM-DD_HH-mm-ss.json  ← permanent test run history
├── wgsl/                               ← WebGPU shaders with metadata headers
│   └── NNN_KernelName.wgsl
├── glsl/                               ← WebGL shaders with metadata headers
│   └── NNN_KernelName.glsl
└── wasm/                               ← Wasm binaries + compilation info
    ├── NNN_KernelName.wasm             ← disassemble: wasm2wat --enable-threads
    └── NNN_KernelName.txt              ← params, locals, barriers, shared mem size
```

### How to Use
- **Find a kernel:** Grep the `.txt` files for `hasBarriers=True`, `helpers=1`, etc.
- **Disassemble Wasm:** `wasm2wat --enable-threads NNN_kernel.wasm > kernel.wat`
- **Read WGSL/GLSL:** Files include metadata headers (kernel name, workgroup size, shared mem, bindings, timestamp)
- **Track test results:** `latest.json` updates after every test. Compare `test-run-*.json` across runs.
- **The files are on disk.** Do NOT ask TJ to capture output or run tests manually. Read the dump folder.

### Test Results (live via latest.json)
`UnitTestsView` writes results to the same debug folder via the `ResultsDirectory` parameter. **`latest.json` is overwritten after EVERY test completion** — it contains the full test suite state in real-time: pass/fail/skip/pending counts and per-test details (class, method, result, error, duration, stack trace). A timestamped `test-run-*.json` is written when the full run finishes.

**During test runs, read `latest.json` to see results as they happen.** Don't wait for the run to finish. Parse it with `node -e` to find failures:
```bash
node -e "const d=JSON.parse(require('fs').readFileSync('path/to/latest.json','utf8')); console.log('Pass:',d.passed,'Fail:',d.failed,'Skip:',d.skipped,'Pending:',d.pending); d.tests.filter(t=>t.result==='Error').forEach(t=>console.log('FAIL:',t.className+'.'+t.method,'-',(t.error||'?').substring(0,200)));"
```

## Engineering Philosophy

- **Bugs found here are HIGHEST PRIORITY.** SpawnDev.ILGPU is the foundation for SpawnDev.ILGPU.ML, SpawnScene, and every project that uses GPU compute. A bug here is a bug in everything. When a consuming project discovers a SpawnDev.ILGPU bug, stop all other work and fix it here first — with unit tests. No workarounds in consumers. No "fix it later." Treat every release as the final release.
- **Correctness is non-negotiable. Performance is a close second.** Kernels dispatch thousands of times/sec.
- **No workarounds that mask problems.** Fix root causes.
- **Cross-backend impact** — changes to `ILGPU/` affect all 6 backends. Consider all of them.
- **No quick fixes** — plan before implementing complex changes.
- **Do not hardcode evolving hardware limits** — preserve full i64 index paths.

## Global Constraints

These apply everywhere, not just one directory:

- **No backend-specific kernel variants.** NEVER create backend-specific copies of algorithm kernels (e.g., `WasmRadixSortKernel1`) to work around bugs. The same kernel must work on all 6 backends. Fix bugs in the codegen, dispatch, or memory management — not by duplicating the algorithm. Only acceptable if it is absolutely IMPOSSIBLE to fix any other way.
- **Blazor WASM is single-threaded** — all async, no blocking calls. The browser backends (WebGPU/WebGL/Wasm) are async-only at the GPU->CPU boundary: a sync GPU->CPU readback (`CopyToCPU`/`GetAsArray1D`) THROWS, `Synchronize()` only FLUSHES the queued work WITHOUT waiting (use `await SynchronizeAsync()` when you need it finished - it is NOT a no-op and NOT a deadlock), and blocking the thread on async work (`.Result`/`.Wait()`) DEADLOCKS. Full contract + per-method reference: [`Docs/async.md`](Docs/async.md).
- **T4 Templates in `ILGPU/`** — check for `.tt` before editing `.cs`. Generated files are silently overwritten.
- **Device loss detection** — WebGPU: `device.lost` promise. WebGL: `webglcontextlost` event. Guards on dispatch/synchronize. Intentional disposal filtered out.

## Capability Gating — `AcceleratorRequirements` (v4.9.2-rc.10+)

Kernels that use features some backends can't implement (atomics on WebGL, native f64 on WebGPU) will silently produce wrong output if they land on the wrong backend. Declare requirements up-front and the selection path filters out incapable backends:

```csharp
using SpawnDev.ILGPU;

using var acc = context.CreatePreferredAccelerator(
    new AcceleratorRequirements
    {
        RequiresAtomics = true,        // rules out WebGL
        RequiresFloat64Native = true,  // rules out WebGPU + WebGL
    });
// -> on desktop: CUDA > OpenCL > CPU
// -> on browser with the above combo: only Wasm survives
// -> throws NotSupportedException naming the requirements when nothing matches
```

Other entry points: `context.EnumerateCompatibleDevices(requirements)` for ranking your own pick, `device.Satisfies(requirements)` for per-device checks.

**All flags** (mirror the 6-backend feature matrix below): `RequiresAtomics`, `RequiresSharedMemory`, `RequiresBarriers`, `RequiresFloat16`, `RequiresFloat16Native`, `RequiresFloat64`, `RequiresFloat64Native`, `RequiresInt64`, `RequiresInt64Native`, `RequiresInt64Atomics`, `RequiresSubGroups`, `RequiresScatterStores` (in-kernel scatter / >1 output element per thread - rules out WebGL). `AcceleratorRequirements.None` = no filter (accepts every backend).

**Use this INSTEAD of hand-rolling `if (backend == WebGL) skip;` in consuming projects.** The logic belongs in one place; consumers declare intent, not backend knowledge.

**Compile-time guard (shipped for SOME WebGL silent-wrong classes):** beyond the selection gate, the WebGL codegen THROWS `UnsupportedKernelFeatureException` at kernel-compile time for the "consumer pinned to WebGL anyway" case - wired for atomics (`GenericAtomic`/`AtomicCAS`) and in-kernel group/warp Scan/Reduce. A compile guard for **multi-store-per-thread / scatter output** is NOT yet shipped (a blunt first attempt false-positived on 40 legit kernels and was backed out 2026-06-13 - positional `v*K+slot` multi-store and grid-stride loops are both valid; the correct criterion is an open problem); use the `RequiresScatterStores` selection gate for now. Remaining `Requires*` flags are selection-gate-only.

## WebGPU Binding Limits (v4.9.1+)

**maxStorageBuffersPerShaderStage = 10 (Chrome).** WebGPU spec minimum is 8. Every `ArrayView` kernel parameter uses one storage buffer binding. Scalar parameters (int, float, etc.) are packed into a single `_scalar_params` buffer.

**Total bindings = (number of ArrayView params) + 1 (_scalar_params) + (any struct params)**

If total > 10: `InvalidOperationException` at dispatch time (v4.9.1+). Before v4.9.1, this silently produced "Invalid BindGroupLayout due to a previous error."

**How to stay under the limit:**
- Combine related ArrayViews using struct packing (e.g., `ArrayView<MyStruct>` with multiple fields instead of separate arrays)
- Maximum safe ArrayView count: **9** (leaves room for _scalar_params)
- Check `accelerator.MaxStorageBufferBindings` at runtime

## Sub-Word Data Types (v4.9.0+)

`ArrayView<byte>`, `ArrayView<sbyte>`, `ArrayView<short>`, `ArrayView<ushort>`, `ArrayView<Half>` (ILGPU.Half), `ArrayView<BFloat16>` (ILGPU.BFloat16) supported on all 6 backends.

**Use `ILGPU.Half`, NOT `System.Half`** in kernel signatures. Implicit conversion operators exist for interop. Same for **`ILGPU.BFloat16`** (the "brain float": top 16 bits of an fp32, so fp32's full dynamic range - the ML-weights trade vs `Half`) and the two FP8 types **`ILGPU.Float8E4M3`** (forward/inference, no Inf, sat ±448) + **`ILGPU.Float8E5M2`** (backward/gradient, IEEE Inf/NaN). bf16/FP8 detail: [Docs/data-type-support.md](Docs/data-type-support.md). On CUDA bf16 + FP8 use an f32-register-compute model (no native PTX bf16/fp8 arithmetic); the load/store conversion is **PORTABLE bit-manipulation (basic integer ops on every CUDA arch incl. pre-Ampere)** - 4.13.0+ replaced the sm_80-only `cvt.*.bf16` shortcut that broke on older cards. The browser/OpenCL/Wasm backends emulate the same exact conversion, byte-identical to CUDA.

**Per-backend implementation:**
- **WebGPU:** Packed into `array<atomic<u32>>`. Load via atomicLoad + shift + mask. Store via atomicAnd + atomicOr (thread-safe sub-word writes). Float16 load/store calls `_f16_to_f32` / `_f32_to_f16` helpers from `WGSLEmulationLibrary.F16Functions` when `!shader-f16`; native WGSL `f16` type otherwise. `WebGPUBackend.ForceEmulatedF16` test flag forces the emulation path for verification.
- **Wasm:** Native `i32.load8_s/u`, `i32.load16_s/u`, `i32.store8`, `i32.store16`. Float16 emulated via inline IEEE 754 bit conversion at load/store.
- **WebGL:** texelFetch from R32I with shift+mask in GLSL. Float16 load/store calls `_f16_to_f32` / `_f32_to_f16` helpers from `GLSLEmulationLibrary.F16Functions`; capability reports true (always emulated on WebGL).
- **OpenCL:** `Capabilities.Float16` always true. When `cl_khr_fp16` present: native `half` type. When absent: Float16 promoted to `float` for arithmetic, `vload_half` / `vstore_half` built-ins (available without the extension) handle buffer load/store. `Capabilities.Float16Native` selects the path.
- **CUDA/CPU:** Native support.

**Gotchas:**
- WGSL requires explicit parenthesization for mixed-precedence shift/mask expressions
- WebGPU sub-word stores use atomic RMW (data race if non-atomic when threads write different halves of same u32)
- `arrayLength()` on sub-word buffers returns u32 count, multiply by elements-per-word for actual element count

## CopyFromJS (v4.9.0+)

Zero-copy JS TypedArray/ArrayBuffer to GPU buffer transfer. Available on all 3 browser backends.

```csharp
var jsArray = new Int16Array(data);
((IBrowserMemoryBuffer)buffer).CopyFromJS(jsArray);
// or
((IBrowserMemoryBuffer)buffer).CopyFromJS(arrayBuffer);
```

**Backend notes:**
- WebGPU: Uses `queue.WriteBuffer` directly
- WebGL: Copies to backing array, sets `NeedsUpload = true` (data uploaded on next dispatch, NOT immediately on GPU)
- Wasm: Pure JS-to-JS copy within SharedArrayBuffer

## CopyFromHost Buffer Rules

- `CopyFromHost(sourceArray)`: source.Length must be <= buffer.Length - targetOffset. Throws if too large. Partial fills allowed.
- Buffer sizes are padded to 4-byte alignment at creation (WebGPU requirement)
- Use `EnsureBuffer` pattern for grow-only reallocation (avoid Dispose+Allocate churn)

## Lambda Kernels (v4.4.0+)

Captured scalar values (int, float, etc.) are automatically passed to GPU. ArrayViews CANNOT be captured - they must be explicit kernel parameters.

```csharp
int multiplier = 5;
var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>>(
    (index, buf) => { buf[index] = index * multiplier; });
```

## Feature Matrix by Backend

| Feature | WebGPU | WebGL | Wasm | CUDA | OpenCL | CPU |
|---------|--------|-------|------|------|--------|-----|
| Shared Memory | Yes | No | Yes | Yes | Yes | Yes |
| Barriers | Yes | No | Yes | Yes | Yes | Yes |
| Atomics | Yes | No | Yes | Yes | Yes | Yes |
| In-kernel scatter / multi-store per thread† | Yes | No | Yes | Yes | Yes | Yes |
| Sub-word types | Yes | Yes | Yes | Yes | Yes | Yes |
| CopyFromJS | Yes | Yes | Yes | N/A | N/A | N/A |
| ILGPU Algorithms | Yes | Host‡ | Yes | Yes | Yes | Yes |
| Subgroups | Yes* | No | Emulated***** | Yes | Yes* | N/A |
| f64 native | No (emulated) | No (emulated) | Yes | Yes | Yes | Yes |
| i64 native | No (emulated) | No (emulated) | Yes | Yes | Yes | Yes |
| f16 native | Native or emulated** | No (emulated)*** | No (emulated) | Yes | Native or emulated**** | Yes |
| bf16 | Emulated | Emulated | Emulated | f32-reg + portable bit-manip (all arch) | Emulated | Native (managed) |
| FP8 (E4M3 + E5M2) | Emulated | Emulated | Emulated | f32-reg + portable bit-manip (all arch) | Emulated | Native (managed) |

*Subgroups: WebGPU requires browser support + adapter feature. OpenCL: device-dependent.
*****Wasm subgroups are EMULATED (no hardware warps): `WarpSize = 8` (mirrors CPU), `Warp.Shuffle`/`SubWarpShuffle` lower to a shared-memory exchange (write per-lane slot → barrier → read source-lane slot) — see `WasmBackend.WasmWarpSize` + `EmitWarpShuffle` in `WasmKernelFunctionGenerator.cs`. `LaneIdx = threadIdX % WarpSize`; `WarpIdx`/`IsFirstLane` derive in IL. Algorithm-layer warp Reduce/Scan route through `WasmWarpExtensions` (also shared-memory). Verified vs the CPU oracle (`SubgroupShuffleTest`, `ReduceMinMaxTest`). Fixed 2026-06-07 (`116c789`).
**WebGPU f16: native WGSL `f16` when the adapter exposes `shader-f16`, otherwise emulated in WGSL via `_f16_to_f32` / `_f32_to_f16` helpers with f32 arithmetic + packed u16 storage. `Capabilities.Float16` always true; `Capabilities.Float16Native` distinguishes. Emulation is lossless.
***WebGL f16: emulated via `_f16_to_f32` / `_f32_to_f16` GLSL helpers. Load through `texelFetch` on R32I + bit-extract, store through Transform Feedback uint. **Half RadixSort RUNS on WebGL** (host scatter path, 4.9.13+; unpacked-f32 working repr) and so do host `CreateScan`/`CreateReduce`; only the IN-KERNEL group Scan/Reduce (`GroupExtensions.*`) skip (need shared memory/barriers - they throw `UnsupportedKernelFeatureException`). The 5 non-algorithm Half tests run. `Capabilities.Float16Native` always false on WebGL.
†In-kernel scatter / multi-store per thread: a kernel where ONE thread writes &gt;1 element of the same output buffer at offsets that don't match the positional `v*storeCount+slot` layout, or scatters to an arbitrary computed index. WebGL Transform-Feedback captures one output record per vertex at the thread's OWN slot (gather-only), so a non-conforming such kernel silently drops/relocates stores. **Selection gate `AcceleratorRequirements.RequiresScatterStores` (WebGL=false) is shipped; the compile-time fail-loud guard is NOT (a blunt first attempt false-positived on 40 legit kernels - positional multi-store like `GpuMatrix4x4`/`Idct16` and the grid-stride loop idiom are both valid; backed out 2026-06-13, correct criterion is an OPEN problem).** SAFE on WebGL: one store/thread at own slot, positional `v*K+slot` multi-store, grid-stride loops, loops that only read/accumulate, multiple distinct output buffers (one store each), struct-element + emulated-64-bit stores. WebGL CAN still scatter at the host/algorithm layer (RadixSort via render-to-texture). See `WebGL/CLAUDE.md` "One-store-per-thread contract".
‡ILGPU Algorithms on WebGL = HOST-level only: `CreateRadixSort`/`CreateRadixSortPairs` (all key types incl. Half), `CreateScan` (Hillis-Steele multi-pass), `CreateReduce` (multi-pass) work via shared-memory-free multi-dispatch (draw-call boundary = barrier). IN-KERNEL group/warp ops (`Group.Scan`/`Reduce`, `Warp.*`) are structurally impossible (no shared memory/barriers) and throw `UnsupportedKernelFeatureException` at codegen (4.9.13+; previously silent-zero).
****OpenCL f16: native `half` type when `cl_khr_fp16` is available; emulated via `vload_half` / `vstore_half` (OpenCL built-ins that work without the extension) + f32 arithmetic otherwise. `Capabilities.Float16` always true on OpenCL; `Capabilities.Float16Native` reflects the `cl_khr_fp16` extension and selects the codegen path.
