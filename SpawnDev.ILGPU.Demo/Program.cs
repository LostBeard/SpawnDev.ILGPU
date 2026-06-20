using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.Cryptography;
using SpawnDev.ILGPU.Demo;
using SpawnDev.ILGPU.Demo.Shared;
using SpawnDev.ILGPU.Demo.UnitTests;
using SpawnDev.ILGPU.P2P;
using SpawnDev.ILGPU.WebGPU.Backend;

// Print build timestamp so we can verify we're running the right build via browser console
Console.WriteLine($"[SpawnDev.ILGPU.Demo] Build: {BuildTimestamp.Value}");

// Register the demo's P2P kernels with the P2PKernelSerializer allowlist at boot,
// so any peer acting as coordinator OR worker can dispatch / resolve them without
// each page having to remember to call RegisterKernelType first. Without this, the
// worker's ResolveKernel fails, the dispatcher reports "execution failed on peer"
// and retries into exhaustion — visible in the demo as a fast-fail on the first
// distributed benchmark dispatch.
P2PKernelSerializer.RegisterKernelType(typeof(P2PDemoKernels));

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Ensure WebGPU verbose logging is disabled during Blazor unit tests
WebGPUBackend.VerboseLogging = false;

// Demo opts in to BrowserRTCPeerConnection's per-tick diagnostic flags so
// PMT P2PSwarm.TwoTab_PeerDiscovery can probe wire-close paths via JS globals.
// Production consumers leave this off (default) - the per-tick writes add JS
// interop overhead and aren't needed unless investigating wire-close issues.
SpawnDev.RTC.Browser.BrowserRTCPeerConnection.DiagnosticsEnabled = true;
builder.Services.AddBlazorJSRuntime();
builder.Services.AddPlatformCrypto();

// P2P: WebTorrent client
builder.Services.AddSingleton(sp =>
    new SpawnDev.WebTorrent.WebTorrentClient());

// P2P: Shared swarm service - holds the active compute swarm for all pages
builder.Services.AddSingleton<SpawnDev.ILGPU.Demo.Shared.Services.P2PSwarmService>();

builder.Services.AddSingleton<WebGPUTests>();
builder.Services.AddSingleton<WebGPUNoSubgroupsTests>();

builder.Services.AddSingleton<WasmTests>();
builder.Services.AddSingleton<WebGLTests>();
builder.Services.AddSingleton<DefaultTests>();

// ─── P2P backend ON HOLD (core-6 focus). Uncomment to re-enable the P2P tests.
//     Discovery is registration-only (the /tests page no longer scans the assembly),
//     so commenting these out removes them from the browser test run. ───────────────
// builder.Services.AddSingleton<SpawnDev.ILGPU.Demo.Shared.UnitTests.P2PLogicTests>();  // 169 P2P logic/dispatch tests
// builder.Services.AddSingleton<WasmP2PBrowserTests>();                                  // 180s real-WebRTC two-popup tests


builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<SpawnDev.ILGPU.Services.ShaderDebugService>();

builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var host = builder.Build();

// Stage-3d dual-mode CI: when PMT launches the test page with ?wasmsimd=off (set by
// PMT_WASM_SIMD=off), force the Wasm SCALAR path for the whole sweep so the suite runs in
// scalar mode on SIMD hardware — the cross-mode oracle for every in-class kernel. Read BEFORE
// any kernel compiles. (The Wasm SIMD gate tests toggle Force* themselves and are unaffected.)
{
    var nav = host.Services.GetRequiredService<Microsoft.AspNetCore.Components.NavigationManager>();
    if (nav.Uri.Contains("wasmsimd=off", StringComparison.OrdinalIgnoreCase))
    {
        SpawnDev.ILGPU.Wasm.Backend.WasmBackend.ForceScalar = true;
        // Prefix "[Wasm" so PMT's console hook captures this marker to the run's console log (proof the
        // scalar-mode sweep actually engaged, vs the query being lost across the COI service-worker reload).
        Console.WriteLine("[Wasm-SIMD-Mode] PMT_WASM_SIMD=off -> WasmBackend.ForceScalar=true (scalar-mode sweep engaged)");
    }
}

await host.BlazorJSRunAsync();
