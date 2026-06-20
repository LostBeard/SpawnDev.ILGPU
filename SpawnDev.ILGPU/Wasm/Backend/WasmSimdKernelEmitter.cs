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
using global::ILGPU.IR.Analyses;
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

            if (_indexParam == null)
                return false;
            if (_blockCount != 1)
                // multi-block: canonical counted loop, else a divergent if-diamond (Stage 3b masks);
                // each bails to the proven scalar path on anything outside its exact shape.
                return TryGenerateSimdLoopKernel() || TryGenerateSimdDiamondKernel();

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

        /// <summary>
        /// Stage-3a MULTI-BLOCK path: vectorizes the CANONICAL counted while-loop shape
        /// (entry → header[cond] → body → header back-edge, exit after) — e.g. an un-unrolled
        /// <c>for(k=0;k&lt;reps;k++) acc = f(acc, a[i]…)</c>. Control flow is lane-UNIFORM (the
        /// induction variable + bound + condition are all lane-invariant → emitted SCALAR), so the
        /// loop structure is identical across the 4 lanes; only the lane-VARIANT data (loads,
        /// arithmetic, the accumulator phi) becomes v128. Emitted as structured wasm
        /// <c>block $exit { loop $hdr { &lt;cond→br_if exit&gt; &lt;body v128&gt; &lt;phi updates&gt; br $hdr } }</c>.
        /// Detection uses ILGPU's <see cref="LoopInfo{TOrder,TDirection}"/> (single entry/header/exit/
        /// back-edge); ANYTHING outside this exact shape returns false ⇒ the proven scalar state-machine
        /// path runs (zero regression). Saves/restores the generator context like the single-block path,
        /// so the scalar kernel stays byte-identical.
        /// </summary>
        private bool TryGenerateSimdLoopKernel()
        {
            // ── Detect the canonical single counted while-loop (var avoids naming LoopInfo<,>) ──
            if (_blockCount != 4) return false;          // entry, header, body, exit
            var cfg = Method.Blocks.CreateCFG();
            var loops = cfg.CreateLoops();
            if (loops.Count != 1) return false;          // exactly one loop ⇒ not nested
            BasicBlock? entryB = null, headerB = null, bodyB = null, exitB = null, backEdgeB = null;
            bool isDoWhile = true, gotInfo = false;
            foreach (var loop in loops)
            {
                if (!loop.TryGetLoopInfo(out var info) || info == null) return false;
                entryB = info.Entry; headerB = info.Header; bodyB = info.Body; exitB = info.Exit;
                backEdgeB = info.BackEdge; isDoWhile = info.IsDoWhileLoop; gotInfo = true;
            }
            if (!gotInfo || isDoWhile) return false;     // while-loop only (top-tested); do-while later
            if (entryB == null || headerB == null || bodyB == null || exitB == null) return false;

            BasicBlock entry = entryB, header = headerB, body = bodyB, exit = exitB;
            if (!ReferenceEquals(backEdgeB, body)) return false;         // single body block is the back-edge
            // Terminator shapes: entry/body → unconditional to header; header → IfBranch; exit → return.
            if (entry.Terminator is not UnconditionalBranch eub || !ReferenceEquals(eub.Target, header)) return false;
            if (body.Terminator is not UnconditionalBranch bub || !ReferenceEquals(bub.Target, header)) return false;
            if (header.Terminator is not IfBranch hib) return false;
            if (exit.Terminator is not ReturnTerminator) return false;
            // The IfBranch must go header→{body, exit} in some order.
            bool condTrueIsBody = ReferenceEquals(hib.TrueTarget, body) && ReferenceEquals(hib.FalseTarget, exit);
            bool condTrueIsExit = ReferenceEquals(hib.TrueTarget, exit) && ReferenceEquals(hib.FalseTarget, body);
            if (!condTrueIsBody && !condTrueIsExit) return false;

            var analysis = WasmSimdAnalysis.Analyze(Method, _indexParam, out var laneVariant);
            if (!analysis.Vectorizable) return false;

            // Collect header phis; classify each (variant→v128, invariant→scalar). Each must have
            // exactly the two predecessors {entry, body}. A variant phi must be a 4-lane class (f32/i32).
            var headerPhis = new List<PhiValue>();
            foreach (var ve in header)
                if (ve.Value is PhiValue phi)
                {
                    if (phi.Count != 2) return false;
                    bool hasEntry = false, hasBody = false;
                    for (int j = 0; j < phi.Count; j++)
                    {
                        if (ReferenceEquals(phi.Sources[j], entry)) hasEntry = true;
                        else if (ReferenceEquals(phi.Sources[j], body)) hasBody = true;
                    }
                    if (!hasEntry || !hasBody) return false;
                    if (laneVariant.Contains(phi) && ClassOf(phi.Type) == LaneClass.None) return false;
                    headerPhis.Add(phi);
                }

            // Class-gate every non-phi value in every block (phis handled above; terminators handled
            // structurally). A lane-variant CompareValue would be a data compare (selects) → out of class.
            foreach (var b in new[] { entry, header, body, exit })
                foreach (var ve in b)
                {
                    var v = ve.Value;
                    if (v is PhiValue) continue;
                    if (!IsStage3aEmittable(v, laneVariant)) return false;
                }

            // ── Save the scalar context (mirror the single-block path) ──
            var savedCode = Code.ToArray();
            var savedLocals = new List<WasmLocal>(_locals);
            var savedMap = new Dictionary<string, uint>(_localMap);
            var savedNext = _nextLocalIndex;
            var savedFirstState = new Dictionary<uint, int>(_localFirstState);
            var savedCrosses = new HashSet<uint>(_localCrossesState);
            bool savedStateMachine = _isStateMachine;

            bool ok = false;
            try
            {
                Code.Clear();
                _locals.Clear();
                var paramMap = savedMap.Where(kv => kv.Value < _paramCount).ToDictionary(kv => kv.Key, kv => kv.Value);
                _localMap.Clear();
                foreach (var kv in paramMap) _localMap[kv.Key] = kv.Value;
                _nextLocalIndex = (uint)_paramCount;
                _localFirstState.Clear();
                _localCrossesState.Clear();
                _simdV128StoreCount = 0;
                _simdV128Values.Clear();
                _isStateMachine = false; // straight-line value emission within blocks

                // Pre-allocate header phi locals (v128 for lane-variant, scalar otherwise).
                foreach (var phi in headerPhis)
                {
                    if (laneVariant.Contains(phi))
                    {
                        AllocateLocal(phi, WasmOpCodes.V128);
                        _simdV128Values.Add(phi);
                    }
                    else
                    {
                        AllocateLocal(phi, GetWasmType(phi));
                    }
                }

                // entry block values, then init the header phis from the entry predecessor.
                foreach (var ve in entry) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }
                if (!WriteHeaderPhis(headerPhis, entry, laneVariant)) return false;

                // block $exit { loop $hdr {   (void block types, like the scalar state machine)
                Code.Add(WasmOpCodes.Block); Code.Add(WasmOpCodes.Void);
                Code.Add(WasmOpCodes.Loop); Code.Add(WasmOpCodes.Void);

                // header non-phi values (the loop condition compare etc.)
                foreach (var ve in header) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }
                // conditional exit: continue into body while staying in the loop; br_if 1 ($exit) to leave.
                EmitGetLocal(hib.Condition.Resolve());
                if (condTrueIsBody) Code.Add(WasmOpCodes.I32Eqz); // exit when !(cond)
                Code.Add(WasmOpCodes.BrIf); WasmModuleBuilder.EmitU32Leb128(Code, 1); // → $exit

                // body values, then update the header phis from the body (back-edge) predecessor.
                foreach (var ve in body) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }
                if (!WriteHeaderPhis(headerPhis, body, laneVariant)) return false;

                Code.Add(WasmOpCodes.Br); WasmModuleBuilder.EmitU32Leb128(Code, 0); // br $hdr
                Code.Add(WasmOpCodes.End); // end loop
                Code.Add(WasmOpCodes.End); // end block

                // exit block values + return 0 (matches scalar).
                foreach (var ve in exit) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }
                if (_simdV128StoreCount == 0) return false; // must do real per-lane vector work (the o[i] store)
                if (exit.Terminator != null) GenerateCodeFor(exit.Terminator);
                WasmModuleBuilder.EmitI32Const(Code, 0);

                SimdKernelCode = Code.ToArray();
                SimdKernelLocals = new List<WasmLocal>(_locals);
                HasSimdKernel = true;
                ok = true;
                return true;
            }
            finally
            {
                Code.Clear(); Code.AddRange(savedCode);
                _locals.Clear(); _locals.AddRange(savedLocals);
                _localMap.Clear(); foreach (var kv in savedMap) _localMap[kv.Key] = kv.Value;
                _nextLocalIndex = savedNext;
                _localFirstState.Clear(); foreach (var kv in savedFirstState) _localFirstState[kv.Key] = kv.Value;
                _localCrossesState.Clear(); foreach (var x in savedCrosses) _localCrossesState.Add(x);
                _isStateMachine = savedStateMachine;
                if (!ok) { HasSimdKernel = false; SimdKernelCode = null; SimdKernelLocals = null; }
            }
        }

        /// <summary>Writes each header phi's local from the operand contributed by <paramref name="pred"/>
        /// (entry = the loop-entry init; body = the back-edge update). Lane-variant phis are written as a
        /// v128 (the source v128 local, or a splat of a uniform init); lane-invariant (induction) phis are
        /// written scalar with i32/i64 width coercion, mirroring the scalar <c>PushPhiValues</c>.</summary>
        private bool WriteHeaderPhis(List<PhiValue> headerPhis, BasicBlock pred, HashSet<Value> laneVariant)
        {
            foreach (var phi in headerPhis)
            {
                int j = -1;
                for (int s = 0; s < phi.Count; s++) if (ReferenceEquals(phi.Sources[s], pred)) { j = s; break; }
                if (j < 0) return false;
                var src = phi[j].Resolve();
                var phiLocal = GetLocal(phi);
                if (laneVariant.Contains(phi))
                {
                    if (!PushAsV128(src, laneVariant)) return false; // v128 source, or splat of a uniform init
                }
                else
                {
                    EmitGetLocal(src);
                    var srcType = GetWasmTypeFromIR(src.Type);
                    var phiType = GetLocalType(phiLocal);
                    if (srcType == WasmOpCodes.I64 && phiType == WasmOpCodes.I32) Code.Add(WasmOpCodes.I32WrapI64);
                    else if (srcType == WasmOpCodes.I32 && phiType == WasmOpCodes.I64) Code.Add(WasmOpCodes.I64ExtendI32S);
                }
                WasmModuleBuilder.EmitLocalSet(Code, phiLocal);
            }
            return true;
        }

        /// <summary>
        /// Stage-3b (lite) MULTI-BLOCK path: vectorizes a divergent if-DIAMOND
        /// (entry[cond] → then → merge, entry → else → merge) — the shape a data-dependent ternary
        /// <c>o[i] = cond ? x : y</c> lowers to — via MASK-based IF-CONVERSION: compute the per-lane
        /// compare MASK, execute BOTH sides unconditionally (legal only because they are side-effect-free
        /// — pure loads + arithmetic; the single store lives in the merge block), then merge each result
        /// phi with <c>v128.bitselect(trueSideVal, falseSideVal, mask)</c>. No real per-lane branching.
        /// Detection: 4 acyclic blocks, entry IfBranch on a lane-variant comparison, then/else each
        /// unconditionally branch to the one merge block, merge returns. ANYTHING else ⇒ scalar fallback.
        /// Same context save/restore as the other SIMD paths (scalar kernel stays byte-identical).
        /// </summary>
        private bool TryGenerateSimdDiamondKernel()
        {
            if (_blockCount != 4) return false;
            var cfg = Method.Blocks.CreateCFG();
            if (cfg.CreateLoops().Count != 0) return false;   // acyclic diamond only (no loops)

            // entry = the block whose terminator is an IfBranch; it must be the method's first block.
            BasicBlock? entryB = null;
            foreach (var b in Method.Blocks) { if (b.Terminator is IfBranch) { entryB = b; break; } }
            if (entryB == null || !ReferenceEquals(entryB, Method.Blocks.First())) return false;
            var entry = entryB;
            var ifb = (IfBranch)entry.Terminator;
            var tTarget = ifb.TrueTarget; var fTarget = ifb.FalseTarget;
            if (ReferenceEquals(tTarget, fTarget)) return false;
            // then/else each unconditionally branch to the SAME merge block.
            if (tTarget.Terminator is not UnconditionalBranch tub) return false;
            if (fTarget.Terminator is not UnconditionalBranch fub) return false;
            if (!ReferenceEquals(tub.Target, fub.Target)) return false;
            var merge = tub.Target;
            if (merge.Terminator is not ReturnTerminator) return false;
            // 4 distinct blocks: entry, then, else, merge.
            if (ReferenceEquals(merge, entry) || ReferenceEquals(merge, tTarget) || ReferenceEquals(merge, fTarget)) return false;

            // The branch condition must be a lane-variant comparison we can emit as a v128 mask.
            var cond = ifb.Condition.Resolve();
            var analysis = WasmSimdAnalysis.Analyze(Method, _indexParam, out var laneVariant);
            if (laneVariant.Count == 0) return false;          // Analyze early-rejected (barrier/atomic/warp)
            if (cond is not CompareValue cmp || !laneVariant.Contains(cmp) || MapCompare(cmp) == 0) return false;

            // then/else MUST be side-effect-free (both run for all lanes). The store is in the merge block.
            foreach (var sideBlock in new[] { tTarget, fTarget })
                foreach (var ve in sideBlock)
                    if (ve.Value is Store or GenericAtomic or AtomicCAS or global::ILGPU.IR.Values.Barrier or MemoryBarrier or PredicateBarrier or MethodCall or Alloca)
                        return false; // side effect ⇒ cannot if-convert (would run on inactive lanes)

            // Merge phis (the diamond results) must select between the two sides and be a 4-lane class.
            var mergePhis = new List<PhiValue>();
            foreach (var ve in merge)
                if (ve.Value is PhiValue phi)
                {
                    if (phi.Count != 2) return false;
                    bool hasT = false, hasF = false;
                    for (int j = 0; j < phi.Count; j++)
                    {
                        if (ReferenceEquals(phi.Sources[j], tTarget)) hasT = true;
                        else if (ReferenceEquals(phi.Sources[j], fTarget)) hasF = true;
                    }
                    if (!hasT || !hasF) return false;
                    if (ClassOf(phi.Type) == LaneClass.None) return false; // must be a v128 (bitselect) result
                    mergePhis.Add(phi);
                }
            if (mergePhis.Count == 0) return false;

            // Class-gate every non-phi value (phis handled; terminators structural).
            foreach (var b in new[] { entry, tTarget, fTarget, merge })
                foreach (var ve in b)
                {
                    var v = ve.Value;
                    if (v is PhiValue) continue;
                    if (!IsStage3aEmittable(v, laneVariant)) return false;
                }

            // ── Save the scalar context (mirror the loop path) ──
            var savedCode = Code.ToArray();
            var savedLocals = new List<WasmLocal>(_locals);
            var savedMap = new Dictionary<string, uint>(_localMap);
            var savedNext = _nextLocalIndex;
            var savedFirstState = new Dictionary<uint, int>(_localFirstState);
            var savedCrosses = new HashSet<uint>(_localCrossesState);
            bool savedStateMachine = _isStateMachine;

            bool ok = false;
            try
            {
                Code.Clear();
                _locals.Clear();
                var paramMap = savedMap.Where(kv => kv.Value < _paramCount).ToDictionary(kv => kv.Key, kv => kv.Value);
                _localMap.Clear();
                foreach (var kv in paramMap) _localMap[kv.Key] = kv.Value;
                _nextLocalIndex = (uint)_paramCount;
                _localFirstState.Clear();
                _localCrossesState.Clear();
                _simdV128StoreCount = 0;
                _simdV128Values.Clear();
                _isStateMachine = false;

                // Pre-allocate the merge result phi locals (all v128).
                foreach (var phi in mergePhis) { AllocateLocal(phi, WasmOpCodes.V128); _simdV128Values.Add(phi); }

                // entry values (incl. the lane-variant compare → v128 mask), then BOTH sides unconditionally.
                foreach (var ve in entry) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }
                foreach (var ve in tTarget) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }
                foreach (var ve in fTarget) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }

                // Merge each result phi: bitselect(trueTargetVal, falseTargetVal, mask).
                foreach (var phi in mergePhis)
                {
                    Value trueOp = null!, falseOp = null!;
                    for (int j = 0; j < phi.Count; j++)
                    {
                        if (ReferenceEquals(phi.Sources[j], tTarget)) trueOp = phi[j].Resolve();
                        else if (ReferenceEquals(phi.Sources[j], fTarget)) falseOp = phi[j].Resolve();
                    }
                    if (trueOp == null || falseOp == null) return false;
                    if (!PushAsV128(trueOp, laneVariant)) return false;   // selected where cond (mask) = 1
                    if (!PushAsV128(falseOp, laneVariant)) return false;  // selected where cond = 0
                    EmitGetLocal(cmp);                                    // the v128 mask
                    WasmModuleBuilder.EmitSimd(Code, WasmOpCodes.V128Bitselect);
                    WasmModuleBuilder.EmitLocalSet(Code, GetLocal(phi));
                }

                // merge non-phi values (the store) + return 0.
                foreach (var ve in merge) { var v = ve.Value; if (v is PhiValue) continue; if (!EmitSimdValue(v, laneVariant)) return false; }
                if (_simdV128StoreCount == 0) return false;
                if (merge.Terminator != null) GenerateCodeFor(merge.Terminator);
                WasmModuleBuilder.EmitI32Const(Code, 0);

                SimdKernelCode = Code.ToArray();
                SimdKernelLocals = new List<WasmLocal>(_locals);
                HasSimdKernel = true;
                ok = true;
                return true;
            }
            finally
            {
                Code.Clear(); Code.AddRange(savedCode);
                _locals.Clear(); _locals.AddRange(savedLocals);
                _localMap.Clear(); foreach (var kv in savedMap) _localMap[kv.Key] = kv.Value;
                _nextLocalIndex = savedNext;
                _localFirstState.Clear(); foreach (var kv in savedFirstState) _localFirstState[kv.Key] = kv.Value;
                _localCrossesState.Clear(); foreach (var x in savedCrosses) _localCrossesState.Add(x);
                _isStateMachine = savedStateMachine;
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
                case ConvertValue cv:
                    return MapConvert(cv) != 0 && AllUsesVectorizable(cv);
                case CompareValue cmp:
                    return MapCompare(cmp) != 0 && AllUsesVectorizable(cmp);
                case Predicate pred:
                    return IsVectorizablePredicate(pred, laneVariant) && AllUsesVectorizable(pred);
                default:
                    // gather LEAs, shifts, f32->i32 convert, i64/f64 ⇒ later increments
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
                    case ConvertValue cv when MapConvert(cv) != 0:
                        break; // mapped lane conversion consumes its source as a vector lane
                    case CompareValue cmp when MapCompare(cmp) != 0:
                        break; // feeds a vector compare (operand lane)
                    case Predicate:
                        break; // feeds a select (condition mask / true / false value)
                    case IfBranch:
                        break; // a (lane-variant) compare feeding the if-DIAMOND condition — handled by bitselect
                    case PhiValue:
                        break; // feeds a loop-carried phi (the v128 accumulator update) — see the loop path
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
                case ConvertValue cv:
                {
                    uint op = MapConvert(cv);
                    if (op == 0) return false;
                    var target = AllocateLocal(cv, WasmOpCodes.V128);
                    if (!PushAsV128(cv.Value.Resolve(), laneVariant)) return false; // source v128 (i32x4)
                    WasmModuleBuilder.EmitSimd(Code, op);                             // -> f32x4
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    _simdV128Values.Add(cv);
                    return true;
                }
                case CompareValue cmp:
                {
                    uint op = MapCompare(cmp);
                    if (op == 0) return false;
                    var target = AllocateLocal(cmp, WasmOpCodes.V128);               // the per-lane mask
                    if (!PushAsV128(cmp.Left.Resolve(), laneVariant)) return false;
                    if (!PushAsV128(cmp.Right.Resolve(), laneVariant)) return false;
                    WasmModuleBuilder.EmitSimd(Code, op);                             // f32x4/i32x4 compare -> mask
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    _simdV128Values.Add(cmp);
                    return true;
                }
                case Predicate pred:
                {
                    if (!IsVectorizablePredicate(pred, laneVariant)) return false;
                    var target = AllocateLocal(pred, WasmOpCodes.V128);
                    // v128.bitselect(trueVal, falseVal, mask): selects trueVal lanes where mask bits are 1.
                    if (!PushAsV128(pred.TrueValue.Resolve(), laneVariant)) return false;
                    if (!PushAsV128(pred.FalseValue.Resolve(), laneVariant)) return false;
                    EmitGetLocal(pred.Condition.Resolve());                           // the v128 mask (a vectorized compare)
                    WasmModuleBuilder.EmitSimd(Code, WasmOpCodes.V128Bitselect);
                    WasmModuleBuilder.EmitLocalSet(Code, target);
                    _simdV128Values.Add(pred);
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
                    UnaryArithmeticKind.SqrtF => WasmOpCodes.F32x4Sqrt,   // IEEE sqrt — bit-identical to scalar f32.sqrt
                    UnaryArithmeticKind.FloorF => WasmOpCodes.F32x4Floor, // IEEE round-to-integral (toward -inf)
                    UnaryArithmeticKind.CeilingF => WasmOpCodes.F32x4Ceil,// IEEE round-to-integral (toward +inf)
                    _ => 0u, // transcendentals (sin/exp/…) are a later increment (need lane-wise math calls)
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

        /// <summary>Maps a 4-lane lane conversion to its v128 opcode, or 0 if unsupported in 3a.
        /// Only i32x4 → f32x4 (signed/unsigned) is supported: it rounds identically to the scalar
        /// <c>f32.convert_i32_s/_u</c>, so it is cross-mode EXACT. The reverse (f32 → i32) is NOT
        /// vectorized — wasm core SIMD only offers the SATURATING <c>trunc_sat</c>, but the scalar path
        /// traps (non-saturating), so they diverge on overflow/NaN (cross-mode determinism). Same-width
        /// only (4-lane↔4-lane); i32/f32 ↔ i64/f64 (4↔2 lane) is a later increment.</summary>
        private static uint MapConvert(ConvertValue cv)
        {
            var srcCls = ClassOf(cv.Value.Resolve().Type);
            var dstCls = ClassOf(cv.Type);
            if (srcCls == LaneClass.I32x4 && dstCls == LaneClass.F32x4)
                return (cv.Flags & ConvertFlags.SourceUnsigned) == ConvertFlags.SourceUnsigned
                    ? WasmOpCodes.F32x4ConvertI32x4U
                    : WasmOpCodes.F32x4ConvertI32x4S;
            return 0u;
        }

        /// <summary>Maps a comparison to its f32x4/i32x4 lane-compare opcode (result = per-lane all-ones/
        /// all-zeros MASK for <c>v128.bitselect</c>), or 0 if unsupported in 3a. Lane class is taken from
        /// the OPERANDS (the result is a boolean). i32 picks signed/unsigned from IsUnsignedOrUnordered.</summary>
        private static uint MapCompare(CompareValue cv)
        {
            var cls = ClassOf(cv.Left.Resolve().Type);
            if (cls == LaneClass.F32x4)
                return cv.Kind switch
                {
                    CompareKind.Equal => WasmOpCodes.F32x4Eq,
                    CompareKind.NotEqual => WasmOpCodes.F32x4Ne,
                    CompareKind.LessThan => WasmOpCodes.F32x4Lt,
                    CompareKind.LessEqual => WasmOpCodes.F32x4Le,
                    CompareKind.GreaterThan => WasmOpCodes.F32x4Gt,
                    CompareKind.GreaterEqual => WasmOpCodes.F32x4Ge,
                    _ => 0u,
                };
            if (cls == LaneClass.I32x4)
            {
                bool u = cv.IsUnsignedOrUnordered;
                return cv.Kind switch
                {
                    CompareKind.Equal => WasmOpCodes.I32x4Eq,
                    CompareKind.NotEqual => WasmOpCodes.I32x4Ne,
                    CompareKind.LessThan => u ? WasmOpCodes.I32x4LtU : WasmOpCodes.I32x4LtS,
                    CompareKind.LessEqual => u ? WasmOpCodes.I32x4LeU : WasmOpCodes.I32x4LeS,
                    CompareKind.GreaterThan => u ? WasmOpCodes.I32x4GtU : WasmOpCodes.I32x4GtS,
                    CompareKind.GreaterEqual => u ? WasmOpCodes.I32x4GeU : WasmOpCodes.I32x4GeS,
                    _ => 0u,
                };
            }
            return 0u;
        }

        /// <summary>A lane-variant Predicate (select) is vectorizable iff its CONDITION is a lane-variant
        /// comparison we can emit as a v128 mask, and both selected values are a 4-lane class. (A uniform
        /// condition would need a typed v128 select — deferred.)</summary>
        private bool IsVectorizablePredicate(Predicate p, HashSet<Value> laneVariant)
        {
            if (ClassOf(p.Type) == LaneClass.None) return false;
            var cond = p.Condition.Resolve();
            return cond is CompareValue cc && laneVariant.Contains(cc) && MapCompare(cc) != 0;
        }
    }
}
