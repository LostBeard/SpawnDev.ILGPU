# Capabilities and Backend Selection

SpawnDev.ILGPU runs the same kernel C# across six backends — three browser (WebGPU, WebGL, Wasm) and three desktop (CUDA, OpenCL, CPU) — but the backends do not have identical capability sets. WebGL has no atomics, no shared memory, no barriers. WebGPU and WebGL emulate Float64 and Int64. Subgroups are device-dependent on WebGPU and OpenCL.

If your kernel uses a feature the chosen backend can't implement bit-exactly, the result is one of:

- **Silent wrong output** (legacy behavior, deprecated): the kernel compiles, runs, returns garbage. The most expensive failure mode — costs hours of debugging before you suspect the backend.
- **`UnsupportedKernelFeatureException`** (current default for hard mismatches): a typed exception at kernel-compile time naming the unsupported feature and the backend.
- **Selection-time filter via `AcceleratorRequirements`** (the API in this doc): declare what your kernel needs upfront, and the runtime picks a backend that satisfies it or throws if none can.

This doc covers the third option — declarative capability gating — plus the runtime exception that backs it up.

## Quick Start

```csharp
using SpawnDev.ILGPU;
using ILGPU.Runtime;

// Declare the capabilities your kernel actually needs:
var requirements = new AcceleratorRequirements
{
    RequiresAtomics = true,        // sub-word writes / Atomic.* — rules out WebGL
    RequiresFloat64Native = true,  // double precision must be hardware — rules out WebGPU + WebGL
};

// Pick the best compatible backend, or throw if none fit:
using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());
using var accelerator = await context.CreatePreferredAcceleratorAsync(requirements);

// Desktop-only code can use the synchronous form:
// using var accelerator = context.CreatePreferredAccelerator(requirements);

// Use accelerator as normal — it's guaranteed to be a backend that satisfies all flags.
```

If no backend on this host satisfies the requirements, `CreatePreferredAcceleratorAsync` / `CreatePreferredAccelerator` throw `NotSupportedException` with a message naming which requirements aren't met.

## When to use this

Use it when **your kernel relies on features that aren't universal**:

- Sub-word writes (`ArrayView<byte>`, `ArrayView<short>`, `ArrayView<Half>`, `ArrayView<BFloat16>`) — the codegen synthesizes atomic RMW for thread-safe partial-word writes. Set `RequiresAtomics = true`.
- `Group.Barrier` / `SharedMemory.Allocate` / `SharedMemory.GetDynamic` — set `RequiresBarriers = true` / `RequiresSharedMemory = true`.
- `Warp.Shuffle` / `Group.Broadcast` — set `RequiresSubGroups = true`.
- Native `double` / `long` performance (vs. emulation) — set `RequiresFloat64Native = true` / `RequiresInt64Native = true`. The emulated paths are correct but slower.
- 64-bit atomics (`Atomic.Add(ref long, long)` etc.) — set `RequiresInt64Atomics = true`.
- In-kernel scatter / more than one output element per thread — a kernel that writes a computed/arbitrary output index (`out[someIndex] = ...`), or writes several elements of one buffer per thread at offsets that aren't the consecutive `v*K+slot` layout. Set `RequiresScatterStores = true`. WebGL Transform-Feedback captures one output record per vertex at the thread's own slot (gather-only), so these can't run in-kernel there. (WebGL still scatters at the host/algorithm layer — e.g. RadixSort via render-to-texture — so this is specifically about scatter in *your own* kernel body.)

Don't use it when your kernel is portable across all backends. `AcceleratorRequirements.None` (the default) accepts every backend.

## All Capability Flags

Every flag below is `bool`, defaults to `false` (no requirement), and can be set via the object initializer. Each flag is documented with which backends it rules OUT.

