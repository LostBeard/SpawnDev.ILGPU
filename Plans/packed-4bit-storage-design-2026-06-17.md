# Design: TRUE Packed 4-bit Storage (sub-byte addressing) — `ArrayView<Int4>` = 2/byte

**Author:** Geordi · **Date:** 2026-06-17 · **Status:** DESIGN (TJ chose "true packed, ArrayView<Int4> = 2/byte" over the 1-byte compute type; FP4 retrofitted too for family consistency).

## Goal
`ArrayView<Int4>` / `<UInt4>` / `<Float4E2M1>` of N elements allocates **ceil(N/2) bytes** and addresses **2 nibbles per byte (8 per u32)**, with transparent `q[i]` indexing. The real 4-bit memory win, not the 1-byte-per-element placeholder FP4 currently uses.

## What the addressing map established (file:line anchors)
- **Allocation byte math** (the genuinely-new mechanic): `MemoryBuffer.cs:87` `LengthInBytes => Length * ElementSize`; `Accelerator.cs:426-430` `AllocateRaw(length, elementSize)`; `ArrayView.cs:331,398` (`LengthInBytes`, `GetIndexInBytes`). `ElementSize = Interop.SizeOf<T>()` (CLR struct size = 1 for a 1-byte struct) — an INT, can't be ½. The IR in-kernel stride is a SEPARATE number: `PrimitiveTypes.cs:27-41,83` (`Float4E2M1 = 1`).
- **The LEA is the ONLY `index*Size` site per backend** (2D/3D flatten to a linear element index in the Stride types BEFORE the LEA — `StrideTypes.cs` `ComputeElementIndex`; `ViewIntrinsics.cs:218` `CreateLoadElementAddress(view, linearIndex)`). So nibble addressing lives in ONE LEA/Load/Store triple per backend.
- **Index survives to Load/Store (keep-index model):** WGSL `WGSLKernelFunctionGenerator.cs` LEA `:5016` tracks `_subWordLEAVars[var]=paramIdx`, load `:5642` (`word=idx/4; shift=(idx%4)*8; mask 0xFF`), store `:5862` (atomicAnd/atomicOr RMW). GLSL `:2853`/`:3040`. OpenCL `CLCodeGenerator.Views.cs:102` `_fp4EmulatedLEAs[target]=(source,index)`, load `Values.cs:580` `_e2m1_bits_to_f32(base[index])`.
- **Index folded (needs the keep-index path added for 4-bit):** PTX `PTXCodeGenerator.Views.cs:31,62-69` (`base + index*Size` MAD → byte addr, index gone by `Values.cs:1164` load). Velocity/CPU `VelocityCodeGenerator.Views.cs:77`. Wasm `WasmKernelFunctionGenerator.cs:2155-2170` (`index*elemSize` byte addr + native `i32.load8_u`; no `load4`). **Wasm is the highest-effort backend** (map's caveat).

## Architecture decision: the "keep-index" model, uniform across backends
For a 4-bit element type, the LEA does NOT produce a flat byte pointer; it keeps `(base, elementIndex)` so the Load/Store compute **byte = base + index>>1, nibble = (index&1)*4, mask 0xF, sign-extend/decode**. Browser+OpenCL already do this (just change density 4→8 / byte+nibble). PTX/CPU/Wasm get the path added for 4-bit. This is the established sub-word pattern, pushed one level finer.

A 4-bit type is identified by a single new property — call it **`PrimitiveType.IsPacked4Bit`** (or a `BitWidth=4`) — derived from the BasicValueType set {Int4, UInt4-via-arith, Float4E2M1}. Every packing decision (alloc byte-length, LEA keep-index, Load/Store nibble) consults it.

## Allocation packing (the new mechanic — do this FIRST, it's the foundation)
Introduce a per-type **pack factor** `P` (elements per byte; P=2 for 4-bit, P=1 for everything else) consulted by the byte-length math:
- Managed: `bytes = ceil(Length / P) * groupBytes` (group to byte; round up). Plumb `P` from `ArrayView<T>.ElementSize` analog — a new `ArrayView<T>.PackFactor` (static, from a `[Packed4Bit]` attribute on the Int4/UInt4/Float4E2M1 structs, or a type check). `MemoryBuffer.LengthInBytes`, `Accelerator.AllocateRaw`, `ArrayView.LengthInBytes`/`GetIndexInBytes` consult it.
- Round allocation to 4-byte (u32) for the browser word-packing + alignment (existing pad-to-4 already there).
- **Host CopyFrom/CopyTo PACK/UNPACK:** a host `Int4[]` is N×1 managed byte (low nibble per element). The device buffer is ceil(N/2) packed bytes. `CopyFromHost` must pack 2 nibbles→1 byte; `CopyToHost` must unpack. (Or: define the host representation as already-packed `byte[]` and the typed `Int4[]` is a logical wrapper — DECIDE: simplest is pack-on-upload / unpack-on-download in the buffer copy path.)

## Per-backend nibble Load/Store (after allocation packs)
- **WGSL/GLSL (template, least change):** density 4→8: `word=idx>>3; shift=(idx&7)*4; mask 0xF`. WGSL load `:5642`/store `:5862` RMW; GLSL load `:3040`. `arrayLength()` elements-per-word 4→8 (`:4728-4729,7276`). FP4 `_subWordFloat4Params` registration already exists.
- **OpenCL (keeps index):** load `base[index]`→`(base[index>>1] >> ((index&1)*4)) & 0xF` + decode; store = byte RMW (read byte, clear nibble, or-in). `CLCodeGenerator.Values.cs:580,678`.
- **PTX/CUDA + CPU/Velocity (fold index):** add a 4-bit branch to the LEA that keeps the index (don't fold), and to Load/Store that compute byte=base+idx>>1 + nibble extract + the existing `EmitFP4BitsToF32`/`EmitF32ToFP4Bits` (FP4) or sign-extend (Int4). Store = byte RMW (ld.u8, clear nibble, or, st.u8) — NB the desktop store needs to be safe vs adjacent-nibble races (atomic RMW on the byte, or the radix/algorithm guarantees no concurrent adjacent writes — DECIDE; CUDA has `atom.*.b32`-level, byte RMW needs a u32 atomic on the containing word like the browser path).
- **Wasm (highest effort):** no `load4`; add the index-keep + nibble-extract (byte `i32.load8_u` at idx>>1 + shift/mask; store = byte RMW or u32-atomic RMW under barriers).

## Build order (each step verified before the next)
1. **Design + the `IsPacked4Bit`/PackFactor plumbing** (this doc + the property on the types/PrimitiveType + the ElementSize/PackFactor static).
2. **Allocation packs** (managed byte-math + host pack/unpack on CopyFrom/CopyTo) — validate with a host round-trip: upload Int4[]/FP4[], download, byte-count = ceil(N/2). NO kernel yet.
3. **Build the nibble LEA/Load/Store on ONE backend first — OpenCL** (it already keeps the index → smallest delta; AND desktop-verifiable without PMT). Validate: a kernel `y[i]=(float)x[i]` over a packed FP4 buffer round-trips, and `fp4-verify`-style bit-exact. Use FP4 (already fully wired) as the vehicle so only the STORAGE changes.
4. **Roll out** CPU/Velocity → PTX → then the 3 browser backends (WGSL/GLSL density tweak + Wasm new path). PMT each.
5. **INT4** (`Int4`/`UInt4` structs + BasicValueType + sign-extend convert + radix + capability) — reuses the packed machinery from 1-4. Conversion-only (no INumber, matching Int8/16, per TJ).
6. **Retrofit FP4** is folded into steps 2-4 (FP4 IS the vehicle); update `fp4-verify` + PMT for the packed layout; re-ship.

## Open decisions to lock during impl
- Host representation: pack-on-copy (typed `Int4[]` host, packed device) vs packed-`byte[]` host wrapper. **RESOLVED 2026-06-17 = raw packed bytes, NO transparent typed pack/unpack** (Tuvok confirmed MXFP4/NF4 = option (a): consumers supply pre-packed `uint`/`byte` words + decode in-register; a transparent `Int4[]` host API buys the fused dequant nothing). So `CopyFrom`/`CopyTo` stay RAW byte copies; the user packs/unpacks. No pack-on-copy. Matches TJ's "raw transport, no needless conversion."
- Desktop store race: **RESOLVED = u32-atomic-RMW on the containing word, on EVERY backend incl. desktop.** A general `dst[i]=…` kernel has thread i and thread i+1 writing the two nibbles of the SAME byte concurrently — a plain byte RMW races (one clobbers the other). CUDA `atom.and/atom.or .b32`, OpenCL `atomic_and/atomic_or`, browser already atomic. (NB: the fork was told plain byte RMW is OK for the desktop validation — VERIFY its store and upgrade to atomic-word-RMW; a small-N test can false-pass a racy store.)
- `Length` semantics for an odd N (last byte half-used) — element count stays N; allocation rounds up.

## STATUS UPDATE 2026-06-17 (what's actually built vs this doc)
- **Step 2 (allocation packs) DONE + committed `1bded96`** — but via a cleaner mechanic than the `IsPacked4Bit`/`PackFactor` sketch above: a **`[PackedBits(4)]` attribute** on the type → **`ArrayView<T>.BitsPerElement`** static (4 or ElementSize*8) → threaded `AllocateRaw`→8 backend `AllocateRawInternal`→ctors→`MemoryBuffer.Init`; **`LengthInBytes = ceil(Length*BitsPerElement/8)`** (collapses to Length*ElementSize for whole-byte types). `MemoryBuffer<TView>` wrapper inherits BitsPerElement from the underlying buffer. Validated `packed-alloc-verify` (Int4=ceil(N/2), whole-byte unchanged), no regression (PMT Float4E2M1 23/0). No host pack/unpack (raw-bytes decision above).
- **Sequence CHANGED: INT4 FIRST, FP4 retrofit LAST** (not FP4-as-vehicle). Reason: FP4 is shipped + its allocation flip ([PackedBits] on Float4E2M1) is GLOBAL (breaks all 6 backends at once until every load/store is nibble-ported) — no incremental validation. Int4 is NEW → develop + validate per-backend in isolation with zero breakage. Once the nibble load/store machinery is proven on Int4 across all 6, retrofit FP4 (all-6 flip) reusing it.
- **IN FLIGHT:** fork wiring Int4/UInt4 BasicValueType (enums `BasicValueType.Int4` + `ArithmeticBasicValueType.Int4`/`UInt4` added) + the nibble load/store on CPU/CUDA/OpenCL + a `packed-int4-verify` harness. Browser backends (WGSL/GLSL density 4→8 + Wasm new path, all atomic-word-RMW store) + radix keys + `RequiresInt4`/capability + FP4 retrofit = my follow-on.
