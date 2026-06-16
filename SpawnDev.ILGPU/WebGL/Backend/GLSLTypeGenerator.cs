// ---------------------------------------------------------------------------------------
//                                 SpawnDev.ILGPU.WebGL
//                        Copyright (c) 2024 SpawnDev Project
//
// File: GLSLTypeGenerator.cs
//
// Maps ILGPU IR types to GLSL ES 3.0 types with f64/i64 emulation support.
// ---------------------------------------------------------------------------------------

using global::ILGPU.IR;
using global::ILGPU.IR.Types;
using global::ILGPU.Util;
using ILGPU;
using System.Text;

namespace SpawnDev.ILGPU.WebGL.Backend
{
    /// <summary>
    /// Generates internal GLSL type structures that are used inside vertex shaders.
    /// Maps ILGPU IR types to GLSL ES 3.0 equivalents.
    /// </summary>
    public sealed class GLSLTypeGenerator : DisposeBase
    {
        #region Instance

        private readonly ReaderWriterLockSlim readerWriterLock =
            new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly Dictionary<TypeNode, string> mapping =
            new Dictionary<TypeNode, string>();

        /// <summary>
        /// Constructs a new GLSL type generator.
        /// </summary>
        internal GLSLTypeGenerator(WebGLBackend backend, IRTypeContext typeContext)
        {
            Backend = backend;
            TypeContext = typeContext;

            // Declare primitive types
            mapping[typeContext.VoidType] = "void";
            mapping[typeContext.StringType] = "uint"; // Not supported, placeholder

            // Map basic types
            foreach (var basicValueType in IRTypeContext.BasicValueTypes)
            {
                string? glslType = GetBasicValueType(basicValueType);
                if (glslType != null)
                {
                    var primitiveType = typeContext.GetPrimitiveType(basicValueType);
                    mapping[primitiveType] = glslType;
                }
            }
        }

        #endregion

        #region Properties

        /// <summary>
        /// Returns the parent backend.
        /// </summary>
        public WebGLBackend Backend { get; }

        /// <summary>
        /// Returns the underlying type context.
        /// </summary>
        public IRTypeContext TypeContext { get; }

