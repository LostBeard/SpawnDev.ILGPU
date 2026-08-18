// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2018-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: AlgorithmContext.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Algorithms;
using ILGPU.Algorithms.CL;
using ILGPU.Algorithms.IL;
using ILGPU.Algorithms.PTX;
using ILGPU.IR;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using ILGPU.Util;

namespace ILGPU
{
    /// <summary>
    /// Represents the main driver class for all algorithms.
    /// </summary>
    public static partial class AlgorithmContext
    {
        #region Fields

        /// <summary>
        /// The default intrinsic binding flags.
        /// </summary>
        internal const BindingFlags IntrinsicBindingFlags =
            BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// The global <see cref="XMath"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(TrimmingAnnotations.HandlerMethods)]
        internal static readonly Type XMathType = typeof(XMath);

        /// <summary>
        /// The global <see cref="GroupExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(TrimmingAnnotations.HandlerMethods)]
        internal static readonly Type GroupExtensionsType = typeof(GroupExtensions);

        /// <summary>
        /// The global <see cref="WarpExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(TrimmingAnnotations.HandlerMethods)]
        internal static readonly Type WarpExtensionsType = typeof(WarpExtensions);

        #endregion

        #region Static Instance

        /// <summary>
        /// Initializes a static instance.
        /// </summary>
        /// <remarks>
        /// TRIMMING: <see cref="RegisterMathRemappings"/> (T4-generated in
        /// AlgorithmContextMappings.cs) resolves every mapping by name via
        /// <see cref="Type.GetMethod(string, BindingFlags, Binder, Type[],
        /// ParameterModifier[])"/>. The trimmer cannot see those lookups, so
        /// the source and target types are rooted explicitly here - otherwise
        /// an overload the app never calls statically is removed and this
        /// cctor throws MissingMethodException on the first Context.Create.
        /// See the matching block on the RemappedIntrinsics cctor.
        /// </remarks>
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(XMath))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(Math))]
        [DynamicDependency(DynamicallyAccessedMemberTypes.PublicMethods, typeof(MathF))]
        [DynamicDependency(
            DynamicallyAccessedMemberTypes.PublicMethods,
            typeof(IntrinsicMath))]
        [DynamicDependency(
            DynamicallyAccessedMemberTypes.PublicMethods,
            typeof(IntrinsicMath.CPUOnly))]
        static AlgorithmContext()
        {
            RegisterMathRemappings();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Enables algorithm extensions in the scope of the given context builder.
        /// </summary>
        /// <param name="builder">The builder to enable algorithms for.</param>
        public static Context.Builder EnableAlgorithms(this Context.Builder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var intrinsicManager = builder.GetIntrinsicManager();
            CLContext.EnableCLAlgorithms(intrinsicManager);
            ILContext.EnableILAlgorithms(intrinsicManager);
            PTXContext.EnablePTXAlgorithms(intrinsicManager);
            return builder;
        }

        #endregion
    }
}
