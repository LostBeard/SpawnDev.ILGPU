using System.Text.Json;
using SpawnDev.SpawnJS;
using SpawnDev.SpawnJS.JSObjects;
using SpawnDev.UnitTesting;

namespace SpawnDev.ILGPU.Demo.UnitTests
{
    /// <summary>
    /// Browser-level P2P tests that drive two <see cref="Pages.ComputeSwarm"/> pages via
    /// <c>window.open</c> popups inside the same Playwright Chromium session.
    ///
    /// <para>Why: the RealWebRtcPipelineTests in SpawnDev.ILGPU.P2P.IntegrationTests run on
    /// desktop SIPSorcery - they prove the library path, but NOT the browser-native
    /// <c>RTCPeerConnection</c> + Blazor dispatcher + InvokeAsync ordering + razor-component
    /// state wiring that the actual /compute demo depends on in a real browser. Two popups
    /// in the same tab-group give us the full end-to-end browser path without needing an
    /// external orchestrator. Pattern mirrors SpawnDev.RTC.Demo
    /// ChatDemo_TextChat_RoundTrips_BetweenTwoIframes.</para>
    ///
    /// <para>Popups (NOT iframes): top-level browsing contexts load Blazor WASM the same way
    /// the parent /tests page does. Iframes hit mysterious SIPSorcery metadata 404s per
    /// Riker's docstring on the chat variant.</para>
    /// </summary>
    public class WasmP2PBrowserTests
    {
        /// <summary>
        /// Opens a coordinator popup that auto-creates a swarm and is primed to auto-run
        /// the distributed benchmark the moment a peer connects; opens a worker popup that
        /// auto-joins via the coordinator's join link; asserts the harness sees peer
        /// discovery + benchmark completion with real non-zero results. Proves the full
        /// browser-WebRTC path: tracker signaling → DTLS handshake → sd_compute BEP 10
        /// handshake → auto-ship inputs via OnSendBuffer → worker ResolveKernel +
        /// execute real P2PDemoKernels.MultiplyBy2 → modified-buffer auto-push back →
        /// coordinator's BufferTransfer.OnBufferReceived → benchmark completion.
        ///
        /// The exact path the /compute demo page runs. If this passes, the demo works.
        /// </summary>
        [TestMethod(Timeout = 180_000)]
        public async Task ComputeSwarm_Benchmark_RoundTrips_BetweenTwoPopups()
        {
            var JS = SpawnJSRuntime.Instance;

            var coordId = "coord" + Guid.NewGuid().ToString("N")[..6];
            var workerId = "work" + Guid.NewGuid().ToString("N")[..6];

            // Clear any prior test state
            string[] slots =
            {
                $"computeSwarmState_{coordId}", $"computeSwarmState_{workerId}",
                $"computeSwarmAlive_{coordId}", $"computeSwarmAlive_{workerId}",
            };
            foreach (var k in slots) JS.Set(k, (object?)null);

            // Resolve /compute against document.baseURI so this works under GitHub Pages subpaths.
            var baseUri = JS.Get<string>("document.baseURI");
            string coordUrl = baseUri.TrimEnd('/')
                + $"/compute?testId={Uri.EscapeDataString(coordId)}&autoCreate=true&autoBenchmark=true";

            using var coordWin = JS.Call<string, string, string, Window>("window.open", coordUrl, "_blank", "width=420,height=520");
            if (coordWin == null)
                throw new Exception("window.open returned null for coordinator - popup blocked?");

            Window? workerWin = null;
            try
            {
                // 1. Wait for the coordinator popup to boot and publish a joinLink.
                await WaitFor(
                    () => GetCoordJoinLink(JS, coordId) is string s && s.StartsWith("http"),
                    timeoutSeconds: 60,
                    label: $"coordinator ({coordId}) published joinLink to computeSwarmState",
                    diagDump: () => Diag(JS, coordId, workerId));

                var joinLink = GetCoordJoinLink(JS, coordId)!;
                var separator = joinLink.Contains('?') ? "&" : "?";
                // Worker popup: auto-join the coordinator's swarm + emit state keyed to workerId.
                // `autojoin=1` skips the Join Consent dialog (existing URL-param contract).
                // Same origin as coord - each WebTorrent client generates a distinct 20-byte
                // PeerId per session, so the tracker sees two separate peers and relays them
                // normally. Same-origin also means the worker's PublishToHarness -> opener.X
                // writes land in the test page's window so we can observe worker state too.
                var workerUrl = joinLink + separator
                    + $"autojoin=1&testId={Uri.EscapeDataString(workerId)}";

                workerWin = JS.Call<string, string, string, Window>("window.open", workerUrl, "_blank", "width=420,height=520");
                if (workerWin == null)
                    throw new Exception("window.open returned null for worker - popup blocked?");

                // 2. Wait for the coordinator to report a peer count >= 1.
                await WaitFor(
                    () => GetState(JS, coordId)?.PeerCount >= 1,
                    timeoutSeconds: 60,
                    label: "coordinator sees peerCount >= 1",
                    diagDump: () => Diag(JS, coordId, workerId));

                // 3. Wait for the auto-benchmark to complete on the coordinator.
                await WaitFor(
                    () => GetState(JS, coordId) is ComputeState s && s.BenchmarkComplete,
                    timeoutSeconds: 90,
                    label: "coordinator.benchmarkComplete == true",
                    diagDump: () => Diag(JS, coordId, workerId));

                // 4. Prove the benchmark actually produced successful dispatches (not a no-op pass).
                var final = GetState(JS, coordId)!;
                if (final.BenchmarkSuccessCount < 1)
                    throw new Exception(
                        $"Benchmark completed but 0 chunks succeeded (success={final.BenchmarkSuccessCount}, " +
                        $"totalElements={final.BenchmarkTotalElements}, totalTime={final.BenchmarkTotalTime}ms)");
                if (final.BenchmarkTotalElements < 1)
                    throw new Exception(
                        $"Benchmark reported 0 total elements processed (success={final.BenchmarkSuccessCount})");

                Console.WriteLine(
                    $"[P2PBrowserTest] PASS: coord={coordId} worker={workerId} " +
                    $"peers={final.PeerCount} benchmark={final.BenchmarkSuccessCount}x ok / " +
                    $"{final.BenchmarkTotalElements:N0} elements in {final.BenchmarkTotalTime}ms");
            }
            finally
            {
                try { workerWin?.Close(); } catch { }
                try { coordWin.Close(); } catch { }
                workerWin?.Dispose();
                foreach (var k in slots) JS.Set(k, (object?)null);
            }
        }

