// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: KernelLoaders.tt/KernelLoaders.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2016-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeInformation.ttinclude
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Runtime.CompilerServices;

#pragma warning disable IDE0046 // Convert to conditional expression

// disable: max_line_length

namespace ILGPU.Runtime
{
    /// <summary>
    /// Contains extensions for convenient kernel loading of default kernels.
    /// </summary>
    public static class KernelLoaders
    {
        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1> LoadKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action)
            where T1 : struct =>
            accelerator.LoadKernel<T1>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1> LoadKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action,
            out KernelInfo? kernelInfo)
            where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1> LoadStreamKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action)
            where T1 : struct =>
            accelerator.LoadStreamKernel<T1>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1> LoadStreamKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action,
            out KernelInfo? kernelInfo)
            where T1 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1 });
            var baseKernel = accelerator.LoadKernel<T1>(action, out kernelInfo);
            return (KernelConfig config, T1 param1) =>
                baseKernel(accelerator.DefaultStream, config, param1);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1> LoadKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action,
            KernelSpecialization specialization)
            where T1 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1> LoadKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1> LoadStreamKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action,
            KernelSpecialization specialization)
            where T1 : struct =>
            accelerator.LoadStreamKernel<T1>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1> LoadStreamKernel<T1>(
            this Accelerator accelerator,
            Action<T1> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1 });
            var baseKernel = accelerator.LoadKernel<T1>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1) =>
                baseKernel(accelerator.DefaultStream, config, param1);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1> LoadImplicitlyGroupedKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1> LoadImplicitlyGroupedKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1> LoadImplicitlyGroupedStreamKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1> LoadImplicitlyGroupedStreamKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1) =>
                baseKernel(accelerator.DefaultStream, index, param1);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1> LoadAutoGroupedKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1> LoadAutoGroupedKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1> LoadAutoGroupedStreamKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1>(action);
            return (TIndex index, T1 param1) =>
                baseKernel(accelerator.DefaultStream, index, param1);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1> LoadAutoGroupedStreamKernel<TIndex, T1>(
            this Accelerator accelerator,
            Action<TIndex, T1> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1) =>
                baseKernel(accelerator.DefaultStream, index, param1);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2> LoadKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action)
            where T1 : struct where T2 : struct =>
            accelerator.LoadKernel<T1, T2>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2> LoadKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2> LoadStreamKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action)
            where T1 : struct where T2 : struct =>
            accelerator.LoadStreamKernel<T1, T2>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2> LoadStreamKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2 });
            var baseKernel = accelerator.LoadKernel<T1, T2>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2> LoadKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2> LoadKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2> LoadStreamKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct =>
            accelerator.LoadStreamKernel<T1, T2>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2> LoadStreamKernel<T1, T2>(
            this Accelerator accelerator,
            Action<T1, T2> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2 });
            var baseKernel = accelerator.LoadKernel<T1, T2>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2> LoadImplicitlyGroupedKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2> LoadImplicitlyGroupedKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2> LoadAutoGroupedKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2> LoadAutoGroupedKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2> LoadAutoGroupedStreamKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2>(action);
            return (TIndex index, T1 param1, T2 param2) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2> LoadAutoGroupedStreamKernel<TIndex, T1, T2>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3> LoadKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action)
            where T1 : struct where T2 : struct where T3 : struct =>
            accelerator.LoadKernel<T1, T2, T3>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3> LoadKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3> LoadStreamKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action)
            where T1 : struct where T2 : struct where T3 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3> LoadStreamKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3> LoadKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3> LoadKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3> LoadStreamKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3> LoadStreamKernel<T1, T2, T3>(
            this Accelerator accelerator,
            Action<T1, T2, T3> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3> LoadAutoGroupedKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3> LoadAutoGroupedKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4> LoadKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4> LoadKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4> LoadStreamKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4> LoadStreamKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4> LoadKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4> LoadKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4> LoadStreamKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4> LoadStreamKernel<T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5> LoadKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5> LoadKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5> LoadStreamKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5> LoadStreamKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5> LoadKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5> LoadKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5> LoadStreamKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5> LoadStreamKernel<T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6> LoadKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6> LoadKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6> LoadStreamKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6> LoadStreamKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6> LoadKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6> LoadKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6> LoadStreamKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6> LoadStreamKernel<T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(
                action.Method,
                default,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(action, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, default, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(action, out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
                accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(
                action.Method,
                specialization,
                out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadKernel<Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(
                action.Method,
                specialization,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            KernelSpecialization specialization)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            accelerator.LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(action, specialization, out var _);

        /// <summary>
        /// Loads the given explicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="specialization">The kernel specialization.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernels will be launched with a group size
        /// of the current warp size of the accelerator.
        /// </remarks>
        public static Action<KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadStreamKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            KernelSpecialization specialization,
            out KernelInfo? kernelInfo)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchKernel(action.Method, specialization, out kernelInfo) is { } boxedKernel)
                return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, config, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14 });
            var baseKernel = accelerator.LoadKernel<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
                action,
                specialization,
                out kernelInfo);
            return (KernelConfig config, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                baseKernel(accelerator.DefaultStream, config, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(
                action.Method,
                customGroupSize);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that can receive arbitrary accelerator streams (first parameter).
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadImplicitlyGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(
                action.Method,
                customGroupSize,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            int customGroupSize)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            accelerator.LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
                action,
                customGroupSize,
                out var _);

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// group size.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="customGroupSize">The custom group size to use.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        /// <remarks>
        /// Note that implicitly-grouped kernel will be launched with the given group size.
        /// </remarks>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadImplicitlyGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            int customGroupSize,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchImplicitlyGroupedKernel(action.Method, customGroupSize, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14 });
            var baseKernel = accelerator.LoadImplicitlyGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
                action,
                customGroupSize,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(action.Method);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that
        /// can receive arbitrary accelerator streams (first parameter).
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return accelerator.LoadAutoGroupedKernel<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(
                action.Method,
                out kernelInfo);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate
        /// that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            if (action.Method.IsCapturingLambda())
                return CapturingLambdaHelper
                    .LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
                        accelerator, action);
            // Check if any type parameter is a DelegateSpecialization<T>
            if (DelegateSpecializationRouter.TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
                    accelerator, action, out var dsResult))
                return dsResult!;
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out _) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(action);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14);
        }

        /// <summary>
        /// Loads the given implicitly grouped kernel and returns a launcher delegate that uses the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="accelerator">The current accelerator.</param>
        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelInfo">Detailed information about the loaded kernel.</param>
        /// <returns>The loaded kernel-launcher delegate.</returns>
        public static Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> LoadAutoGroupedStreamKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            this Accelerator accelerator,
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            out KernelInfo? kernelInfo)
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            // Browser backends: launch the kernel with boxed arguments and never create the launcher delegate,
            // which Mono AOT cannot always create (see Accelerator.SupportsBoxedLaunch).
            ArgumentNullException.ThrowIfNull(action);
            if (accelerator.LoadBoxedLaunchAutoGroupedKernel(action.Method, out kernelInfo) is { } boxedKernel)
                return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                    boxedKernel.LaunchBoxed(accelerator.DefaultStream, index, new object[] { param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14 });
            var baseKernel = accelerator.LoadAutoGroupedKernel<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
                action,
                out kernelInfo);
            return (TIndex index, T1 param1, T2 param2, T3 param3, T4 param4, T5 param5, T6 param6, T7 param7, T8 param8, T9 param9, T10 param10, T11 param11, T12 param12, T13 param13, T14 param14) =>
                baseKernel(accelerator.DefaultStream, index, param1, param2, param3, param4, param5, param6, param7, param8, param9, param10, param11, param12, param13, param14);
        }

    }

    // Additional accelerator launch methods

    partial class Accelerator
    {
        #region Nested Types

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1> : IKernelLaunchLoader<
            Action<T1>,
            Action<AcceleratorStream, KernelConfig, T1>>
            where T1 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1> Load(
                Accelerator accelerator,
                Action<T1> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1> : IKernelLaunchLoader<
            Action<TIndex, T1>,
            Action<AcceleratorStream, TIndex, T1>>
            where TIndex : struct, IIndex
            where T1 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1> Load(
                Accelerator accelerator,
                Action<TIndex, T1> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2> : IKernelLaunchLoader<
            Action<T1, T2>,
            Action<AcceleratorStream, KernelConfig, T1, T2>>
            where T1 : struct where T2 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2> Load(
                Accelerator accelerator,
                Action<T1, T2> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2> : IKernelLaunchLoader<
            Action<TIndex, T1, T2>,
            Action<AcceleratorStream, TIndex, T1, T2>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3> : IKernelLaunchLoader<
            Action<T1, T2, T3>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3>>
            where T1 : struct where T2 : struct where T3 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3> Load(
                Accelerator accelerator,
                Action<T1, T2, T3> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3>,
            Action<AcceleratorStream, TIndex, T1, T2, T3>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7, T8>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7, T8> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an explicit kernel loader.
        /// </summary>
        private readonly struct KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IKernelLaunchLoader<
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>,
            Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> Load(
                Accelerator accelerator,
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> source) =>
                KernelLoaders.LoadKernel(accelerator, source);
        }

        /// <summary>
        /// Wraps an automatically grouped kernel loader.
        /// </summary>
        private readonly struct KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> : IKernelLaunchLoader<
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>,
            Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>
            where TIndex : struct, IIndex
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public readonly Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> Load(
                Accelerator accelerator,
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> source) =>
                KernelLoaders.LoadAutoGroupedKernel(accelerator, source);
        }


        #endregion

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1>(
            Action<T1> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1)
            where T1 : struct =>
            GetOrLoadLauncher<
                Action<T1>,
                Action<AcceleratorStream, KernelConfig, T1>,
                KernelLaunchLoader<T1>>(action)(
                stream,
                kernelConfig,
                arg1);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1>(
            Action<T1> action,
            in KernelConfig kernelConfig,
            T1 arg1)
            where T1 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1>(
            Action<TIndex, T1> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1>,
                Action<AcceleratorStream, TIndex, T1>,
                KernelLaunchAutoGroupedLoader<TIndex, T1>>(action)(
                stream,
                extent,
                arg1);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1>(
            Action<TIndex, T1> action,
            TIndex extent,
            T1 arg1)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2>(
            Action<T1, T2> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2)
            where T1 : struct where T2 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2>,
                Action<AcceleratorStream, KernelConfig, T1, T2>,
                KernelLaunchLoader<T1, T2>>(action)(
                stream,
                kernelConfig,
                arg1, arg2);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2>(
            Action<T1, T2> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2)
            where T1 : struct where T2 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2>(
            Action<TIndex, T1, T2> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2>,
                Action<AcceleratorStream, TIndex, T1, T2>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2>>(action)(
                stream,
                extent,
                arg1, arg2);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2>(
            Action<TIndex, T1, T2> action,
            TIndex extent,
            T1 arg1, T2 arg2)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3>(
            Action<T1, T2, T3> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3)
            where T1 : struct where T2 : struct where T3 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3>,
                KernelLaunchLoader<T1, T2, T3>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3>(
            Action<T1, T2, T3> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3)
            where T1 : struct where T2 : struct where T3 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3>(
            Action<TIndex, T1, T2, T3> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3>,
                Action<AcceleratorStream, TIndex, T1, T2, T3>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3>>(action)(
                stream,
                extent,
                arg1, arg2, arg3);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3>(
            Action<TIndex, T1, T2, T3> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4>(
            Action<T1, T2, T3, T4> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4>,
                KernelLaunchLoader<T1, T2, T3, T4>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4>(
            Action<T1, T2, T3, T4> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4>(
            Action<TIndex, T1, T2, T3, T4> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4>(
            Action<TIndex, T1, T2, T3, T4> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5>(
            Action<T1, T2, T3, T4, T5> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5>,
                KernelLaunchLoader<T1, T2, T3, T4, T5>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5>(
            Action<T1, T2, T3, T4, T5> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5>(
            Action<TIndex, T1, T2, T3, T4, T5> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5>(
            Action<TIndex, T1, T2, T3, T4, T5> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6>(
            Action<T1, T2, T3, T4, T5, T6> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6>(
            Action<T1, T2, T3, T4, T5, T6> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6>(
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6>(
            Action<TIndex, T1, T2, T3, T4, T5, T6> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7>(
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7>(
            Action<T1, T2, T3, T4, T5, T6, T7> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7, T8>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>
        /// <param name="arg14">Argument 14 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            AcceleratorStream stream,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            GetOrLoadLauncher<
                Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>,
                Action<AcceleratorStream, KernelConfig, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>,
                KernelLaunchLoader<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(action)(
                stream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="kernelConfig">The launch configuration.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>
        /// <param name="arg14">Argument 14 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Launch<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            in KernelConfig kernelConfig,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            Launch(
                action,
                DefaultStream,
                kernelConfig,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="stream">The stream to use.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>
        /// <param name="arg14">Argument 14 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            AcceleratorStream stream,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            GetOrLoadLauncher<
                Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>,
                Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>,
                KernelLaunchAutoGroupedLoader<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>>(action)(
                stream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);

        /// <summary>
        /// Loads the given kernel and launches it immediately using the given arguments
        /// with the help of the default accelerator stream.
        /// </summary>
        /// <typeparam name="TIndex">The index type.</typeparam>
        /// <typeparam name="T1">Parameter type of parameter 1.</typeparam>
        /// <typeparam name="T2">Parameter type of parameter 2.</typeparam>
        /// <typeparam name="T3">Parameter type of parameter 3.</typeparam>
        /// <typeparam name="T4">Parameter type of parameter 4.</typeparam>
        /// <typeparam name="T5">Parameter type of parameter 5.</typeparam>
        /// <typeparam name="T6">Parameter type of parameter 6.</typeparam>
        /// <typeparam name="T7">Parameter type of parameter 7.</typeparam>
        /// <typeparam name="T8">Parameter type of parameter 8.</typeparam>
        /// <typeparam name="T9">Parameter type of parameter 9.</typeparam>
        /// <typeparam name="T10">Parameter type of parameter 10.</typeparam>
        /// <typeparam name="T11">Parameter type of parameter 11.</typeparam>
        /// <typeparam name="T12">Parameter type of parameter 12.</typeparam>
        /// <typeparam name="T13">Parameter type of parameter 13.</typeparam>
        /// <typeparam name="T14">Parameter type of parameter 14.</typeparam>

        /// <param name="action">The action to compile into a kernel.</param>
        /// <param name="extent">The launch extent.</param>
        /// <param name="arg1">Argument 1 of the underlying kernel.</param>
        /// <param name="arg2">Argument 2 of the underlying kernel.</param>
        /// <param name="arg3">Argument 3 of the underlying kernel.</param>
        /// <param name="arg4">Argument 4 of the underlying kernel.</param>
        /// <param name="arg5">Argument 5 of the underlying kernel.</param>
        /// <param name="arg6">Argument 6 of the underlying kernel.</param>
        /// <param name="arg7">Argument 7 of the underlying kernel.</param>
        /// <param name="arg8">Argument 8 of the underlying kernel.</param>
        /// <param name="arg9">Argument 9 of the underlying kernel.</param>
        /// <param name="arg10">Argument 10 of the underlying kernel.</param>
        /// <param name="arg11">Argument 11 of the underlying kernel.</param>
        /// <param name="arg12">Argument 12 of the underlying kernel.</param>
        /// <param name="arg13">Argument 13 of the underlying kernel.</param>
        /// <param name="arg14">Argument 14 of the underlying kernel.</param>

        /// <remarks>
        /// Uses the internal launcher cache that explicitly caches all launched kernels
        /// using strong references. To avoid caching use the context caching mode
        /// <see cref="CachingMode.NoKernelCaching" />.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LaunchAutoGrouped<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(
            Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14> action,
            TIndex extent,
            T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10, T11 arg11, T12 arg12, T13 arg13, T14 arg14)
            where TIndex : struct, IGenericIndex<TIndex>
            where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct =>
            LaunchAutoGrouped(
                action,
                DefaultStream,
                extent,
                arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, arg9, arg10, arg11, arg12, arg13, arg14);

    }
}

#pragma warning restore IDE0046 // Convert to conditional expression