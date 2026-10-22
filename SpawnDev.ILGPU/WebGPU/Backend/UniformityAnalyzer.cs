// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGPU
//                        Copyright (c) 2024 SpawnDev Project
//
// File: UniformityAnalyzer.cs
//
// IR-level uniformity analysis for WGSL loop break conditions.
// Extracted from WGSLKernelFunctionGenerator (Phase 1.3).
// ---------------------------------------------------------------------------------------

using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using global::ILGPU.IR.Analyses.ControlFlowDirection;
using global::ILGPU.IR.Analyses.TraversalOrders;
using global::ILGPU.IR.Values;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Result of tracing an IR value to its builtin source.
    /// Used to classify loop counter variables for uniformity analysis.
    /// </summary>
    internal enum BuiltinTraceResult
    {
        /// <summary>No builtin source found (e.g., parameter, constant, or trace depth exceeded).</summary>
        Unknown,
        /// <summary>Traces to GridIndexValue (group_id) — UNIFORM within a workgroup.</summary>
        GridIndex,
        /// <summary>Traces to GroupIndexValue (local_id) — NON-UNIFORM (per-thread).</summary>
        GroupIndex,
        /// <summary>Traces to GridDimensionValue (num_workgroups) — UNIFORM.</summary>
        GridDimension,
        /// <summary>Traces to GroupDimensionValue (workgroup_size) — UNIFORM.</summary>
        GroupDimension,
        /// <summary>Traces to mixed sources including at least one non-uniform source.</summary>
        MixedNonUniform
    }

    /// <summary>
    /// Classifies a loop by its counter increment pattern.
    /// Used to avoid applying grid-stride uniformity transforms to tile loops.
    /// </summary>
    internal enum LoopType
    {
        /// <summary>Unrecognized loop pattern — apply synthetic counter conservatively.</summary>
        Unknown,
        /// <summary>Counter increments by GroupDimension (workgroup_size). All threads
        /// execute the same number of iterations — break condition is already uniform.</summary>
        TileLoop,
        /// <summary>Counter increments by a value involving GridDimension (num_workgroups).
        /// Needs synthetic uniform counter for barrier-containing loops.</summary>
        GridStrideLoop
    }

    /// <summary>
    /// IR-level uniformity analysis for WGSL loop break conditions.
    ///
    /// WGSL uniformity analysis is SYNTACTIC: the browser's WGSL validator traces
    /// variable origins through the control flow graph. Even if a break condition is
    /// mathematically uniform, the validator rejects it if ANY value in the expression
    /// chain traces back to local_invocation_id (non-uniform).
    ///
    /// This analyzer classifies IR values and loop counters to determine:
    /// - Whether a loop is a tile loop (step = GroupDimension) or grid-stride loop
    /// - Whether a value traces to a uniform or non-uniform builtin source
    /// - How to decompose an expression to remove GroupIndex (local_id) terms
    /// </summary>
    internal static class UniformityAnalyzer
    {
        /// <summary>
        /// Recursively traces an IR value to find if it originates from a device builtin.
        /// This is used to determine whether a loop counter phi variable derives from
        /// group_id (uniform) or local_id (non-uniform).
        /// </summary>
        /// <param name="value">The IR value to trace.</param>
        /// <param name="maxDepth">Maximum recursion depth to prevent infinite loops.</param>
        /// <returns>The builtin classification of the value's origin.</returns>
        public static BuiltinTraceResult TraceToBuiltinSource(Value value, int maxDepth = 12)
        {
            if (maxDepth <= 0) return BuiltinTraceResult.Unknown;

            // Direct builtin matches
            if (value is GridIndexValue)
                return BuiltinTraceResult.GridIndex;
            if (value is GroupIndexValue)
                return BuiltinTraceResult.GroupIndex;
            if (value is GridDimensionValue)
                return BuiltinTraceResult.GridDimension;
            if (value is GroupDimensionValue)
                return BuiltinTraceResult.GroupDimension;

            // Unary operations preserve the source classification
            if (value is UnaryArithmeticValue unary)
                return TraceToBuiltinSource(unary.Value.Resolve(), maxDepth - 1);

            // Convert operations preserve the source
            if (value is ConvertValue convert)
                return TraceToBuiltinSource(convert.Value.Resolve(), maxDepth - 1);

            // Binary operations: combine both operands' classifications
            if (value is BinaryArithmeticValue binary)
            {
                var left = TraceToBuiltinSource(binary.Left.Resolve(), maxDepth - 1);
                var right = TraceToBuiltinSource(binary.Right.Resolve(), maxDepth - 1);
                return CombineTraceResults(left, right);
            }

            // CompareValue: check both sides
            if (value is CompareValue compare)
            {
                var left = TraceToBuiltinSource(compare.Left.Resolve(), maxDepth - 1);
                var right = TraceToBuiltinSource(compare.Right.Resolve(), maxDepth - 1);
                return CombineTraceResults(left, right);
            }

            // PhiValue: check all node values (not Sources, which are predecessor blocks)
            if (value is PhiValue phi)
            {
                BuiltinTraceResult combined = BuiltinTraceResult.Unknown;
                for (int idx = 0; idx < phi.Nodes.Length; idx++)
                {
                    var nodeVal = phi.Nodes[idx].Resolve();
                    // Skip self-references to avoid infinite recursion in loops
                    if (nodeVal == phi) continue;
                    var result = TraceToBuiltinSource(nodeVal, maxDepth - 1);
                    combined = CombineTraceResults(combined, result);
                }
                return combined;
            }

            // MethodCall: trace through all arguments (result is uniform if all args are uniform)
            // This handles grid stride computations like num_workgroups * workgroup_size
            if (value is MethodCall methodCall)
            {
                BuiltinTraceResult combined = BuiltinTraceResult.Unknown;
                for (int idx = 0; idx < methodCall.Nodes.Length; idx++)
                {
                    var argVal = methodCall.Nodes[idx].Resolve();
                    var result = TraceToBuiltinSource(argVal, maxDepth - 1);
                    combined = CombineTraceResults(combined, result);
                }
                return combined;
            }

            // Broadcast: output is UNIFORM by definition (broadcasts a single value to all threads)
            // Trace through the source value to determine the classification
            if (value is Broadcast broadcast)
                return TraceToBuiltinSource(broadcast.Variable.Resolve(), maxDepth - 1);

            // GetField: trace through the source object
            if (value is GetField getField)
                return TraceToBuiltinSource(getField.ObjectValue.Resolve(), maxDepth - 1);

            // PrimitiveValue (constants): always uniform
            if (value is PrimitiveValue)
                return BuiltinTraceResult.Unknown; // Unknown = no builtin source, but not non-uniform

            // Constants and parameters are Unknown (not builtin-sourced)
            return BuiltinTraceResult.Unknown;
        }

        /// <summary>
        /// Combines two trace results. If either contains GroupIndex (non-uniform),
        /// the result is MixedNonUniform. Otherwise, returns the more specific result.
        /// </summary>
        public static BuiltinTraceResult CombineTraceResults(BuiltinTraceResult a, BuiltinTraceResult b)
        {
            // If either is explicitly non-uniform, the combination is non-uniform
            if (a == BuiltinTraceResult.GroupIndex || a == BuiltinTraceResult.MixedNonUniform ||
                b == BuiltinTraceResult.GroupIndex || b == BuiltinTraceResult.MixedNonUniform)
                return BuiltinTraceResult.MixedNonUniform;

            // If one is unknown, return the other
            if (a == BuiltinTraceResult.Unknown) return b;
            if (b == BuiltinTraceResult.Unknown) return a;

            // Both are uniform builtins — if they're the same type, keep it;
            // otherwise return the first (both are uniform, doesn't matter which)
            return a;
        }

        /// <summary>
        /// Classifies a loop as tile loop or grid-stride loop by analyzing
        /// the phi counter's back-edge increment expression.
        ///
        /// Tile loops increment by GroupDimension (workgroup_size) — all threads
        /// execute the same number of iterations, so the break condition is
        /// already uniform. No synthetic counter needed.
        ///
        /// Grid-stride loops increment by a value involving GridDimension
        /// (num_workgroups) — the synthetic uniform counter IS needed.
        /// </summary>
        public static LoopType ClassifyLoopType(
            PhiValue threadCounterPhi,
            Loops<ReversePostOrder, Forwards>.Node loopNode)
        {
            // Find the back-edge: the phi input whose source block is inside the loop.
            for (int i = 0; i < threadCounterPhi.Sources.Length; i++)
            {
                var sourceBlock = threadCounterPhi.Sources[i];
                bool isLoopBlock = false;
                foreach (var member in loopNode.AllMembers)
                {
                    if (member == sourceBlock)
                    {
                        isLoopBlock = true;
                        break;
                    }
                }
                if (!isLoopBlock)
                    continue;

                // This is the back-edge value — the expression that updates the
                // counter at the end of each iteration.
                var updateValue = threadCounterPhi.Nodes[i].Resolve();

                // Expect: Add(counter, stepSize) or Add(stepSize, counter)
                if (updateValue is BinaryArithmeticValue binary &&
                    binary.Kind == BinaryArithmeticKind.Add)
                {
                    var left = binary.Left.Resolve();
                    var right = binary.Right.Resolve();

                    // Determine which operand is the step (the one that isn't the phi)
                    Value stepValue = null;
                    if (left == threadCounterPhi)
                        stepValue = right;
                    else if (right == threadCounterPhi)
                        stepValue = left;

                    if (stepValue != null)
                    {
                        var stepTrace = TraceToBuiltinSource(stepValue, 8);

                        if (WebGPUBackend.VerboseLogging)
                            WebGPUBackend.Log($"[Uniformity] ClassifyLoopType: step traces to {stepTrace}");

                        if (stepTrace == BuiltinTraceResult.GroupDimension)
                            return LoopType.TileLoop;

                        if (stepTrace == BuiltinTraceResult.GridDimension)
                            return LoopType.GridStrideLoop;
                    }
                }

                // Could not classify — return Unknown
                return LoopType.Unknown;
            }

            return LoopType.Unknown;
        }

        /// <summary>
        /// Finds the phi's initial value — the input from a block OUTSIDE the loop.
        /// This is the value assigned to the phi before the first iteration.
        /// </summary>
        public static Value FindPhiInitValue(
            PhiValue phi,
            Loops<ReversePostOrder, Forwards>.Node loopNode)
        {
            for (int i = 0; i < phi.Sources.Length; i++)
            {
                var sourceBlock = phi.Sources[i];
                bool isLoopBlock = false;
                foreach (var member in loopNode.AllMembers)
                {
                    if (member == sourceBlock)
                    {
                        isLoopBlock = true;
                        break;
                    }
                }
                if (!isLoopBlock)
                    return phi.Nodes[i].Resolve();
            }
            return null;
        }

        /// <summary>
        /// True when <paramref name="value"/> depends on something WGSL's uniformity analysis cannot prove uniform
        /// and the thread-counter transforms did not already replace: a load from global or shared memory (every
        /// kernel view is bound <c>read_write</c>, so its loads are non-uniform to the validator even when all
        /// threads read the same element), a warp shuffle, or an atomic. A loop whose break depends on such a value
        /// and that contains a barrier is rejected ("'workgroupBarrier' must only be called from uniform control
        /// flow"). Loads from the thread's own local (private) memory are tracked through assignments by the
        /// validator and are not counted. Scalar kernel parameters are not IR loads (read-only storage, uniform).
        /// </summary>
        /// <param name="value">The break condition.</param>
        /// <returns>True if the value may be non-uniform.</returns>
        public static bool DependsOnNonUniformMemory(Value value)
        {
            var visited = new HashSet<Value>();
            var stack = new Stack<Value>();
            stack.Push(value);
            while (stack.Count > 0 && visited.Count < 512)
            {
                var current = stack.Pop().Resolve();
                if (!visited.Add(current)) continue;
                switch (current)
                {
                    case Load load:
                        if (load.SourceAddressSpace == MemoryAddressSpace.Local)
                            break;   // the thread's own private memory: the validator follows its assignments
                        return true;
                    case WarpShuffle:
                    case SubWarpShuffle:
                    case AtomicValue:
                        return true;
                }
                foreach (var node in current.Nodes)
                    stack.Push(node.Resolve());
            }
            return false;
        }

        /// <summary>
        /// Checks if any block within a loop node contains a memory barrier instruction.
        /// This is used to determine if a loop needs uniformity analysis for its break condition.
        /// </summary>
        /// <param name="loopNode">The loop analysis node containing all blocks in the loop.</param>
        /// <returns>True if any block in the loop contains a MemoryBarrier.</returns>
        public static bool LoopContainsBarrier(Loops<ReversePostOrder, Forwards>.Node loopNode)
        {
            foreach (var block in loopNode.AllMembers)
            {
                foreach (var valueEntry in block)
                {
                    if (valueEntry.Value is MemoryBarrier)
                        return true;
                }
            }
            // Note: Barriers in inlined helpers won't appear as IR MemoryBarrier here,
            // since inlining happens during WGSL code emission (not at the IR level).
            // The post-processor's text-based "workgroupBarrier()" detection handles those.
            return false;
        }

        /// <summary>
        /// Attempts to produce a WGSL expression equivalent to <paramref name="value"/>
        /// but with any GroupIndex (local_invocation_id) addend removed.
        ///
        /// This is used for tile loops: the phi init is e.g.
        ///   Add(Add(Mul(GridIdx, tileSize), GroupIdx), GroupDim)
        /// and the uniform version (with GroupIdx removed) is
        ///   Add(Mul(GridIdx, tileSize), GroupDim)
        /// which equals the counter value for thread 0.
        ///
        /// Returns the WGSL expression string for the uniform part.
        /// Returns "" if the value IS the GroupIndex term (removed).
        /// Returns null if decomposition fails.
        /// </summary>
        /// <param name="value">The IR value to decompose.</param>
        /// <param name="loadValue">A delegate that converts an IR Value to its WGSL variable name.</param>
        /// <returns>The WGSL expression string with GroupIndex removed, "" if pure GroupIndex, or null on failure.</returns>
        public static string TryRemoveGroupIndex(Value value, Func<Value, string> loadValue)
        {
            var trace = TraceToBuiltinSource(value, 8);

            // Pure GroupIndex — mark for removal
            if (trace == BuiltinTraceResult.GroupIndex)
                return "";

            // Uniform or Unknown (constants, parameters) — keep as-is
            if (trace != BuiltinTraceResult.MixedNonUniform)
                return loadValue(value);

            // MixedNonUniform — try to decompose if it's an Add
            if (value is BinaryArithmeticValue binary &&
                binary.Kind == BinaryArithmeticKind.Add)
            {
                string left = TryRemoveGroupIndex(binary.Left.Resolve(), loadValue);
                string right = TryRemoveGroupIndex(binary.Right.Resolve(), loadValue);

                if (left == null || right == null)
                    return null; // one side can't be decomposed

                if (left == "" && right == "")
                    return ""; // both were GroupIndex

                if (left == "")
                    return right; // left was GroupIndex, keep right

                if (right == "")
                    return left; // right was GroupIndex, keep left

                return $"({left} + {right})";
            }

            // MixedNonUniform but not Add — can't decompose
            return null;
        }
    }
}
