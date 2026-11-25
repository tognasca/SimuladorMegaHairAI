const OFFLINE = "/offline.html";
const CACHE = "megahair-offline-v1";

self.addEventListener("install", (event) => {
    event.waitUntil(caches.open(CACHE).then((cache) => cache.add(OFFLINE)));
});

self.addEventListener("activate", (event) => {
    event.waitUntil((async () => {
        for (const name of await caches.keys()) {
            if (name.startsWith("megahair-offline-") && name !== CACHE) {
                await caches.delete(name);
            }
        }
    })());
});

self.addEventListener("message", (event) => {
    if (event.data === "ativar-versao") event.waitUntil(self.skipWaiting());
});

self.addEventListener("fetch", (event) => {
    const url = new URL(event.request.url);
    if (event.request.mode !== "navigate" || event.request.method !== "GET"
        || url.origin !== self.location.origin || url.search
        || !["/", "/login", "/captura", "/catalogo", "/clientes"].includes(url.pathname)) return;
    event.respondWith(fetch(event.request).catch(async () =>
        (await (await caches.open(CACHE)).match(OFFLINE)) || Response.error()));
});
