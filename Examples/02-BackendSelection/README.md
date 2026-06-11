# 02 - Backend Selection (Console)

One package, six backends. This example shows how to see every device, take the default preferred
pick, and **gate on capabilities** so a kernel never lands on a backend that can't run it.

```bash
cd Examples/02-BackendSelection
dotnet run
```

## What to notice
- **`context.Devices`** lists everything discovered (Cuda/OpenCL/CPU on the desktop;
  WebGPU/WebGL/Wasm in a Blazor WASM host).
- **`CreatePreferredAcceleratorAsync()`** takes the default pick - a real GPU before the CPU.
- **`AcceleratorRequirements`** is the safe way to choose. Declare what the kernel needs
  (`RequiresFloat64Native`, `RequiresAtomics`, `RequiresSharedMemory`, `RequiresSubGroups`, ...) and:
  - `context.EnumerateCompatibleDevices(requirements)` lists the ones that qualify,
  - `context.CreatePreferredAccelerator(requirements)` picks one (or throws, naming the unmet requirements),
  - `device.Satisfies(requirements)` checks a single device.

  This puts the backend knowledge in one place - your code declares *intent*, not `if (backend == WebGL)`.

Next: **[03 - Algorithms](../03-Algorithms)** - RadixSort / Scan / Reduce, the same call on every backend.
