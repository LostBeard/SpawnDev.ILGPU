# 03 - Algorithms (GPU RadixSort)

You don't have to write a sort kernel. The ILGPU **Algorithms** layer ships `RadixSort`, `Scan`,
and `Reduce`, and the **same call runs on every backend** (CUDA, OpenCL, CPU, WebGPU, WebGL, Wasm).

```bash
cd Examples/03-Algorithms
dotnet run
```

Output:

```
Sorting on: <your device>  (...)
Before: 36, 71, 12, ...
After:  4, 12, 17, ...
Done.
```

## What to notice
- **`AllAcceleratorsAsync()` enables the algorithms layer** automatically - no extra setup.
- **The recipe**: `ComputeRadixSortTempStorageSize<T, TOp>(n)` for scratch -> `CreateRadixSort<T, Stride1D.Dense, TOp>()`
  -> call it with the stream + your view + the temp view. It sorts in place.
- **The sort operation is a type parameter** (`AscendingInt32`, `DescendingInt32`, `AscendingFloat`, ...) -
  swap it to change the order/key type. (WebGL even sorts `Half` keys, as of 4.9.13.)
- **`Scan` / `Reduce`** follow the same shape: `accelerator.CreateScan<...>(ScanKind.Inclusive)` and
  `accelerator.CreateReduce<...>()`.

Next: **[04 - LambdaAndDelegateSpec](../04-LambdaAndDelegateSpec)** - one kernel, many ops, inlined at compile time.
