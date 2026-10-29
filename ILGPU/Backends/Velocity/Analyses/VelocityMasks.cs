// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityMasks.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// Uncomment this line to or define a preprocessor symbol to enable detailed Velocity
// accelerator debugging:
// #define DEBUG_VELOCITY

using ILGPU.Backends.IL;
using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Analyses.ControlFlowDirection;
using ILGPU.IR.Analyses.TraversalOrders;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Loop = ILGPU.IR.Analyses.Loops<
    ILGPU.IR.Analyses.TraversalOrders.ReversePostOrder,
    ILGPU.IR.Analyses.ControlFlowDirection.Forwards>.Node;

namespace ILGPU.Backends.Velocity.Analyses
{
    /// <summary>
    /// A program analysis to gather information about vector masks to be used.
    /// </summary>
    /// <typeparam name="TILEmitter">The IL emitter type.</typeparam>
    sealed class VelocityMasks<TILEmitter>
        where TILEmitter : struct, IILEmitter
    {
        #region Instance

        /// <summary>
        /// The set of all back edge source blocks.
        /// </summary>
        private BasicBlockSet backEdges;

        /// <summary>
        /// The set of all loop blocks.
        /// </summary>
        private BasicBlockSet loopBlocks;

        /// <summary>
        /// The set of all loop headers.
        /// </summary>
        private BasicBlockMap<Loop> loopHeaders;

        /// <summary>
        /// The set of all exit blocks.
        /// </summary>
        private BasicBlockMap<HashSet<Loop>> exitBlocks;

        /// <summary>
        /// The set of all loop masks.
        /// </summary>
        private readonly Dictionary<Loop, ILLocal> loopMasks = new();

        /// <summary>
        /// Maps each loop to its exit block that is generated first.
        /// </summary>
        private readonly Dictionary<Loop, BasicBlock> firstExits = new();

        /// <summary>
        /// Maps blocks to their input masks.
        /// </summary>
        private readonly BasicBlockMap<ILLocal> blockMasks;

        /// <summary>
        /// Stores all loops.
        /// </summary>
        private readonly Loops<ReversePostOrder, Forwards> loops;

        public VelocityMasks(
            BasicBlockCollection<ReversePostOrder, Forwards> blocks,
            TILEmitter emitter,
            VelocityTargetSpecializer specializer)
        {
            loopHeaders = blocks.CreateMap<Loop>();
            exitBlocks = blocks.CreateMap<HashSet<Loop>>();
            backEdges = blocks.CreateSet();
            loopBlocks = blocks.CreateSet();
            blockMasks = blocks.CreateMap<ILLocal>();

            // Iterate over all loops and determine all body blocks and all back edges
            var cfg = blocks.CreateCFG();
            loops = cfg.CreateLoops();

            // The position of each block in the order the code is generated in
            EmissionOrder = ComputeEmissionOrder(blocks, loops);
            var blockIndices = new Dictionary<BasicBlock, int>();
            var blockOrder = new List<BasicBlock>();
            foreach (var block in EmissionOrder)
            {
                blockIndices.Add(block, blockIndices.Count);
                blockOrder.Add(block);
            }

            loops.ProcessLoops(loop =>
            {
                // Declare a new loop mask
                loopMasks[loop] = emitter.DeclareLocal(specializer.WarpType32);

                // Register all loop headers
                foreach (var header in loop.Headers)
                    loopHeaders.Add(header, loop);

                // Remember all body blocks
                foreach (var block in loop.AllMembers)
                    loopBlocks.Add(block);

                // Remember all exits
                BasicBlock? firstExit = null;
                foreach (var block in loop.Exits)
                {
                    if (firstExit is null ||
                        blockIndices[block] < blockIndices[firstExit])
                    {
                        firstExit = block;
                    }

                    if (!exitBlocks.TryGetValue(block, out var set))
                    {
                        set = new HashSet<Loop>(2)
                        {
                            loop
                        };
                    }
                    else
                    {
                        set.Add(loop);
                    }
                    exitBlocks[block] = set;
                }
                // Continue with the block generated right after the loop, not with the exit: blocks between the loop
                // and its exit (the else arm of `if (c) { loop } else { loop }`, whose loops share the merge block as
                // exit) hold lanes too. Jumping to the exit skipped them: a then-loop no lane entered left at once and
                // every store of the else arm was lost (BranchLoops_LoopInEachArm_EveryArmStoresOrIsRefusedOnWebGL, SpawnDev.Codecs
                // VorbisResidueAccumulateGpu). Every block between runs under its own mask.
                if (firstExit is not null)
                {
                    int next = 0;
                    foreach (var member in loop.AllMembers)
                        next = Math.Max(next, blockIndices[member] + 1);
                    firstExits[loop] = next < blockIndices[firstExit] ? blockOrder[next] : firstExit;
                }

                // Register all back edges
                foreach (var backEdge in loop.BackEdges)
                    backEdges.Add(backEdge);
            });

            // Remove all headers again from all loops from the instantly reset set
            loops.ProcessLoops(loop =>
            {
                foreach (var header in loop.Headers)
                    loopBlocks.Remove(header);
            });

            // Allocate local masks and initialize all of them
            foreach (var block in blocks)
            {
                // Create a local variable to store the entry mask for this block
                var blockMask = emitter.DeclareLocal(specializer.WarpType32);
                blockMasks[block] = blockMask;
            }
        }

        /// <summary>
        /// The order the code generator emits the blocks in: reverse post order, with every loop's body contiguous and its
        /// exits after it.
        /// </summary>
        public IReadOnlyList<BasicBlock> EmissionOrder { get; }

        /// <summary>
        /// Reverse post order, then for every loop the blocks that are not part of it but fall between its first and its
        /// last member move behind its last member (keeping their order), until no loop has one.
        /// </summary>
        /// <remarks>
        /// Execution falls through from block to block under the blocks' masks, and the branch builder relies on a loop's
        /// exits coming after its whole body: once no lane is left in the loop it jumps to the first exit and continues from
        /// there. Reverse post order breaks that when a loop has exits in different branches,
        /// `while (true) { if (c) { ...; if (a) break; } else { ...; if (b) break; } }`: the depth-first search finishes the
        /// first branch's exit block before it visits the other branch, so that exit lands between two body blocks. The
        /// jump to it then fell through into the body again, whose branch jumped back to it, forever (a hang with a single
        /// thread; SpawnDev.Codecs' CELT band recursion as an explicit-stack loop).
        /// </remarks>
        private static List<BasicBlock> ComputeEmissionOrder(
            BasicBlockCollection<ReversePostOrder, Forwards> blocks,
            Loops<ReversePostOrder, Forwards> loops)
        {
            var order = new List<BasicBlock>();
            foreach (var block in blocks)
                order.Add(block);
            bool changed = true;
            for (int round = 0; changed && round <= order.Count; round++)
            {
                changed = false;
                loops.ProcessLoops(loop =>
                {
                    int first = -1, last = -1;
                    for (int i = 0; i < order.Count; i++)
                    {
                        if (!loop.Contains(order[i]))
                            continue;
                        if (first < 0)
                            first = i;
                        last = i;
                    }
                    if (first < 0)
                        return;
                    var members = new List<BasicBlock>();
                    var others = new List<BasicBlock>();
                    for (int i = first; i <= last; i++)
                        (loop.Contains(order[i]) ? members : others).Add(order[i]);
                    if (others.Count == 0)
                        return;
                    order.RemoveRange(first, last - first + 1);
                    members.AddRange(others);
                    order.InsertRange(first, members);
                    changed = true;
                });
            }
            return order;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Disables all internal lanes.
        /// </summary>
        public void DisableAllLanes(
            Method method,
            TILEmitter emitter,
            VelocityTargetSpecializer specializer)
        {
            foreach (var (basicBlock, blockMask) in blockMasks)
            {
                // Ignore the entry block
                if (basicBlock == method.EntryBlock)
                    continue;

                // Ignore blocks that will be reset automatically
                if (NeedsToRefreshMask(basicBlock))
                    continue;

                specializer.PushNoLanesMask32(emitter);
                emitter.Emit(LocalOperation.Store, blockMask);
            }
        }

        /// <summary>
        /// Tries to map the given block to a loop and returns the loop if possible.
        /// </summary>
        /// <param name="block">The block to map to a loop.</param>
        /// <param name="loop">The resolved loop (if any).</param>
        /// <returns>True if the given block could be mapped to a loop.</returns>
        public bool TryGetLoop(BasicBlock block, [NotNullWhen(true)] out Loop? loop) =>
            loops.TryGetLoops(block, out loop);

        /// <summary>
        /// Returns the block mask for the given basic block.
        /// </summary>
        /// <param name="block">The block to lookup.</param>
        /// <returns>The block mask to use.</returns>
        public ILLocal GetBlockMask(BasicBlock block) => blockMasks[block];

        /// <summary>
        /// Returns the loop mask for the given loop.
        /// </summary>
        /// <param name="loop">The loop to lookup.</param>
        /// <returns>The loop mask to use.</returns>
        public ILLocal GetLoopMask(Loop loop) => loopMasks[loop];

        /// <summary>
        /// Returns true if the given block is a header and also returns a set of nested
        /// loop headers that are implicitly controlled by this header.
        /// </summary>
        public bool IsHeader(
            BasicBlock target,
            [NotNullWhen(true)]
            out Loop? loop) =>
            loopHeaders.TryGetValue(target, out loop);

        /// <summary>
        /// Returns true if the given target block is an exit.
        /// </summary>
        public bool IsExit(
            BasicBlock target,
            [NotNullWhen(true)]
            out Predicate<Loop>? containsLoop)
        {
            if (exitBlocks.TryGetValue(target, out var loopsToExit))
            {
                containsLoop = loopsToExit.Contains;
                return true;
            }

            containsLoop = null;
            return false;
        }

        /// <summary>
        /// Returns the exit block of the given loop that is generated first. Lanes that
        /// leave a loop wait in the mask of the exit block they leave to, and the loop
        /// is done when no lane is left in it: leaving it has to continue with the first
        /// exit block and flow through the others (a block without active lanes does
        /// nothing), as jumping straight to one exit block would skip the lanes that
        /// are waiting in an earlier one (an early return from a loop and the normal
        /// exit are two exit blocks).
        /// </summary>
        /// <param name="loop">The loop to lookup.</param>
        /// <returns>The first exit block of the loop.</returns>
        public BasicBlock GetFirstExit(Loop loop) => firstExits[loop];

        /// <summary>
        /// Returns true if the given block is a target potentially hit from a back edge.
        /// </summary>
        public bool IsBackEdgeBlock(BasicBlock block) => backEdges.Contains(block);

        /// <summary>
        /// Returns true if this block needs to refresh its mask instantly.
        /// </summary>
        public bool NeedsToRefreshMask(BasicBlock block) =>
            loopBlocks.Contains(block);

#if DEBUG_VELOCITY
        public void DumpAllMasks(
            TILEmitter emitter,
            VelocityTargetSpecializer specializer)
        {
            foreach (var (block, mask) in blockMasks)
            {
                emitter.Emit(LocalOperation.Load, mask);
                specializer.DumpWarp32(emitter, $"  {block.ToReferenceString()}");
            }
            foreach (var (loop, mask) in loopMasks)
            {
                emitter.Emit(LocalOperation.Load, mask);
                specializer.DumpWarp32(
                    emitter,
                    $"  LOOP_{loop.Headers[0].ToReferenceString()}");
            }
        }
#endif

        #endregion
    }
}
