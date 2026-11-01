// Real-browser Family smoke tests using only dedicated, verified test Parents.
// Credentials arrive on stdin; tokens stay in memory. No HAR/trace/secret screenshots.
const fs = require('node:fs/promises');
const path = require('node:path');
const { randomUUID } = require('node:crypto');

async function main() {
  let input = '';
  for await (const chunk of process.stdin) input += chunk;
  const accounts = JSON.parse(input);
  const { chromium } = require(require.resolve('playwright', { paths: [process.env.STNG_PLAYWRIGHT_MODULES] }));
  const root = 'https://zezotaha.ddns.net:44391';
  const evidence = path.resolve(__dirname, '../../../artifacts/family-browser-e2e');
  const statePath = path.join(evidence, 'family-fixtures.json');
  const updateOnly = process.env.STNG_BROWSER_UPDATE_ONLY === '1';
  const reportPath = path.join(evidence, updateOnly ? 'update-diagnostic-results.json' : 'authenticated-results.json');
  await fs.mkdir(evidence, { recursive: true });
  let state;
  try { state = JSON.parse(await fs.readFile(statePath, 'utf8')); }
  catch (error) {
    if (error.code !== 'ENOENT') throw error;
    state = { run: randomUUID().slice(0, 8), families: {} };
  }
  const results = [];
  let step = 'browser initialization';
  const browser = await chromium.launch({ channel: 'msedge', headless: false });
  async function record(name, passed, detail = {}) {
    results.push({ name, result: passed ? 'Passed' : 'Failed', ...detail });
    await fs.writeFile(reportPath, JSON.stringify({ timestamp: new Date().toISOString(), browser: browser.version(), results }, null, 2));
    console.log(JSON.stringify(results.at(-1)));
    if (!passed) throw new Error('A browser assertion failed');
  }
  try {
    const context = await browser.newContext({ ignoreHTTPSErrors: false, viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    await page.goto(root + '/swagger/index.html', { waitUntil: 'domcontentloaded', timeout: 30000 });
    await page.locator('#operations-Family-get_api_families_mine').waitFor({ state: 'visible', timeout: 30000 });
    let token;
    await page.route('**/api/**', route => {
      const url = new URL(route.request().url());
      if (url.origin === root && (url.pathname.startsWith('/api/families') || url.pathname === '/api/auth/me'))
        return route.continue({ headers: { ...route.request().headers(), authorization: `Bearer ${token}` } });
      return route.continue();
    });

    async function login(account) {
      step = `${account.label}: login`;
      // The existing STNG_Mobile password grant is the public Parent login flow.
      // This POST executes in the real browser; its credential/token body is never logged.
      const response = await page.evaluate(async ({ email, password }) => {
        const response = await fetch('/connect/token', { method: 'POST', headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
          body: new URLSearchParams({ grant_type: 'password', client_id: 'STNG_Mobile', username: email,
            password, scope: 'openid profile email roles offline_access STNG' }) });
        const body = await response.json();
        return { status: response.status, token: body.access_token, error: body.error };
      }, { email: account.email, password: account.password });
      await record(step, response.status === 200 && typeof response.token === 'string', { status: response.status });
      token = response.token;
      // Refresh Swagger's normal antiforgery cookie as the same bearer identity.
      const csrfStatus = await page.evaluate(async access => (await fetch('/abp/Swashbuckle/SetCsrfCookie', {
        credentials: 'same-origin', headers: { Authorization: `Bearer ${access}` }
      })).status, token);
      await record(`${account.label}: refresh Swagger antiforgery`, csrfStatus === 204, { status: csrfStatus });
    }

    async function execute(name, operationId, method, routePath, params = {}, body, expected = 200, errorCode) {
      step = name;
      const operation = page.locator(`#${operationId}`);
      if (!(await operation.getByRole('button', { name: 'Execute', exact: true }).count())) {
        await operation.locator('.opblock-summary').click();
        await operation.getByRole('button', { name: 'Try it out', exact: true }).click();
      }
      // Changing Swagger's request media type resets its example body. Select it
      // before entering the actual payload and avoid redundant change events.
      for (const select of await operation.locator('select').elementHandles()) {
        const values = await select.evaluate(node => [...node.options].map(option => option.value));
        if (values.includes('application/json') && await select.inputValue() !== 'application/json') await select.selectOption('application/json');
      }
      for (const input of await operation.locator('input').elementHandles()) {
        const placeholder = await input.getAttribute('placeholder');
        if (placeholder) await input.fill(String(params[placeholder.toLowerCase()] ?? ''));
      }
      if (body !== undefined) await operation.locator('textarea').fill(JSON.stringify(body));
      const initial = page.waitForResponse(response => new URL(response.url()).pathname === routePath && response.request().method() === method);
      const final = page.waitForResponse(response => {
        if (response.status() >= 300 && response.status() < 400) return false;
        let request = response.request();
        while (request) {
          if (new URL(request.url()).pathname === routePath && request.method() === method) return true;
          request = request.redirectedFrom();
        }
        return false;
      });
      await operation.getByRole('button', { name: 'Execute', exact: true }).click();
      const first = await initial;
      const response = await final;
      let json = null;
      const responseText = await response.text();
      try { json = JSON.parse(responseText); } catch { /* A no-content or HTML response stays null. */ }
      await record(name, first.status() === expected && response.status() === expected && (!errorCode || json?.error?.code === errorCode),
        { initialStatus: first.status(), status: response.status(), expected, contentType: response.headers()['content-type'], errorCode: json?.error?.code ?? undefined,
          ...(response.status() !== expected ? { contentType: response.headers()['content-type'],
            validationFields: Object.keys(json?.errors ?? {}),
            validationMembers: json?.error?.validationErrors?.flatMap(error => error.members ?? []),
            submittedFields: Object.keys(first.request().postDataJSON() ?? {}),
            webDavError: /WebDAV/i.test(responseText), iis405: /HTTP Error 405/i.test(responseText),
            antiforgeryError: /antiforgery|anti-forgery|csrf/i.test(responseText) } : {}) });
      return json;
    }
    const ids = {
      create: 'operations-Family-post_api_families', mine: 'operations-Family-get_api_families_mine',
      details: 'operations-Family-get_api_families__id_', update: 'operations-Family-put_api_families__id_',
      members: 'operations-Family-get_api_families__id__members', transfers: 'operations-Family-get_api_families__id__ownership_transfers',
      leave: 'operations-Family-post_api_families__id__leave', remove: 'operations-Family-post_api_families__id__members__parentId__remove',
      me: 'operations-ParentCurrentUser-get_api_auth_me'
    };
    async function create(label, actorId) {
      if (!state.families[label]) {
        state.families[label] = { requestId: randomUUID(), name: `STNG Browser E2E ${state.run} ${label}` };
        await fs.writeFile(statePath, JSON.stringify(state, null, 2));
      }
      const fixture = state.families[label];
      const family = await execute(`${label}: create/replay`, ids.create, 'POST', '/api/families', {},
        { requestId: fixture.requestId, name: `  ${fixture.name}  ` });
      await record(`${label}: normalized name, Owner and concurrency contract`, family.name === family.name.trim() && family.myRole === 0 &&
        family.createdByParentId === actorId && typeof family.concurrencyStamp === 'string' && family.concurrencyStamp.length > 0);
      if (fixture.id && fixture.id !== family.id) throw new Error('Fixture identity changed');
      fixture.id = family.id;
      await fs.writeFile(statePath, JSON.stringify(state, null, 2));
      return family;
    }
    const details = (name, id, status = 200) => execute(name, ids.details, 'GET', `/api/families/${id}`, { id }, undefined, status,
      status === 404 ? 'STNG.Family:1001' : undefined);

    await login(accounts[0]);
    if (updateOnly) {
      const id = state.families.A1.id;
      const current = await details('Dedicated Family before update diagnostic', id);
      const targetName = state.families.A1.name + ' PUT Retest';
      const updated = await execute('Dedicated Family PUT diagnostic', ids.update, 'PUT', `/api/families/${id}`, { id },
        { name: targetName, concurrencyStamp: current.concurrencyStamp });
      await record('PUT returned the Family JSON contract and a new stamp', updated.id === id && updated.name === targetName &&
        updated.myRole === 0 && updated.concurrencyStamp !== current.concurrencyStamp);
      const persisted = await details('Dedicated Family after successful PUT', id);
      await record('PUT rename and stamp persisted', persisted.name === targetName && persisted.concurrencyStamp === updated.concurrencyStamp);
      return;
    }
    const actorA = await execute('Parent A: authenticated identity', ids.me, 'GET', '/api/auth/me');
    await record('Parent A: server-resolved Parent identity', typeof actorA.parentId === 'string' && actorA.email === accounts[0].email);
    const a = await create('A1', actorA.parentId);
    const replay = await create('A1', actorA.parentId);
    await record('Creation request replay returns the same Family', replay.id === a.id);
    const a2 = await create('A2', actorA.parentId);
    await record('One Parent can own multiple distinct Families', a.id !== a2.id);
    for (const name of ['', '   ', 'x'.repeat(121)])
      await execute('Family name validation', ids.create, 'POST', '/api/families', {}, { requestId: randomUUID(), name }, 400);

    const mine = await execute('Parent A: My Families', ids.mine, 'GET', '/api/families/mine', { maxresultcount: 100 });
    await record('My Families contains only the two test Families', mine.totalCount === 2 && mine.items.length === 2 &&
      mine.items.every(item => [a.id, a2.id].includes(item.id) && item.myRole === 0));
    const page1 = await execute('My Families: first page', ids.mine, 'GET', '/api/families/mine', { maxresultcount: 1 });
    const page2 = await execute('My Families: second page', ids.mine, 'GET', '/api/families/mine', { skipcount: 1, maxresultcount: 1 });
    const repeat = await execute('My Families: repeat first page', ids.mine, 'GET', '/api/families/mine', { maxresultcount: 1 });
    await record('Pagination totals and deterministic ordering', page1.totalCount === 2 && page2.totalCount === 2 &&
      page1.items.length === 1 && page2.items.length === 1 && page1.items[0].id !== page2.items[0].id && repeat.items[0].id === page1.items[0].id);
    await execute('Pagination maximum validation', ids.mine, 'GET', '/api/families/mine', { maxresultcount: 101 }, undefined, 400);

    let current = await details('Parent A: Family details', a.id);
    const members = await execute('Parent A: members list', ids.members, 'GET', `/api/families/${a.id}/members`, { id: a.id, maxresultcount: 1 });
    await record('Exactly one active Owner at creation', members.totalCount === 1 && members.items[0].parentId === actorA.parentId &&
      members.items[0].role === 0 && members.items[0].status === 0);
    const empty = await execute('Members: page beyond first', ids.members, 'GET', `/api/families/${a.id}/members`,
      { id: a.id, skipcount: 1, maxresultcount: 1 });
    await record('Member pagination retains total and bounds results', empty.totalCount === 1 && empty.items.length === 0);
    const transfers = await execute('Parent A: retained transfer list', ids.transfers, 'GET', `/api/families/${a.id}/ownership-transfers`, { id: a.id });
    await record('New Family transfer contract', transfers.totalCount === 0 && transfers.items.length === 0);

    await execute('Owner cannot leave', ids.leave, 'POST', `/api/families/${a.id}/leave`, { id: a.id },
      { concurrencyStamp: current.concurrencyStamp }, 409, 'STNG.Family:2005');
    await execute('Owner cannot be removed', ids.remove, 'POST', `/api/families/${a.id}/members/${actorA.parentId}/remove`,
      { id: a.id, parentid: actorA.parentId }, { concurrencyStamp: current.concurrencyStamp }, 409, 'STNG.Family:2006');
    await execute('Stale stamp is rejected before an Owner mutation', ids.leave, 'POST', `/api/families/${a.id}/leave`, { id: a.id },
      { concurrencyStamp: 'invalid-stamp' }, 409, 'STNG.Family:1005');
    await details('Unknown Family is inaccessible', randomUUID(), 404);

    await login(accounts[1]);
    const actorB = await execute('Parent B: authenticated identity', ids.me, 'GET', '/api/auth/me');
    await record('Parents resolve to distinct server identities', actorB.parentId !== actorA.parentId);
    const b = await create('B1', actorB.parentId);
    await details('Parent B cannot read Parent A Family', a.id, 404);
    await execute('Parent B cannot list Parent A members', ids.members, 'GET', `/api/families/${a.id}/members`, { id: a.id }, undefined, 404, 'STNG.Family:1001');
    await execute('Parent B cannot list Parent A transfers', ids.transfers, 'GET', `/api/families/${a.id}/ownership-transfers`, { id: a.id }, undefined, 404, 'STNG.Family:1001');
    await execute('Parent B cannot mutate Parent A membership', ids.leave, 'POST', `/api/families/${a.id}/leave`, { id: a.id },
      { concurrencyStamp: current.concurrencyStamp }, 404, 'STNG.Family:1001');
    const bMine = await execute('Parent B: scoped My Families', ids.mine, 'GET', '/api/families/mine', { maxresultcount: 100 });
    await record('Parent B sees only its own Family', bMine.totalCount === 1 && bMine.items[0].id === b.id);
    await login(accounts[0]);
    await details('Parent A cannot read Parent B Family', b.id, 404);

    const oldStamp = current.concurrencyStamp;
    current = await execute('Owner renames Family', ids.update, 'PUT', `/api/families/${a.id}`, { id: a.id },
      { name: state.families.A1.name + ' Renamed', concurrencyStamp: oldStamp });
    await record('Successful update changes the concurrency stamp', current.concurrencyStamp !== oldStamp);
    await execute('Stale update is rejected', ids.update, 'PUT', `/api/families/${a.id}`, { id: a.id },
      { name: 'Must not persist', concurrencyStamp: oldStamp }, 409, 'STNG.Family:1005');
    await execute('Invalid stamp is rejected', ids.update, 'PUT', `/api/families/${a.id}`, { id: a.id },
      { name: 'Must not persist', concurrencyStamp: 'invalid-stamp' }, 409, 'STNG.Family:1005');
    await execute('Invalid update name is rejected', ids.update, 'PUT', `/api/families/${a.id}`, { id: a.id },
      { name: '', concurrencyStamp: current.concurrencyStamp }, 400);
    const after = await details('Family after rejected updates', a.id);
    const other = await details('Unrelated owned Family after updates', a2.id);
    await record('Rejected writes and unrelated Family remain unchanged', after.name === current.name && after.concurrencyStamp === current.concurrencyStamp &&
      other.name === a2.name && other.concurrencyStamp === a2.concurrencyStamp);
    await login(accounts[1]);
    await execute('Parent B cannot rename Parent A Family', ids.update, 'PUT', `/api/families/${a.id}`, { id: a.id },
      { name: 'Must not persist', concurrencyStamp: current.concurrencyStamp }, 404, 'STNG.Family:1001');
    await record('Fresh authenticated Family smoke completed', true, { createdFamilies: 3 });
  } catch {
    console.error(JSON.stringify({ result: 'Stopped', step, detail: 'Sensitive values suppressed; inspect sanitized results.' }));
    process.exitCode = 1;
  } finally { await browser.close(); }
}

main().catch(() => { console.error('Browser setup failed; sensitive details suppressed.'); process.exitCode = 1; });
