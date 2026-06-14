using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Offline Wasm SIMD128 emitter probe (Phase 1 of the SIMD port). Hand-builds a complete wasm
/// module that exercises the new v128 emit surface in <see cref="WasmModuleBuilder"/> — the v128
/// local type, the 0xFD prefix + u32-LEB128 sub-opcode encoding (including the multi-byte
/// sub-opcodes like f32x4.add = 228), v128.const / splat / arithmetic / bitselect / shuffle /
/// extract_lane / replace_lane, and v128.load/store memargs — then writes it to disk so
/// `wasm-validate --enable-simd` confirms it VALIDATES and `wasm2wat --enable-simd` confirms the
/// bytes DECODE to the intended instructions. Codegen does not emit v128 yet (Phase 2/3); this
/// verifies the foundation independently. No browser, no dispatch.
///
/// Run: dotnet run --project SpawnDev.ILGPU.DemoConsole -- wasm-simd-probe
/// Verify: wasm-validate --enable-simd probe.wasm  &amp;&amp;  wasm2wat --enable-simd probe.wasm
/// </summary>
internal static class WasmSimdProbe
{
    public static Task<int> Run(string[] args)
    {
        string outDir = args.Length > 1
            ? args[1]
            : Path.Combine(Directory.GetCurrentDirectory(), "_wasm_simd_probe");
        Directory.CreateDirectory(outDir);

        var b = new WasmModuleBuilder();
        // A shared memory import (like real kernels) so v128.load/store have a memory to address.
        b.ImportSharedMemory("env", "memory", 1, 16384);

        // Function type: (f32) -> f32. Returns ((param + 1)^2) lane 0 after a round-trip through
        // every v128 op below — a self-checking shape (the scalar reference is trivial).
        int typeIdx = b.AddFuncType(new byte[] { WasmOpCodes.F32 }, new byte[] { WasmOpCodes.F32 });
        int funcIdx = b.AddFunction(typeIdx);
        b.ExportFunction("probe", funcIdx);

        // Locals: one v128 (local index 1; local 0 is the f32 param).
        var locals = new List<WasmLocal> { new WasmLocal { Count = 1, Type = WasmOpCodes.V128 } };
        const uint V = 1; // v128 local index

        var c = new List<byte>();

        // v = f32x4.splat(param)
        WasmModuleBuilder.EmitLocalGet(c, 0);
        WasmModuleBuilder.EmitSimd(c, WasmOpCodes.F32x4Splat);
        // v = v + (1,1,1,1)   — v128.const splat + the multi-byte-LEB f32x4.add (228 -> 0xE4 0x01)
        WasmModuleBuilder.EmitF32x4ConstSplat(c, 1.0f);
        WasmModuleBuilder.EmitSimd(c, WasmOpCodes.F32x4Add);
        WasmModuleBuilder.EmitLocalSet(c, V);
        // v = v * v   (f32x4.mul = 230 -> 0xE6 0x01)
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitSimd(c, WasmOpCodes.F32x4Mul);
        WasmModuleBuilder.EmitLocalSet(c, V);

        // round-trip through memory: store v at addr 0 (align 16 => 2^4, log2align = 4), load back
        WasmModuleBuilder.EmitI32Const(c, 0);
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitSimdMem(c, WasmOpCodes.V128Store, 4, 0);
        WasmModuleBuilder.EmitI32Const(c, 0);
        WasmModuleBuilder.EmitSimdMem(c, WasmOpCodes.V128Load, 4, 0);
        WasmModuleBuilder.EmitLocalSet(c, V);

        // bitselect(v, 0, all-ones) == v  (mask all 1 -> picks first operand)
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitV128Const(c, new byte[16]);                 // zeros
        var allOnes = new byte[16]; for (int i = 0; i < 16; i++) allOnes[i] = 0xFF;
        WasmModuleBuilder.EmitV128Const(c, allOnes);
        WasmModuleBuilder.EmitSimd(c, WasmOpCodes.V128Bitselect);
        WasmModuleBuilder.EmitLocalSet(c, V);

        // identity i8x16.shuffle (take bytes 0..15 from the first vector)
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitLocalGet(c, V);
        var identity = new byte[16]; for (int i = 0; i < 16; i++) identity[i] = (byte)i;
        WasmModuleBuilder.EmitI8x16Shuffle(c, identity);
        WasmModuleBuilder.EmitLocalSet(c, V);

        // replace_lane 1 with extract_lane 0 (lane immediate path)
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitSimdLane(c, WasmOpCodes.F32x4ExtractLane, 0);
        WasmModuleBuilder.EmitSimdLane(c, WasmOpCodes.F32x4ReplaceLane, 1);
        WasmModuleBuilder.EmitLocalSet(c, V);

        // also exercise an i32x4 op family (i32x4.add = 174 -> 0xAE 0x01) on a throwaway value
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitSimd(c, WasmOpCodes.I32x4Add);
        WasmModuleBuilder.EmitSimd(c, WasmOpCodes.I32x4Neg);              // unary
        c.Add(WasmOpCodes.Drop);                                          // drop the i32x4 result

        // result = f32x4.extract_lane 0 of v  -> (param+1)^2
        WasmModuleBuilder.EmitLocalGet(c, V);
        WasmModuleBuilder.EmitSimdLane(c, WasmOpCodes.F32x4ExtractLane, 0);

        b.SetFunctionBody(0, locals, c.ToArray());
        byte[] wasm = b.Emit();

        string path = Path.Combine(outDir, "probe.wasm");
        File.WriteAllBytes(path, wasm);

        Console.WriteLine($"[wasm-simd-probe] wrote {wasm.Length}-byte module: {path}");
        Console.WriteLine($"[wasm-simd-probe] EffectiveWasmSimd(this host)={WasmBackend.EffectiveWasmSimd} " +
                          $"RuntimeSupportsWasmSimd={WasmBackend.RuntimeSupportsWasmSimd}");
        Console.WriteLine("[wasm-simd-probe] verify:");
        Console.WriteLine($"    wasm-validate --enable-simd \"{path}\"");
        Console.WriteLine($"    wasm2wat --enable-simd \"{path}\"");
        return Task.FromResult(0);
    }
}
