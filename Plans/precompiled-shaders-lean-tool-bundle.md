# Design: Lean precompiler-tool bundle (resolve the runtime closure from the consumer's output)

**Status:** Design / proposal (2026-06-11). Author: Geordi, with TJ. Target: **4.10.1** (4.10.0 shipped the FAT bundle).
**Audience:** SpawnDev.ILGPU contributors.
**One-liner:** Stop shipping a second copy of the whole ILGPU runtime inside the package's `tools/` folder. Ship ONLY `SpawnDev.ILGPU.Precompiler.dll` (+ its `.deps.json`/`.runtimeconfig.json`), and have the tool resolve `ILGPU.dll`, `ILGPU.Algorithms.dll`, `SpawnDev.BlazorJS.dll` and the `Microsoft.*` deps from the CONSUMER's own build output at precompile time - where they already exist. `tools/` drops from ~13 MB / 48 files to ~3 files / tens of KB.

---

## 1. Motivation

4.10.0 bundles the precompiler tool as a full `dotnet publish` closure (`_BundlePrecompilerTool` in `SpawnDev.ILGPU.csproj`, gated on `-p:PackPrecompilerTool=true`). That is **48 files, ~13 MB** in `tools/`: `ILGPU.dll`, `ILGPU.Algorithms.dll`, `SpawnDev.BlazorJS.dll`, the whole `Microsoft.AspNetCore.Components.*` + `Microsoft.Extensions.*` graph, plus PDBs.

It works and the cost is **developer-restore-only** (NuGet `tools/` never flows to a consumer's `bin/`, publish output, or a deployed Blazor WASM app - it sits in the dev's package cache). So this is NOT urgent. But it's wasteful: **the precompiler runs AFTER the consumer's build, against `$(TargetPath)`** (the just-built assembly), and the consumer's output directory ALREADY contains `ILGPU.dll` + `ILGPU.Algorithms.dll` + `SpawnDev.BlazorJS.dll` + the `Microsoft.*` deps (they are the consumer's own transitive references via the `SpawnDev.ILGPU` package). Bundling a second copy is pure duplication.

Examining TJ's `SpawnDev.BlazorJS.WebWorkers` bundle (an in-process MSBuild `Task` in `tasks/`, ~17 KB + framework deps) sparked this: WebWorkers' task does file/manifest work and needs no runtime closure, so it stays tiny. The precompiler genuinely needs the net10.0 ILGPU runtime (it instantiates `WebGPUBackend` and runs the transpiler), so it MUST be a process-isolated `dotnet <dll>` invocation - but it does NOT need to SHIP that runtime; it can borrow the consumer's.

## 2. Design

### 2.1 What ships in `tools/`
- `SpawnDev.ILGPU.Precompiler.dll`
- `SpawnDev.ILGPU.Precompiler.deps.json`
- `SpawnDev.ILGPU.Precompiler.runtimeconfig.json`

Nothing else. (~tens of KB.)

### 2.2 Runtime assembly resolution
`SpawnDev.ILGPU.Precompiler/Program.cs` already receives the consumer's built assembly path as `args[0]` (`$(TargetPath)` from the `.targets`). Its directory is the consumer's output dir, which holds the full dependency set. Add, as the FIRST thing in `Main`:

```csharp
var targetAssemblyPath = args[0];
var probeDir = Path.GetDirectoryName(Path.GetFullPath(targetAssemblyPath))!;

System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    // Resolve ILGPU/Algorithms/BlazorJS/Microsoft.* from the CONSUMER's output dir.
    var candidate = Path.Combine(probeDir, name.Name + ".dll");
    return File.Exists(candidate) ? ctx.LoadFromAssemblyPath(candidate) : null;
};
```

