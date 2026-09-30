using System.Collections.Generic;
using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Analyses.ControlFlowDirection;
using ILGPU.IR.Analyses.TraversalOrders;

namespace SpawnDev.ILGPU.CodeGen;

/// <summary>
/// Where the two arms of an if/else inside a loop body converge, for the structured WGSL and GLSL emitters.
/// </summary>
/// <remarks>
/// 🔴 When one arm can LEAVE the loop (a <c>break</c> / <c>return</c> inside it), the function-wide post-dominator of
/// the branch lies outside the loop and cannot be the merge. The emitters then guessed "the target that reaches the
/// header through unconditional branches" - but that is only ONE arm's block, not where both arms meet. For
/// <c>for (k..) { if (c) { run++; if (run &gt;= 9) break; } else run = 0; }</c> it picked the else arm's own block, the
/// then-arm walked straight through the loop latch (the <c>k++</c>), the else arm never reached it, and the loop never
/// advanced on that path: an infinite loop on the GPU (SpawnScene FAST-9 detector: DXGI_ERROR_DEVICE_HUNG, 2026-09-28).
/// The merge is the first block BOTH arms reach inside the loop, with the header (the back edge) and every exit left
/// out of the walk.
/// </remarks>
public static class StructuredLoopMerge
{
    /// <summary>
    /// The first block both <paramref name="a"/> and <paramref name="b"/> reach within <paramref name="loop"/>, not
    /// crossing its header or leaving it; null when their in-loop paths never meet (each arm ends in a back edge or
    /// an exit of its own).
    /// </summary>
    public static BasicBlock? FindInLoopMerge(BasicBlock a, BasicBlock b, Loops<ReversePostOrder, Forwards>.Node loop)
    {
        var common = InLoopReachable(a, loop, null);
        common.IntersectWith(InLoopReachable(b, loop, null));
        // A merge must be on EVERY in-loop path from both arms back to the header (paths that leave the loop are
        // exits and do not count). Being merely reachable from both is not enough: for
        // `if (s < 0 || s >= n) { if (m == 0) break; ... }` the out-of-bounds block is reachable from both arms of
        // `s < 0`, but the in-bounds path bypasses it to the latch. Picking it emitted the latch inside one arm only;
        // the `s < 0` path never advanced the loop - ML PadKernel reflect pad, DXGI_ERROR_DEVICE_HUNG (2026-09-30).
        common.RemoveWhere(m => ReachesBackEdgeAvoiding(a, m, loop) || ReachesBackEdgeAvoiding(b, m, loop));
        if (common.Count == 0) return null;
        // The earliest convergence point reaches every other one (it is on every in-loop path to them).
        foreach (var candidate in common)
        {
            var reach = InLoopReachable(candidate, loop, null);
            if (reach.IsSupersetOf(common)) return candidate;
        }
        return null;
    }

    /// <summary>True when some path from <paramref name="start"/> gets back to a header of <paramref name="loop"/>
    /// (a back edge) without passing through <paramref name="avoid"/> or leaving the loop.</summary>
    private static bool ReachesBackEdgeAvoiding(BasicBlock start, BasicBlock avoid, Loops<ReversePostOrder, Forwards>.Node loop)
    {
        if (start == avoid) return false;
        foreach (var block in InLoopReachable(start, loop, avoid))
            foreach (var succ in block.Successors)
                if (IsHeader(succ, loop)) return true;
        return false;
    }

    /// <summary>Blocks reachable from <paramref name="start"/> (inclusive) without entering a header of
    /// <paramref name="loop"/>, leaving it, or entering <paramref name="avoid"/>.</summary>
    private static HashSet<BasicBlock> InLoopReachable(BasicBlock start, Loops<ReversePostOrder, Forwards>.Node loop,
        BasicBlock? avoid)
    {
        var seen = new HashSet<BasicBlock>();
        if (!loop.Contains(start) || IsHeader(start, loop) || start == avoid) return seen;
        var work = new Stack<BasicBlock>();
        seen.Add(start);
        work.Push(start);
        while (work.Count > 0)
        {
            foreach (var succ in work.Pop().Successors)
            {
                if (!loop.Contains(succ) || IsHeader(succ, loop) || succ == avoid || !seen.Add(succ)) continue;
                work.Push(succ);
            }
        }
        return seen;
    }

    private static bool IsHeader(BasicBlock block, Loops<ReversePostOrder, Forwards>.Node loop)
    {
        foreach (var h in loop.Headers)
            if (h == block) return true;
        return false;
    }
}
