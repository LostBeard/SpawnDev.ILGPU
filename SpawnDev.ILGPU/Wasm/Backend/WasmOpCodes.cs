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
        /// <summary>Value type i32 (0x7F).</summary>
        public const byte I32 = 0x7F;
        /// <summary>Value type i64 (0x7E).</summary>
        public const byte I64 = 0x7E;
        /// <summary>Value type f32 (0x7D).</summary>
        public const byte F32 = 0x7D;
        /// <summary>Value type f64 (0x7C).</summary>
        public const byte F64 = 0x7C;
        /// <summary>Value type v128, the SIMD 128-bit vector (0x7B).</summary>
        public const byte V128 = 0x7B;  // SIMD 128-bit vector value type
        /// <summary>Reference type funcref (0x70).</summary>
        public const byte FuncRef = 0x70;
        /// <summary>Empty block type, a block with no result (0x40).</summary>
        public const byte Void = 0x40;  // block type: void

        // === Control Flow ===
        /// <summary>unreachable (0x00).</summary>
        public const byte Unreachable = 0x00;
        /// <summary>nop (0x01).</summary>
        public const byte Nop = 0x01;
        /// <summary>block (0x02).</summary>
        public const byte Block = 0x02;
        /// <summary>loop (0x03).</summary>
        public const byte Loop = 0x03;
        /// <summary>if (0x04).</summary>
        public const byte If = 0x04;
        /// <summary>else (0x05).</summary>
        public const byte Else = 0x05;
        /// <summary>end (0x0B).</summary>
        public const byte End = 0x0B;
        /// <summary>br (0x0C).</summary>
        public const byte Br = 0x0C;
        /// <summary>br_if (0x0D).</summary>
        public const byte BrIf = 0x0D;
        /// <summary>br_table (0x0E).</summary>
        public const byte BrTable = 0x0E;
        /// <summary>return (0x0F).</summary>
        public const byte Return = 0x0F;
        /// <summary>call (0x10).</summary>
        public const byte Call = 0x10;
        /// <summary>call_indirect (0x11).</summary>
        public const byte CallIndirect = 0x11;

        // === Parametric ===
        /// <summary>drop (0x1A).</summary>
        public const byte Drop = 0x1A;
        /// <summary>select (0x1B).</summary>
        public const byte Select = 0x1B;

        // === Variable ===
        /// <summary>local.get (0x20).</summary>
        public const byte LocalGet = 0x20;
        /// <summary>local.set (0x21).</summary>
        public const byte LocalSet = 0x21;
        /// <summary>local.tee (0x22).</summary>
        public const byte LocalTee = 0x22;
        /// <summary>global.get (0x23).</summary>
        public const byte GlobalGet = 0x23;
        /// <summary>global.set (0x24).</summary>
        public const byte GlobalSet = 0x24;

        // === Memory Load ===
        /// <summary>i32.load (0x28).</summary>
        public const byte I32Load = 0x28;
        /// <summary>i64.load (0x29).</summary>
        public const byte I64Load = 0x29;
        /// <summary>f32.load (0x2A).</summary>
        public const byte F32Load = 0x2A;
        /// <summary>f64.load (0x2B).</summary>
        public const byte F64Load = 0x2B;
        /// <summary>i32.load8_s (0x2C).</summary>
        public const byte I32Load8S = 0x2C;
        /// <summary>i32.load8_u (0x2D).</summary>
        public const byte I32Load8U = 0x2D;
        /// <summary>i32.load16_s (0x2E).</summary>
        public const byte I32Load16S = 0x2E;
        /// <summary>i32.load16_u (0x2F).</summary>
        public const byte I32Load16U = 0x2F;
        /// <summary>i64.load8_s (0x30).</summary>
        public const byte I64Load8S = 0x30;
        /// <summary>i64.load8_u (0x31).</summary>
        public const byte I64Load8U = 0x31;
        /// <summary>i64.load16_s (0x32).</summary>
        public const byte I64Load16S = 0x32;
        /// <summary>i64.load16_u (0x33).</summary>
        public const byte I64Load16U = 0x33;
        /// <summary>i64.load32_s (0x34).</summary>
        public const byte I64Load32S = 0x34;
        /// <summary>i64.load32_u (0x35).</summary>
        public const byte I64Load32U = 0x35;

        // === Memory Store ===
        /// <summary>i32.store (0x36).</summary>
        public const byte I32Store = 0x36;
        /// <summary>i64.store (0x37).</summary>
        public const byte I64Store = 0x37;
        /// <summary>f32.store (0x38).</summary>
        public const byte F32Store = 0x38;
        /// <summary>f64.store (0x39).</summary>
        public const byte F64Store = 0x39;
        /// <summary>i32.store8 (0x3A).</summary>
        public const byte I32Store8 = 0x3A;
        /// <summary>i32.store16 (0x3B).</summary>
        public const byte I32Store16 = 0x3B;
        /// <summary>i64.store8 (0x3C).</summary>
        public const byte I64Store8 = 0x3C;
        /// <summary>i64.store16 (0x3D).</summary>
        public const byte I64Store16 = 0x3D;
        /// <summary>i64.store32 (0x3E).</summary>
        public const byte I64Store32 = 0x3E;

        // === Memory Control ===
        /// <summary>memory.size (0x3F).</summary>
        public const byte MemorySize = 0x3F;
        /// <summary>memory.grow (0x40).</summary>
        public const byte MemoryGrow = 0x40;

        // === Constants ===
        /// <summary>i32.const (0x41).</summary>
        public const byte I32Const = 0x41;
        /// <summary>i64.const (0x42).</summary>
        public const byte I64Const = 0x42;
        /// <summary>f32.const (0x43).</summary>
        public const byte F32Const = 0x43;
        /// <summary>f64.const (0x44).</summary>
        public const byte F64Const = 0x44;

        // === i32 Comparison ===
        /// <summary>i32.eqz (0x45).</summary>
        public const byte I32Eqz = 0x45;
        /// <summary>i32.eq (0x46).</summary>
        public const byte I32Eq = 0x46;
        /// <summary>i32.ne (0x47).</summary>
        public const byte I32Ne = 0x47;
        /// <summary>i32.lt_s (0x48).</summary>
        public const byte I32LtS = 0x48;
        /// <summary>i32.lt_u (0x49).</summary>
        public const byte I32LtU = 0x49;
        /// <summary>i32.gt_s (0x4A).</summary>
        public const byte I32GtS = 0x4A;
        /// <summary>i32.gt_u (0x4B).</summary>
        public const byte I32GtU = 0x4B;
        /// <summary>i32.le_s (0x4C).</summary>
        public const byte I32LeS = 0x4C;
        /// <summary>i32.le_u (0x4D).</summary>
        public const byte I32LeU = 0x4D;
        /// <summary>i32.ge_s (0x4E).</summary>
        public const byte I32GeS = 0x4E;
        /// <summary>i32.ge_u (0x4F).</summary>
        public const byte I32GeU = 0x4F;

        // === i64 Comparison ===
        /// <summary>i64.eqz (0x50).</summary>
        public const byte I64Eqz = 0x50;
        /// <summary>i64.eq (0x51).</summary>
        public const byte I64Eq = 0x51;
        /// <summary>i64.ne (0x52).</summary>
        public const byte I64Ne = 0x52;
        /// <summary>i64.lt_s (0x53).</summary>
        public const byte I64LtS = 0x53;
        /// <summary>i64.lt_u (0x54).</summary>
        public const byte I64LtU = 0x54;
        /// <summary>i64.gt_s (0x55).</summary>
        public const byte I64GtS = 0x55;
        /// <summary>i64.gt_u (0x56).</summary>
        public const byte I64GtU = 0x56;
        /// <summary>i64.le_s (0x57).</summary>
        public const byte I64LeS = 0x57;
        /// <summary>i64.le_u (0x58).</summary>
        public const byte I64LeU = 0x58;
        /// <summary>i64.ge_s (0x59).</summary>
        public const byte I64GeS = 0x59;
        /// <summary>i64.ge_u (0x5A).</summary>
        public const byte I64GeU = 0x5A;

        // === f32 Comparison ===
        /// <summary>f32.eq (0x5B).</summary>
        public const byte F32Eq = 0x5B;
        /// <summary>f32.ne (0x5C).</summary>
        public const byte F32Ne = 0x5C;
        /// <summary>f32.lt (0x5D).</summary>
        public const byte F32Lt = 0x5D;
        /// <summary>f32.gt (0x5E).</summary>
        public const byte F32Gt = 0x5E;
        /// <summary>f32.le (0x5F).</summary>
        public const byte F32Le = 0x5F;
        /// <summary>f32.ge (0x60).</summary>
        public const byte F32Ge = 0x60;

        // === f64 Comparison ===
        /// <summary>f64.eq (0x61).</summary>
        public const byte F64Eq = 0x61;
        /// <summary>f64.ne (0x62).</summary>
        public const byte F64Ne = 0x62;
        /// <summary>f64.lt (0x63).</summary>
        public const byte F64Lt = 0x63;
        /// <summary>f64.gt (0x64).</summary>
        public const byte F64Gt = 0x64;
        /// <summary>f64.le (0x65).</summary>
        public const byte F64Le = 0x65;
        /// <summary>f64.ge (0x66).</summary>
        public const byte F64Ge = 0x66;

        // === i32 Arithmetic ===
        /// <summary>i32.clz (0x67).</summary>
        public const byte I32Clz = 0x67;
        /// <summary>i32.ctz (0x68).</summary>
        public const byte I32Ctz = 0x68;
        /// <summary>i32.popcnt (0x69).</summary>
        public const byte I32Popcnt = 0x69;
        /// <summary>i32.add (0x6A).</summary>
        public const byte I32Add = 0x6A;
        /// <summary>i32.sub (0x6B).</summary>
        public const byte I32Sub = 0x6B;
        /// <summary>i32.mul (0x6C).</summary>
        public const byte I32Mul = 0x6C;
        /// <summary>i32.div_s (0x6D).</summary>
        public const byte I32DivS = 0x6D;
        /// <summary>i32.div_u (0x6E).</summary>
        public const byte I32DivU = 0x6E;
        /// <summary>i32.rem_s (0x6F).</summary>
        public const byte I32RemS = 0x6F;
        /// <summary>i32.rem_u (0x70).</summary>
        public const byte I32RemU = 0x70;
        /// <summary>i32.and (0x71).</summary>
        public const byte I32And = 0x71;
        /// <summary>i32.or (0x72).</summary>
        public const byte I32Or = 0x72;
        /// <summary>i32.xor (0x73).</summary>
        public const byte I32Xor = 0x73;
        /// <summary>i32.shl (0x74).</summary>
        public const byte I32Shl = 0x74;
        /// <summary>i32.shr_s (0x75).</summary>
        public const byte I32ShrS = 0x75;
        /// <summary>i32.shr_u (0x76).</summary>
        public const byte I32ShrU = 0x76;
        /// <summary>i32.rotl (0x77).</summary>
        public const byte I32Rotl = 0x77;
        /// <summary>i32.rotr (0x78).</summary>
        public const byte I32Rotr = 0x78;

        // === i64 Arithmetic ===
        /// <summary>i64.clz (0x79).</summary>
        public const byte I64Clz = 0x79;
        /// <summary>i64.ctz (0x7A).</summary>
        public const byte I64Ctz = 0x7A;
        /// <summary>i64.popcnt (0x7B).</summary>
        public const byte I64Popcnt = 0x7B;
        /// <summary>i64.add (0x7C).</summary>
        public const byte I64Add = 0x7C;
        /// <summary>i64.sub (0x7D).</summary>
        public const byte I64Sub = 0x7D;
        /// <summary>i64.mul (0x7E).</summary>
        public const byte I64Mul = 0x7E;
        /// <summary>i64.div_s (0x7F).</summary>
        public const byte I64DivS = 0x7F;
        /// <summary>i64.div_u (0x80).</summary>
        public const byte I64DivU = 0x80;
        /// <summary>i64.rem_s (0x81).</summary>
        public const byte I64RemS = 0x81;
        /// <summary>i64.rem_u (0x82).</summary>
        public const byte I64RemU = 0x82;
        /// <summary>i64.and (0x83).</summary>
        public const byte I64And = 0x83;
        /// <summary>i64.or (0x84).</summary>
        public const byte I64Or = 0x84;
        /// <summary>i64.xor (0x85).</summary>
        public const byte I64Xor = 0x85;
        /// <summary>i64.shl (0x86).</summary>
        public const byte I64Shl = 0x86;
        /// <summary>i64.shr_s (0x87).</summary>
        public const byte I64ShrS = 0x87;
        /// <summary>i64.shr_u (0x88).</summary>
        public const byte I64ShrU = 0x88;
        /// <summary>i64.rotl (0x89).</summary>
        public const byte I64Rotl = 0x89;
        /// <summary>i64.rotr (0x8A).</summary>
        public const byte I64Rotr = 0x8A;

        // === f32 Arithmetic ===
        /// <summary>f32.abs (0x8B).</summary>
        public const byte F32Abs = 0x8B;
        /// <summary>f32.neg (0x8C).</summary>
        public const byte F32Neg = 0x8C;
        /// <summary>f32.ceil (0x8D).</summary>
        public const byte F32Ceil = 0x8D;
        /// <summary>f32.floor (0x8E).</summary>
        public const byte F32Floor = 0x8E;
        /// <summary>f32.trunc (0x8F).</summary>
        public const byte F32Trunc = 0x8F;
        /// <summary>f32.nearest (0x90).</summary>
        public const byte F32Nearest = 0x90;
        /// <summary>f32.sqrt (0x91).</summary>
        public const byte F32Sqrt = 0x91;
        /// <summary>f32.add (0x92).</summary>
        public const byte F32Add = 0x92;
        /// <summary>f32.sub (0x93).</summary>
        public const byte F32Sub = 0x93;
        /// <summary>f32.mul (0x94).</summary>
        public const byte F32Mul = 0x94;
        /// <summary>f32.div (0x95).</summary>
        public const byte F32Div = 0x95;
        /// <summary>f32.min (0x96).</summary>
        public const byte F32Min = 0x96;
        /// <summary>f32.max (0x97).</summary>
        public const byte F32Max = 0x97;
        /// <summary>f32.copysign (0x98).</summary>
        public const byte F32Copysign = 0x98;

        // === f64 Arithmetic ===
        /// <summary>f64.abs (0x99).</summary>
        public const byte F64Abs = 0x99;
        /// <summary>f64.neg (0x9A).</summary>
        public const byte F64Neg = 0x9A;
        /// <summary>f64.ceil (0x9B).</summary>
        public const byte F64Ceil = 0x9B;
        /// <summary>f64.floor (0x9C).</summary>
        public const byte F64Floor = 0x9C;
        /// <summary>f64.trunc (0x9D).</summary>
        public const byte F64Trunc = 0x9D;
        /// <summary>f64.nearest (0x9E).</summary>
        public const byte F64Nearest = 0x9E;
        /// <summary>f64.sqrt (0x9F).</summary>
        public const byte F64Sqrt = 0x9F;
        /// <summary>f64.add (0xA0).</summary>
        public const byte F64Add = 0xA0;
        /// <summary>f64.sub (0xA1).</summary>
        public const byte F64Sub = 0xA1;
        /// <summary>f64.mul (0xA2).</summary>
        public const byte F64Mul = 0xA2;
        /// <summary>f64.div (0xA3).</summary>
        public const byte F64Div = 0xA3;
        /// <summary>f64.min (0xA4).</summary>
        public const byte F64Min = 0xA4;
        /// <summary>f64.max (0xA5).</summary>
        public const byte F64Max = 0xA5;
        /// <summary>f64.copysign (0xA6).</summary>
        public const byte F64Copysign = 0xA6;

        // === Conversions ===
        /// <summary>i32.wrap_i64 (0xA7).</summary>
        public const byte I32WrapI64 = 0xA7;
        /// <summary>i32.trunc_f32_s (0xA8). Traps on NaN or overflow.</summary>
        public const byte I32TruncF32S = 0xA8;
        /// <summary>i32.trunc_f32_u (0xA9). Traps on NaN or overflow.</summary>
        public const byte I32TruncF32U = 0xA9;
        /// <summary>i32.trunc_f64_s (0xAA). Traps on NaN or overflow.</summary>
        public const byte I32TruncF64S = 0xAA;
        /// <summary>i32.trunc_f64_u (0xAB). Traps on NaN or overflow.</summary>
        public const byte I32TruncF64U = 0xAB;
        /// <summary>i64.extend_i32_s (0xAC).</summary>
        public const byte I64ExtendI32S = 0xAC;
        /// <summary>i64.extend_i32_u (0xAD).</summary>
        public const byte I64ExtendI32U = 0xAD;
        /// <summary>i64.trunc_f32_s (0xAE). Traps on NaN or overflow.</summary>
        public const byte I64TruncF32S = 0xAE;
        /// <summary>i64.trunc_f32_u (0xAF). Traps on NaN or overflow.</summary>
        public const byte I64TruncF32U = 0xAF;
        /// <summary>i64.trunc_f64_s (0xB0). Traps on NaN or overflow.</summary>
        public const byte I64TruncF64S = 0xB0;
        /// <summary>i64.trunc_f64_u (0xB1). Traps on NaN or overflow.</summary>
        public const byte I64TruncF64U = 0xB1;
        /// <summary>f32.convert_i32_s (0xB2).</summary>
        public const byte F32ConvertI32S = 0xB2;
        /// <summary>f32.convert_i32_u (0xB3).</summary>
        public const byte F32ConvertI32U = 0xB3;
        /// <summary>f32.convert_i64_s (0xB4).</summary>
        public const byte F32ConvertI64S = 0xB4;
        /// <summary>f32.convert_i64_u (0xB5).</summary>
        public const byte F32ConvertI64U = 0xB5;
        /// <summary>f32.demote_f64 (0xB6).</summary>
        public const byte F32DemoteF64 = 0xB6;
        /// <summary>f64.convert_i32_s (0xB7).</summary>
        public const byte F64ConvertI32S = 0xB7;
        /// <summary>f64.convert_i32_u (0xB8).</summary>
        public const byte F64ConvertI32U = 0xB8;
        /// <summary>f64.convert_i64_s (0xB9).</summary>
        public const byte F64ConvertI64S = 0xB9;
        /// <summary>f64.convert_i64_u (0xBA).</summary>
        public const byte F64ConvertI64U = 0xBA;
        /// <summary>f64.promote_f32 (0xBB).</summary>
        public const byte F64PromoteF32 = 0xBB;
        /// <summary>i32.reinterpret_f32 (0xBC).</summary>
        public const byte I32ReinterpretF32 = 0xBC;
        /// <summary>i64.reinterpret_f64 (0xBD).</summary>
        public const byte I64ReinterpretF64 = 0xBD;
        /// <summary>f32.reinterpret_i32 (0xBE).</summary>
        public const byte F32ReinterpretI32 = 0xBE;
        /// <summary>f64.reinterpret_i64 (0xBF).</summary>
        public const byte F64ReinterpretI64 = 0xBF;

        // === Sign Extension ===
        /// <summary>i32.extend8_s (0xC0).</summary>
        public const byte I32Extend8S = 0xC0;
        /// <summary>i32.extend16_s (0xC1).</summary>
        public const byte I32Extend16S = 0xC1;
        /// <summary>i64.extend8_s (0xC2).</summary>
        public const byte I64Extend8S = 0xC2;
        /// <summary>i64.extend16_s (0xC3).</summary>
        public const byte I64Extend16S = 0xC3;
        /// <summary>i64.extend32_s (0xC4).</summary>
        public const byte I64Extend32S = 0xC4;

        // === Atomic Instructions (0xFE prefix) ===
        // Ref: https://webassembly.github.io/threads/core/binary/instructions.html
        /// <summary>
        /// Prefix byte (0xFE) for the threads/atomics instructions. The atomic sub-opcode
        /// constants below follow this prefix.
        /// </summary>
        public const byte AtomicPrefix = 0xFE;

        // Atomic memory control
        /// <summary>memory.atomic.notify (0xFE 0x00).</summary>
        public const byte MemoryAtomicNotify = 0x00;    // memory.atomic.notify
        /// <summary>memory.atomic.wait32 (0xFE 0x01).</summary>
        public const byte MemoryAtomicWait32 = 0x01;    // memory.atomic.wait32
        /// <summary>memory.atomic.wait64 (0xFE 0x02).</summary>
        public const byte MemoryAtomicWait64 = 0x02;    // memory.atomic.wait64
        /// <summary>atomic.fence (0xFE 0x03).</summary>
        public const byte AtomicFence = 0x03;            // atomic.fence

        // Atomic loads
        /// <summary>i32.atomic.load (0xFE 0x10).</summary>
        public const byte I32AtomicLoad = 0x10;          // i32.atomic.load
        /// <summary>i64.atomic.load (0xFE 0x11).</summary>
        public const byte I64AtomicLoad = 0x11;          // i64.atomic.load
        /// <summary>i32.atomic.load8_u (0xFE 0x12).</summary>
        public const byte I32AtomicLoad8U = 0x12;        // i32.atomic.load8_u
        /// <summary>i32.atomic.load16_u (0xFE 0x13).</summary>
        public const byte I32AtomicLoad16U = 0x13;       // i32.atomic.load16_u
        /// <summary>i64.atomic.load8_u (0xFE 0x14).</summary>
        public const byte I64AtomicLoad8U = 0x14;        // i64.atomic.load8_u
        /// <summary>i64.atomic.load16_u (0xFE 0x15).</summary>
        public const byte I64AtomicLoad16U = 0x15;       // i64.atomic.load16_u
        /// <summary>i64.atomic.load32_u (0xFE 0x16).</summary>
        public const byte I64AtomicLoad32U = 0x16;       // i64.atomic.load32_u

        // Atomic stores
        /// <summary>i32.atomic.store (0xFE 0x17).</summary>
        public const byte I32AtomicStore = 0x17;         // i32.atomic.store
        /// <summary>i64.atomic.store (0xFE 0x18).</summary>
        public const byte I64AtomicStore = 0x18;         // i64.atomic.store
        /// <summary>i32.atomic.store8 (0xFE 0x19).</summary>
        public const byte I32AtomicStore8 = 0x19;        // i32.atomic.store8
        /// <summary>i32.atomic.store16 (0xFE 0x1A).</summary>
        public const byte I32AtomicStore16 = 0x1A;       // i32.atomic.store16
        /// <summary>i64.atomic.store8 (0xFE 0x1B).</summary>
        public const byte I64AtomicStore8 = 0x1B;        // i64.atomic.store8
        /// <summary>i64.atomic.store16 (0xFE 0x1C).</summary>
        public const byte I64AtomicStore16 = 0x1C;       // i64.atomic.store16
        /// <summary>i64.atomic.store32 (0xFE 0x1D).</summary>
        public const byte I64AtomicStore32 = 0x1D;       // i64.atomic.store32

        // Atomic RMW — Add (0x1E-0x24)
        /// <summary>i32.atomic.rmw.add (0xFE 0x1E).</summary>
        public const byte I32AtomicRmwAdd = 0x1E;        // i32.atomic.rmw.add
        /// <summary>i64.atomic.rmw.add (0xFE 0x1F).</summary>
        public const byte I64AtomicRmwAdd = 0x1F;        // i64.atomic.rmw.add
        /// <summary>i32.atomic.rmw8.add_u (0xFE 0x20).</summary>
        public const byte I32AtomicRmw8AddU = 0x20;      // i32.atomic.rmw8.add_u
        /// <summary>i32.atomic.rmw16.add_u (0xFE 0x21).</summary>
        public const byte I32AtomicRmw16AddU = 0x21;     // i32.atomic.rmw16.add_u
        /// <summary>i64.atomic.rmw8.add_u (0xFE 0x22).</summary>
        public const byte I64AtomicRmw8AddU = 0x22;      // i64.atomic.rmw8.add_u
        /// <summary>i64.atomic.rmw16.add_u (0xFE 0x23).</summary>
        public const byte I64AtomicRmw16AddU = 0x23;     // i64.atomic.rmw16.add_u
        /// <summary>i64.atomic.rmw32.add_u (0xFE 0x24).</summary>
        public const byte I64AtomicRmw32AddU = 0x24;     // i64.atomic.rmw32.add_u

        // Atomic RMW — Sub (0x25-0x2B)
        /// <summary>i32.atomic.rmw.sub (0xFE 0x25).</summary>
        public const byte I32AtomicRmwSub = 0x25;        // i32.atomic.rmw.sub
        /// <summary>i64.atomic.rmw.sub (0xFE 0x26).</summary>
        public const byte I64AtomicRmwSub = 0x26;        // i64.atomic.rmw.sub
        /// <summary>i32.atomic.rmw8.sub_u (0xFE 0x27).</summary>
        public const byte I32AtomicRmw8SubU = 0x27;      // i32.atomic.rmw8.sub_u
        /// <summary>i32.atomic.rmw16.sub_u (0xFE 0x28).</summary>
        public const byte I32AtomicRmw16SubU = 0x28;     // i32.atomic.rmw16.sub_u
        /// <summary>i64.atomic.rmw8.sub_u (0xFE 0x29).</summary>
        public const byte I64AtomicRmw8SubU = 0x29;      // i64.atomic.rmw8.sub_u
        /// <summary>i64.atomic.rmw16.sub_u (0xFE 0x2A).</summary>
        public const byte I64AtomicRmw16SubU = 0x2A;     // i64.atomic.rmw16.sub_u
        /// <summary>i64.atomic.rmw32.sub_u (0xFE 0x2B).</summary>
        public const byte I64AtomicRmw32SubU = 0x2B;     // i64.atomic.rmw32.sub_u

        // Atomic RMW — And (0x2C-0x32)
        /// <summary>i32.atomic.rmw.and (0xFE 0x2C).</summary>
        public const byte I32AtomicRmwAnd = 0x2C;        // i32.atomic.rmw.and
        /// <summary>i64.atomic.rmw.and (0xFE 0x2D).</summary>
        public const byte I64AtomicRmwAnd = 0x2D;        // i64.atomic.rmw.and
        /// <summary>i32.atomic.rmw8.and_u (0xFE 0x2E).</summary>
        public const byte I32AtomicRmw8AndU = 0x2E;      // i32.atomic.rmw8.and_u
        /// <summary>i32.atomic.rmw16.and_u (0xFE 0x2F).</summary>
        public const byte I32AtomicRmw16AndU = 0x2F;     // i32.atomic.rmw16.and_u
        /// <summary>i64.atomic.rmw8.and_u (0xFE 0x30).</summary>
        public const byte I64AtomicRmw8AndU = 0x30;      // i64.atomic.rmw8.and_u
        /// <summary>i64.atomic.rmw16.and_u (0xFE 0x31).</summary>
        public const byte I64AtomicRmw16AndU = 0x31;     // i64.atomic.rmw16.and_u
        /// <summary>i64.atomic.rmw32.and_u (0xFE 0x32).</summary>
        public const byte I64AtomicRmw32AndU = 0x32;     // i64.atomic.rmw32.and_u

        // Atomic RMW — Or (0x33-0x39)
        /// <summary>i32.atomic.rmw.or (0xFE 0x33).</summary>
        public const byte I32AtomicRmwOr = 0x33;         // i32.atomic.rmw.or
        /// <summary>i64.atomic.rmw.or (0xFE 0x34).</summary>
        public const byte I64AtomicRmwOr = 0x34;         // i64.atomic.rmw.or
        /// <summary>i32.atomic.rmw8.or_u (0xFE 0x35).</summary>
        public const byte I32AtomicRmw8OrU = 0x35;       // i32.atomic.rmw8.or_u
        /// <summary>i32.atomic.rmw16.or_u (0xFE 0x36).</summary>
        public const byte I32AtomicRmw16OrU = 0x36;      // i32.atomic.rmw16.or_u
        /// <summary>i64.atomic.rmw8.or_u (0xFE 0x37).</summary>
        public const byte I64AtomicRmw8OrU = 0x37;       // i64.atomic.rmw8.or_u
        /// <summary>i64.atomic.rmw16.or_u (0xFE 0x38).</summary>
        public const byte I64AtomicRmw16OrU = 0x38;      // i64.atomic.rmw16.or_u
        /// <summary>i64.atomic.rmw32.or_u (0xFE 0x39).</summary>
        public const byte I64AtomicRmw32OrU = 0x39;      // i64.atomic.rmw32.or_u

        // Atomic RMW — Xor (0x3A-0x40)
        /// <summary>i32.atomic.rmw.xor (0xFE 0x3A).</summary>
        public const byte I32AtomicRmwXor = 0x3A;        // i32.atomic.rmw.xor
        /// <summary>i64.atomic.rmw.xor (0xFE 0x3B).</summary>
        public const byte I64AtomicRmwXor = 0x3B;        // i64.atomic.rmw.xor
        /// <summary>i32.atomic.rmw8.xor_u (0xFE 0x3C).</summary>
        public const byte I32AtomicRmw8XorU = 0x3C;      // i32.atomic.rmw8.xor_u
        /// <summary>i32.atomic.rmw16.xor_u (0xFE 0x3D).</summary>
        public const byte I32AtomicRmw16XorU = 0x3D;     // i32.atomic.rmw16.xor_u
        /// <summary>i64.atomic.rmw8.xor_u (0xFE 0x3E).</summary>
        public const byte I64AtomicRmw8XorU = 0x3E;      // i64.atomic.rmw8.xor_u
        /// <summary>i64.atomic.rmw16.xor_u (0xFE 0x3F).</summary>
        public const byte I64AtomicRmw16XorU = 0x3F;     // i64.atomic.rmw16.xor_u
        /// <summary>i64.atomic.rmw32.xor_u (0xFE 0x40).</summary>
        public const byte I64AtomicRmw32XorU = 0x40;     // i64.atomic.rmw32.xor_u

        // Atomic RMW — Xchg (0x41-0x47)
        /// <summary>i32.atomic.rmw.xchg (0xFE 0x41).</summary>
        public const byte I32AtomicRmwXchg = 0x41;       // i32.atomic.rmw.xchg
        /// <summary>i64.atomic.rmw.xchg (0xFE 0x42).</summary>
        public const byte I64AtomicRmwXchg = 0x42;       // i64.atomic.rmw.xchg
        /// <summary>i32.atomic.rmw8.xchg_u (0xFE 0x43).</summary>
        public const byte I32AtomicRmw8XchgU = 0x43;     // i32.atomic.rmw8.xchg_u
        /// <summary>i32.atomic.rmw16.xchg_u (0xFE 0x44).</summary>
        public const byte I32AtomicRmw16XchgU = 0x44;    // i32.atomic.rmw16.xchg_u
        /// <summary>i64.atomic.rmw8.xchg_u (0xFE 0x45).</summary>
        public const byte I64AtomicRmw8XchgU = 0x45;     // i64.atomic.rmw8.xchg_u
        /// <summary>i64.atomic.rmw16.xchg_u (0xFE 0x46).</summary>
        public const byte I64AtomicRmw16XchgU = 0x46;    // i64.atomic.rmw16.xchg_u
        /// <summary>i64.atomic.rmw32.xchg_u (0xFE 0x47).</summary>
        public const byte I64AtomicRmw32XchgU = 0x47;    // i64.atomic.rmw32.xchg_u

        // Atomic RMW — CmpXchg (0x48-0x4E)
        /// <summary>i32.atomic.rmw.cmpxchg (0xFE 0x48).</summary>
        public const byte I32AtomicRmwCmpxchg = 0x48;    // i32.atomic.rmw.cmpxchg
        /// <summary>i64.atomic.rmw.cmpxchg (0xFE 0x49).</summary>
        public const byte I64AtomicRmwCmpxchg = 0x49;    // i64.atomic.rmw.cmpxchg
        /// <summary>i32.atomic.rmw8.cmpxchg_u (0xFE 0x4A).</summary>
        public const byte I32AtomicRmw8CmpxchgU = 0x4A;  // i32.atomic.rmw8.cmpxchg_u
        /// <summary>i32.atomic.rmw16.cmpxchg_u (0xFE 0x4B).</summary>
        public const byte I32AtomicRmw16CmpxchgU = 0x4B; // i32.atomic.rmw16.cmpxchg_u
        /// <summary>i64.atomic.rmw8.cmpxchg_u (0xFE 0x4C).</summary>
        public const byte I64AtomicRmw8CmpxchgU = 0x4C;  // i64.atomic.rmw8.cmpxchg_u
        /// <summary>i64.atomic.rmw16.cmpxchg_u (0xFE 0x4D).</summary>
        public const byte I64AtomicRmw16CmpxchgU = 0x4D; // i64.atomic.rmw16.cmpxchg_u
        /// <summary>i64.atomic.rmw32.cmpxchg_u (0xFE 0x4E).</summary>
        public const byte I64AtomicRmw32CmpxchgU = 0x4E; // i64.atomic.rmw32.cmpxchg_u

        // === SIMD Instructions (0xFD prefix) ===
        // Ref: https://github.com/WebAssembly/simd/blob/main/proposals/simd/BinarySIMD.md
        // (opcode values verified against the spec table 2026-06-14).
        // CRITICAL: unlike the atomic 0xFE sub-opcodes (single byte), the SIMD sub-opcode is a
        // u32 LEB128 after the 0xFD prefix — values >= 128 (e.g. f32x4.add = 228) encode to TWO
        // LEB bytes (0xE4 0x01). These constants are the RAW decimal sub-opcode; always emit them
        // through EmitU32Leb128, never as a raw byte. Stored as uint because core SIMD opcodes
        // exceed 255 (and to make the LEB requirement explicit at the call site).
        /// <summary>
        /// Prefix byte (0xFD) for the SIMD128 instructions. The SIMD sub-opcode constants below
        /// follow this prefix as a u32 LEB128, not a single byte.
        /// </summary>
        public const byte SimdPrefix = 0xFD;

        // Memory (take a memarg: align + offset)
        /// <summary>v128.load (0xFD 0).</summary>
        public const uint V128Load = 0;          // v128.load
        /// <summary>v128.store (0xFD 11).</summary>
        public const uint V128Store = 11;        // v128.store
        // Const (16 immediate bytes) / shuffle (16 lane-index bytes)
        /// <summary>v128.const (0xFD 12).</summary>
        public const uint V128Const = 12;        // v128.const
        /// <summary>i8x16.shuffle (0xFD 13).</summary>
        public const uint I8x16Shuffle = 13;     // i8x16.shuffle
        // Splat (lane -> all lanes)
        /// <summary>i8x16.splat (0xFD 15).</summary>
        public const uint I8x16Splat = 15;       // i8x16.splat
        /// <summary>i16x8.splat (0xFD 16).</summary>
        public const uint I16x8Splat = 16;       // i16x8.splat
        /// <summary>i32x4.splat (0xFD 17).</summary>
        public const uint I32x4Splat = 17;       // i32x4.splat
        /// <summary>i64x2.splat (0xFD 18).</summary>
        public const uint I64x2Splat = 18;       // i64x2.splat
        /// <summary>f32x4.splat (0xFD 19).</summary>
        public const uint F32x4Splat = 19;       // f32x4.splat
        /// <summary>f64x2.splat (0xFD 20).</summary>
        public const uint F64x2Splat = 20;       // f64x2.splat
        // Extract/replace lane (take a 1-byte lane index immediate)
        /// <summary>i32x4.extract_lane (0xFD 27).</summary>
        public const uint I32x4ExtractLane = 27; // i32x4.extract_lane
        /// <summary>i32x4.replace_lane (0xFD 28).</summary>
        public const uint I32x4ReplaceLane = 28; // i32x4.replace_lane
        /// <summary>i64x2.extract_lane (0xFD 29).</summary>
        public const uint I64x2ExtractLane = 29; // i64x2.extract_lane
        /// <summary>i64x2.replace_lane (0xFD 30).</summary>
        public const uint I64x2ReplaceLane = 30; // i64x2.replace_lane
        /// <summary>f32x4.extract_lane (0xFD 31).</summary>
        public const uint F32x4ExtractLane = 31; // f32x4.extract_lane
        /// <summary>f32x4.replace_lane (0xFD 32).</summary>
        public const uint F32x4ReplaceLane = 32; // f32x4.replace_lane
        /// <summary>f64x2.extract_lane (0xFD 33).</summary>
        public const uint F64x2ExtractLane = 33; // f64x2.extract_lane
        /// <summary>f64x2.replace_lane (0xFD 34).</summary>
        public const uint F64x2ReplaceLane = 34; // f64x2.replace_lane
        // Bitwise (whole-vector)
        /// <summary>v128.not (0xFD 77).</summary>
        public const uint V128Not = 77;          // v128.not
        /// <summary>v128.and (0xFD 78).</summary>
        public const uint V128And = 78;          // v128.and
        /// <summary>v128.andnot (0xFD 79).</summary>
        public const uint V128AndNot = 79;       // v128.andnot
        /// <summary>v128.or (0xFD 80).</summary>
        public const uint V128Or = 80;           // v128.or
        /// <summary>v128.xor (0xFD 81).</summary>
        public const uint V128Xor = 81;          // v128.xor
        /// <summary>v128.bitselect (0xFD 82).</summary>
        public const uint V128Bitselect = 82;    // v128.bitselect (a, b, mask) -> per-bit select
        // i32x4 comparisons (each lane → all-ones / all-zeros mask for v128.bitselect)
        /// <summary>i32x4.eq (0xFD 55).</summary>
        public const uint I32x4Eq = 55;          // i32x4.eq
        /// <summary>i32x4.ne (0xFD 56).</summary>
        public const uint I32x4Ne = 56;          // i32x4.ne
        /// <summary>i32x4.lt_s (0xFD 57).</summary>
        public const uint I32x4LtS = 57;         // i32x4.lt_s
        /// <summary>i32x4.lt_u (0xFD 58).</summary>
        public const uint I32x4LtU = 58;         // i32x4.lt_u
        /// <summary>i32x4.gt_s (0xFD 59).</summary>
        public const uint I32x4GtS = 59;         // i32x4.gt_s
        /// <summary>i32x4.gt_u (0xFD 60).</summary>
        public const uint I32x4GtU = 60;         // i32x4.gt_u
        /// <summary>i32x4.le_s (0xFD 61).</summary>
        public const uint I32x4LeS = 61;         // i32x4.le_s
        /// <summary>i32x4.le_u (0xFD 62).</summary>
        public const uint I32x4LeU = 62;         // i32x4.le_u
        /// <summary>i32x4.ge_s (0xFD 63).</summary>
        public const uint I32x4GeS = 63;         // i32x4.ge_s
        /// <summary>i32x4.ge_u (0xFD 64).</summary>
        public const uint I32x4GeU = 64;         // i32x4.ge_u
        // f32x4 comparisons
        /// <summary>f32x4.eq (0xFD 65).</summary>
        public const uint F32x4Eq = 65;          // f32x4.eq
        /// <summary>f32x4.ne (0xFD 66).</summary>
        public const uint F32x4Ne = 66;          // f32x4.ne
        /// <summary>f32x4.lt (0xFD 67).</summary>
        public const uint F32x4Lt = 67;          // f32x4.lt
        /// <summary>f32x4.gt (0xFD 68).</summary>
        public const uint F32x4Gt = 68;          // f32x4.gt
        /// <summary>f32x4.le (0xFD 69).</summary>
        public const uint F32x4Le = 69;          // f32x4.le
        /// <summary>f32x4.ge (0xFD 70).</summary>
        public const uint F32x4Ge = 70;          // f32x4.ge
        // i32x4 arithmetic
        /// <summary>i32x4.abs (0xFD 160).</summary>
        public const uint I32x4Abs = 160;        // i32x4.abs
        /// <summary>i32x4.neg (0xFD 161).</summary>
        public const uint I32x4Neg = 161;        // i32x4.neg
        /// <summary>i32x4.shl (0xFD 171).</summary>
        public const uint I32x4Shl = 171;        // i32x4.shl   (v128, i32 count) -> v128
        /// <summary>i32x4.shr_s (0xFD 172).</summary>
        public const uint I32x4ShrS = 172;       // i32x4.shr_s (v128, i32 count) -> v128
        /// <summary>i32x4.shr_u (0xFD 173).</summary>
        public const uint I32x4ShrU = 173;       // i32x4.shr_u (v128, i32 count) -> v128
        /// <summary>i32x4.add (0xFD 174).</summary>
        public const uint I32x4Add = 174;        // i32x4.add
        /// <summary>i32x4.sub (0xFD 177).</summary>
        public const uint I32x4Sub = 177;        // i32x4.sub
        /// <summary>i32x4.mul (0xFD 181).</summary>
        public const uint I32x4Mul = 181;        // i32x4.mul
        /// <summary>i32x4.min_s (0xFD 182).</summary>
        public const uint I32x4MinS = 182;       // i32x4.min_s
        /// <summary>i32x4.min_u (0xFD 183).</summary>
        public const uint I32x4MinU = 183;       // i32x4.min_u
        /// <summary>i32x4.max_s (0xFD 184).</summary>
        public const uint I32x4MaxS = 184;       // i32x4.max_s
        /// <summary>i32x4.max_u (0xFD 185).</summary>
        public const uint I32x4MaxU = 185;       // i32x4.max_u
        // f32x4 rounding (IEEE round-to-integral — bit-identical to scalar f32.ceil/floor)
        /// <summary>f32x4.ceil (0xFD 103).</summary>
        public const uint F32x4Ceil = 103;       // f32x4.ceil
        /// <summary>f32x4.floor (0xFD 104).</summary>
        public const uint F32x4Floor = 104;      // f32x4.floor
        /// <summary>f32x4.trunc (0xFD 105).</summary>
        public const uint F32x4Trunc = 105;      // f32x4.trunc
        /// <summary>f32x4.nearest (0xFD 106).</summary>
        public const uint F32x4Nearest = 106;    // f32x4.nearest
        // f32x4 arithmetic
        /// <summary>f32x4.abs (0xFD 224).</summary>
        public const uint F32x4Abs = 224;        // f32x4.abs
        /// <summary>f32x4.neg (0xFD 225).</summary>
        public const uint F32x4Neg = 225;        // f32x4.neg
        /// <summary>f32x4.sqrt (0xFD 227).</summary>
        public const uint F32x4Sqrt = 227;       // f32x4.sqrt
        /// <summary>f32x4.add (0xFD 228).</summary>
        public const uint F32x4Add = 228;        // f32x4.add
        /// <summary>f32x4.sub (0xFD 229).</summary>
        public const uint F32x4Sub = 229;        // f32x4.sub
        /// <summary>f32x4.mul (0xFD 230).</summary>
        public const uint F32x4Mul = 230;        // f32x4.mul
        /// <summary>f32x4.div (0xFD 231).</summary>
        public const uint F32x4Div = 231;        // f32x4.div
        /// <summary>f32x4.min (0xFD 232).</summary>
        public const uint F32x4Min = 232;        // f32x4.min
        /// <summary>f32x4.max (0xFD 233).</summary>
        public const uint F32x4Max = 233;        // f32x4.max
        // Lane conversions (4-lane <-> 4-lane). NOTE: f32x4 -> i32x4 is ONLY the SATURATING
        // trunc_sat form (248/249); the scalar path uses non-saturating i32.trunc_f32_s (traps on
        // overflow/NaN), so vectorizing f32->i32 would break cross-mode determinism — deferred.
        // i32x4 -> f32x4 (250/251) rounds identically to the scalar f32.convert_i32_s/_u, so it is
        // cross-mode exact and safe to vectorize.
        /// <summary>i32x4.trunc_sat_f32x4_s (0xFD 248). Saturating, unlike the scalar i32.trunc_f32_s.</summary>
        public const uint I32x4TruncSatF32x4S = 248; // i32x4.trunc_sat_f32x4_s (saturating; deferred)
        /// <summary>i32x4.trunc_sat_f32x4_u (0xFD 249). Saturating, unlike the scalar i32.trunc_f32_u.</summary>
        public const uint I32x4TruncSatF32x4U = 249; // i32x4.trunc_sat_f32x4_u (saturating; deferred)
        /// <summary>f32x4.convert_i32x4_s (0xFD 250).</summary>
        public const uint F32x4ConvertI32x4S = 250;  // f32x4.convert_i32x4_s
        /// <summary>f32x4.convert_i32x4_u (0xFD 251).</summary>
        public const uint F32x4ConvertI32x4U = 251;  // f32x4.convert_i32x4_u
        // i64x2 comparisons (signed only in core SIMD; per-64-bit-lane all-ones/all-zeros mask)
        /// <summary>i64x2.eq (0xFD 214).</summary>
        public const uint I64x2Eq = 214;         // i64x2.eq
        /// <summary>i64x2.ne (0xFD 215).</summary>
        public const uint I64x2Ne = 215;         // i64x2.ne
        /// <summary>i64x2.lt_s (0xFD 216).</summary>
        public const uint I64x2LtS = 216;        // i64x2.lt_s
        /// <summary>i64x2.gt_s (0xFD 217).</summary>
        public const uint I64x2GtS = 217;        // i64x2.gt_s
        /// <summary>i64x2.le_s (0xFD 218).</summary>
        public const uint I64x2LeS = 218;        // i64x2.le_s
        /// <summary>i64x2.ge_s (0xFD 219).</summary>
        public const uint I64x2GeS = 219;        // i64x2.ge_s
        // f64x2 comparisons
        /// <summary>f64x2.eq (0xFD 71).</summary>
        public const uint F64x2Eq = 71;          // f64x2.eq
        /// <summary>f64x2.ne (0xFD 72).</summary>
        public const uint F64x2Ne = 72;          // f64x2.ne
        /// <summary>f64x2.lt (0xFD 73).</summary>
        public const uint F64x2Lt = 73;          // f64x2.lt
        /// <summary>f64x2.gt (0xFD 74).</summary>
        public const uint F64x2Gt = 74;          // f64x2.gt
        /// <summary>f64x2.le (0xFD 75).</summary>
        public const uint F64x2Le = 75;          // f64x2.le
        /// <summary>f64x2.ge (0xFD 76).</summary>
        public const uint F64x2Ge = 76;          // f64x2.ge
        // i64x2 arithmetic (2-lane, double-pumped; no SIMD i64 min/max/div)
        /// <summary>i64x2.neg (0xFD 193).</summary>
        public const uint I64x2Neg = 193;        // i64x2.neg
        /// <summary>i64x2.add (0xFD 206).</summary>
        public const uint I64x2Add = 206;        // i64x2.add
        /// <summary>i64x2.sub (0xFD 209).</summary>
        public const uint I64x2Sub = 209;        // i64x2.sub
        /// <summary>i64x2.mul (0xFD 213).</summary>
        public const uint I64x2Mul = 213;        // i64x2.mul
        // f64x2 arithmetic
        /// <summary>f64x2.abs (0xFD 236).</summary>
        public const uint F64x2Abs = 236;        // f64x2.abs
        /// <summary>f64x2.neg (0xFD 237).</summary>
        public const uint F64x2Neg = 237;        // f64x2.neg
        /// <summary>f64x2.sqrt (0xFD 239).</summary>
        public const uint F64x2Sqrt = 239;       // f64x2.sqrt
        /// <summary>f64x2.add (0xFD 240).</summary>
        public const uint F64x2Add = 240;        // f64x2.add
        /// <summary>f64x2.sub (0xFD 241).</summary>
        public const uint F64x2Sub = 241;        // f64x2.sub
        /// <summary>f64x2.mul (0xFD 242).</summary>
        public const uint F64x2Mul = 242;        // f64x2.mul
        /// <summary>f64x2.div (0xFD 243).</summary>
        public const uint F64x2Div = 243;        // f64x2.div
        /// <summary>f64x2.min (0xFD 244).</summary>
        public const uint F64x2Min = 244;        // f64x2.min
        /// <summary>f64x2.max (0xFD 245).</summary>
        public const uint F64x2Max = 245;        // f64x2.max

        // === Section IDs ===
        /// <summary>Module section id 0: custom section.</summary>
        public const byte SectionCustom = 0;
        /// <summary>Module section id 1: type section.</summary>
        public const byte SectionType = 1;
        /// <summary>Module section id 2: import section.</summary>
        public const byte SectionImport = 2;
        /// <summary>Module section id 3: function section.</summary>
        public const byte SectionFunction = 3;
        /// <summary>Module section id 4: table section.</summary>
        public const byte SectionTable = 4;
        /// <summary>Module section id 5: memory section.</summary>
        public const byte SectionMemory = 5;
        /// <summary>Module section id 6: global section.</summary>
        public const byte SectionGlobal = 6;
        /// <summary>Module section id 7: export section.</summary>
        public const byte SectionExport = 7;
        /// <summary>Module section id 8: start section.</summary>
        public const byte SectionStart = 8;
        /// <summary>Module section id 9: element section.</summary>
        public const byte SectionElement = 9;
        /// <summary>Module section id 10: code section.</summary>
        public const byte SectionCode = 10;
        /// <summary>Module section id 11: data section.</summary>
        public const byte SectionData = 11;
        /// <summary>Module section id 12: data count section.</summary>
        public const byte SectionDataCount = 12;

        // === Import/Export Kinds ===
        /// <summary>Import/export descriptor kind 0x00: function.</summary>
        public const byte ExternalFunc = 0x00;
        /// <summary>Import/export descriptor kind 0x01: table.</summary>
        public const byte ExternalTable = 0x01;
        /// <summary>Import/export descriptor kind 0x02: memory.</summary>
        public const byte ExternalMemory = 0x02;
        /// <summary>Import/export descriptor kind 0x03: global.</summary>
        public const byte ExternalGlobal = 0x03;

        // === Memory Limits Flags ===
        /// <summary>Limits flag 0x00: unshared, minimum only.</summary>
        public const byte LimitsNoMax = 0x00;
        /// <summary>Limits flag 0x01: unshared, minimum and maximum.</summary>
        public const byte LimitsWithMax = 0x01;
        /// <summary>Limits flag 0x02: shared, minimum only.</summary>
        public const byte LimitsSharedNoMax = 0x02;
        /// <summary>Limits flag 0x03: shared, minimum and maximum (threads proposal shared memory).</summary>
        public const byte LimitsSharedWithMax = 0x03;

        // === Function Type Tag ===
        /// <summary>Leading byte of a function type in the type section (0x60).</summary>
        public const byte FuncTypeTag = 0x60;
    }
}
