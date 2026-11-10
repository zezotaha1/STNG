# Family Join Requests — final review and PR readiness

> Local PR preparation (2026-11-01): this document retains the original implementation/review checkpoints. The clean Phase 2 branch excludes IIS/WebDAV and API error-page changes. Fresh verification and the local commit series are recorded in the PR preparation report. The browser evidence was moved to its own Phase 2 file to avoid an add/add conflict with the independently added Stage A report.


Review: **2026-10-30**, worktree `F:/STNG/STNG-family-core`, branch `feat/family-join-requests`. HEAD, freshly fetched `origin/develop` and merge base all equal **`87726c565207d736c3d0eea3e50a13af94d15367`**. No divergence or feature commits. Installed SDK 10.0.403; repository has no global.json pin. ABP 10.6.0, EF Core SQL Server 10.0.9, .NET 10.

**Verdict: Ready for PR review.** No Critical or High implementation defect confirmed. Two Low testing/diagnostic issues fixed; migration/audit verification gaps closed. No production source or schema correction required during this review. Deployment is not approved by this verdict.

## Findings

| Severity | Evidence | Disposition |
| --- | --- | --- |
| Critical | None confirmed. | No open finding. |
| High | None confirmed. | No open finding. |
| Medium — known operational limitation | Limits are process-local: [Parent limiter](src/STNG.Application/Families/FamilyJoinAttemptLimiter.cs:17), [IP limiter](src/STNG.HttpApi.Host/STNGHttpApiHostModule.cs:108). Replicas/restarts do not share budgets. | Tested for one process. Shared enforcement and trusted client-IP handling need verification before relying on global limits across workers. No speculative infrastructure added. |
| Medium — existing deployed finding | Earlier Stage A evidence records shared Development API-to-HTML redirects. The local [error branch](src/STNG.HttpApi.Host/STNGHttpApiHostModule.cs:314) fixes this contract. | Local production-mode middleware tests pass; fix remains undeployed. Shared Development was not retested or changed during this review. |
| Low — fixed | String assertions could print a secret/token/response on regression failure: [API](test/STNG.HttpApi.Host.Tests/Families/FamilyJoinApiTests.cs:43), [Domain](test/STNG.Domain.Tests/Families/FamilyJoinDomainTests.cs:34). | Boolean comparisons and neutral diagnostics now suppress values. No prior passing log is claimed to have leaked a secret. |
| Low — fixed | Browser success required only 35 assertions rather than workflow completion; transfer failures checked 409 without its reason. [Harness](test/STNG.HttpApi.Host.Tests/Browser/FamilyJoinBrowserTests.cs:92), [error codes](test/STNG.HttpApi.Host.Tests/Browser/family-join-local.cjs:152). | Require final completion marker and exact stale/expiry codes. Fresh run: 137 passing assertions. |

Additional verification: [migration round trip](test/STNG.HttpApi.Host.Tests/Families/FamilyJoinMigrationTests.cs:34) and [audit suppression with positive controls](test/STNG.HttpApi.Host.Tests/Families/FamilyJoinAuditTests.cs:39). The latter enables auditing/relevant entity history, proves control records are saved, then verifies successful/failed submissions do not persist capabilities or hashes in action parameters/property changes. Credential history remains excluded even when selected.

The Medium rows describe known operational follow-ups, not newly demonstrated authorization bypasses or schema corruption. No unresolved code blocker was found.

## Migration verification

The small reported delta was Git accounting: ordinary `git diff --stat` omits untracked migration/Designer files. Complete contents were inspected:

| File | Actual size |
| --- | --- |
| [Migration](src/STNG.EntityFrameworkCore/Migrations/20261030154452_AddFamilyJoinRequests.cs:14) | 179 lines /9,958 bytes; untracked |
| [Designer](src/STNG.EntityFrameworkCore/Migrations/20261030154452_AddFamilyJoinRequests.Designer.cs:1) | 3,015 lines /118,803 bytes; untracked |
| [Snapshot](src/STNG.EntityFrameworkCore/Migrations/STNGDbContextModelSnapshot.cs:164) | 3,012 lines /118,684 bytes; 210 added lines, none removed |

