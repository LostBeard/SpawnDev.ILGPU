// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: VelocityLowerLoopSwitches.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Analyses;
using ILGPU.IR.Transformations;
using ILGPU.IR.Values;
using ILGPU.Util;
using System.Collections.Generic;

namespace ILGPU.Backends.Velocity.Transformations
{
    /// <summary>
    /// Rewrites every switch that leaves a loop into a chain of if branches, one per
    /// distinct case target, with the default target at the end of the chain.
    /// </summary>
    /// <remarks>
    /// The Velocity branch builder needs the loop exits of a block ordered after the
    /// targets inside the loop; <see cref="VelocityBlockScheduling"/> gets that order for
    /// an if branch by inverting it, which a switch does not allow (case i must go to
    /// target i), so a switch with a case or default leaving its loop was rejected
    /// ("Skip this case for now", with a message that did not even format: a
    /// FormatException hid it). C#: a `switch` inside a loop with a case that returns,
    /// breaks out with goto, or ends the loop (BackendTestBase.ScfSwitch).
    ///
    /// Each distinct target T other than the default gets one test,
    /// `v == c1 | v == c2 | ...` over the cases that go to T, computed in the switch's
    /// block; the chain tests them in case order and branches to the default when none
    /// matches (this covers the values outside the case range too). A target has exactly
    /// one edge from the chain, so its phi arguments move from the switch's block to the
    /// chain block that branches to it.
    /// </remarks>
    sealed class VelocityLowerLoopSwitches : UnorderedTransformation
    {
        /// <summary>
        /// Lowers all switches that leave a loop.
        /// </summary>
        protected override bool PerformTransformation(Method.Builder builder)
        {
            builder.AcceptControlFlowUpdates(accept: true);

            var cfg = builder.SourceBlocks.CreateCFG();
            var loops = cfg.CreateLoops();

            var switches = new List<BasicBlock>();
            var seen = new HashSet<BasicBlock>(new BasicBlock.Comparer());
            loops.ProcessLoops(loop =>
            {
                foreach (var breaker in loop.Breakers)
                {
                    if (breaker.Terminator is SwitchBranch && seen.Add(breaker))
                        switches.Add(breaker);
                }
            });

            foreach (var block in switches)
                Lower(builder, block);
            return switches.Count > 0;
        }

        /// <summary>
        /// Replaces the switch of the given block with a chain of if branches.
        /// </summary>
        private static void Lower(Method.Builder builder, BasicBlock block)
        {
            var switchBranch = (SwitchBranch)block.Terminator.AsNotNull();
            var location = switchBranch.Location;
            var value = switchBranch.Condition.Resolve();
            var defaultBlock = switchBranch.DefaultBlock;

            // The distinct targets other than the default, in case order, with their cases
            var targets = new List<BasicBlock>();
            var cases = new List<List<int>>();
            for (int i = 0, e = switchBranch.NumCasesWithoutDefault; i < e; ++i)
            {
                var target = switchBranch.GetCaseTarget(i);
                if (target == defaultBlock)
                    continue;
                int index = targets.IndexOf(target);
                if (index < 0)
                {
                    index = targets.Count;
                    targets.Add(target);
                    cases.Add(new List<int>());
                }
                cases[index].Add(i);
            }

            var current = builder[block];
            if (targets.Count < 1)
            {
                current.CreateBranch(location, defaultBlock);
                return;
            }

            // The tests, in the switch's block (it dominates the whole chain)
            var tests = new Value[targets.Count];
            for (int k = 0; k < targets.Count; ++k)
            {
                Value? test = null;
                foreach (int caseValue in cases[k])
                {
                    Value constant = current.CreatePrimitiveValue(
                        location,
                        value.BasicValueType,
                        caseValue);
                    Value equal = current.CreateCompare(
                        location,
                        value,
                        constant,
                        CompareKind.Equal);
                    test = test is null
                        ? equal
                        : current.CreateArithmetic(
                            location,
                            test,
                            equal,
                            BinaryArithmeticKind.Or);
                }
                tests[k] = test.AsNotNull();
            }

            // The chain: block -> if (test 0) target 0 else next -> ... -> default
            var chain = current;
            for (int k = 0; k < targets.Count; ++k)
            {
                BasicBlock.Builder? next = k + 1 < targets.Count
                    ? builder.CreateBasicBlock(location, block.Name + "_case")
                    : null;
                chain.CreateIfBranch(
                    location,
                    tests[k],
                    targets[k],
                    next?.BasicBlock ?? defaultBlock);
                if (chain.BasicBlock != block)
                {
                    builder[targets[k]].RemapPhiArguments(
                        new PhiValue.BlockRemapper(block, chain.BasicBlock));
                }
                if (next is null)
                    break;
                chain = next;
            }
            if (chain.BasicBlock != block)
            {
                builder[defaultBlock].RemapPhiArguments(
                    new PhiValue.BlockRemapper(block, chain.BasicBlock));
            }
        }
    }
}