        /// <summary>
        /// Returns the associated GLSL type name.
        /// </summary>
        public string this[TypeNode typeNode]
        {
            get
            {
                // For Float64/Int64 primitives, always compute dynamically
                // to respect the current emulation flag state.
                if (typeNode is PrimitiveType primitiveType)
                {
                    var basicType = primitiveType.BasicValueType;
                    if (basicType == BasicValueType.Float64 ||
                        basicType == BasicValueType.Int64)
                    {
                        return GetBasicValueType(basicType)!;
                    }
                }

                using var readWriteScope = readerWriterLock.EnterUpgradeableReadScope();
                if (mapping.TryGetValue(typeNode, out string? typeName))
                    return typeName;

                using var writeScope = readWriteScope.EnterWriteScope();
                return GetOrCreateType(typeNode);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Maps BasicValueType to GLSL ES 3.0 type strings.
        /// </summary>
        public string? GetBasicValueType(BasicValueType type)
        {
            return type switch
            {
                BasicValueType.Int1 => "bool",
                BasicValueType.Int8 => "int",   // GLSL ES 3.0 has no i8, promote
                BasicValueType.Int16 => "int",  // GLSL ES 3.0 has no i16, promote
                BasicValueType.Int32 => "int",
                BasicValueType.Int64 => Backend.EnableI64Emulation ? "uvec2" : "int",
                BasicValueType.Float16 => "float", // Promoting
                BasicValueType.BFloat16 => "float", // bfloat16 always emulated as float on WebGL
                BasicValueType.Float8E4M3 => "float", // FP8 always emulated as float on WebGL
                BasicValueType.Float8E5M2 => "float",
                BasicValueType.Float32 => "float",
                BasicValueType.Float64 => Backend.EnableF64Emulation ? (Backend.UseOzakiF64Emulation ? "vec4" : "vec2") : "float",
                _ => null
            };
        }

        /// <summary>
        /// Maps ArithmeticBasicValueType to GLSL ES 3.0 type strings.
        /// </summary>
        public string? GetBasicValueType(ArithmeticBasicValueType type)
        {
            return type switch
            {
                ArithmeticBasicValueType.UInt1 => "bool",
                ArithmeticBasicValueType.Int8 => "int",
                ArithmeticBasicValueType.Int16 => "int",
                ArithmeticBasicValueType.Int32 => "int",
                ArithmeticBasicValueType.Int64 => Backend.EnableI64Emulation ? "uvec2" : "int",
                ArithmeticBasicValueType.UInt8 => "uint",
                ArithmeticBasicValueType.UInt16 => "uint",
                ArithmeticBasicValueType.UInt32 => "uint",
                ArithmeticBasicValueType.UInt64 => Backend.EnableI64Emulation ? "uvec2" : "uint",
                ArithmeticBasicValueType.Float16 => "float",
                ArithmeticBasicValueType.BFloat16 => "float",
                ArithmeticBasicValueType.Float8E4M3 => "float",
                ArithmeticBasicValueType.Float8E5M2 => "float",
                ArithmeticBasicValueType.Float32 => "float",
                ArithmeticBasicValueType.Float64 => Backend.EnableF64Emulation ? (Backend.UseOzakiF64Emulation ? "vec4" : "vec2") : "float",
                _ => null
            };
        }

        private string GetOrCreateType(TypeNode typeNode)
        {
            if (mapping.TryGetValue(typeNode, out string? name))
                return name;

            if (typeNode is PointerType pointerType)
            {
                // GLSL doesn't have pointers — represent as the element type
                var elementType = this[pointerType.ElementType];
                name = elementType;
            }
            else if (typeNode is StructureType structureType)
            {
                var typeNameStr = structureType.ToString();
                if (typeNameStr.Contains("Index1D") || typeNameStr.Contains("LongIndex1D"))
                {
                    name = "int";
                }
                else if (typeNameStr.Contains("Index2D") || typeNameStr.Contains("LongIndex2D"))
                {
                    name = "ivec2";
                }
                else if (typeNameStr.Contains("Index3D") || typeNameStr.Contains("LongIndex3D"))
                {
                    name = "ivec3";
                }
                else
                {
                    name = "struct_" + typeNode.Id;
                }
            }
            else if (typeNode is PrimitiveType primitiveType)
            {
                name = GetBasicValueType(primitiveType.BasicValueType);
                if (name == null) name = "uint"; // Fallback
            }
            else if (typeNode is ViewType viewType)
            {
                name = this[viewType.ElementType];
            }
            else
            {
                name = "uint"; // Fallback
            }

            mapping[typeNode] = name;
            return name;
        }

        /// <summary>
        /// Generates struct type definitions — emitted at the top of the shader.
        /// </summary>
        public void GenerateTypeDefinitions(StringBuilder builder)
        {
            GenerateTypeDefinitions(builder, null);
        }

        /// <summary>
        /// Skip emitting GLSL struct definitions for the given set of IR type IDs —
        /// used for body-struct kernel parameters whose fields are decomposed into
        /// per-field samplers. Emitting the body struct as a plain GLSL struct would
        /// create a definition that contains "int" placeholders for ArrayView fields
        /// (since ViewType lowers to its element type) — which doesn't match anything
        /// the kernel actually references and would produce dead declarations or
        /// type-name collisions.
        /// </summary>
        public void GenerateTypeDefinitions(StringBuilder builder, HashSet<long>? skipTypeIds)
        {
            foreach (var kvp in mapping)
            {
                if (kvp.Key is StructureType structType)
                {
                    // Skip built-in GLSL types
                    if (kvp.Value == "int" ||
                        kvp.Value == "uint" ||
                        kvp.Value == "float" ||
                        kvp.Value == "bool" ||
                        kvp.Value.StartsWith("vec") ||
                        kvp.Value.StartsWith("ivec") ||
                        kvp.Value.StartsWith("uvec") ||
                        kvp.Value.StartsWith("mat"))
                        continue;

                    if (skipTypeIds != null && skipTypeIds.Contains(structType.Id))
                        continue;

                    builder.AppendLine($"struct {kvp.Value} {{");
                    int fieldIdx = 0;
                    foreach (var fieldType in structType.Fields)
                    {
                        builder.AppendLine($"    {this[fieldType]} field_{fieldIdx};");
                        fieldIdx++;
                    }
                    builder.AppendLine("};");
                    builder.AppendLine();
                }
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            readerWriterLock.Dispose();
            base.Dispose(disposing);
        }
    }
}
