// Read-only shared-Development smoke probe. No credentials or database writes.
// Use an existing Playwright installation; no application dependency is added.
const fs = require('node:fs/promises');
const path = require('node:path');
const { randomUUID } = require('node:crypto');

async function main() {
  const moduleDirectory = process.env.STNG_PLAYWRIGHT_MODULES;
  if (!moduleDirectory) throw new Error('Set STNG_PLAYWRIGHT_MODULES to an existing Playwright package directory.');
  const { chromium } = require(require.resolve('playwright', { paths: [moduleDirectory] }));
  const evidence = path.resolve(__dirname, '../../../artifacts/family-browser-e2e');
  const oauthOnly = process.env.STNG_BROWSER_OAUTH_ONLY === '1';
  await fs.mkdir(evidence, { recursive: true });
  const browser = await chromium.launch({ channel: 'msedge', headless: false });
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: false, viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    const errors = [];
    const network = [];
    page.on('pageerror', error => errors.push({ type: 'pageerror', message: error.message }));
    page.on('console', message => {
      if (message.type() === 'error') errors.push({ type: 'console', message: message.text().slice(0, 1500) });
    });
    const relevantUrl = value => {
      const url = new URL(value);
      return ['zezotaha.ddns.net', 'localhost'].includes(url.hostname) ? url.origin + url.pathname : null;
    };
    page.on('response', response => {
      const url = relevantUrl(response.url());
      if (url) network.push({ url, status: response.status() });
    });
    page.on('requestfailed', request => {
      const url = relevantUrl(request.url());
      if (url) errors.push({ type: 'requestfailed', url, error: request.failure()?.errorText });
    });
    const response = await page.goto('https://zezotaha.ddns.net:44391/swagger/index.html', {
      waitUntil: 'domcontentloaded', timeout: 30000
    });
    let operationsReady = true;
    try { await page.locator('.opblock').first().waitFor({ state: 'visible', timeout: 30000 }); }
    catch { operationsReady = false; }
    const family = page.locator('.opblock-tag-section').filter({ has: page.locator('#operations-Family-get_api_families_mine') });
    const result = {
      timestamp: new Date().toISOString(),
      browser: `Microsoft Edge ${browser.version()}`,
      automation: 'Playwright; visible browser; isolated fresh context; TLS errors rejected',
      url: page.url(), status: response.status(), title: await page.title(), operationsReady,
      errors, network,
      visibleFamilyText: operationsReady ? await family.innerText() : (await page.locator('body').innerText()).slice(0, 1500),
      securitySchemes: operationsReady ? await page.evaluate(() => window.ui.specSelectors.specJson().toJS().components.securitySchemes) : null,
      operationIds: await page.locator('.opblock').evaluateAll(nodes => nodes.map(node => ({
        id: node.id,
        method: node.querySelector('.opblock-summary-method')?.textContent,
        path: node.querySelector('.opblock-summary-path')?.textContent?.trim()
      })).filter(operation => operation.id.startsWith('operations-Family-') || operation.id.startsWith('operations-ParentCurrentUser-'))),
      scenarios: []
    };
    await page.screenshot({ path: path.join(evidence, 'swagger-anonymous.png'), fullPage: false });
    if (operationsReady && oauthOnly) {
      await page.locator('.scheme-container').getByRole('button', { name: 'Authorize', exact: true }).click();
      const dialog = page.locator('.dialog-ux');
      await dialog.waitFor({ state: 'visible' });
      result.oauth = {
        completedLogin: false,
        clientId: await dialog.locator('input[type="text"]').inputValue(),
        visibleDialogText: (await dialog.innerText()).slice(0, 5000),
        controls: await dialog.locator('input, select, button').evaluateAll(nodes => nodes.map(node => ({
          tag: node.tagName, name: node.name, type: node.type,
          text: node.tagName === 'BUTTON' ? node.textContent.trim() : undefined
        })))
      };
    }
    if (operationsReady && !oauthOnly) {
      await family.screenshot({ path: path.join(evidence, 'swagger-family-endpoints.png') });
      const unknownId = randomUUID();
      async function executeGet(operationId, routePath, mode = 'anonymous') {
        const operation = page.locator(`#${operationId}`);
        if (!(await operation.getByRole('button', { name: 'Execute', exact: true }).count())) {
          await operation.locator('.opblock-summary').click();
          await operation.getByRole('button', { name: 'Try it out', exact: true }).click();
        }
        for (const input of await operation.locator('input').elementHandles()) {
          if ((await input.getAttribute('placeholder')) === 'id') await input.fill(unknownId);
        }
        // Select Swagger's documented JSON response media type (and therefore Accept).
        for (const select of await operation.locator('select').elementHandles()) {
          const values = await select.evaluate(node => [...node.options].map(option => option.value));
          if (values.includes('application/json')) await select.selectOption('application/json');
        }
        const received = page.waitForResponse(response => new URL(response.url()).pathname === routePath && response.request().method() === 'GET');
        const finalReceived = page.waitForResponse(response => {
          if (response.status() >= 300 && response.status() < 400) return false;
          let request = response.request();
          while (request) {
            if (new URL(request.url()).pathname === routePath && request.method() === 'GET') return true;
            request = request.redirectedFrom();
          }
          return false;
        });
        await operation.getByRole('button', { name: 'Execute', exact: true }).click();
        const response = await received;
        const finalResponse = await finalReceived;
        const status = finalResponse.status();
        result.scenarios.push({ name: `${mode}: GET ${routePath.replace(unknownId, '{unknown-id}')}`,
          accept: response.request().headers().accept, initialStatus: response.status(), status,
          finalPath: new URL(finalResponse.url()).pathname, expected: 401,
          accessDenied: status === 401,
          result: response.status() === 401 && status === 401 ? 'Passed' : 'Failed' });
        await operation.locator('.live-responses-table').waitFor({ state: 'visible' });
        if (operationId === 'operations-Family-get_api_families_mine' && mode === 'anonymous')
          await operation.screenshot({ path: path.join(evidence, 'swagger-anonymous-family-401.png'),
            mask: [operation.locator('pre').filter({ hasText: /RequestVerificationToken|Authorization/i })] });
      }
      await executeGet('operations-Family-get_api_families_mine', '/api/families/mine');
      await executeGet('operations-Family-get_api_families__id_', `/api/families/${unknownId}`);
      await executeGet('operations-Family-get_api_families__id__members', `/api/families/${unknownId}/members`);
      await executeGet('operations-Family-get_api_families__id__ownership_transfers', `/api/families/${unknownId}/ownership-transfers`);
      await executeGet('operations-ParentCurrentUser-get_api_auth_me', '/api/auth/me');
      await page.route('**/api/families/mine*', route => route.continue({
        headers: { ...route.request().headers(), authorization: 'Bearer deliberately-malformed-e2e-input' }
      }));
      await executeGet('operations-Family-get_api_families_mine', '/api/families/mine', 'malformed bearer');
      await page.unroute('**/api/families/mine*');
      const discoveryUrl = result.securitySchemes?.oidc?.openIdConnectUrl;
      result.scenarios.push({ name: 'Swagger OIDC discovery uses the public authority',
        discoveryUrl, expectedOrigin: new URL(page.url()).origin,
        result: discoveryUrl && new URL(discoveryUrl).origin === new URL(page.url()).origin ? 'Passed' : 'Failed' });
    }
    await fs.writeFile(path.join(evidence, oauthOnly ? 'oauth-browser-observation.json' : 'browser-observation.json'), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result, null, 2));
    if (!operationsReady || result.scenarios.some(scenario => scenario.result === 'Failed')) process.exitCode = 1;
  } finally {
    await browser.close();
  }
}

main().catch(error => { console.error(error.message); process.exitCode = 1; });
