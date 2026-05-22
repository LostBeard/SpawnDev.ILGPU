using Microsoft.Extensions.DependencyInjection;
using SpawnDev.BlazorJS.Cryptography;
using SpawnDev.ILGPU.DemoConsole.P2PTests;
using SpawnDev.UnitTesting;

try
{
    var services = new ServiceCollection();
    services.AddPlatformCrypto();
    services.AddSingleton<SpawnDev.WebTorrent.WebTorrentClient>();
    var sp = services.BuildServiceProvider();
    var runner = new UnitTestRunner(sp, true);

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