| Flag | Rules out | When to set true |
|------|-----------|------------------|
| `RequiresAtomics` | WebGL | `Atomic.*` operations OR sub-word view writes (`ArrayView<byte>` etc.) |
| `RequiresSharedMemory` | WebGL | `SharedMemory.Allocate` / `SharedMemory.GetDynamic` |
| `RequiresBarriers` | WebGL | `Group.Barrier` |
| `RequiresFloat16` | (none — every backend supports it) | Documentation that you use `Half`. Filter is a no-op. |
| `RequiresFloat16Native` | WebGL, Wasm, WebGPU adapters without `shader-f16`, OpenCL without `cl_khr_fp16` | Performance-critical Half kernels where emulation overhead matters |
| `RequiresBFloat16` | (none - every backend supports it, always emulated) | Documentation that you use `BFloat16`. Filter is a no-op. |
| `RequiresFloat8E4M3` / `RequiresFloat8E5M2` | (none - every backend supports them, always emulated) | Documentation that you use FP8. Filter is a no-op. |
| `RequiresFloat4E2M1` / `RequiresQInt4` / `RequiresQUInt4` | (none - every backend can LOAD them) | Documentation that you use the packed 4-bit types. Filter is a no-op for the type itself. |
| `RequiresPacked4Store` | WebGL, CPU | Your kernel STORES into a packed 4-bit view (`Float4E2M1` / `QInt4` / `QUInt4`) or radix-sorts one. A nibble store is an atomic RMW of its 32-bit word: WebGL has no atomics, and the CPU backend's managed indexer cannot address a sub-byte element. Both fail loud at compile time; this flag filters them at selection instead. |
| `RequiresFloat64` | (none — every backend supports it) | Documentation that you use `double`. Filter is a no-op. |
| `RequiresFloat64Native` | WebGPU, WebGL (emulate via Dekker) | Numerics where exact IEEE 754 matters or Dekker overhead is unacceptable |
| `RequiresFloat64Strict` | (none — every backend can satisfy it) | Strict IEEE 754 f64 semantics. Native-f64 backends always satisfy; WebGPU/WebGL are auto-configured to Ozaki by `CreatePreferredAcceleratorAsync`. See "Strict IEEE 754 f64" section below. |
| `RequiresInt64` | (none — every backend supports it) | Documentation. WebGPU/WebGL emulate via vec2&lt;u32&gt;. |
| `RequiresInt64Native` | WebGPU, WebGL | Performance-critical i64/u64 kernels |
| `RequiresInt64Atomics` | WebGL; OpenCL without `cl_khr_int64_base_atomics` | `Atomic.Add(ref long, ...)` and friends |
| `RequiresSubGroups` | WebGL; WebGPU adapters without the `subgroups` feature; OpenCL without the subgroup extensions. Wasm (emulated warps) and CPU (simulated warps) pass | `Warp.Shuffle`, `Group.Broadcast`, etc. |
| `RequiresScatterStores` | WebGL | In-kernel scatter (`out[computedIndex] = ...`) or >1 output element per thread that isn't the positional `v*K+slot` layout. WebGL TF is gather-only (one record per vertex at its own slot). |

> **Warps and subgroups are the same thing:** "warp" is CUDA's name and "subgroup" is WebGPU / Vulkan / OpenCL's (AMD says "wavefront") - a small set of threads that exchange values directly. ILGPU's `Warp.*` API maps to WebGPU subgroups. `RequiresSubGroups` is a correctness gate: Wasm has no hardware warps and emulates them (`WarpSize` 8, shared-memory exchange), and the CPU accelerator simulates them; both give the same results, without hardware speed.
>
> **WebGPU features are read from the adapter** when devices are listed (`WebGPUILGPUDevice.AdapterSupportsShaderF16` / `AdapterSupportsSubgroups`); the accelerator requests both whenever the adapter has them, so selection matches what the accelerator gets (on master, after 5.3.6; before, WebGPU always failed `RequiresFloat16Native` and always passed `RequiresSubGroups`). `WebGPUBackend.ForceEmulatedF16` turns native f16 off for selection too. `WebGPUBackendOptions.ForceDisableSubgroups` is a per-accelerator option that selection cannot see.

## API Surface

Three extension methods on `Context` / `Device` carry the requirements through ILGPU's existing accelerator-selection paths, plus the device ranking they use.

### `CreatePreferredAcceleratorAsync(requirements)` / `CreatePreferredAccelerator(requirements)`

```csharp
using var accelerator = await context.CreatePreferredAcceleratorAsync(requirements);   // browser + desktop
using var accelerator = context.CreatePreferredAccelerator(requirements);              // desktop
```

Filters the context's devices by the requirements, then picks the best one: **WebGPU > WebGL > Wasm** in the browser, and on desktop the [device ranking](#device-ranking-devicepreference) below (CUDA > discrete OpenCL GPU > integrated OpenCL GPU > other OpenCL > CPU). Throws `NotSupportedException` if no device on the host satisfies all flags; the message names the unmet requirements and the available devices, so error reports come with diagnostic context built in.

Use the **async** form in the browser: the WebGPU and Wasm accelerators can only be created asynchronously, so the synchronous form throws `NotSupportedException` when one of them is the pick.

### Device ranking: `DevicePreference`

