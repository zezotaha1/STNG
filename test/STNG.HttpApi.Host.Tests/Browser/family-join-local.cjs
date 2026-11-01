// Isolated HTTPS Kestrel + disposable SQL Server + test email capture. No shared server.
// Secrets/OTPs/tokens use stdin or memory only; output contains assertion metadata.
const { createInterface } = require('node:readline');
const { randomUUID } = require('node:crypto');
const lines = createInterface({ input: process.stdin })[Symbol.asyncIterator]();
let step = 'initialization';
const record = (name, passed, status) => {
  console.log(JSON.stringify({ type: 'assertion', name, passed, ...(status === undefined ? {} : { status }) }));
  if (!passed) throw new Error('Browser assertion failed');
};
async function main() {
  const { root, accounts } = JSON.parse((await lines.next()).value);
  if (!/^https:\/\/localhost:\d+$/.test(root)) throw new Error('Loopback HTTPS required');
  const { chromium } = require(require.resolve('playwright', { paths: [process.env.STNG_PLAYWRIGHT_MODULES] }));
  const browser = await chromium.launch({ channel: 'msedge', headless: false });
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: false });
    const page = await context.newPage();
    await page.goto(root + '/swagger/index.html');
    await page.locator('.opblock').first().waitFor();
    record('Microsoft Edge loads isolated HTTPS Swagger with strict TLS', true);
    let token;
    await page.route('**/api/**', route => new URL(route.request().url()).origin === root && token
      ? route.continue({ headers: { ...route.request().headers(), authorization: `Bearer ${token}` } }) : route.continue());
    async function execute(name, method, template, params = {}, body, expected = 200) {
      step = name;
      const id = await page.evaluate(({ method, template }) => [...document.querySelectorAll('.opblock')]
        .find(op => op.querySelector('.opblock-summary-method')?.textContent.trim() === method &&
          op.querySelector('.opblock-summary-path')?.getAttribute('data-path') === template)?.id, { method, template });
      if (!id) throw new Error('Swagger operation missing');
      const op = page.locator(`[id="${id}"]`);
      if (!(await op.getByRole('button', { name: 'Execute', exact: true }).count())) {
        await op.locator('.opblock-summary').click();
        await op.getByRole('button', { name: 'Try it out', exact: true }).click();
      }
      for (const select of await op.locator('select').elementHandles()) {
        const values = await select.evaluate(node => [...node.options].map(x => x.value));
        if (values.includes('application/json') && await select.inputValue() !== 'application/json') await select.selectOption('application/json');
      }
      for (const input of await op.locator('input').elementHandles()) {
        const placeholder = await input.getAttribute('placeholder');
        if (placeholder) await input.fill(String(params[placeholder.toLowerCase()] ?? ''));
      }
      if (body !== undefined) await op.locator('textarea').fill(JSON.stringify(body));
      const path = template.replace(/\{(\w+)\}/g, (_, key) => params[key.toLowerCase()]);
      const pending = page.waitForResponse(r => new URL(r.url()).pathname === path && r.request().method() === method);
      await op.getByRole('button', { name: 'Execute', exact: true }).click();
      const response = await pending;
      record(name, response.status() === expected, response.status());
      if (response.status() === 204) return null;
      try { return await response.json(); } catch { throw new Error('Expected JSON response'); }
    }
    for (const account of accounts) {
      await execute(`Parent ${account.label} registers`, 'POST', '/api/auth/parent/register', {}, {
        email: account.email, fullName: `Browser Parent ${account.label}`, password: account.password,
        job: 'Test Engineer', dateOfBirth: '1990-01-01', phoneCountryIso2: 'EG', phoneNumber: '01012345678'
      }, 204);
      console.log(JSON.stringify({ type: 'otp', label: account.label }));
      const { otp } = JSON.parse((await lines.next()).value);
      await execute(`Parent ${account.label} verifies the actual captured email OTP`, 'POST', '/api/auth/parent/verify-email', {}, { email: account.email, otp });
      // Clear sensitive form fields immediately; no screenshots/traces are recorded.
      await page.locator('textarea').fill('').catch(() => {});
    }
    async function login(index) {
      const account = accounts[index]; step = `Parent ${account.label} login`;
      const result = await page.evaluate(async ({ email, password }) => {
        const response = await fetch('/connect/token', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: new URLSearchParams({ grant_type: 'password', client_id: 'STNG_Mobile', username: email, password,
            scope: 'openid profile email roles offline_access STNG' }) });
        const json = await response.json(); return { status: response.status, token: json.access_token };
      }, account);
      record(step, result.status === 200 && !!result.token, result.status); token = result.token;
      const csrf = await page.evaluate(async token => (await fetch('/abp/Swashbuckle/SetCsrfCookie', { headers: { Authorization: `Bearer ${token}` } })).status, token);
      record('Refresh normal Swagger antiforgery cookie', csrf === 204, csrf);
      return execute(`Parent ${account.label} identity`, 'GET', '/api/auth/me');
    }
    const get = (id, expected = 200) => execute('Family access', 'GET', '/api/families/{id}', { id }, undefined, expected);
    const create = () => execute('Create dedicated local Family', 'POST', '/api/families', {}, { requestId: randomUUID(), name: 'Isolated Browser Family' });
    const issue = (id, kind = 0) => execute('Issue one-time join credential', 'POST', '/api/families/{id}/join-credentials', { id }, { requestId: randomUUID(), kind });
    const submit = (code, requestId = randomUUID()) => execute('Submit pending join request', 'POST', '/api/family-join-requests', {}, { requestId, kind: code.credential.kind, secret: code.secret });
    const approve = request => execute('Approve request', 'POST', '/api/families/{id}/join-requests/{requestId}/approve',
      { id: request.familyId, requestid: request.id }, { concurrencyStamp: request.concurrencyStamp });
    const memberChange = async (id, parentid, action) => {
      const family = await get(id);
      return execute(`Membership ${action}`, 'POST', `/api/families/{id}/members/{parentId}/${action}`, { id, parentid }, { concurrencyStamp: family.concurrencyStamp });
    };
    const a = await login(0); const family = await create(); const code = await issue(family.id);
    const replay = await execute('Credential retry returns metadata only', 'POST', '/api/families/{id}/join-credentials', { id: family.id }, { requestId: code.credential.id, kind: 0 });
    record('No secret on credential retry', replay.secret == null && replay.joinLink == null);
    const b = await login(1); const pending = await submit(code); await submit(code, pending.id); await get(family.id, 404);
    const own = await execute('Requester sees own pending request', 'GET', '/api/me/family-join-requests');
    record('Pending has no active membership', own.items[0].status === 0 && own.items[0].parentId === b.parentId);
    await login(0);
    const review = await execute('Owner sees pending request', 'GET', '/api/families/{id}/join-requests', { id: family.id });
    record('Reviewer list scoped to pending Family request', review.totalCount === 1 && review.items[0].id === pending.id);
    await approve(pending); await approve(pending);
    await login(1); record('Approved applicant becomes Member', (await get(family.id)).myRole === 2);
    await execute('Member cannot manage credentials', 'POST', '/api/families/{id}/join-credentials', { id: family.id }, { requestId: randomUUID(), kind: 0 }, 403);
    await execute('Member cannot review requests', 'GET', '/api/families/{id}/join-requests', { id: family.id }, undefined, 403);
    const c = await login(2); await get(family.id, 404); const unrelated = await create(); const cRequest = await submit(code);
    await login(0);
    await execute('Reject separate applicant', 'POST', '/api/families/{id}/join-requests/{requestId}/reject',
      { id: family.id, requestid: cRequest.id }, { concurrencyStamp: cRequest.concurrencyStamp, reason: 'Isolated test rejection' });
    const qr = await issue(family.id, 1);
    record('QR payload carries only opaque token', qr.joinLink === 'stng://family/join?token=' + qr.secret && !qr.joinLink.includes(family.id));
    // Join a different Family with the QR credential to exercise the same workflow without rejection cooldown.
    const second = await create(); const secondQr = await issue(second.id, 1);
    await login(2); const cancelled = await submit(secondQr);
    await execute('Requester cancels QR request', 'POST', '/api/family-join-requests/{requestId}/cancel',
      { requestid: cancelled.id }, { concurrencyStamp: cancelled.concurrencyStamp });
    // Advance only the disposable host's injected clock through test-process IPC.
    console.log(JSON.stringify({ type: 'advance' })); await lines.next();
    const returning = await submit(qr);
    await login(0); await approve(returning);
    await memberChange(family.id, c.parentId, 'promote');
    await memberChange(family.id, b.parentId, 'promote');
    await login(1);
    const guardStamp = await get(family.id);
    await execute('Admin cannot remove another Admin', 'POST', '/api/families/{id}/members/{parentId}/remove',
      { id: family.id, parentid: c.parentId }, { concurrencyStamp: guardStamp.concurrencyStamp }, 403);
    await execute('Admin cannot remove Owner', 'POST', '/api/families/{id}/members/{parentId}/remove',
      { id: family.id, parentid: a.parentId }, { concurrencyStamp: guardStamp.concurrencyStamp }, 409);
    await login(0);
    await memberChange(family.id, c.parentId, 'remove');
    await login(2); await get(family.id, 404);
    const reactivation = await submit(qr);
    await login(0); await approve(reactivation);
    await login(2); record('Removed Admin reactivates only as Member', (await get(family.id)).myRole === 2);
    await login(0);
    const before = await get(family.id);
    const transfer = await execute('Owner initiates transfer to approved member', 'POST', '/api/families/{id}/ownership-transfers', { id: family.id },
      { requestId: randomUUID(), toParentId: b.parentId, concurrencyStamp: before.concurrencyStamp });
    await login(1); const fresh = await get(family.id);
    await execute('Designated recipient accepts transfer', 'POST', '/api/families/{id}/ownership-transfers/{transferId}/accept',
      { id: family.id, transferid: transfer.id }, { concurrencyStamp: fresh.concurrencyStamp });
    const members = await execute('Inspect ownership after transfer', 'GET', '/api/families/{id}/members', { id: family.id });
    record('Exactly one active Owner and previous Owner is Admin', members.items.filter(x => x.role === 0 && x.status === 0).length === 1 &&
      members.items.some(x => x.parentId === b.parentId && x.role === 0) && members.items.some(x => x.parentId === a.parentId && x.role === 1));
    await memberChange(family.id, a.parentId, 'demote');
    await login(0);
    await execute('Demotion immediately removes review rights', 'GET', '/api/families/{id}/join-requests', { id: family.id }, undefined, 403);
    const leftStamp = await get(family.id);
    await execute('Former Owner can leave after transfer', 'POST', '/api/families/{id}/leave', { id: family.id }, { concurrencyStamp: leftStamp.concurrencyStamp }, 204);
    await get(family.id, 404);
    await login(1); await get(unrelated.id, 404);
    await login(2); await get(second.id, 404);
    await login(1);
    const expiryFamily = await get(family.id);
    const expiringTransfer = await execute('Initiate transfer for expiry regression', 'POST', '/api/families/{id}/ownership-transfers', { id: family.id },
      { requestId: randomUUID(), toParentId: c.parentId, concurrencyStamp: expiryFamily.concurrencyStamp });
    await login(2);
    const staleAcceptance = await execute('Stale transfer acceptance is rejected', 'POST', '/api/families/{id}/ownership-transfers/{transferId}/accept',
      { id: family.id, transferid: expiringTransfer.id }, { concurrencyStamp: 'stale' }, 409);
    record('Stale acceptance reports the concurrency error', staleAcceptance.error?.code === 'STNG.Family:1005');
    console.log(JSON.stringify({ type: 'advance', minutes: 2881 })); await lines.next();
    const expiredFamily = await get(family.id);
    const expiredAcceptance = await execute('Expired transfer cannot change ownership', 'POST', '/api/families/{id}/ownership-transfers/{transferId}/accept',
      { id: family.id, transferid: expiringTransfer.id }, { concurrencyStamp: expiredFamily.concurrencyStamp }, 409);
    record('Expired acceptance reports the expiry error', expiredAcceptance.error?.code === 'STNG.Family:3003');
    await login(1); record('Expiry preserves the current Owner', (await get(family.id)).myRole === 0);
    record('All isolated browser assertions completed', true);
  } finally { await browser.close(); }
}
main().then(() => process.exit(0)).catch(() => {
  console.log(JSON.stringify({ type: 'assertion', name: step, passed: false })); process.exit(1);
});
