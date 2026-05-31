# Trip + TJ session handoff — SpawnDev.ILGPU & SpawnDev.ILGPU.ML

**Date:** 2026-05-29  
**Audience:** Tuvok (and crew)  
**Authors:** Trip (Cursor agent) with Captain (TJ)  
**Package head (ILGPU source tree):** `4.9.10-local.15` (uncommitted at time of writing)  
**ML consumer pin (csproj):** still `4.9.10-local.12` — bump when validating ML against latest ILGPU bits

---

## Executive summary

This session had two intertwined goals:

1. **Unblock SpawnDev.ILGPU.ML** — `TensorView<T>` / `Half` kernels that wrote zeros on Wasm and WebGL while WebGPU worked.
2. **Close a 48-failure PMT regression wedge** on Wasm (`GridStrideLoopKernel` param mapping) and WebGL (undeclared `v_*` / struct hoisting / BVH).

**Outcome:**

| Area | Result |
|------|--------|
| ML `TensorView_Half_RoundTrip` | **Pass** WebGPU, Wasm, WebGL (`_mldump/latest.json`, 2026-05-29) |
| Full PMT pre-fix | 1648 pass / **48 fail** (Wasm-only lanes) |
| Concurrent stress post-fix (`01-43`) | 1686 pass / **10 fail** (WebGL undeclared vars + 2 env) |
| Concurrent stress post local.14/15 (`03-22`) | 1694 pass / **2 fail** |
| Concurrent stress latest (`12-14`) | **1695 pass / 1 fail each** (Wasm large-sort only, complementary tests) |

**Not shipped:** no git commit/push in this handoff unless Captain requests it. CHANGELOG.md documents through `local.12` only; `local.13`–`local.15` entries belong in the next changelog commit.

---

## Process / tooling (read first)

### Local NuGet feed — hierarchical layout only

Trip briefly corrupted the feed by bypassing Captain's publish bats and by a bad flat `copy /Y` into `D:\users\SpawnDevPackages`. **Fixed:** `_publish-nuget.local.release.bat` at repo root now documents hierarchy-only publish.

**Correct layout:**

```
D:\users\SpawnDevPackages\<package-id>\<version>\<package>.<version>.nupkg
```

**Correct publish:**

```bat
cd D:\users\tj\Projects\SpawnDev.ILGPU
dotnet build SpawnDev.ILGPU\SpawnDev.ILGPU\SpawnDev.ILGPU.csproj -c Release
dotnet pack   SpawnDev.ILGPU\SpawnDev.ILGPU\SpawnDev.ILGPU.csproj -c Release -o SpawnDev.ILGPU\SpawnDev.ILGPU\bin\Release --no-build
_publish-nuget.local.release.bat
```

