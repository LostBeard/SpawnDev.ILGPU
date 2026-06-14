using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SpawnDev.ILGPU.Wasm.Backend;

/// <summary>
/// Wasm SIMD128 Phase 2 — the DECISION-GATE A/B benchmark (Option A: prove the v128 path + measure the
/// win on a CLEAN contiguous ALU-dense kernel, no gather, before committing to the Phase 3 Velocity port).
/// See Plans/wasm-simd128-phase2-design-2026-06-14.md.
///
/// Emits TWO standalone wasm modules of the Phase-0 microbench shape — `out[i] = R-fold of (acc*k1 + k2)
/// over in[i]` (1 contiguous load, R register FMAs, 1 contiguous store) — one SCALAR (f32) and one
/// SIMD (f32x4, 4 elements/step), exported as `run(start, count, R, outByteBase)`. Plus a Node harness
/// (run-bench.mjs) that instantiates both, fills input, runs at production N and several R, checks BOTH
/// against a JS scalar reference (CPU-oracle + cross-mode determinism — same mul-then-add, NO one-mode
/// FMA), and prints the scalar-vs-SIMD wall-clock ratio. Node's V8 is the same wasm engine as the
/// browser, and a single-thread run isolates the PURE ALU win from the browser dispatch/worker floor —
/// exactly the gate number we want. No browser, no Tuvok-machine contention.
///
/// Run:  dotnet run --project SpawnDev.ILGPU.DemoConsole -c Release -- simd-bench-emit
/// Then: node &lt;dir&gt;/run-bench.mjs
/// </summary>
internal static class WasmSimdBench
{
    // Fold constants: k1 just under 1 so R hundreds of FMAs stays finite (no inf/nan divergence between
    // modes). Both modes use the identical sequence mul(k1) then add(k2) — no fused FMA in either.
    private const float K1 = 0.9990234375f;
    private const float K2 = 0.000123f;

    public static Task<int> Run(string[] args)
    {
        string dir = args.Length > 1
            ? args[1]
            : Path.Combine(Directory.GetCurrentDirectory(), "_wasm_simd_bench");
        Directory.CreateDirectory(dir);

        File.WriteAllBytes(Path.Combine(dir, "scalar.wasm"), BuildModule(simd: false));
        File.WriteAllBytes(Path.Combine(dir, "simd.wasm"), BuildModule(simd: true));
        File.WriteAllText(Path.Combine(dir, "run-bench.mjs"), HarnessJs());

        Console.WriteLine($"[simd-bench-emit] wrote scalar.wasm + simd.wasm + run-bench.mjs to {dir}");
        Console.WriteLine($"[simd-bench-emit] k1={K1} k2={K2}");
        Console.WriteLine($"[simd-bench-emit] verify:  wasm-validate --enable-threads \"{Path.Combine(dir, "simd.wasm")}\"");
        Console.WriteLine($"[simd-bench-emit] run A/B:  node \"{Path.Combine(dir, "run-bench.mjs")}\"");
        return Task.FromResult(0);
    }

