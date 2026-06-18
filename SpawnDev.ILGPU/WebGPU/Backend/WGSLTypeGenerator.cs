using global::ILGPU.IR;
using global::ILGPU.IR.Types;
using global::ILGPU.Util;
using ILGPU;
using System.Text;

namespace SpawnDev.ILGPU.WebGPU.Backend
{
    /// <summary>
    /// Generates internal WGSL type structures that are used inside kernels.
    /// </summary>
    public sealed class WGSLTypeGenerator : DisposeBase
    {
        #region Instance

        private readonly ReaderWriterLockSlim readerWriterLock =
            new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);
        private readonly Dictionary<TypeNode, string> mapping =
            new Dictionary<TypeNode, string>();

        /// <summary>
        /// Constructs a new type generator.
        /// </summary>
        internal WGSLTypeGenerator(WebGPUBackend backend, IRTypeContext typeContext)
        {
            Backend = backend;
            TypeContext = typeContext;

            // Declare primitive types
            mapping[typeContext.VoidType] = "void";
            mapping[typeContext.StringType] = "u32"; // Not supported, placeholder

            // Map basic types
            foreach (var basicValueType in IRTypeContext.BasicValueTypes)
            {
                string wgslType = GetBasicValueType(basicValueType);
                if (wgslType != null)
                {
                    var primitiveType = typeContext.GetPrimitiveType(basicValueType);
                    mapping[primitiveType] = wgslType;
                }
            }
        }

        #endregion

        #region Properties


        /// <summary>
        /// Returns the parent backend.
        /// </summary>
        public WebGPUBackend Backend { get; }

        /// <summary>
        /// Returns the underlying type context.
        /// </summary>
        public IRTypeContext TypeContext { get; }

        /// <summary>
        /// Per-kernel override: when set to false, Int64 maps to i32 even if Backend.EnableI64Emulation is true.
        /// Set by GenerateHeader based on whether the kernel parameters actually contain Int64.
        /// When null, falls back to Backend.EnableI64Emulation.
        /// </summary>
        public bool? KernelUsesI64 { get; set; }

        /// <summary>
        /// Per-kernel override: when set to false, Float64 maps to f32 even if Backend.EnableF64Emulation is true.
        /// Set by GenerateHeader based on whether the kernel parameters actually contain Float64.
        /// When null, falls back to Backend.EnableF64Emulation.
        /// </summary>
        public bool? KernelUsesF64 { get; set; }

