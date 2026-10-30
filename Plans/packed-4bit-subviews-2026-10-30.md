# Packed 4-bit sub-views (QInt4 / QUInt4 / Float4E2M1) - 2026-10-30

Status: KNOWN BUG, listed in CHANGELOG 5.4.13. Next ILGPU work after the 5.4.13 release (Data).

## Symptom (measured 10-30, scratchpad probe `clsubview`, RTX 4070)

A kernel launched on `view.SubView(at, n)` of a packed 4-bit view, `at > 0`:

| backend | at = 1024 (even) | at = 1023 (odd) |
|---|---|---|
| CUDA | 1365 / 4096 wrong; writes past the view into the next buffer | same |
| OpenCL | same as CUDA | CL_OUT_OF_RESOURCES |
| Velocity | 1365 / 4096 wrong | fatal error (process dies) |

The CPU (IL) backend reads/writes through `ArrayView.LoadPackedElement`, which uses
`(Index + index) * BitsPerElement` bits and is right for any offset.

## Cause

A packed element is half a byte, but every byte address of a view is computed as `Index * ElementSize`
(`ElementSize` = 1 for these types), so the sub-view starts at byte `at` instead of `at / 2`:

- `ArrayView<T>.LoadEffectiveAddress(index)`: `Interop.ComputeEffectiveAddress(ptr, Index + index, ElementSize)`.
  Used for kernel arguments (CUDA, Velocity), CUDA copies (`CudaMemoryBuffer` 49, 80-81) and CPU copies.
- OpenCL copies: `CLMemoryBuffer` 101 / 118 / 129-130 use `Index * ArrayView<T>.ElementSize`
  (line 60 already uses `GetIndexInBytes()`, which is right).
- OpenCL kernel arguments: the view struct's offset field (element index) is added to the `uchar*`
  parameter in `CLKernelFunctionGenerator.GenerateArgumentMapping`.
- WebGPU / Wasm / WebGL (SpawnDev.ILGPU): their own sub-view byte offsets; audit each.

`ArrayView<T>.GetIndexInBytes()` = `Index * BitsPerElement / 8` is the right byte offset for an even element offset.

## An odd offset has no byte address

A device pointer cannot point at the high nibble of a byte, and the GPU load/store paths nibble-address relative
to the view's base (`b[i >> 1]`, nibble `i & 1`). Carrying a nibble phase through every backend's view
representation is not worth it for a 4-bit weight format whose tensors are byte-aligned in practice.

## Fix

1. `SubView` / `GetSubView` of a packed view throws `ArgumentOutOfRangeException` when `(Index + offset) * BitsPerElement`
   is not a multiple of 8, on every backend, the CPU one included (one contract everywhere).
2. Every byte address of a view uses bits: `LoadEffectiveAddress` and the OpenCL copy offsets go through
   `GetIndexInBytes()` (or an equivalent bit computation for `Index + index`).
3. OpenCL kernel arguments: the offset added to the storage pointer is the byte offset for packed types
   (`index * BitsPerElement / 8`), from the argument mapper or the generated mapping.
4. WebGPU / Wasm / WebGL: same audit and fix of their sub-view offsets.

## Test

Shared `BackendTestBase` test, all 9 lanes: QInt4 and Float4E2M1, chunked `SubView` launches at even offsets
(read through one view, write through another, raw packed bytes on the host, everything outside the sub-view and a
neighbouring buffer checked) and an odd offset that must throw at `SubView`. Red-check: the even case fails on
CUDA / OpenCL / Velocity before the fix.