Up creates **2 tables, 31 columns** (credentials 16, requests 15), **2 PKs, 1 alternate key, 7 restrictive FKs and 11 explicit indexes** (14 including key-backed indexes). It includes required auditing/extra-property/concurrency fields, required varchar(64) SecretHash and nullable nvarchar(256) RejectionReason. Composite credential/request and reviewer/issuer FKs enforce Family boundaries. Unique indexes cover `(Kind, SecretHash)`, one Active credential per Family/kind and one Pending request per Family/Parent; the latter two filter `[Status] = 0`. Paging/revocation/FK indexes are present.

[Down](src/STNG.EntityFrameworkCore/Migrations/20261030154452_AddFamilyJoinRequests.cs:170) drops requests before credentials. Schema reverses correctly; **join data is dropped**, so deployment rollback needs backup/recovery if it must retain that data. No historical Parent/File/Family migration changed.

Snapshot properties/indexes are complete ([metadata](src/STNG.EntityFrameworkCore/Migrations/STNGDbContextModelSnapshot.cs:164)), as are [relationships](src/STNG.EntityFrameworkCore/Migrations/STNGDbContextModelSnapshot.cs:2621). The test compares the **entire Designer target relational model** to the current design-time model: no differences. EF CLI reports no pending model changes.

The new test owns a unique LocalDB database and checks its LocalDB/catalog identity before rollback. It establishes latest develop migration `20261027193809_AddFamilyCore`, creates Parents/Family through existing APIs and persists a FileAsset through the existing repository. It proves:

1. Develop → feature applies, preserves baseline data and supports legitimate credential issuance/submission.
2. Feature → develop removes join rows/tables, restores original table/column/index/FK schema, and preserves existing Parent/Family/member/history/FileAsset records.
3. Generated idempotent upgrade SQL applies from develop and runs twice safely, with correct history and no pending model changes.

Upgrade SQL contains only the new tables/constraints/indexes and history insertion. Rollback SQL drops only these tables and removes their history row. Shared Development was never used.

## Security and architecture

- [Generation](src/STNG.Domain/Families/FamilyJoinSecrets.cs:16) uses cryptographic randomness: manual codes contain **100 random bits**, QR tokens **256 bits**. Domain-separated SHA-256 lookup and fixed-time verification suit generated capabilities rather than human passwords. Normalization does not reduce generated entropy.
- [Submission](src/STNG.Domain/Families/FamilyJoinManager.cs:94) checks Parent eligibility, active Family, credential validity, duplicate Pending state and cooldown. The decisive untracked read follows the Family snapshot, preventing stale tracked revocation state. Knowing a code never activates membership.
- [Rotation/revocation](src/STNG.Domain/Families/FamilyJoinManager.cs:73) cancels associated Pending requests atomically. Natural expiry prevents new submission while preserving valid Pending requests. Plaintext appears only at first issuance; bound retries and ordinary DTOs expose no reusable secret/hash.
- ParentBearer protects explicit HTTP routes. Application resolves CurrentUser through existing Parent checks; client identifiers cannot override the actor. Soft-deletion and fresh Identity eligibility remain enforced. Cancellation is requester scoped. No data filter is disabled; these records are host scoped.
- [Approval](src/STNG.Domain/Families/FamilyJoinManager.cs:123) requires current active Owner/Admin, forbids self-review and reuses **FamilyManager.AddApprovedMemberAsync** at line 139. Membership uniqueness, retained IDs, Member-only reactivation and membership history remain in established domain behavior.
- Both retained audited join roots encapsulate state and reference aggregates by ID. Domain has no Application/EF/HTTP dependency. Application orchestrates and HTTP delegates, reusing ABP services, repositories, units of work, validation, errors/localization and existing DTO projection conventions.
- [Reads](src/STNG.Application/Families/FamilyJoinAppService.cs:163) authorize before pagination and repeat active Family/current reviewer predicates in count/page SQL. Own requests filter by server Parent. Bounded deterministic DTO projections materialize no aggregates; SQL/materialization probes pass. Mutations deliberately load the existing tracked aggregate for invariants.
- [Persistence](src/STNG.EntityFrameworkCore/EntityFrameworkCore/STNGDbContext.Families.cs:42) preserves the Family Version + ABP stamp lock. Changed join roots must advance Family.Version. Ordered retirement, EF writes and savepoints share a transaction; failed contexts are discarded. SQL races confirm one writer commits across review/cancel/revoke, demotion, archiving, rotation and membership creation/reactivation, preserving membership/history/Owner invariants.
- Audit storage was verified with positive controls; ordinary DTOs, browser/test outputs and source candidates were inspected. External proxy/body-capture configuration and an exhaustive historical secret audit are outside this scope.

