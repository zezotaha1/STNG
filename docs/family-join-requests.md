# Family Join Requests — Phase 2

> Local PR preparation (2026-11-01): this document retains the original implementation/review checkpoints. The clean Phase 2 branch excludes IIS/WebDAV and API error-page changes. Fresh verification and the local commit series are recorded in the PR preparation report. The browser evidence was moved to its own Phase 2 file to avoid an add/add conflict with the independently added Stage A report.


Local implementation dated 2026-10-30. Branch `feat/family-join-requests` starts at fetched `origin/develop` commit `87726c565207d736c3d0eea3e50a13af94d15367`. Existing uncommitted Stage A fixes and browser evidence were preserved. No commit, push, merge or deployment is part of this work.

## Stage A prerequisite

Stage A's mandatory gate passed before Phase 2. Shared Development PUT returned 200 with a persisted name and fresh stamp. The separate hosting PR retains the full Stage A report and undeployed API-error fix. This branch contains [isolated Phase 2 browser evidence](family-join-browser-e2e-report.md) and intentionally excludes hosting fixes.

Subsequent [PR-readiness review](family-join-pr-readiness.md) completed at **19:42:45 Cairo on 2026-10-30**: **219 tests and 137 isolated Edge assertions passed**, no failures/skips. Migration upgrade/rollback/SQL replay and audit-secret checks passed. No production source/schema correction was required; two tests were added and failure diagnostics/browser completion checks strengthened. The implementation checkpoint below retains its original 217/135 evidence; the linked review is the latest verdict and commit plan.

## Domain and lifecycle

`FamilyJoinCredential` and `FamilyJoinRequest` are host-scoped retained `AuditedAggregateRoot<Guid>` types. Neither is a tenant nor soft deleted. Audited fields identify creation/modification; explicit decision fields and membership history supply the business record. Physical deletion is rejected by the Family persistence guard. Credential entities, submission DTOs, issuance responses, controller and application service disable ABP parameter/entity auditing to keep capabilities out of audit payloads. Domain events contain IDs, status and time only.

The credential transitions from Active to Revoked or Expired. Manual codes have 20 random Crockford alphabet characters (100 bits), displayed in four groups of five. QR credentials contain 32 random bytes encoded as base64url (256 bits). Domain-separated SHA-256 hashes allow indexed lookup of these high entropy generated capabilities; verification uses fixed-time hash comparison. Manual input accepts case differences, whitespace and group separators. QR tokens require the exact case-sensitive token.

Plaintext is returned **once**, when an Owner/Admin creates the credential. Repeating the same `requestId` returns metadata with null `secret` and `joinLink`. A caller that loses the response must deliberately issue a new request ID and rotate the credential. IDs are bound to their original Family, kind and issuer. Ordinary Family DTOs and credential lists never return a secret or hash.

There is one stored Active credential per Family/kind. A new ID rotates that kind. Explicit revocation and rotation of a still-live credential **cancel all its Pending requests in the same transaction**. Natural expiration prevents new submissions but preserves valid existing Pending requests. Replacing a naturally expired credential marks it Expired and preserves those requests for review. Read DTOs also report natural expiry without requiring a scheduled database write.

Requests transition Pending → Approved, Rejected or Cancelled. Identical terminal operation retries are safe; conflicting transitions fail. A server-resolved eligible Parent submits using a valid credential without gaining membership. One Pending request per Parent/Family is allowed. Existing active members cannot submit another. A rejected applicant can reapply with a new ID after the configurable cooldown; cancelled applicants may retry. Submission ID replays are bound to the applicant and original credential, including after later rotation.

Owner/Admin review rights are checked against active membership in an active Family in Application and Domain. A requester cannot approve or reject their own request, even if later promoted. Approval calls the existing `FamilyManager.AddApprovedMemberAsync`; activation/reactivation always uses **Member**, retains an existing membership ID and appends the existing membership audit history. Approval and activation commit atomically. A requester alone can cancel their own Pending request, including while the Family is archiving. No unrestricted membership-add endpoint was added.

## API contract

All nine routes require the existing `ParentBearer` scheme. JSON enums use the existing numeric representation: kind ManualCode=0 / QRToken=1; credential Active=0 / Revoked=1 / Expired=2; request Pending=0 / Approved=1 / Rejected=2 / Cancelled=3.

| Method | Path | Input / response |
| --- | --- | --- |
| POST | `/api/families/{id}/join-credentials` | `requestId`, `kind` → credential metadata, one-time `secret`, optional `joinLink` |
| GET | `/api/families/{id}/join-credentials` | paged metadata; Owner/Admin |
| POST | `/api/families/{id}/join-credentials/{credentialId}/revoke` | credential `concurrencyStamp`; 204 |
| POST | `/api/family-join-requests` | `requestId`, `kind`, `secret` → request metadata |
| GET | `/api/me/family-join-requests` | own retained requests; optional `status` |
| GET | `/api/families/{id}/join-requests` | Owner/Admin; `status` defaults to Pending |
| POST | `/api/families/{id}/join-requests/{requestId}/approve` | request `concurrencyStamp` → request metadata |
| POST | `/api/families/{id}/join-requests/{requestId}/reject` | request `concurrencyStamp`, optional `reason` ≤256 characters |
| POST | `/api/family-join-requests/{requestId}/cancel` | request `concurrencyStamp` → request metadata |

