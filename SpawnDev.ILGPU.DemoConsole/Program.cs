using Microsoft.Extensions.DependencyInjection;
using SpawnDev.BlazorJS.Cryptography;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;   // P2PLogicTests (re-enable block below)
using SpawnDev.ILGPU.DemoConsole.P2PTests;     // console P2P folder (re-enable block below)
using SpawnDev.UnitTesting;

try
{
    // CPU per-launch latency probe (GGUFDecodeKVCache slowness). No browser, no ML.
    if (args.Length > 0 && args[0] == "cpu-launch-lat")
        return await CpuLaunchLatencyProbe.Run(args);

    // Offline WGSL dump of QInt4 vs FP4 radix kernels (WebGPU-only QInt4 mis-sort). No browser.
    if (args.Length > 0 && args[0] == "qint4-radix-wgsl")
        return await QInt4RadixWgslDump.Run();

    if (args.Length > 0 && args[0] == "fp4-helper-wgsl")
        return await Fp4HelperWgslDump.Run();

    if (args.Length > 0 && args[0] == "fp4-helper-glsl")
        return await Fp4HelperGlslDump.Run();

    if (args.Length > 0 && args[0] == "fp4-helper-ptx")
        return await Fp4HelperPtxDump.Run();

    if (args.Length > 0 && args[0] == "fp4-helper-wasm")
        return await Fp4HelperWasmDump.Run();

    // Offline WGSL/GLSL dump of the FromRawBits sub-word decode (browser construction gap). No browser.
    if (args.Length > 0 && args[0] == "fromrawbits-dump")
        return await FromRawBitsDump.Run();

    // Offline Wasm compile dump (H8 shared-alloca audit). No browser, no dispatch.
    if (args.Length > 0 && args[0] == "wasm-dump")
        return await WasmCompileDump.Run();

    // Offline Wasm local-array (new float[]) miscompile dump (Tuvok 2026-06-21).
    if (args.Length > 0 && args[0] == "local-array-dump")
        return await LocalArrayDump.Run();

    // Offline Wasm +inf codegen probe (Tuvok finding #2). No browser, no dispatch.
    if (args.Length > 0 && args[0] == "wasm-inf")
        return await WasmInfProbe.Run();

    // Offline Wasm SIMD128 emitter probe (SIMD port Phase 1). Hand-builds a v128 module and writes
    // it for wasm-validate/wasm2wat verification. No browser, no dispatch.
    if (args.Length > 0 && args[0] == "wasm-simd-probe")
        return await WasmSimdProbe.Run(args);

    // Wasm SIMD128 Phase 2 decision-gate A/B: emit scalar + f32x4 FMA-fold kernels + a Node harness
    // (run-bench.mjs) that times scalar-vs-SIMD on the same ALU-dense workload. No browser.
    if (args.Length > 0 && args[0] == "simd-bench-emit")
        return await WasmSimdBench.Run(args);

    // Wasm SIMD128 Phase 3 Stage-3a: validate the uniformity analysis on real kernel IR. No browser.
    if (args.Length > 0 && args[0] == "simd-analyze")
        return await WasmSimdAnalyzeProbe.Run();

    // Wasm SIMD128 Phase 3 Stage-3a STRUCTURAL gate: ForceSimd-compile real elementwise kernels,
    // assert a kernel_simd export is emitted + write each module for wasm-validate. No browser.
    if (args.Length > 0 && args[0] == "wasm-simd-emit-gate")
        return await WasmSimdEmitGateProbe.Run(args);

    // Offline dump of GridGroupDimensionKernel Wasm (debug the explicit-KernelConfig GlobalIndex bug).
    if (args.Length > 0 && args[0] == "wasm-gridgroup-dump")
        return await WasmGridGroupDumpProbe.Run(args);

    // Desktop repro for generic INumber<T> Half/bf16 codegen gaps (CUDA bf16 + OpenCL scalar param).
    if (args.Length > 0 && args[0] == "generic-precision-repro")
        return await GenericPrecisionRepro.Run();

    // FP8 conversion verification (CPU/managed) — E5M2 idempotence + decode reference + specials.
    if (args.Length > 0 && args[0] == "fp8-verify")
        return await Float8Repro.Run();

    // FP4 conversion verification (CPU/managed + CPU/CUDA/OpenCL kernel) — E2M1 idempotence +
    // decode reference + exact values + overflow saturation + generic INumber relu kernel.
    if (args.Length > 0 && args[0] == "fp4-verify")
        return await Float4Repro.Run();

    // Packed 4-bit storage allocation foundation: ArrayView<QInt4> = ceil(N/2) bytes.
    if (args.Length > 0 && args[0] == "packed-alloc-verify")
        return await PackedAllocVerify.Run();

    // Packed QInt4 nibble LOAD end-to-end (CPU/CUDA/OpenCL): y[i] = (int)x[i].
    if (args.Length > 0 && args[0] == "packed-qint4-verify")
        return await PackedQInt4Verify.Run();

    // Packed QInt4 nibble STORE (CUDA/OpenCL atomic-word-RMW; CPU fail-loud): dst[i] = (QInt4)src[i].
    if (args.Length > 0 && args[0] == "packed-qint4-store-verify")
        return await PackedQInt4StoreVerify.Run();

    // FP8 conversions vs the ml_dtypes reference (float8_e4m3fn / e5m2) - answers the
    // overflow-convention question flagged in Float8E4M3.cs with evidence.
    if (args.Length > 0 && args[0] == "fp8-oracle")
        return await Float8OracleCompare.Run(args);

    // bf16 + Half conversions vs ml_dtypes.bfloat16 / numpy.float16 (exhaustive, all 65536 patterns).
    if (args.Length > 0 && args[0] == "bf16-f16-oracle")
        return await LowPrecisionOracleCompare.Run(args);

    // Generic in-kernel float<->T conversion (Tuvok's PrecisionConvert ask) on desktop backends.
    if (args.Length > 0 && args[0] == "precision-convert")
        return await GenericConvertRepro.Run();

    // Offline WGSL generation probe (precompiled-shaders Layer 1). No device/browser.
    if (args.Length > 0 && args[0] == "shader-gen")
        return await ShaderGenDump.Run();

    // Offline bf16 ExtractRadixBits WGSL dump (emulated-f16 profile = the failing bf16 radix path).
    if (args.Length > 0 && args[0] == "bf16-radix-emit")
        return await BF16RadixWgslProbe.Run();

    // Offline subgroup/warp reduce probe (work-order #1). Confirms the high-level reduce API
    // lowers to native subgroupAdd with subgroups, shared-mem fallback without. No device/browser.
    if (args.Length > 0 && args[0] == "subgroup-reduce")
        return await SubgroupReduceProbe.Run(args);

    // Emit the real generated inclusive-scan kernel(s) for the pure-Node repro harness.
    if (args.Length > 0 && args[0] == "scan-emit")
        return await WasmScanEmit.Run(args.Length > 1 ? args[1] : @"D:\users\tj\Projects\SpawnDev.ILGPU\wasm-scan-repro");

    // Emit the real radix-sort kernels (pass1/scan/pass2) for the full-pipeline Node repro.
    if (args.Length > 0 && args[0] == "radix-emit")
        return await WasmScanEmit.Run(args.Length > 1 ? args[1] : @"D:\users\tj\Projects\SpawnDev.ILGPU\wasm-radix-repro", radix: true);

    // Emit the in-kernel single-value ExclusiveScan exerciser (item-2 validation harness).
    if (args.Length > 0 && args[0] == "inkernel-emit")
        return await WasmInKernelScanEmit.Run(args.Length > 1 ? args[1] : @"D:\users\tj\Projects\SpawnDev.ILGPU\SpawnDev.ILGPU\SpawnDev.ILGPU\Wasm\repro\wasm-inkernel-scan-repro");

    // Precompiled-shaders Layer 2 serialization round-trip (sidecar + manifest). No device/browser.
    if (args.Length > 0 && args[0] == "shader-roundtrip")
        return ShaderArtifactRoundTrip.Run();

    // Precompiled-shaders Layer 2 end-to-end: build-time precompile -> emit -> runtime load. No browser.
    if (args.Length > 0 && args[0] == "precompile-e2e")
        return ShaderArtifactRoundTrip.RunPrecompileE2E();

    var services = new ServiceCollection();
    services.AddPlatformCrypto();
    services.AddSingleton<SpawnDev.WebTorrent.WebTorrentClient>();
    var sp = services.BuildServiceProvider();

    // Registration-only test discovery. Replaces the old `findAll` reflection scan,
    // which was load-order fragile (it silently missed standalone Demo.Shared test
    // classes like P2PLogicTests). Every desktop test class is listed explicitly here;
    // to gate a class off, comment its line out.
    var runner = new UnitTestRunner(sp);
    runner.SetTestTypes(new[]
    {
        typeof(CPUTests),
        typeof(CudaTests),
        typeof(OpenCLTests),
        typeof(AcceleratorRequirementsTests),
        typeof(CuRandTests),
        typeof(NvJpegTests),
        typeof(UnsupportedKernelFeatureExceptionTests),

        // ─── P2P backend ON HOLD (core-6 focus). Uncomment this block to re-enable
        //     every P2P unit test in one place. ───────────────────────────────────
        // typeof(P2PLogicTests),               // 169 backend-agnostic P2P logic/dispatch tests
        // typeof(CorePipelineTests),
        // typeof(MultiPeerTests),
        // typeof(P2PBinaryFrameTests),
        // typeof(SecurityTests),
        // typeof(StressTests),
        // typeof(Bep46PropagationTests),
        // typeof(RealWebRtcPipelineTests),
        // typeof(StrictFloat64WireTests),
    });

    // NOTE: LocalTrackerFixture disabled by TJ on 2026-05-21 11-14 due to the massive perforamnce hit
    // tests take and it should cleanly fallback to the live hub.spawndev.com tracker
    // anyways. When it was enabled it started every single test causign a full test run to take ~3 hours.
    // 
    // Start the local P2P tracker once before any tests run. Tests that don't need it
    // are unaffected; real - WebRTC tests use LocalTrackerFixture.GetTrackerUrl() which
    // falls back to hub.spawndev.com when the local tracker isn't available.
    //await LocalTrackerFixture.InitAsync();

    await ConsoleRunner.Run(args, runner);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"[DemoConsole] Fatal: {ex}");
    return 1;
}
return 0;
