const assert = require('node:assert/strict');
const { chromium } = require('playwright');

(async () => {
    const browser = await chromium.launch({ headless: true });
    try {
        const origin = process.env.MEGAHAIR_TEST_WEB || 'http://127.0.0.1:5180';
        const context = await browser.newContext();
        const page = await context.newPage();
        page.setDefaultTimeout(20000);
        await page.goto(origin + '/login');
        const manifestUrl = await page.locator('link[rel=manifest]').getAttribute('href');
        const response = await context.request.get(new URL(manifestUrl, origin).href);
        assert.equal(response.status(), 200);
        const manifest = await response.json();
        assert.equal(manifest.display, 'standalone');
        assert.deepEqual(manifest.icons.map(icon => icon.sizes), ['192x192', '512x512']);
        for (const icon of manifest.icons) {
            const response = await context.request.get(new URL(icon.src, origin).href);
            assert.equal(response.status(), 200);
            assert.match(response.headers()['content-type'], /image\/png/);
        }

        await page.waitForFunction(async () => (await navigator.serviceWorker.getRegistration())?.active);
        await page.reload();
        await page.waitForFunction(() => navigator.serviceWorker.controller);
        const cachedPaths = await page.evaluate(async () => {
            const names = (await caches.keys()).filter(name => name.startsWith('megahair-offline-'));
            const requests = await Promise.all(names.map(async name => (await caches.open(name)).keys()));
            return requests.flat().map(request => new URL(request.url).pathname);
        });
        assert.deepEqual(cachedPaths, ['/offline.html']);
        await context.setOffline(true);
        await page.goto(origin + '/captura');
        await page.getByRole('heading', { name: 'Sem conexão com o MegaHair AI', exact: true }).waitFor();
        console.log('Manifesto, ícones, cache restrito e navegação offline verificados no Chromium.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
