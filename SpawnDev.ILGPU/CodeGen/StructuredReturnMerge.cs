using System.Collections.Generic;
using ILGPU.IR;
using ILGPU.IR.Values;

namespace SpawnDev.ILGPU.CodeGen;

/// <summary>
/// Where the two arms of a function-level if/else converge when one of them can <c>return</c> early, for the
/// structured WGSL and GLSL emitters.
/// </summary>
/// <remarks>
/// 🔴 An early <c>return</c> inside an arm stops the code after the if/else from post-dominating the branch, so the
/// branch's immediate post-dominator is the function's exit block. Using the exit as the merge makes each arm emit
/// everything up to the end of the function: for
/// <c>if (a || b) { o = Atomic.Add(..); if (o + n &gt; len) { Atomic.Add(.., -n); return; } write(o); }</c>
/// repeated N times, the rest of the kernel was emitted three times per block (the then-arm, the re-emitted shared
/// body of the <c>||</c>, and the overflow if's else-arm) - 3^N. AubsCraft's 6-face LOD mesh kernel never finished
/// compiling (one core spinning, 4 faces already 10 MB of WGSL, 2026-10-02). 4.9.15 compiled it in 232 ms; it hangs
/// from 4.10.0 on (at that release the inliner's cumulative budget dropping 16384 -> 2048 exposed the shape; at
/// 5.3.x it blows up with either budget). GLSL had the same walker shape (same 3^N) and also dropped every early
/// return after the first, because the shared exit block was "visited" once emitted.
/// A path that returns is a dead end in structured code - WGSL can <c>return</c> from any depth - so the merge is the
/// first block both arms reach such that every path from either arm that avoids it ends in a <c>return</c> without
/// touching the code that follows it.
/// </remarks>
public sealed class StructuredReturnMerge
{
    readonly Dictionary<BasicBlock, HashSet<BasicBlock>> _reachable = new();

    /// <summary>
    /// The return-aware merge of the arms <paramref name="a"/> and <paramref name="b"/>, not past
    /// <paramref name="stop"/> (the enclosing region's end; null at the top level): the earliest block that every
    /// path from both arms either passes through or avoids by returning. Null when there is none.
    /// </summary>
    public BasicBlock? FindMerge(BasicBlock a, BasicBlock b, BasicBlock? stop)
    {
        // Reachable from EITHER arm: an arm whose every path returns never reaches the merge at all
        // (`if (fits) { write } else { undo; return; }` merges at the write arm's own block).
        var common = new HashSet<BasicBlock>(ReachableInclusive(a));
        common.UnionWith(ReachableInclusive(b));
        // A return path is a dead end, never a merge.
        common.RemoveWhere(m => IsReturn(m) || IsCodeFreeReturnPath(m));
        if (stop != null)
        {
            // Nothing past the enclosing region's end: its caller owns that code.
            var pastStop = Reachable(stop);
            common.RemoveWhere(m => m != stop && pastStop.Contains(m));
        }
        if (common.Count == 0) return null;

        // The earliest merge reaches every later one, so it has the largest reachable set: test in that order and
        // stop at the first block that really is a merge.
        var candidates = new List<BasicBlock>(common);
        candidates.Sort((x, y) => Reachable(y).Count.CompareTo(Reachable(x).Count));
        foreach (var m in candidates)
            if (AllPathsMeetOrReturn(a, m, stop) && AllPathsMeetOrReturn(b, m, stop))
                return m;
        return null;
    }

    /// <summary>True when every path from <paramref name="start"/> either passes through <paramref name="merge"/> or
    /// ends in a return without entering the code that follows <paramref name="merge"/> or leaving through
    /// <paramref name="stop"/>.</summary>
    bool AllPathsMeetOrReturn(BasicBlock start, BasicBlock merge, BasicBlock? stop)
    {
        if (start == merge) return true;
        var afterMerge = Reachable(merge);
        var seen = new HashSet<BasicBlock> { start };
        var work = new Stack<BasicBlock>();
        work.Push(start);
        while (work.Count > 0)
        {
            var block = work.Pop();
            // Reaching the region's end, or code the merge leads to, means this path skipped the merge.
            if (block == stop) return false;
            // Only a CODE-FREE return path (phi blocks + a bare return) may be shared with the code after the merge:
            // emitting it again in this arm is just a `return`. An exit block with real code (a kernel's final
            // Group.Barrier()) is where both arms genuinely meet - copying it into one arm duplicates the code and
            // puts a barrier in non-uniform control flow (WGSL rejects that: CrossGroupScanReuseDetector, 2026-10-02).
            if (afterMerge.Contains(block) && !IsCodeFreeReturnPath(block)) return false;
            foreach (var succ in block.Successors)
                if (succ != merge && seen.Add(succ)) work.Push(succ);
        }
        return true;
    }

    static bool IsReturn(BasicBlock block) => block.Terminator is ReturnTerminator;

    /// <summary>True when <paramref name="to"/> is <paramref name="from"/> or reachable from it. An arm that cannot
    /// reach its merge only returns: the emitters write it as a guard clause (<c>if (c) { ...; return; }</c>) and keep
    /// the other arm at the enclosing scope, because that arm dominates everything after the merge and its
    /// declarations (a local array, a let binding) must stay visible there.</summary>
    public bool Reaches(BasicBlock from, BasicBlock to) => from == to || Reachable(from).Contains(to);

    /// <summary>The blocks of a guard clause's returning arm: <paramref name="returningArm"/> and everything it reaches.
    /// None of them reaches the merge, so the region is a dead end and may be emitted again for another branch that
    /// enters it - the second test of <c>if (a || b) { ..; return; }</c> jumps into the SAME returning block the first
    /// test's guard already emitted. The emitters un-visit this region before each guard; skipping it as "visited" left
    /// that guard empty and the thread ran on past the return (PointerAlias_ViewStoredInTwoBranches, 2026-10-02).</summary>
    public IEnumerable<BasicBlock> ReturningRegion(BasicBlock returningArm) => ReachableInclusive(returningArm);

    /// <summary>True when <paramref name="block"/> is a bare return path: phi-only blocks joined by unconditional
    /// branches, ending in a return block with no code of its own. Emitting it again is just a <c>return</c>.</summary>
    public static bool IsCodeFreeReturnPath(BasicBlock block)
    {
        var current = block;
        for (int limit = 16; current != null && limit-- > 0;)
        {
            foreach (var entry in current)
                if (entry.Value is not PhiValue) return false;
            if (current.Terminator is ReturnTerminator) return true;
            if (current.Terminator is UnconditionalBranch ub) current = ub.Target;
            else return false;
        }
        return false;
    }

    HashSet<BasicBlock> ReachableInclusive(BasicBlock start)
    {
        var set = new HashSet<BasicBlock>(Reachable(start)) { start };
        return set;
    }

    /// <summary>Blocks reachable from <paramref name="start"/> through at least one edge (cached per block).</summary>
    HashSet<BasicBlock> Reachable(BasicBlock start)
    {
        if (_reachable.TryGetValue(start, out var cached)) return cached;
        var seen = new HashSet<BasicBlock>();
        var work = new Stack<BasicBlock>();
        foreach (var succ in start.Successors)
            if (seen.Add(succ)) work.Push(succ);
        while (work.Count > 0)
            foreach (var succ in work.Pop().Successors)
                if (seen.Add(succ)) work.Push(succ);
        _reachable[start] = seen;
        return seen;
    }
}
