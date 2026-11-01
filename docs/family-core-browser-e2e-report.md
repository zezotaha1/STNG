# Family Core browser E2E — Stage A

Date: **2026-10-30**, Africa/Cairo. Shared Development URL: **https://zezotaha.ddns.net:44391/swagger/index.html**. **The mandatory Stage A gate has passed after the IIS repair. This report accompanies the hosting/API fixes.** Earlier checkpoints are retained below as history.

## Post-IIS-repair verification — latest checkpoint

The user reported that Development was restored after repairing IIS web.config and applying site-scoped WebDAV removal. The effective remote IIS module/handler configuration was not read directly. The following browser results verify behavior rather than assuming the configuration change was sufficient.

- Microsoft Edge, Playwright, a fresh context, normal TLS validation, dedicated verified Parents and the existing dedicated test Families were used again.
- A fresh GET loaded Parent A's Family and its current concurrency stamp. **Authenticated `PUT /api/families/{id}` returned HTTP 200 with `application/json; charset=utf-8`**, a Family DTO, Owner role and a new concurrency stamp. A subsequent GET returned the new name and the same new stamp. The focused diagnostic recorded **7 Passed / 0 Failed** checks.
- The complete authenticated browser suite then recorded **60 Passed / 0 Failed** assertions. Rename succeeded; stale and invalid stamps returned 409/`STNG.Family:1005`; invalid names returned 400; failed writes preserved state; the unrelated owned Family remained unchanged; an unrelated Parent's PUT returned 404/`STNG.Family:1001`.
- That run also rechecked creation/replay, multiple Families per Parent, normalization, DTO/stamp contracts, mine/details/member/transfer reads, pagination/order, Owner restrictions, POST stamp checks and cross-family read/membership-write isolation.
- The anonymous/metadata rerun recorded **1 Passed / 6 Failed contract checks**. OIDC discovery remains public and correct. All six anonymous/malformed-bearer probes still deny access through **302 to `/Error`, then HTML 401**. This is the known Medium response-contract finding; the local API error-page fix remains **undeployed**. The IIS repair has not been presented as deployment of that code fix.

Latest full browser assertion inventory: **61 Passed / 6 Failed**, plus the separately reported focused PUT diagnostic. Latest grouped inventory: **10 Passed / 1 Failed / 3 Blocked**. The blocked groups are legitimate multi-member membership/role scenarios, ownership transfer with multiple members, and complete interactive Swagger authorization-code login. The first two are explicitly deferred to the isolated Phase 2 join workflow; Parent authentication was verified through the existing legitimate password grant in the actual browser. No interactive OAuth login is claimed from metadata alone.

The mandatory gate is ready to close: registration/verification evidence is retained, fresh login and authenticated Family create/read/update work, isolation is checked with both approved test Parents, and no Critical/High defect remains in those exercised paths. The remaining Medium contract issue and optional coverage gaps stay visible; this is not production certification.

The authenticated run finished at **17:08 Cairo time (15:08 UTC)**. Fresh captures/logs are in ignored `artifacts/family-browser-e2e/`; the previous 405 evidence was preserved under `before-iis-repair/` before new results were written. No credentials, OTPs or bearer tokens were included in the evidence.

Remote refs were fetched again. Branch `feat/family-join-requests` was created from latest `origin/develop` **`87726c565207d736c3d0eea3e50a13af94d15367`** in the same worktree. All seven existing local changed/untracked files were hash-checked across the switch: **zero changed or lost**. No stash, reset, rebase, commit, push, merge or deployment was performed.

## Previous resumed validation — pre-IIS-repair checkpoint

The user reported that the registration failure was caused by missing/invalid `EmailOtp:HashKey` configuration and that the server configuration had been corrected. The following results were executed afresh; they do not reuse the historical 165-test result as browser evidence.

### Registration and authentication

