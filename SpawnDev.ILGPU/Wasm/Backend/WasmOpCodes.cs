// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmOpCodes.cs
// All opcodes verified against the WebAssembly spec (2026-03-22 audit).
// Ref: https://webassembly.github.io/spec/core/binary/instructions.html
// Ref: https://webassembly.github.io/threads/core/binary/instructions.html
// ---------------------------------------------------------------------------------------

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// WebAssembly opcode constants for binary encoding.
    /// </summary>
    public static class WasmOpCodes
    {
        // === Value Types ===
        public const byte I32 = 0x7F;
        public const byte I64 = 0x7E;
        public const byte F32 = 0x7D;
        public const byte F64 = 0x7C;
        public const byte V128 = 0x7B;  // SIMD 128-bit vector value type
        public const byte FuncRef = 0x70;
        public const byte Void = 0x40;  // block type: void

        // === Control Flow ===
        public const byte Unreachable = 0x00;
        public const byte Nop = 0x01;
        public const byte Block = 0x02;
        public const byte Loop = 0x03;
        public const byte If = 0x04;
        public const byte Else = 0x05;
        public const byte End = 0x0B;
        public const byte Br = 0x0C;
        public const byte BrIf = 0x0D;
        public const byte BrTable = 0x0E;
        public const byte Return = 0x0F;
        public const byte Call = 0x10;
        public const byte CallIndirect = 0x11;

        // === Parametric ===
        public const byte Drop = 0x1A;
        public const byte Select = 0x1B;

        // === Variable ===
        public const byte LocalGet = 0x20;
        public const byte LocalSet = 0x21;
        public const byte LocalTee = 0x22;
        public const byte GlobalGet = 0x23;
        public const byte GlobalSet = 0x24;

        // === Memory Load ===
        public const byte I32Load = 0x28;
        public const byte I64Load = 0x29;
        public const byte F32Load = 0x2A;
        public const byte F64Load = 0x2B;
        public const byte I32Load8S = 0x2C;
        public const byte I32Load8U = 0x2D;
        public const byte I32Load16S = 0x2E;
        public const byte I32Load16U = 0x2F;
        public const byte I64Load8S = 0x30;
        public const byte I64Load8U = 0x31;
        public const byte I64Load16S = 0x32;
        public const byte I64Load16U = 0x33;
        public const byte I64Load32S = 0x34;
        public const byte I64Load32U = 0x35;

        // === Memory Store ===
        public const byte I32Store = 0x36;
        public const byte I64Store = 0x37;
        public const byte F32Store = 0x38;
        public const byte F64Store = 0x39;
        public const byte I32Store8 = 0x3A;
        public const byte I32Store16 = 0x3B;
        public const byte I64Store8 = 0x3C;
        public const byte I64Store16 = 0x3D;
        public const byte I64Store32 = 0x3E;

        // === Memory Control ===
        public const byte MemorySize = 0x3F;
        public const byte MemoryGrow = 0x40;

        // === Constants ===
        public const byte I32Const = 0x41;
        public const byte I64Const = 0x42;
        public const byte F32Const = 0x43;
        public const byte F64Const = 0x44;

        // === i32 Comparison ===
        public const byte I32Eqz = 0x45;
        public const byte I32Eq = 0x46;
        public const byte I32Ne = 0x47;
        public const byte I32LtS = 0x48;
        public const byte I32LtU = 0x49;
        public const byte I32GtS = 0x4A;
        public const byte I32GtU = 0x4B;
        public const byte I32LeS = 0x4C;
        public const byte I32LeU = 0x4D;
        public const byte I32GeS = 0x4E;
        public const byte I32GeU = 0x4F;

        // === i64 Comparison ===
        public const byte I64Eqz = 0x50;
        public const byte I64Eq = 0x51;
        public const byte I64Ne = 0x52;
        public const byte I64LtS = 0x53;
        public const byte I64LtU = 0x54;
        public const byte I64GtS = 0x55;
        public const byte I64GtU = 0x56;
        public const byte I64LeS = 0x57;
        public const byte I64LeU = 0x58;
        public const byte I64GeS = 0x59;
        public const byte I64GeU = 0x5A;

        // === f32 Comparison ===
        public const byte F32Eq = 0x5B;
        public const byte F32Ne = 0x5C;
        public const byte F32Lt = 0x5D;
        public const byte F32Gt = 0x5E;
        public const byte F32Le = 0x5F;
        public const byte F32Ge = 0x60;

        // === f64 Comparison ===
        public const byte F64Eq = 0x61;
        public const byte F64Ne = 0x62;
        public const byte F64Lt = 0x63;
        public const byte F64Gt = 0x64;
        public const byte F64Le = 0x65;
        public const byte F64Ge = 0x66;

        // === i32 Arithmetic ===
        public const byte I32Clz = 0x67;
        public const byte I32Ctz = 0x68;
        public const byte I32Popcnt = 0x69;
        public const byte I32Add = 0x6A;
        public const byte I32Sub = 0x6B;
        public const byte I32Mul = 0x6C;
        public const byte I32DivS = 0x6D;
        public const byte I32DivU = 0x6E;
        public const byte I32RemS = 0x6F;
        public const byte I32RemU = 0x70;
        public const byte I32And = 0x71;
        public const byte I32Or = 0x72;
        public const byte I32Xor = 0x73;
        public const byte I32Shl = 0x74;
        public const byte I32ShrS = 0x75;
        public const byte I32ShrU = 0x76;
        public const byte I32Rotl = 0x77;
        public const byte I32Rotr = 0x78;

        // === i64 Arithmetic ===
        public const byte I64Clz = 0x79;
        public const byte I64Ctz = 0x7A;
        public const byte I64Popcnt = 0x7B;
        public const byte I64Add = 0x7C;
        public const byte I64Sub = 0x7D;
        public const byte I64Mul = 0x7E;
        public const byte I64DivS = 0x7F;
        public const byte I64DivU = 0x80;
        public const byte I64RemS = 0x81;
        public const byte I64RemU = 0x82;
        public const byte I64And = 0x83;
        public const byte I64Or = 0x84;
        public const byte I64Xor = 0x85;
        public const byte I64Shl = 0x86;
        public const byte I64ShrS = 0x87;
        public const byte I64ShrU = 0x88;
        public const byte I64Rotl = 0x89;
        public const byte I64Rotr = 0x8A;

        // === f32 Arithmetic ===
        public const byte F32Abs = 0x8B;
        public const byte F32Neg = 0x8C;
        public const byte F32Ceil = 0x8D;
        public const byte F32Floor = 0x8E;
        public const byte F32Trunc = 0x8F;
        public const byte F32Nearest = 0x90;
        public const byte F32Sqrt = 0x91;
        public const byte F32Add = 0x92;
        public const byte F32Sub = 0x93;
        public const byte F32Mul = 0x94;
        public const byte F32Div = 0x95;
        public const byte F32Min = 0x96;
        public const byte F32Max = 0x97;
        public const byte F32Copysign = 0x98;

        // === f64 Arithmetic ===
        public const byte F64Abs = 0x99;
        public const byte F64Neg = 0x9A;
        public const byte F64Ceil = 0x9B;
        public const byte F64Floor = 0x9C;
        public const byte F64Trunc = 0x9D;
        public const byte F64Nearest = 0x9E;
        public const byte F64Sqrt = 0x9F;
        public const byte F64Add = 0xA0;
        public const byte F64Sub = 0xA1;
        public const byte F64Mul = 0xA2;
        public const byte F64Div = 0xA3;
        public const byte F64Min = 0xA4;
        public const byte F64Max = 0xA5;
        public const byte F64Copysign = 0xA6;

        // === Conversions ===
        public const byte I32WrapI64 = 0xA7;
        public const byte I32TruncF32S = 0xA8;
        public const byte I32TruncF32U = 0xA9;
        public const byte I32TruncF64S = 0xAA;
        public const byte I32TruncF64U = 0xAB;
        public const byte I64ExtendI32S = 0xAC;
        public const byte I64ExtendI32U = 0xAD;
        public const byte I64TruncF32S = 0xAE;
        public const byte I64TruncF32U = 0xAF;
        public const byte I64TruncF64S = 0xB0;
        public const byte I64TruncF64U = 0xB1;
        public const byte F32ConvertI32S = 0xB2;
        public const byte F32ConvertI32U = 0xB3;
        public const byte F32ConvertI64S = 0xB4;
        public const byte F32ConvertI64U = 0xB5;
        public const byte F32DemoteF64 = 0xB6;
        public const byte F64ConvertI32S = 0xB7;
        public const byte F64ConvertI32U = 0xB8;
        public const byte F64ConvertI64S = 0xB9;
        public const byte F64ConvertI64U = 0xBA;
        public const byte F64PromoteF32 = 0xBB;
        public const byte I32ReinterpretF32 = 0xBC;
        public const byte I64ReinterpretF64 = 0xBD;
        public const byte F32ReinterpretI32 = 0xBE;
        public const byte F64ReinterpretI64 = 0xBF;

        // === Sign Extension ===
        public const byte I32Extend8S = 0xC0;
        public const byte I32Extend16S = 0xC1;
        public const byte I64Extend8S = 0xC2;
        public const byte I64Extend16S = 0xC3;
        public const byte I64Extend32S = 0xC4;

        // === Atomic Instructions (0xFE prefix) ===
        // Ref: https://webassembly.github.io/threads/core/binary/instructions.html
        public const byte AtomicPrefix = 0xFE;

        // Atomic memory control
        public const byte MemoryAtomicNotify = 0x00;    // memory.atomic.notify
        public const byte MemoryAtomicWait32 = 0x01;    // memory.atomic.wait32
        public const byte MemoryAtomicWait64 = 0x02;    // memory.atomic.wait64
        public const byte AtomicFence = 0x03;            // atomic.fence

        // Atomic loads
        public const byte I32AtomicLoad = 0x10;          // i32.atomic.load
        public const byte I64AtomicLoad = 0x11;          // i64.atomic.load
        public const byte I32AtomicLoad8U = 0x12;        // i32.atomic.load8_u
        public const byte I32AtomicLoad16U = 0x13;       // i32.atomic.load16_u
        public const byte I64AtomicLoad8U = 0x14;        // i64.atomic.load8_u
        public const byte I64AtomicLoad16U = 0x15;       // i64.atomic.load16_u
        public const byte I64AtomicLoad32U = 0x16;       // i64.atomic.load32_u

        // Atomic stores
        public const byte I32AtomicStore = 0x17;         // i32.atomic.store
        public const byte I64AtomicStore = 0x18;         // i64.atomic.store
        public const byte I32AtomicStore8 = 0x19;        // i32.atomic.store8
        public const byte I32AtomicStore16 = 0x1A;       // i32.atomic.store16
        public const byte I64AtomicStore8 = 0x1B;        // i64.atomic.store8
        public const byte I64AtomicStore16 = 0x1C;       // i64.atomic.store16
        public const byte I64AtomicStore32 = 0x1D;       // i64.atomic.store32

        // Atomic RMW — Add (0x1E-0x24)
        public const byte I32AtomicRmwAdd = 0x1E;        // i32.atomic.rmw.add
        public const byte I64AtomicRmwAdd = 0x1F;        // i64.atomic.rmw.add
        public const byte I32AtomicRmw8AddU = 0x20;      // i32.atomic.rmw8.add_u
        public const byte I32AtomicRmw16AddU = 0x21;     // i32.atomic.rmw16.add_u
        public const byte I64AtomicRmw8AddU = 0x22;      // i64.atomic.rmw8.add_u
        public const byte I64AtomicRmw16AddU = 0x23;     // i64.atomic.rmw16.add_u
        public const byte I64AtomicRmw32AddU = 0x24;     // i64.atomic.rmw32.add_u

        // Atomic RMW — Sub (0x25-0x2B)
        public const byte I32AtomicRmwSub = 0x25;        // i32.atomic.rmw.sub
        public const byte I64AtomicRmwSub = 0x26;        // i64.atomic.rmw.sub
        public const byte I32AtomicRmw8SubU = 0x27;      // i32.atomic.rmw8.sub_u
        public const byte I32AtomicRmw16SubU = 0x28;     // i32.atomic.rmw16.sub_u
        public const byte I64AtomicRmw8SubU = 0x29;      // i64.atomic.rmw8.sub_u
        public const byte I64AtomicRmw16SubU = 0x2A;     // i64.atomic.rmw16.sub_u
        public const byte I64AtomicRmw32SubU = 0x2B;     // i64.atomic.rmw32.sub_u

        // Atomic RMW — And (0x2C-0x32)
        public const byte I32AtomicRmwAnd = 0x2C;        // i32.atomic.rmw.and
        public const byte I64AtomicRmwAnd = 0x2D;        // i64.atomic.rmw.and
        public const byte I32AtomicRmw8AndU = 0x2E;      // i32.atomic.rmw8.and_u
        public const byte I32AtomicRmw16AndU = 0x2F;     // i32.atomic.rmw16.and_u
        public const byte I64AtomicRmw8AndU = 0x30;      // i64.atomic.rmw8.and_u
        public const byte I64AtomicRmw16AndU = 0x31;     // i64.atomic.rmw16.and_u
        public const byte I64AtomicRmw32AndU = 0x32;     // i64.atomic.rmw32.and_u

        // Atomic RMW — Or (0x33-0x39)
        public const byte I32AtomicRmwOr = 0x33;         // i32.atomic.rmw.or
        public const byte I64AtomicRmwOr = 0x34;         // i64.atomic.rmw.or
        public const byte I32AtomicRmw8OrU = 0x35;       // i32.atomic.rmw8.or_u
        public const byte I32AtomicRmw16OrU = 0x36;      // i32.atomic.rmw16.or_u
        public const byte I64AtomicRmw8OrU = 0x37;       // i64.atomic.rmw8.or_u
        public const byte I64AtomicRmw16OrU = 0x38;      // i64.atomic.rmw16.or_u
        public const byte I64AtomicRmw32OrU = 0x39;      // i64.atomic.rmw32.or_u

        // Atomic RMW — Xor (0x3A-0x40)
        public const byte I32AtomicRmwXor = 0x3A;        // i32.atomic.rmw.xor
        public const byte I64AtomicRmwXor = 0x3B;        // i64.atomic.rmw.xor
        public const byte I32AtomicRmw8XorU = 0x3C;      // i32.atomic.rmw8.xor_u
        public const byte I32AtomicRmw16XorU = 0x3D;     // i32.atomic.rmw16.xor_u
        public const byte I64AtomicRmw8XorU = 0x3E;      // i64.atomic.rmw8.xor_u
        public const byte I64AtomicRmw16XorU = 0x3F;     // i64.atomic.rmw16.xor_u
        public const byte I64AtomicRmw32XorU = 0x40;     // i64.atomic.rmw32.xor_u

        // Atomic RMW — Xchg (0x41-0x47)
        public const byte I32AtomicRmwXchg = 0x41;       // i32.atomic.rmw.xchg
        public const byte I64AtomicRmwXchg = 0x42;       // i64.atomic.rmw.xchg
        public const byte I32AtomicRmw8XchgU = 0x43;     // i32.atomic.rmw8.xchg_u
        public const byte I32AtomicRmw16XchgU = 0x44;    // i32.atomic.rmw16.xchg_u
        public const byte I64AtomicRmw8XchgU = 0x45;     // i64.atomic.rmw8.xchg_u
        public const byte I64AtomicRmw16XchgU = 0x46;    // i64.atomic.rmw16.xchg_u
        public const byte I64AtomicRmw32XchgU = 0x47;    // i64.atomic.rmw32.xchg_u

        // Atomic RMW — CmpXchg (0x48-0x4E)
        public const byte I32AtomicRmwCmpxchg = 0x48;    // i32.atomic.rmw.cmpxchg
        public const byte I64AtomicRmwCmpxchg = 0x49;    // i64.atomic.rmw.cmpxchg
        public const byte I32AtomicRmw8CmpxchgU = 0x4A;  // i32.atomic.rmw8.cmpxchg_u
        public const byte I32AtomicRmw16CmpxchgU = 0x4B; // i32.atomic.rmw16.cmpxchg_u
        public const byte I64AtomicRmw8CmpxchgU = 0x4C;  // i64.atomic.rmw8.cmpxchg_u
        public const byte I64AtomicRmw16CmpxchgU = 0x4D; // i64.atomic.rmw16.cmpxchg_u
        public const byte I64AtomicRmw32CmpxchgU = 0x4E; // i64.atomic.rmw32.cmpxchg_u

        // === SIMD Instructions (0xFD prefix) ===
        // Ref: https://github.com/WebAssembly/simd/blob/main/proposals/simd/BinarySIMD.md
        // (opcode values verified against the spec table 2026-06-14).
        // CRITICAL: unlike the atomic 0xFE sub-opcodes (single byte), the SIMD sub-opcode is a
        // u32 LEB128 after the 0xFD prefix — values >= 128 (e.g. f32x4.add = 228) encode to TWO
        // LEB bytes (0xE4 0x01). These constants are the RAW decimal sub-opcode; always emit them
        // through EmitU32Leb128, never as a raw byte. Stored as uint because core SIMD opcodes
        // exceed 255 (and to make the LEB requirement explicit at the call site).
        public const byte SimdPrefix = 0xFD;

        // Memory (take a memarg: align + offset)
        public const uint V128Load = 0;          // v128.load
        public const uint V128Store = 11;        // v128.store
        // Const (16 immediate bytes) / shuffle (16 lane-index bytes)
        public const uint V128Const = 12;        // v128.const
        public const uint I8x16Shuffle = 13;     // i8x16.shuffle
        // Splat (lane -> all lanes)
        public const uint I8x16Splat = 15;       // i8x16.splat
        public const uint I16x8Splat = 16;       // i16x8.splat
        public const uint I32x4Splat = 17;       // i32x4.splat
        public const uint I64x2Splat = 18;       // i64x2.splat
        public const uint F32x4Splat = 19;       // f32x4.splat
        public const uint F64x2Splat = 20;       // f64x2.splat
        // Extract/replace lane (take a 1-byte lane index immediate)
        public const uint I32x4ExtractLane = 27; // i32x4.extract_lane
        public const uint I32x4ReplaceLane = 28; // i32x4.replace_lane
        public const uint I64x2ExtractLane = 29; // i64x2.extract_lane
        public const uint I64x2ReplaceLane = 30; // i64x2.replace_lane
        public const uint F32x4ExtractLane = 31; // f32x4.extract_lane
        public const uint F32x4ReplaceLane = 32; // f32x4.replace_lane
        public const uint F64x2ExtractLane = 33; // f64x2.extract_lane
        public const uint F64x2ReplaceLane = 34; // f64x2.replace_lane
        // Bitwise (whole-vector)
        public const uint V128Not = 77;          // v128.not
        public const uint V128And = 78;          // v128.and
        public const uint V128AndNot = 79;       // v128.andnot
        public const uint V128Or = 80;           // v128.or
        public const uint V128Xor = 81;          // v128.xor
        public const uint V128Bitselect = 82;    // v128.bitselect (a, b, mask) -> per-bit select
        // i32x4 comparisons (each lane → all-ones / all-zeros mask for v128.bitselect)
        public const uint I32x4Eq = 55;          // i32x4.eq
        public const uint I32x4Ne = 56;          // i32x4.ne
        public const uint I32x4LtS = 57;         // i32x4.lt_s
        public const uint I32x4LtU = 58;         // i32x4.lt_u
        public const uint I32x4GtS = 59;         // i32x4.gt_s
        public const uint I32x4GtU = 60;         // i32x4.gt_u
        public const uint I32x4LeS = 61;         // i32x4.le_s
        public const uint I32x4LeU = 62;         // i32x4.le_u
        public const uint I32x4GeS = 63;         // i32x4.ge_s
        public const uint I32x4GeU = 64;         // i32x4.ge_u
        // f32x4 comparisons
        public const uint F32x4Eq = 65;          // f32x4.eq
        public const uint F32x4Ne = 66;          // f32x4.ne
        public const uint F32x4Lt = 67;          // f32x4.lt
        public const uint F32x4Gt = 68;          // f32x4.gt
        public const uint F32x4Le = 69;          // f32x4.le
        public const uint F32x4Ge = 70;          // f32x4.ge
        // i32x4 arithmetic
        public const uint I32x4Abs = 160;        // i32x4.abs
        public const uint I32x4Neg = 161;        // i32x4.neg
        public const uint I32x4Shl = 171;        // i32x4.shl   (v128, i32 count) -> v128
        public const uint I32x4ShrS = 172;       // i32x4.shr_s (v128, i32 count) -> v128
        public const uint I32x4ShrU = 173;       // i32x4.shr_u (v128, i32 count) -> v128
        public const uint I32x4Add = 174;        // i32x4.add
        public const uint I32x4Sub = 177;        // i32x4.sub
        public const uint I32x4Mul = 181;        // i32x4.mul
        public const uint I32x4MinS = 182;       // i32x4.min_s
        public const uint I32x4MinU = 183;       // i32x4.min_u
        public const uint I32x4MaxS = 184;       // i32x4.max_s
        public const uint I32x4MaxU = 185;       // i32x4.max_u
        // f32x4 arithmetic
        public const uint F32x4Abs = 224;        // f32x4.abs
        public const uint F32x4Neg = 225;        // f32x4.neg
        public const uint F32x4Sqrt = 227;       // f32x4.sqrt
        public const uint F32x4Add = 228;        // f32x4.add
        public const uint F32x4Sub = 229;        // f32x4.sub
        public const uint F32x4Mul = 230;        // f32x4.mul
        public const uint F32x4Div = 231;        // f32x4.div
        public const uint F32x4Min = 232;        // f32x4.min
        public const uint F32x4Max = 233;        // f32x4.max
        // Lane conversions (4-lane <-> 4-lane). NOTE: f32x4 -> i32x4 is ONLY the SATURATING
        // trunc_sat form (248/249); the scalar path uses non-saturating i32.trunc_f32_s (traps on
        // overflow/NaN), so vectorizing f32->i32 would break cross-mode determinism — deferred.
        // i32x4 -> f32x4 (250/251) rounds identically to the scalar f32.convert_i32_s/_u, so it is
        // cross-mode exact and safe to vectorize.
        public const uint I32x4TruncSatF32x4S = 248; // i32x4.trunc_sat_f32x4_s (saturating; deferred)
        public const uint I32x4TruncSatF32x4U = 249; // i32x4.trunc_sat_f32x4_u (saturating; deferred)
        public const uint F32x4ConvertI32x4S = 250;  // f32x4.convert_i32x4_s
        public const uint F32x4ConvertI32x4U = 251;  // f32x4.convert_i32x4_u
        // f64x2 arithmetic
        public const uint F64x2Add = 240;        // f64x2.add
        public const uint F64x2Sub = 241;        // f64x2.sub
        public const uint F64x2Mul = 242;        // f64x2.mul
        public const uint F64x2Div = 243;        // f64x2.div
        public const uint F64x2Min = 244;        // f64x2.min
        public const uint F64x2Max = 245;        // f64x2.max

        // === Section IDs ===
        public const byte SectionCustom = 0;
        public const byte SectionType = 1;
        public const byte SectionImport = 2;
        public const byte SectionFunction = 3;
        public const byte SectionTable = 4;
        public const byte SectionMemory = 5;
        public const byte SectionGlobal = 6;
        public const byte SectionExport = 7;
        public const byte SectionStart = 8;
        public const byte SectionElement = 9;
        public const byte SectionCode = 10;
        public const byte SectionData = 11;
        public const byte SectionDataCount = 12;

        // === Import/Export Kinds ===
        public const byte ExternalFunc = 0x00;
        public const byte ExternalTable = 0x01;
        public const byte ExternalMemory = 0x02;
        public const byte ExternalGlobal = 0x03;

        // === Memory Limits Flags ===
        public const byte LimitsNoMax = 0x00;
        public const byte LimitsWithMax = 0x01;
        public const byte LimitsSharedNoMax = 0x02;
        public const byte LimitsSharedWithMax = 0x03;

        // === Function Type Tag ===
        public const byte FuncTypeTag = 0x60;
    }
}
