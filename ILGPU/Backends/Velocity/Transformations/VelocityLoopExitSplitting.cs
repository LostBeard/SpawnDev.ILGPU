// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityLoopExitSplitting.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Transformations;
using ILGPU.IR.Values;
using ILGPU.Util;
using System;
using System.Collections.Generic;

namespace ILGPU.Backends.Velocity.Transformations
{
    /// <summary>
    /// Routes every edge that leaves a loop straight into a loop header through a new
    /// empty block.
    /// </summary>
    /// <remarks>
    /// The Velocity branch builder handles a branch to a loop header before it looks at
    /// loop exits: an edge that is both the exit of an inner loop and the back edge of
    /// the enclosing loop (a `while` whose body ends in an inner `while`) sent the lanes
    /// that left the inner loop back to the outer header at once, without removing them
    /// from the inner loop's mask or waiting for the other lanes, and the loop-carried
    /// values came out wrong (0 for every lane in
    /// `while (i &lt; n) { run = w[i++]; while (run &gt; 0) { nt++; run--; } }`). Behind
    /// the new block the edge is an ordinary loop exit, and the block's own branch is the
    /// back edge.
    /// </remarks>
    sealed class VelocityLoopExitSplitting : UnorderedTransformation
    {
        /// <summary>
        /// Remaps one target block to another.
        /// </summary>
        private readonly struct Remapper : TerminatorValue.ITargetRemapper
        {
            public Remapper(BasicBlock source, BasicBlock target)
            {
                Source = source;
                Target = target;
            }

            public BasicBlock Source { get; }

            public BasicBlock Target { get; }

            public readonly bool CanRemap(in ReadOnlySpan<BasicBlock> blocks) =>
                blocks.Contains(Source, new BasicBlock.Comparer());

            public readonly BasicBlock Remap(BasicBlock block) =>
                block == Source ? Target : block;
        }

        /// <summary>
        /// Splits all loop exits that branch to a loop header.
        /// </summary>
        protected override bool PerformTransformation(Method.Builder builder)
        {
            builder.AcceptControlFlowUpdates(accept: true);

            var cfg = builder.SourceBlocks.CreateCFG();
            var loops = cfg.CreateLoops();

            var headers = new HashSet<BasicBlock>(new BasicBlock.Comparer());
            loops.ProcessLoops(loop =>
            {
                foreach (var header in loop.Headers)
                    headers.Add(header);
            });

            var edges = new List<(BasicBlock Source, BasicBlock Header)>();
            var seen = new HashSet<(BasicBlock, BasicBlock)>();
            loops.ProcessLoops(loop =>
            {
                foreach (var breaker in loop.Breakers)
                {
                    foreach (var target in breaker.Successors)
                    {
                        if (loop.Contains(target) || !headers.Contains(target))
                            continue;
                        if (seen.Add((breaker, target)))
                            edges.Add((breaker, target));
                    }
                }
            });

            foreach (var (source, header) in edges)
            {
                var terminator = source.Terminator.AsNotNull();
                var exitBlock = builder.CreateBasicBlock(
                    terminator.Location,
                    source.Name + "_exit");
                exitBlock.CreateBranch(terminator.Location, header);

                terminator.RemapTargets(builder[source], new Remapper(header, exitBlock.BasicBlock));
                builder[header].RemapPhiArguments(
                    new PhiValue.BlockRemapper(source, exitBlock.BasicBlock));
            }

            return edges.Count > 0;
        }
    }
}
