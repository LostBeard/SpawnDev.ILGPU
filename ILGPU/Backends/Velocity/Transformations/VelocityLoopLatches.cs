// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityLoopLatches.cs
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
    /// Gives every loop with several back edges a single latch block: all back edges
    /// branch to the latch, the latch branches to the header, and the values the back
    /// edges carried become phis of the latch.
    /// </summary>
    /// <remarks>
    /// Velocity runs the blocks of a loop in schedule order under lane masks and jumps
    /// back to the header from a back edge block as soon as that block has lanes. With
    /// two back edges (an if/else whose arms each end in an inner loop) the first one
    /// jumped back while lanes still waited to run the blocks of the other arm, and their
    /// work was lost. Through a single latch, every lane has finished the body when the
    /// jump is taken.
    /// </remarks>
    sealed class VelocityLoopLatches : UnorderedTransformation
    {
        /// <summary>
        /// Remaps one target block to another.
        /// </summary>
        private readonly struct TargetRemapper : TerminatorValue.ITargetRemapper
        {
            public TargetRemapper(BasicBlock source, BasicBlock target)
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
        /// Replaces the arguments of all back edges of a header phi by a single argument
        /// from the latch: the latch phi that collects them.
        /// </summary>
        private readonly struct HeaderPhiRemapper : PhiValue.IArgumentRemapper
        {
            public HeaderPhiRemapper(
                HashSet<BasicBlock> backEdges,
                BasicBlock representative,
                BasicBlock latch,
                Dictionary<PhiValue, Value> latchPhis)
            {
                BackEdges = backEdges;
                Representative = representative;
                Latch = latch;
                LatchPhis = latchPhis;
            }

            public HashSet<BasicBlock> BackEdges { get; }

            public BasicBlock Representative { get; }

            public BasicBlock Latch { get; }

            public Dictionary<PhiValue, Value> LatchPhis { get; }

            public readonly bool CanRemap(PhiValue phiValue) => true;

            public readonly bool TryRemap(
                PhiValue phiValue,
                BasicBlock block,
                out BasicBlock newBlock)
            {
                newBlock = block == Representative ? Latch : block;
                // The other back edges are folded into the latch argument
                return block == Representative || !BackEdges.Contains(block);
            }

            public readonly Value RemapValue(
                PhiValue phiValue,
                BasicBlock updatedBlock,
                Value value) =>
                updatedBlock == Latch ? LatchPhis[phiValue] : value;
        }

        /// <summary>
        /// Creates a single latch for every loop that has several back edges.
        /// </summary>
        protected override bool PerformTransformation(Method.Builder builder)
        {
            builder.AcceptControlFlowUpdates(accept: true);

            var cfg = builder.SourceBlocks.CreateCFG();
            var loops = cfg.CreateLoops();

            var work = new List<(BasicBlock Header, BasicBlock[] BackEdges)>();
            loops.ProcessLoops(loop =>
            {
                if (loop.Headers.Length != 1 || loop.BackEdges.Length < 2)
                    return;
                work.Add((loop.Headers[0], loop.BackEdges.ToArray()));
            });

            foreach (var (header, backEdges) in work)
            {
                var location = header.Terminator.AsNotNull().Location;
                var latch = builder.CreateBasicBlock(location, header.Name + "_latch");

                // Collect the values every back edge carries into a phi of the latch
                var headerPhis = new List<PhiValue>();
                foreach (var value in header)
                {
                    if (value.Value is PhiValue phi)
                        headerPhis.Add(phi);
                }
                var latchPhis = new Dictionary<PhiValue, Value>();
                foreach (var phi in headerPhis)
                {
                    var latchPhi = latch.CreatePhi(phi.Location, phi.Type);
                    for (int i = 0, e = phi.Nodes.Length; i < e; ++i)
                    {
                        if (Array.IndexOf(backEdges, phi.Sources[i]) >= 0)
                            latchPhi.AddArgument(phi.Sources[i], phi.Nodes[i]);
                    }
                    latchPhis[phi] = latchPhi.Seal();
                }

                // Route all back edges through the latch
                foreach (var backEdge in backEdges)
                {
                    backEdge.Terminator.AsNotNull().RemapTargets(
                        builder[backEdge],
                        new TargetRemapper(header, latch.BasicBlock));
                }
                latch.CreateBranch(location, header);

                // The header now has one back edge argument: the latch phi
                var remapper = new HeaderPhiRemapper(
                    new HashSet<BasicBlock>(backEdges, new BasicBlock.Comparer()),
                    backEdges[0],
                    latch.BasicBlock,
                    latchPhis);
                var headerBuilder = builder[header];
                foreach (var phi in headerPhis)
                {
                    headerBuilder.SetupInsertPosition(phi);
                    phi.RemapArguments(builder, remapper);
                }
            }

            return work.Count > 0;
        }
    }
}
