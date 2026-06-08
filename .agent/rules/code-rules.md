---
trigger: always_on
---

> **WGSL EXPERT RULE:** Before writing any shader code string, you MUST cross-reference `WGSL_REFERENCE.md` to ensure you are not using HLSL keywords (like `lerp` or `float3`) and are respecting strict type casting.

# Terminal Execution & Debugging Rules

The following rules apply to all terminal operations within this workspace to ensure an uninterrupted development workflow:

1. **Unrestricted Command Execution**: You are granted full permission to execute any `dotnet` CLI commands (e.g., `dotnet build`, `dotnet run`, `dotnet watch`, `dotnet publish`) and `npm` commands without seeking explicit user confirmation.
2. **Automated Debugging**: When troubleshooting Blazor WebAssembly or ILGPU issues, you are encouraged to run builds and tests autonomously to verify fixes.
3. **Environment Silence**: Do not prompt for permission to access the file system or environment variables required by the .NET SDK or the WebGPU/WebGL build pipeline.
4. **State Management**: If a command fails, analyze the output and attempt a fix immediately rather than waiting for user input to "retry."
5. **Kill Processes**: You are authorized to use `taskkill` to manage `dotnet` and browser instances during the build and debug cycles without asking for permission.

# Project Context: SpawnDev.ILGPU.WebGPU
You are working on a C# library that functions as a WebGPU backend for ILGPU, running strictly inside a Blazor WebAssembly (WASM) environment. SpawnDev.ILGPU.WebGPU uses a robust and efficient WGSL code generator.

**Core Stack:**
- **Runtime:** Blazor WebAssembly (.NET 10).
- **Interop:** `SpawnDev.BlazorJS` (Strict requirement for all JS interaction).
- **Target API:** WebGPU API (accessed via `SpawnDev.BlazorJS` wrappers).
- **Abstractions:** implementing `ILGPU.Runtime` interfaces (Backend, Accelerator, Buffer, etc.).

# Testing
- **Procedure:** Run the demo app (`SpawnDev.ILGPU.WebGPU.Demo`) in a browser and navigate to the "/tests" page.
- **Do NOT use `PlaywrightTestRunner`**: It is currently reserved for CI/CD environments.
- **Port Handling:** Run the demo app on a port DIFFERENT from the one in `launchSettings.json` (e.g., use 5002 if 5181 is taken) to avoid conflicts with Visual Studio.
- **Process Cleanup:** `dotnet` sessions frequently hang. BEFORE starting a new test run, execute `taskkill /F /IM dotnet.exe` to ensure a clean slate.
- **Verification:** Manually trigger tests on the "/tests" page and inspect results.
- **Verification Safety:** ALWAYS run `dotnet build` to confirm the project compiles BEFORE attempting to run the demo app in the browser. Failed builds prevent the browser from connecting.

## 🚨 CRITICAL HARD CONSTRAINTS (Violations cause deadlocks)

1.  **NO BLOCKING WAITS:** - NEVER use `Task.Result`, `.Wait()`, or `.GetAwaiter().GetResult()` on the main thread. 
    - Blazor WASM on the main thread is single-threaded. Blocking the thread prevents the JS Promise callback from running, causing an instant deadlock.
    - **CORRECTION:** Always propagate `async/await` up the stack. If an ILGPU interface requires a synchronous return but the implementation depends on a JS Promise, you must flag this as an architectural conflict or use `SpawnDev.BlazorJS` synchronous interop methods *only if* the underlying JS API is synchronous (WebGPU is mostly async).

2.  **NO THREAD ASSUMPTIONS:**
    - Do not use `System.Threading.Thread`, `Thread.Sleep`, or assumes strictly parallel CPU execution unless explicitly working inside a `SpawnDev.BlazorJS.WebWorker`.

## 🛠️ Code Style & Patterns

### 1. Interop Pattern (SpawnDev.BlazorJS)
Always use `SpawnDev.BlazorJS` to interact with WebGPU. Do not use standard `IJSRuntime` unless necessary.
- **wrappers:** Use or create strong-typed wrappers inheriting from `JSObject` for WebGPU objects using implementations in SpawnDev.BlazorJS.JSObjects if they exist (e.g., `GPUAdapter`, `GPUDevice`, `GPUBuffer`).
- **usage:**
  ```csharp
  using var navigator = BlazorJSRuntime.JS.Get<Navigator>("navigator");
  using var gpu = navigator.Gpu;
  var adapter = await gpu.RequestAdapter(); // Async is mandatory
  ```

