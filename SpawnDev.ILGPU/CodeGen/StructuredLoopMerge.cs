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
        var common = InLoopReachable(a, loop);
        common.IntersectWith(InLoopReachable(b, loop));
        if (common.Count == 0) return null;
        // The earliest convergence point reaches every other common block (it is on every in-loop path to them).
        foreach (var candidate in common)
        {
            var reach = InLoopReachable(candidate, loop);
            if (reach.IsSupersetOf(common)) return candidate;
        }
        return null;
    }

    /// <summary>Blocks reachable from <paramref name="start"/> (inclusive) without entering a header of
    /// <paramref name="loop"/> or leaving it.</summary>
    private static HashSet<BasicBlock> InLoopReachable(BasicBlock start, Loops<ReversePostOrder, Forwards>.Node loop)
    {
        var seen = new HashSet<BasicBlock>();
        if (!loop.Contains(start) || IsHeader(start, loop)) return seen;
        var work = new Stack<BasicBlock>();
        seen.Add(start);
        work.Push(start);
        while (work.Count > 0)
        {
            foreach (var succ in work.Pop().Successors)
            {
                if (!loop.Contains(succ) || IsHeader(succ, loop) || !seen.Add(succ)) continue;
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