```csharp
using SpawnDev.ILGPU;

Device best = context.GetBestDevice();                                  // best device on the context
IReadOnlyList<Device> ranked = context.Devices.OrderByPreference();     // all devices, best first
using var accelerator = best.CreateAccelerator(context);                // desktop
```

Ranks devices by **kind**, best first: CUDA, then discrete OpenCL GPUs, then integrated OpenCL GPUs (detected with `CL_DEVICE_HOST_UNIFIED_MEMORY`, Intel vendor as the fallback), then other OpenCL devices (CPU runtimes, accelerators), then WebGPU, WebGL, Wasm, then anything else, then CPU. Memory size only breaks ties between devices of the same rank, then the original order.

Use it instead of ILGPU's `Context.GetPreferredDevice(preferCPU: false)`, which sorts by `MemorySize` alone. Integrated GPUs and OpenCL CPU runtimes report system RAM as device memory, so on an Intel HD 620 + NVIDIA 940MX laptop `GetPreferredDevice(false)` picked Intel's OpenCL **CPU** runtime (8 GB) over the 940MX (2 GB), and on an RTX 4070 PC it picked the OpenCL view of the card over CUDA. `GetBestDevice()` picks CUDA in both cases. (The fork leaves `GetPreferredDevice` unchanged to stay close to upstream ILGPU.)