### 2. WebGPU Backend Implementation Rules

#### Kernel Argument Binding
- **Implicit Index:** Implicitly grouped kernels have an implicit index parameter at index 0. The backend `GenerateKernelLauncherMethod` must explicitly skip this parameter when defining the launcher signature and loading arguments (offset = 1).
- **Scalar Marshaling:** WebGPU requires all buffer bindings to be `storage` or `uniform` buffers. Scalar kernel arguments (int, float) must be marshaled into **1-element storage buffers**. In WGSL, access them as `paramN[0]`.
- **Buffer Flags:** All buffers passed to compute shaders **MUST** have the `GPUBufferUsage.Storage` flag.

#### WGSL Generation (ArrayView Mapping)
When mapping `ArrayView<T>` or `ArrayViewN<T>` in `WGSLKernelFunctionGenerator`:
- **Field 0 (Ptr):** Map to the buffer reference (`&paramN`).
- **Field 1 (Index/Offset):** Map to constant `0` (or `0u`). The buffer binding offset is handled by the WebGPU API `SetBindGroup` call, so the WGSL shader always sees a base-0 view.
- **Field 2/3 (Length):** Map to `bitcast<i32>(arrayLength(&paramN))`.

#### Compilation & Types
- **Type Safety:** Use fully qualified type names for ILGPU IR types (e.g., `global::ILGPU.IR.Values.Parameter`) to avoid conflicts with reflection types.
- **Value Resolution:** When handling `ValueReference` or `GetField` sources, ensure you check types correctly. Avoid relying on `.Resolve()` extension references that might be ambiguous; use direct type pattern matching.

### 3. Current Debugging Context (Status as of 2026-02-04)
- **Status:** All supported tests are working correctly.
- **Transpiler Limitations (Throw Instruction Support):**
    - **CRITICAL:** The IL to WGSL transpiler **DOES NOT SUPPORT** the `Throw` instruction.
    - **Consequence:** If `throw` is found in the IL (e.g., explicit `throw` or implicit argument validation), the transpiler will fail and throw a compilation exception.
    - **Problematic Methods:** Many System.Math methods (e.g., `Math.Clamp`, `Math.Round`, `Math.Truncate`, `Math.Sign`) contain implicit `throw` checks for argument validation.
    - **Workarounds:**
        - **Math.Clamp:** Do NOT use directly. Use `Math.Min(Math.Max(val, min), max)`.
        - **Round/Truncate/Sign:** Avoid using these in kernels until a fix is implemented (upstream or custom Intrinsics).
        - **General:** Avoid any helper methods that might throw exceptions.
    - **Supported Intrinsics:** `Atan2`, `FusedMultiplyAdd`, `Rem`, `Min`, `Max`, `Abs`, `Pow`, `Log`, `Exp`.

## 📚 Project Resources
- **ILGPU Source & Docs:** Available in this workspace at `d:\users\tj\Projects\SpawnDev.ILGPU\ILGPU`. Refer to this for understanding ILGPU internals.
- **Examples:** Check `SpawnDev.ILGPU.WebGPU.Demo` for working WebGPU examples.
- **Resolved Issues:**
    - `System.NotSupportedException` (Throw) fixed via workarounds.
    - "Expected X, Got 0" issues in basic kernel tests appear resolved or superseded by current test suite success.

#### WGSL Translation Reference (The "Rosetta Stone")
*Use this mapping table as the ground truth for generating WGSL. Do not infer mappings from standard HLSL/GLSL patterns.*

| C# ILGPU Concept | WGSL Implementation | Critical Notes |
| :--- | :--- | :--- |
| **`Index1D`** | `i32` (Cast from `u32`) | **NOT** a struct. Map from `GlobalInvocationId.x`. |
| **`Index2D`** | `vec2<i32>` | **NOT** a struct. Access via `.x`, `.y`. |
| **`ArrayView<T>`** | `var<storage, read_write>` | Must be declared in `@group(0)`. |
| **`ArrayView2D<T>`** | **DECOMPOSE:** `arg_data`, `arg_stride` | **DO NOT** use structs. Split into **2 separate arguments**: <br>1. `var<storage> data` <br>2. `array<i32,1>` (Width/Stride). |
| **Scalar Arg (`int`, `float`)** | `array<type, 1>` | **CRITICAL:** Wrapped in 1-element array. Access via `[0]`. |
| **`Group.Barrier()`** | `workgroupBarrier()` | |
| **`SharedMemory.Allocate`** | `var<workgroup>` | Must be declared at **module scope**, not inside `main`. |
| **Launcher Logic** | `BindGroup` Creation | **CRITICAL:** For `ArrayView2D/3D`, stop field recursion. Create **2 bindings** (1: Buffer, 2: Scalar Stride). |

