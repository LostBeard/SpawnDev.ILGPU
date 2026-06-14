// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmModuleBuilder.cs
//
// Builds valid WebAssembly binary modules directly from C#.
// Handles LEB128 encoding, section construction, and module emission.
// Reference: https://webassembly.github.io/spec/core/binary/
// ---------------------------------------------------------------------------------------

using System.Text;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// Represents a WebAssembly function type signature.
    /// </summary>
    public class WasmFuncType
    {
        public byte[] ParamTypes { get; set; } = Array.Empty<byte>();
        public byte[] ResultTypes { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Represents a local variable declaration in a function body.
    /// </summary>
    public class WasmLocal
    {
        public uint Count { get; set; }
        public byte Type { get; set; }
    }

    /// <summary>
    /// Represents a function body (locals + code).
    /// </summary>
    public class WasmFuncBody
    {
        public List<WasmLocal> Locals { get; set; } = new();
        public byte[] Code { get; set; } = Array.Empty<byte>();
    }

    /// <summary>
    /// Builds valid WebAssembly binary modules from structured data.
    /// Supports shared memory imports, function definitions, and exports.
    /// </summary>
    public class WasmModuleBuilder
    {
        private readonly List<WasmFuncType> _types = new();
        private readonly List<(string Module, string Name, byte Kind, object Details)> _imports = new();
        private readonly List<int> _funcTypeIndices = new();
        private readonly List<(string Name, byte Kind, uint Index)> _exports = new();
        private readonly List<WasmFuncBody> _bodies = new();

        // Track import counts by kind for export index calculation
        private int _importFuncCount = 0;

        /// <summary>
        /// Adds a function type and returns its index.
        /// </summary>
        public int AddFuncType(byte[] paramTypes, byte[] resultTypes)
        {
            _types.Add(new WasmFuncType { ParamTypes = paramTypes, ResultTypes = resultTypes });
            return _types.Count - 1;
        }

        /// <summary>
        /// Imports a shared memory.
        /// </summary>
        public void ImportSharedMemory(string module, string name, uint minPages, uint maxPages)
        {
            _imports.Add((module, name, WasmOpCodes.ExternalMemory,
                new MemoryImportDetails { MinPages = minPages, MaxPages = maxPages, Shared = true }));
        }

        /// <summary>
        /// Imports a function and returns its function index.
        /// Imported functions come before defined functions in the index space.
        /// </summary>
        public int ImportFunction(string module, string name, int typeIndex)
        {
            int funcIdx = _importFuncCount;
            _importFuncCount++;
            _imports.Add((module, name, WasmOpCodes.ExternalFunc,
                new FuncImportDetails { TypeIndex = typeIndex }));
            return funcIdx;
        }

        /// <summary>
        /// Adds a function with the given type index and returns its function index
        /// (accounting for imported functions).
        /// </summary>
        public int AddFunction(int typeIndex)
        {
            _funcTypeIndices.Add(typeIndex);
            int funcIndex = _importFuncCount + _funcTypeIndices.Count - 1;
            return funcIndex;
        }

        /// <summary>
        /// Exports a function by name.
        /// </summary>
        public void ExportFunction(string name, int funcIndex)
        {
            _exports.Add((name, WasmOpCodes.ExternalFunc, (uint)funcIndex));
        }

        /// <summary>
        /// Sets the body for a defined function (by its position in the defined functions list, 0-based).
        /// </summary>
        public void SetFunctionBody(int definedIndex, List<WasmLocal> locals, byte[] code)
        {
            while (_bodies.Count <= definedIndex)
                _bodies.Add(new WasmFuncBody());
            _bodies[definedIndex] = new WasmFuncBody { Locals = locals, Code = code };
        }

        /// <summary>
        /// Emits the complete WebAssembly binary module.
        /// </summary>
        public byte[] Emit()
        {
            using var ms = new MemoryStream();

            // Magic number + version
            ms.Write(new byte[] { 0x00, 0x61, 0x73, 0x6D }); // \0asm
            ms.Write(new byte[] { 0x01, 0x00, 0x00, 0x00 }); // version 1

            // Section 1: Type
            if (_types.Count > 0)
                WriteSection(ms, WasmOpCodes.SectionType, WriteTypeSection);

            // Section 2: Import
            if (_imports.Count > 0)
                WriteSection(ms, WasmOpCodes.SectionImport, WriteImportSection);

            // Section 3: Function
            if (_funcTypeIndices.Count > 0)
                WriteSection(ms, WasmOpCodes.SectionFunction, WriteFunctionSection);

            // Section 7: Export
            if (_exports.Count > 0)
                WriteSection(ms, WasmOpCodes.SectionExport, WriteExportSection);

            // Section 10: Code
            if (_bodies.Count > 0)
                WriteSection(ms, WasmOpCodes.SectionCode, WriteCodeSection);

            return ms.ToArray();
        }

        #region Section Writers

        private void WriteTypeSection(MemoryStream s)
        {
            WriteU32Leb128(s, (uint)_types.Count);
            foreach (var type in _types)
            {
                s.WriteByte(WasmOpCodes.FuncTypeTag); // 0x60
                WriteU32Leb128(s, (uint)type.ParamTypes.Length);
                s.Write(type.ParamTypes);
                WriteU32Leb128(s, (uint)type.ResultTypes.Length);
                s.Write(type.ResultTypes);
            }
        }

        private void WriteImportSection(MemoryStream s)
        {
            WriteU32Leb128(s, (uint)_imports.Count);
            foreach (var (module, name, kind, details) in _imports)
            {
                WriteString(s, module);
                WriteString(s, name);
                s.WriteByte(kind);

                if (kind == WasmOpCodes.ExternalFunc && details is FuncImportDetails funcImport)
                {
                    WriteU32Leb128(s, (uint)funcImport.TypeIndex);
                }
                else if (kind == WasmOpCodes.ExternalMemory && details is MemoryImportDetails mem)
                {
                    // Shared memory with max: flags=0x03, min, max
                    if (mem.Shared)
                    {
                        s.WriteByte(WasmOpCodes.LimitsSharedWithMax);
                        WriteU32Leb128(s, mem.MinPages);
                        WriteU32Leb128(s, mem.MaxPages);
                    }
                    else if (mem.MaxPages > 0)
                    {
                        s.WriteByte(WasmOpCodes.LimitsWithMax);
                        WriteU32Leb128(s, mem.MinPages);
                        WriteU32Leb128(s, mem.MaxPages);
                    }
                    else
                    {
                        s.WriteByte(WasmOpCodes.LimitsNoMax);
                        WriteU32Leb128(s, mem.MinPages);
                    }
                }
            }
        }

        private void WriteFunctionSection(MemoryStream s)
        {
            WriteU32Leb128(s, (uint)_funcTypeIndices.Count);
            foreach (var typeIdx in _funcTypeIndices)
                WriteU32Leb128(s, (uint)typeIdx);
        }

        private void WriteExportSection(MemoryStream s)
        {
            WriteU32Leb128(s, (uint)_exports.Count);
            foreach (var (name, kind, index) in _exports)
            {
                WriteString(s, name);
                s.WriteByte(kind);
                WriteU32Leb128(s, index);
            }
        }

        private void WriteCodeSection(MemoryStream s)
        {
            WriteU32Leb128(s, (uint)_bodies.Count);
            foreach (var body in _bodies)
            {
                // Build body bytes
                using var bodyStream = new MemoryStream();

                // Local declarations
                // Group consecutive locals of same type
                var groupedLocals = GroupLocals(body.Locals);
                WriteU32Leb128(bodyStream, (uint)groupedLocals.Count);
                foreach (var local in groupedLocals)
                {
                    WriteU32Leb128(bodyStream, local.Count);
                    bodyStream.WriteByte(local.Type);
                }

                // Instructions
                bodyStream.Write(body.Code);

                // End opcode
                bodyStream.WriteByte(WasmOpCodes.End);

                // Write body size + body
                var bodyBytes = bodyStream.ToArray();
                WriteU32Leb128(s, (uint)bodyBytes.Length);
                s.Write(bodyBytes);
            }
        }

        #endregion

        #region Encoding Helpers

        private void WriteSection(MemoryStream target, byte sectionId, Action<MemoryStream> writer)
        {
            using var sectionStream = new MemoryStream();
            writer(sectionStream);
            var sectionBytes = sectionStream.ToArray();

            target.WriteByte(sectionId);
            WriteU32Leb128(target, (uint)sectionBytes.Length);
            target.Write(sectionBytes);
        }

        /// <summary>
        /// Writes an unsigned 32-bit integer in LEB128 encoding.
        /// </summary>
        public static void WriteU32Leb128(MemoryStream s, uint value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    b |= 0x80;
                s.WriteByte(b);
            } while (value != 0);
        }

        /// <summary>
        /// Writes a signed 32-bit integer in LEB128 encoding.
        /// </summary>
        public static void WriteS32Leb128(MemoryStream s, int value)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if ((value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0))
                    more = false;
                else
                    b |= 0x80;
                s.WriteByte(b);
            }
        }

        /// <summary>
        /// Writes a signed 64-bit integer in LEB128 encoding.
        /// </summary>
        public static void WriteS64Leb128(MemoryStream s, long value)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if ((value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0))
                    more = false;
                else
                    b |= 0x80;
                s.WriteByte(b);
            }
        }

        /// <summary>
        /// Writes a string (length-prefixed UTF-8).
        /// </summary>
        public static void WriteString(MemoryStream s, string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            WriteU32Leb128(s, (uint)bytes.Length);
            s.Write(bytes);
        }

        /// <summary>
        /// Writes an i32.const instruction to a byte list.
        /// </summary>
        public static void EmitI32Const(List<byte> code, int value)
        {
            code.Add(WasmOpCodes.I32Const);
            EmitS32Leb128(code, value);
        }

        /// <summary>
        /// Writes an i64.const instruction to a byte list.
        /// </summary>
        public static void EmitI64Const(List<byte> code, long value)
        {
            code.Add(WasmOpCodes.I64Const);
            EmitS64Leb128(code, value);
        }

        /// <summary>
        /// Writes an f32.const instruction to a byte list.
        /// </summary>
        public static void EmitF32Const(List<byte> code, float value)
        {
            code.Add(WasmOpCodes.F32Const);
            code.AddRange(BitConverter.GetBytes(value));
        }

        /// <summary>
        /// Writes an f64.const instruction to a byte list.
        /// </summary>
        public static void EmitF64Const(List<byte> code, double value)
        {
            code.Add(WasmOpCodes.F64Const);
            code.AddRange(BitConverter.GetBytes(value));
        }

        /// <summary>
        /// Writes a local.get instruction to a byte list.
        /// </summary>
        public static void EmitLocalGet(List<byte> code, uint index)
        {
            code.Add(WasmOpCodes.LocalGet);
            EmitU32Leb128(code, index);
        }

        /// <summary>
        /// Writes a local.set instruction to a byte list.
        /// </summary>
        public static void EmitLocalSet(List<byte> code, uint index)
        {
            code.Add(WasmOpCodes.LocalSet);
            EmitU32Leb128(code, index);
        }

        /// <summary>
        /// Writes a local.tee instruction to a byte list.
        /// </summary>
        public static void EmitLocalTee(List<byte> code, uint index)
        {
            code.Add(WasmOpCodes.LocalTee);
            EmitU32Leb128(code, index);
        }

        /// <summary>
        /// Writes a memory load instruction with alignment and offset.
        /// </summary>
        public static void EmitLoad(List<byte> code, byte opcode, uint align, uint offset)
        {
            code.Add(opcode);
            EmitU32Leb128(code, align);
            EmitU32Leb128(code, offset);
        }

        /// <summary>
        /// Writes a memory store instruction with alignment and offset.
        /// </summary>
        public static void EmitStore(List<byte> code, byte opcode, uint align, uint offset)
        {
            code.Add(opcode);
            EmitU32Leb128(code, align);
            EmitU32Leb128(code, offset);
        }

        /// <summary>
        /// Writes an atomic RMW instruction (0xFE prefix + opcode + memarg).
        /// Atomic instructions use the same alignment/offset format as regular memory ops.
        /// NOTE (2026-06-10, Seven): an experiment translating all atomic LOADS here to
        /// rmw.add(+0) reads made the residual large-sort race WORSE (11/120 vs 2-3/120
        /// with the dispatcher sense-barrier fix alone) - the "RMW reads bypass the
        /// lagged view" model was falsified; rate changes from load-vs-RMW swaps are
        /// timing perturbation. Loads stay loads.
        /// </summary>
        public static void EmitAtomicRmw(List<byte> code, byte atomicOpcode, uint align, uint offset)
        {
            code.Add(WasmOpCodes.AtomicPrefix);
            EmitU32Leb128(code, atomicOpcode);
            EmitU32Leb128(code, align);
            EmitU32Leb128(code, offset);
        }

        // ── SIMD (v128) emit helpers ──────────────────────────────────────────────────
        // Every SIMD instruction is the 0xFD prefix followed by a u32 LEB128 sub-opcode
        // (WasmOpCodes.Simd* constants), then any immediates. The LEB128 step is mandatory —
        // sub-opcodes >= 128 (most arithmetic ops) are multi-byte. These are the only correct
        // way to emit v128 ops; do NOT hand-write 0xFD + a raw byte. (Phase 1 emitter foundation
        // for the Wasm SIMD128 port — see Plans/wasm-simd128-velocity-port-plan-2026-06-12.md.)

        /// <summary>
        /// Writes a SIMD op that takes no immediate (arithmetic / bitwise / splat):
        /// 0xFD prefix + u32 LEB128 sub-opcode. e.g. f32x4.add, v128.bitselect, i32x4.splat.
        /// </summary>
        public static void EmitSimd(List<byte> code, uint simdOpcode)
        {
            code.Add(WasmOpCodes.SimdPrefix);
            EmitU32Leb128(code, simdOpcode);
        }

        /// <summary>
        /// Writes a SIMD memory op (v128.load / v128.store): 0xFD prefix + sub-opcode + memarg
        /// (align + offset), matching the scalar load/store memarg format. Natural alignment for
        /// a full v128 access is 4 (2^4 = 16 bytes); pass a smaller align for unaligned access.
        /// </summary>
        public static void EmitSimdMem(List<byte> code, uint simdOpcode, uint align, uint offset)
        {
            code.Add(WasmOpCodes.SimdPrefix);
            EmitU32Leb128(code, simdOpcode);
            EmitU32Leb128(code, align);
            EmitU32Leb128(code, offset);
        }

        /// <summary>
        /// Writes a SIMD lane op (extract_lane / replace_lane): 0xFD prefix + sub-opcode + a
        /// single 1-byte lane index immediate (0..lanes-1 for the shape).
        /// </summary>
        public static void EmitSimdLane(List<byte> code, uint simdOpcode, byte lane)
        {
            code.Add(WasmOpCodes.SimdPrefix);
            EmitU32Leb128(code, simdOpcode);
            code.Add(lane);
        }

        /// <summary>
        /// Writes a v128.const: 0xFD prefix + sub-opcode 12 + exactly 16 immediate bytes (the
        /// little-endian vector literal).
        /// </summary>
        public static void EmitV128Const(List<byte> code, byte[] sixteenBytes)
        {
            if (sixteenBytes == null || sixteenBytes.Length != 16)
                throw new ArgumentException("v128.const requires exactly 16 immediate bytes", nameof(sixteenBytes));
            code.Add(WasmOpCodes.SimdPrefix);
            EmitU32Leb128(code, WasmOpCodes.V128Const);
            code.AddRange(sixteenBytes);
        }

        /// <summary>
        /// Splats a single f32 across all 4 lanes via v128.const (4 copies of the same f32 bits).
        /// Convenience over push-scalar + f32x4.splat when the value is a compile-time constant.
        /// </summary>
        public static void EmitF32x4ConstSplat(List<byte> code, float value)
        {
            var b = BitConverter.GetBytes(value); // little-endian on the wasm target
            var v = new byte[16];
            for (int lane = 0; lane < 4; lane++)
                Array.Copy(b, 0, v, lane * 4, 4);
            EmitV128Const(code, v);
        }

        /// <summary>
        /// Writes an i8x16.shuffle: 0xFD prefix + sub-opcode 13 + 16 lane-select bytes
        /// (each 0..31 picking a byte from the two input vectors).
        /// </summary>
        public static void EmitI8x16Shuffle(List<byte> code, byte[] sixteenLaneIndices)
        {
            if (sixteenLaneIndices == null || sixteenLaneIndices.Length != 16)
                throw new ArgumentException("i8x16.shuffle requires exactly 16 lane indices", nameof(sixteenLaneIndices));
            code.Add(WasmOpCodes.SimdPrefix);
            EmitU32Leb128(code, WasmOpCodes.I8x16Shuffle);
            code.AddRange(sixteenLaneIndices);
        }

        /// <summary>
        /// Writes a call instruction to a byte list.
        /// </summary>
        public static void EmitCall(List<byte> code, uint funcIndex)
        {
            code.Add(WasmOpCodes.Call);
            EmitU32Leb128(code, funcIndex);
        }

        /// <summary>
        /// Writes an unsigned LEB128 to a byte list (for instruction operands).
        /// </summary>
        public static void EmitU32Leb128(List<byte> code, uint value)
        {
            do
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if (value != 0)
                    b |= 0x80;
                code.Add(b);
            } while (value != 0);
        }

        /// <summary>
        /// Writes a signed LEB128 to a byte list (for instruction operands).
        /// </summary>
        public static void EmitS32Leb128(List<byte> code, int value)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if ((value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0))
                    more = false;
                else
                    b |= 0x80;
                code.Add(b);
            }
        }

        /// <summary>
        /// Writes a signed 64-bit LEB128 to a byte list.
        /// </summary>
        public static void EmitS64Leb128(List<byte> code, long value)
        {
            bool more = true;
            while (more)
            {
                byte b = (byte)(value & 0x7F);
                value >>= 7;
                if ((value == 0 && (b & 0x40) == 0) || (value == -1 && (b & 0x40) != 0))
                    more = false;
                else
                    b |= 0x80;
                code.Add(b);
            }
        }

        private List<WasmLocal> GroupLocals(List<WasmLocal> locals)
        {
            if (locals.Count == 0) return new List<WasmLocal>();

            var grouped = new List<WasmLocal>();
            byte currentType = locals[0].Type;
            uint currentCount = locals[0].Count;

            for (int i = 1; i < locals.Count; i++)
            {
                if (locals[i].Type == currentType)
                {
                    currentCount += locals[i].Count;
                }
                else
                {
                    grouped.Add(new WasmLocal { Type = currentType, Count = currentCount });
                    currentType = locals[i].Type;
                    currentCount = locals[i].Count;
                }
            }
            grouped.Add(new WasmLocal { Type = currentType, Count = currentCount });
            return grouped;
        }

        #endregion

        #region Internal Types

        private class MemoryImportDetails
        {
            public uint MinPages { get; set; }
            public uint MaxPages { get; set; }
            public bool Shared { get; set; }
        }

        private class FuncImportDetails
        {
            public int TypeIndex { get; set; }
        }

        #endregion
    }
}