In the browser, `WebGPUDevice.GetDevicesAsync()` lists a distinct high-performance adapter first. Chrome on Windows currently returns the same adapter for every `powerPreference` ([crbug.com/369219127](https://crbug.com/369219127)); users can switch the browser's GPU in Windows Graphics settings or with Chrome's `--force_high_performance_gpu` flag.

### `EnumerateCompatibleDevices(requirements)`

```csharp
var compatible = context.EnumerateCompatibleDevices(requirements);
foreach (var device in compatible)
{
    Console.WriteLine($"OK: {device.AcceleratorType} on {device.Name}");
}
```

Returns `IReadOnlyList<Device>` of every compatible device in context-native order. Never throws - useful when you want to score / rank candidates yourself (`.OrderByPreference()` gives the library's ranking) or display options to the user.

### `device.Satisfies(requirements)`

```csharp
foreach (var device in context.Devices)
{
    bool ok = device.Satisfies(requirements);
    Console.WriteLine($"{device.AcceleratorType}: {(ok ? "compatible" : "filtered out")}");
}
```

Per-device check. Constant-time per flag.

## UnsupportedKernelFeatureException — the runtime safety net

`AcceleratorRequirements` filters at SELECTION time. But sometimes a consumer pins directly to a specific backend (`context.GetCPUDevice()`, etc.) without using the selection helpers, OR the IR emits a feature the static capability check didn't catch.

For those cases, the kernel-compile step throws `UnsupportedKernelFeatureException` (a `NotSupportedException` subclass) when it tries to lower a feature the backend can't represent. Catch it in code that should fall back to a different backend or a CPU path:

```csharp
try
{
    var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<byte>>(
        (i, buf) => buf[i] = (byte)(i * 3));
}
catch (UnsupportedKernelFeatureException ex)
{
    Console.WriteLine($"Backend {ex.Backend} can't run this kernel: {ex.Feature}");
    Console.WriteLine($"Suggestion: {ex.Remediation}");
    // Fall back: try a different accelerator or skip this code path
}
```

Properties:
- `Feature` — what was unsupported (e.g. `"Generic atomic on byte sub-word view"`).
- `Backend` — the `AcceleratorType` that rejected it.
- `Remediation` — text suggestion (which backend to try, or how to restructure the kernel).

The exception replaces the **deprecated** silent-wrong-output behavior where an unsupported intrinsic produced a "working" kernel that returned garbage. If you see a runtime crash that would have been silent garbage, that's the upgrade.

## Common Patterns

### Capability-gated demo page

```csharp
// In a Razor page constructor or component:
public Demo(Context context)
{
    var atomicsRequired = new AcceleratorRequirements { RequiresAtomics = true };
    if (!context.EnumerateCompatibleDevices(atomicsRequired).Any())
    {
        Status = "This demo requires a backend with atomics support. Open in Chrome (WebGPU) or use the desktop demo.";
        return;
    }
    Accelerator = context.CreatePreferredAccelerator(atomicsRequired);
}
```

### Choosing the best of multiple options

```csharp
// Want WebGPU if available, else CUDA, else CPU - all must support atomics:
var atomics = new AcceleratorRequirements { RequiresAtomics = true };
var compatible = context.EnumerateCompatibleDevices(atomics);

var device =
    compatible.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.WebGPU)
    ?? compatible.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.Cuda)
    ?? compatible.FirstOrDefault(d => d.AcceleratorType == AcceleratorType.CPU)
    ?? throw new NotSupportedException("No atomics-capable backend on this host");

using var accelerator = device.CreateAccelerator(context);
```

### Defensive runtime check + AcceleratorRequirements together

```csharp
var reqs = new AcceleratorRequirements { RequiresFloat64Native = true };
using var accelerator = context.CreatePreferredAccelerator(reqs);

try
{
    var kernel = accelerator.LoadAutoGroupedStreamKernel<Index1D, ArrayView<double>>(MyKernel);
}
catch (UnsupportedKernelFeatureException ex)
{
    // Belt-and-suspenders: if the kernel uses a feature the requirements didn't anticipate,
    // we still get a typed exception instead of garbage output.
    Logger.LogError(ex, "Kernel features extend beyond declared requirements");
    throw;
}
```

## Strict IEEE 754 f64 — `RequiresFloat64Strict`

`RequiresFloat64Native` and `RequiresFloat64Strict` look similar but are different shapes. The first is a hardware-only filter; the second is a correctness contract that every backend can satisfy through some path.

| Flag | What it filters | What it configures |
|---|---|---|
| `RequiresFloat64Native` | Rules out WebGPU + WebGL because they don't have hardware f64. Routes to CPU / CUDA / OpenCL / Wasm only. | Nothing — picks a backend that already has the feature. |
| `RequiresFloat64Strict` | Rules out nothing. Every backend can produce IEEE 754 f64 — natively on CPU/CUDA/OpenCL/Wasm, via Ozaki emulation on WebGPU/WebGL. | Sets `F64Emulation = Ozaki` on browser-backend accelerators when `CreatePreferredAcceleratorAsync` picks one. |

Use `Float64Strict` when you care about the **mathematical semantics** (full 53-bit IEEE 754) and the runtime should pick the right path; use `Float64Native` when you specifically want **hardware f64** for performance and are willing to skip WebGPU and WebGL (Wasm has native f64 and stays eligible).

### Async-only entry point

The configuration step happens at accelerator-create time, so the strict-f64 path is only available via the async overload:

```csharp
using SpawnDev.ILGPU;
using ILGPU.Runtime;

using var context = await Context.CreateAsync(builder => builder.AllAcceleratorsAsync());

// On browser: WebGPU is auto-configured for Ozaki (vec4<f32> emulation).
// On desktop: routes to CPU/CUDA/OpenCL/Wasm (always strict).
using var accelerator = await context.CreatePreferredAcceleratorAsync(
    new AcceleratorRequirements { RequiresFloat64Strict = true });
```

The synchronous `CreatePreferredAccelerator(requirements)` keeps working for desktop hosts but does not configure browser backends - use the async overload for cross-platform code.

### Runtime mode flips

If you already have a `WebGPUAccelerator` or `WebGLAccelerator` and want to switch precision modes without recreating the device, set the `F64Mode` property directly:

```csharp
using SpawnDev.ILGPU;
using SpawnDev.ILGPU.WebGPU;

WebGPUAccelerator accelerator = ...;

// Promote to strict for the next compile:
accelerator.F64Mode = F64EmulationMode.Ozaki;

// Drop back to fast Dekker mode for everyday work:
accelerator.F64Mode = F64EmulationMode.Dekker;
```

The flip affects subsequent kernel compiles. Cached compiled kernels retain the mode they were compiled under - call `ClearShaderCache()` (WebGPU) if you need everything to recompile under the new mode.

### P2P swarms

When dispatching across a P2P swarm, the coordinator carries the requested mode in `KernelDispatchRequest.F64Mode`; the receiving peer's worker auto-configures its WebGPU/WebGL accelerator to that mode before compiling the kernel. Native-f64 peers (CPU/CUDA/OpenCL/Wasm) ignore the field — they're always strict regardless. Coordinators can pre-flight filter peers by calling `PeerCapabilities.SupportsStrictFloat64()` before dispatching. See [`p2p-compute.md`](p2p-compute.md) for the full P2P API.

## See also

- [`backends.md`](backends.md) — full per-backend capability table
- [`atomic-operations.md`](atomic-operations.md) — what each atomic op does on each backend
- [`limitations.md`](limitations.md) — language and IR-level constraints (separate from backend capability gaps)
- [`data-type-support.md`](data-type-support.md) — per-type, per-backend verification matrix