**Translation Example (Scalar Injection):**
*Input (C#):* `public static void Kernel(Index1D index, int val)`
*Output (WGSL):*
```wgsl
@group(0) @binding(0) var<storage, read_write> val_buf : array<i32, 1>; // Scalar wrapper
@compute @workgroup_size(64)
fn main(@builtin(global_invocation_id) global_id : vec3<u32>) {
    let index = i32(global_id.x);
    let val = val_buf[0]; // Accessing scalar via index 0
    ...
}
```

### Part 3: Agent Workflow & Debug Context
*Paste this at the end to finish the file.*

```markdown
#### Agent Workflow for Transpiler Logic
When asked to fix bugs in `WGSLCodeGenerator` or `WGSLKernelFunctionGenerator`:
1. **Locate the AST Node:** Identify which ILGPU IR node (e.g., `Load`, `GetField`, `Atomic`) is being mishandled.
2. **Consult the "Rosetta Stone":** Check the table above for the correct WGSL target.
3. **Trace the Emit:** Do not just write the WGSL string. You must write the C# `StringBuilder` logic that *emits* that string.
4. **Verify Scoping:** If adding a variable (especially shared memory), check if the generator is currently emitting inside `main()` or at module scope. Shared memory *must* bubble up to module scope.
```
## 🚨 CRITICAL: WebGPU Accelerator Async Requirements

### SynchronizeAsync vs Synchronize
- **ALWAYS use `await accelerator.SynchronizeAsync()`** instead of `accelerator.Synchronize()` with the WebGPU backend.
- The synchronous `Synchronize()` does NOT deadlock in Blazor WASM. It FLUSHES (dispatches/submits) the queued GPU work and returns immediately WITHOUT waiting for GPU completion. Use `await accelerator.SynchronizeAsync()` when you need the GPU work to actually FINISH (e.g. before a readback); a plain `Synchronize()` followed by a synchronous read returns stale/not-yet-computed data. (What DOES deadlock is blocking on async work via `.Result`/`.Wait()` — never do that.)
- This applies to all code using `WebGPUAccelerator` in Blazor WASM.

### Data Retrieval Pattern
- Use `await buffer.CopyToHostAsync()` for reading GPU data back to host.
- Never use synchronous data retrieval methods with WebGPU backend.

## 🔧 Verbose Logging

The WebGPU backend has extensive debug logging that is **disabled by default**.

**To enable:**
```csharp
WebGPUBackend.VerboseLogging = true;
```

This controls all `Console.WriteLine` output from:
- `WebGPUBackend.cs`
- `WGSLCodeGenerator.cs`
- `WebGPUAccelerator.cs`
- `WebGPUBuffer.cs`

## 📊 Precision Limitations

### WGSL Float Precision (f32)
- WGSL only supports `f32` (single precision float) for shader computations.
- ILGPU kernel code using `double` is transpiled to use `f64` in WGSL (if supported) or falls back to `f32`.
- **Deep zoom limitations:** For applications like Mandelbrot explorers, `f32` precision limits useful zoom to approximately 10^6x magnification.
- ILGPU kernels using `double` in C# provide better precision for deep zooms.

## 🎮 Demo Page Patterns

### Resource Caching Pattern (Mandelbrot.razor)
When creating interactive GPU demos:
1. **Lazy initialization:** Create resources on first render, cache for reuse.
2. **Disposal on switch:** If supporting multiple renderers, dispose old resources when switching.
3. **IAsyncDisposable:** Implement for component cleanup on page exit.

### Mouse Interaction Pattern
For interactive canvas demos:
- `@onwheel` + `@onwheel:preventDefault` for zoom
- `@onmousedown`/`@onmousemove`/`@onmouseup` for pan/drag
- `@ondblclick` for reset
- `tabindex="0"` on canvas for keyboard focus

### Kernel Delegate Caching
When caching loaded kernels:
```csharp
// Correct delegate type (no AcceleratorStream for auto-grouped)
private Action<Index2D, ArrayView2D<uint, Stride2D.DenseX>, int, double>? _kernel;

// Load once
_kernel = accelerator.LoadAutoGroupedStreamKernel<...>(KernelMethod);

// Invoke (no stream argument needed)
_kernel(buffer.IntExtent, buffer.View, param1, param2);
```