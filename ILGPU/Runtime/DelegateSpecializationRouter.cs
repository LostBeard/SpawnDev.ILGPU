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
    // TRIMMING: the TryRoute lambdas recover the wrapped delegate with
    // typeof(Tn).GetField("_delegate", NonPublic). Annotating those generic
    // parameters would push a DynamicallyAccessedMembers requirement out through
    // every KernelLoaders.Load*Kernel overload and onto consumer kernel signatures,
    // so the field is rooted here instead. It is in fact already preserved by the
    // direct read in DelegateSpecializationHelper (spec._delegate.Method); the
    // DynamicDependency keeps that guarantee from depending on an unrelated file.
    [UnconditionalSuppressMessage("Trimming", "IL2090",
        Justification = TrimmingAnnotations.SpecializedDelegate)]
    internal static class DelegateSpecializationRouter
    {
        // Cache: (accelerator, originalMethod, targetMethod) → kernel
        private static readonly ConcurrentDictionary<
            (int accelId, MethodInfo original, MethodInfo target),
            Kernel> _cache = new();

        [DynamicDependency(
            TrimmingAnnotations.SpecializedDelegateFields,
            typeof(DelegateSpecialization<>))]
        private static Kernel GetOrCreateKernel(
            Accelerator accelerator,
            MethodInfo originalMethod,
            MethodInfo targetMethod)
        {
            var key = (accelerator.GetHashCode(), originalMethod, targetMethod);
            return _cache.GetOrAdd(key, _ =>
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

                // Compile through the normal ILGPU pipeline
                return accelerator.LoadAutoGroupedKernel(
                    syntheticMethod);
            });
        }

        /// <summary>
        /// Checks if the last type parameter is a DelegateSpecialization
        /// and routes accordingly. Returns false if no delegation needed.
        /// </summary>
        /// <remarks>
        /// This is the 2-param overload (TIndex + T1).
        /// T1 could be a DelegateSpecialization.
        /// </remarks>
        public static bool TryRoute<TIndex, T1>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1>? result)
            where TIndex : struct, IIndex
            where T1 : struct
        {
            result = null;
            if (!typeof(T1).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (TIndex index, T1 delegateSpec) =>
            {
                // Extract the delegate from the DelegateSpecialization<T>
                // struct via reflection (T1 is DelegateSpecialization<TDel>)
                var delegateField = typeof(T1).GetField("_delegate",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var del = (Delegate)delegateField.GetValue(delegateSpec)!;
                var target = del.Method;

                var kernel = GetOrCreateKernel(
                    accelerator, originalMethod, target);
                // Synthetic kernel has delegate param removed — just index
                kernel.Launch(accelerator.DefaultStream, index);
            };
            return true;
        }

        /// <summary>
        /// 3-param overload (TIndex + T1 + T2).
        /// T2 could be a DelegateSpecialization.
        /// </summary>
        public static bool TryRoute<TIndex, T1, T2>(
            Accelerator accelerator,
            Delegate action,
            out Action<TIndex, T1, T2>? result)
            where TIndex : struct, IIndex
            where T1 : struct
            where T2 : struct
        {
            result = null;
            if (!typeof(T2).IsDelegateSpecializedType())
                return false;

            var originalMethod = action.Method;
            result = (TIndex index, T1 param1, T2 delegateSpec) =>
            {
                var delegateField = typeof(T2).GetField("_delegate",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var del = (Delegate)delegateField.GetValue(delegateSpec)!;
                var target = del.Method;

                var kernel = GetOrCreateKernel(
                    accelerator, originalMethod, target);
                kernel.Launch(accelerator.DefaultStream, index, param1);
            };
            return true;
        }

        // For overloads with 3+ explicit params, add TryRoute methods
        // that check the last type param. For brevity, only the first
        // few are explicitly typed; the rest return false.
        public static bool TryRoute<TIndex, T1, T2, T3>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct { r = null; if (!typeof(T3).IsDelegateSpecializedType()) return false; var m = d.Method; r = (i, p1, p2, ds) => { var df = typeof(T3).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic)!; var dl = (Delegate)df.GetValue(ds)!; GetOrCreateKernel(a, m, dl.Method).Launch(a.DefaultStream, i, p1, p2); }; return true; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct { r = null; if (!typeof(T4).IsDelegateSpecializedType()) return false; var m = d.Method; r = (i, p1, p2, p3, ds) => { var df = typeof(T4).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic)!; var dl = (Delegate)df.GetValue(ds)!; GetOrCreateKernel(a, m, dl.Method).Launch(a.DefaultStream, i, p1, p2, p3); }; return true; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct { r = null; if (!typeof(T5).IsDelegateSpecializedType()) return false; var m = d.Method; r = (i, p1, p2, p3, p4, ds) => { var df = typeof(T5).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic)!; var dl = (Delegate)df.GetValue(ds)!; GetOrCreateKernel(a, m, dl.Method).Launch(a.DefaultStream, i, p1, p2, p3, p4); }; return true; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct { r = null; if (!typeof(T6).IsDelegateSpecializedType()) return false; var m = d.Method; r = (i, p1, p2, p3, p4, p5, ds) => { var df = typeof(T6).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic)!; var dl = (Delegate)df.GetValue(ds)!; GetOrCreateKernel(a, m, dl.Method).Launch(a.DefaultStream, i, p1, p2, p3, p4, p5); }; return true; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct { r = null; if (!typeof(T7).IsDelegateSpecializedType()) return false; var m = d.Method; r = (i, p1, p2, p3, p4, p5, p6, ds) => { var df = typeof(T7).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic)!; var dl = (Delegate)df.GetValue(ds)!; GetOrCreateKernel(a, m, dl.Method).Launch(a.DefaultStream, i, p1, p2, p3, p4, p5, p6); }; return true; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct { r = null; if (!typeof(T8).IsDelegateSpecializedType()) return false; var m = d.Method; r = (i, p1, p2, p3, p4, p5, p6, p7, ds) => { var df = typeof(T8).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic)!; var dl = (Delegate)df.GetValue(ds)!; GetOrCreateKernel(a, m, dl.Method).Launch(a.DefaultStream, i, p1, p2, p3, p4, p5, p6, p7); }; return true; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct { r = null; if (!typeof(T9).IsDelegateSpecializedType()) return false; var m = d.Method; r = (i, p1, p2, p3, p4, p5, p6, p7, p8, ds) => { var df = typeof(T9).GetField("_delegate", BindingFlags.Instance | BindingFlags.NonPublic)!; var dl = (Delegate)df.GetValue(ds)!; GetOrCreateKernel(a, m, dl.Method).Launch(a.DefaultStream, i, p1, p2, p3, p4, p5, p6, p7, p8); }; return true; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct { r = null; return false; }
        public static bool TryRoute<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>(Accelerator a, Delegate d, out Action<TIndex, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12, T13, T14>? r) where TIndex : struct, IIndex where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct where T7 : struct where T8 : struct where T9 : struct where T10 : struct where T11 : struct where T12 : struct where T13 : struct where T14 : struct { r = null; return false; }
    }
}
