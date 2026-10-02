using System.Diagnostics.CodeAnalysis;
// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU
//                    Copyright (c) 2024-2026 SpawnDev / LostBeard
//
// File: WasmAlgorithmContext.cs
//
// Wasm-specific algorithm intrinsic registrations.
// Mirrors WebGPUAlgorithmContext.cs but uses WasmIntrinsic for BackendType.Wasm.
// ---------------------------------------------------------------------------------------

using ILGPU;
using ILGPU.Backends.Wasm;
using ILGPU.IR.Intrinsics;
using ILGPU.Util;
using System;
using System.Reflection;

namespace SpawnDev.ILGPU.Wasm.Algorithms
{
    /// <summary>
    /// Manages custom Wasm-specific algorithm intrinsics.
    /// </summary>
    static class WasmAlgorithmContext
    {
        /// <summary>
        /// The <see cref="WasmGroupExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.HandlerMethods)]
        internal static readonly Type WasmGroupExtensionsType =
            typeof(WasmGroupExtensions);

        /// <summary>
        /// The <see cref="WasmWarpExtensions"/> type.
        /// </summary>
        [DynamicallyAccessedMembers(global::ILGPU.Util.TrimmingAnnotations.HandlerMethods)]
        internal static readonly Type WasmWarpExtensionsType =
            typeof(WasmWarpExtensions);

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
            try
            {
                var sourceMethod = sourceType.GetMethod(
                    name,
                    IntrinsicBindingFlags);
                if (sourceMethod == null)
                {
                    if (Backend.WasmBackend.VerboseLogging)
                        Backend.WasmBackend.Log($"[WasmAlg] ERROR: Source method '{sourceType.Name}.{name}' not found!");
                    return;
                }
                var targetMethod = targetType.GetMethod(
                    name,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
                if (targetMethod == null)
                {
                    if (Backend.WasmBackend.VerboseLogging)
                        Backend.WasmBackend.Log($"[WasmAlg] ERROR: Target method '{targetType.Name}.{name}' not found!");
                    return;
                }
                manager.RegisterMethod(
                    sourceMethod,
                    new WasmIntrinsic(
                        targetType,
                        name,
                        IntrinsicImplementationMode.Redirect));
            }
            catch (Exception ex)
            {
                if (Backend.WasmBackend.VerboseLogging)
                    Backend.WasmBackend.Log($"[WasmAlg] EXCEPTION registering {name}: {ex}");
            }
        }

        /// <summary>
        /// Enables Wasm-specific algorithm intrinsics.
        /// </summary>
        public static void EnableWasmAlgorithms(
            IntrinsicImplementationManager manager)
        {
            // Register group intrinsics (all use Redirect mode)
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "Reduce");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "AllReduce");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "ExclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "InclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "ExclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "InclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "ExclusiveScanNextIteration");
            RegisterIntrinsicMapping(
                manager,
                GroupExtensionsType,
                WasmGroupExtensionsType,
                "InclusiveScanNextIteration");

            // Register warp intrinsics
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WasmWarpExtensionsType,
                "Reduce");
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WasmWarpExtensionsType,
                "AllReduce");
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WasmWarpExtensionsType,
                "ExclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                WarpExtensionsType,
                WasmWarpExtensionsType,
                "InclusiveScan");
        }
    }
}
