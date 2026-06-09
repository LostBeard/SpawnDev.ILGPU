using Microsoft.Extensions.DependencyInjection;
using SpawnDev.BlazorJS.Cryptography;
using SpawnDev.ILGPU.Demo.Shared.UnitTests;   // P2PLogicTests (re-enable block below)
using SpawnDev.ILGPU.DemoConsole.P2PTests;     // console P2P folder (re-enable block below)
using SpawnDev.UnitTesting;

try
{
    // Offline Wasm compile dump (H8 shared-alloca audit). No browser, no dispatch.
    if (args.Length > 0 && args[0] == "wasm-dump")
        return await WasmCompileDump.Run();

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
