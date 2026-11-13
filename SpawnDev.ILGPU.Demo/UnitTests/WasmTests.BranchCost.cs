using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.UnitTests
{
    // BackendTestBase.BranchCost.cs on Wasm with the structured path on and off, in the same page, each in a fresh
    // Context + accelerator (the flag is read at compile time). Both must give the host's result; the timings are a
    // [report] line (the acceptance number of WasmStructuredControlFlow.cs: the 960-step loop near native).
    public partial class WasmTests
    {
        [TestMethod]
        public async Task BranchCost_StructuredOnAndOff_Report()
        {
            bool saved = WasmBackend.StructuredControlFlow;
            try
            {
                foreach (bool on in new[] { false, true })
                {
                    WasmBackend.StructuredControlFlow = on;
                    using var ctx = Context.Create().EnableAlgorithms().EnableWasmAlgorithms().Wasm().ToContext();
                    using var acc = await ctx.CreateWasmAcceleratorAsync();
                    Console.WriteLine($"[report] Wasm branch cost, structured control flow {(on ? "on" : "off")}: {await BranchCostReportAsync(acc)}");
                }
            }
            finally
            {
                WasmBackend.StructuredControlFlow = saved;
            }
        }
    }
}
