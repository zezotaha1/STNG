// Dedicated test-account registration/verification through the real Swagger UI.
// Input arrives on stdin; never log credentials, OTPs, request bodies or screenshots.
const fs = require('node:fs/promises');
const path = require('node:path');

async function main() {
  let input = '';
  for await (const chunk of process.stdin) input += chunk;
  const command = JSON.parse(input);
  const { chromium } = require(require.resolve('playwright', { paths: [process.env.STNG_PLAYWRIGHT_MODULES] }));
  const browser = await chromium.launch({ channel: 'msedge', headless: false });
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: false });
    const page = await context.newPage();
    await page.goto('https://zezotaha.ddns.net:44391/swagger/index.html', { waitUntil: 'domcontentloaded', timeout: 30000 });
    const operations = {
      register: { id: 'operations-ParentRegistration-post_api_auth_parent_register', path: '/api/auth/parent/register' },
      verify: { id: 'operations-ParentEmailVerification-post_api_auth_parent_verify_email', path: '/api/auth/parent/verify-email' }
    };
    const target = operations[command.action];
    if (!target) throw new Error('Unsupported account action');
    const operation = page.locator(`#${target.id}`);
    await operation.waitFor({ state: 'visible', timeout: 30000 });
    await operation.locator('.opblock-summary').click();
    await operation.getByRole('button', { name: 'Try it out', exact: true }).click();
    await operation.locator('textarea').fill(JSON.stringify(command.body));
    const received = page.waitForResponse(response => new URL(response.url()).pathname === target.path &&
      response.request().method() === 'POST', { timeout: 60000 });
    await operation.getByRole('button', { name: 'Execute', exact: true }).click();
    const response = await received;
    let body = null;
    try { body = await response.json(); } catch { /* Registration normally returns 204. */ }
    const result = { action: command.action, label: command.label, status: response.status(),
      verified: command.action === 'verify' ? body?.verified === true : undefined,
      errorCode: body?.error?.code ?? undefined, timestamp: new Date().toISOString() };
    const evidence = path.resolve(__dirname, '../../../artifacts/family-browser-e2e');
    await fs.mkdir(evidence, { recursive: true });
    await fs.writeFile(path.join(evidence, `${command.label}-${command.action}-result.json`), JSON.stringify(result, null, 2));
    console.log(JSON.stringify(result));
    if (response.status() >= 300 || (command.action === 'verify' && !result.verified)) process.exitCode = 1;
  } finally { await browser.close(); }
}

main().catch(() => { console.error('Account browser step failed; credentials and request details were suppressed.'); process.exitCode = 1; });
