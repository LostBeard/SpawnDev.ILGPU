// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2018-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: DefaultILBackend.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.Backends.EntryPoints;
using ILGPU.Runtime.CPU;
using ILGPU.Util;
using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU.Backends.IL
{
    /// <summary>
    /// The default IL backend that uses the original kernel method.
    /// </summary>
    public class DefaultILBackend : ILBackend
    {
        #region Instance

        /// <summary>
        /// Constructs a new IL backend.
        /// </summary>
        /// <param name="context">The context to use.</param>
        protected internal DefaultILBackend(Context context)
            : base(context, new CPUCapabilityContext(), 1, new ILArgumentMapper(context))
        { }

        #endregion

        #region Methods

        /// <summary>
        /// Generates the actual kernel invocation call.
        /// </summary>
        [UnconditionalSuppressMessage("Trimming", "IL2075",
            Justification = TrimmingAnnotations.EmittedType)]
        protected override void GenerateCode<TEmitter>(
            EntryPoint entryPoint,
            in BackendContext backendContext,
            TEmitter emitter,
            in ILLocal task,
            in ILLocal index,
            ImmutableArray<ILLocal> locals)
        {
            bool isCapturing = entryPoint.MethodInfo.IsCapturingLambda();

            if (isCapturing)
            {
                // For capturing lambdas, create a display class instance
                // and populate its fields from the captures struct (last local).
                var displayClassType = entryPoint.MethodInfo.DeclaringType!;
                var ctor = displayClassType.GetConstructor(
                    BindingFlags.Instance | BindingFlags.Public
                        | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null)!;
                emitter.EmitNewObject(ctor);

                // Copy fields from the captures struct to the display class.
                // The captures struct is the last local (it's the last
                // parameter in the EntryPoint).
                var capturedFields = entryPoint.MethodInfo.GetCapturedFields();
                var capturesLocal = locals[^1];
                var mappedCaptureType = capturesLocal.VariableType;
                var mappedFields = mappedCaptureType.GetFields(
                    BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < capturedFields.Length
                    && i < mappedFields.Length; i++)
                {
                    emitter.Emit(OpCodes.Dup);
                    emitter.Emit(LocalOperation.Load, capturesLocal);
                    emitter.Emit(OpCodes.Ldfld, mappedFields[i]);
                    emitter.Emit(OpCodes.Stfld, capturedFields[i]);
                }
                // Display class instance is now on the stack as 'this'
            }
            else if (entryPoint.MethodInfo.IsNotCapturingLambda())
            {
                // Load placeholder 'this' argument
                emitter.Emit(OpCodes.Ldnull);
            }

            if (entryPoint.IsImplicitlyGrouped)
            {
                // Load index
                emitter.Emit(LocalOperation.Load, index);
            }

            // Load kernel arguments (skip captures struct for capturing
            // lambdas — it was already consumed above)
            int localCount = isCapturing ? locals.Length - 1 : locals.Length;
            for (int i = 0; i < localCount; i++)
                emitter.Emit(LocalOperation.Load, locals[i]);

            // Invoke kernel
            emitter.EmitCall(entryPoint.MethodInfo);
        }

        #endregion
    }
}