## Fresh verification

Completed **2026-10-30 19:42:45 Cairo /17:42:45 UTC**.

| Check | Actual result |
| --- | --- |
| Clean Release build | Passed: 0 errors, 1 existing CS0162 host warning |
| Domain | 55 passed |
| Application | 19 passed |
| EF Core | 40 passed |
| HTTP / SQL / migration / audit / browser | 105 passed |
| Full suite | **219 passed /0 failed /0 skipped** |
| Isolated Edge | **137 assertions passed /0 failed**, within one passing test |
| Migration application/rollback/generated SQL replay | Passed on disposable SQL Server LocalDB |
| Model consistency | No pending changes; full Designer equivalence passed |

Commands: `dotnet clean STNG.slnx -c Release`; `dotnet build STNG.slnx -c Release --no-restore`; `dotnet test STNG.slnx -c Release --no-build --no-restore --logger 'trx;LogFileName=family-join-pr-review.trx'` with documented Node/Playwright runtime variables; EF CLI consistency and upgrade/rollback script generation with `--configuration Release --no-build`. TestBase is support code with zero tests.

Ignored evidence: `artifacts/family-join-review/` contains build/test logs, upgrade.sql, rollback.sql, model-consistency.log, sanitized browser/test summaries, secret-scope-check.json and file inventory. TRX: `test/*/TestResults/family-join-pr-review.trx`. No shared browser writes, IIS changes or deployment occurred during this review.

## PR separation and remaining risks

**Recommend a separate hosting/API-fix PR first**, followed by Phase 2 after integration. Hosting changes resolve an existing deployed issue without join-schema dependencies and can be reviewed/rolled out independently. A combined PR is technically compatible and locally tested, but couples hosting recovery to a schema feature and complicates rollback review. No branch/commit was created for this recommendation.

The host module requires hunk separation: error-page helper/initialization belong to hosting; Family using/named join limiter belong to Phase 2. Split the browser report's Stage A and isolated Phase 2 evidence similarly. Exact commit file recommendations follow below.

Remaining follow-ups: process-local limits and undeployed response fix above; backup planning for Down; load testing bulk revocation and retained-history aggregate mutations at expected scale. Correctness tests establish no throughput limit. Parent eligibility is refreshed at the domain decision; independent account administration is not globally serialized with Family commits. Flutter links and complete interactive Swagger OAuth login remain outside this backend review.

CI has not run for uncommitted changes. Existing workflows do not provision browser runtime variables, so that fact explicitly skips there unless infrastructure is added; the local run executed it without skips. This is PR readiness, not production certification.

## Original mixed-checkout Git scope (review checkpoint)

**46 unique files: 14 tracked modifications /32 untracked, all unstaged.** No unrelated candidate identified. No feature commit, push, PR creation, merge or deployment. HEAD remains fetched develop. No appsettings, workflow, connection string or certificate changed; web.config is intentional. Three already-tracked appsettings.secrets.json files are unchanged empty JSON objects. The separate original checkout's observed secret hash is unchanged. No credential material was identified in reviewed candidates; ignored DPAPI state, SQL artifacts, screenshots, logs, TestResults, bin and obj remain excluded. No history rewrite, reset, stash, discard or shared database manipulation occurred.

## Independent Phase 2 preparation — 2026-11-01

The previous mixed commit plan was executed by scope in separate worktrees. This branch contains only Phase 2 implementation, tests and documentation. It excludes web.config, ApiErrorPageTests, Stage A browser scripts and the API error-page helper/call. The host-module change is limited to the Family join IP limiter. The mixed report's Phase 2 evidence is retained in family-join-browser-e2e-report.md. Source and test reports in the original checkout are preserved by a complete verified checkpoint. Local commit identities and fresh verification are recorded in the external PR preparation report. PR 2 remains queued until PR 1 merges and latest develop is integrated and retested.