        private record ComputeState(
            string? TestId,
            string? Role,
            int PeerCount,
            string? JoinLink,
            bool BenchmarkRunning,
            bool BenchmarkComplete,
            int BenchmarkSuccessCount,
            long BenchmarkTotalElements,
            long BenchmarkTotalTime,
            int DispatchCount);

        private static ComputeState? GetState(SpawnJSRuntime js, string id)
        {
            try
            {
                var je = js.Get<JsonElement?>($"computeSwarmState_{id}");
                if (je is null || je.Value.ValueKind != JsonValueKind.Object) return null;
                var el = je.Value;
                return new ComputeState(
                    TestId: el.TryGetProperty("testId", out var tid) && tid.ValueKind == JsonValueKind.String ? tid.GetString() : null,
                    Role: el.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null,
                    PeerCount: el.TryGetProperty("peerCount", out var pc) ? pc.GetInt32() : 0,
                    JoinLink: el.TryGetProperty("joinLink", out var jl) && jl.ValueKind == JsonValueKind.String ? jl.GetString() : null,
                    BenchmarkRunning: el.TryGetProperty("benchmarkRunning", out var br) && br.GetBoolean(),
                    BenchmarkComplete: el.TryGetProperty("benchmarkComplete", out var bc) && bc.GetBoolean(),
                    BenchmarkSuccessCount: el.TryGetProperty("benchmarkSuccessCount", out var bs) ? bs.GetInt32() : 0,
                    BenchmarkTotalElements: el.TryGetProperty("benchmarkTotalElements", out var bte) ? bte.GetInt64() : 0L,
                    BenchmarkTotalTime: el.TryGetProperty("benchmarkTotalTime", out var btt) ? btt.GetInt64() : 0L,
                    DispatchCount: el.TryGetProperty("dispatchCount", out var dc) ? dc.GetInt32() : 0);
            }
            catch
            {
                return null;
            }
        }

        private static string? GetCoordJoinLink(SpawnJSRuntime js, string coordId)
            => GetState(js, coordId)?.JoinLink;

        private static string Ph(SpawnJSRuntime js, string id)
        {
            var alive = js.Get<long?>($"computeSwarmAlive_{id}") != null ? "Alive" : "-";
            var s = GetState(js, id);
            return s == null
                ? $"{alive}/-"
                : $"{alive}/role={s.Role ?? "?"}/peers={s.PeerCount}/bench={(s.BenchmarkRunning ? "run" : s.BenchmarkComplete ? "done" : "idle")}";
        }

        private static string Diag(SpawnJSRuntime js, string coordId, string workerId)
            => $"C[{coordId}] {Ph(js, coordId)} | W[{workerId}] {Ph(js, workerId)}";

        private static async Task WaitFor(Func<bool> predicate, int timeoutSeconds, string label, Func<string>? diagDump = null)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            var nextDiag = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                try { if (predicate()) return; } catch { }
                if (diagDump != null && DateTime.UtcNow >= nextDiag)
                {
                    try { Console.WriteLine($"[WaitFor] {label}: {diagDump()}"); } catch { }
                    nextDiag = DateTime.UtcNow.AddSeconds(3);
                }
                await Task.Delay(200);
            }
            var final = diagDump != null ? $" (final state: {diagDump()})" : "";
            throw new Exception($"Timeout ({timeoutSeconds}s) waiting for: {label}{final}");
        }
    }
}
