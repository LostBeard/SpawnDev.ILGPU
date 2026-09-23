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
        public WasmFunctionGenerator(
            in GeneratorArgs args,
            Method method,
            Allocas allocas)
            : base(args, method, allocas)
        {
        }

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

        public override void Merge(StringBuilder builder)
        {
            builder.AppendLine($"// Wasm helper function: {Code.Count} bytes");
        }
    }
}
