// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: Inliner.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Frontend;
using ILGPU.IR.Analyses;
using ILGPU.IR.Values;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ILGPU.IR.Transformations
{
    /// <summary>
    /// Represents a function inliner.
    /// </summary>
    public sealed class Inliner : OrderedTransformation
    {
        #region Constants

        /// <summary>
        /// The maximum number of IL instructions to inline in Conservative mode.
        /// </summary>
        private const int MaxNumILInstructionsToInline = 32;

        /// <summary>
        /// Hard upper bound for a method's IL instruction count before the inliner
        /// declines to inline in Aggressive / Default mode. Methods explicitly marked
        /// with <c>[MethodImpl(MethodImplOptions.AggressiveInlining)]</c> or living
        /// inside ILGPU's own assembly bypass this cap (their explicit attribute is
        /// the user's signal that the cost is intentional).
        ///
        /// Without this cap, ILGPU's Aggressive (default) mode unconditionally inlines
        /// every method reachable from the kernel entry, regardless of size. For
        /// codecs that compose many medium-sized helpers via a recursive partition
        /// tree (e.g. Tuvok's VP9 EncodeFrameKernel — see
        /// `tuvok-to-geordi-codecs-wasm-monolithic-inlining-2026-05-06.md`), the
        /// resulting kernel function balloons to tens of thousands of locals + KB-MB
        /// of instruction bytes in a single Wasm/WGSL function. wabt's parser rejects
        /// at &gt;50K locals (`function local count exceeds maximum value`), V8's
        /// TurboFan tier-up compile latency dwarfs Playwright's 30s test budget, and
        /// Tint + WebGPU shader-validators choke similarly on the WGSL side.
        ///
        /// Per-function cap alone is not enough: a 400-IL helper called 100 times via
        /// a recursive partition tree contributes 40K IL into the kernel even though
        /// no individual helper trips the cap. The companion
        /// <see cref="CumulativeInlinedILBudget"/> on the Inliner pass catches that case.
        ///
        /// 1024 instructions is generous enough to inline almost all "real" C#
        /// helpers (typical 50-200; heavy 500-1000) while preventing 5K+
        /// mega-helpers from being unconditionally inlined.
        /// (2026-05-05 codecs Wasm finding; cumulative budget added 2026-05-06.)
        /// </summary>
        private const int MaxNumILInstructionsAggressiveCap = 1024;

        /// <summary>
        /// Cumulative IL-instruction budget per kernel transformation pass. Once
        /// exceeded, the Inliner stops specializing further <see cref="MethodFlags.Inline"/>-flagged
        /// calls and leaves them as ordinary fn-defs / calls in the IR. This catches the
        /// case where each individual helper sits comfortably under
        /// <see cref="MaxNumILInstructionsAggressiveCap"/> but a recursive call tree
        /// expands to tens of thousands of inlined IL instructions in a single kernel
        /// function (Tuvok's VP9 EncodeFrameBody → 4 × EncodeSb64 → 4 × EncodeBlock32x32
        /// → 4 × EncodeBlock16x16 → leaf encoders, each ~400-600 IL).
        ///
        /// 16,384 IL instructions corresponds roughly to a Wasm function locals count
        /// well under V8's 50K hard cap (`kV8MaxWasmFunctionLocals`) with comfortable
        /// margin for the 1.x-3x IL-to-Wasm-local expansion typical of arithmetic-heavy
        /// codec kernels.
        ///
        /// Methods marked <c>[MethodImpl(MethodImplOptions.AggressiveInlining)]</c> or
        /// living inside ILGPU's own assembly are NOT subject to this budget — their
        /// explicit attribute is the user's signal that the cost is intentional.
        /// (2026-05-06 follow-up to `tuvok-rc28-codecs-still-52K-locals-2026-05-06.md`.)
        /// </summary>
        private const int CumulativeInlinedILBudget = 2048;

        #endregion

        #region Static

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void SetupInliningAttributes(
            IRContext context,
            Method method,
            DisassembledMethod disassembledMethod)
        {
            // Always record the IL instruction count so the Inliner pass can enforce
            // its cumulative-IL budget regardless of whether this method ends up
            // marked Inline (some methods get marked later via different paths and
            // we still want the count available for budget bookkeeping).
            method.ILInstructionCount = disassembledMethod.Instructions.Length;

            // Check whether we can inline this method
            if (!method.HasImplementation)
                return;

            if (method.HasSource)
            {
                var source = method.Source;
                if ((source.MethodImplementationFlags &
                    MethodImplAttributes.NoInlining) ==
                    MethodImplAttributes.NoInlining)
                {
                    return;
                }

                if ((source.MethodImplementationFlags &
                    MethodImplAttributes.AggressiveInlining) ==
                    MethodImplAttributes.AggressiveInlining ||
                    source.Module.Name == Context.FullAssemblyModuleName)
                {
                    // Explicit AggressiveInlining attribute or ILGPU-internal helper
                    // bypasses BOTH the per-function size cap AND the cumulative
                    // budget. Mark with the dedicated ForceInline flag so the
                    // Inliner pass knows to skip the budget check for these calls.
                    method.AddFlags(MethodFlags.Inline | MethodFlags.ForceInline);
                    return;
                }
            }

            // Hard cap (Aggressive / Default mode): decline to inline methods whose
            // IL body exceeds the aggressive cap. Without this, kernels composed of
            // medium-sized helpers via deep call graphs balloon to tens of thousands
            // of locals + KB-MB instruction bytes in a single emitted Wasm/WGSL
            // function — see XML comment on `MaxNumILInstructionsAggressiveCap` for
            // the codec finding that motivated this. Methods opting in explicitly
            // (AggressiveInlining attribute / ILGPU internal) bypassed this above.
            if (disassembledMethod.Instructions.Length > MaxNumILInstructionsAggressiveCap)
                return;

            // Evaluate a simple inlining heuristic
            if (context.Properties.InliningMode != InliningMode.Conservative ||
                disassembledMethod.Instructions.Length <= MaxNumILInstructionsToInline)
            {
                method.AddFlags(MethodFlags.Inline);
            }
        }

        /// <summary>
        /// Tries to inline method calls. Enforces a cumulative IL-instruction
        /// budget so a recursive call tree of small-but-many helpers cannot
        /// blow up the kernel function past Wasm/WGSL limits. Methods marked
        /// <see cref="MethodFlags.ForceInline"/> bypass the budget.
        /// </summary>
        /// <param name="builder">The current method builder.</param>
        /// <param name="currentBlock">The current block (may be modified).</param>
        /// <param name="cumulativeInlinedIL">
        /// Running total of IL instructions added to this kernel by the inliner.
        /// Updated when a call is specialized.
        /// </param>
        /// <returns>True, in case of an inlined call.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool InlineCalls(
            Method.Builder builder,
            ref BasicBlock currentBlock,
            ref int cumulativeInlinedIL)
        {
            foreach (var valueEntry in currentBlock)
            {
                if (!(valueEntry.Value is MethodCall call))
                    continue;

                if (!call.Target.HasFlags(MethodFlags.Inline))
                    continue;

                // Cumulative budget check (skipped for ForceInline targets):
                // once the kernel has absorbed enough IL, leave further
                // marked-Inline calls as ordinary function calls. This
                // prevents recursive helper trees from exploding the
                // emitted Wasm/WGSL function.
                if (!call.Target.HasFlags(MethodFlags.ForceInline))
                {
                    int targetIL = call.Target.ILInstructionCount;
                    if (targetIL > 0 &&
                        cumulativeInlinedIL + targetIL > CumulativeInlinedILBudget)
                    {
                        // Budget exhausted for this call — leave it as a call.
                        // Don't bail the whole pass: a later, smaller call in
                        // the same block might still fit.
                        continue;
                    }
                    cumulativeInlinedIL += targetIL;
                }

                var blockBuilder = builder[currentBlock];
                var tempBlock = blockBuilder.SpecializeCall(call);

                // We can continue our search in the temp block
                currentBlock = tempBlock.BasicBlock;
                return true;
            }

            return false;
        }

        #endregion

        #region Instance

        /// <summary>
        /// Constructs a new inliner that inlines all methods marked with
        /// <see cref="MethodFlags.Inline"/> flags.
        /// </summary>
        public Inliner() { }

        #endregion

        #region Methods

        /// <summary>
        /// Applies the inlining transformation.
        /// </summary>
        protected override bool PerformTransformation(
            IRContext context,
            Method.Builder builder,
            Landscape landscape,
            Landscape.Entry current)
        {
            var processed = builder.SourceBlocks.CreateSet();
            var toProcess = new Stack<BasicBlock>();

            // Cumulative IL absorbed by this kernel transformation. Seeded with
            // the kernel's own IL count so the budget represents total post-inline
            // IL, not just inlined IL. ForceInline calls do not consume budget.
            int cumulativeInlinedIL = builder.Method.ILInstructionCount;

            bool result = false;
            var currentBlock = builder.EntryBlock;

            while (true)
            {
                if (processed.Add(currentBlock))
                {
                    if (result = InlineCalls(
                        builder, ref currentBlock, ref cumulativeInlinedIL))
                    {
                        result = true;
                        continue;
                    }

                    var successors = currentBlock.CurrentSuccessors;
                    if (successors.Length > 0)
                    {
                        currentBlock = successors[0];
                        for (int i = 1, e = successors.Length; i < e; ++i)
                            toProcess.Push(successors[i]);
                        continue;
                    }
                }

                if (toProcess.Count < 1)
                    break;
                currentBlock = toProcess.Pop();
            }

            return result;
        }

        #endregion

    }
}
