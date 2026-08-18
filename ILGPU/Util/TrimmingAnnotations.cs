// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: TrimmingAnnotations.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace ILGPU.Util
{
    /// <summary>
    /// Shared <see cref="DynamicallyAccessedMembersAttribute"/> member sets used to
    /// keep ILGPU trim safe.
    /// </summary>
    /// <remarks>
    /// ILGPU resolves large parts of its intrinsic and remapping tables by NAME at
    /// runtime. The IL trimmer cannot see those lookups, so any member an application
    /// never calls statically is removed and the lookup returns null - surfacing as a
    /// MissingMethodException or "Not supported intrinsic type" on the first
    /// Context.Create. Annotating the <see cref="System.Type"/> that flows into each
    /// lookup tells the trimmer what to keep, and keeps the trim analyzer honest.
    /// </remarks>
    public static class TrimmingAnnotations
    {
        /// <summary>
        /// Members of a type whose methods are looked up by name with
        /// Public | NonPublic | Static | Instance binding flags - the intrinsic
        /// handler types (PTXMath, CLGroupExtensions, XMath, ...).
        /// </summary>
        public const DynamicallyAccessedMemberTypes HandlerMethods =
            DynamicallyAccessedMemberTypes.PublicMethods |
            DynamicallyAccessedMemberTypes.NonPublicMethods;

        /// <summary>
        /// Members of a type whose public static methods are looked up by name -
        /// the remapping sources and targets (System.Math, IntrinsicMath, ...).
        /// </summary>
        public const DynamicallyAccessedMemberTypes PublicMethods =
            DynamicallyAccessedMemberTypes.PublicMethods;

        /// <summary>
        /// Members of a type whose fields define a GPU structure layout.
        /// Losing a field here silently changes the layout rather than throwing,
        /// so this one guards correctness, not just startup.
        /// </summary>
        public const DynamicallyAccessedMemberTypes StructureFields =
            DynamicallyAccessedMemberTypes.PublicFields |
            DynamicallyAccessedMemberTypes.NonPublicFields;

        /// <summary>
        /// Members of a type that Reflection.Emit subclasses at runtime, and whose
        /// constructors and property getters the emitted IL then calls directly
        /// (CPUAcceleratorTask). Nothing calls these statically, so without this the
        /// trimmer removes them and kernel compilation fails with an
        /// ArgumentNullException from ILGenerator.Emit.
        /// </summary>
        public const DynamicallyAccessedMemberTypes RuntimeSubclass =
            DynamicallyAccessedMemberTypes.All;

        /// <summary>
        /// Constructors of a type resolved by signature at runtime.
        /// </summary>
        public const DynamicallyAccessedMemberTypes Constructors =
            DynamicallyAccessedMemberTypes.PublicConstructors |
            DynamicallyAccessedMemberTypes.NonPublicConstructors;

        /// <summary>
        /// Properties of a type resolved by name at runtime.
        /// </summary>
        public const DynamicallyAccessedMemberTypes Properties =
            DynamicallyAccessedMemberTypes.PublicProperties |
            DynamicallyAccessedMemberTypes.NonPublicProperties;

        /// <summary>
        /// The private backing field of a DelegateSpecialized&lt;T&gt; struct, which
        /// DelegateSpecializationRouter reads by name to recover the wrapped
        /// delegate. Nothing reads it statically, so it must be preserved
        /// explicitly or delegate specialization breaks at launch time.
        /// </summary>
        public const DynamicallyAccessedMemberTypes SpecializedDelegateFields =
            DynamicallyAccessedMemberTypes.NonPublicFields;

        /// <summary>
        /// Justification for the delegate-specialization router's field lookup.
        /// </summary>
        public const string SpecializedDelegate =
            "The '_delegate' field of DelegateSpecialization<> is rooted by an " +
            "explicit DynamicDependency on the router, and is additionally read " +
            "directly by DelegateSpecializationHelper, so the trimmer preserves it. " +
            "The requirement is deliberately NOT expressed on the generic parameter: " +
            "that would propagate out through every KernelLoaders.Load*Kernel " +
            "overload and onto consumer kernel signatures.";

        /// <summary>
        /// Justification for reflection over a type built at runtime with
        /// Reflection.Emit (the launcher / accelerator-task classes).
        /// </summary>
        public const string EmittedType =
            "The type being reflected over is created at runtime by Reflection.Emit " +
            "(RuntimeSystem.DefineRuntimeClass / DefineRuntimeStruct). It does not " +
            "exist in any assembly the trimmer processes, so none of its members can " +
            "be trimmed. Where the reflected type is instead a fixed ILGPU base type, " +
            "that base is rooted by the DynamicallyAccessedMembers annotation on the " +
            "DefineRuntimeClass call that produced the subclass.";

        /// <summary>
        /// Justification for reflection over a lambda's compiler-generated display
        /// class, which is how captured scalars reach a kernel.
        /// </summary>
        public const string DisplayClass =
            "The fields of a lambda's compiler-generated display class are written " +
            "by the enclosing method's own IL (newobj + stfld), so the trimmer keeps " +
            "them for exactly the reason it keeps them for the JIT - removing one " +
            "would break the code that captures the variable. Verified end to end: a " +
            "captured-scalar kernel returns correct values under TrimMode=full.";

        /// <summary>
        /// Justification for MakeGenericMethod / MakeGenericType sites whose target
        /// declares no DynamicallyAccessedMembers requirement.
        /// </summary>
        public const string UnconstrainedGeneric =
            "The generic method or type being closed declares no " +
            "DynamicallyAccessedMembers requirement on its type parameters, so " +
            "closing it over a runtime Type preserves nothing beyond the type " +
            "itself, which the caller already holds. The instantiation is over a " +
            "kernel parameter or field type that is reachable from the kernel " +
            "signature.";

        /// <summary>
        /// Justification for reflection over the fields of a type that is used as a
        /// kernel parameter or an IR structure type.
        /// </summary>
        public const string StructureLayout =
            "The type is a blittable value type reachable from a kernel signature. " +
            "The trimmer preserves the instance fields of a value type because its " +
            "layout is observable - removing one would change sizeof and every " +
            "struct copy - so the field walk ILGPU performs here sees the same " +
            "layout the runtime does. Verified end to end: a kernel taking a struct " +
            "with a field the host never reads returns correct values under " +
            "TrimMode=full.";

        /// <summary>
        /// Justification for the view-implementation generics, whose members are
        /// rooted by an explicit DynamicDependency on ViewImplementation.
        /// </summary>
        public const string RootedViewImplementation =
            "ViewImplementation<> has an explicit DynamicDependency rooting all of " +
            "its members, so closing the open generic over a kernel element type " +
            "finds the constructor and fields the argument mapper needs. The " +
            "element type itself is reachable from the kernel signature.";

        /// <summary>
        /// Justification for IL2111 on ILGPU's own intrinsic registrars.
        /// </summary>
        /// <remarks>
        /// A handler type annotated with <see cref="HandlerMethods"/> has ALL of its
        /// methods marked reflection-accessible. When the same type is also the class
        /// that registers the intrinsics - CLContext, PTXIntrinsics, CPUAcceleratorTask -
        /// its own annotated registrar helpers get swept up, and the analyzer reports
        /// that it cannot prove their annotated parameters are satisfied. They are:
        /// every call site passes a typeof() literal, which the trimmer resolves
        /// statically, and nothing ever invokes these helpers through reflection.
        /// </remarks>
        public const string SelfRegistrar =
            "The reported methods are ILGPU's own intrinsic registrars. Every call " +
            "site passes a typeof() literal that the trimmer resolves statically, so " +
            "the DynamicallyAccessedMembers requirement on their parameters is " +
            "always satisfied, and nothing invokes them through reflection. They are " +
            "only flagged because the enclosing handler type is itself annotated, " +
            "which marks all of its methods reflection-accessible.";

        /// <summary>
        /// Justification for the IL-frontend sites that call
        /// <see cref="System.Reflection.MethodBase.GetMethodBody"/> and the
        /// Module.Resolve* family, both of which the BCL marks
        /// RequiresUnreferencedCode.
        /// </summary>
        /// <remarks>
        /// Read this before adding a new suppression that points at it - it states
        /// a specific guarantee, not a blanket "we know better".
        /// </remarks>
        public const string ILFrontend =
            "ILGPU disassembles a kernel body and resolves that body's metadata " +
            "tokens against the SAME module the body was read from. The trimmer " +
            "cannot leave a kept method body referencing a removed token, because " +
            "the runtime could not JIT such a body, so the IL and the metadata " +
            "stay mutually consistent after trimming. The kernel's callees are " +
            "reachable through ordinary call instructions, so the trimmer keeps " +
            "them for the same reason it keeps them for the JIT. RESIDUAL " +
            "LIMITATION: ILLink feature-switch body substitution can replace a " +
            "kept body, and ILGPU then compiles the substituted body - which is " +
            "also what the CPU would execute, so the two stay in agreement. " +
            "Trimming a kernel's transitive callees away is therefore not " +
            "possible; AOT with WasmStripILAfterAOT, which removes IL outright, " +
            "IS incompatible and is a separate, documented constraint.";
    }
}
