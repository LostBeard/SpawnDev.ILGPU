# Design: Offline Codegen + Build-Time Shader Precompilation + Runtime Cache

**Status:** Design / proposal (2026-06-09). Author: Geordi, with TJ.
**Audience:** SpawnDev.ILGPU contributors. A user-facing version lands in `Docs/` once Layer 1 + 2 are real.
**One-liner:** Let any backend GENERATE its shader/binary code without a real device (any host OS), so we can (a) debug generated code anywhere, (b) precompile kernels at BUILD TIME via an MSBuild task driven by `.csproj` flags + method attributes, and (c) load those precompiled artifacts at runtime to skip IL→shader transpilation.

This is the classic **AOT shader / pipeline-cache** pattern (Unity shader variants, Vulkan `VkPipelineCache`, D3D precompiled bytecode), adapted to SpawnDev.ILGPU's runtime IL→WGSL/GLSL/Wasm transpiler.

---

## 1. Motivation / use cases

1. **Cross-backend debugging on any machine.** Generate the WGSL/GLSL/Wasm for a kernel without owning that backend's device (e.g. inspect WebGPU output on a box with no WebGPU, dump a Wasm barrier kernel on the desktop). Today this is partially possible only as a side effect of `WGSLDumpPath` / `WasmCompileDump`; it should be a first-class, uniform API.
2. **Build-time precompilation for performance + determinism (TJ's `.csproj` idea).** Move IL→shader transpilation off the runtime hot path. A `.csproj` flag + per-method attributes declare which kernels to precompile, and an MSBuild task bakes the generated artifacts into the project. At runtime the accelerator loads the artifact instead of transpiling. Performance is the mission (global Rule 4); shader transpilation at startup is pure overhead we can pay once at build.
3. **Hardware-specific kernels (TJ).** Someone writes a method that must run on a specific device profile (e.g. `shader-f16`-capable Chrome WebGPU, or a fixed `MaxNumThreadsPerGroup`). Precompiling for that exact profile and shipping the artifact gives deterministic, zero-transpile-at-runtime behavior for that hardware, while still falling back gracefully elsewhere.

These are three consumers of ONE foundation: **device-independent code generation.**

---

## 2. Why this is tractable

The transpiler is already a pure **IL → shader** function. A real device is needed for exactly two things, neither of which is codegen:
- **Capability flags the codegen branches on** — and that surface is SMALL. Measured in the WebGPU/WebGL generators: `shader-f16` (native vs emulated f16), `MaxNumThreadsPerGroup`, `Capabilities.Float16Native` / `ForceEmulatedF16`, and the storage-buffer binding limit (`MaxStorageBufferBindings`, 10 on Chrome). f64 mode (`F64EmulationMode`) on WebGPU. That is a small, serializable **capability profile**.
- **Resource allocation + dispatch** — buffers, bind groups, pipelines, worker pools. None of this is needed to PRODUCE the shader text/bytes.

Evidence the transpilers already run without a device:
- **Wasm:** `SpawnDev.ILGPU.DemoConsole/WasmCompileDump.cs` (`-- wasm-dump`) creates a degraded accelerator on the desktop (the `BlazorJSRuntime.JS` lookup is wrapped in try/catch, defaults to 4 cores) and `CreateRadixSort*` eagerly compiles kernels via `LoadKernel` BEFORE any dispatch. The IL→wasm path runs fully offline.
- **WebGPU:** `WebGPUBackend.WGSLRegistry` (named registry of every compiled shader), `WGSLDumpPath` (auto-writes `{KernelName}.wgsl` to disk when set and `!OperatingSystem.IsBrowser()`), and the `OnShaderCompiled` hook all capture the generated WGSL string during compilation.
- **WebGL:** GLSL generation is the same shape (pure C# transpiler).

So the work is to FORMALIZE a codegen-only path (no fake accelerator, no device) driven by an explicit profile, and build the precompile + cache layers on top.

---

## 3. Architecture: three layers

```
Layer 1  OFFLINE CODEGEN  (foundation)
         CapabilityProfile + Backend transpiler  ->  shader/binary + metadata
         no device, any host OS

Layer 2  BUILD-TIME PRECOMPILE  (.csproj + attributes + MSBuild task)
         reflect built assembly -> run Layer 1 per (kernel x profile)
         -> embed artifacts as resources / emit C# registration

Layer 3  RUNTIME CACHE
         device profile -> lookup precompiled artifact (hit: skip transpile)
                        -> miss: fall back to runtime Layer-1 generation
```

Each layer is independently useful. Layer 1 ships value alone (debugging). Layer 2 needs Layer 1. Layer 3 needs Layer 1 (for fallback) and is most useful with Layer 2.

---

## 4. Layer 1 — Offline codegen

### 4.1 `CapabilityProfile`
A serializable description of the target device's codegen-relevant capabilities. Reuse the existing `AcceleratorRequirements` flag vocabulary (`RequiresAtomics`, `RequiresFloat16Native`, `RequiresInt64Native`, `RequiresSubGroups`, ...) inverted into a "device HAS" profile, plus the numeric limits codegen reads:

```csharp
public sealed record CapabilityProfile
{
    public BackendKind Backend { get; init; }          // WebGPU | WebGL | Wasm
    public bool Float16Native { get; init; }           // shader-f16 (WGSL) / cl_khr_fp16
    public bool Float64Native { get; init; }
    public F64EmulationMode Float64Mode { get; init; }  // Dekker | Ozaki | Disabled (WebGPU)
    public bool Int64Native { get; init; }
    public bool SubGroups { get; init; }
    public int  MaxNumThreadsPerGroup { get; init; }
    public int  MaxStorageBufferBindings { get; init; } // WebGPU binding ceiling (10 on Chrome)
    public int  WarpSize { get; init; }                 // Wasm = 8, etc.
    // ... only fields the transpilers actually branch on; keep minimal + versioned.
    public string Name { get; init; }                   // e.g. "Chrome-WebGPU-f16", "WebGL2-Quest3"
    public int  ProfileSchemaVersion { get; init; }     // bump when codegen gains a new cap branch
}
```

Provide **named presets** (`CapabilityProfiles.ChromeWebGPU`, `.ChromeWebGPUNoF16`, `.WebGL2Baseline`, `.WasmDefault`) AND a `CapabilityProfile.FromDevice(accelerator)` that snapshots a real device (so a developer ON the target hardware can capture an exact profile for build-time precompile of that hardware).

**Design rule:** the profile must contain EVERY cap the codegen branches on, and a drift guard (a test that asserts the transpiler reads nothing outside the profile) so an artifact can never silently depend on an un-profiled cap. (Same class of bug as `feedback-capability-list-must-derive-from-registry-not-duplicate` — derive, do not hand-maintain.)

### 4.2 Compile-only API
```csharp
// Host-OS-independent. No accelerator, no device.
GeneratedKernel ShaderCompiler.Generate<TKernel>(
    Delegate kernelMethod,            // or MethodInfo
    CapabilityProfile profile,
    KernelSpecialization? spec = null);

public sealed record GeneratedKernel
{
    public BackendKind Backend { get; init; }
    public string?  Source { get; init; }   // WGSL / GLSL text
    public byte[]?  Binary { get; init; }    // Wasm bytes
    public KernelMetadata Metadata { get; init; } // workgroup size, bindings, shared mem,
                                                  // barrier count, emulation flags, profile hash
    public IReadOnlyList<Diagnostic> Diagnostics { get; init; } // pre-validation results
}
```

Internally this drives the SAME `WGSLCodeGenerator` / `WGSLKernelFunctionGenerator` / `WasmKernelFunctionGenerator` / WebGL GLSL generator used at runtime - the artifacts MUST be byte-identical to what the live backend would emit for the same profile (otherwise the cache is a correctness hazard). The decoupling work is: feed `SetEmulationFlags` and the binding/limit checks from `CapabilityProfile` instead of a live adapter, and stop short of resource/pipeline creation.

### 4.3 Per-backend feasibility
| Backend | Generate offline? | Notes |
|---|---|---|
| **Wasm** | Yes (already, informally) | `WasmCompileDump` proves it. Formalize: drop the degraded-accelerator requirement; generate from profile directly. |
| **WebGPU (WGSL)** | Yes | `WGSLRegistry`/`WGSLDumpPath`/`OnShaderCompiled` already capture WGSL during compile. Feed `shader-f16`, binding limit, f64 mode from the profile. |
| **WebGL (GLSL)** | Yes | Pure GLSL transpiler. Feed f16-always-emulated + texel/binding constraints from the profile. |
| CUDA / OpenCL / CPU | Out of scope here | Handled by upstream ILGPU's own PTX/CL/IL paths; this doc targets the three browser transpilers SpawnDev added. (A profile abstraction could later cover them too.) |

### 4.4 Immediate dividend
Layer 1 alone gives a uniform `dotnet`-runnable "dump any kernel for any backend/profile" tool - replacing the ad-hoc `wasm-dump` and the WGSL side-channel, and directly serving live debugging (including the current Wasm large-sort investigation, which needs the emitted scan kernel).

---

## 5. Layer 2 — Build-time precompilation

This is the `.csproj` + MSBuild-task layer. **Aligns with the established SpawnDev pattern:** `SpawnDev.BlazorJS.WebWorkers` already uses an MSBuild task to build-time-patch the Blazor WASM dotnet JS files (so Blazor WASM runs in Web Workers + service workers - shipped in the Anaglyphohol Chrome extension), gated by a `.csproj` flag that toggles build-time patching vs runtime monkey-patching. We mirror that ergonomics exactly.

### 5.1 Declaration
**Method attributes** (the "what"):
```csharp
[PrecompiledKernel(BackendKind.WebGPU, Profile = "Chrome-WebGPU-f16")]
[PrecompiledKernel(BackendKind.Wasm,   Profile = "WasmDefault")]
static void MyKernel(Index1D i, ArrayView<float> data) { ... }
```
Multiple attributes = multiple (backend, profile) artifacts for one kernel. `Profile` names a registered `CapabilityProfile` preset (or a project-defined one).

**`.csproj` flags** (the "whether / global"):
```xml
<PropertyGroup>
  <SpawnDevPrecompileShaders>true</SpawnDevPrecompileShaders>  <!-- master toggle, mirrors WebWorkers flag -->
  <SpawnDevPrecompileProfiles>Chrome-WebGPU-f16;WasmDefault</SpawnDevPrecompileProfiles> <!-- optional global set -->
</PropertyGroup>
```
When the flag is OFF, behavior is exactly today's (runtime transpile) - precompilation is purely opt-in, never a hidden default.

### 5.2 The MSBuild task
A post-build `Task` (NOT a Roslyn source generator - see 5.3):
1. Loads the just-built consuming assembly via reflection (in a separate AppDomain/AssemblyLoadContext or a spawned `dotnet` worker for isolation - the same "load + run the real compile path" approach as `WasmCompileDump`).
2. Scans for `[PrecompiledKernel]` (and/or the global profile set).
3. Runs **Layer 1** `ShaderCompiler.Generate` for each (kernel × profile).
4. Emits artifacts per the packaging mode (§8, decided): **blobs as `wwwroot` content files** by default (`wwwroot/_shaders/{profile}/{kernelName}.{shorthash}.ext`) + a **tiny generated C# manifest** (kernel-key → profile → artifact URL + content hash + codegen version). Optional `Embedded`/`Both` modes for non-browser / single-DLL distribution.
5. Fails the build (or warns, per a strictness flag) if a declared kernel does not transpile for a declared profile - precompile errors surface at build, not at the user's runtime.

### 5.3 Why an MSBuild task, not a source generator
A Roslyn **source generator** runs against the compilation's syntax/semantic model and has **no compiled IL** to feed the transpiler - and our codegen consumes compiled IL (it reflects over real `MethodInfo`/IL). So a source generator cannot run the transpiler. The correct mechanism is a build **task/tool** that reflects the BUILT assembly (post-compile), exactly the model `WasmCompileDump` and the WebWorkers patcher use. Attributes declare intent; the task does the running. (A source generator could still play a minor supporting role - e.g. emitting the strongly-typed cache-registration glue - but it cannot produce the shaders.)

---

## 6. Layer 3 — Runtime cache

(Discussed previously for kernel perf; this slots it in.)

At `LoadKernel` time the accelerator:
1. Computes the cache key = **(stable kernel identity, active device profile, ProfileSchemaVersion, codegen version)**.
2. Looks up a precompiled artifact (from embedded resources / registered blobs, and/or an OPFS/IndexedDB runtime cache).
3. **Hit:** skip IL→shader transpilation; hand the artifact straight to pipeline/module creation.
4. **Miss:** fall back to runtime Layer-1 generation (today's behavior), optionally populating a runtime cache.

**Cache key / identity:** must be STABLE and collision-free - a content hash (SHA-256 of IL + profile) or method handle, **never `Object.GetHashCode()`** (we already paid for that lesson: the Wasm worker module-cache `kernelId`-from-`GetHashCode` collision bug; see `Wasm/CLAUDE.md` "kernelId MUST be a monotonic unique id"). Profile + schema version in the key prevents serving a stale artifact after codegen changes.

### 6.1 The runtime fallback is NON-NEGOTIABLE — where we deliberately diverge from upstream ILGPU

Upstream ILGPU's next-generation refactor (MoFtZ, [ILGPU#1387](https://github.com/m4rs-mt/ILGPU/issues/1387), 2025-09-04) is moving to AOT compilation and **removing runtime code generation entirely** — explicitly to "reduce the friction in supporting WebGPU." We share the *direction* (build-time transpilation, AOT-friendly), but we must **NOT** follow the full removal, and Layer 3's fallback-on-miss must **never** be "optimized away to match upstream."

**SpawnDev.ILGPU generates kernels DYNAMICALLY — not all kernels are knowable at build time.** The features that make this fork valuable produce kernels at *runtime*:
- **Lambda Kernels** — captured scalar values compiled into the kernel.
- **DelegateSpecialization** — one kernel, many ops, specialized at runtime.
- Above all, **the ML layer transpiling ONNX graphs into kernels at runtime** (SpawnDev.ILGPU.ML) — you cannot precompile a kernel that does not exist until a graph is loaded. (E.g. SD-Turbo's `FusedRegBlockedLinearActivation` is synthesized from the diffusion graph.)

Pure AOT (precompile everything, delete the runtime transpiler) is viable only when every kernel is **statically known**. For us it would amputate exactly those dynamic capabilities — capabilities upstream does not have, which is why removal is reasonable for *them* and wrong for *us*. So our shape is **AOT + runtime fallback**, never AOT-only:
- **Precompile** the static/hot kernels → the determinism + zero-runtime-transpile win upstream is chasing.
- **Keep the runtime IL→shader transpiler alive** for the dynamic long tail: runtime-generated kernels AND unknown/un-profiled device combinations the browser's device diversity throws at us.

This is the only shape that survives **both** the browser's device diversity **and** runtime-generated kernels. The fallback is a **load-bearing capability, not a stopgap.** A future "align with upstream / drop the now-redundant runtime path" pass that deletes it would silently break the entire ML graph path and every Lambda/Delegate kernel. **Do not remove the runtime transpiler.** (§7.1's profile-keyed + fallback-on-miss already encodes the mechanism; this section records *why* it is mandatory.)

---

## 7. Correctness invariants (global Rule 1 - no silent wrongness)

1. **Profile-keyed, profile-matched, fallback-on-miss.** A precompiled artifact is valid ONLY for devices matching its profile. An `shader-f16` WGSL artifact is WRONG on a non-f16 device. The runtime MUST match the real device's profile against the artifact's profile and fall back to runtime generation on any mismatch. Precompilation is a pure optimization that can never change results.
2. **Byte-identical to runtime.** Offline generation for profile P must equal what the live backend emits for a device matching P. Guard with a cross-check test: for representative kernels, `Generate(profile-of-this-device) == live-backend-output`.
3. **Drift guard on the profile.** The profile must enumerate every cap the transpiler branches on; a test derives the branch set and fails if codegen reads an un-profiled cap (prevents an artifact silently depending on something not in its key).
4. **Versioned artifacts.** Bump `ProfileSchemaVersion` / a codegen version when a new cap branch or codegen change lands; mismatched-version artifacts are ignored (treated as a miss), never trusted.
5. **Opt-in only.** Master `.csproj` flag OFF = today's exact behavior. No precompile path is a hidden default.

---

## 8. Open design decisions

- **Artifact packaging: DECIDED (TJ 2026-06-09).** Blobs default to **`wwwroot` content files** (`wwwroot/_shaders/{profile}/{kernelName}.{shorthash}.ext`), NOT embedded resources. Rationale: embedded resources compile into the DLL and are part of the `_framework/` download whether used or not - bad for Wasm kernel blobs (50-75 KB each in our own measurements). Content files are lazy/on-demand-fetched, independently cacheable (browser cache + service worker + OPFS, matching ModelHub), trivially inspectable (matches `ShaderDebugService`'s dump-to-folder shape), and make post-build analysis "look at the folder." The content-hash in the filename gives immutable browser caching. Only a **tiny generated C# manifest** (kernel-key → profile → artifact URL + hash + codegen version) lives in the assembly; the heavy blobs never bloat the DLL. A csproj enum `<SpawnDevPrecompilePackaging>Content|Embedded|Both</SpawnDevPrecompilePackaging>` (default `Content`) covers single-DLL distribution / non-browser desktop (no wwwroot) via `Embedded`. (Earlier "generated C# referencing embedded blobs" lean was reversed - it was Blazor-naive about always-downloaded DLL weight.)
- **Profile presets vs free-form:** ship a curated preset set (Chrome WebGPU ±f16, WebGL2 baseline, Wasm default) plus `FromDevice()` capture. How users define custom named profiles in `.csproj`.
- **Isolation for the build task:** separate `AssemblyLoadContext` vs spawned `dotnet` worker process (cleaner teardown, matches `WasmCompileDump` ergonomics) for loading + reflecting the consuming assembly.
- **Wasm specifics:** the Wasm "binary" embeds worker-count-independent codegen, but dispatch params (group size, worker pool) are runtime. Confirm the precompiled Wasm module is dispatch-parameter-independent (it should be - params are passed at dispatch, not baked).
- **Trimming / AOT:** reflecting kernel methods at build time must survive `PublishTrimmed`. Precompiled artifacts could actually HELP here (less runtime reflection), but the build task's own reflection needs trim roots.
- **CUDA/OpenCL/CPU:** keep out of v1, but design `CapabilityProfile` + `BackendKind` so they can join later without a breaking change.

---

## 9. Suggested build order

1. **Layer 1 first.** `CapabilityProfile` (+ presets + `FromDevice`), `ShaderCompiler.Generate` across WebGPU/WebGL/Wasm, the byte-identical + drift guards. Replace `wasm-dump`/`WGSLDumpPath` ad-hoc paths with it. Ships debugging value immediately and unblocks the offline kernel dump for the active Wasm-race work.
2. **Layer 3 runtime cache shell** (lookup + fallback + key), populated initially by `FromDevice()` at runtime (warm cache) - proves the hit path end to end before the build task exists.
3. **Layer 2 MSBuild task + `[PrecompiledKernel]` attribute + `.csproj` flags**, mirroring the WebWorkers patcher pattern; emit artifacts that Layer 3 already knows how to consume.
4. **`Docs/` user guide** once 1-3 are real.

---

## 10. References
- Existing offline hooks: `WebGPUBackend.WGSLRegistry` / `WGSLDumpPath` / `OnShaderCompiled`; `SpawnDev.ILGPU.DemoConsole/WasmCompileDump.cs`; `WebGPUBackend.LastWasmBinary` (Wasm `LastWasmBinary`).
- Capability vocabulary: `AcceleratorRequirements` (`Requires*` flags) + `Capabilities` (`ILGPU/Static/CapabilitiesImporter.ttinclude`) + the WebGPU/Wasm `CLAUDE.md` feature matrices.
- Precedent for the MSBuild-task + `.csproj`-flag ergonomics: **SpawnDev.BlazorJS.WebWorkers** build-time dotnet-JS patcher (build-time vs runtime monkey-patch toggle; shipped in the Anaglyphohol Chrome extension).
- Cache-key lesson: `Wasm/CLAUDE.md` "kernelId MUST be a monotonic unique id - NEVER GetHashCode".
- Industry pattern: AOT shader variants / pipeline caches (Unity, Vulkan `VkPipelineCache`, D3D precompiled bytecode).

---

## 11. Review decisions LOCKED (Tuvok design review + TJ go, 2026-06-09) — Layer 1 build spec

These supersede the earlier open phrasing in §4-§8 where they conflict. Implementing Layer 1 against these.

1. **API home = static `ShaderCompiler.Generate(Delegate kernel, CapabilityProfile profile, KernelSpecialization? spec = null)`** is canonical (the build task + offline dump have no Context/device). A thin `context.GenerateKernelCode<TKernel>(profile)` convenience is allowed ONLY as a Layer-3-fallback wrapper over the static core; the static form is the home. Do not make the device-bound form canonical for a device-independent feature.
2. **F64/F16 native gating (no enum pollution).** Reuse `F64EmulationMode` UNCHANGED — do NOT add a `Native` value (it would make the name lie). The profile's `Float64Native` bool is the gate: `true` → native path, `Float64Mode` ignored; `false` → consult `Float64Mode` (Dekker/Ozaki/Disabled). Document on the field: "Float64Mode is only meaningful when `!Float64Native`." Same shape for `Float16Native`.
3. **Byte-identical guard is STRUCTURAL, not an audit (most important invariant).** Route EVERY capability read in the three generators through `CapabilityProfile` — zero direct `accelerator.Capabilities.X` / adapter-feature reads on the codegen path. The profile becomes the single cap source by construction (runtime path builds a profile from its device and feeds the SAME generators). Enforce with a grep/analyzer test that fails on any direct device-cap access in the generator files. This unifies runtime + offline codegen through one cap interface.
   - **STATUS — VERIFIED 2026-06-09 (Geordi).** Audit of all six generator files (`WGSLCodeGenerator`, `WGSLKernelFunctionGenerator`, `GLSLCodeGenerator`, `GLSLKernelFunctionGenerator`, `WasmKernelFunctionGenerator`, `WasmCodeGenerator`) found **ZERO live-device cap reads** — the generators already consume only the backend's data-derived properties (`HasShaderF16`/`F64Mode`/`HasSubgroups`/`DefaultMaxWorkgroupSize`), which `ShaderCompiler.Generate` feeds from the profile (runtime feeds them from the device). So the invariant holds by construction TODAY. Guard implemented (`ShaderGenDump.CheckCapRoutingGuard`, run by `dotnet run -- shader-gen`): scans the six generator files for `Capabilities`/`requestAdapter`/`navigator.gpu`/`adapter.features`/`.Adapter.` and fails on any; currently **PASS**. (A Roslyn analyzer is the eventual CI form; the source-scan guard is the v1 enforcement.)
4. **Determinism is a hard requirement.** `(IL, profile) → bytes` must be deterministic: no dictionary-iteration-order dependence, no `GetHashCode`-derived naming (SSA `v_NNN` = monotonic counter), no timestamps in the cached artifact (the WGSL dump header currently embeds a timestamp — the cached artifact must exclude/normalize the header). Add a "generate twice == identical bytes" test.
5. **Generate path is JS-runtime-free** on all three backends (the build task runs on CI with no `BlazorJSRuntime.JS`). Confirm + enforce zero `JS.*` interop at generate time (JS only at dispatch). `WasmCompileDump`'s try/catch JS-default is the *informal* precedent; the formal path must not touch JS.
6. **Build-task isolation = spawned `dotnet` worker** (process isolation, clean teardown, matches `WasmCompileDump`), NOT `AssemblyLoadContext` (incomplete-unload + dep-version pitfalls). [Layer 2.]
7. **Runtime profile-match = EXACT profile-equality for v1** (Layer 3). Per-field compatibility relaxation (`>=` on threshold caps like `MaxStorageBufferBindings`) is a later cache-hit-rate optimization behind its own test. No fuzzy matching on day one (Rule-1 hazard).

**Layer 1 build order (in progress):** (a) `CapabilityProfile` + `GeneratedKernel` + `BackendKind` (pure data) → (b) presets + `FromAccelerator` → (c) structural cap-read routing through the profile (WebGPU first — bug #3 lives there) → (d) `ShaderCompiler.Generate` static entry → (e) guards: byte-identical, determinism, JS-free, no-direct-cap-read grep test.
