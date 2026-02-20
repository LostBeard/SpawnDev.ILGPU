/*! coi-serviceworker - Cross-Origin Isolation via Service Worker */
/*
 * This service worker intercepts all responses and adds the required
 * Cross-Origin-Opener-Policy and Cross-Origin-Embedder-Policy headers
 * to enable SharedArrayBuffer support in the browser.
 *
 * Works locally and on static hosts like GitHub Pages where you
 * cannot set server response headers.
 *
 * Registration: Add <script src="blazor-coi-serviceworker.js"></script> to index.html
 */

if (typeof window !== 'undefined') {
    // --- Running as a regular script in the page context ---

    // Set to true to enable verbose logging of service worker registration and activation flow. Useful for debugging but can be noisy in normal use.
    var verbose = false;
    function consoleLog(...args) {
        if (!verbose) return;
        console.log("[COI]", ...args);
    }

    // Helper to check if Blazor script is loaded
    function isBlazorRunning() {
        return !!document.querySelector('script[src*="blazor.webassembly.js"]');
    }

    // Start Blazor immediately if we're already cross-origin isolated.
    if (window.crossOriginIsolated && !isBlazorRunning()) {
        // Already cross-origin isolated — nothing to do
        consoleLog("[COI] Cross-origin isolated ✓");
        var s = document.createElement("script");
        s.src = "_framework/blazor.webassembly.js";
        document.body.appendChild(s);
    } else {
        navigator.serviceWorker.ready.then((registration) => {
            consoleLog(`A service worker is active: ${registration.active}`);
            window.location.reload();
        });
    }

    // Register the service worker if supported. The SW will add the necessary headers to enable
    if ("serviceWorker" in navigator) {
        // Register the service worker, which will add the necessary headers to enable
        // cross-origin isolation. The SW will reload the page once activated to apply the headers.
        consoleLog("[COI] Registering service worker...");
        navigator.serviceWorker
            .register(window.document.currentScript.src)
            .then(function (reg) {
                consoleLog("[COI] Service worker registered:", reg.scope);
            })
            .catch(function (err) {
                console.error("[COI] Service worker registration failed:", err);
            });
    }
} else {
    // Set to true to enable verbose logging of service worker registration and activation flow. Useful for debugging but can be noisy in normal use.
    var verbose = false;
    function consoleLog(...args) {
        if (!verbose) return;
        console.log("[COI]", ...args);
    }
    // --- Running as a Service Worker ---
    self.addEventListener("install", function () { self.skipWaiting(); });
    self.addEventListener("activate", function (event) { event.waitUntil(self.clients.claim()); });

    self.addEventListener("fetch", function (event) {
        // Skip requests the SW can't meaningfully intercept
        if (event.request.cache === "only-if-cached" && event.request.mode !== "same-origin") {
            return;
        }

        // Don't intercept cross-origin requests — we can't add useful
        // headers to them and they are the main source of "Failed to fetch" errors
        var url = new URL(event.request.url);
        if (url.origin !== self.location.origin) {
            return; // Let the browser handle it natively
        }

        event.respondWith(
            fetch(event.request)
                .then(function (response) {
                    var newHeaders = new Headers(response.headers);
                    newHeaders.set("Cross-Origin-Embedder-Policy", "credentialless");
                    newHeaders.set("Cross-Origin-Opener-Policy", "same-origin");

                    return new Response(response.body, {
                        status: response.status,
                        statusText: response.statusText,
                        headers: newHeaders,
                    });
                })
                .catch(function (e) {
                    consoleLog("[COI] Fetch failed for:", event.request.url, e.message);
                    return new Response("Service Worker fetch failed", {
                        status: 502,
                        statusText: "Service Worker Fetch Failed",
                    });
                })
        );
    });
}
