# Development Notes

This page collects the non-obvious traps and conventions you need to know to contribute to SpawnDev.ILGPU without setting your day on fire.

## ⚠️ T4 Templates Silently Overwrite `.cs` Files in `ILGPU/`

**This is the #1 trap in this codebase.** Read this section before editing any `.cs` file under `ILGPU/`.

### What happens

The `ILGPU/` subdirectory contains the forked ILGPU core. Many `.cs` files in there are **generated** from `.tt` (T4 text-template) files — for example, `ILGPU/Static/CapabilityContext.cs` is generated from `ILGPU/Static/CapabilityContext.tt`.

- **Local incremental builds DO NOT run T4 transforms.** If you manually edit a generated `.cs` file, your edit sticks, your build passes, your tests pass, and you ship. Looks great.
- **Clean builds (CI, fresh clones, deploy pipelines, new dev machines) DO run T4 transforms.** They run the `.tt`, regenerate the `.cs`, and silently overwrite your manual edit. Then downstream consumers fail to compile or behave wrong.

This bug pattern has bitten this project at least three times, most recently 2026-04-25 when it blocked the GitHub Pages deploy with 9 OpenCL compile errors (`'CLCapabilityContext' does not contain a definition for 'Float16Native'`).

### How to avoid it

1. **Before editing any `.cs` file in `ILGPU/`, check for a sibling `.tt` file**:
   ```
   find ILGPU -name "<basename>.tt"
   ```
   If a `.tt` exists, edit the **template**, not the generated `.cs`.

2. **After ANY change to a `.tt` file, do a clean rebuild locally**:
   ```bash
   rm -rf ILGPU/obj ILGPU/bin
   dotnet build ILGPU/ILGPU.csproj
   ```
   Watch for `Templates transformed for ILGPU` in the output — that's T4 firing. If the build succeeds, commit BOTH the `.tt` and the regenerated `.cs` in the same change.

3. **If you find a `.cs` with a comment like "MANUAL edit", "NOT in sync with .tt", "re-apply this", that file is a ticking time bomb.** Port the manual edit into the matching `.tt`, regen the `.cs`, commit both. Then remove the warning comment.

4. **Run the drift-detection grep periodically**:
   ```bash
   grep -rn "MANUAL.*\.tt\|NOT in sync\|MANUAL edit\|MANUAL addition\|re-apply this" ILGPU/
   ```
   Every hit is a drift candidate that needs porting.

### CI guard

The repo runs a `T4 Drift Check` workflow (`.github/workflows/tt-drift-check.yml`) on every push and pull request that touches `ILGPU/` or `ILGPU.Algorithms/`. It does a clean build (which runs T4) then `git diff --exit-code` on the source tree. **Any push that introduces drift fails CI in ~30 seconds with a clear message** — including the exact files that were regenerated and a pointer back to this doc.

If you see this CI check fail, the fix is always:
1. Pull the regenerated files: `rm -rf ILGPU/obj && dotnet build ILGPU/ && git status`
2. Commit the regenerated `.cs` alongside whatever change you originally made.
3. If the regen result is wrong, you have manual edits that aren't in the `.tt`. Port them.

### Where T4 is and isn't used

- **T4 IS used in:** `ILGPU/` and `ILGPU.Algorithms/` (the forked core).
- **T4 is NOT used in:** `SpawnDev.ILGPU/` (browser backends — WebGPU, WebGL, Wasm), `SpawnDev.ILGPU.P2P/`, `SpawnDev.ILGPU.Demo/`, or any other top-level project. You can edit those `.cs` files directly.

The reason: ILGPU and ILGPU.Algorithms are upstream projects we forked, and they originally used T4 for cross-backend code generation. SpawnDev's own additions don't depend on T4.

## Other Notable Traps

### `SynchronizeAsync` vs `Synchronize` in Blazor WASM

Blazor WebAssembly runs on a single thread. The synchronous wait/observe APIs — `accelerator.Synchronize()` and `buffer.GetAsArray1D()` / `CopyToCPU()` — **throw `NotSupportedException` on the browser backends** (loud and immediate: the single thread cannot block-wait). Use `Flush()` if you only need to submit without waiting. Separately, **blocking on any async GPU work with `.Result` / `.Wait()` deadlocks the main thread** — the JS event loop can't pump message-channel callbacks while the C# call stack is blocked, so the resolution never arrives. Both are why you `await` the async forms.

In Blazor WASM, always use:
- `await accelerator.SynchronizeAsync()` (NOT `accelerator.Synchronize()`)
- `await buffer.CopyToHostAsync<T>()` (NOT `buffer.GetAsArray1D()`)

On desktop, both work. Async is recommended everywhere for cross-platform code.

### Browser Backend Capability Mismatches

WebGL has no atomics, no shared memory, no barriers. WebGPU and WebGL emulate Float64 and Int64. If your kernel relies on a feature, declare it via `AcceleratorRequirements` so the runtime selects a compatible backend instead of running on an incompatible one and producing silent garbage.

See [`capabilities-and-backend-selection.md`](capabilities-and-backend-selection.md) for the full flag set.

### The `[MethodImpl(NoInlining)]` Compile Cliff

Large helpers called from a kernel inline by default into the kernel body. With many call sites, this produces multi-thousand-line WGSL that hits Tint's validator size limit. Mark large multi-call helpers with `[MethodImpl(MethodImplOptions.NoInlining)]` so the codegen emits a real `fn` definition.

See [`kernels.md` - Helper Methods and Inlining](kernels.md#helper-methods-and-inlining).

## Build Commands

```bash
# Main library only (~2s incremental, ~10s clean)
dotnet build SpawnDev.ILGPU/SpawnDev.ILGPU.csproj

# Forked ILGPU core (runs T4 transforms on clean builds)
dotnet build ILGPU/ILGPU.csproj

# Full solution
dotnet build SpawnDev.ILGPU.slnx

# Demo apps
dotnet run --project SpawnDev.ILGPU.DemoConsole   # Desktop tests
dotnet run --project SpawnDev.ILGPU.Demo          # Browser tests via Blazor WASM
```

## Test Commands

PlaywrightMultiTest is the only way to run tests when it's in the solution (it is). It publishes the Blazor WASM app, starts the HTTPS server with the right COI headers, launches Chromium, runs browser + desktop tests in the right order, and writes a results JSON.

```bash
# Full suite (desktop + browser, ~8-10 min)
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj

# Filter to a specific area
dotnet test PlaywrightMultiTest/PlaywrightMultiTest.csproj --filter "FullyQualifiedName~AcceleratorRequirements"
```

See the project's CLAUDE.md files for more detailed conventions per directory.
