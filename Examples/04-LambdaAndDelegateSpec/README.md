# 04 - Lambda Kernels + DelegateSpecialization

Two ways to parameterize a kernel without writing N copies of it.

```bash
cd Examples/04-LambdaAndDelegateSpec
dotnet run
```

Output:

```
Lambda  (i * 5):           0, 5, 10, 15, 20, 25, 30, 35
MapKernel(Negate)   on 1..8:  -1, -2, -3, -4, -5, -6, -7, -8
MapKernel(DoubleIt)  (then):  -2, -4, -6, -8, -10, -12, -14, -16
Done.
```

## What to notice
- **Lambda kernels** capture scalars automatically (`multiplier` is shipped to the GPU). ArrayViews
  *can't* be captured - they're explicit kernel parameters; scalars can.
- **DelegateSpecialization** lets one kernel (`MapKernel`) apply any `Func<int,int>`. Each distinct
  target (`Negate`, `DoubleIt`) compiles its OWN kernel with the function body **inlined at compile
  time** - so there's no indirect call on the GPU, and you write the kernel once.
- The two `MapKernel` launches above are genuinely different compiled kernels, cached separately
  (this is exactly the specialization the 4.10.0 cache-key fix made correct).

Next: **[05 - BlazorBasic](../05-BlazorBasic)** - the same kernels, in the browser (WebGPU/WebGL/Wasm).