Never `dotnet nuget push` to the feed root. Never hand-copy `.nupkg` to `SpawnDevPackages\`.

### PMT

- ILGPU: `SpawnDev.ILGPU\PlaywrightMultiTest\PlaywrightMultiTest.csproj`
- ML: `SpawnDev.ILGPU.ML\PlaywrightMultiTest\PlaywrightMultiTest.csproj`
- Prefer `$env:PMT_FILTER='...'` when parallel scheduler is off
- Stress dumps: `SpawnDev.ILGPU\_tj_dump_local\`, `_tj_dump_local_2\`
- ML shader dumps: `SpawnDev.ILGPU.ML\_mldump\` (`ShaderDebugService` + `TestResultsWriter`)

---

## SpawnDev.ILGPU — version ladder (4.9.10-local.12 → local.15)

Fork packages unchanged: `SpawnDev.ILGPU.Fork` / `SpawnDev.ILGPU.Algorithms.Fork` at `2.0.7`.

### local.12 — Float16 `PrimitiveValue` codegen (ML Half unblock)

**Problem:** `Float16` IR constants store raw half bits in `PrimitiveValue.rawValue`. Wasm/WebGL promoted via `Float32Value`, reinterpreting bits as IEEE-754 single → near-zero garbage. Kernels assigning `(Half)1.5f` wrote zeros.

**Fix:**

| File | Change |
|------|--------|
| `Wasm/Backend/WasmCodeGenerator.cs` | `GenerateCode(PrimitiveValue)`: emit `(float)value.Float16Value` when `BasicValueType == Float16` |
| `WebGL/Backend/GLSLCodeGenerator.cs` | Same for GLSL |

**Verified:** `ML_ArrayView1D_Half_*`, `ML_TensorView_Half_RoundTrip_CrossAssembly`, `ViewStructHalf_*` — all backends in full PMT.

---

### local.2–local.7 (ML TensorView path — iterative, before full PMT wedge)

Published during ML debugging (`local.2` … `local.7`). Highlights still in tree:

| Theme | Files | What |
|-------|-------|------|
| Cross-assembly inlining | `WebGL/Backend/GLSLKernelFunctionGenerator.cs` | `GenerateCode(MethodCall)` inlines single-block `MethodFlags.Inline` callees from other assemblies (e.g. `TensorView.Get2D`) at call site |
| Cross-assembly inlining | `Wasm/Backend/WasmKernelFunctionGenerator.cs` | `inlineCrossAssembly` path when callee not in `HelperMethods` |
| `TensorView<T>` body struct | `GLSLKernelFunctionGenerator.cs`, `WasmKernelFunctionGenerator.cs` | `IsTensorViewLikeBodyStruct` — one view + ≥4 `int32` shape fields; must not classify as `ArrayView3D` |
| ML consumer | `SpawnDev.ILGPU.ML/.../TensorView.cs` | `[MethodImpl(AggressiveInlining)]` on `Get1D`–`Set4D` so IR inlines accessors into kernels |

**Regression tests (ILGPU demo, not ML repo):** `BackendTestBase.Tests24.TensorViewStructParam.cs` — mirrors ML `TensorView` layout for WebGPU scalar-slot, Wasm/WebGL zero-output, Half Get2D/Set2D.

---

### local.13 — Wasm `IrUserParamIndexOffset` (48-failure Lane A/B/C)

**Problem:** Dispatch used `irParamIdx = paramIdx + 1`, assuming IR param 0 is always implicit `Index`. **`GridStrideLoopKernel`** places `LongIndex1D` extent at IR param 0 (`startIdx = 0` in codegen). Every view/struct scratch mapping was off by one → `memory access out of bounds` in radix, reduce, capturing-lambda tests.

**Fix:**

| File | Change |
|------|--------|
| `Wasm/Backend/WasmCodeGenerator.cs` | `GeneratorArgs.IrUserParamIndexOffset` |
| `Wasm/Backend/WasmKernelFunctionGenerator.cs` | Set from `SetupParameters` `startIdx` (0 = GridStride / explicit body struct first; 1 = typical Index-first kernel) |
| `Wasm/Backend/WasmCompiledKernel.cs` | Persist `IrUserParamIndexOffset` on compiled kernel |
| `Wasm/Backend/WasmBackend.cs` | Pass offset into `WasmCompiledKernel` ctor |
| `Wasm/WasmAccelerator.cs` | All `paramIdx + 1` → `paramIdx + compiledKernel.IrUserParamIndexOffset`; struct serialization index fix |

**Verified (scoped PMT):** `WasmMinimalPairsSortDiag`, `ILGPUReduce*`, `CapturingLambda*`, `RegisterHeavyBody*`, `AlgorithmRadixSortPairsTest`, `RadixSortBoundary16K`, `GlobalInclusiveScan*`.

---

### local.14 — WebGL undeclared `v_*` + Wasm yield scaling

#### WebGL — GLSL scope / hoisting (Lanes from concurrent stress `01-43`)

**Problem:** Multi-block kernels use a GLSL `switch` state machine. Variables declared in one `case` are invisible in siblings. `declaredVariables` was updated on comment-only aliases (buffer param loads, `NewView` forwards) without emitting declarations → `v_34` undeclared (`IntMathTest`), same for `QR_Render_*`, `SpecializedIntrinsicsTest`.

**Fix (representative):**

| Mechanism | File | Behavior |
|-----------|------|----------|
| `TryEmitDeclaration` | `GLSLCodeGenerator.cs` | Declare-with-init only if name not already in `declaredVariables` |
| `EmitLeaIntPointer` / `EmitTypedAssignment` / `EmitHoistedOrTypedAssignment` | `GLSLCodeGenerator.cs`, `GLSLKernelFunctionGenerator.cs` | Centralize declare-vs-assign |
| `AnalyzeHoisting` | `GLSLKernelFunctionGenerator.cs` | Hoist `PointerType` (LEA results), not only primitives/structs |
| `EmitHoistedDeclarations` | `GLSLKernelFunctionGenerator.cs` | Pointer hoists as `int` + `0`, not element struct type |
| `PushPhiValues` | `GLSLKernelFunctionGenerator.cs` | `Declare()` phi target if not yet declared |
| `GenerateCode(Alloca)` / `NewView` | `GLSLKernelFunctionGenerator.cs` | `declaredVariables.Add` only when a real `int` pointer or array backing is emitted; `NewView` forwards `_allocaArrayNames` without fake declare |
| `GenerateCode(Load)` buffer alias | `GLSLKernelFunctionGenerator.cs` | Comment-only alias for view param loads — no `declaredVariables.Add` |
| NoInlining redefinition | `GLSLKernelFunctionGenerator.cs` | `declaredVariables.Add` on array alloca only when `firstPtrDecl` — avoids duplicate `int v_N` when helper + kernel both touch same alloca name |

**Root cause evidence (IntMath):** branch A `int v_34 = int(_idx)`; branch B `v_34 = ...` only — classic ES 3.0 scope error.

**Verified (PMT, clean machine):** `IntMathTest`, `QR_Render_GPU`, `QR_Render_GPU_WithLogo`, `QR_Render_GPU_CPUMatch`, `SpecializedIntrinsicsTest`.

#### Wasm — `MAX_YIELD_ITERS` under contention

**Problem:** Under Fallout76 + dual full PMT, large radix sorts still flake (~1 failure/sweep) with sort-order violations. Trip's Variant C notify path is in tree; yield cap was too low for heavy CPU contention.

**Fix:** `Wasm/WasmAccelerator.cs` — `BuildWasmWorkerScript`:

```csharp
int maxYieldIters = Math.Max(10000, compiledKernel.BarrierCount * Math.Max(phaseCount, 1) * 250);
```

**Verified (scoped PMT):** `RadixSortDescending2MTest`, `RadixSortDescending4MTest` on quiet machine.

**Note:** `Wasm/Notes/residual-sort-race-2026-05-25.md` remains the investigation diary. Broadcast tag handshake (in CHANGELOG 4.9.10 stable section) is separate from this yield scaling.

---

### local.15 — WebGL BVH struct constructor regression

**Problem:** After PointerType hoisting (local.14), `EmitHoistedDeclarations` used `TypeGenerator[PointerType]` → element type (`struct_BVHNode`). Init was `struct_N(0)` (one arg). Nested structs (`BVHNode` → `AABB` → `Vec3`) need per-field GLSL constructors. ANGLE: *"Number of constructor parameters does not match structure fields"* — `WebGLTests.BVHRayTraversalTest`.

**Fix:**

| File | Change |
|------|--------|
| `WebGL/Backend/GLSLCodeGenerator.cs` | `GetStructDefaultInitializer(StructureType)` — recursive per-field literals; used in `NullValue` |
| `WebGL/Backend/GLSLFunctionGenerator.cs` | Non-void helper fallback `return` uses `GetStructDefaultInitializer` |
| `WebGL/Backend/GLSLKernelFunctionGenerator.cs` | Hoists: `PointerType` → `int`/`0`; `StructureType` → full initializer |

**Verified (PMT):** `BVHRayTraversalTest` + `IntMathTest` regression guard.

---

## SpawnDev.ILGPU — verification matrix

### Full single-machine PMT (pre concurrent fixes)

| Metric | Value |
|--------|-------|
| Passed | 1648 |
| Failed | 48 (Wasm only) |
| ML Half tests | All pass all backends |

### Concurrent stress (Fallout76 + dual PMT)

| Dump | Passed | Failed | Failures |
|------|--------|--------|----------|
| `01-43-26` / `01-43-28` (pre WebGL hoisting) | 1686 | 10 | WebGL IntMath, QR×3, SpecializedIntrinsics; Wasm radix contention; WebGPU Instance (env) |
| `03-22-01` / `03-22-02` (post local.14) | 1694 | 2 | `BVHRayTraversal` (fixed in local.15); WebGPU radix Instance (env) |
| `12-14-20` / `12-14-22` (post local.15) | **1695** | **1 each** | Run1: `WasmTests.RadixSortDescending4MTest`; Run2: `WasmTests.RadixSortAscending1_4MTest` — complementary pass on other run |

**Interpretation for Tuvok:** Wasm large-sort failures under dual PMT + heavy load match the **residual sort race** documented in `Wasm/Notes/residual-sort-race-2026-05-25.md` (~1 random large sort per sweep, magnitude scales with contention). **Not** the fixed GridStride OOB wedge. WebGPU `Instance reference no longer exists` did **not** appear in `12-14` dumps.

### Tests that must stay green after any Wasm/WebGL codegen edit

```
IntMathTest
QR_Render_GPU / QR_Render_GPU_WithLogo / QR_Render_GPU_CPUMatch
SpecializedIntrinsicsTest
BVHRayTraversalTest
WasmMinimalPairsSortDiag
RadixSortDescending4MTest (quiet machine)
ML_TensorView_Half_RoundTrip_CrossAssembly
TensorViewStructParam_Half_Get2DSet2D_RoundTrip
```

---

## SpawnDev.ILGPU.ML — changes and status

### Library (`SpawnDev.ILGPU.ML` v `4.0.0-preview.4`)

| Item | Detail |
|------|--------|
| `Tensors/TensorView.cs` | `[MethodImpl(AggressiveInlining)]` on `Get1D`–`Set4D` — encourages ILGPU IR inline of accessors when kernels call them cross-assembly |
| `SpawnDev.ILGPU.ML.csproj` | Package ref **`SpawnDev.ILGPU` `4.9.10-local.12`** — update to `local.15` before ML demo validation against hoisting/BVH fixes |
| CHANGELOG | Last entry `preview.4` (2026-05-23); no ML package version bump this session |

### Demo / test harness

| Item | Detail |
|------|--------|
| `MLTestBase.TensorTests.cs` | `TensorView_Half_RoundTrip` — 4×8 `Half` tensor, kernel uses `Data[...]` indexing + `(Half)1.5f` |
| `PlaywrightMultiTest/ProjectRunner.cs` | Recreates test page after WebGPU row when next row is WebGL — avoids GPU state bleed (`TensorView_Half` note in source) |
| `ShaderDebugService` | Dumps WGSL/GLSL/Wasm + `latest.json` under `_mldump\` |
| `TestResultsWriter` | Writes PMT results to `_mldump\` |

### ML verification (`_mldump/latest.json`, 2026-05-29)

| Test | WebGPU | Wasm | WebGL |
|------|--------|------|-------|
| `TensorView_Half_RoundTrip` | Success | Success | Success |

ILGPU-side cross-assembly tests (`ML_TensorView_Half_RoundTrip_CrossAssembly` in Demo.Shared) also pass on all three browser backends in full sweeps.

### ML follow-ups for Tuvok

1. Bump `SpawnDev.ILGPU` package ref in `.csproj` to **`4.9.10-local.15`** (or whatever is on feed after publish) and run ML PMT / depth demo.
2. Phase-2 kernels still on legacy `ArrayView` signatures in places — `ImagePostprocessKernel` TensorView overloads exist; broader migration is preview.5+ scope.
3. If `TensorView_Half_RoundTrip` fails only in **full ML sweep after WebGPU**, check `RunLaneSequentialAsync` page recreation — isolation passes are documented in `ProjectRunner.cs`.

---

## Open issues (owned lanes)

| Issue | Owner hint | Status |
|-------|------------|--------|
| Wasm residual large-sort race under CPU contention | Tuvok diary `residual-sort-race-2026-05-25.md` | Environmental under dual PMT; ~1 fail/sweep. Quiet-machine single-test usually passes. |
| CHANGELOG entries for local.13–15 | Whoever commits | Not written yet |
| ML csproj pin vs ILGPU head | Data/Trip | ML still on local.12 |
| Git commit/push | Captain | Explicitly deferred this session |

---

## Key file index (quick navigation)

### ILGPU — Wasm

- `SpawnDev.ILGPU/Wasm/WasmAccelerator.cs` — dispatch, `IrUserParamIndexOffset`, `MAX_YIELD_ITERS`, TensorView struct marshal comments
- `SpawnDev.ILGPU/Wasm/Backend/WasmKernelFunctionGenerator.cs` — `startIdx`, cross-assembly inline, `IsTensorViewLikeBodyStruct`, Broadcast tag slots
- `SpawnDev.ILGPU/Wasm/Backend/WasmCompiledKernel.cs` — `IrUserParamIndexOffset` property
- `SpawnDev.ILGPU/Wasm/Notes/residual-sort-race-2026-05-25.md` — residual race investigation

### ILGPU — WebGL

- `SpawnDev.ILGPU/WebGL/Backend/GLSLCodeGenerator.cs` — `TryEmitDeclaration`, `GetStructDefaultInitializer`, Float16 constants
- `SpawnDev.ILGPU/WebGL/Backend/GLSLKernelFunctionGenerator.cs` — hoisting, MethodCall cross-assembly inline, `IsBodyStruct` / TensorView, Load/NewView/Alloca
- `SpawnDev.ILGPU/WebGL/Backend/GLSLFunctionGenerator.cs` — emulation forward decls, struct return fallback

### ILGPU — tests

- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.Tests24.TensorViewStructParam.cs`
- `SpawnDev.ILGPU.Demo.Shared/UnitTests/BackendTestBase.Tests4.cs` — `BVHRayTraversalKernel_1539`

