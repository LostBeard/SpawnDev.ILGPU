using System.Collections.Generic;

namespace SpawnDev.ILGPU;

/// <summary>
/// The phi copies taken on ONE control-flow edge form a PARALLEL copy: every phi of the target
/// block receives the value its operand had BEFORE the edge. Emitting them as ordinary
/// assignments one after another is only equivalent when no copy reads a variable that an
/// earlier copy on the same edge already overwrote. A loop that rotates or swaps its
/// loop-carried values breaks that (<c>a' = b; b' = c; c' = a</c> - the header phis' back-edge
/// operands are the header phis themselves), and the sequential form hands <c>c</c> the NEW
/// <c>a</c>. Shared by every text backend's phi emission (WGSL, GLSL; kernel bodies and
/// [NoInlining] helper functions alike).
/// </summary>
internal static class PhiParallelCopy
{
    /// <summary>
    /// Returns, per copy, whether its source must be staged into a temporary before ANY target on
    /// this edge is written - true exactly when the source is the target of another copy on the
    /// same edge. Returns null when nothing needs staging (the common case), so callers keep
    /// emitting plain sequential assignments.
    /// </summary>
    /// <param name="targets">The phi variable names written on this edge, in emission order.</param>
    /// <param name="sources">The variable names read, index-aligned with <paramref name="targets"/>.</param>
    public static bool[]? SourcesToStage(IReadOnlyList<string> targets, IReadOnlyList<string> sources)
    {
        int count = targets.Count;
        if (count < 2)
            return null;
        bool[]? stage = null;
        for (int i = 0; i < count; i++)
        {
            string source = sources[i];
            if (source == targets[i])
                continue;
            for (int j = 0; j < count; j++)
            {
                if (j != i && targets[j] == source)
                {
                    (stage ??= new bool[count])[i] = true;
                    break;
                }
            }
        }
        return stage;
    }
}
