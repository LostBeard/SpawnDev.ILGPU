# Precompiled Shaders (Offline Codegen, Build-Time Precompile, Runtime Cache)

SpawnDev.ILGPU transpiles .NET IL into GPU shader languages (WGSL, GLSL) and WebAssembly at runtime. Precompiled shaders let you move that transpilation off the runtime hot path and generate/inspect shader code on any machine without the target device. It is the classic AOT shader / pipeline-cache pattern (Unity shader variants, Vulkan `VkPipelineCache`, D3D precompiled bytecode) adapted to this fork's runtime transpiler.

The system has three layers, each independently useful:

| Layer | What | Status |
|-------|------|--------|
| **1 - Offline codegen** | Generate a kernel's WGSL/GLSL/Wasm for a capability profile with NO device, on any host OS. Powers cross-backend debugging. | **Shipped** |
| **3 - Runtime cache** | At kernel-load time, use a ready artifact for the active device profile instead of transpiling; fall back to runtime generation on a miss. | **Shipped (WebGPU)** |
| **2 - Build-time precompile** | A `.targets` (gated by `<SpawnDevPrecompileShaders>`) spawns a worker that reflects the built assembly, runs Layer 1 per `[PrecompiledKernel]` (kernel x profile), and writes `wwwroot/_shaders/{manifest.json + sidecars}` the runtime loader consumes. | **Mechanism complete + build-cycle verified**; nupkg tool-shipping is the remaining deployment wiring (see [Layer 2 - Packaging](#packaging-shipping-the-build-tool)). |

> The whole pipeline is verified end-to-end with no device/browser: `dotnet run --project SpawnDev.ILGPU.DemoConsole -- precompile-e2e` (build-time precompile -> emit -> runtime load -> cache hit), and a real `dotnet build` of a consumer with `<SpawnDevPrecompileShaders>true</...>` emits the artifacts via the `.targets`.

> Precompilation is a pure optimization. It is opt-in, profile-matched, and falls back to runtime generation on any miss, so it can never change results (global Rule 1).

---

## Quick start

### Inspect a kernel's generated code on any machine (Layer 1)

```csharp
using SpawnDev.ILGPU;

// No accelerator, no device - runs on a CI box with no GPU.
GeneratedKernel gen = ShaderCompiler.Generate(
    (Index1D i, ArrayView<float> data) => data[i] = i,
    CapabilityProfiles.WebGPUFull);   // a named profile

Console.WriteLine(gen.Source);        // the WGSL text (or gen.Binary for Wasm)
Console.WriteLine(gen.Metadata.GroupSize);
foreach (var d in gen.Diagnostics)    // pre-validation results, if any
    Console.WriteLine($"{d.Severity}: {d.Message}");
```

Or dump from the command line:

```bash
dotnet run --project SpawnDev.ILGPU.DemoConsole -- shader-gen
```

This replaces the older ad-hoc `WGSLDumpPath` / `wasm-dump` side channels with one uniform "generate any kernel for any backend/profile" path. It is JS-free and deterministic: `(IL, profile)` always produces identical bytes.

### Declare a kernel for build-time precompilation (Layer 2)

```csharp
[PrecompiledKernel(AcceleratorType.WebGPU, Profile = "WebGPU-Dekker-Subgroups-NativeF16")]
[PrecompiledKernel(AcceleratorType.Wasm,   Profile = "Wasm-NativeF64-Subgroups")]
static void MyKernel(Index1D i, ArrayView<float> data) { /* ... */ }
```

```xml
<!-- in your .csproj -->
<PropertyGroup>
  <SpawnDevPrecompileShaders>true</SpawnDevPrecompileShaders>
</PropertyGroup>
```

When the flag is on, the build emits the precompiled artifacts (see [Layer 2](#layer-2--build-time-precompilation)); at startup the runtime registers them so the first load of `MyKernel` is a cache hit instead of a transpile. When the flag is off, behavior is exactly today's (runtime transpile) - precompilation is never a hidden default.

---

## Capability profiles

A `CapabilityProfile` is a serializable description of the codegen-relevant capabilities of a target device - only the things the transpilers actually branch on (native vs emulated f16/f64/i64, subgroups, max threads per group, storage-buffer binding limit, warp size). It is the single source of capability truth: the code generators read **only** from the profile, both offline and at runtime, so an offline artifact is byte-identical to what the live backend emits for a matching device.

### Built-in presets

> **f16 (and f64, i64) work on every backend.** Where the hardware lacks them, SpawnDev.ILGPU emulates them losslessly (f16 via `_f16_to_f32` / `_f32_to_f16` bit conversion, f64 via Dekker/Ozaki, i64 via paired u32). A profile's `Float16Native` / `Float64Native` / `Int64Native` flags do **not** gate whether the type is *supported* - they only select which **codegen path** the transpiler emits (native `f16` type vs emulation helpers + packed storage), because that changes the emitted shader and therefore the artifact. The `f16` in a preset NAME refers to native `shader-f16`; an artifact without it still runs f16 kernels, just via the emulated path.

WebGPU presets are keyed by **capability**, not by browser (WGSL is a W3C standard; what differs between browsers is the feature/limit set):

| Preset (`CapabilityProfiles.*`) | Name string | f64 | f16 codegen | subgroups | Notes |
|---|---|---|---|---|---|
| `WebGPUFull` | `WebGPU-Dekker-Subgroups-NativeF16` | Dekker (emul.) | native `shader-f16` | yes | modern Chrome-class; 10 bindings |
| `WebGPUNoSubgroups` | `WebGPU-Dekker-NativeF16` | Dekker (emul.) | native `shader-f16` | no (shared-mem fallback) | typical Firefox-class today |
| `WebGPUBaseline` | `WebGPU-Dekker` | Dekker (emul.) | emulated f16 | no | broadest device match (all paths emulated) |
| `WebGL2Baseline` | `WebGL2-Dekker` | Dekker (emul.) | emulated f16 | no | i64 also emulated; no shared memory/atomics/barriers |
| `WasmDefault` | `Wasm-NativeF64-Subgroups` | native | emulated f16 | yes (8-wide emulated warps) | native f64 + i64; 256 threads/group |

Profile names are ordered by codegen impact: **f64 strategy** (highest - changes the shader *and* the result; `Dekker`/`Ozaki`/`noF64` when emulated, `NativeF64` when native), then **`Subgroups`**, then **`NativeF16`**. The absence of `NativeF16` means *emulated* f16, not "no f16" - every profile runs f16 kernels. The name is a human label only; the cache key hashes the full field set, so it never needs to encode every field (i64-native and numeric limits are intentionally not in the name). There is room to add `Ozaki`/`noF64` f64 variants when first needed.

Every profile above supports f16 kernels; the column says which f16 codegen path the artifact uses. The `Profile = "..."` string on `[PrecompiledKernel]` (and the `.csproj` profile list) resolves against these names via `CapabilityProfiles.Resolve(name)`.

### Custom and device-snapshot profiles

```csharp
// Register a project-defined profile (resolvable by its Name).
CapabilityProfiles.Register(myProfile);

// Snapshot a LIVE device's effective capabilities - the only sanctioned place to read
// caps off a real device. Run this ON the target hardware to capture an exact profile
// for build-time precompile of that hardware.
CapabilityProfile p = CapabilityProfiles.FromAccelerator(accelerator);
```

---

## Layer 1 - Offline codegen

`ShaderCompiler.Generate(Delegate kernel, CapabilityProfile profile, KernelSpecialization? spec = null)` is the canonical, device-independent entry point. It drives the SAME `WGSLCodeGenerator` / `WasmKernelFunctionGenerator` / GLSL generator used at runtime, fed by the profile instead of a live adapter, and stops short of resource/pipeline creation. The result:

```csharp
public sealed record GeneratedKernel
{
    AcceleratorType Backend;            // WebGPU | WebGL | Wasm
    CapabilityProfile Profile;          // what it was generated for
    string? Source;                     // WGSL/GLSL text (text backends)
    byte[]? Binary;                     // Wasm bytes (binary backend)
    GeneratedKernelMetadata Metadata;   // group size, bindings, shared mem, barriers, emulation flags
    object? CodegenMetadata;            // backend-specific dispatch metadata (see Layer 3)
    IReadOnlyList<GeneratedKernelDiagnostic> Diagnostics; // pre-validation results
    bool HasErrors;
}
```

Guarantees (enforced by guard tests): generation is **deterministic** (no `GetHashCode`-derived naming, no timestamps in the artifact), **JS-runtime-free** (runs on CI with no `BlazorJSRuntime`), and **byte-identical** to what the live backend emits for a matching profile.

Use it for: debugging generated code on a machine without that backend, diffing codegen changes, and (with a pre-validator wired in) reporting a shader-validation error - e.g. a WGSL `cannot assign 'f32' to 'i32'` - with no device or browser.

---

## Layer 3 - Runtime cache

At kernel-load time the accelerator computes a cache key of `(stable kernel identity, active device profile)` and looks it up in `ShaderArtifactCache`:

- **Hit:** rebuild the compiled kernel from the cached shader + its `CodegenMetadata` (the dispatch info - scalar packing, binding count, i64-spinlock indices, coalesce manifest, dynamic-shared overrides - that is NOT recoverable from the shader text), skipping the transpiler.
- **Miss:** run the normal codegen and warm-register the result, so later loads in the same session hit even without build-time precompilation.

```csharp
ShaderArtifactCache.Hits;     // counters
ShaderArtifactCache.Misses;
ShaderArtifactCache.Count;    // registered artifacts
ShaderArtifactCache.Enabled = false;  // kill switch: force pure runtime generation (A/B, debugging)
```

Kernel identity is the method's full signature (declaring type + name + parameter types) - stable across builds, identical between the build-time task and the runtime, and never `Object.GetHashCode()` (a non-unique heuristic - see the kernelId-collision lesson in `Wasm/CLAUDE.md`).

> Currently implemented for the **WebGPU** backend. The Wasm artifact is self-contained bytes (dispatch-parameter-independent); a Wasm load-hook is a follow-up.

---

## Layer 2 - Build-time precompilation

> **Status: mechanism complete + build-cycle verified.** The `[PrecompiledKernel]` attribute, the `ShaderPrecompiler` worker, the spawnable `SpawnDev.ILGPU.Precompiler` tool, the `SpawnDev.ILGPU.targets` automation, and the runtime loader all work and are verified end-to-end (a real `dotnet build` of a consumer emits artifacts; the runtime loads them). The remaining piece is shipping the worker tool inside the NuGet package - see [Packaging](#packaging-shipping-the-build-tool).

### Declaration

Method attributes say **what** to precompile:

```csharp
[PrecompiledKernel(AcceleratorType.WebGPU, Profile = "WebGPU-Dekker-Subgroups-NativeF16")]
[PrecompiledKernel(AcceleratorType.Wasm,   Profile = "Wasm-NativeF64-Subgroups")]
static void MyKernel(Index1D i, ArrayView<float> data) { /* ... */ }
```

`.csproj` flags say **whether** and the global set:

```xml
<PropertyGroup>
  <SpawnDevPrecompileShaders>true</SpawnDevPrecompileShaders>            <!-- master toggle -->
  <SpawnDevPrecompileProfiles>WebGPU-Dekker-Subgroups-NativeF16;Wasm-NativeF64-Subgroups</SpawnDevPrecompileProfiles> <!-- optional global set -->
  <SpawnDevPrecompilePackaging>Content</SpawnDevPrecompilePackaging>     <!-- Content (default) | Embedded | Both -->
</PropertyGroup>
```

### What the build emits

By default (`Content` packaging), blobs are written as `wwwroot` content files so they are lazily fetched on demand, independently cacheable (browser cache + service worker + OPFS), and trivially inspectable:

```
wwwroot/_shaders/
  manifest.json                                  # kernel-key -> [{backend, profile, artifact path, content hash, codegen version}]
  {profile}/{kernelName}.{shorthash}.wgsl        # WebGPU shader text
  {profile}/{kernelName}.{shorthash}.meta.json   # CodegenMetadata sidecar (dispatch info)
  {profile}/{kernelName}.{shorthash}.wasm        # Wasm module bytes
```

The content hash in the filename gives immutable browser caching. Only the tiny `manifest.json` is consulted at startup; the heavy blobs are fetched on demand. `Embedded` packaging instead bakes blobs into the assembly (for non-browser / single-DLL distribution); `Both` does both.

### How it runs

1. A post-build MSBuild task (gated by `SpawnDevPrecompileShaders`) spawns an isolated `dotnet` worker that loads the just-built assembly by reflection.
2. The worker scans for `[PrecompiledKernel]` (and the global profile set), runs `ShaderCompiler.Generate` for each (kernel x profile), and writes the sidecars + manifest.
3. The build **fails** (or warns, per a strictness flag) if a declared kernel does not transpile for a declared profile - precompile errors surface at build time, not at the user's runtime.
4. At app startup the runtime fetches `manifest.json` + the referenced sidecars and registers each artifact into `ShaderArtifactCache`, so the first load of a precompiled kernel is a Layer 3 hit.

This mirrors the established `SpawnDev.BlazorJS.WebWorkers` pattern (a build-time MSBuild task gated by a `.csproj` flag).

> **Why a build tool, not a Roslyn source generator:** the transpiler consumes compiled IL (it reflects over real `MethodInfo`/IL), which a source generator does not have. The correct mechanism is a tool that reflects the BUILT assembly - the same model `WasmCompileDump` and the WebWorkers patcher use. The worker logic lives in `ShaderPrecompiler.Run` (in the library, so it is unit-testable in-process); `SpawnDev.ILGPU.Precompiler` is the thin spawnable host the `.targets` runs.

### Runtime: warming the cache (opt-in, lazy, WebWorkers-safe)

Nothing fetches at startup automatically. The consumer opts in by configuring the loader with the manifest URL and a fetch delegate, then warms kernels on demand:

```csharp
// Once, when wiring GPU work. A SpawnDev.BlazorJS.WebWorkers worker that runs no kernels
// never calls this, so it never touches the network.
ShaderArtifactManifestLoader.Configure(
    "_shaders/manifest.json",
    async url => await http.GetByteArrayAsync(url));   // BlazorJS fetch in-browser / HttpClient on desktop

// Lazy, per-kernel, right before a kernel is loaded (fetches just that one artifact):
await ShaderArtifactManifestLoader.TryWarmAsync(myKernelMethodInfo, profile);
// -> the next LoadKernel is a Layer 3 cache HIT (no transpile).

// Or preload the whole matching-profile set up front:
await ShaderArtifactManifestLoader.WarmAllAsync(profile);
```

Not configuring it = pure runtime transpile (today's behavior). `fetch` is a `Func<string,Task<byte[]>>`, so the library core takes no `SpawnDev.BlazorJS` dependency and the loader is unit-testable with an in-memory fetch.

### Packaging: shipping the build tool

The `.targets` is auto-imported from the package `build/` folder and is gated OFF (no opt-in = unaffected). It locates the worker via `$(SpawnDevPrecompilerToolPath)`, which defaults to the package `tools/` folder and is overridable for a source/repo build:

```xml
<!-- source/repo build: point at the tool's bin output -->
<SpawnDevPrecompilerToolPath>$(RepoRoot)\SpawnDev.ILGPU.Precompiler\bin\$(Configuration)\net10.0\SpawnDev.ILGPU.Precompiler.dll</SpawnDevPrecompilerToolPath>
```

The remaining deployment wiring is bundling the `SpawnDev.ILGPU.Precompiler` tool (+ its dependency closure) into the package `tools/` folder so a NuGet consumer gets it with no override. (Options: bundle in `tools/`, ship a sibling build-tool package, or a `dotnet` tool - a packaging-design choice; the mechanism above is independent of it.)

---

## Correctness invariants

1. **Profile-keyed, profile-matched, fallback-on-miss.** An artifact is valid only for devices whose profile matches exactly (v1 uses exact profile equality). A `shader-f16` artifact is wrong on a non-f16 device, so the runtime matches profiles and falls back to runtime generation on any mismatch.
2. **Byte-identical to runtime.** Offline generation for profile P equals what the live backend emits for a device matching P. The generators read capabilities only through `CapabilityProfile` (structural guard), so this holds by construction.
3. **Deterministic + versioned.** `(IL, profile)` produces identical bytes; artifacts carry a profile schema + codegen version, and a version mismatch is treated as a miss (never trusted).
4. **Opt-in only.** Master `.csproj` flag off = today's exact behavior.

## The runtime transpiler stays - deliberately

SpawnDev.ILGPU generates kernels **dynamically**: Lambda Kernels (captured scalars), DelegateSpecialization (one kernel, many ops), and above all the ML layer transpiling ONNX graphs into kernels at runtime (you cannot precompile a kernel that does not exist until a graph is loaded). So this fork is **AOT + runtime fallback**, never AOT-only: precompile the static/hot kernels for the determinism win, and keep the runtime IL-to-shader transpiler alive for the dynamic long tail and for unknown/un-profiled devices. The fallback is a load-bearing capability, not a stopgap. (This is where we deliberately diverge from upstream ILGPU's AOT-only direction; see `Plans/precompiled-shaders.md` 6.1.)

---

## See also

- Design + locked decisions: `Plans/precompiled-shaders.md`
- Capability gating for backend selection: `Docs/capabilities-and-backend-selection.md`
- WebGPU codegen internals: `SpawnDev.ILGPU/WebGPU/CLAUDE.md`
- Cache-key lesson (never `GetHashCode` for identity): `SpawnDev.ILGPU/Wasm/CLAUDE.md`