        /// <summary>
        /// Returns the associated WGSL type name.
        /// </summary>
        public string this[TypeNode typeNode]
        {
            get
            {
                // CRITICAL FIX: For Float64/Int64 primitives, always compute the type dynamically
                // to respect the current emulation flag state, not the cached value from construction.
                if (typeNode is PrimitiveType primitiveType)
                {
                    var basicType = primitiveType.BasicValueType;
                    if (basicType == BasicValueType.Float16 ||
                        basicType == BasicValueType.Float64 ||
                        basicType == BasicValueType.Int64)
                    {
                        // Return dynamic value based on current flags (f16 capability, emulation)
                        return GetBasicValueType(basicType);
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

        public string GetBasicValueType(BasicValueType type)
        {
            // Use per-kernel overrides if set, otherwise fall back to global options
            var useI64Emu = KernelUsesI64 ?? Backend.EnableI64Emulation;
            var useF64Emu = KernelUsesF64 ?? Backend.EnableF64Emulation;
            return type switch
            {
                BasicValueType.Int1 => "bool",
                BasicValueType.Int8 => "i32", // WGSL doesn't have i8, promote to i32
                BasicValueType.Int16 => "i32", // WGSL doesn't have i16, promote to i32
                BasicValueType.Int32 => "i32",
                BasicValueType.Int64 => useI64Emu ? "emu_i64" : "i32",
                BasicValueType.Float16 => Backend.HasShaderF16 ? "f16" : "f32", // Native f16 when shader-f16 enabled
                BasicValueType.BFloat16 => "f32", // bfloat16 always emulated (no native WGSL bf16); value held as f32
                BasicValueType.Float8E4M3 => "f32", // FP8 always emulated; value held as f32
                BasicValueType.Float8E5M2 => "f32",
                BasicValueType.Float4E2M1 => "f32", // FP4 always emulated; value held as f32
                BasicValueType.QInt4 => "i32", // packed signed 4-bit; the nibble load sign-extends to i32
                BasicValueType.Float32 => "f32",
                BasicValueType.Float64 => useF64Emu ? "emu_f64" : "f32",
                _ => null
            };
        }

        public string GetBasicValueType(ArithmeticBasicValueType type)
        {
            // Use per-kernel overrides if set, otherwise fall back to global options
            var useI64Emu = KernelUsesI64 ?? Backend.EnableI64Emulation;
            var useF64Emu = KernelUsesF64 ?? Backend.EnableF64Emulation;
            return type switch
            {
                ArithmeticBasicValueType.UInt1 => "bool",
                ArithmeticBasicValueType.Int8 => "i32",
                ArithmeticBasicValueType.Int16 => "i32",
                ArithmeticBasicValueType.Int32 => "i32",
                ArithmeticBasicValueType.Int64 => useI64Emu ? "emu_i64" : "i32",
                ArithmeticBasicValueType.UInt8 => "u32",
                ArithmeticBasicValueType.UInt16 => "u32",
                ArithmeticBasicValueType.UInt32 => "u32",
                ArithmeticBasicValueType.UInt64 => useI64Emu ? "emu_u64" : "u32",
                ArithmeticBasicValueType.Float16 => Backend.HasShaderF16 ? "f16" : "f32",
                ArithmeticBasicValueType.BFloat16 => "f32", // bfloat16 always emulated; value held as f32
                ArithmeticBasicValueType.Float8E4M3 => "f32", // FP8 always emulated; value held as f32
                ArithmeticBasicValueType.Float8E5M2 => "f32",
                ArithmeticBasicValueType.Float4E2M1 => "f32", // FP4 always emulated; value held as f32
                ArithmeticBasicValueType.QInt4 => "i32", // packed signed 4-bit; sign-extended to i32
                ArithmeticBasicValueType.QUInt4 => "u32", // packed unsigned 4-bit; zero-extended to u32
                ArithmeticBasicValueType.Float32 => "f32",
                ArithmeticBasicValueType.Float64 => useF64Emu ? "emu_f64" : "f32",
                _ => null
            };
        }

        private string GetOrCreateType(TypeNode typeNode)
        {
            if (mapping.TryGetValue(typeNode, out string? name))
                return name;

            if (typeNode is PointerType pointerType)
            {
                // In WGSL, pointers are explicit: ptr<storage_class, element_type [, access_mode]>
                var storageClass = pointerType.AddressSpace switch
                {
                    MemoryAddressSpace.Global => "storage",
                    MemoryAddressSpace.Shared => "workgroup",
                    _ => "function"
                };

                var elementType = this[pointerType.ElementType];

                if (storageClass == "storage")
                    name = $"ptr<{storageClass}, {elementType}, read_write>";
                else
                    name = $"ptr<{storageClass}, {elementType}>";
            }
            else if (typeNode is StructureType structureType)
            {
                var typeNameStr = structureType.ToString();
                if (typeNameStr.Contains("Index1D") || typeNameStr.Contains("LongIndex1D"))
                {
                    name = "i32";
                }
                else if (typeNameStr.Contains("Index2D") || typeNameStr.Contains("LongIndex2D"))
                {
                    name = "vec2<i32>";
                }
                else if (typeNameStr.Contains("Index3D") || typeNameStr.Contains("LongIndex3D"))
                {
                    name = "vec3<i32>";
                }
                else
                {
                    name = "struct_" + typeNode.Id;
                }
            }
            else if (typeNode is PrimitiveType primitiveType)
            {
                // Try to resolve primitive type logic again if missing from map
                name = GetBasicValueType(primitiveType.BasicValueType);
                if (name == null) name = "u32"; // Actual fallback for unknown primitives
            }
            else if (typeNode is ViewType viewType)
            {
                name = this[viewType.ElementType];
            }
            else
            {
                name = "u32"; // Fallback for unknown node types
            }

            mapping[typeNode] = name;
            return name;
        }

        public void GenerateTypeDefinitions(StringBuilder builder)
        {
            // Emit struct definitions.
            //
            // CONCURRENT-MODIFICATION FIX 2026-05-04: `this[fieldType]` (the indexer)
            // can REGISTER a new type in `mapping` (see line 207). Iterating `mapping`
            // directly while the indexer mutates it throws
            // `InvalidOperationException: InvalidOperation_EnumFailedVersion`.
            // Surfaced by `Tests23_MinimalShortIntBodyStruct_IntOnly` on WebGPU when
            // a struct field's type wasn't pre-registered before this method ran.
            // Snapshot the entries first so subsequent indexer calls can grow the
            // dictionary without invalidating the enumerator. Outer-loop must also
            // re-iterate if any new struct types were added by recursive emission;
            // we drain via a worklist so all transitively-referenced struct types
            // get definitions emitted.
            var emitted = new HashSet<TypeNode>();
            while (true)
            {
                var snapshot = mapping.ToList();
                bool emittedAny = false;
                foreach (var kvp in snapshot)
                {
                    if (emitted.Contains(kvp.Key)) continue;
                    if (kvp.Key is StructureType structType)
                    {
                        // Skip if mapped to a built-in WGSL type
                        if (kvp.Value == "i32" ||
                            kvp.Value == "u32" ||
                            kvp.Value == "f32" ||
                            kvp.Value == "f16" ||
                            kvp.Value == "bool" ||
                            kvp.Value.StartsWith("vec") ||
                            kvp.Value.StartsWith("mat") ||
                            kvp.Value.StartsWith("ptr<"))
                        {
                            emitted.Add(kvp.Key);
                            continue;
                        }

                        builder.AppendLine($"struct {kvp.Value} {{");
                        int fieldIdx = 0;
                        foreach (var fieldType in structType.Fields)
                        {
                            // The indexer may add `fieldType` to `mapping` if not present;
                            // safe under our snapshot+drain pattern.
                            builder.AppendLine($"    field_{fieldIdx} : {this[fieldType]},");
                            fieldIdx++;
                        }
                        builder.AppendLine("};");
                        builder.AppendLine();
                        emitted.Add(kvp.Key);
                        emittedAny = true;
                    }
                    else
                    {
                        emitted.Add(kvp.Key);
                    }
                }
                // If the snapshot was complete (no new entries appeared while we emitted),
                // we're done. Otherwise loop and emit any structs that were lazily added.
                if (!emittedAny || mapping.Count == snapshot.Count) break;
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
