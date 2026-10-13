// ---------------------------------------------------------------------------------------
//                               SpawnDev.ILGPU.Wasm
//                    WebAssembly Compute Backend for Blazor WebAssembly
//
// File: WasmFunctionGenerator.cs
// ---------------------------------------------------------------------------------------

using global::ILGPU.IR;
using global::ILGPU.IR.Analyses;
using System.Text;

namespace SpawnDev.ILGPU.Wasm.Backend
{
    /// <summary>
    /// Code generator for helper (non-kernel) functions.
    /// In Phase 1, helper functions are inlined or stubbed.
    /// </summary>
    public class WasmFunctionGenerator : WasmCodeGenerator
    {
        /// <summary>
        /// Initializes a generator for a non-kernel method.
        /// </summary>
        /// <param name="args">The generator arguments shared by all generators of the kernel.</param>
        /// <param name="method">The method to generate.</param>
        /// <param name="allocas">The method's allocations.</param>
        public WasmFunctionGenerator(
            in GeneratorArgs args,
            Method method,
            Allocas allocas)
            : base(args, method, allocas)
        {
        }

        /// <summary>
        /// Visits every value and terminator of the method's blocks in order. Does nothing for
        /// intrinsic or external methods, whose IR body is only a placeholder.
        /// </summary>
        public override void GenerateCode()
        {
            // An intrinsic/external method's body is ILGPU's placeholder, never real code.
            if (!Method.HasImplementation) return;
            // Visit all blocks (for helper functions)
            foreach (var block in Method.Blocks)
            {
                foreach (var value in block)
                {
                    GenerateCodeFor(value);
                }
                if (block.Terminator != null)
                    GenerateCodeFor(block.Terminator);
            }
        }

        /// <summary>Appends a one-line comment with the helper's body size to the text builder.</summary>
        /// <param name="builder">The kernel text builder.</param>
        public override void Merge(StringBuilder builder)
        {
            builder.AppendLine($"// Wasm helper function: {Code.Count} bytes");
        }
    }
}
