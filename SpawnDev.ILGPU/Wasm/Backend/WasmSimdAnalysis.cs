// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmSimdAnalysis.cs
//
// Wasm SIMD128 port — Stage 3a uniformity analysis (Velocity-model lane classification).
// See Plans/wasm-simd128-phase3-velocity-port-design-2026-06-14.md.
//
// PURE ANALYSIS, no codegen side effects. Classifies each IR value as lane-INVARIANT (the
// same for every lane of a SIMD warp — kernel params, group dims, loop bounds, block-uniform
// scales, loads from uniform addresses) or lane-VARIANT (depends on the per-lane thread index
// or a load from a lane-variant address). Stage 3a vectorizes a kernel ONLY when its control
// flow is lane-uniform (no lane-variant branch) and it has no barriers / atomics / warp
// shuffles — those need Stage 3b (masks) / 3c (real shuffles). Lane-invariant values stay
// scalar (and are splatted into v128 only where they feed a vector op); lane-variant values
// become v128. The scalar emitter remains the fallback + the cross-mode correctness oracle.
// ---------------------------------------------------------------------------------------

using System.Collections.Generic;
using global::ILGPU.IR;
using global::ILGPU.IR.Values;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>Result of <see cref="WasmSimdAnalysis.Analyze"/>.</summary>
    public readonly struct WasmSimdAnalysisResult
    {
        /// <summary>True if the kernel is eligible for Stage 3a SIMD codegen (uniform control
        /// flow, no barriers/atomics/warp ops). False ⇒ emit the scalar path (no regression).</summary>
        public bool Vectorizable { get; init; }
        /// <summary>Human-readable reason (why not vectorizable, or the green-light reason).</summary>
        public string Reason { get; init; }
        /// <summary>Count of values classified lane-variant.</summary>
        public int LaneVariantCount { get; init; }
        /// <summary>Total IR values examined.</summary>
        public int TotalValues { get; init; }

        public override string ToString() =>
            $"Vectorizable={Vectorizable} ({Reason}); laneVariant={LaneVariantCount}/{TotalValues}";
    }

    /// <summary>Stage 3a lane-uniformity analysis for the Wasm SIMD128 port.</summary>
    public static class WasmSimdAnalysis
    {
        /// <summary>
        /// Classifies lane-variant values and decides Stage-3a vectorizability.
        /// </summary>
        /// <param name="method">The kernel IR method.</param>
        /// <param name="indexParam">The kernel's index parameter (the per-lane thread index seed),
        /// or null if none was identified (⇒ not vectorizable).</param>
        /// <param name="laneVariant">Receives the set of lane-variant values (for the emitter to
        /// decide v128 vs scalar per value).</param>
        public static WasmSimdAnalysisResult Analyze(
            Method method,
            global::ILGPU.IR.Values.Parameter? indexParam,
            out HashSet<Value> laneVariant)
        {
            laneVariant = new HashSet<Value>();
            if (method == null)
                return new WasmSimdAnalysisResult { Vectorizable = false, Reason = "no method" };
            if (indexParam == null)
                return new WasmSimdAnalysisResult { Vectorizable = false, Reason = "no index parameter" };

            // Collect all values once (the IR is immutable here).
            var allValues = new List<Value>();
            foreach (var block in method.Blocks)
                foreach (var v in block)
                    allValues.Add(v);
            int total = allValues.Count;

            // Reject ops that need a later stage BEFORE the (cheaper) fixpoint — barriers (3b),
            // atomics (no v128 atomic; 3b+), warp shuffles/broadcast (3c). A single occurrence
            // disqualifies the whole kernel from Stage 3a (it falls back to scalar emit).
            foreach (var v in allValues)
            {
                switch (v.ValueKind)
                {
                    case ValueKind.Barrier:
                    case ValueKind.PredicateBarrier:
                    case ValueKind.MemoryBarrier:
                        return Reject("kernel has a barrier (Stage 3b+)", total);
                    case ValueKind.GenericAtomic:
                    case ValueKind.AtomicCAS:
                        return Reject("kernel has an atomic (v128 has no atomic store; Stage 3b+)", total);
                    case ValueKind.WarpShuffle:
                    case ValueKind.SubWarpShuffle:
                    case ValueKind.Broadcast:
                        return Reject("kernel has a warp shuffle/broadcast (Stage 3c)", total);
                }
            }

            // Seed: the per-lane index is lane-variant. Everything else becomes variant only by
            // (transitive) dependence on it. A Load is handled by the generic operand rule below
            // — its address is an operand, so a load from a uniform address stays invariant (a
            // broadcast) and a load from a variant address becomes variant. Other parameters
            // (views, scalars) have no variant operand ⇒ stay invariant (uniform across lanes).
            laneVariant.Add(indexParam);

            // ALSO seed the thread-POSITION intrinsics — a kernel can read its per-lane id directly
            // via Grid.GlobalIndex / Group.Idx / Grid.Idx / LaneIdx instead of the Index1D parameter
            // (e.g. an explicitly-grouped kernel). These differ across the 4 consecutive lanes of a
            // by-4 warp, so they are lane-VARIANT and MUST seed the fixpoint. Missing them silently
            // classified a whole kernel as lane-uniform → the emitter produced an all-scalar
            // `kernel_simd` that the by-4 dispatch ran once per 4 threads, SKIPPING 3 of every 4
            // (GridGroupDimension regression, 2026-06-16). Group/Grid index are seeded conservatively
            // (Grid.Idx is uniform within a group but a 4-lane warp can straddle a group boundary).
            foreach (var v in allValues)
            {
                switch (v.ValueKind)
                {
                    case ValueKind.GridIndex:   // Grid.Idx (group index)
                    case ValueKind.GroupIndex:  // Group.Idx (per-lane thread id within the group)
                    case ValueKind.LaneIdx:     // per-lane warp lane id
                        laneVariant.Add(v);
                        break;
                }
            }

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var v in allValues)
                {
                    if (laneVariant.Contains(v))
                        continue;
                    foreach (var op in v.Nodes)
                    {
                        if (laneVariant.Contains(op.Resolve()))
                        {
                            laneVariant.Add(v);
                            changed = true;
                            break;
                        }
                    }
                }
            }

            // Control flow must be lane-uniform for Stage 3a (no per-lane divergence) — every
            // conditional branch condition must be lane-invariant. Lane-variant branches need
            // mask-based if-conversion (Stage 3b).
            foreach (var block in method.Blocks)
            {
                if (block.Terminator is ConditionalBranch cb &&
                    laneVariant.Contains(cb.Condition.Resolve()))
                {
                    return new WasmSimdAnalysisResult
                    {
                        Vectorizable = false,
                        Reason = "lane-variant conditional branch (Stage 3b masks)",
                        LaneVariantCount = laneVariant.Count,
                        TotalValues = total,
                    };
                }
            }

            return new WasmSimdAnalysisResult
            {
                Vectorizable = true,
                Reason = "uniform control flow; no barriers/atomics/warp ops",
                LaneVariantCount = laneVariant.Count,
                TotalValues = total,
            };

            WasmSimdAnalysisResult Reject(string reason, int totalValues) => new WasmSimdAnalysisResult
            {
                Vectorizable = false,
                Reason = reason,
                LaneVariantCount = 0,
                TotalValues = totalValues,
            };
        }
    }
}