    // ── module construction ───────────────────────────────────────────────────────
    private static byte[] BuildModule(bool simd)
    {
        var b = new WasmModuleBuilder();
        b.ImportSharedMemory("env", "memory", 1, 16384);
        // run(start:i32, count:i32, R:i32, outByteBase:i32) -> void
        int t = b.AddFuncType(new byte[] { WasmOpCodes.I32, WasmOpCodes.I32, WasmOpCodes.I32, WasmOpCodes.I32 },
                              Array.Empty<byte>());
        int f = b.AddFunction(t);
        b.ExportFunction("run", f);

        // locals (params 0..3): 4=end(i32) 5=i(i32) 6=r(i32) 7=acc(f32) | vacc(v128)
        var locals = new List<WasmLocal>
        {
            new WasmLocal { Count = 3, Type = WasmOpCodes.I32 },                       // end, i, r
            new WasmLocal { Count = 1, Type = simd ? WasmOpCodes.V128 : WasmOpCodes.F32 }, // acc/vacc
        };
        const uint P_start = 0, P_count = 1, P_R = 2, P_outBase = 3;
        const uint L_end = 4, L_i = 5, L_r = 6, L_acc = 7;
        int step = simd ? 4 : 1;

        var c = new List<byte>();

        // end = start + count
        WasmModuleBuilder.EmitLocalGet(c, P_start);
        WasmModuleBuilder.EmitLocalGet(c, P_count);
        c.Add(WasmOpCodes.I32Add);
        WasmModuleBuilder.EmitLocalSet(c, L_end);
        // i = start
        WasmModuleBuilder.EmitLocalGet(c, P_start);
        WasmModuleBuilder.EmitLocalSet(c, L_i);

        // block $exit { loop $cont {  if (i >= end) br $exit ; <body> ; i += step ; br $cont } }
        c.Add(WasmOpCodes.Block); c.Add(WasmOpCodes.Void);
        c.Add(WasmOpCodes.Loop); c.Add(WasmOpCodes.Void);

        // if (i >= end) br_if $exit   ->  (i < end) eqz, br_if depth=1
        WasmModuleBuilder.EmitLocalGet(c, L_i);
        WasmModuleBuilder.EmitLocalGet(c, L_end);
        c.Add(WasmOpCodes.I32LtS);
        c.Add(WasmOpCodes.I32Eqz);
        c.Add(WasmOpCodes.BrIf); WasmModuleBuilder.EmitU32Leb128(c, 1); // -> $exit

        // acc = load(input + i*4)
        EmitElemAddr(c, L_i);                       // pushes byte address i*4
        if (simd) WasmModuleBuilder.EmitSimdMem(c, WasmOpCodes.V128Load, 2, 0);
        else c.AddRange(LoadF32());
        WasmModuleBuilder.EmitLocalSet(c, L_acc);

        // r = 0 ; while (r < R) { acc = acc*k1 + k2 ; r++ }
        WasmModuleBuilder.EmitI32Const(c, 0);
        WasmModuleBuilder.EmitLocalSet(c, L_r);
        c.Add(WasmOpCodes.Block); c.Add(WasmOpCodes.Void);
        c.Add(WasmOpCodes.Loop); c.Add(WasmOpCodes.Void);
        WasmModuleBuilder.EmitLocalGet(c, L_r);
        WasmModuleBuilder.EmitLocalGet(c, P_R);
        c.Add(WasmOpCodes.I32LtS);
        c.Add(WasmOpCodes.I32Eqz);
        c.Add(WasmOpCodes.BrIf); WasmModuleBuilder.EmitU32Leb128(c, 1); // exit inner
        // acc = acc * k1 + k2
        WasmModuleBuilder.EmitLocalGet(c, L_acc);
        if (simd)
        {
            WasmModuleBuilder.EmitF32x4ConstSplat(c, K1);
            WasmModuleBuilder.EmitSimd(c, WasmOpCodes.F32x4Mul);
            WasmModuleBuilder.EmitF32x4ConstSplat(c, K2);
            WasmModuleBuilder.EmitSimd(c, WasmOpCodes.F32x4Add);
        }
        else
        {
            WasmModuleBuilder.EmitF32Const(c, K1);
            c.Add(WasmOpCodes.F32Mul);
            WasmModuleBuilder.EmitF32Const(c, K2);
            c.Add(WasmOpCodes.F32Add);
        }
        WasmModuleBuilder.EmitLocalSet(c, L_acc);
        // r++
        WasmModuleBuilder.EmitLocalGet(c, L_r);
        WasmModuleBuilder.EmitI32Const(c, 1);
        c.Add(WasmOpCodes.I32Add);
        WasmModuleBuilder.EmitLocalSet(c, L_r);
        c.Add(WasmOpCodes.Br); WasmModuleBuilder.EmitU32Leb128(c, 0); // inner loop
        c.Add(WasmOpCodes.End); // inner loop
        c.Add(WasmOpCodes.End); // inner block

        // store(outBase + i*4, acc)
        WasmModuleBuilder.EmitLocalGet(c, P_outBase);
        EmitIMul4(c, L_i);
        c.Add(WasmOpCodes.I32Add);                  // outBase + i*4
        WasmModuleBuilder.EmitLocalGet(c, L_acc);
        if (simd) WasmModuleBuilder.EmitSimdMem(c, WasmOpCodes.V128Store, 2, 0);
        else c.AddRange(StoreF32());

        // i += step
        WasmModuleBuilder.EmitLocalGet(c, L_i);
        WasmModuleBuilder.EmitI32Const(c, step);
        c.Add(WasmOpCodes.I32Add);
        WasmModuleBuilder.EmitLocalSet(c, L_i);
        c.Add(WasmOpCodes.Br); WasmModuleBuilder.EmitU32Leb128(c, 0); // outer loop
        c.Add(WasmOpCodes.End); // outer loop
        c.Add(WasmOpCodes.End); // outer block

        b.SetFunctionBody(0, locals, c.ToArray());
        return b.Emit();
    }

