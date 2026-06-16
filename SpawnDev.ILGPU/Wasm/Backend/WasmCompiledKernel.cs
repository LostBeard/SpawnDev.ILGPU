// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmCompiledKernel.cs
// ---------------------------------------------------------------------------------------

using global::ILGPU;
using global::ILGPU.Backends;
using global::ILGPU.Backends.EntryPoints;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// Represents a compiled Wasm kernel containing the binary module bytes.
    /// </summary>
    public sealed class WasmCompiledKernel : CompiledKernel
    {
        /// <summary>
        /// Gets the compiled WebAssembly binary module.
        /// </summary>
        public byte[] WasmBinary { get; }

        /// <summary>
        /// Gets the number of parameter bindings expected by the kernel.
        /// </summary>
        public int BindingCount { get; }

        /// <summary>
        /// Gets the parameter metadata for marshaling arguments.
        /// </summary>
        public List<WasmParamInfo> ParamInfos { get; }

        /// <summary>
        /// Total bytes needed for shared memory allocations within a workgroup.
        /// </summary>
        public int SharedMemorySize { get; }

        /// <summary>
        /// Number of barrier synchronization points in the kernel.
        /// Each barrier uses 8 bytes (2 × i32: arrival counter + sense flag).
        /// </summary>
        public int BarrierCount { get; }

        /// <summary>
        /// Whether this kernel uses shared memory or barriers.
        /// </summary>
        public bool HasBarriers { get; }

        /// <summary>
        /// Number of elements requested for dynamic shared memory (0 if none).
        /// </summary>
        public int DynamicSharedElementSize { get; }

        /// <summary>
        /// Scratch memory bytes used per thread. For barrier kernels, each worker needs
        /// its own scratch region (total = ScratchPerThread × groupSize).
        /// </summary>
        public int ScratchPerThread { get; }

        /// <summary>
        /// Number of phases for fiber-based dispatch. 1 = no barriers (single phase).
        /// N+1 for N barriers — the kernel is called once per phase per fiber.
        /// </summary>
        public int PhaseCount { get; }

        /// <summary>
        /// Set of kernel-parameter indices that the kernel STORES to. Populated by the
        /// codegen via <c>WasmKernelFunctionGenerator.WrittenParamIndices</c> -> here.
        /// Used by <c>WasmAccelerator.RunKernelAsync</c> to skip copy-OUT for buffers
        /// whose param is not in this set: those are input-only and copying their
        /// stale wasmMemory contents back to SharedBuffer would clobber any
        /// host-side <c>CopyFromCPU</c> writes that landed during the in-flight
        /// dispatch (the 2026-05-03 Wasm copy-OUT race).
        /// </summary>
        public HashSet<int> WrittenParamIndices { get; }

        /// <summary>
        /// Diagnostic: per-Store trace records captured by codegen. Inspect when
        /// the copy-OUT skip behavior misclassifies a buffer as input-only.
        /// </summary>
        public List<string> StoreTargetTrace { get; }

        /// <summary>
        /// IR parameter index of dispatch user arg <c>args[paramIdx]</c> (after optional
        /// Index extent skip in args[]). Equals codegen startIdx offset: 0 for
        /// GridStrideLoopKernel (LongIndex extent at IR param 0), 1 for typical kernels.
        /// </summary>
        public int IrUserParamIndexOffset { get; }

        /// <summary>
        /// True iff the module ALSO exports a v128 <c>kernel_simd</c> (4 lanes/call) for the
        /// Stage-3a f32 unit-stride elementwise class. When true, the non-barrier flat dispatch
        /// runs <c>kernel_simd</c> by 4 then the scalar <c>kernel</c> for the <c>count % 4</c> tail.
        /// False ⇒ pure scalar dispatch (no regression). Only set when SIMD was effective at
        /// compile time AND the kernel was in-class — so the worker engine accepts v128.
        /// </summary>
        public bool HasSimdKernel { get; }

        /// <summary>
        /// Creates a new compiled Wasm kernel.
        /// </summary>
        public WasmCompiledKernel(
            Context context,
            EntryPoint entryPoint,
            byte[] wasmBinary,
            int bindingCount,
            List<WasmParamInfo> paramInfos,
            int sharedMemorySize = 0,
            int barrierCount = 0,
            bool hasBarriers = false,
            int dynamicSharedElementSize = 0,
            int scratchPerThread = 0,
            int phaseCount = 1,
            HashSet<int>? writtenParamIndices = null,
            List<string>? storeTargetTrace = null,
            int irUserParamIndexOffset = 1,
            bool hasSimdKernel = false)
            : base(context, entryPoint, null)
        {
            WasmBinary = wasmBinary;
            BindingCount = bindingCount;
            ParamInfos = paramInfos;
            SharedMemorySize = sharedMemorySize;
            BarrierCount = barrierCount;
            HasBarriers = hasBarriers;
            DynamicSharedElementSize = dynamicSharedElementSize;
            ScratchPerThread = scratchPerThread;
            PhaseCount = phaseCount;
            WrittenParamIndices = writtenParamIndices ?? new HashSet<int>();
            StoreTargetTrace = storeTargetTrace ?? new List<string>();
            IrUserParamIndexOffset = irUserParamIndexOffset;
            HasSimdKernel = hasSimdKernel;
        }
    }

    /// <summary>
    /// Describes a kernel parameter for marshaling.
    /// </summary>
    public class WasmParamInfo
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public bool IsView { get; set; }
        /// <summary>User body struct (e.g. TensorView) with embedded views expanded to flat view args.</summary>
        public bool IsBodyStruct { get; set; }
        public bool IsScalar { get; set; }
        public byte WasmType { get; set; } = WasmOpCodes.I32;
        public int ElementSize { get; set; } = 4;

        /// <summary>
        /// For struct parameters: the flattened IR field layout.
        /// Each entry describes a leaf field's offset, type, and size within the struct.
        /// Used at dispatch time to manually serialize the struct to match the IR layout.
        /// </summary>
        public List<StructFieldInfo> StructFields { get; set; }

        /// <summary>
        /// For struct parameters: total size in bytes according to IR layout.
        /// </summary>
        public int StructSize { get; set; }

        /// <summary>
        /// Debug: the IR type name for this parameter.
        /// </summary>
        public string IRTypeName { get; set; }
    }

    /// <summary>
    /// Describes a leaf field within an IR StructureType.
    /// </summary>
    public class StructFieldInfo
    {
        /// <summary>Byte offset within the struct.</summary>
        public int Offset { get; set; }
        /// <summary>Wasm type (I32, I64, F32, F64).</summary>
        public byte WasmType { get; set; }
        /// <summary>Size in bytes.</summary>
        public int Size { get; set; }
        /// <summary>True if this is the pointer field of a view (needs buffer offset patching).</summary>
        public bool IsViewPtr { get; set; }
    }
}
