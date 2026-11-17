const assert = require('node:assert/strict');
const { chromium } = require('playwright');

(async () => {
    const browser = await chromium.launch({ headless: true });
    try {
        const page = await browser.newPage();
        page.setDefaultTimeout(20000);
        await page.goto('http://127.0.0.1:5180/login?retorno=/admin/saloes');
        await page.locator('input[type=email]').fill('admin@example.com');
        await page.locator('input[type=password]').fill('TesteCI-12345678!');
        await page.getByRole('button', { name: 'ENTRAR', exact: true }).click();
        await page.getByRole('button', { name: 'Cadastrar salão' }).waitFor();
        for (const width of [360, 768, 1280]) {
            await page.setViewportSize({ width, height: 900 });
            assert(await page.getByRole('heading', { name: 'Salões', exact: true }).isVisible());
            await page.getByRole('button', { name: 'Cadastrar salão' }).click();
            await page.locator('#salao-nome').fill('Teste da tela ' + width);
            await page.locator('#salao-identificador').fill('tela-' + width);
            await page.locator('#salao-responsavel').fill('Operador da tela');
            await page.locator('#salao-email').fill('tela@example.com');
            await page.locator('#salao-whatsapp').fill('5511999999999');
            await page.getByRole('button', { name: 'Salvar cadastro', exact: true }).click();
            await page.getByText('Cadastro salvo.', { exact: true }).waitFor();
            await page.getByRole('cell', { name: 'Teste da tela ' + width, exact: false }).waitFor();
            await page.reload();
            await page.getByRole('cell', { name: 'Teste da tela ' + width, exact: false }).waitFor();
        }
        console.log('Login, cadastro pela tela e persistência aprovados em 360, 768 e 1280 pixels.');
    } finally {
        await browser.close();
    }
})().catch(error => { console.error(error); process.exitCode = 1; });
