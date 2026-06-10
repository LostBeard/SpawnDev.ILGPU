# SpawnDev.ILGPU 16-bit Type Support Audit

**Date:** 2026-04-12
**Scope:** WebGPU backend - int8, int16, uint16, float16 math and buffer access
**Finding:** Sub-word buffer access infrastructure EXISTS but only handles 8-bit. 16-bit is broken.

> **⚠ STATUS: HISTORICAL / RESOLVED (do not cite as current).** This is the PRE-FIX audit (Apr 2026). Everything below — including "Float16 without shader-f16 ... LIKELY BROKEN" and the "What Needs Fixing" list — has SHIPPED. **f16 is supported on EVERY backend; where native `shader-f16` is unavailable it is EMULATED losslessly** (`_f16_to_f32`/`_f32_to_f16`), so `Capabilities.Float16` is always `true` and only `Capabilities.Float16Native` distinguishes native vs emulated. See `Plans/f16-emulation-plan.md` "Shipping status" (Phases 1-4 SHIPPED) and `SpawnDev.ILGPU/WebGPU/CLAUDE.md` "Float16 (Half) — Native and Emulated". Kept for historical context only.

---

## The Core Bug

`WGSLKernelFunctionGenerator.cs` line 1126-1128:
```csharp
if (paramElemType is PrimitiveType pt &&
    (pt.BasicValueType == BasicValueType.Int8 || pt.BasicValueType == BasicValueType.Int16))
    _byteElementParams.Add(param.Index);
```

Both Int8 AND Int16 get added to `_byteElementParams`. But the extraction code at line 3708 only handles BYTE extraction:
```csharp
// Extracts ONE BYTE from a u32 word - divides by 4, shifts by 8 bits, masks 0xFF
var extractExpr = $"i32((param{byteParamIdx}[u32({byteIdx}) / 4u] >> ((u32({byteIdx}) % 4u) * 8u)) & 0xFFu)";
```

For Int16, this reads ONE BYTE instead of TWO BYTES. Data corruption.

### What Int16 extraction should look like
```csharp
// Extracts ONE SHORT (2 bytes) from a u32 word - divides by 2, shifts by 16 bits, masks 0xFFFF
var extractExpr = $"i32((param{paramIdx}[u32({idx}) / 2u] >> ((u32({idx}) % 2u) * 16u)) & 0xFFFFu)";
```

### What needs to change
1. Separate tracking: `_byteElementParams` for Int8, new `_shortElementParams` for Int16/UInt16
2. LEA codegen: different address math for 1-byte vs 2-byte elements
3. Load codegen: byte extraction (/ 4, % 4, * 8, & 0xFF) vs short extraction (/ 2, % 2, * 16, & 0xFFFF)
4. Store codegen: same pattern for writes (atomic RMW or read-modify-write)

---

## Full Audit: All 16-bit Touchpoints

### WGSLTypeGenerator.cs (type mapping)

| Line | Mapping | Status |
|------|---------|--------|
| 116 | Int8 -> "i32" | OK (promoted) |
| 117 | Int16 -> "i32" | OK (promoted) |
| 120 | Float16 -> "f16" or "f32" | OK (conditional native) |
| 135 | ArithmeticInt8 -> "i32" | OK |
| 136 | ArithmeticInt16 -> "i32" | OK |
| 139 | ArithmeticUInt8 -> "u32" | OK |
| 140 | ArithmeticUInt16 -> "u32" | OK |
| 143 | ArithmeticFloat16 -> "f16" or "f32" | OK |

Type PROMOTION is handled. Types become i32/u32/f32 in WGSL. The issue is only in BUFFER ACCESS.

### WGSLKernelFunctionGenerator.cs (buffer access)

| Line | What | Issue |
|------|------|-------|
| 69-73 | `_byteElementParams` tracking | **BUG: Int16 lumped with Int8** |
| 1126-1128 | Adding Int8 + Int16 to same set | **BUG: should be separate** |
| 3553-3565 | LEA for byte-element views | **BUG: address math is byte-only** |
| 3704-3708 | Load extraction | **BUG: extracts 1 byte, not 2 for Int16** |
| 3564 | Cross-block pointer expression | **BUG: byte extraction only** |

### WGSLKernelFunctionGenerator.cs (Store for sub-word)

**NOT FOUND.** There is Load extraction but no Store packing. If a kernel writes to an `ArrayView<short>`, the Store codegen likely writes a full i32 to the buffer, overwriting the adjacent 16-bit value. This needs atomic read-modify-write or at minimum a pack-and-write.

### WebGPUIntrinsics.cs (math intrinsics)

| Function | short | sbyte | Status |
|----------|-------|-------|--------|
| Abs | line 164 | line 158 | OK - C# level, promoted to i32 in WGSL |
| Min | line 190 | line 183 | OK |
| Max | line 218 | line 213 | OK |

