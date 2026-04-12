/*! SpawnDev.WebTorrent — Service Worker + Blazor Loader */
/*
 * Combined service worker and Blazor loader for SpawnDev.WebTorrent apps.
 *
 * In the page context (window):
 *   - Registers this file as a service worker
 *   - Waits for SW to be ready (for COI headers + streaming)
 *   - Reloads once to apply Cross-Origin-Isolation headers
 *   - Then loads Blazor WebAssembly
 *
 * As a service worker (self):
 *   - Adds COOP/COEP headers for SharedArrayBuffer support
 *   - Intercepts /webtorrent/ requests for torrent streaming
 *   - Forwards streaming requests to the main window via MessageChannel
 *   - Supports HTTP range requests for video/audio seeking
 *
 * Usage: Replace <script src="_framework/blazor.webassembly.js"> with:
 *   <script src="webtorrent-sw.js"></script>
 *
 * Deploys to app root via StaticWebAssetBasePath="/".
 */

if (typeof window !== 'undefined') {
    // --- Running as a regular script in the page context ---

    var verbose = false;
    function consoleLog(...args) {
        if (!verbose) return;
        console.log("[COI]", ...args);
    }

    // Helper to check if Blazor script tag exists in the HTML
    function hasBlazorScript() {
        return !!document.querySelector('script[src*="blazor.webassembly"]');
    }

    // Load Blazor dynamically (only needed when no static script tag in HTML)
    function loadBlazor() {
        if (hasBlazorScript()) return;
        var s = document.createElement("script");
        s.src = "_framework/blazor.webassembly.js";
        document.body.appendChild(s);
    }

    if (!("serviceWorker" in navigator)) {
        // No service worker support — load Blazor directly, no COI or streaming
        consoleLog("[COI] Service workers not supported — SharedArrayBuffer unavailable");
        loadBlazor();
    } else {
        // Always register the SW — it provides both COI headers AND torrent streaming.
        // Registration is idempotent (browser no-ops if the script hasn't changed).
        var swRegistered = false;
        navigator.serviceWorker
            .register(window.document.currentScript.src)
            .then(function (reg) {
                swRegistered = true;
                consoleLog("[COI] Service worker registered:", reg.scope);
            })
            .catch(function (err) {
                console.error("[COI] Service worker registration failed:", err);
            });

        if (window.crossOriginIsolated && navigator.serviceWorker.controller) {
            // COI active AND SW already controlling — best case, load immediately.
            consoleLog("[COI] Cross-origin isolated ✓, SW controlling ✓");
            sessionStorage.removeItem("coi-reload-count");
            loadBlazor();
        } else if (window.crossOriginIsolated && !navigator.serviceWorker.controller) {
            // COI active (server headers) but SW not controlling yet.
            // Wait for SW to activate and claim, then reload once so it intercepts requests.
            var reloadKey = "coi-sw-reload";
            var reloadCount = parseInt(sessionStorage.getItem(reloadKey) || "0", 10);
            if (reloadCount < 1) {
                navigator.serviceWorker.ready.then(function () {
                    sessionStorage.setItem(reloadKey, "1");
                    consoleLog("[COI] SW active — reloading for control");
                    window.location.reload();
                });
                // Fallback: if SW never activates, load anyway
                setTimeout(function () {
                    consoleLog("[COI] SW not ready after 5s — loading Blazor without streaming");
                    sessionStorage.removeItem(reloadKey);
                    loadBlazor();
                }, 5000);
            } else {
                // Already reloaded once — SW should be controlling now, but if not, proceed
                consoleLog("[COI] Post-reload — loading Blazor");
                sessionStorage.removeItem(reloadKey);
                loadBlazor();
            }
        } else {
            // Not yet isolated — wait for SW to activate, then reload to apply COI headers.
            // Use sessionStorage to prevent infinite reload loops.
            var reloadKey = "coi-reload-count";
            var reloadCount = parseInt(sessionStorage.getItem(reloadKey) || "0", 10);

            if (reloadCount < 2) {
                var reloaded = false;
                var doReload = function () {
                    if (reloaded) return;
                    reloaded = true;
                    sessionStorage.setItem(reloadKey, String(reloadCount + 1));
                    consoleLog("[COI] Reloading to apply COI headers (attempt " + (reloadCount + 1) + ")");
                    window.location.reload();
                };

                navigator.serviceWorker.ready.then(doReload);
                setTimeout(function () {
                    if (!reloaded && navigator.serviceWorker.controller) {
                        // SW is controlling but ready didn't fire — force reload
                        doReload();
                    } else if (!reloaded) {
                        consoleLog("[COI] Service worker not ready after 5s — loading without COI");
                        loadBlazor();
                    }
                }, 5000);
            } else {
                // Already tried reloading — COI isn't working, proceed without it.
                // Clear the counter so next fresh navigation can try again.
                console.warn("[COI] Cross-origin isolation failed after " + reloadCount +
                    " reload(s) — SharedArrayBuffer unavailable. Wasm limited to 1 worker.");
                sessionStorage.removeItem(reloadKey);
                loadBlazor();
            }
        }
    }

} else {
    // ═══════════════════════════════════════════════════════════
    //  SERVICE WORKER CONTEXT — COI Headers + Torrent Streaming
    // ═══════════════════════════════════════════════════════════

    self.addEventListener('install', () => self.skipWaiting());
    self.addEventListener('activate', (event) => event.waitUntil(self.clients.claim()));

    self.addEventListener('fetch', (event) => {
        if (event.request.cache === 'only-if-cached' && event.request.mode !== 'same-origin') {
            return;
        }

        const url = new URL(event.request.url);
        if (url.origin !== self.location.origin) {
            return;
        }

        // Health check — lets clients verify the SW is active and intercepting
        if (url.pathname.endsWith('/webtorrent-sw-check')) {
            event.respondWith(new Response(JSON.stringify({
                name: 'SpawnDev.WebTorrent',
                active: true,
                scope: self.registration.scope,
            }), {
                status: 200,
                headers: { 'Content-Type': 'application/json' },
            }));
            return;
        }

        // WebTorrent streaming — intercept /webtorrent/ paths
        if (url.pathname.includes('/webtorrent/')) {
            event.respondWith(handleWebtorrentStream(event));
            return;
        }

        // COI headers for all other same-origin requests
        event.respondWith(addCoiHeaders(event.request));
    });

    async function addCoiHeaders(request) {
        try {
            const response = await fetch(request);
            const headers = new Headers(response.headers);
            headers.set('Cross-Origin-Embedder-Policy', 'credentialless');
            headers.set('Cross-Origin-Opener-Policy', 'same-origin');
            return new Response(response.body, {
                status: response.status,
                statusText: response.statusText,
                headers,
            });
        } catch (e) {
            return new Response('Service Worker fetch failed', { status: 502 });
        }
    }

    // ── WebTorrent streaming ──
    // Protocol (matches SpawnDev.BlazorJS.WebDesktop/service-worker-fs.js exactly):
    // 1. SW posts request to client via MessageChannel port2
    // 2. Client wires up port.OnMessage, calls port.Start(), then port.PostMessage(response)
    // 3. If response.body === 'stream_pull': SW creates ReadableStream
    // 4. On pull: SW sends { eventType: 'pull', desiredSize: N } to client
    // 5. Client reads chunk, sends Uint8Array back. Falsy = done.
    // 6. On cancel: SW sends { eventType: 'cancel', desiredSize: 0 }

    async function handleWebtorrentStream(event) {
        const allClients = await self.clients.matchAll({ type: 'window', includeUncontrolled: true });
        if (allClients.length === 0) {
            return new Response('No client available', { status: 503 });
        }

        const request = event.request;
        const clientId = event.clientId || event.resultingClientId;
        let client = clientId ? await self.clients.get(clientId) : null;
        if (!client) client = allClients[0];

        const mc = new MessageChannel();

        // Wait for initial response from client
        const data = await new Promise((resolve) => {
            mc.port1.onmessage = (evt) => resolve(evt.data);
            client.postMessage({
                type: 'webtorrent',
                url: request.url,
                method: request.method,
                destination: request.destination,
                headers: Object.fromEntries(request.headers.entries()),
                scope: self.registration.scope,
            }, [mc.port2]);
        });

        if (!data || !data.body) {
            return new Response('No response from client', { status: 500 });
        }

        if (data.body === 'stream_pull') {
            // Pull-based streaming — matches service-worker-fs.js exactly
            const stream = new ReadableStream({
                pull(controller) {
                    const desiredSize = controller.desiredSize;
                    return new Promise((resolve) => {
                        mc.port1.onmessage = (evt) => {
                            let done = !evt.data;
                            if (evt.data) {
                                try {
                                    controller.enqueue(evt.data);
                                } catch (ex) {
                                    done = true;
                                    mc.port1.postMessage({ eventType: 'error', desiredSize: 0 });
                                }
                            }
                            if (done) {
                                try { controller.close(); } catch {}
                                mc.port1.onmessage = null;
                            }
                            resolve();
                        };
                        mc.port1.postMessage({ eventType: 'pull', desiredSize: desiredSize });
                    });
                },
                cancel() {
                    mc.port1.postMessage({ eventType: 'cancel', desiredSize: 0 });
                }
            });
            return new Response(stream, data);
        } else {
            // Direct response
            return new Response(data.body, data);
        }
    }
}