Request metadata includes request/Family/Parent/credential IDs, status, decision fields and concurrency stamp. No email, name, password or capability is returned. IDs supplied in the route are scoped before use; the submit body cannot choose the actor or Family. Unknown and inaccessible resources return the existing not-found response. Invalid, revoked, expired and inactive-Family credentials share the same generic unavailable response.

Paging uses `skipCount` ≥0 and `maxResultCount` 1–100 (default 20). Status filters are validated. Own request counts/pages apply the current Parent predicate. Reviewer counts/pages repeat Family status and current membership/role predicates in SQL. Reads project DTOs without materializing Family/join entities. Requests order by RequestedAt descending then ID; credentials by CreatedAt descending then ID. No arbitrary client sorting or full aggregate load is used for these lists.

New localized EN/AR errors use `STNG.Family.Join`: unavailable=1001 (404), duplicate Pending=1002 (409), invalid transition=1003 (409), self review=1004 (403), request identity conflict=1005 (409), attempt/cooldown limit=1006 (429), invalid domain input=1007 (400). Existing Family access/role/eligibility and stamp errors retain their current codes; stale stamps return `STNG.Family:1005` /409. Transport validation returns 400. IP throttling returns 429 through the ASP.NET limiter.

## QR payload and configuration

The issuance response for QR kind includes `stng://family/join?token=<opaque-token>`. The backend supplies an opaque payload; Flutter can encode/render/handle it later. No QR bitmap dependency, application UI, public scan endpoint or automatic membership activation is needed. Optional HTTPS app links must be configured to an owned endpoint; the URI cannot contain userinfo, an existing query or a fragment. No Family or Parent information is embedded in the payload.

Settings bind under `Family:Join`, validate on startup and have these defaults:

| Setting | Default | Validation |
| --- | --- | --- |
| ManualCodeLifetime | `1.00:00:00` | positive, ≤30 days |
| QRTokenLifetime | `1.00:00:00` | positive, ≤30 days |
| ReapplyCooldown | `00:01:00` | 0–1 day |
| AttemptsPerParentPerMinute | 10 | positive |
| AttemptsPerIpPerMinute | 30 | positive |
| QRJoinBaseUri | `stng://family/join` | absolute HTTPS or STNG URI |

The HTTP fixed-window limiter protects all submissions per remote IP before authentication, including anonymous attempts. A second fixed-window limiter applies per resolved Parent inside Application and counts invalid credentials too. Existing auth/token IP protections remain in place. The only additional runtime package is the standard `System.Threading.RateLimiting` 10.0.9 in Application; Domain has no HTTP or limiter dependency.

## SQL Server and concurrency

Migration **`20261030154452_AddFamilyJoinRequests`** adds only `AppFamilyJoinCredentials` and `AppFamilyJoinRequests`, their constraints/indexes, and the matching snapshot entries. Historical Parent, File and Family migrations are unchanged. All foreign keys restrict deletion. Composite foreign keys bind a request to a credential in the **same Family**, and credential issuers/revokers and request reviewers to retained Family membership. Applicant Parent FK respects retained Parent records.

Filtered unique indexes enforce one Active credential per Family/kind and one Pending request per Parent/Family. A unique `(Kind, SecretHash)` index supports credential lookup. Request list indexes support scoped paging and revocation of associated Pending requests. New roots use ABP concurrency stamps.

Every changed join root must also advance the tracked `Family.Version`. The existing persistence coordinator locks the Family root with an original Version + ABP ConcurrencyStamp comparison in the ambient transaction. Concurrent approval/rejection/cancellation, rotation/revocation, membership creation/reactivation, archiving and reviewer demotion therefore cannot both commit from the same snapshot. SQL uniqueness constraints remain the final defense. Status retirement executes before replacement inserts to satisfy SQL Server's filtered indexes; all preliminary updates and EF changes share the same transaction/savepoint. Failures restore both and discard the failed context; no automatic retry with stale tracked entities occurs.

Two details are covered by regression tests: decisive credential validation is an untracked read **after** loading the Family version, so an older tracked Active credential cannot conceal a committed revocation; initial lookup is also untracked so inserting a referencing request does not trigger ABP's navigation-change update of the credential stamp. These follow the installed [ABP 10.6.0 concurrency/navigation handling](https://github.com/abpframework/abp/blob/10.6.0/framework/src/Volo.Abp.EntityFrameworkCore/Volo/Abp/EntityFrameworkCore/AbpDbContext.cs).

## Verification and limits

The final clean Release run completed **2026-10-30 18:09:54 Cairo /16:09:54 UTC**:

