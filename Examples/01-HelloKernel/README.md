# 01 - Hello Kernel (Console)

The smallest useful GPU program: add two arrays in parallel. One C# static method (`AddKernel`)
becomes a kernel that runs on every element at once.

```bash
cd Examples/01-HelloKernel
dotnet run
```

Expected output (the accelerator name depends on your machine - CUDA / OpenCL / CPU):

```
Running on: <your device>  (Cuda | OpenCL | CPU)
     0 +    0 = 0
     1 +   10 = 11
     2 +   20 = 22
  ...
Done.
```

## What to notice
- **One package.** `SpawnDev.ILGPU` brings ILGPU's native CUDA/OpenCL/CPU backends on the desktop;
  in a Blazor WASM host the same package adds WebGPU/WebGL/Wasm.
- **The async pattern is portable.** `Context.CreateAsync(...AllAcceleratorsAsync())`,
  `CreatePreferredAcceleratorAsync()`, `SynchronizeAsync()`, `CopyToHostAsync<T>()` run identically on
  desktop and browser (the browser backends *require* async; the desktop ones fall back to sync).
- **The kernel is just C#.** `AddKernel` is the exact same method you'd run on WebGPU or CUDA -
  write once, run on any backend.

Next: **[02 - BackendSelection](../02-BackendSelection)** - enumerate devices and gate on capabilities.
