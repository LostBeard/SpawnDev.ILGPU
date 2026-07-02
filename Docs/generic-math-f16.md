# Generic-Math f16 Kernels

*(SpawnDev.ILGPU 4.9.12+ — forks 2.0.10+)*

Write **one** generic kernel over the numeric type instead of a dedicated kernel per type. The same
kernel source compiles for `float` weights and `Half` (f16) weights, and on every backend
(WebGPU, WebGL, Wasm, Cuda, OpenCL, CPU) it monomorphizes to the **exact same shader/machine code** a
hand-written per-type kernel would produce — so generic costs you nothing at runtime, only less source
to maintain.

Two pieces make this work:

1. **`ILGPU.Half` implements `INumber<Half>`** — the C# 11 generic-math operators transpile over Half.
2. **`NumericConvert.ToFloat32<T>` / `ToFloat64<T>`** — transpilable generic converts, so a kernel body
   can widen a generic numeric value to `float`/`double` (e.g. to accumulate f16 weights in fp32).

> Use **`ILGPU.Half`**, not `System.Half`, in kernel signatures (`ArrayView<Half>` etc.). Implicit
> conversions between the two exist for host interop.

---

## 1. `INumber<Half>` — generic-math operators over Half

`ILGPU.Half` implements `INumber<Half>` and `ISignedNumber<Half>`. A kernel constrained to
`where T : INumber<T>` can use the operators, identities, and methods of generic math, and they lower to
Half's transpilable FP32 intrinsics on all six backends:

```csharp
using ILGPU;
using ILGPU.Runtime;
using System.Numerics;

// abs(w*w - 1) + clamp(w, 0, 1), generic over T (works for float, double, Half, int, ...)
static T Activation<T>(T w) where T : INumber<T>
    => T.Abs(w * w - T.One) + T.Clamp(w, T.Zero, T.One);

static void ActivationKernel<T>(Index1D i, ArrayView<T> a, ArrayView<T> o)
    where T : unmanaged, INumber<T>
    => o[i] = Activation(a[i]);

// Launch over Half (f16) weights — same source also instantiates for float, double, etc.
var k = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<Half>, ArrayView<Half>>(ActivationKernel<Half>);
k((Index1D)n, aBuf.View, oBuf.View);
```

Operators (`+ - * / %`, comparisons, unary `-`/`+`, `++`/`--`), identities (`T.One`, `T.Zero`), and the
numeric methods (`T.Abs`, `T.Clamp`, `T.Min`, `T.Max`, `T.CopySign`, `T.Sign`, the `T.Is*` predicates)
all transpile.

### Why not `System.Half`?

Before 4.9.12, a generic-math kernel over Half had to use `System.Half` (which implements `INumber`),
and `System.Half`'s `INumber` members lower to a `BitCast` the frontend rejects — the kernel failed to
compile on **every** backend. `ILGPU.Half` now implements `INumber<Half>` directly, binding the generic
math to Half's FP32 intrinsics instead, so it transpiles.

---

## 2. `NumericConvert.ToFloat32` — widen a generic value to float

The catch with a generic numeric kernel is **converting** between types in the body — e.g. widening an
f16 weight to `float` so you can accumulate in fp32 (ORT-parity; avoids f16-accumulation precision
loss). The standard C# generic-math convert does **not** work in a kernel:

```csharp
float w32 = float.CreateTruncating(w);   // ❌ NotSupportedException on ALL backends
```

`float.CreateTruncating<T>` (and `CreateChecked`, `CreateSaturating`) inspect `typeof(T)` internally to
pick a conversion — and the frontend can't lower `System.Type`. It fails even for `T = float`.

Use `NumericConvert` instead — a transpilable generic convert that monomorphizes to the concrete
per-type GPU cast for the instantiated `T` (`(float)Half`, identity for `float`, `(float)int`, ...):

```csharp
float w32 = NumericConvert.ToFloat32(w);   // ✅ transpiles on all backends
double w64 = NumericConvert.ToFloat64(w);  // ✅
```

---

## Worked example: one generic kernel, float **and** Half weights

A linear layer `y = x * w + bias`, generic over the weight type `TW`, accumulating in fp32:

```csharp
using ILGPU;
using ILGPU.Runtime;
using System.Numerics;

// TW = float OR Half. Widen the weight to float for fp32 math, regardless of storage type.
static void Linear<TW>(Index1D i, ArrayView<float> x, ArrayView<TW> w, ArrayView<float> y)
    where TW : unmanaged, INumber<TW>
    => y[i] = x[i] * NumericConvert.ToFloat32(w[i]) + 1.0f;

// f16 weights (half the bandwidth / memory)
var kHalf = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<float>, ArrayView<Half>, ArrayView<float>>(Linear<Half>);
kHalf((Index1D)n, xBuf.View, wHalfBuf.View, yBuf.View);

// fp32 weights — the SAME kernel source; ToFloat32 is identity here
var kFloat = accelerator.LoadAutoGroupedStreamKernel<
    Index1D, ArrayView<float>, ArrayView<float>, ArrayView<float>>(Linear<float>);
kFloat((Index1D)n, xBuf.View, wFloatBuf.View, yBuf.View);
```

This is the pattern for a generic **MatMul** / **Conv** over a generic weight type: store weights as
`ArrayView<TW>` (f16 to halve memory + bandwidth), `NumericConvert.ToFloat32` each weight as you read
it, accumulate the dot product in a `float`, write the `float` result. One source covers both
precisions; ILGPU compiles `Linear<Half>` to identical code to a hand-written half kernel.

---

## Notes

- **Zero runtime cost vs dedicated kernels.** A generic `K<Half>` monomorphizes to the same shader /
  machine code as a hand-written half kernel. Generic buys maintainability, not overhead.
- **fp32 accumulation.** Convert f16 weights to `float` (or `double` via `ToFloat64`) and accumulate in
  the wider type to match CPU/ORT reference precision — f16 storage, fp32 math.
- **Backends.** Everything here is verified on all six backends. On WebGPU/WebGL/Wasm, Half is stored
  packed and f16 arithmetic is emulated in fp32 (lossless); `ToFloat32` is the same widen either way.
  See [Data Type Support](data-type-support.md) for the per-type matrix.
- **Algorithm kernels.** The **host-level** `accelerator.CreateRadixSort`/`CreateRadixSortPairs`/`CreateScan`/`CreateReduce`
  DO run on WebGL (shared-memory-free multi-dispatch - the draw-call boundary acts as the barrier). Only the
  **in-kernel** group/warp ops (`Group.Scan`/`Group.Reduce`/`Warp.*`) require shared memory + barriers and so
  throw on WebGL. Either way that is unrelated to generic math (a plain elementwise generic kernel runs on WebGL fine).

## See also

- [Writing Kernels](kernels.md) — kernel fundamentals
- [Data Type Support](data-type-support.md) — per-type, per-backend matrix (including `Half`)
- [Capabilities & Backend Selection](capabilities-and-backend-selection.md)