- Verification of the exact user-supplied mailbox returned HTTP 200 with `verified: true` at 13:59:02 UTC. Its password was not requested again or used for Family writes.
- Repeating the original dedicated Parent A alias registration returned **HTTP 204** at 14:00:21 UTC. Parent B's dedicated registration returned **HTTP 204** at 14:06:53 UTC. Both were verified through Swagger using OTPs supplied by the user: HTTP 200 and `verified: true` for A and B.
- Both dedicated Parents authenticated through the existing public `STNG_Mobile` password grant in the real browser, receiving HTTP 200. Their bearer-authenticated `/api/auth/me` requests returned HTTP 200 and distinct server-resolved Parent identities. Tokens remained in memory; passwords are retained only with Windows DPAPI protection in ignored local state files.
- Fresh public discovery now advertises issuer `https://zezotaha.ddns.net:44391/` and public authorization/token endpoints. The actual Swagger OIDC scheme now points to `https://zezotaha.ddns.net:44391/.well-known/openid-configuration`. **The previous loopback-authority finding is resolved in deployed configuration.** No public server address was hardcoded into local development defaults.
- The Family tests click Swagger Try it out/Execute. They use the legitimate bearer token from the normal Parent grant, scoped to this origin's Family and identity requests, and refresh Swagger's normal antiforgery cookie. This does not replace server authorization or modify the server's security checks.
- An exploratory generic ABP Account cookie-login call rejected the generated 48-character password because that separate endpoint advertises a 32-character maximum. The Parent registration/password-grant flow accepts it and works. No account password/security settings were changed. A complete Swagger authorization-code login has not been claimed from successful metadata loading or from the Parent password grant.

### Fresh Family browser results

The authenticated runner recorded **48 passed assertions and 1 failed assertion**. The anonymous/metadata runner recorded **1 passed and 6 failed contract assertions**. Five account registration/verification UI actions passed. Thus the fresh recorded browser checks total **54 Passed / 7 Failed**, with planned groups still blocked as listed below. Repeated login/replay assertions are counted as recorded checks, not as unique business rules. The previous grouped result of 5/8/9 uses a different inventory and remains historical.

| Fresh scenario group | Result | Evidence |
| --- | --- | --- |
| Registration fix | Passed | Both dedicated registrations 204; no repeated registration 500 |
| OTP delivery and email verification | Passed | User supplied delivered codes; both dedicated verifications and exact-address verification returned 200/true |
| Parent authentication and actor resolution | Passed | Token grant 200; identity 200; distinct server Parent IDs |
| External issuer and Swagger discovery | Passed | Public issuer/discovery URLs; Swagger metadata loaded in Edge |
| Family creation, normalization, Owner/stamp contract, replay and multiple Families | Passed | Creation/replay 200; same request ID returns the same Family; two distinct owned Families |
| Family reads, pagination, deterministic ordering and validation | Passed | Details/lists 200; page totals/order stable; invalid names/page maximum 400 |
| Initial ownership protections and mutation stamp checking | Passed | Exactly one active Owner; Owner leave/remove 409; stale stamp on POST 409 with `STNG.Family:1005` |
| Cross-family isolation | Passed | A/B see only their own Families; outside details/member/transfer reads and membership POST return 404 with `STNG.Family:1001` |
| Owner rename over PUT | **Failed — mandatory** | Deployed initial/final response 405, HTML, instead of 200 |
| Anonymous/malformed-bearer API response contract | **Failed** | Six requests still return 302 then HTML 401; access is denied. Local fix tested, not deployed |
| Successful rename followed by PUT-specific stale/invalid stamps, rejected-write state and outside PUT | Blocked | IIS/hosting PUT failure prevents these checks; POST stamp behavior was verified separately |
| Owner/Admin/Member membership lifecycle beyond Owner protections | Blocked — Requires Approved Membership Fixture | No direct add-member endpoint or database manipulation was used; exercise after legitimate joining exists |
| Ownership-transfer lifecycle with multiple members | Blocked — Requires Approved Membership Fixture | Same legitimate-fixture dependency |
| Complete Swagger authorization-code login | Blocked / not completed | Metadata/Parent grant success does not establish interactive Swagger OAuth completion |

