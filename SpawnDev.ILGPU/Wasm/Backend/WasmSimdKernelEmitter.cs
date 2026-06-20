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

        /// <summary>Count of lane-variant v128 STORES emitted in the current SIMD pass. MUST be &gt; 0 for a
        /// valid `kernel_simd`: the by-4 dispatch runs `kernel_simd(i)` once per 4 threads, so if the body
        /// has NO per-lane v128 store it would process only lane i and SKIP i+1..i+3. A kernel the analysis
        /// classified as all-lane-uniform produces zero v128 stores → it must bail to the scalar path
        /// (which runs every thread). Guards against any analysis under-detection of lane variance.</summary>
        private int _simdV128StoreCount;

        /// <summary>The lane-VARIANT values that were actually emitted as a real v128 local (a Load
        /// result or an f32x4/i32x4 arithmetic result). A lane-variant operand that is NOT in this set
        /// is something the 4-lane model cannot broadcast correctly — most importantly the per-lane
        /// thread INDEX used as DATA (e.g. <c>out[i] = i * 2</c>): it differs per lane as
        /// <c>(base, base+1, base+2, base+3)</c>, NOT a splat of <c>base</c>. <see cref="PushAsV128"/>
        /// bails when it sees such an operand, so index-as-data kernels fall back to scalar until the
        /// index-vector increment lands. Reset per <see cref="TryGenerateSimdKernel"/> attempt.</summary>
        private readonly HashSet<Value> _simdV128Values = new();

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
                _simdV128StoreCount = 0;
                _simdV128Values.Clear();

                foreach (var v in block)
                {
                    if (!EmitSimdValue(v, laneVariant))
                        return false; // (finally restores; ok stays false)
                }

                // SAFETY: a valid kernel_simd MUST contain at least one lane-variant v128 store. If the
                // analysis under-detected variance (e.g. a thread-position intrinsic it failed to seed),
                // every value would be emitted scalar and kernel_simd would have NO v128 store — then the
                // by-4 dispatch (kernel_simd(i), i+=4) would process only lane i and silently skip
                // i+1..i+3. Bail to the always-correct scalar path instead.
                if (_simdV128StoreCount == 0)
                    return false; // (finally restores)

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

        /// <summary>The 4-lane SIMD class of a primitive type: f32 → f32x4, i32 → i32x4, else none.
        /// Both pack 4 elements per v128 (matching the by-4 dispatch). Sub-word ints (byte/short) and
        /// 2-lane (i64/f64) are deliberately NOT in this set yet — they need a different lane count.</summary>
        private enum LaneClass { None, F32x4, I32x4 }

        private static LaneClass ClassOf(TypeNode t) =>
            t is PrimitiveType pt
                ? pt.BasicValueType switch
                {
                    global::ILGPU.BasicValueType.Float32 => LaneClass.F32x4,
                    global::ILGPU.BasicValueType.Int32 => LaneClass.I32x4,
                    _ => LaneClass.None,
                }
                : LaneClass.None;

        /// <summary>True if <paramref name="v"/> is in the Stage-3a unit-stride elementwise class.
        /// Address / lane-invariant / terminator values are always fine (emitted scalar); lane-variant
        /// values must be a 4-lane (f32 or i32) Load / arithmetic / Store. Anything else ⇒ bail.</summary>
        private bool IsStage3aEmittable(Value v, HashSet<Value> laneVariant)
        {
            // Structs anywhere ⇒ out of class (struct snapshot path touches scratch; later increment).
            if (v.Type is StructureType) return false;

            // Pointer-typed (address) values: emitted scalar regardless of variance. OK.
            if (v.Type is AddressSpaceType) return true;

            // Lane-invariant primitives: emitted scalar + splatted on demand. OK.
            if (!laneVariant.Contains(v)) return true;

            // Lane-variant data: must be a 4-lane (f32/i32) Load / arith / Store. (An index-as-data
            // operand passes this gate but is rejected during emit by PushAsV128 — it has no v128 local.)
            switch (v)
            {
                case Load ld:
                    return ClassOf(ld.Type) != LaneClass.None && AllUsesVectorizable(ld);
                case Store st:
                    return ClassOf(st.Value.Resolve().Type) != LaneClass.None;
                case BinaryArithmeticValue ba:
                    return MapBinary(ba) != 0 && AllUsesVectorizable(ba);
                case UnaryArithmeticValue ua:
                    return MapUnary(ua) != 0 && AllUsesVectorizable(ua);
                default:
                    // compares/selects, converts, gather LEAs, shifts, i64/f64 ⇒ later increments
                    return false;
            }
        }

        /// <summary>True iff EVERY consumer of the (about-to-be-vectorized) value <paramref name="v"/> is
        /// a use we emit as a vector: a mapped f32x4/i32x4 arithmetic operand, or the stored VALUE of a
        /// Store. Anything else — most importantly an address computation (a <c>LoadElementAddress</c>
        /// using a loaded i32 as a GATHER index, or a store TARGET) — reads the operand as a scalar i32,
        /// so vectorizing <paramref name="v"/> to a v128 would make the emitted module fail to validate
        /// (<c>i32.mul expected i32, found v128</c>). When a use is outside the vector subgraph we bail
        /// the whole kernel to the proven scalar path. This is what keeps the vectorized value-set CLOSED;
        /// it is the guard that distinguishes a unit-stride elementwise kernel (safe) from a gather/scatter
        /// kernel (an i32 index load feeding an address — Stage-3a-later). The f32-only emitter never
        /// needed this because address indices are integers, never f32.</summary>
        private bool AllUsesVectorizable(Value v)
        {
            foreach (var use in v.Uses)
            {
                var user = use.Resolve();
                switch (user)
                {
                    case BinaryArithmeticValue ba when MapBinary(ba) != 0:
                        break; // mapped binary: both operand positions are v128 (we don't map scalar-count shifts)
                    case UnaryArithmeticValue ua when MapUnary(ua) != 0:
                        break;
                    case Store st when ReferenceEquals(st.Value.Resolve(), v):
                        break; // used as the stored value (vector store)
                    default:
                        return false; // address/LEA index, store target, compare, convert, call, struct, terminator…
                }
            }
            return true;
        }

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
                    if (ClassOf(ld.Type) == LaneClass.None) return false;
                    var target = AllocateLocal(ld, WasmOpCodes.V128);
                    EmitGetLocal(ld.Source.Resolve());                 // scalar lane-base byte address
                    WasmModuleBuilder.EmitSimdMem(Code, WasmOpCodes.V128Load, 2, 0); // 4-byte align, unit-stride
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    _simdV128Values.Add(ld);
                    return true;
                }
                case BinaryArithmeticValue ba:
                {
                    uint op = MapBinary(ba);
                    if (op == 0) return false;
                    var target = AllocateLocal(ba, WasmOpCodes.V128);
                    if (!PushAsV128(ba.Left.Resolve(), laneVariant)) return false;
                    if (!PushAsV128(ba.Right.Resolve(), laneVariant)) return false;
                    WasmModuleBuilder.EmitSimd(Code, op);              // f32x4/i32x4 op (NO fused FMA)
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    _simdV128Values.Add(ba);
                    return true;
                }
                case UnaryArithmeticValue ua:
                {
                    uint op = MapUnary(ua);
                    if (op == 0) return false;
                    var target = AllocateLocal(ua, WasmOpCodes.V128);
                    if (!PushAsV128(ua.Value.Resolve(), laneVariant)) return false;
                    WasmModuleBuilder.EmitSimd(Code, op);
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    _simdV128Values.Add(ua);
                    return true;
                }
                case Store st:
                {
                    var storeVal = st.Value.Resolve();
                    if (ClassOf(storeVal.Type) == LaneClass.None) return false;
                    EmitGetLocal(st.Target.Resolve());                 // scalar lane-base byte address
                    if (!PushAsV128(storeVal, laneVariant)) return false;
                    WasmModuleBuilder.EmitSimdMem(Code, WasmOpCodes.V128Store, 2, 0);
                    _simdV128StoreCount++;                             // real per-lane vector work emitted
                    return true;
                }
                default:
                    return false;
            }
        }

        /// <summary>Pushes <paramref name="op"/> onto the stack as a v128. A lane-VARIANT value must
        /// already be a real v128 local (a Load/arith result tracked in <see cref="_simdV128Values"/>) —
        /// if it is lane-variant but NOT such a local (the per-lane index used as data), we cannot
        /// broadcast it correctly in the 4-lane model, so bail. A lane-INVARIANT scalar is loaded and
        /// splatted (f32x4.splat / i32x4.splat) to all four lanes.</summary>
        private bool PushAsV128(Value op, HashSet<Value> laneVariant)
        {
            var cls = ClassOf(op.Type);
            if (cls == LaneClass.None) return false;
            if (laneVariant.Contains(op))
            {
                if (!_simdV128Values.Contains(op)) return false;       // index-as-data etc. ⇒ scalar fallback
                EmitGetLocal(op);                                      // already a v128 local
                return true;
            }
            EmitGetLocal(op);                                          // uniform scalar
            WasmModuleBuilder.EmitSimd(Code, cls == LaneClass.F32x4 ? WasmOpCodes.F32x4Splat : WasmOpCodes.I32x4Splat);
            return true;
        }

        /// <summary>Maps a binary arithmetic value to its f32x4/i32x4 opcode, or 0 if unsupported in 3a.
        /// Integer Min/Max pick the signed/unsigned variant from <see cref="ArithmeticFlags.Unsigned"/>;
        /// And/Or/Xor are the whole-vector v128 bitwise ops. Div/Rem (no SIMD int divide) and shifts
        /// (their count operand is a scalar i32, not a lane — a later increment) return 0 ⇒ scalar.</summary>
        private static uint MapBinary(BinaryArithmeticValue v)
        {
            var cls = ClassOf(v.Type);
            bool u = (v.Flags & ArithmeticFlags.Unsigned) == ArithmeticFlags.Unsigned;
            if (cls == LaneClass.F32x4)
                return v.Kind switch
                {
                    BinaryArithmeticKind.Add => WasmOpCodes.F32x4Add,
                    BinaryArithmeticKind.Sub => WasmOpCodes.F32x4Sub,
                    BinaryArithmeticKind.Mul => WasmOpCodes.F32x4Mul,
                    BinaryArithmeticKind.Div => WasmOpCodes.F32x4Div,
                    BinaryArithmeticKind.Min => WasmOpCodes.F32x4Min,
                    BinaryArithmeticKind.Max => WasmOpCodes.F32x4Max,
                    _ => 0u,
                };
            if (cls == LaneClass.I32x4)
                return v.Kind switch
                {
                    BinaryArithmeticKind.Add => WasmOpCodes.I32x4Add,
                    BinaryArithmeticKind.Sub => WasmOpCodes.I32x4Sub,
                    BinaryArithmeticKind.Mul => WasmOpCodes.I32x4Mul,
                    BinaryArithmeticKind.And => WasmOpCodes.V128And,
                    BinaryArithmeticKind.Or => WasmOpCodes.V128Or,
                    BinaryArithmeticKind.Xor => WasmOpCodes.V128Xor,
                    BinaryArithmeticKind.Min => u ? WasmOpCodes.I32x4MinU : WasmOpCodes.I32x4MinS,
                    BinaryArithmeticKind.Max => u ? WasmOpCodes.I32x4MaxU : WasmOpCodes.I32x4MaxS,
                    _ => 0u, // Div/Rem (no SIMD int divide), Shl/Shr (scalar count operand) ⇒ later
                };
            return 0u;
        }

        /// <summary>Maps a unary arithmetic value to its f32x4/i32x4 opcode, or 0 if unsupported in 3a.</summary>
        private static uint MapUnary(UnaryArithmeticValue v)
        {
            var cls = ClassOf(v.Type);
            if (cls == LaneClass.F32x4)
                return v.Kind switch
                {
                    UnaryArithmeticKind.Neg => WasmOpCodes.F32x4Neg,
                    UnaryArithmeticKind.Abs => WasmOpCodes.F32x4Abs,
                    _ => 0u, // Sqrt/etc. are a later increment
                };
            if (cls == LaneClass.I32x4)
                return v.Kind switch
                {
                    UnaryArithmeticKind.Neg => WasmOpCodes.I32x4Neg,
                    UnaryArithmeticKind.Abs => WasmOpCodes.I32x4Abs,
                    UnaryArithmeticKind.Not => WasmOpCodes.V128Not, // whole-vector bitwise not
                    _ => 0u,
                };
            return 0u;
        }
    }
}
