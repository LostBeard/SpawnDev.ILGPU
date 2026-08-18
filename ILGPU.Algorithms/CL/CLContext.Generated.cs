// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2024 ILGPU Project
//                                    www.ilgpu.net
//
// File: CLContext.Generated.tt/CLContext.Generated.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2020-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: TypeInformation.ttinclude
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

using ILGPU.Backends.OpenCL;
using ILGPU.IR;
using ILGPU.IR.Intrinsics;
using ILGPU.IR.Values;
using ILGPU.Util;
using System.Diagnostics.CodeAnalysis;

namespace ILGPU.Algorithms.CL
{
    partial class CLContext
    {
        [UnconditionalSuppressMessage("Trimming", "IL2111",
            Justification = TrimmingAnnotations.SelfRegistrar)]
        public static void EnableCLAlgorithms(IntrinsicImplementationManager manager)
        {
            // Register math intrinsics
            RegisterXMathCodeGenerator(
                manager,
                typeof(CLContext),
                "RoundAwayFromZero",
                "GenerateRoundAwayFromZero",
                typeof(float));
            RegisterXMathCodeGenerator(
                manager,
                typeof(CLContext),
                "RoundAwayFromZero",
                "GenerateRoundAwayFromZero",
                typeof(double));
            RegisterXMathCodeGenerator(
                manager,
                typeof(CLContext),
                "RoundToEven",
                "GenerateRoundToEven",
                typeof(float));
            RegisterXMathCodeGenerator(
                manager,
                typeof(CLContext),
                "RoundToEven",
                "GenerateRoundToEven",
                typeof(double));
            RegisterXMathCodeGenerator(
                manager,
                typeof(CLContext),
                "IEEERemainder",
                "GenerateIEEERemainder",
                typeof(float),
                typeof(float));
            RegisterXMathCodeGenerator(
                manager,
                typeof(CLContext),
                "IEEERemainder",
                "GenerateIEEERemainder",
                typeof(double),
                typeof(double));

            // Register group intrinsics
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "Reduce",
                "GenerateReduce");
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "AllReduce",
                "GenerateAllReduce");
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "ExclusiveScan",
                "GenerateExclusiveScan");
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "InclusiveScan",
                "GenerateInclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "ExclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "InclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "ExclusiveScanNextIteration");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                CLGroupExtensionsType,
                "InclusiveScanNextIteration");

            // Register warp intrinsics
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.WarpExtensionsType,
                CLWarpExtensionsType,
                "Reduce",
                "GenerateReduce");
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.WarpExtensionsType,
                CLWarpExtensionsType,
                "AllReduce",
                "GenerateAllReduce");
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.WarpExtensionsType,
                CLWarpExtensionsType,
                "ExclusiveScan",
                "GenerateExclusiveScan");
            RegisterIntrinsicCodeGenerator(
                manager,
                AlgorithmContext.WarpExtensionsType,
                CLWarpExtensionsType,
                "InclusiveScan",
                "GenerateInclusiveScan");
        }

        private static void GenerateMethodCall(
            string functionName,
            CLCodeGenerator codeGenerator,
            MethodCall methodCall)
        {
            var target = methodCall.Target;
            var returnType = target.ReturnType;

            CLCodeGenerator.StatementEmitter statementEmitter;
            if (!returnType.IsVoidType)
            {
                var returnValue = codeGenerator.Allocate(methodCall);
                statementEmitter = codeGenerator.BeginStatement(returnValue);
                statementEmitter.AppendCommand(functionName);
            }
            else
            {
                statementEmitter = codeGenerator.BeginStatement(functionName);
            }

            // Append arguments
            statementEmitter.BeginArguments();
            foreach (var argument in methodCall)
            {
                var variable = codeGenerator.Load(argument);
                statementEmitter.AppendArgument(variable);
            }
            statementEmitter.EndArguments();

            // End call
            statementEmitter.Finish();
        }

        /// <summary>
        /// Generates an intrinsic code generator for RoundAwayFromZero.
        /// </summary>
        /// <param name="backend">The current backend.</param>
        /// <param name="codeGenerator">The code generator.</param>
        /// <param name="value">The value to generate code for.</param>
        private static void GenerateRoundAwayFromZero(
            CLBackend backend,
            CLCodeGenerator codeGenerator,
            Value value)
        {
            GenerateMethodCall(
                "round",
                codeGenerator,
                value as MethodCall);
        }

        /// <summary>
        /// Generates an intrinsic code generator for RoundToEven.
        /// </summary>
        /// <param name="backend">The current backend.</param>
        /// <param name="codeGenerator">The code generator.</param>
        /// <param name="value">The value to generate code for.</param>
        private static void GenerateRoundToEven(
            CLBackend backend,
            CLCodeGenerator codeGenerator,
            Value value)
        {
            GenerateMethodCall(
                "rint",
                codeGenerator,
                value as MethodCall);
        }

        /// <summary>
        /// Generates an intrinsic code generator for IEEERemainder.
        /// </summary>
        /// <param name="backend">The current backend.</param>
        /// <param name="codeGenerator">The code generator.</param>
        /// <param name="value">The value to generate code for.</param>
        private static void GenerateIEEERemainder(
            CLBackend backend,
            CLCodeGenerator codeGenerator,
            Value value)
        {
            GenerateMethodCall(
                "remainder",
                codeGenerator,
                value as MethodCall);
        }

    }
}