Fresh grouped inventory: **8 Passed / 2 Failed / 4 Blocked**. These grouped statuses are separate from the raw assertion counts above.

Three clearly named test Families were created through public APIs: two owned by Parent A and one by Parent B, with the prefix `STNG Browser E2E 9ce78ca2`. They remain identifiable test fixtures; no real Family was read or modified and no database cleanup was performed directly. IDs/request IDs are retained in ignored `family-fixtures.json` to make retries safe.

### Remaining failure: deployed PUT is rejected

The real browser's `PUT /api/families/{id}` on Parent A's own fresh Family returns **HTTP 405, `text/html`**. A second focused diagnostic read the current Family/stamp and repeated PUT using the unchanged current name; it also returned 405. Successful GET/POST on the same Family and the deployed Swagger PUT operation were verified. No successful rename is claimed.

The site uses IIS and the repository's `web.config` already gives the ASP.NET Core handler all verbs. An inherited IIS/WebDAV handler is a plausible cause. **The remote generic response does not name its module/handler, and server logs are unavailable here, so WebDAV is not a verified root cause.** [Microsoft documents WebDAV interference as one cause of IIS PUT 405 and the module/handler removals for that case](https://learn.microsoft.com/en-us/troubleshoot/developer/webapps/iis/site-behavior-performance/http-error-405-website).

A site-scoped configuration change is prepared in `src/STNG.HttpApi.Host/web.config`: remove `WebDAVModule` and the `WebDAV` handler, retaining the existing ASP.NET Core handler, hosting model and security configuration. Local publish output was generated and inspected: both removals survive transformation, `verb="*"` remains, and hosting stays in-process. This is a reviewable remediation for the suspected cause, **not proof that the shared server is fixed**. The server operator was asked to review/apply it or provide the actual IIS 405 module/handler details. No deployment was performed because the continuation brief explicitly forbids deployment at this stage.

### Confirmed local fix: API errors preserve API status

`UseErrorPage()` outside Development reproduced the observed redirect in real ASP.NET Core middleware tests: **5 API/protocol cases failed before the fix; 2 browser-page cases passed**. The host now applies that HTML error-page middleware only outside `/api` and `/connect`, with case-insensitive segment matching. Authentication/authorization middleware and token/HTTPS validation are unchanged.

All **7 regression cases pass after the fix**: API/protocol 401/403/404/400 remain their original status with authentication headers preserved and no redirect; browser-page redirects remain intact. The fix is local in `STNGHttpApiHostModule.cs`, with `ApiErrorPageTests.cs` coverage. The shared deployment still shows its earlier behavior until the reviewed code is deployed through the authorized workflow.

### Fresh local verification

| Check | Result |
| --- | --- |
| Clean Release solution build | Passed, 0 errors; one pre-existing CS0162 warning in the host module |
| Domain | 39 passed |
| Application | 17 passed |
| EF Core | 40 passed |
| HTTP/SQL Server plus new middleware regressions | 76 passed |
| Total | **172 passed, 0 failed, 0 skipped** |
| EF migration consistency | No pending model changes |
| Local publish configuration inspection | WebDAV exclusions retained; ASP.NET Core all-verbs handler/in-process hosting retained |

Commands were `dotnet clean STNG.slnx --configuration Release`, `dotnet build STNG.slnx --configuration Release --no-restore`, and `dotnet test STNG.slnx --configuration Release --no-build --no-restore --logger 'trx;LogFileName=family-browser-resume.trx'`. The EF project passed `dotnet ef migrations has-pending-model-changes --configuration Release --no-build`. Only disposable fixture databases were migrated by tests. The TestBase support assembly contains no tests.

Sanitized fresh evidence is in ignored `artifacts/family-browser-e2e/`: account `*-result.json`, `authenticated-results.json`, `authenticated-browser.log`, `update-diagnostic-results.json`, `browser-observation.json`, `resume-*.log`, and the local publish preview. TRX reports are `test/*/TestResults/family-browser-resume.trx`. Historical captures remain in `previous-run/` and `parent-a-register-previous-500.json`. No OTP, password, valid bearer token, raw authenticated response body or credential screenshot is included in these logs/reports.

