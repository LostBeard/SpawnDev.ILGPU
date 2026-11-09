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
        private readonly bool keepGlobalViewCalls;

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
            : this(inlineBudgetSurvivors, keepGlobalViewCalls: false)
        { }

        /// <summary>
        /// <paramref name="keepGlobalViewCalls"/>: leave a call to a NON-Inline helper (<c>[NoInlining]</c> / over the
        /// IL cap) as a call when every address-carrying argument is a plain view of GLOBAL memory (a storage buffer):
        /// the generator emits such a helper once as a function with <c>ptr&lt;storage&gt;</c> parameters. Calls passing
        /// local or shared memory, or a structure that carries an address, are still inlined (the case the transformation
        /// exists for). Without it, every view-taking helper was copied into every call site: SpawnDev.Codecs' VP9 decoder
        /// kernels reached 33-87 MB of WGSL (2026-11-09).
        /// </summary>
        public InlineAddressParameterCalls(bool inlineBudgetSurvivors, bool keepGlobalViewCalls)
        {
            this.inlineBudgetSurvivors = inlineBudgetSurvivors;
            this.keepGlobalViewCalls = keepGlobalViewCalls;
        }

        /// <summary>
        /// True when every argument of <paramref name="call"/> that carries an address is a view or pointer (not a
        /// structure containing one) of a supported element type (<see cref="IsHelperAbiElement"/>) whose origin is a
        /// KERNEL parameter (a storage buffer). Views of an allocation (local / shared memory), of a helper's own
        /// parameter, chosen at run time (phi, select) or loaded from memory are not. Address spaces are not inferred
        /// yet at this point (the arguments are Generic), so the origin is traced instead.
        /// </summary>
        public static bool PassesOnlyGlobalViews(MethodCall call)
        {
            for (int i = 0, e = call.Count; i < e; ++i)
            {
                var type = call[i].Type;
                if (!CarriesAddress(type))
                    continue;
                if (type is not ViewType view || !IsHelperAbiElement(view.ElementType) || !OriginatesFromKernelParameter(call[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Version 1 of the helper-function view ABI (Plans/view-helpers-as-functions-2026-11-09.md): a call in the KERNEL
        /// whose view arguments are all kernel-parameter views of a supported element type, to a helper that (with
        /// everything it calls) has no atomics, barriers, warp / broadcast operations or shared memory, in a kernel that
        /// uses no atomics anywhere (an atomically used binding is declared <c>array&lt;atomic&lt;T&gt;&gt;</c>, which a
        /// plain <c>ptr&lt;storage, array&lt;T&gt;&gt;</c> parameter would not match).
        /// </summary>
        private static bool KeepsAsFunction(Method caller, Method target, MethodCall call) =>
            caller.HasFlags(MethodFlags.EntryPoint)
            && PassesOnlyGlobalViews(call)
            && !UsesSynchronization(target, new HashSet<Method>(), sharedToo: true)
            && !UsesSynchronization(caller, new HashSet<Method>(), sharedToo: false);

        /// <summary>
        /// True when <paramref name="method"/> or anything it calls contains an atomic, a barrier, a memory barrier, a warp
        /// shuffle / broadcast, or a shared-memory allocation; with <paramref name="sharedToo"/> false (the kernel-wide
        /// check) only atomics count.
        /// </summary>
        private static bool UsesSynchronization(Method method, HashSet<Method> visited, bool sharedToo)
        {
            if (!visited.Add(method) || !method.HasImplementation)
                return false;
            foreach (var block in method.Blocks)
                foreach (var entry in block)
                {
                    switch (entry.Value)
                    {
                        case AtomicValue:
                            return true;
                        case BarrierOperation or MemoryBarrier or Broadcast or ShuffleOperation when sharedToo:
                            return true;
                        case Alloca alloca when sharedToo && alloca.AddressSpace == MemoryAddressSpace.Shared:
                            return true;
                        case MethodCall nested when UsesSynchronization(nested.Target, visited, sharedToo):
                            return true;
                    }
                }
            return false;
        }

        /// <summary>
        /// Element types the WGSL helper-function view ABI (binding pointer + element offset) supports: plain 32-bit
        /// (<c>array&lt;i32|u32|f32&gt;</c>) and the sub-word types packed into <c>array&lt;atomic&lt;u32&gt;&gt;</c>. 64-bit
        /// (emulated vec2 storage), packed 4-bit and structure elements keep the inlining path.
        /// </summary>
        private static bool IsHelperAbiElement(TypeNode elementType) =>
            elementType is PrimitiveType primitive && primitive.BasicValueType switch
            {
                BasicValueType.Int8 or BasicValueType.Int16 or BasicValueType.Int32 or
                BasicValueType.Float16 or BasicValueType.BFloat16 or BasicValueType.Float32 or
                BasicValueType.Float8E4M3 or BasicValueType.Float8E5M2 => true,
                _ => false,
            };

        /// <summary>
        /// Follows sub-views, casts, alignment hints and field reads back to a parameter of the KERNEL (entry point): a
        /// storage buffer. A helper's own parameter does not qualify, so a view-taking call inside a surviving helper is
        /// inlined into that helper's function.
        /// </summary>
        private static bool OriginatesFromKernelParameter(Value value)
        {
            for (int guard = 0; guard < 256; guard++)
            {
                value = value.Resolve();
                switch (value)
                {
                    case Parameter parameter:
                        return parameter.Method.HasFlags(MethodFlags.EntryPoint);
                    case PointerValue pointerValue:   // SubViewValue, LoadElementAddress, AsAligned / AlignTo
                        value = pointerValue.Source;
                        continue;
                    case CastValue cast:              // AddressSpaceCast, ViewCast, PointerCast
                        value = cast.Value;
                        continue;
                    case NewView newView:
                        value = newView.Pointer;
                        continue;
                    case GetField getField:
                        value = getField.ObjectValue;
                        continue;
                    default:                          // Alloca (local / shared), phi, select, load, ...
                        return false;
                }
            }
            return false;
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
                    // WebGPU non-Inline helper ([NoInlining] / over the IL cap): a WGSL fn-def, which can take a
                    // storage-buffer view (ptr<storage>) but not a local / shared / struct-carried address.
                    inline = TakesAddress(target) && !(keepGlobalViewCalls && KeepsAsFunction(builder.Method, target, call));
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
