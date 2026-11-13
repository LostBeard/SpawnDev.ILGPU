// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.Wasm
//                    Structured control flow for functions without barriers
// ---------------------------------------------------------------------------------------
//
// WHY. The state machine (GenerateStateMachineCode) turns every branch between basic blocks
// into "set the state local, br to the loop, go through a br_table over ALL blocks", in a
// nest of one wasm block per basic block. That is what a barrier needs (a kernel that yields
// must be re-entered in the middle), but it is paid by every multi-block function, and the
// price of a branch grows with the size of the function. Measured 2026-11-13 (one thread, a
// loop of xorshift steps each followed by a data-dependent branch with a store, ns per step
// for a loop body of 12 / 120 / 960 steps): desktop CPU accelerator 1.65 / 1.73 / 2.32,
// Wasm 2.66 / 3.85 / 22.76. The same steps without branches: 0.41 native, 0.42 Wasm. A real
// kernel of a few thousand blocks (SpawnDev.Codecs' AV1 range coder pass) ran 35x slower
// than on the desktop CPU accelerator.
//
// WHAT. A function that never yields (no barrier-like value, no call to a helper that has
// one, no phase mode) needs no re-entry, so its control flow can be what wasm has: block,
// loop, if, and br to a label. The translation is the one of N. Ramsey, "Beyond Relooper:
// recursive translation of unstructured control flow to structured control flow" (ICFP 2022),
// for reducible control flow graphs:
//
//   - The blocks are numbered in reverse post order. An edge to a block with a number not
//     greater than its source's is a back edge; its target is a LOOP HEADER. A block with two
//     or more forward edges into it is a MERGE NODE.
//   - A block X is translated with the merge nodes it immediately dominates, Y1 < Y2 < ...:
//         block  block  [X's code and terminator]  end  [Y1]  end  [Y2]
//     so a forward branch to a merge node is a br to the block that ends where the node begins.
//     A loop header's translation is wrapped in  loop ... end ; a back edge is a br to it.
//   - A conditional branch is  if [true edge] else [false edge] end . An edge is: the phi
//     copies of that edge, then a br to the label of its target (loop header: back edge; merge
//     node), or, for a target with a single forward edge into it, the target's translation
//     itself, right there.
//
// A graph is reducible exactly when the target of every back edge dominates its source. A
// function for which that does not hold (or which the checks below reject) keeps the state
// machine; nothing else changes. The translation is ITERATIVE (an explicit work stack): a
// kernel can have thousands of blocks in a chain, and the code generator also runs on the
// interpreter's stack in the browser.

