/*! coi-serviceworker - Cross-Origin Isolation via Service Worker */
/*
 * This service worker intercepts all responses and adds the required
 * Cross-Origin-Opener-Policy and Cross-Origin-Embedder-Policy headers
 * to enable SharedArrayBuffer support in the browser.
 *
 * Works locally and on static hosts like GitHub Pages where you
 * cannot set server response headers.
 *
 * Registration: Add <script src="coi-serviceworker.js"></script> to index.html
 */

if (typeof window !== 'undefined') {
    // --- Running as a regular script in the page context ---

    if (window.crossOriginIsolated) {
        // Already cross-origin isolated — nothing to do
        console.log("[COI] Cross-origin isolated ✓");
    } else if ("serviceWorker" in navigator) {
        if (navigator.serviceWorker.controller) {
            // SW is controlling the page but we're not isolated.
            // This can happen if the SW was just updated or headers aren't applied yet.
            // Reload to pick up the headers from the active SW.
            console.warn("[COI] Controlled by SW but not isolated. Reloading...");
            window.location.reload();
        } else {
            // No controller — register (or re-register) the SW, then reload
            // once it's active and claiming clients.
            console.log("[COI] Registering service worker...");
            navigator.serviceWorker
                .register(window.document.currentScript.src)
                .then(function (reg) {
                    console.log("[COI] Service worker registered:", reg.scope);

                    function waitForActivation(worker) {
                        worker.addEventListener("statechange", function () {
                            if (worker.state === "activated") {
                                console.log("[COI] Worker activated — reloading.");
                                window.location.reload();
                            }
                        });
                    }

                    if (reg.installing) {
                        // SW is installing — wait for it to activate
                        waitForActivation(reg.installing);
                    } else if (reg.waiting) {
                        // SW installed but waiting — wait for activation
                        waitForActivation(reg.waiting);
                    } else if (reg.active) {
                        // SW is active but not controlling this page yet.
                        // clients.claim() in the SW will trigger "controllerchange".
                        navigator.serviceWorker.addEventListener("controllerchange", function () {
                            console.log("[COI] Controller changed — reloading.");
                            window.location.reload();
                        });
                    }
                })
                .catch(function (err) {
                    console.error("[COI] Service worker registration failed:", err);
                });
        }
    } else {
        console.warn("[COI] Service workers are not supported.");
    }
} else {
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
                    console.warn("[COI] Fetch failed for:", event.request.url, e.message);
                    return new Response("Service Worker fetch failed", {
                        status: 502,
                        statusText: "Service Worker Fetch Failed",
                    });
                })
        );
    });
}