    // pushes the byte address (i * 4) of element local L_i, from input base 0
    private static void EmitElemAddr(List<byte> c, uint iLocal) => EmitIMul4(c, iLocal);

    private static void EmitIMul4(List<byte> c, uint iLocal)
    {
        WasmModuleBuilder.EmitLocalGet(c, iLocal);
        WasmModuleBuilder.EmitI32Const(c, 4);
        c.Add(WasmOpCodes.I32Mul);
    }

    // scalar f32.load/store with align=2 (4-byte), offset 0 — address already on stack
    private static byte[] LoadF32() { var l = new List<byte>(); WasmModuleBuilder.EmitLoad(l, WasmOpCodes.F32Load, 2, 0); return l.ToArray(); }
    private static byte[] StoreF32() { var l = new List<byte>(); WasmModuleBuilder.EmitStore(l, WasmOpCodes.F32Store, 2, 0); return l.ToArray(); }

    // ── Node A/B harness ──────────────────────────────────────────────────────────
    private static string HarnessJs() => """
// Wasm SIMD128 Phase 2 decision-gate A/B. Node V8 = same wasm engine as the browser; single-thread run
// isolates the pure ALU win from the browser dispatch/worker floor. See WasmSimdBench.cs.
import { readFileSync } from 'fs';
const k1 = 0.9990234375, k2 = 0.000123;
const pages = 16384;
const memory = new WebAssembly.Memory({ initial: 1024, maximum: pages, shared: true });

function load(name) {
  const bytes = readFileSync(new URL('./' + name, import.meta.url));
  const mod = new WebAssembly.Module(bytes);
  return new WebAssembly.Instance(mod, { env: { memory } }).exports.run;
}
const runScalar = load('scalar.wasm');
const runSimd = load('simd.wasm');

function refFold(f, R) { let a = f; for (let r = 0; r < R; r++) a = a * k1 + k2; return a; }

function bench(run, N, R, outBase, iters) {
  // warm
  run(0, N, R, outBase);
  const t0 = process.hrtime.bigint();
  for (let it = 0; it < iters; it++) run(0, N, R, outBase);
  const t1 = process.hrtime.bigint();
  return Number(t1 - t0) / 1e6 / iters; // ms/iter
}

function check(run, N, R, outBase, view) {
  // fresh input each check
  for (let i = 0; i < N; i++) view[i] = ((i * 2654435761) >>> 0) / 4294967296 * 4 - 2; // [-2,2)
  run(0, N, R, outBase);
  let maxErr = 0;
  for (let i = 0; i < N; i++) {
    const ref = refFold(Math.fround(view[i]), R);
    const got = view[(outBase >> 2) + i];
    const e = Math.abs(got - ref);
    if (e > maxErr) maxErr = e;
  }
  return maxErr;
}

const N = 1 << 20;            // 1,048,576 elements (multiple of 4)
const outBase = N * 4;        // output right after input
const f32 = new Float32Array(memory.buffer);
for (let i = 0; i < N; i++) f32[i] = ((i * 2654435761) >>> 0) / 4294967296 * 4 - 2;

console.log(`N=${N}  (scalar vs f32x4 SIMD; Node ${process.version})`);
console.log('  R     scalar ms   simd ms    speedup   maxErr(scalar/simd vs JS ref)');
for (const R of [1, 4, 16, 64, 256]) {
  const errS = check(runScalar, N, R, outBase, f32);
  const errV = check(runSimd, N, R, outBase, f32);
  // reset input for timing
  for (let i = 0; i < N; i++) f32[i] = ((i * 2654435761) >>> 0) / 4294967296 * 4 - 2;
  const iters = R >= 64 ? 20 : 50;
  const ms = bench(runScalar, N, R, outBase, iters);
  const mv = bench(runSimd, N, R, outBase, iters);
  console.log(`  ${String(R).padStart(3)}   ${ms.toFixed(3).padStart(9)}   ${mv.toFixed(3).padStart(8)}   ${(ms/mv).toFixed(2).padStart(6)}x   ${errS.toExponential(2)} / ${errV.toExponential(2)}`);
}
""";
}