using global::ILGPU.IR;
using global::ILGPU.IR.Analyses.ControlFlowDirection;
using global::ILGPU.IR.Analyses.TraversalOrders;
using global::ILGPU.IR.Values;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    public partial class WasmKernelFunctionGenerator
    {
        /// <summary>What a label on the wasm control stack of the structured translation is.</summary>
        private enum ScfLabel : byte
        {
            /// <summary>An <c>if</c> frame (never a branch target; it only counts as a level).</summary>
            If,
            /// <summary>A <c>loop</c> headed by a block: the target of the back edges to that block.</summary>
            Loop,
            /// <summary>A <c>block</c> that ends where a merge node begins: the target of the forward edges to it.</summary>
            Block,
        }

        /// <summary>The steps of the structured translation's work stack.</summary>
        private enum ScfStep : byte
        {
            /// <summary>Translate block A with everything it dominates.</summary>
            Tree,
            /// <summary>Translate block A inside the blocks of its first N merge-node children.</summary>
            Within,
            /// <summary>Emit the edge from block A to block B.</summary>
            Edge,
            /// <summary>Emit <c>end</c> and drop the innermost label.</summary>
            End,
            /// <summary>Emit <c>else</c>.</summary>
            Else,
            /// <summary>Emit the test of case N of block A's switch, open its <c>if</c>.</summary>
            SwitchCase,
        }

        private readonly struct ScfWork
        {
            public ScfWork(ScfStep step, int a = 0, int b = 0, int n = 0) { Step = step; A = a; B = b; N = n; }
            public readonly ScfStep Step;
            public readonly int A;
            public readonly int B;
            public readonly int N;
        }

        /// <summary>
        /// Emits the function's body with structured control flow when the function allows it (see the file
        /// header): no barrier-like value, no phase mode, only return / branch / conditional branch / switch
        /// terminators, and a reducible control flow graph. Returns false, having emitted nothing, when the
        /// function has to use the state machine. Leaves no value on the stack: the caller pushes the result.
        /// </summary>
        private bool TryGenerateStructuredCode(BasicBlockCollection<ReversePostOrder, Forwards> blocks)
        {
            if (!WasmBackend.StructuredControlFlow) return false;
            // The conditions under which GenerateStateMachineCode would be in phase mode.
            if (_generatorArgs.PhaseCount > 1) return false;
            foreach (var kv in _generatorArgs.HelperBarrierCounts)
                if (kv.Value > 0) return false;

            // ---- The blocks in reverse post order, and what must not be in them ----
            var order = new Dictionary<BasicBlock, int>();
            var list = new List<BasicBlock>();
            foreach (var block in blocks)
            {
                order[block] = list.Count;
                list.Add(block);
            }
            int n = list.Count;
            for (int i = 0; i < n; i++)
            {
                foreach (var entry in list[i])
                {
                    var value = entry.Value;
                    if (value is global::ILGPU.IR.Values.Barrier || value is PredicateBarrier || value is Broadcast || value is ShuffleOperation)
                        return false;
                }
                var terminator = list[i].Terminator;
                if (!(terminator is ReturnTerminator || terminator is UnconditionalBranch
                    || terminator is IfBranch || terminator is SwitchBranch))
                    return false;
            }

            // ---- Edges (a switch can name a block twice: that is two edges) ----
            var successors = new int[n][];
            var forwardIn = new int[n];
            var isLoopHeader = new bool[n];
            var predecessors = new List<int>[n];
            for (int i = 0; i < n; i++) predecessors[i] = new List<int>();
            for (int i = 0; i < n; i++)
            {
                var span = list[i].Successors;
                var targets = new int[span.Length];
                for (int k = 0; k < span.Length; k++)
                {
                    if (!order.TryGetValue(span[k], out int t)) return false;
                    targets[k] = t;
                    predecessors[t].Add(i);
                    if (t <= i) isLoopHeader[t] = true;
                    else forwardIn[t]++;
                }
                successors[i] = targets;
            }

            // ---- Immediate dominators (Cooper, Harvey, Kennedy: "A simple, fast dominance algorithm") ----
            var idom = new int[n];
            for (int i = 0; i < n; i++) idom[i] = -1;
            idom[0] = 0;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int b = 1; b < n; b++)
                {
                    int best = -1;
                    var preds = predecessors[b];
                    for (int k = 0; k < preds.Count; k++)
                    {
                        int p = preds[k];
                        if (idom[p] < 0) continue;
                        if (best < 0) { best = p; continue; }
                        int x = p, y = best;
                        while (x != y)
                        {
                            while (x > y) x = idom[x];
                            while (y > x) y = idom[y];
                        }
                        best = x;
                    }
                    if (best < 0) return false;   // a block no processed block reaches: not a graph we translate
                    if (idom[b] != best) { idom[b] = best; changed = true; }
                }
            }

            // ---- Reducible: the target of every back edge dominates its source ----
            for (int i = 0; i < n; i++)
            {
                var targets = successors[i];
                for (int k = 0; k < targets.Length; k++)
                {
                    int t = targets[k];
                    if (t > i) continue;
                    int d = i;
                    while (d > t) d = idom[d];
                    if (d != t) return false;
                }
            }

            // ---- The merge nodes each block immediately dominates, in ascending order ----
            var mergeChildren = new List<int>[n];
            for (int b = 1; b < n; b++)
            {
                if (forwardIn[b] < 2) continue;
                (mergeChildren[idom[b]] ??= new List<int>()).Add(b);
            }

            // ---- Translate ----
            _isStateMachine = false;
            _phaseMode = false;
            _blockMap.Clear();
            for (int i = 0; i < n; i++) _blockMap[list[i]] = i;

            var labels = new List<(ScfLabel Kind, int Block)>();
            var work = new Stack<ScfWork>();
            work.Push(new ScfWork(ScfStep.Tree, 0));
            while (work.Count > 0)
            {
                var w = work.Pop();
                switch (w.Step)
                {
                    case ScfStep.Tree:
                    {
                        if (isLoopHeader[w.A])
                        {
                            Code.Add(WasmOpCodes.Loop);
                            Code.Add(WasmOpCodes.Void);
                            labels.Add((ScfLabel.Loop, w.A));
                            work.Push(new ScfWork(ScfStep.End));
                        }
                        work.Push(new ScfWork(ScfStep.Within, w.A, 0, mergeChildren[w.A]?.Count ?? 0));
                        break;
                    }
                    case ScfStep.Within:
                    {
                        if (w.N > 0)
                        {
                            // block [X within its first N - 1 merge children] end [merge child N]
                            int child = mergeChildren[w.A]![w.N - 1];
                            Code.Add(WasmOpCodes.Block);
                            Code.Add(WasmOpCodes.Void);
                            labels.Add((ScfLabel.Block, child));
                            work.Push(new ScfWork(ScfStep.Tree, child));
                            work.Push(new ScfWork(ScfStep.End));
                            work.Push(new ScfWork(ScfStep.Within, w.A, 0, w.N - 1));
                            break;
                        }
                        // X's own code, then its terminator
                        var block = list[w.A];
                        _currentBlockEmitIndex = w.A;
                        foreach (var entry in block)
                            GenerateCodeFor(entry);
                        switch (block.Terminator)
                        {
                            case ReturnTerminator ret:
                                EmitStructuredReturn(ret);
                                break;
                            case UnconditionalBranch jump:
                                work.Push(new ScfWork(ScfStep.Edge, w.A, order[jump.Target]));
                                break;
                            case IfBranch branch:
                                // The condition is read before either arm runs its edge's phi copies.
                                EmitGetLocal(branch.Condition.Resolve());
                                Code.Add(WasmOpCodes.If);
                                Code.Add(WasmOpCodes.Void);
                                labels.Add((ScfLabel.If, -1));
                                work.Push(new ScfWork(ScfStep.End));
                                work.Push(new ScfWork(ScfStep.Edge, w.A, order[branch.FalseTarget]));
                                work.Push(new ScfWork(ScfStep.Else));
                                work.Push(new ScfWork(ScfStep.Edge, w.A, order[branch.TrueTarget]));
                                break;
                            case SwitchBranch select:
                                // if (v == 0) [edge 0] end  if (v == 1) [edge 1] end  ...  [default edge]
                                // An arm always leaves (br, return, or a translation that does), so the next
                                // test is reached only when the case did not match.
                                work.Push(new ScfWork(ScfStep.Edge, w.A, order[select.DefaultBlock]));
                                for (int i = select.NumCasesWithoutDefault - 1; i >= 0; i--)
                                    work.Push(new ScfWork(ScfStep.SwitchCase, w.A, 0, i));
                                break;
                        }
                        break;
                    }
                    case ScfStep.SwitchCase:
                    {
                        var select = (SwitchBranch)list[w.A].Terminator!;
                        EmitGetLocal(select.Condition.Resolve());
                        WasmModuleBuilder.EmitI32Const(Code, w.N);
                        Code.Add(WasmOpCodes.I32Eq);
                        Code.Add(WasmOpCodes.If);
                        Code.Add(WasmOpCodes.Void);
                        labels.Add((ScfLabel.If, -1));
                        work.Push(new ScfWork(ScfStep.End));
                        work.Push(new ScfWork(ScfStep.Edge, w.A, order[select.GetCaseTarget(w.N)]));
                        break;
                    }
                    case ScfStep.Edge:
                    {
                        // The phi copies of THIS edge, then the jump (or the target itself).
                        PushPhiValues(list[w.A], list[w.B]);
                        bool back = w.B <= w.A;
                        if (!back && forwardIn[w.B] < 2)
                        {
                            work.Push(new ScfWork(ScfStep.Tree, w.B));
                            break;
                        }
                        var kind = back ? ScfLabel.Loop : ScfLabel.Block;
                        int depth = -1;
                        for (int k = labels.Count - 1; k >= 0; k--)
                        {
                            if (labels[k].Kind == kind && labels[k].Block == w.B)
                            {
                                depth = labels.Count - 1 - k;
                                break;
                            }
                        }
                        if (depth < 0)
                            throw new InvalidOperationException(
                                $"Wasm structured control flow: no {kind} label for block {w.B} at the edge from block {w.A} of '{Method.Name}'.");
                        Code.Add(WasmOpCodes.Br);
                        WasmModuleBuilder.EmitU32Leb128(Code, (uint)depth);
                        break;
                    }
                    case ScfStep.End:
                        Code.Add(WasmOpCodes.End);
                        labels.RemoveAt(labels.Count - 1);
                        break;
                    case ScfStep.Else:
                        Code.Add(WasmOpCodes.Else);
                        break;
                }
            }
            if (labels.Count != 0)
                throw new InvalidOperationException($"Wasm structured control flow: {labels.Count} labels left open in '{Method.Name}'.");

            // What the state machine's tail does for a function that is not in phase mode.
            if (!_isHelperFunction)
                _generatorArgs.PhaseCount = 1;
            WasmBackend.StructuredFunctionCount++;
            if (WasmBackend.VerboseLogging) WasmBackend.Log($"[Wasm-SCF] '{Method.Name}': {n} blocks as structured control flow");
            return true;
        }

        /// <summary>
        /// A return in the structured translation: the kernel returns i32 0 (done); a helper returns its value.
        /// </summary>
        private void EmitStructuredReturn(ReturnTerminator ret)
        {
            if (!_isHelperFunction)
            {
                WasmModuleBuilder.EmitI32Const(Code, 0);
            }
            else if (_helperResultType.HasValue)
            {
                if (!ret.IsVoidReturn) EmitGetLocal(ret.ReturnValue.Resolve());
                else WasmModuleBuilder.EmitLocalGet(Code, _helperResultLocal);
            }
            Code.Add(WasmOpCodes.Return);
        }
    }
}
