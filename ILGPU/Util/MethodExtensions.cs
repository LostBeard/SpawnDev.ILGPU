// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2017-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: MethodExtensions.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using ILGPU.Util;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU.Util
{
    /// <summary>
    /// Extensions for methods.
    /// </summary>
    static class MethodExtensions
    {
        /// <summary>
        /// Returns a parameter offset of 1 for instance methods and 0 for static
        /// methods. Capturing lambdas return 0 because arg 0 (this) is handled
        /// as a captures struct value, not skipped.
        /// </summary>
        /// <param name="method">The method to compute the parameter offset for.</param>
        /// <returns>
        /// A parameter offset of 1 for instance methods and 0 for static methods.
        /// </returns>
        public static int GetParameterOffset(this MethodBase method) =>
            method.IsStatic || method.IsNotCapturingLambda() ? 0 : 1;

        /// <summary>
        /// Returns true if the method can be considered a non-capturing lambda.
        /// </summary>
        /// <param name="method">The method to check.</param>
        /// <returns>True, if the method is a non-capturing lambda.</returns>
        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = TrimmingAnnotations.DisplayClass)]
        public static bool IsNotCapturingLambda(this MethodBase method)
        {
            if (method.IsStatic)
                return false;

            // C# and F# both create a helper class to represent a lambda - the fields of
            // the class are used to capture the variables. To detect a non-capturing
            // lambda, we look for a class that has no instance fields or properties (so
            // that it cannot have local state).
            //
            // IMPORTANT: We currently do not check for the existance of the
            // CompilerGenerated attribute because F# uses a different attribute
            // i.e. [CompilationMapping(SourceConstructFlags.Closure)], which only exists
            // in F#. As a side-effect, the detection therefore also allows instance
            // methods on any class without instance fields or properties to be
            // considered as a non-capturing lambda.
            //
            // IMPORTANT: It is possible for the lambda to capture static fields and
            // properties, and still pass this detection because we do not inspect the
            // IL instructions here. We are relying on the rest of the compilation to
            // detect invalid cases.
            //
            // NB: In future, this will only apply to C#, as F# has been updated to create
            // a static function for non-capturing lambdas:
            // https://github.com/dotnet/fsharp/tree/596f3d7
            //
            var declaringType = method.DeclaringType;
            return declaringType != null
                && declaringType.IsClass
                && declaringType.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.Public).Length == 0
                && declaringType.GetProperties(
                    BindingFlags.Instance |
                    BindingFlags.NonPublic |
                    BindingFlags.Public).Length == 0;
        }

        /// <summary>
        /// Returns true if the method is an instance method on a compiler-generated
        /// display class with captured fields (a capturing lambda).
        /// </summary>
        /// <param name="method">The method to check.</param>
        /// <returns>True, if the method is a capturing lambda.</returns>
        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = TrimmingAnnotations.DisplayClass)]
        public static bool IsCapturingLambda(this MethodBase method)
        {
            if (method.IsStatic)
                return false;

            var declaringType = method.DeclaringType;
            if (declaringType == null || !declaringType.IsClass)
                return false;

            // Check for CompilerGenerated attribute (C# display classes)
            if (!declaringType.IsDefined(typeof(CompilerGeneratedAttribute), false))
                return false;

            // Must have instance fields (the captured variables)
            var fields = declaringType.GetFields(
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public);
            return fields.Length > 0;
        }

        /// <summary>
        /// Returns the captured fields of a capturing lambda's display class,
        /// sorted by metadata token for deterministic ordering.
        /// </summary>
        /// <param name="method">The capturing lambda method.</param>
        /// <returns>The captured fields sorted by metadata token.</returns>
        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = TrimmingAnnotations.DisplayClass)]
        public static FieldInfo[] GetCapturedFields(this MethodBase method)
        {
            var fields = method.DeclaringType!.GetFields(
                BindingFlags.Instance |
                BindingFlags.NonPublic |
                BindingFlags.Public);
            Array.Sort(fields, (a, b) =>
                a.MetadataToken.CompareTo(b.MetadataToken));
            return fields;
        }
    }
}
