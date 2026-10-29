// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: CLCodeGenerator.Terminators.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR;
using ILGPU.IR.Values;

namespace ILGPU.Backends.OpenCL
{
    partial class CLCodeGenerator
    {
        /// <summary cref="IBackendCodeGenerator.GenerateCode(ReturnTerminator)"/>
        public void GenerateCode(ReturnTerminator returnTerminator)
        {
            // No successor block -> no phi bindings to emit.
            using var statement = BeginStatement(CLInstructions.ReturnStatement);
            if (!returnTerminator.IsVoidReturn)
            {
                var resultRegister = Load(returnTerminator.ReturnValue);
                statement.AppendArgument(resultRegister);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(UnconditionalBranch)"/>
        public void GenerateCode(UnconditionalBranch branch)
        {
            // Per-target phi binding emit before the goto. Mirrors PTX behavior.
            ResetPhiBindingScope();
            BindPhis(branch.BasicBlock, branch.Target);
            GotoStatement(branch.Target);
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(IfBranch)"/>
        public void GenerateCode(IfBranch branch)
        {
            // TODO: refactor if-block generation into a separate emitter
            // See also EmitImplicitKernelIndex

            var condition = Load(branch.Condition);
            if (condition is ConstantVariable constantVariable)
            {
                // Compile-time-known branch: only one target's phis fire.
                ResetPhiBindingScope();
                if (constantVariable.Value.RawValue != 0)
                {
                    BindPhis(branch.BasicBlock, branch.TrueTarget);
                    GotoStatement(branch.TrueTarget);
                }
                else
                {
                    BindPhis(branch.BasicBlock, branch.FalseTarget);
                    GotoStatement(branch.FalseTarget);
                }
            }
            else
            {
                // Both targets are reachable - phi bindings must fire only on the path
                // actually taken at runtime, otherwise back-edge updates stomp values
                // that downstream blocks rely on (the do-while-with-back-edge-phi-alias
                // bug Tuvok hit on Av1RangeDecoderGpu.DecodeCdfQ15 OpenCL 2026-04-28).
                ResetPhiBindingScope();
                AppendIndent();
                Builder.Append("if (");
                Builder.Append(condition.ToString());
                Builder.AppendLine(")");
                PushIndent();
                AppendIndent();
                Builder.AppendLine("{");
                PushIndent();
                BindPhis(branch.BasicBlock, branch.TrueTarget);
                GotoStatement(branch.TrueTarget);
                PopIndent();
                AppendIndent();
                Builder.AppendLine("}");
                PopIndent();
                // The false edge does not run the true edge's moves: its bindings must
                // not read the temporaries those moves saved intermediate phis to.
                ResetPhiBindingScope();
                BindPhis(branch.BasicBlock, branch.FalseTarget);
                GotoStatement(branch.FalseTarget);
            }
        }

        /// <summary cref="IBackendCodeGenerator.GenerateCode(SwitchBranch)"/>
        public void GenerateCode(SwitchBranch branch)
        {
            // Each case and the default take their own edge: every one binds the phis
            // of its target before its goto. (Binding them after the switch statement
            // left them unreachable: a case that jumped straight to a block with phis
            // read stale values.)
            var condition = Load(branch.Condition);
            AppendIndent();
            Builder.Append("switch (");
            Builder.Append(condition.ToString());
            Builder.AppendLine(") {");
            for (int i = 0, e = branch.NumCasesWithoutDefault; i < e; ++i)
            {
                AppendIndent();
                Builder.Append("case ");
                Builder.Append(i);
                Builder.AppendLine(": {");
                EmitSwitchEdge(branch.BasicBlock, branch.GetCaseTarget(i));
            }
            AppendIndent();
            Builder.AppendLine("default: {");
            EmitSwitchEdge(branch.BasicBlock, branch.DefaultBlock);
            AppendIndent();
            Builder.AppendLine("}");
        }

        /// <summary>
        /// Emits the body of one switch case: the phi bindings of the edge to
        /// <paramref name="target"/>, its goto and the closing brace of the case.
        /// </summary>
        private void EmitSwitchEdge(BasicBlock current, BasicBlock target)
        {
            PushIndent();
            ResetPhiBindingScope();
            BindPhis(current, target);
            GotoStatement(target);
            PopIndent();
            AppendIndent();
            Builder.AppendLine("}");
        }
    }
}
