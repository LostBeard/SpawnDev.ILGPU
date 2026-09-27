// ---------------------------------------------------------------------------------------
//                                    SpawnDev.ILGPU
//
// File: InlineAddressParameterCalls.cs
//
// This file is part of the SpawnDev ILGPU fork and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Analyses;
using ILGPU.IR.Types;
using ILGPU.IR.Values;
using System.Collections.Generic;

namespace ILGPU.IR.Transformations
{
    /// <summary>
    /// Inlines every remaining call whose target takes a pointer or view parameter (directly or inside a
    /// structure), regardless of the <see cref="Inliner"/>'s size caps and cumulative budget.
    /// </summary>
    /// <remarks>
    /// For backends whose function-definition emission cannot pass an address: the WGSL and GLSL generators
    /// do not marshal pointer/view parameters into helper functions. MEASURED 2026-09-27 (SpawnScene
    /// GpuEpipolarRansac): a 1,000+ IL helper taking <c>LocalMemory</c> views stayed a call once the kernel's
    /// inlining budget was spent, and the WGSL generator typed every view parameter
    /// <c>ptr&lt;storage, array&lt;f32&gt;&gt;</c>, declared the function-local array's alias as a plain
    /// <c>f32</c> and passed wrong arguments - Tint rejected the shader ("cannot assign
    /// 'ptr&lt;function, array&lt;f32, 72&gt;, read_write&gt;' to 'f32'") only at pipeline creation, in the
    /// browser. The CPU accelerator ran the same kernel correctly, so nothing earlier could see it.
    /// A call the generator cannot express must never reach it: inlining is always correct, at the cost of
    /// a larger shader (the budget exists for Wasm's local-count limit, which these backends do not share).
    /// This applies to <c>[MethodImpl(NoInlining)]</c> helpers too - for these backends the attribute cannot
    /// be honoured for an address-taking helper without miscompiling it.
    /// </remarks>
    public sealed class InlineAddressParameterCalls : OrderedTransformation
    {
        private readonly bool inlineBudgetSurvivors;

        /// <summary>
        /// <paramref name="inlineBudgetSurvivors"/>: also inline calls whose target IS marked
        /// <see cref="MethodFlags.Inline"/> but was left as a call once the <see cref="Inliner"/>'s cumulative
        /// budget ran out. For WebGPU: its generator inlines those at EMISSION time anyway (same code size, so
        /// the budget saves nothing there) through a separate structurizer that mis-emits early exits
        /// (MEASURED 2026-09-27: a scalar helper's `return` from nested loops kept looping and was then
        /// overwritten by the later loop's result - HelperCodegen_ScalarHelper_EarlyReturnSkipsLoop_ManyCallSites).
        /// IR inlining routes them through the kernel's structured walker, which handles them. Helpers that
        /// allocate shared memory stay with the emission inliner (<see cref="AllocatesShared(Method)"/>). With
        /// <c>false</c> (WebGL - no emission inliner, every surviving call is a GLSL function) only address-taking
        /// calls are inlined.
        /// </summary>
        public InlineAddressParameterCalls(bool inlineBudgetSurvivors = false)
        {
            this.inlineBudgetSurvivors = inlineBudgetSurvivors;
        }

        /// <summary>True when <paramref name="type"/> is, or (for a structure) contains, a pointer or view.</summary>
        public static bool CarriesAddress(TypeNode type)
        {
            if (type.IsViewOrPointerType)
                return true;
            if (type is StructureType structure)
            {
                foreach (var (fieldType, _) in structure)
                    if (CarriesAddress(fieldType))
                        return true;
            }
            return false;
        }

        /// <summary>
        /// True when <paramref name="method"/>, or anything it calls, allocates SHARED (workgroup) memory. The
        /// WGSL generator's emission-time inliner maps every call site of such a helper onto ONE workgroup
        /// array; IR inlining would give each copy its own and multiply workgroup storage (MEASURED 2026-09-27:
        /// radix sort's scan helpers went 16 KB -> 48 KB, over WebGPU's 32 KB limit, 112 PMT failures).
        /// </summary>
        public static bool AllocatesShared(Method method) => AllocatesShared(method, new HashSet<Method>());

        private static bool AllocatesShared(Method method, HashSet<Method> visited)
        {
            if (!visited.Add(method) || !method.HasImplementation)
                return false;
            foreach (var block in method.Blocks)
                foreach (var entry in block)
                {
                    if (entry.Value is Alloca alloca && alloca.AddressSpace == MemoryAddressSpace.Shared)
                        return true;
                    if (entry.Value is MethodCall call && AllocatesShared(call.Target, visited))
                        return true;
                }
            return false;
        }

        /// <summary>True when any parameter of <paramref name="method"/> carries an address.</summary>
        public static bool TakesAddress(Method method)
        {
            foreach (var param in method.Parameters)
                if (CarriesAddress(param.ParameterType))
                    return true;
            return false;
        }

        private bool InlineCalls(Method.Builder builder, ref BasicBlock currentBlock)
        {
            foreach (var valueEntry in currentBlock)
            {
                if (!(valueEntry.Value is MethodCall call))
                    continue;
                var target = call.Target;
                if (!target.HasImplementation || target == builder.Method)
                    continue;
                bool inline;
                if (!inlineBudgetSurvivors)
                {
                    // WebGL: every call left in the IR becomes a GLSL function, which cannot take an address.
                    inline = TakesAddress(target);
                }
                else if (target.HasFlags(MethodFlags.Inline))
                {
                    // WebGPU budget survivor: the generator would inline it at emission time through a
                    // structurizer that mis-emits early exits - inline it here instead, unless it allocates
                    // shared memory (the emission inliner shares one workgroup array across call sites).
                    inline = !AllocatesShared(target);
                }
                else
                {
                    // WebGPU non-Inline helper ([NoInlining] / over the IL cap): a WGSL fn-def, which cannot
                    // marshal an address.
                    inline = TakesAddress(target);
                }
                if (!inline)
                    continue;
                var tempBlock = builder[currentBlock].SpecializeCall(call);
                currentBlock = tempBlock.BasicBlock;
                return true;
            }
            return false;
        }

        /// <summary>Applies the transformation (same block walk as <see cref="Inliner"/>).</summary>
        protected override bool PerformTransformation(
            IRContext context,
            Method.Builder builder,
            Landscape landscape,
            Landscape.Entry current)
        {
            var processed = builder.SourceBlocks.CreateSet();
            var toProcess = new Stack<BasicBlock>();
            bool result = false;
            var currentBlock = builder.EntryBlock;
            while (true)
            {
                if (processed.Add(currentBlock))
                {
                    if (InlineCalls(builder, ref currentBlock))
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
    }
}
