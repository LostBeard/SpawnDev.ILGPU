using System.Diagnostics.CodeAnalysis;
// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                    Copyright (c) 2024-2026 SpawnDev / LostBeard
//
// File: WebGPUAlgorithmContext.cs
//
// WebGPU-specific algorithm intrinsic registrations.
// Mirrors the pattern of ILGPU.Algorithms/CL/CLContext.cs and IL/ILContext.cs
// but lives in SpawnDev.ILGPU to keep Blazor backends separate from upstream ILGPU.
// ---------------------------------------------------------------------------------------

using ILGPU;
using ILGPU.Backends.WebGPU;
using ILGPU.IR.Intrinsics;
using ILGPU.Util;
using System;
using System.Reflection;

namespace SpawnDev.ILGPU.WebGPU.Algorithms
{
    /// <summary>
    /// Manages custom WebGPU-specific algorithm intrinsics.
    /// </summary>
    static class WebGPUAlgorithmContext
    {
        /// <summary>
        /// The <see cref="WebGPUGroupExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.HandlerMethods)]
        internal static readonly Type WebGPUGroupExtensionsType =
            typeof(WebGPUGroupExtensions);

        /// <summary>
        /// The <see cref="WebGPUWarpExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.HandlerMethods)]
        internal static readonly Type WebGPUWarpExtensionsType =
            typeof(WebGPUWarpExtensions);

        /// <summary>
        /// The <see cref="ILGPU.Algorithms.GroupExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.PublicMethods)]
        internal static readonly Type GroupExtensionsType =
            typeof(global::ILGPU.Algorithms.GroupExtensions);

        /// <summary>
        /// The <see cref="ILGPU.Algorithms.WarpExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.PublicMethods)]
        internal static readonly Type WarpExtensionsType =
            typeof(global::ILGPU.Algorithms.WarpExtensions);

        /// <summary>
        /// Binding flags for finding intrinsic methods.
        /// </summary>
        internal const BindingFlags IntrinsicBindingFlags =
            BindingFlags.Public | BindingFlags.Static;

        /// <summary>
        /// Registers an intrinsic mapping (redirect mode).
        /// </summary>
        private static void RegisterIntrinsicMapping(
            IntrinsicImplementationManager manager,
            [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.PublicMethods)] Type sourceType,
            [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.HandlerMethods)] Type targetType,
            string name)
        {
            var sourceMethod = sourceType.GetMethod(
                name,
                IntrinsicBindingFlags)
                .ThrowIfNull();
            manager.RegisterMethod(
                sourceMethod,
                new WebGPUIntrinsic(
                    targetType,
                    name,
                    IntrinsicImplementationMode.Redirect));
        }

        /// <summary>
        /// Enables WebGPU-specific algorithm intrinsics.
        /// </summary>
        public static void EnableWebGPUAlgorithms(
            IntrinsicImplementationManager manager)
        {
            // Register group intrinsics (all use Redirect mode)
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "Reduce");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "AllReduce");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "ExclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "InclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "ExclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "InclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "ExclusiveScanNextIteration");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WebGPUGroupExtensionsType,
                "InclusiveScanNextIteration");

            // Register warp intrinsics
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WebGPUWarpExtensionsType,
                "Reduce");
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WebGPUWarpExtensionsType,
                "AllReduce");
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WebGPUWarpExtensionsType,
                "ExclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WebGPUWarpExtensionsType,
                "InclusiveScan");
        }
    }
}
