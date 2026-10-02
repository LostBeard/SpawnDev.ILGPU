// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 SpawnDev
//
// File: DelegateSpecializationRouter.cs
//
// Runtime detection of DelegateSpecialization<T> parameters in kernel
// loading overloads. Routes to the specialization helper when detected.
// ---------------------------------------------------------------------------------------

using ILGPU.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU.Runtime
{
    /// <summary>
    /// Detects DelegateSpecialization&lt;T&gt; parameters at runtime and
    /// routes to the specialization dispatch path.
    /// </summary>
    /// <remarks>
    /// The specialization is the LAST kernel parameter. A launch resolves (accelerator, kernel method, target method)
    /// to a TYPED launcher for the synthetic kernel - <c>Action&lt;AcceleratorStream, TIndex, T1..&gt;</c> without the
    /// specialization parameter - and calls it directly.
    /// <para>MEASURED 2026-10-02 (Anaglyphohol DAv3, Blazor WASM AOT CPU profile): every launch used to look the
    /// wrapped delegate up with <c>Type.GetField("_delegate")</c> + <c>FieldInfo.GetValue</c>, allocate a closure for
    /// the kernel-cache lookup, and launch through <c>Kernel.Launch(params object[])</c> - boxing every argument and
    /// invoking the launcher by reflection (<c>DynamicMethod.Invoke</c>). That was ~60% of every broadcast
    /// elementwise op's host time (~0.6 ms a frame); the GPU work was the same.</para>
    /// </remarks>
    internal static class DelegateSpecializationRouter
    {
        // (accelerator, original kernel method, specialization target) -> typed launcher of the synthetic kernel
        private static readonly ConcurrentDictionary<
            (int accelId, MethodInfo original, MethodInfo target),
            Delegate> _launchers = new();

        /// <summary>The wrapped delegate of a DelegateSpecialization&lt;T&gt; argument, read without reflection.</summary>
        private static Delegate SpecializedDelegate<TSpec>(TSpec specialization) where TSpec : struct =>
            ((IDelegateSpecialization)specialization).Target
                ?? throw new ArgumentException(
                    "default(DelegateSpecialization<T>) carries no delegate - construct it with the target method.");

        /// <summary>The typed launcher of the kernel specialized for <paramref name="specialized"/>'s method.</summary>
        private static TLauncher GetOrCreateLauncher<TLauncher>(
            Accelerator accelerator,
            MethodInfo originalMethod,
            Delegate specialized)
            where TLauncher : Delegate
        {
            var key = (accelerator.GetHashCode(), originalMethod, specialized.Method);
            if (_launchers.TryGetValue(key, out var launcher))
                return (TLauncher)launcher;
            return (TLauncher)_launchers.GetOrAdd(
                key,
                static (k, acc) => CreateLauncher<TLauncher>(acc, k.original, k.target),
                accelerator);
        }

        [DynamicDependency(
            TrimmingAnnotations.SpecializedDelegateFields,
            typeof(DelegateSpecialization<>))]
        private static TLauncher CreateLauncher<TLauncher>(
            Accelerator accelerator,
            MethodInfo originalMethod,
            MethodInfo targetMethod)
            where TLauncher : Delegate
        {
            // Find DelegateSpecialization parameter indices
            var origParams = originalMethod.GetParameters();
            var targets = new Dictionary<int, MethodInfo>();
            for (int i = 0; i < origParams.Length; i++)
            {
                if (origParams[i].ParameterType
                    .IsDelegateSpecializedType())
                    targets[i] = targetMethod;
            }

            // Create synthetic method with delegate calls inlined
            var syntheticMethod =
                DelegateSpecializationRewriter.RewriteKernel(
                    originalMethod, targets);

            // Compile through the normal ILGPU pipeline; the launcher takes the stream first.
            return accelerator.LoadAutoGroupedKernel<TLauncher>(syntheticMethod);
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T1"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1>? result)
            where TIndex : struct, IIndex where T1 : struct
        {
            result = null;
            if (!typeof(T1).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T2"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct
        {
            result = null;
            if (!typeof(T2).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T3"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2, T3>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2, T3>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct
        {
            result = null;
            if (!typeof(T3).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, p2, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1, T2>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1, p2);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T4"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2, T3, T4>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2, T3, T4>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct
        {
            result = null;
            if (!typeof(T4).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, p2, p3, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1, T2, T3>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1, p2, p3);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T5"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2, T3, T4, T5>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
        {
            result = null;
            if (!typeof(T5).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, p2, p3, p4, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1, T2, T3, T4>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1, p2, p3, p4);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T6"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2, T3, T4, T5, T6>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
        {
            result = null;
            if (!typeof(T6).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, p2, p3, p4, p5, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1, p2, p3, p4, p5);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T7"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2, T3, T4, T5, T6, T7>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct
        {
            result = null;
            if (!typeof(T7).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, p2, p3, p4, p5, p6, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1, p2, p3, p4, p5, p6);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T8"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct
        {
            result = null;
            if (!typeof(T8).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, p2, p3, p4, p5, p6, p7, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1, p2, p3, p4, p5, p6, p7);
            return true;
        }

        /// <summary>
        /// Routes a kernel whose last parameter (<typeparamref name="T9"/>) is a DelegateSpecialization.
        /// Returns false when it is not.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>? result)
            where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct
        {
            result = null;
            if (!typeof(T9).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (index, p1, p2, p3, p4, p5, p6, p7, p8, spec) =>
                GetOrCreateLauncher<Action<AcceleratorStream, TIndex, T1, T2, T3, T4, T5, T6, T7, T8>>(
                    accelerator, originalMethod, SpecializedDelegate(spec))(accelerator.DefaultStream, index, p1, p2, p3, p4, p5, p6, p7, p8);
            return true;
        }

        // More than 9 parameters after the index: not routed (a DelegateSpecialization there is not supported).
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct { r = null; return false; }
    }
}