| Check | Actual result |
| --- | --- |
| Clean solution build | Passed; 0 errors, 1 pre-existing CS0162 host warning |
| Domain | 55 passed |
| Application | 19 passed |
| EF Core | 40 passed |
| HTTP / SQL Server / browser | 103 passed |
| Total | **217 passed, 0 failed, 0 skipped** |
| Real Edge browser assertions | **135 passed, 0 failed, 0 blocked**, within one passing .NET test |
| EF migration consistency | No pending model changes |
| Idempotent SQL migration script | Generated and inspected; only the two join tables and their constraints/indexes |

Commands: `dotnet clean STNG.slnx -c Release`, `dotnet build STNG.slnx -c Release --no-restore`, `dotnet test STNG.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=family-join-final.trx'` (browser runtime variables set), and `dotnet ef migrations has-pending-model-changes --project src/STNG.EntityFrameworkCore --configuration Release --no-build`. TestBase is a support assembly with zero tests. The final result adds 45 tests to the 172-test Stage A local checkpoint.

SQL tests use synchronized independent transactions for duplicate Pending submissions, two approvals, approve/reject, approve/cancel, approve/revoke, reviewer demotion, Family archiving, external membership creation and reactivation, and concurrent credential rotation. They assert one winner and the resulting membership/history/Owner invariants. Additional tests verify filtered unique indexes, same-Family credential foreign keys, retention/root-write guards, stale tracked credential rejection, and rollback to the savepoint after ordered status updates followed by an EF failure. API tests verify scoped IDs/visibility, validation, paging SQL projections, current permissions, idempotency, former Admin reactivation, Parent deactivation/soft deletion and both rate limits.

Tests use uniquely named disposable LocalDB SQL Server databases and migrate/delete those databases only. Shared Development records are never prepared through direct SQL. Ignored evidence is in `artifacts/family-join/` (`final-*.log`, `browser-results.json`, `test-summary.json`, migration logs/script, and `review-files.txt`); per-suite TRX evidence is under `test/*/TestResults/family-join-final.trx`.

The browser test runs the real host over loopback HTTPS Kestrel and visible Microsoft Edge/Playwright with normal certificate validation. It registers and verifies three new Parents through Swagger using the actual generated OTPs captured by the existing test email sender, then uses the public Parent grant. Passwords, OTPs and tokens travel through process stdin or memory only. No trace, HAR or authenticated screenshot is recorded. Test-process clock control is restricted to the disposable fixture and creates no HTTP administration endpoint. The browser exercises joining, review, permissions, QR/cancellation, reapplication, reactivation, promotion/removal, ownership transfer and isolation.

To include browser tests in a local full run, set `STNG_BROWSER_NODE` to a Node executable and `STNG_PLAYWRIGHT_MODULES` to a directory containing the installed `playwright` module, have Microsoft Edge installed and a normally trusted localhost HTTPS development certificate. Without those explicit runtime paths the browser fact is skipped with a reason; API-only tests are not presented as browser coverage.

Remaining deployment considerations: rate limit state is process-local, so multiple replicas require a shared enforcement policy; a reverse proxy must supply trusted client IPs correctly. End-to-end Flutter link handling and complete interactive Swagger authorization-code login are outside this backend verification. The existing shared Development API-to-HTML redirect remains until normal reviewed deployment of the local middleware fix. Account eligibility is rechecked against committed state before domain decisions, but is not a global serializable lock across independent account administration. No production readiness claim is made.

## Implementation checkpoint scope and Git state

The final file inventory is `artifacts/family-join/review-files.txt`: **43 files**, comprising **14 tracked modifications and 29 new files**, all unstaged. This includes the preserved Stage A report/scripts, middleware regression tests and WebDAV configuration change. Phase 2 adds the two roots/managers/options/secrets, enums/localization, application contracts/service/validation/limiter, explicit HTTP controller, EF configuration/migration/snapshot, tests and this document. Existing Parent/File implementation and historical migrations were not refactored.

Final HEAD and merge base remain `87726c565207d736c3d0eea3e50a13af94d15367`. There is no new commit or PR. Secrets, DPAPI credential state, test results, temporary SQL and generated build artifacts remain outside the Git change inventory. The separately observed original checkout's secret configuration hash is unchanged. No reset, stash, force operation, shared database manipulation or deployment was performed.

## Independent Phase 2 verification — 2026-11-01

Clean preparation branch: prep/family-join-requests, based on origin/develop 87726c565207d736c3d0eea3e50a13af94d15367. The original feat/family-join-requests checkout remains untouched. Fresh Release build passed with 0 errors and the existing CS0162 warning. **212 tests passed, 0 failed, 0 skipped** (Domain 55, Application 19, EF 40, HTTP/SQL/browser 98); the **137 isolated Edge assertions** passed. The seven API error-page tests are deliberately isolated in PR 1, explaining the reduction from the mixed review's 219 tests. Migration upgrade/rollback/generated SQL replay and audit-secret suppression passed; EF reports no pending changes. No web.config or error-page helper changes are present. After PR 1 actually merges, integrate latest develop and rerun the relevant suites before publishing PR 2.
