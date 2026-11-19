const assert = require('node:assert/strict');
const { chromium, devices } = require('playwright');

(async () => {
    const browser = await chromium.launch({ headless: true });
    try {
        for (const [label, options] of [
            ['desktop', { viewport: { width: 1440, height: 900 } }],
            ['celular', devices['Pixel 7']],
            ['tablet', devices['iPad (gen 7)']],
        ]) {
            const context = await browser.newContext(options);
            const page = await context.newPage();
            const portrait = 'data:image/svg+xml,' + encodeURIComponent(
                '<svg xmlns="http://www.w3.org/2000/svg" width="100" height="150"><rect width="100" height="150" fill="purple"/></svg>');
            await page.setContent(`
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <div id="wrap" class="mh-comp-wrap">
                    <div class="mh-comp">
                        <div class="mh-comp-stage">
                            <img class="mh-comp-img mh-comp-antes" src="${portrait}" alt="Antes">
                            <img class="mh-comp-img mh-comp-depois" src="${portrait}" alt="Depois">
                        </div>
                        <div class="mh-comp-handle" role="slider" tabindex="0"></div>
                        <div class="mh-comp-tools">
                            <button data-acao="zoom-mais">Aproximar</button>
                            <button data-acao="restaurar">Restaurar</button>
                        </div>
                    </div>
                </div>`);
            await page.addStyleTag({ path: 'SimuladorMegaHair.Web/wwwroot/css/espelho.css' });
            await page.addScriptTag({ path: 'SimuladorMegaHair.Web/wwwroot/js/resultado.js' });

            const iniciar = () => page.evaluate(() => megaHairResultado.iniciarComparador('wrap'));
            assert.deepEqual(await iniciar(), { ok: true, desalinhado: false });
            assert.equal(await page.getByAltText('Depois').evaluate(img => getComputedStyle(img).objectFit), 'contain');
            const slider = page.getByRole('slider');
            await slider.focus();
            await slider.press('ArrowRight');
            assert.ok(Number(await slider.getAttribute('aria-valuenow')) > 50);

            const quadro = page.locator('.mh-comp');
            await page.getByRole('button', { name: 'Aproximar' }).click();
            assert.match(await quadro.getAttribute('class'), /mh-zoom/);
            await page.getByRole('button', { name: 'Restaurar' }).click();
            assert.doesNotMatch(await quadro.getAttribute('class'), /mh-zoom/);

            const depois = page.getByAltText('Depois');
            await depois.evaluate(img => { img.src = 'data:image/png;base64,abc'; });
            assert.equal((await iniciar()).ok, false);
            await depois.evaluate((img, src) => { img.src = src; }, portrait);
            assert.equal((await iniciar()).ok, true);

            await context.close();
            console.log(`Comparador aprovado em ${label}.`);
        }
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
