// ---------------------------------------------------------------------------------------
//                                        ILGPU
//                        Copyright (c) 2019-2023 ILGPU Project
//                                    www.ilgpu.net
//
// File: CLTypeGenerator.cs
//
// This file is part of ILGPU and is distributed under the University of Illinois Open
// Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Types;
using ILGPU.Runtime.OpenCL;
using ILGPU.Util;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace ILGPU.Backends.OpenCL
{
    /// <summary>
    /// An abstract type generator that can emit type declarations and definitions.
    /// </summary>
    public interface ICLTypeGenerator
    {
        /// <summary>
        /// Generate all forward type declarations.
        /// </summary>
        /// <param name="builder">The target builder.</param>
        void GenerateTypeDeclarations(StringBuilder builder);

        /// <summary>
        /// Generate all type definitions.
        /// </summary>
        /// <param name="builder">The target builder.</param>
        void GenerateTypeDefinitions(StringBuilder builder);
    }

    /// <summary>
    /// Generates internal OpenCL type structures that are used inside kernels.
    /// </summary>
    public sealed class CLTypeGenerator : DisposeBase, ICLTypeGenerator
    {
        #region Constants

        /// <summary>
        /// The string format of a single structure-like type.
        /// </summary>
        private const string TypeNameFormat = "type_{0}";

        /// <summary>
        /// The string format of a single structure field.
        /// </summary>
        private const string FieldNameFormat = "field_{0}";

        #endregion

        #region Static

        /// <summary>
        /// Maps basic value types to OpenCL language types.
        /// </summary>
        private static readonly ImmutableArray<string> BasicTypeMapping =
            ImmutableArray.Create(
                string.Empty,
                "bool",
                "char",
                "short",
                "int",
                "long",
                "half",
                "float",
                "double",
                "float", // BFloat16 (ordinal 9) - emulated as float on OpenCL (no native cl bf16)
                "float", // Float8E4M3 (ordinal 10) - emulated as float on OpenCL
                "float", // Float8E5M2 (ordinal 11) - emulated as float on OpenCL
                "float"); // Float4E2M1 (ordinal 12) - emulated as float on OpenCL

        /// <summary>
        /// Maps arithmetic-basic value types to OpenCL language types.
        /// </summary>
        private static readonly ImmutableArray<string> ArtihmeticTypeMapping =
            ImmutableArray.Create(
                string.Empty,
                "bool",
                "char",
                "short",
                "int",
                "long",
                "half",
                "float",
                "double",
                "uchar",
                "ushort",
                "uint",
                "ulong",
                "float", // BFloat16 (ordinal 13) - emulated as float on OpenCL
                "float", // Float8E4M3 (ordinal 14) - emulated as float on OpenCL
                "float", // Float8E5M2 (ordinal 15) - emulated as float on OpenCL
                "float"); // Float4E2M1 (ordinal 16) - emulated as float on OpenCL

        /// <summary>
        /// Maps arithmetic-basic value types to atomic OpenCL language types.
        /// </summary>
        private static readonly ImmutableArray<string?> AtomicTypeMapping =
            ImmutableArray.Create(
                string.Empty,
                null,
                null,
                null,
                "atomic_int",
                "atomic_long",
                null,
                null,
                null,
                null,
                null,
                "atomic_uint",
                "atomic_ulong",
                null, // BFloat16 (ordinal 13) - no bf16 atomics
                null, // Float8E4M3 (ordinal 14) - no fp8 atomics
                null, // Float8E5M2 (ordinal 15) - no fp8 atomics
                null); // Float4E2M1 (ordinal 16) - no fp4 atomics

        /// <summary>
        /// Resolves the given basic-value type to an OpenCL type name.
        /// </summary>
        /// <param name="basicValueType">The basic-value type to resolve.</param>
        /// <returns>The resolved OpenCL type name.</returns>
        public string GetBasicValueType(BasicValueType basicValueType) =>
            basicValueType == BasicValueType.Float16 && !Capabilities.Float16Native
            ? "float" // Emulate: promote Half to float when cl_khr_fp16 unavailable
            : basicValueType == BasicValueType.Float64 && !Capabilities.Float64
                ? throw CLCapabilityContext.GetNotSupportedFloat64Exception()
                : BasicTypeMapping[(int)basicValueType];

        /// <summary>
        /// Resolves the given basic-value type to an OpenCL type name.
        /// </summary>
        /// <param name="basicValueType">The basic-value type to resolve.</param>
        /// <returns>The resolved OpenCL type name.</returns>
        public string GetBasicValueType(ArithmeticBasicValueType basicValueType) =>
            basicValueType == ArithmeticBasicValueType.Float16 && !Capabilities.Float16Native
            ? "float" // Emulate: promote Half to float when cl_khr_fp16 unavailable
            : basicValueType == ArithmeticBasicValueType.Float64 && !Capabilities.Float64
                ? throw CLCapabilityContext.GetNotSupportedFloat64Exception()
                : ArtihmeticTypeMapping[(int)basicValueType];

        /// <summary>
        /// Resolves the given basic-value type to an atomic OpenCL type name.
        /// </summary>
        /// <param name="basicValueType">The basic-value type to resolve.</param>
        /// <returns>The resolved atomic OpenCL type name.</returns>
        public string? GetAtomicType(ArithmeticBasicValueType basicValueType) =>
            basicValueType == ArithmeticBasicValueType.Float16 && !Capabilities.Float16Native
            ? null // No atomic support for emulated Half
            : basicValueType == ArithmeticBasicValueType.Float64 && !Capabilities.Float64
                ? throw CLCapabilityContext.GetNotSupportedFloat64Exception()
                : AtomicTypeMapping[(int)basicValueType];

        /// <summary>
        /// Resolves a unique type name for the given node.
        /// </summary>
        /// <param name="typeNode">The type node.</param>
        /// <returns>The unique type name.</returns>
        public static string GetTypeName(TypeNode typeNode) =>
            string.Format(TypeNameFormat, typeNode.Id.ToString());

        /// <summary>
        /// Resolves a unique field name for the field index.
        /// </summary>
        /// <param name="fieldIndex">The field index.</param>
        /// <returns>The unique field name.</returns>
        public static string GetFieldName(int fieldIndex) =>
            string.Format(FieldNameFormat, fieldIndex.ToString());

        #endregion

        #region Instance

        private readonly ReaderWriterLockSlim readerWriterLock =
            new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly Dictionary<TypeNode, string> mapping =
            new Dictionary<TypeNode, string>();

        /// <summary>
        /// Constructs a new type generator and defines all required types
        /// in OpenCL during construction.
        /// </summary>
        /// <param name="typeContext">The associated type context.</param>
        /// <param name="capabilities">The supported capabilities.</param>
        internal CLTypeGenerator(
            IRTypeContext typeContext,
            CLCapabilityContext capabilities)
        {
            TypeContext = typeContext;
            Capabilities = capabilities;

            // Declare primitive types
            mapping[typeContext.VoidType] = "void";
            mapping[typeContext.StringType] = "__constant char*";

            foreach (var basicValueType in IRTypeContext.BasicValueTypes)
            {
                if (basicValueType == BasicValueType.Float64
                    && TypeContext.MathMode == MathMode.Fast32BitOnly)
                {
                    continue;
                }
                else if (basicValueType == BasicValueType.Float16
                    && !capabilities.Float16)
                {
                    continue;
                }
                else if (basicValueType == BasicValueType.Float64
                    && !capabilities.Float64)
                {
                    continue;
                }

                var primitiveType = typeContext.GetPrimitiveType(basicValueType);
                mapping[primitiveType] = GetBasicValueType(basicValueType);
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the underlying type context.
        /// </summary>
        public IRTypeContext TypeContext { get; }

        /// <summary>
        /// Returns the supported capabilities.
        /// </summary>
        public CLCapabilityContext Capabilities { get; }

        /// <summary>
        /// Returns the associated OpenCL type name.
        /// </summary>
        /// <param name="typeNode">The internal IR type node.</param>
        /// <returns>The resolved OpenCL type name.</returns>
        public string this[TypeNode typeNode]
        {
            get
            {
                // Synchronize all accesses below using a read/write scope
                using var readWriteScope = readerWriterLock.EnterUpgradeableReadScope();

                if (mapping.TryGetValue(typeNode, out string? typeName))
                    return typeName;

                // Synchronize all accesses below using a write scope
                using var writeScope = readWriteScope.EnterWriteScope();

                return GetOrCreateType(typeNode);
            }
        }

        /// <summary>
        /// Returns the associated OpenCL type name.
        /// </summary>
        /// <param name="type">The managed type to use.</param>
        /// <returns>The resolved OpenCL type name.</returns>
        public string this[Type type] => this[TypeContext.CreateType(type)];

        #endregion

        #region Methods

        /// <summary>
        /// Gets or creates the given type in OpenCL.
        /// </summary>
        /// <param name="typeNode">The type to declare.</param>
        /// <returns>The declared type name.</returns>
        private string GetOrCreateType(TypeNode typeNode)
        {
            Debug.Assert(!(typeNode is ViewType), "Invalid view type");
            if (mapping.TryGetValue(typeNode, out string? clName))
                return clName;

            if (typeNode is PointerType pointerType)
            {
                // Make sure the element type is known
                GetOrCreateType(pointerType.ElementType);
                clName = GetTypeName(typeNode);
            }
            else if (typeNode is StructureType structureType)
            {
                // Make sure all field types are known
                foreach (var fieldType in structureType.Fields)
                    GetOrCreateType(fieldType);

                clName = CLInstructions.StructTypePrefix + " " + GetTypeName(typeNode);
            }
            else if (typeNode is PaddingType paddingType)
            {
                clName = GetOrCreateType(paddingType.PrimitiveType);
            }
            else if (typeNode is PrimitiveType primitiveType16
                && primitiveType16.BasicValueType == BasicValueType.Float16
                && !Capabilities.Float16Native)
            {
                // Emulate: treat Half as float when cl_khr_fp16 unavailable
                clName = "float";
            }
            else if (typeNode is PrimitiveType primitiveType64
                && primitiveType64.BasicValueType == BasicValueType.Float64
                && !Capabilities.Float64)
            {
                throw CLCapabilityContext.GetNotSupportedFloat64Exception();
            }
            else
            {
                // Must be a not supported view type
                throw new InvalidCodeGenerationException();
            }

            // Store generated type name
            mapping[typeNode] = clName;
            return clName;
        }

        /// <summary>
        /// Generate all forward type declarations.
        /// </summary>
        /// <param name="builder">The target builder.</param>
        public void GenerateTypeDeclarations(StringBuilder builder)
        {
            foreach (var entry in mapping)
            {
                switch (entry.Key)
                {
                    case PointerType pointerType:
                        builder.Append(
                            CLInstructions.TypeDefStatement);
                        builder.Append(' ');
                        builder.Append(
                            CLInstructions.GetAddressSpacePrefix(
                                pointerType.AddressSpace));
                        builder.Append(' ');
                        // bf16 is stored as raw 2-byte ushort (values compute as float, but the
                        // STORAGE pointer must be ushort* for correct 2-byte stride). The bf16
                        // load/store handlers convert via _bf16_bits_to_f32 / _f32_to_bf16_bits.
                        // FP8 (E4M3/E5M2) is the same idea with 1-byte uchar storage (helpers
                        // _e*m*_bits_to_f32 / _f32_to_e*m*_bits).
                        if (pointerType.ElementType is PrimitiveType bf16Elem
                            && bf16Elem.BasicValueType == BasicValueType.BFloat16)
                            builder.Append("ushort");
                        else if (pointerType.ElementType is PrimitiveType fp8Elem
                            && (fp8Elem.BasicValueType == BasicValueType.Float8E4M3
                                || fp8Elem.BasicValueType == BasicValueType.Float8E5M2))
                            builder.Append("uchar");
                        // FP4 (E2M1) is the same idea: 1-byte uchar storage (4-bit value in the
                        // low nibble), helpers _e2m1_bits_to_f32 / _f32_to_e2m1_bits.
                        else if (pointerType.ElementType is PrimitiveType fp4Elem
                            && fp4Elem.BasicValueType == BasicValueType.Float4E2M1)
                            builder.Append("uchar");
                        else
                            builder.Append(mapping[pointerType.ElementType]);
                        builder.Append(CLInstructions.DereferenceOperation);
                        builder.Append(' ');
                        builder.Append(entry.Value);
                        builder.AppendLine(";");
                        break;
                    case StructureType _:
                        builder.Append(entry.Value);
                        builder.AppendLine(";");
                        break;
                }
            }
            builder.AppendLine();
        }

        /// <summary>
        /// Generate all type definitions.
        /// </summary>
        /// <param name="builder">The target builder.</param>
        public void GenerateTypeDefinitions(StringBuilder builder)
        {
            foreach (var entry in mapping)
            {
                if (!(entry.Key is StructureType structureType))
                    continue;

                GenerateStructureDefinition(
                    structureType,
                    entry.Value,
                    builder);
            }

            builder.AppendLine();
        }

        /// <summary>
        /// Generates a new structure definition in OpenCL format.
        /// </summary>
        /// <param name="structureType">The structure type.</param>
        /// <param name="typeName">The type name.</param>
        /// <param name="builder">The target builder to write to.</param>
        public void GenerateStructureDefinition(
            StructureType structureType,
            string typeName,
            StringBuilder builder)
        {
            int paddingCounter = 0;

            builder.AppendLine(typeName);
            builder.AppendLine("{");
            foreach (var (access, _, padding) in structureType.Offsets)
            {
                // Append padding information
                if (padding > 0)
                {
                    builder.Append('\t');
                    builder.Append(GetBasicValueType(ArithmeticBasicValueType.Int8));
                    builder.Append(' ');
                    builder.Append("__padding");
                    builder.Append(++paddingCounter);
                    builder.Append('[');
                    builder.Append(padding);
                    builder.AppendLine("];");
                }

                builder.Append('\t');
                builder.Append(mapping[structureType[access]]);
                builder.Append(' ');
                builder.Append(GetFieldName(access.Index));
                builder.AppendLine(";");

            }
            builder.AppendLine("};");
        }

        #endregion

        #region IDisposable

        /// <summary cref="DisposeBase.Dispose(bool)"/>
        protected override void Dispose(bool disposing)
        {
            readerWriterLock.Dispose();
            base.Dispose(disposing);
        }

        #endregion
    }
}