### ML

- `SpawnDev.ILGPU.ML/SpawnDev.ILGPU.ML/Tensors/TensorView.cs`
- `SpawnDev.ILGPU.ML/SpawnDev.ILGPU.ML.Demo.Shared/UnitTests/MLTestBase.TensorTests.cs`
- `SpawnDev.ILGPU.ML/_mldump/` — shader + PMT artifacts

### Publish

- `D:\users\tj\Projects\SpawnDev.ILGPU\_publish-nuget.local.release.bat`
- `D:\users\tj\Projects\SpawnDev.ILGPU.ML\_publish-nuget.local.release.bat` (if publishing ML package)

---

## Suggested first actions for Tuvok

1. Read `Wasm/Notes/residual-sort-race-2026-05-25.md` Session 8+ and the `12-14` dump failures — decide if residual needs codegen hardening beyond yield scaling or remains monitor-only.
2. Publish `4.9.10-local.15` via `_publish-nuget.local.release.bat`; confirm `D:\users\SpawnDevPackages\spawndev.ilgpu\4.9.10-local.15\`.
3. Bump ML csproj to `local.15`, run `TensorView_Half_RoundTrip` + depth/TensorView demo paths on Wasm/WebGL.
4. Append CHANGELOG sections for `local.13`, `local.14`, `local.15` before any nuget.org/rc push.
5. If investigating WebGL only: grep `TryEmitDeclaration` / `EmitHoistedDeclarations` / `GetStructDefaultInitializer` — all recent WebGL stability work routes through those helpers.

---

*End of handoff.*
