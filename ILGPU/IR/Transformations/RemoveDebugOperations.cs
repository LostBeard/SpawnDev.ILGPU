// ---------------------------------------------------------------------------------------
//                                    SpawnDev.ILGPU
//
// File: RemoveDebugOperations.cs
//
// This file is part of the SpawnDev ILGPU fork and is distributed under the University of
// Illinois Open Source License. See LICENSE.txt for details.
// ---------------------------------------------------------------------------------------

using ILGPU.IR.Rewriting;
using ILGPU.IR.Values;

namespace ILGPU.IR.Transformations
{
    /// <summary>
    /// Removes every <see cref="DebugAssertOperation"/> and <see cref="WriteToOutput"/> from a program, for backends
    /// that cannot implement them (the WGSL, GLSL and Wasm generators emit nothing for either).
    /// </summary>
    /// <remarks>
    /// Without this pass the operation is gone from the output but its OPERANDS are not: an assertion is a root for
    /// <see cref="DeadCodeElimination"/>, so the values that only feed it (every ArrayView indexer carries a
    /// <c>Trace.Assert</c> on the index and on the length) stay alive and are emitted as unused assignments - on WebGPU
    /// each of them a chain of emulated-i64 compares. MEASURED 2026-10-21 on TRELLIS.2's tiled attention WGSL: 32 % of
    /// all assignments dead, 408 of the 410 i64 calls in main() (gemm: 47 %, 756 of 762). Run <see cref="DeadCodeElimination"/>
    /// after this pass to drop the chains. Backends with a real implementation (CUDA, OpenCL, CPU) use
    /// <see cref="AcceleratorSpecializer"/>, which keeps the operations when assertions are enabled.
    /// </remarks>
    public sealed class RemoveDebugOperations : SequentialUnorderedTransformation
    {
        private static readonly Rewriter Rewriter = new Rewriter();

        static RemoveDebugOperations()
        {
            Rewriter.Add<DebugAssertOperation>((context, value) => context.Remove(value));
            Rewriter.Add<WriteToOutput>((context, value) => context.Remove(value));
        }

        /// <summary>
        /// Removes all debug operations of the given method.
        /// </summary>
        protected override bool PerformTransformation(IRContext context, Method.Builder builder) =>
            Rewriter.Rewrite(builder.SourceBlocks, builder);
    }
}
