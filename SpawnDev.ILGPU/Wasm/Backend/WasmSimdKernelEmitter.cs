// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmSimdKernelEmitter.cs
//
// Wasm SIMD128 port — Stage 3a increment 2: the ADDITIVE v128 `kernel_simd` emitter.
// See Plans/wasm-simd128-phase3-velocity-port-design-2026-06-14.md.
//
// Architecture (LOCKED, zero scalar-path risk):
//   - The scalar `kernel` is generated exactly as before and stays BYTE-IDENTICAL — this file
//     never modifies the scalar GenerateCode(*) methods; it only CALLS them in a fresh, isolated
//     local/Code context for the lane-uniform (address / invariant / terminator) values, and emits
//     v128 ONLY for the lane-VARIANT primitive DATA values (load result, f32x4 arithmetic, store).
//   - Selected by ONE guard (WasmBackend): emit `kernel_simd` additionally iff
//     `EffectiveWasmSimd && WasmSimdAnalysis.Vectorizable && TryGenerateSimdKernel(...)`.
//   - `kernel_simd` processes 4 consecutive lanes/call; the worker loop runs it for full groups
//     and the EXISTING scalar `kernel` handles the `count % 4` tail (no masks — Stage 3b).
//   - HARD BAIL: any value/shape outside the Stage-3a f32 unit-stride elementwise class makes this
//     return false ⇒ no `kernel_simd` is emitted ⇒ pure scalar path ⇒ nothing regresses, the kernel
//     just doesn't speed up yet. Coverage grows in later increments (i64/f64 2-lane, gather, masks).
//
// Routing per value (single-block, no-barrier; the analysis already excluded barriers/atomics/
// warp ops / lane-variant branches):
//   - result type is a pointer (AddressSpaceType)            -> SCALAR (the lane-BASE address;
//     reuses the proven scalar LEA/view emission). v128.load/store reads/writes 4 contiguous lanes
//     from that base (unit-stride assumption — verified numerically by the CPU-oracle gate).
//   - lane-INVARIANT primitive value                          -> SCALAR (splatted to v128 only where
//     it feeds a vector op).
//   - lane-VARIANT primitive f32 value (Load / f32x4 arith)   -> v128 local.
//   - Store of a lane-variant value to a unit-stride address  -> v128.store.
// ---------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using global::ILGPU.IR;
using global::ILGPU.IR.Types;
using global::ILGPU.IR.Values;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    public partial class WasmKernelFunctionGenerator
    {
        /// <summary>The emitted v128 `kernel_simd` body (4 lanes/call), or null if not emitted.</summary>
        internal byte[]? SimdKernelCode;
        /// <summary>The locals declared by the v128 `kernel_simd` body (parallel to <see cref="SimdKernelCode"/>).</summary>
        internal List<WasmLocal>? SimdKernelLocals;
        /// <summary>True iff a valid `kernel_simd` was emitted (⇒ dispatch may run it by-4 + scalar tail).</summary>
        internal bool HasSimdKernel;

        /// <summary>
        /// Attempts to emit the additive v128 <c>kernel_simd</c> for the Stage-3a f32 unit-stride
        /// elementwise class. Saves/clears/restores the generator's local+Code context so the
        /// already-generated scalar <c>kernel</c> is left byte-identical. Returns false (and emits
        /// nothing) on any shape outside the class — the caller then ships scalar-only.
        /// MUST be called AFTER the scalar <see cref="Generate"/> has produced the scalar Code.
        /// </summary>
        internal bool TryGenerateSimdKernel()
        {
            HasSimdKernel = false;
            SimdKernelCode = null;
            SimdKernelLocals = null;

            if (_indexParam == null || _blockCount != 1)
                return false;

            var analysis = WasmSimdAnalysis.Analyze(Method, _indexParam, out var laneVariant);
            if (!analysis.Vectorizable)
                return false;

            var block = Method.Blocks.First();

            // First-cut class gate: every value must be emittable as f32 4-lane (or a scalar
            // address/invariant/terminator). Bail BEFORE mutating any state if not.
            foreach (var v in block)
                if (!IsStage3aEmittable(v, laneVariant))
                    return false;

            // ── Save the scalar context (readonly fields → clear+restore CONTENTS) ──
            var savedCode = Code.ToArray();
            var savedLocals = new List<WasmLocal>(_locals);
            var savedMap = new Dictionary<string, uint>(_localMap);
            var savedNext = _nextLocalIndex;
            var savedFirstState = new Dictionary<uint, int>(_localFirstState);
            var savedCrosses = new HashSet<uint>(_localCrossesState);

            bool ok = false;
            try
            {
                // Fresh body context: keep ONLY the parameter local mappings (index < _paramCount);
                // body locals start empty, next local index resumes after the params.
                Code.Clear();
                _locals.Clear();
                var paramMap = savedMap.Where(kv => kv.Value < _paramCount)
                                       .ToDictionary(kv => kv.Key, kv => kv.Value);
                _localMap.Clear();
                foreach (var kv in paramMap) _localMap[kv.Key] = kv.Value;
                _nextLocalIndex = (uint)_paramCount;
                _localFirstState.Clear();
                _localCrossesState.Clear();

                foreach (var v in block)
                {
                    if (!EmitSimdValue(v, laneVariant))
                        return false; // (finally restores; ok stays false)
                }
                if (block.Terminator != null)
                    GenerateCodeFor(block.Terminator); // ReturnTerminator — lane-uniform
                WasmModuleBuilder.EmitI32Const(Code, 0); // i32 return (0 = done), matches scalar

                SimdKernelCode = Code.ToArray();
                SimdKernelLocals = new List<WasmLocal>(_locals);
                HasSimdKernel = true;
                ok = true;
                return true;
            }
            finally
            {
                // Restore the scalar context exactly (so WasmBackend reads the unchanged scalar Code).
                Code.Clear(); Code.AddRange(savedCode);
                _locals.Clear(); _locals.AddRange(savedLocals);
                _localMap.Clear(); foreach (var kv in savedMap) _localMap[kv.Key] = kv.Value;
                _nextLocalIndex = savedNext;
                _localFirstState.Clear(); foreach (var kv in savedFirstState) _localFirstState[kv.Key] = kv.Value;
                _localCrossesState.Clear(); foreach (var x in savedCrosses) _localCrossesState.Add(x);
                if (!ok) { HasSimdKernel = false; SimdKernelCode = null; SimdKernelLocals = null; }
            }
        }

        /// <summary>True if <paramref name="v"/> is in the Stage-3a f32 unit-stride elementwise class.
        /// Address / lane-invariant / terminator values are always fine (emitted scalar); lane-variant
        /// values must be f32 Load / f32x4 arithmetic / Store of an f32. Anything else ⇒ bail.</summary>
        private bool IsStage3aEmittable(Value v, HashSet<Value> laneVariant)
        {
            // Structs anywhere ⇒ out of class (struct snapshot path touches scratch; later increment).
            if (v.Type is StructureType) return false;

            // Pointer-typed (address) values: emitted scalar regardless of variance. OK.
            if (v.Type is AddressSpaceType) return true;

            // Lane-invariant primitives: emitted scalar + splatted on demand. OK.
            if (!laneVariant.Contains(v)) return true;

            // Lane-variant data: must be f32 (4-lane), and one of Load / arith / Store / Parameter.
            switch (v)
            {
                case Load ld:
                    return IsF32(ld.Type);
                case Store st:
                    // store of a lane-variant f32 value to a (scalar) address
                    return IsF32(st.Value.Resolve().Type);
                case BinaryArithmeticValue ba:
                    return IsF32(ba.Type) && MapF32x4Binary(ba.Kind) != 0;
                case UnaryArithmeticValue ua:
                    return IsF32(ua.Type) && MapF32x4Unary(ua.Kind) != 0;
                default:
                    // index-as-data, compares/selects, converts, gather LEAs, i64/f64 ⇒ later increments
                    return false;
            }
        }

        private static bool IsF32(TypeNode t) =>
            t is PrimitiveType pt && pt.BasicValueType == global::ILGPU.BasicValueType.Float32;

        /// <summary>Emits a single value in the v128 pass. Returns false to bail (unhandled).</summary>
        private bool EmitSimdValue(Value v, HashSet<Value> laneVariant)
        {
            // Address values + lane-invariant values: reuse the PROVEN scalar emission verbatim
            // (produces a scalar local — the lane-base address, or a uniform scalar to splat later).
            if (v.Type is AddressSpaceType || !laneVariant.Contains(v))
            {
                GenerateCodeFor(v);
                return true;
            }

            switch (v)
            {
                case Load ld:
                {
                    if (!IsF32(ld.Type)) return false;
                    var target = AllocateLocal(ld, WasmOpCodes.V128);
                    EmitGetLocal(ld.Source.Resolve());                 // scalar lane-base byte address
                    WasmModuleBuilder.EmitSimdMem(Code, WasmOpCodes.V128Load, 2, 0); // 4-byte align, unit-stride
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    return true;
                }
                case BinaryArithmeticValue ba:
                {
                    uint op = MapF32x4Binary(ba.Kind);
                    if (op == 0 || !IsF32(ba.Type)) return false;
                    var target = AllocateLocal(ba, WasmOpCodes.V128);
                    if (!PushAsV128(ba.Left.Resolve(), laneVariant)) return false;
                    if (!PushAsV128(ba.Right.Resolve(), laneVariant)) return false;
                    WasmModuleBuilder.EmitSimd(Code, op);              // f32x4.<op> (mul+add, NO fused FMA)
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    return true;
                }
                case UnaryArithmeticValue ua:
                {
                    uint op = MapF32x4Unary(ua.Kind);
                    if (op == 0 || !IsF32(ua.Type)) return false;
                    var target = AllocateLocal(ua, WasmOpCodes.V128);
                    if (!PushAsV128(ua.Value.Resolve(), laneVariant)) return false;
                    WasmModuleBuilder.EmitSimd(Code, op);
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    return true;
                }
                case Store st:
                {
                    var storeVal = st.Value.Resolve();
                    if (!IsF32(storeVal.Type)) return false;
                    EmitGetLocal(st.Target.Resolve());                 // scalar lane-base byte address
                    if (!PushAsV128(storeVal, laneVariant)) return false;
                    WasmModuleBuilder.EmitSimdMem(Code, WasmOpCodes.V128Store, 2, 0);
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Pushes <paramref name="op"/> onto the stack as a v128: a lane-variant f32 value is
        /// already a v128 local; a lane-invariant scalar f32 is loaded and <c>f32x4.splat</c>'d.</summary>
        private bool PushAsV128(Value op, HashSet<Value> laneVariant)
        {
            if (!IsF32(op.Type)) return false;
            EmitGetLocal(op);
            if (!laneVariant.Contains(op))
                WasmModuleBuilder.EmitSimd(Code, WasmOpCodes.F32x4Splat); // broadcast the uniform scalar
            return true;
        }

        /// <summary>Maps an f32 binary arithmetic kind to its f32x4 opcode, or 0 if unsupported in 3a.</summary>
        private static uint MapF32x4Binary(BinaryArithmeticKind kind) => kind switch
        {
            BinaryArithmeticKind.Add => WasmOpCodes.F32x4Add,
            BinaryArithmeticKind.Sub => WasmOpCodes.F32x4Sub,
            BinaryArithmeticKind.Mul => WasmOpCodes.F32x4Mul,
            BinaryArithmeticKind.Div => WasmOpCodes.F32x4Div,
            BinaryArithmeticKind.Min => WasmOpCodes.F32x4Min,
            BinaryArithmeticKind.Max => WasmOpCodes.F32x4Max,
            _ => 0u,
        };

        /// <summary>Maps an f32 unary arithmetic kind to its f32x4 opcode, or 0 if unsupported in 3a.</summary>
        private static uint MapF32x4Unary(UnaryArithmeticKind kind) => kind switch
        {
            UnaryArithmeticKind.Neg => WasmOpCodes.F32x4Neg,
            UnaryArithmeticKind.Abs => WasmOpCodes.F32x4Abs,
            _ => 0u,  // Sqrt/etc. are later-increment (verify the kind name against the IR enum first)
        };
    }
}