- Use `AssemblyLoadContext.Default` (the tool is a normal app; we just teach the default context to probe the consumer's dir). No separate ALC needed - the tool and the consumer target the SAME TFM (net10.0), so there is no isolation requirement, only a *location* one.
- The tool's OWN deps (whatever `SpawnDev.ILGPU.Precompiler.dll` references beyond the shared graph) still resolve normally from `tools/` via its `.deps.json`. Since the precompiler references `SpawnDev.ILGPU` (which it now borrows from the consumer), the resolver covers the heavy ones.

### 2.3 csproj changes
Replace `_BundlePrecompilerTool` (which `dotnet publish`'s the full closure) with a target that copies ONLY the three tool files into `tools/`:

```xml
<Target Name="_BundlePrecompilerToolLean">
  <MSBuild Projects="..\SpawnDev.ILGPU.Precompiler\SpawnDev.ILGPU.Precompiler.csproj"
           Targets="Build" Properties="Configuration=$(Configuration)" />
  <ItemGroup>
    <TfmSpecificPackageFile
      Include="..\SpawnDev.ILGPU.Precompiler\bin\$(Configuration)\net10.0\SpawnDev.ILGPU.Precompiler.dll;
               ..\SpawnDev.ILGPU.Precompiler\bin\$(Configuration)\net10.0\SpawnDev.ILGPU.Precompiler.deps.json;
               ..\SpawnDev.ILGPU.Precompiler\bin\$(Configuration)\net10.0\SpawnDev.ILGPU.Precompiler.runtimeconfig.json">
      <PackagePath>tools\</PackagePath>
    </TfmSpecificPackageFile>
  </ItemGroup>
</Target>
```

`build/SpawnDev.ILGPU.targets` is UNCHANGED - it still invokes `dotnet "$(SpawnDevPrecompilerToolPath)" "$(TargetPath)" ...`; the tool path still defaults to `..\tools\SpawnDev.ILGPU.Precompiler.dll`.

## 3. Edge cases to verify (do NOT trust on paper)
1. **Consumer is a library, not an app.** A library build's output dir copies direct + transitive references (CopyLocal default) - so `ILGPU.dll` etc. are present. Confirm on a real class-library consumer.
2. **Consumer is a Blazor WASM app.** Its `bin/$(Config)/net10.0/` holds the managed deps (pre-publish); the `_framework` brotli/wasm set is publish-only. The precompiler runs `AfterBuild`/`AfterPublish`; for the BUILD case the plain `bin` dir has the assemblies. Confirm both `Build` and `Publish` invocations resolve.
3. **A dep the consumer trims/does not copy.** If a needed assembly isn't in the consumer's output (unusual), the resolver returns null and the load fails with a clear `FileNotFoundException` naming the assembly - acceptable, and better than a silent wrong-version load. Log the probe dir on failure.
4. **Version skew.** The consumer's `ILGPU.dll` is the SAME version the precompiler was built against (both come from the `SpawnDev.ILGPU` package the consumer references). No skew by construction. Assert it (compare the loaded `ILGPU` assembly version to the precompiler's expectation; warn on mismatch).

## 4. Verification plan (gate before shipping)
- A real consumer sample project under `Examples/` (see the Examples work) that sets `<SpawnDevPrecompileShaders>true</SpawnDevPrecompileShaders>`, references the LEAN package from the local feed, builds, and asserts `wwwroot/_shaders/manifest.json` + sidecars are emitted and non-empty. Run for BOTH a console/library consumer and a Blazor WASM consumer.
- The existing `PrecompiledShaders_OfflineArtifact_HitsAndDispatches` / `_OfflineWGSL_MatchesRuntime` PMT tests stay green (they exercise the runtime side, unaffected by tool packaging).
- Diff the produced `tools/` (expect 3 files) and the artifact bytes (must equal the fat-bundle run's artifacts - same tool, same codegen).

## 5. Non-goals
- No change to the runtime `ShaderArtifactCache` / Layer-3 hit path (correct as of 4.10.0).
- No in-process MSBuild-task rewrite (the precompiler needs process isolation for the net10.0 runtime; WebWorkers' in-process pattern doesn't apply).

## 6. Rollout
4.10.1, behind the same `-p:PackPrecompilerTool=true` release-pack flag. The everyday `dotnet pack` stays tool-free. Once verified, it transparently replaces the fat bundle - consumers see a smaller restore, identical behavior.