The initial authenticated automation attempt required corrections to media-type/body-entry ordering and Swagger antiforgery setup. Those exploratory 400 responses were not accepted as confirmed Family defects. The final successful creation/read run is the evidence above; the 405 remains reproducible after the request setup corrections.

### Current gate and files

The mandatory create/read/update gate **has not passed** because a successful deployed update remains unverified. Phase 2 is not implemented and `feat/family-join-requests` has not been created. No new entity, API or migration is claimed. The registration and external-issuer blockers from the previous run are resolved; the deployed PUT failure is the remaining mandatory blocker, with the response-contract fix pending normal deployment.

Current uncommitted scope: this report; the three browser scripts; `STNGHttpApiHostModule.cs`; `web.config`; and `ApiErrorPageTests.cs`. Worktree/branch remain `F:/STNG/STNG-family-core` / `feat/family-core`, HEAD `a9190e1`, with fetched develop `87726c5`. No files were staged, committed, pushed, merged or deployed. Original unrelated work and secrets remain preserved.

The original checkout's secrets file had already changed before this resumed run, so its old October 27 hash is no longer a valid preservation baseline. This session did not modify that file or restore older contents; its current fingerprint was recorded in ignored local evidence for subsequent preservation checks. No secret contents were printed.

## Historical initial run — superseded findings retained

The observations below began at 12:17 UTC; the original registration attempt returned 500 at 12:34:24 UTC. They explain the previous checkpoint and do not override the fresh results above.

## Environment and method

