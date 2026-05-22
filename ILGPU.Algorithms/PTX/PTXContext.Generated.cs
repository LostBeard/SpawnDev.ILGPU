// ---------------------------------------------------------------------------------------
//                                   ILGPU Algorithms
//                        Copyright (c) 2019-2021 ILGPU Project
//                                    www.ilgpu.net
//
// File: PTXContext.Generated.tt/PTXContext.Generated.cs
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

using ILGPU.IR.Intrinsics;
using ILGPU.IR.Values;
using ILGPU.Runtime.Cuda;

namespace ILGPU.Algorithms.PTX
{
    partial class PTXContext
    {
        public static void EnablePTXAlgorithms(IntrinsicImplementationManager manager)
        {
            // Register math intrinsics
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.FloorF,
                BasicValueType.Float32,
                GetMathIntrinsic("Floor", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.FloorF,
                BasicValueType.Float64,
                GetMathIntrinsic("Floor", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CeilingF,
                BasicValueType.Float32,
                GetMathIntrinsic("Ceiling", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CeilingF,
                BasicValueType.Float64,
                GetMathIntrinsic("Ceiling", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.LogF,
                BasicValueType.Float32,
                GetMathIntrinsic("Log", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.LogF,
                BasicValueType.Float64,
                GetMathIntrinsic("Log", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log10F,
                BasicValueType.Float32,
                GetMathIntrinsic("Log10", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log10F,
                BasicValueType.Float64,
                GetMathIntrinsic("Log10", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log2F,
                BasicValueType.Float64,
                GetMathIntrinsic("Log2", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.ExpF,
                BasicValueType.Float32,
                GetMathIntrinsic("Exp", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.ExpF,
                BasicValueType.Float64,
                GetMathIntrinsic("Exp", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Exp2F,
                BasicValueType.Float64,
                GetMathIntrinsic("Exp2", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.RsqrtF,
                BasicValueType.Float32,
                GetMathIntrinsic("Rsqrt", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.RsqrtF,
                BasicValueType.Float64,
                GetMathIntrinsic("Rsqrt", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinF,
                BasicValueType.Float64,
                GetMathIntrinsic("Sin", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AsinF,
                BasicValueType.Float32,
                GetMathIntrinsic("Asin", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AsinF,
                BasicValueType.Float64,
                GetMathIntrinsic("Asin", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinhF,
                BasicValueType.Float32,
                GetMathIntrinsic("Sinh", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinhF,
                BasicValueType.Float64,
                GetMathIntrinsic("Sinh", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CosF,
                BasicValueType.Float64,
                GetMathIntrinsic("Cos", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AcosF,
                BasicValueType.Float32,
                GetMathIntrinsic("Acos", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AcosF,
                BasicValueType.Float64,
                GetMathIntrinsic("Acos", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CoshF,
                BasicValueType.Float32,
                GetMathIntrinsic("Cosh", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CoshF,
                BasicValueType.Float64,
                GetMathIntrinsic("Cosh", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanF,
                BasicValueType.Float32,
                GetMathIntrinsic("Tan", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanF,
                BasicValueType.Float64,
                GetMathIntrinsic("Tan", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AtanF,
                BasicValueType.Float32,
                GetMathIntrinsic("Atan", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.AtanF,
                BasicValueType.Float64,
                GetMathIntrinsic("Atan", typeof(System.Double)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanhF,
                BasicValueType.Float64,
                GetMathIntrinsic("Tanh", typeof(System.Double)));

            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Rem,
                BasicValueType.Float32,
                GetMathIntrinsic(
                    "Rem",
                    typeof(System.Single),
                    typeof(System.Single)));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Rem,
                BasicValueType.Float64,
                GetMathIntrinsic(
                    "Rem",
                    typeof(System.Double),
                    typeof(System.Double)));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.PowF,
                BasicValueType.Float32,
                GetMathIntrinsic(
                    "Pow",
                    typeof(System.Single),
                    typeof(System.Single)));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.PowF,
                BasicValueType.Float64,
                GetMathIntrinsic(
                    "Pow",
                    typeof(System.Double),
                    typeof(System.Double)));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Atan2F,
                BasicValueType.Float32,
                GetMathIntrinsic(
                    "Atan2",
                    typeof(System.Single),
                    typeof(System.Single)));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.Atan2F,
                BasicValueType.Float64,
                GetMathIntrinsic(
                    "Atan2",
                    typeof(System.Double),
                    typeof(System.Double)));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.BinaryLogF,
                BasicValueType.Float32,
                GetMathIntrinsic(
                    "Log",
                    typeof(System.Single),
                    typeof(System.Single)));
            manager.RegisterBinaryArithmetic(
                BinaryArithmeticKind.BinaryLogF,
                BasicValueType.Float64,
                GetMathIntrinsic(
                    "Log",
                    typeof(System.Double),
                    typeof(System.Double)));

            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.IsInfF,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.IsInfF,
                BasicValueType.Float64,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.IsNaNF,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.IsNaNF,
                BasicValueType.Float64,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.RcpF,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.RcpF,
                BasicValueType.Float64,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SqrtF,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SqrtF,
                BasicValueType.Float64,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.SinF,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.CosF,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Exp2F,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.Log2F,
                BasicValueType.Float32,
                MathCodeGeneratorIntrinsic);
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanhF,
                BasicValueType.Float32,
                GetMathIntrinsic("Tanh", typeof(System.Single)));
            manager.RegisterUnaryArithmetic(
                UnaryArithmeticKind.TanhF,
                BasicValueType.Float32,
                GetMathCodeGeneratorIntrinsic(CudaArchitecture.SM_75));

            RegisterXMathRedirect(
                manager,
                PTXMathType,
                "RoundAwayFromZero",
                "RoundAwayFromZero",
                typeof(float));
            RegisterXMathRedirect(
                manager,
                PTXMathType,
                "RoundAwayFromZero",
                "RoundAwayFromZero",
                typeof(double));
            RegisterXMathRedirect(
                manager,
                PTXMathType,
                "RoundToEven",
                "RoundToEven",
                typeof(float));
            RegisterXMathRedirect(
                manager,
                PTXMathType,
                "RoundToEven",
                "RoundToEven",
                typeof(double));
            RegisterXMathRedirect(
                manager,
                PTXMathType,
                "IEEERemainder",
                "IEEERemainder",
                typeof(float),
                typeof(float));
            RegisterXMathRedirect(
                manager,
                PTXMathType,
                "IEEERemainder",
                "IEEERemainder",
                typeof(double),
                typeof(double));

            // Register group intrinsics
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "Reduce");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "AllReduce");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "ExclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "InclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "ExclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "InclusiveScanWithBoundaries");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "ExclusiveScanNextIteration");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.GroupExtensionsType,
                PTXGroupExtensionsType,
                "InclusiveScanNextIteration");

            // Register warp intrinsics
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.WarpExtensionsType,
                PTXWarpExtensionsType,
                "Reduce");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.WarpExtensionsType,
                PTXWarpExtensionsType,
                "AllReduce");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.WarpExtensionsType,
                PTXWarpExtensionsType,
                "ExclusiveScan");
            RegisterIntrinsicMapping(
                manager,
                AlgorithmContext.WarpExtensionsType,
                PTXWarpExtensionsType,
                "InclusiveScan");

            // Register PTX-only explicit vector memory intrinsics.
            RegisterMemoryIntrinsics(manager);
        }
    }
}