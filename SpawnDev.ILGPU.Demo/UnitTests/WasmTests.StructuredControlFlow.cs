using ILGPU;
using ILGPU.Runtime;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;
using SpawnDev.ILGPU.Wasm;
using SpawnDev.ILGPU.Wasm.Backend;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.UnitTests
{
    // The A/B of WasmStructuredControlFlow.cs: every branchy kernel of BackendTestBase.StructuredControlFlow.cs compiled
    // with WasmBackend.StructuredControlFlow on and off, each in a fresh Context + accelerator (the flag is read at
    // compile time; a cached kernel would hide it). Both must match the host. The ON run must emit at least one
    // structured function (else the structured path is not under test and the run passes vacuously), the OFF run none,
    // and the irreducible kernel must keep the state machine in both.
    public partial class WasmTests
    {
        [TestMethod]
        public async Task StructuredControlFlow_OnAndOff_MatchHostAndTakeTheirPath()
        {
            bool saved = WasmBackend.StructuredControlFlow;
            try
            {
                foreach (var (name, kernel, oracle) in ScfCases)
                {
                    foreach (bool on in new[] { true, false })
                    {
                        WasmBackend.StructuredControlFlow = on;
                        int before = WasmBackend.StructuredFunctionCount;
                        using var ctx = Context.Create().EnableAlgorithms().EnableWasmAlgorithms().Wasm().ToContext();
                        using var acc = await ctx.CreateWasmAcceleratorAsync();
                        await ScfRunAndCheck(acc, $"{name} (structured {(on ? "on" : "off")})", kernel, oracle);
                        int structured = WasmBackend.StructuredFunctionCount - before;
                        bool expectStructured = on && name != "Irreducible";
                        if (expectStructured && structured == 0)
                            throw new Exception($"{name}: StructuredControlFlow on, but no function was emitted as structured control flow (the state machine ran; the test would pass vacuously)");
                        if (!expectStructured && structured != 0)
                            throw new Exception($"{name}: {structured} structured function(s) with StructuredControlFlow {(on ? "on" : "off")}; expected the state machine");
                    }
                }
                foreach (bool on in new[] { true, false })
                {
                    WasmBackend.StructuredControlFlow = on;
                    using var ctx = Context.Create().EnableAlgorithms().EnableWasmAlgorithms().Wasm().ToContext();
                    using var acc = await ctx.CreateWasmAcceleratorAsync();
                    await ScfRunStructStore(acc, $"StructStore (structured {(on ? "on" : "off")})");
                }
            }
            finally
            {
                WasmBackend.StructuredControlFlow = saved;
            }
        }
    }
}