These work because they're C# intrinsics that get compiled to i32 WGSL operations after type promotion. No buffer access involved.

### WGSLCodeGenerator.cs (constants)

| Line | What | Status |
|------|------|--------|
| 1594 | Int8 constant emission | OK |
| 1595 | Int16 constant emission | OK |
| 1598 | Float16 constant emission | OK (uses float cast) |

Constants are fine - they're scalar values, not buffer reads.

### WebGPUAccelerator.cs (buffer allocation + dispatch)

| Line | What | Issue |
|------|------|-------|
| 1188-1189 | f16 bit packing for buffer upload | OK for native f16 |
| Buffer alloc | MemoryBuffer1D<short> | **NEEDS CHECK: is buffer size correct?** |

When allocating `MemoryBuffer1D<short, Dense>(256)`, does WebGPU allocate 256*2=512 bytes? Or 256*4=1024 bytes? If the WGSL binding declares `array<u32>` (128 elements for 256 shorts), the buffer MUST be 128*4=512 bytes. Check that `AllocateRawInternal` uses the element size correctly.

### ILGPU/IR/Construction/ArithmeticOperations.cs (core IR)

The IR level handles Int8, Int16, Float16 for constant folding (Neg, Not, Abs, PopCount, LeadingZeroCount, etc.). These are compile-time operations, not runtime buffer access. **No issues here.**

### ILGPU.Algorithms (Scan, RadixSort)

RadixSort uses `ArrayView<int>` internally for histograms and scatter. If someone calls RadixSort on `ArrayView<short>`, the algorithm would need to handle sub-word access. **Check: does RadixSort accept non-int element types?** If not, it would fail at compile time (type mismatch), which is safe. If it does, it would hit the same buffer access bug.

---

## Float16 Specific Issues

### With native shader-f16 (GPU supports it)
- Type: `f16` in WGSL
- Buffer: `array<f16>` is valid when shader-f16 enabled
- No sub-word extraction needed - native f16 buffer access works
- HalfExtensions intrinsics registered (lines 731-745)
- **Status: SHOULD WORK on GPUs with shader-f16**

### Without native shader-f16 (emulated, TJ's GPU)
- Type: `f32` in WGSL (promoted)
- Buffer: would need sub-word access like Int16
- Float16 is added to `_byteElementParams`? **CHECK** - line 1126 only checks Int8 and Int16, NOT Float16
- If Float16 buffers are NOT in `_byteElementParams`, the Load codegen treats them as regular f32 reads from a buffer packed with 16-bit floats = same stride mismatch bug as Int16
- **Status: LIKELY BROKEN on GPUs without shader-f16**

### Verification needed
```csharp
// Does this line also need Float16?
if (paramElemType is PrimitiveType pt &&
    (pt.BasicValueType == BasicValueType.Int8 || pt.BasicValueType == BasicValueType.Int16))
    _byteElementParams.Add(param.Index);
// Should it be:
if (paramElemType is PrimitiveType pt &&
    (pt.BasicValueType == BasicValueType.Int8 || 
     pt.BasicValueType == BasicValueType.Int16 ||
     (!Backend.HasShaderF16 && pt.BasicValueType == BasicValueType.Float16)))
    _byteElementParams.Add(param.Index);
```

---

## Summary: What Needs Fixing

### Critical (blocking AubsCraft)
1. **Separate Int16 from Int8 tracking** - new `_shortElementParams` HashSet
2. **Int16 Load extraction** - `/2u`, `*16u`, `&0xFFFFu` instead of `/4u`, `*8u`, `&0xFFu`
3. **Int16 Store packing** - write 16 bits into the correct half of a u32 word
4. **Int16 LEA address math** - element index * 2 bytes, not * 1 byte

### Important (affects ML library)
5. **Float16 without shader-f16** - add to sub-word tracking when native f16 unavailable
6. **Float16 Load/Store** - same sub-word extraction but with f16<->f32 conversion
7. **Float16 buffer allocation** - correct byte size for packed f16 data

### Nice-to-have (completeness)
8. **Int8/UInt8 Store** - verify Store codegen handles byte writes (Load exists, Store may not)
9. **RadixSort type check** - ensure algorithms reject or handle sub-word element types
10. **Unit tests** - int16 read, int16 write, int16 kernel, f16 emulated read/write/kernel

---

## Files to Change (in priority order)

1. `WebGPU/Backend/WGSLKernelFunctionGenerator.cs` - Load/Store/LEA for int16 + f16
2. `WebGPU/Backend/WGSLTypeGenerator.cs` - no changes needed (types already promoted)
3. `WebGPU/WebGPUAccelerator.cs` - verify buffer sizing for sub-word types
4. `WebGPU/Backend/WebGPUBackend.cs` - possibly register f16 emulation intrinsics for non-shader-f16
5. Tests: int16 + f16 buffer access tests on WebGPU backend