- Fetched `origin/develop`: `87726c565207d736c3d0eea3e50a13af94d15367`, the merge of [PR #14](https://github.com/zezotaha1/STNG/pull/14). GitHub confirms it was merged on 2026-10-30 at 01:24:44 UTC.
- [Development workflow 40985695777](https://github.com/zezotaha1/STNG/actions/runs/40985695777) completed successfully for that exact merge SHA. Both Build & Test and Deploy Development succeeded; the deployment's Run STNG DbMigrator and Deploy to development IIS steps succeeded. This verifies the workflow result, not authenticated browser behavior.
- The DDNS name resolves. Normal TLS-validated requests to Swagger and `/health-status` returned HTTP 200. No certificate validation was disabled and no TLS interstitial was bypassed.
- Actual visible **Microsoft Edge 156.0.4314.50**, driven by the existing bundled Playwright installation, using a fresh isolated browser context and `ignoreHTTPSErrors: false`. Swagger rendered its OpenAPI document and all **14 Family operations**.
- The built-in browser-control and Node REPL tools both failed before initialization with `failed to write kernel assets: The system cannot find the path specified. (os error 3)`. Testing continued through installed Playwright using Node directly. This was real browser execution, not an API-only substitute.
- The probe clicked Swagger operation summaries, Try it out and Execute. It recorded browser network responses and redirect chains. JSON was selected in Swagger's response media-type control. A malformed bearer value was added only to the designated read request through Playwright routing; no valid credential was fabricated or used.
- Read-only diagnostics also inspected the public Swagger/discovery documents and source configuration. Those diagnostics supplement the browser tests.

No approved dedicated Parent credentials or membership fixtures were found in the inspected repository configuration. The user subsequently authorized creating fresh Parents, supplied a controlled Gmail inbox and offered to provide the emailed OTP. A unique STNG test alias of that inbox was used for one registration attempt through Swagger. Its password was generated locally and retained with Windows DPAPI protection in an ignored local state file. No password or OTP was logged or included in screenshots.

That registration returned **HTTP 500**, so successful account creation/persistence and OTP delivery are unconfirmed. The browser was closed; no blind retry, duplicate account creation or resend was attempted. The user was asked whether an OTP arrived despite the error or, otherwise, for the sanitized server exception. No Family was created/modified, and no shared database was accessed directly.

## Scenario inventory

**22 grouped checks: 5 Passed, 8 Failed, 9 Blocked.** Grouped checks deliberately do not claim that all individual business-rule cases were executed. Seven browser contract checks and one authorized registration attempt failed for the issues described below. All six authorization probes ultimately denied access with 401; their failures concern the initial API status/HTML redirect contract.

| ID | Scenario | Result | Actual evidence |
| --- | --- | --- | --- |
| P1 | Latest develop and PR #14 integration | Passed | `87726c5`; PR merged |
| P2 | Matching CI, migration and Development deployment | Passed | Matching-SHA workflow/jobs/steps succeeded |
| P3 | DNS, TLS and deployed health | Passed | DNS resolves; TLS accepted normally; health HTTP 200 |
| P4 | Swagger loads in a real browser | Passed | Document and OpenAPI JSON HTTP 200; rendered Swagger UI |
| P5 | Family API documentation present | Passed | 14 Family operations, DTO schemas and configured OIDC scheme observed |
| F1 | Anonymous `GET /api/families/mine`: direct API 401 | Failed | Initial 302, redirected `/Error` response 401, HTML |
| F2 | Anonymous `GET /api/families/{unknown-id}`: direct API 401 | Failed | Initial 302, final 401, HTML |
| F3 | Anonymous `GET /api/families/{unknown-id}/members`: direct API 401 | Failed | Initial 302, final 401, HTML |
| F4 | Anonymous `GET /api/families/{unknown-id}/ownership-transfers`: direct API 401 | Failed | Initial 302, final 401, HTML |
| F5 | Anonymous `GET /api/auth/me`: direct API 401 | Failed | Initial 302, final 401, HTML |
| F6 | Malformed bearer, `GET /api/families/mine`: direct API 401 | Failed | Initial 302, final 401, HTML; invalid authentication denied |
| F7 | Swagger OIDC discovery uses the public authority | Failed | Advertised localhost URL; browser `ERR_CONNECTION_REFUSED` |
| F8 | Fresh dedicated Parent registration through Swagger | Failed | `POST /api/auth/parent/register` returned HTTP 500 at 12:34:24 UTC |
| B1 | Fresh Parent verification/login, eligibility, valid bearer and server-resolved actor | Blocked | Registration failed; OTP/account state unconfirmed; Swagger discovery also fails |
| B2 | Valid creation, normalized name, automatic Owner, IDs/stamp and multiple Families | Blocked | Requires approved dedicated Parent and authorized test Family creation |
| B3 | Empty/long names and creation request-ID idempotency/replay | Blocked | Same prerequisite; no shared writes attempted |
| B4 | Authenticated mine/details/members/transfers contracts, pagination and ordering | Blocked | Requires approved dedicated accounts and test Families |
| B5 | Authenticated cross-family isolation and unknown/inaccessible 404 equivalence | Blocked | Requires at least two approved actors; anonymous 401 cannot establish this |
| B6 | Rename, invalid names, invalid/stale stamps, unrelated Families unchanged | Blocked | Requires approved Family mutation scope |
| B7 | Invalid password or genuinely expired approved credentials | Blocked | No successfully verified test Parent/expired test credential; account security state was not altered |
| B8 | Owner/Admin/Member permissions, promotion/demotion/removal/leave and Owner restrictions | Blocked — Requires Approved Membership Fixture | No legitimate approved multi-member fixture available |
| B9 | Ownership initiation/visibility/acceptance, exact Owner count, replay/expiry/stale stamps and cross-family restrictions | Blocked — Requires Approved Membership Fixture | No legitimate approved multi-member fixture available |

The unknown GUIDs were generated solely as read-only probe inputs; they do not identify an approved or real test Family. Family POST/PUT operations were inspected, not executed. The single Parent registration POST was expressly authorized and used the public endpoint.

## Confirmed findings

### High — deployment blocker: Swagger advertises a loopback OIDC authority

The deployed Swagger document contains:

```json
{"oidc":{"type":"openIdConnect","openIdConnectUrl":"https://localhost:44391/.well-known/openid-configuration"}}
```

The actual browser attempts that address and receives `net::ERR_CONNECTION_REFUSED`. Localhost addresses the browser machine rather than the DDNS deployment. The public discovery endpoint, `https://zezotaha.ddns.net:44391/.well-known/openid-configuration`, returns HTTP 200 but advertises issuer `https://localhost:44391/`; its authorization/token endpoints use the public DDNS origin.

Reproduction: open the development Swagger URL in a fresh browser context, wait for its Family operations, inspect its OIDC security scheme and failed discovery request. No login or secret is necessary. The normal Swagger OIDC setup is unusable from this test browser. Successful Parent authentication and token interoperability remain unverified; this observation does not establish that every direct token flow is broken.

Relevant source: `STNGHttpApiHostModule.cs` uses `AuthServer:Authority` for both `SetIssuer` and `AddAbpSwaggerGenWithOidc`. The checked-in host appsettings default is localhost; its Development file does not override Authority. The deployed metadata confirms the effective mismatch, but this review did not inspect or change the server's secret/environment configuration. These host settings predate Family Core.

Recommended remediation: supply the intended public Authority through the deployment configuration, align the Swagger client/redirect and issuer settings through the normal review/deployment workflow, then repeat browser discovery and approved Parent authentication. Do not globally replace local-development defaults or bypass TLS.

### Medium — API authentication errors redirect to an HTML error page

All six Swagger read probes returned an initial **302** with `Location: /Error?httpStatusCode=401`, followed by an **HTTP 401, `text/html; charset=utf-8`** response. The same initial redirect was independently reproduced with JSON and plain-text Accept headers. No Family data was returned, so these observations do not show an authorization bypass.

Reproduction: in Swagger, expand GET `/api/families/mine`, click Try it out, select `application/json`, then Execute without authorization. Inspect both the original request and its redirect; looking only at Swagger's displayed final 401 hides the contract difference. A malformed bearer is also rejected through this redirect chain.

The documented/tested API behavior is direct 401. Existing host tests force Development; source enables `UseErrorPage()` outside Development. That environment difference is a plausible explanation, not a verified statement about the deployed process environment. Investigate API error-page handling under the actual deployment environment and add representative HTTP coverage before changing middleware. Do not weaken authorization or treat the redirected HTML response as the expected JSON API contract.

### High — mandatory authentication setup blocked: fresh Parent registration returns 500

After the user's authorization, the browser opened the ParentRegistration Swagger operation, clicked Try it out, filled its JSON body and clicked Execute. The payload used a unique test alias of the approved inbox, a locally generated strong password, a clearly labeled QA name/job, an adult test date of birth and a US 202-555-01xx example phone number. The original POST response was **HTTP 500**, rather than the expected 204. No credentials or request body were printed. Sanitized evidence is `artifacts/family-browser-e2e/parent-a-register-result.json`.

The test did not mark the account created, ask the user to bypass verification, read an OTP from the database, or weaken email eligibility. The dedicated email/password state is retained locally for controlled recovery rather than creating more accounts blindly. Parent A's registration state is unknown until a legitimate verification response or server evidence establishes it.

Server logs are unavailable in this workspace (`C:/MySites/STNG` from the deployment workflow is not present). Source inspection shows that registration requires a valid secret `EmailOtp:HashKey` of at least 32 decoded bytes, and a missing/invalid value throws during OTP creation. The checked-in default is empty, while test fixtures inject a valid key. **Missing deployed OTP configuration is only a diagnostic hypothesis; no runtime secret value or server exception was available to confirm the cause.** Other persistence/configuration failures remain possible. The existing registration transaction and SMTP exception handling do not justify assuming either account persistence or email delivery from an HTTP 500 alone.

Required next evidence is the sanitized server exception for the POST at 2026-10-30 12:34:24 UTC, or an actually delivered OTP to verify through the normal endpoint. No server configuration or deployment was changed.

## Evidence and reproduction

### Browser scripts and local captures

The read-only probe is `test/STNG.HttpApi.Host.Tests/Browser/family-core-stage-a.cjs`. It uses an existing Playwright installation without adding an application dependency. It returns a nonzero exit code for a contract failure; the final run failed as expected on the seven contract checks above. It closes its isolated browser in a finally block. `family-core-account.cjs` handles the expressly authorized registration/verification UI actions with sensitive input on stdin; it emits only sanitized status, label and verification/error-code fields and captures no account screenshots.

```powershell
$env:STNG_PLAYWRIGHT_MODULES = '<existing Node module directory containing playwright>'
node test/STNG.HttpApi.Host.Tests/Browser/family-core-stage-a.cjs
```

Local evidence is intentionally ignored under `artifacts/family-browser-e2e/`: `browser-observation.json`, `stage-a-browser.log`, and screenshots. Query strings, request headers, credentials and response bodies are not written to the JSON/log. Third-party browser-injected network paths are excluded. The malformed bearer is a fixed invalid test input, not a usable token.

Sanitized local screenshots:

- `swagger-family-endpoints.png`: rendered Family API operations.
- `swagger-anonymous-family-401.png`: Swagger's displayed final 401 and HTML response; the curl block containing the anonymous anti-forgery value is masked. An earlier unmasked diagnostic copy was removed/replaced and is not a deliverable.

![Deployed Family operations](../artifacts/family-browser-e2e/swagger-family-endpoints.png)

![Anonymous denial with request token masked](../artifacts/family-browser-e2e/swagger-anonymous-family-401.png)

An early navigation wait used network-idle and timed out; the probe was corrected to wait for DOM content and rendered operations. This automation-wait issue is not counted as an application regression.

## Gate, remaining work and Git state

Stage A is **not passed**. Successful Parent registration/verification/authentication, Family creation, authenticated reads, update/concurrency and cross-family isolation remain unverified. The registration failure, OIDC deployment blocker and API error-contract discrepancy remain unresolved. The successful historical 165-test run and matching CI/deployment success do not satisfy these browser prerequisites.

Next steps:

1. Investigate the registration 500 from the server exception. If an OTP actually arrived, verify it through the legitimate endpoint before deciding whether a resend/retry is needed; do not create further accounts blindly.
2. Correct the deployed Authority through the normal workflow and investigate the API error-page behavior; rerun the browser probe. The user has already authorized fresh dedicated test Parents and controlled-inbox OTP verification; do not ask for that authorization again.
3. Execute the mandatory authenticated smoke tests. Mark extended membership/ownership scenarios blocked until approved legitimate fixtures exist.
4. Only after the Stage A gate passes, create `feat/family-join-requests` from fresh develop, load the required DDD/persistence skills and implement Phase 2. Its later browser workflow must use a safe isolated environment, with no automatic shared deployment.

No Phase 2 entities, domain behavior, DTOs/APIs, migration or branch were created. No Stage B automated tests, clean build or Phase 2 browser results are claimed. No .NET source behavior changed during this session.

Current worktree: `F:/STNG/STNG-family-core`, branch `feat/family-core`, HEAD `a9190e1056192eec1dec6cf258457b4fd3e8969c`. Its committed tree matches fetched develop's merged tree. The new report and two browser scripts remain untracked/uncommitted. Sensitive account state and sanitized captures are ignored. Nothing was staged, committed, pushed, merged or deployed in this task. The original checkout and its secrets edit were preserved.

## Local hosting PR preparation — 2026-11-01

The hosting/API changes are isolated from Family Join Requests. Fresh scoped verification is recorded in the local PR preparation report. Historical evidence above retains its original dates; no deployment or shared-database change occurred during preparation.

Fresh isolated verification on 2026-11-01: Release build passed (0 errors, existing CS0162 warning); **172 tests passed, 0 failed, 0 skipped** (Domain 39, Application 17, EF 40, HTTP 76). The seven API/protocol/browser-page error regressions pass. EF reports no pending model changes. XML checks confirm site-scoped WebDAV exclusions with the ASP.NET Core all-verbs/in-process handler and disabled stdout logging preserved. Effective remote IIS configuration and deployed responses were not inspected during this preparation.
