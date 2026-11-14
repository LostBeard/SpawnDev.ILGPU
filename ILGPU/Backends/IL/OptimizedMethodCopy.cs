// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                           Copyright (c) 2026 ILGPU Project
//                                    www.ilgpu.net
//
// File: OptimizedMethodCopy.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace ILGPU.Backends.IL
{
    /// <summary>
    /// Copies the IL of a static kernel method into a <see cref="DynamicMethod"/>, which the JIT compiles fully
    /// optimized the first time (dynamic methods do not take part in tiered compilation).
    /// </summary>
    /// <remarks>
    /// WHY. The IL backend runs a kernel by calling its original managed method, which the runtime compiles at
    /// tier 0 (unoptimized, with instrumentation) and replaces only after about 30 calls, or by on-stack
    /// replacement inside a loop, which does not always happen: a one-thread kernel whose loop body has 960
    /// branchy steps stayed at "Instrumented Tier0" for its whole life (JIT summary), 13 ns per step against
    /// 2.2 with tiering off (BackendTestBase.BranchCost_ByFunctionSize_Report, Velocity's host path, 2026-11-13).
    /// Velocity runs small and branchy launches through this backend (VelocityAccelerator.HostLaunchMaxThreads),
    /// so a kernel launched a few times ran unoptimized code every time.
    ///
    /// HOW. The method body's IL is copied with every metadata token re-issued through
    /// <see cref="DynamicILInfo"/> (methods, fields, types, strings, ldtoken), resolved in the generic context of
    /// the method, and its locals re-declared. Only what is copied exactly is copied: an instance method, a method
    /// with exception handling clauses, a calli or an unresolvable token keeps the original method (null).
    /// Only the IL backend's Velocity host accelerator uses this; the CPU accelerator keeps calling the original
    /// method, so a debugger still stops in the kernel's source.
    /// </remarks>
    internal static class OptimizedMethodCopy
    {
        private static readonly ConcurrentDictionary<MethodInfo, MethodInfo> Copies = new();

        private static readonly OpCode[] OneByteOpCodes = new OpCode[0x100];
        private static readonly OpCode[] TwoByteOpCodes = new OpCode[0x100];

        static OptimizedMethodCopy()
        {
            foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not OpCode code)
                    continue;
                ushort value = (ushort)code.Value;
                if (code.Size == 1)
                    OneByteOpCodes[value] = code;
                else
                    TwoByteOpCodes[value & 0xFF] = code;
            }
        }

        /// <summary>
        /// Diagnostic: kernels copied (and kept the original method) since process start.
        /// </summary>
        public static int CopiedCount;

        /// <summary>
        /// Returns the optimized copy of <paramref name="method"/>, or the method itself when it cannot be copied.
        /// </summary>
        [RequiresDynamicCode("Builds a dynamic method from the kernel's IL.")]
        [RequiresUnreferencedCode("Reads the kernel's IL and resolves its metadata tokens by reflection.")]
        public static MethodInfo GetOrCreate(MethodInfo method) =>
            Copies.GetOrAdd(method, m =>
            {
                try
                {
                    var copy = TryCreate(m);
                    if (copy is null)
                        return m;
                    System.Threading.Interlocked.Increment(ref CopiedCount);
                    return copy;
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException
                    or NotSupportedException or BadImageFormatException or MemberAccessException)
                {
                    Trace.WriteLine($"ILGPU: kernel '{m.Name}' runs its original method: {e.Message}");
                    return m;
                }
            });

        [RequiresDynamicCode("Builds a dynamic method from the kernel's IL.")]
        [RequiresUnreferencedCode("Reads the kernel's IL and resolves its metadata tokens by reflection.")]
        private static DynamicMethod? TryCreate(MethodInfo method)
        {
            if (!method.IsStatic || method.ContainsGenericParameters)
                return null;
            var body = method.GetMethodBody();
            var declaringType = method.DeclaringType;
            if (body is null || declaringType is null || body.ExceptionHandlingClauses.Count > 0)
                return null;
            var il = body.GetILAsByteArray();
            if (il is null)
                return null;

            var typeArgs = declaringType.IsGenericType ? declaringType.GetGenericArguments() : null;
            var methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

            var parameters = method.GetParameters();
            var parameterTypes = new Type[parameters.Length];
            for (int i = 0; i < parameters.Length; ++i)
                parameterTypes[i] = parameters[i].ParameterType;

            var copy = new DynamicMethod(
                method.Name + "_ILGPUOptimized",
                method.ReturnType,
                parameterTypes,
                method.Module,
                skipVisibility: true);
            copy.InitLocals = body.InitLocals;
            var info = copy.GetDynamicILInfo();

            // Locals, in the method's generic context
            var locals = SignatureHelper.GetLocalVarSigHelper();
            foreach (var local in body.LocalVariables)
            {
                var type = Substitute(local.LocalType, typeArgs, methodArgs);
                if (type is null)
                    return null;
                locals.AddArgument(type, local.IsPinned);
            }
            info.SetLocalSignature(locals.GetSignature());

            // Code, with every token re-issued for the dynamic method
            var module = method.Module;
            var code = (byte[])il.Clone();
            for (int pos = 0; pos < code.Length;)
            {
                OpCode op;
                if (code[pos] == 0xFE)
                {
                    if (pos + 1 >= code.Length)
                        return null;
                    op = TwoByteOpCodes[code[pos + 1]];
                    pos += 2;
                }
                else
                {
                    op = OneByteOpCodes[code[pos]];
                    pos += 1;
                }
                if (op.Size == 0)
                    return null;   // not an opcode this table knows

                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        pos += 1;
                        break;
                    case OperandType.InlineVar:
                        pos += 2;
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineI:
                    case OperandType.ShortInlineR:
                        pos += 4;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        pos += 8;
                        break;
                    case OperandType.InlineSwitch:
                        pos += 4 + 4 * BitConverter.ToInt32(code, pos);
                        break;
                    case OperandType.InlineSig:
                        return null;   // calli: its signature names types by this module's tokens
                    case OperandType.InlineString:
                    {
                        int token = BitConverter.ToInt32(code, pos);
                        Write(code, pos, info.GetTokenFor(module.ResolveString(token)));
                        pos += 4;
                        break;
                    }
                    case OperandType.InlineMethod:
                    case OperandType.InlineField:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                    {
                        int token = BitConverter.ToInt32(code, pos);
                        var member = module.ResolveMember(token, typeArgs, methodArgs);
                        int newToken = member switch
                        {
                            Type type => info.GetTokenFor(type.TypeHandle),
                            FieldInfo field => field.DeclaringType is { IsGenericType: true } fieldOwner
                                ? info.GetTokenFor(field.FieldHandle, fieldOwner.TypeHandle)
                                : info.GetTokenFor(field.FieldHandle),
                            MethodBase target => target.DeclaringType is { IsGenericType: true } methodOwner
                                ? info.GetTokenFor(target.MethodHandle, methodOwner.TypeHandle)
                                : info.GetTokenFor(target.MethodHandle),
                            _ => 0,
                        };
                        if (newToken == 0)
                            return null;
                        Write(code, pos, newToken);
                        pos += 4;
                        break;
                    }
                    default:
                        return null;
                }
            }
            info.SetCode(code, body.MaxStackSize);
            return copy;
        }

        private static void Write(byte[] code, int pos, int value)
        {
            code[pos] = (byte)value;
            code[pos + 1] = (byte)(value >> 8);
            code[pos + 2] = (byte)(value >> 16);
            code[pos + 3] = (byte)(value >> 24);
        }

        /// <summary>
        /// Replaces the generic parameters in <paramref name="type"/> with the method's arguments; null when one
        /// cannot be replaced.
        /// </summary>
        [RequiresDynamicCode("Builds array and generic types of the kernel's locals.")]
        [RequiresUnreferencedCode("Instantiates generic types of the kernel's locals.")]
        private static Type? Substitute(Type type, Type[]? typeArgs, Type[]? methodArgs)
        {
            if (!type.ContainsGenericParameters)
                return type;
            if (type.IsGenericParameter)
            {
                var args = type.DeclaringMethod is null ? typeArgs : methodArgs;
                int index = type.GenericParameterPosition;
                return args is not null && index < args.Length ? args[index] : null;
            }
            if (type.IsByRef || type.IsPointer || type.IsArray)
            {
                var element = Substitute(type.GetElementType()!, typeArgs, methodArgs);
                if (element is null)
                    return null;
                if (type.IsByRef)
                    return element.MakeByRefType();
                if (type.IsPointer)
                    return element.MakePointerType();
                return type.IsSZArray ? element.MakeArrayType() : element.MakeArrayType(type.GetArrayRank());
            }
            if (type.IsGenericType)
            {
                var arguments = type.GetGenericArguments();
                var substituted = new List<Type>(arguments.Length);
                foreach (var argument in arguments)
                {
                    var s = Substitute(argument, typeArgs, methodArgs);
                    if (s is null)
                        return null;
                    substituted.Add(s);
                }
                return type.GetGenericTypeDefinition().MakeGenericType(substituted.ToArray());
            }
            return null;
        }
    }
}
