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
    const reloadedByCOI = window.sessionStorage.getItem("coiReloadedByCOI");
    window.sessionStorage.removeItem("coiReloadedByCOI");

    if (window.crossOriginIsolated) {
        // Already cross-origin isolated — nothing to do
        console.log("[COI] Already cross-origin isolated.");
    } else if ("serviceWorker" in navigator) {
        navigator.serviceWorker
            .register(new URL("coi-serviceworker.js", window.location.href).href)
            .then(
                (registration) => {
                    console.log("[COI] Service worker registered.", registration.scope);

                    registration.addEventListener("updatefound", () => {
                        const newWorker = registration.installing;
                        newWorker.addEventListener("statechange", () => {
                            if (newWorker.state === "activated") {
                                console.log("[COI] New service worker activated, reloading.");
                                window.sessionStorage.setItem("coiReloadedByCOI", "true");
                                window.location.reload();
                            }
                        });
                    });

                    // If already active, reload once to apply headers
                    if (registration.active && !reloadedByCOI) {
                        console.log("[COI] Service worker active, reloading to apply headers.");
                        window.sessionStorage.setItem("coiReloadedByCOI", "true");
                        window.location.reload();
                    }
                },
                (err) => {
                    console.error("[COI] Service worker registration failed:", err);
                }
            );
    } else {
        console.warn("[COI] Service workers are not supported.");
    }
} else {
    // --- Running as a Service Worker ---
    self.addEventListener("install", () => self.skipWaiting());
    self.addEventListener("activate", (event) => event.waitUntil(self.clients.claim()));

    self.addEventListener("fetch", function (event) {
        // Skip requests the SW can't meaningfully intercept
        if (event.request.cache === "only-if-cached" && event.request.mode !== "same-origin") {
            return;
        }

        // Don't intercept cross-origin requests — we can't add useful
        // headers to them and they are the main source of "Failed to fetch" errors
        const url = new URL(event.request.url);
        if (url.origin !== self.location.origin) {
            return; // Let the browser handle it natively
        }

        event.respondWith(
            fetch(event.request)
                .then((response) => {
                    const newHeaders = new Headers(response.headers);
                    newHeaders.set("Cross-Origin-Embedder-Policy", "credentialless");
                    newHeaders.set("Cross-Origin-Opener-Policy", "same-origin");

                    return new Response(response.body, {
                        status: response.status,
                        statusText: response.statusText,
                        headers: newHeaders,
                    });
                })
                .catch((e) => {
                    // Return a proper error Response instead of undefined
                    // (returning undefined causes "Failed to convert value to 'Response'")
                    console.warn("[COI] Fetch failed for:", event.request.url, e.message);
                    return new Response("Service Worker fetch failed", {
                        status: 502,
                        statusText: "Service Worker Fetch Failed",
                    });
                })
        );
    });
}
