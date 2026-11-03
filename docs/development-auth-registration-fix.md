# Development Swagger client registration fix

Prepared on 2026-11-03 in an isolated worktree based on develop `365aed9de7c8dfd13cd796646b943fe543b95917`. Publication is authorized for standalone PR review only. This document describes the fix and a future operator procedure; it does not authorize merge, deployment, shared database access, migrations or seeding. Successful CI does not grant that authorization.

## Confirmed Swagger cause

The operator's read-only database evidence shows `STNG_Swagger` registered with the localhost callback and ClientUri, last modified on 2026-11-01 at 21:04:21. The actual browser authorization request uses `https://zezotaha.ddns.net:44391/swagger/oauth2-redirect.html` and is rejected with HTTP 400 / OpenIddict ID2043. Redirect URI validation is working; the registered value is wrong for this environment.

DbMigrator ships `OpenIddict:Applications:STNG_Swagger:RootUrl=https://localhost:44391/` as a local-development default. Its existing seeder derives both the callback and ClientUri from that key. The Development GitHub Actions DbMigrator step supplied only the process environment and database connection override, so the shipped localhost RootUrl was used on every run.

IIS application-pool variables configure the API worker, not the separate GitHub runner's DbMigrator process. `AuthServer:Authority` controls the runtime issuer and Swagger discovery; it does not supply the separate client registration RootUrl.

The fix explicitly supplies this non-secret setting to **only the Development DbMigrator step**:

```text
OpenIddict__Applications__STNG_Swagger__RootUrl=https://zezotaha.ddns.net:44391/
```

The local JSON defaults, Production workflow, API issuer, certificates, client grants and authentication requirements are unchanged. No public URL is baked into a publish artifact. This step-level setting is applied on subsequent Development deployments as well.

The override is under `jobs.deploy.steps[Run STNG DbMigrator].env` in `.github/workflows/development.yml`. That job targets the `development` GitHub environment and `stng-dev` runner, supplies `STNG_DEV_CONNECTION_STRING`, and runs only for a push to `refs/heads/develop`. Its existing `DOTNET_ENVIRONMENT=Production` and `ASPNETCORE_ENVIRONMENT=Production` flags describe the process runtime environment; they do not select the Production deployment target. The override is absent from workflow/job-wide environment settings, published appsettings and `.github/workflows/production.yml` (currently Main CI only). The Production file and shipped defaults are byte-for-byte unchanged against the base commit. No current Production deployment or live configuration has been inspected or changed by this local review.

## Exact standalone PR scope

| File | Intended change |
| --- | --- |
| `.github/workflows/development.yml` | One non-secret environment setting on the Development DbMigrator step |
| `test/STNG.HttpApi.Host.Tests/AuthenticationHostFixture.cs` | One internal read-only accessor for the fixture's generated LocalDB connection string |
| `test/STNG.HttpApi.Host.Tests/OpenIddictRegistrationTests.cs` | Four real SQL/HTTP registration regression cases and guarded cleanup of their owned disposable catalogs |
| `docs/development-auth-registration-fix.md` | Diagnosis, test evidence, seeding scope, operational gate, rollback and conditional review requirements |

These are the exact four staged files. Phase 2, migrations, runtime authentication, pooling, signing certificates, credentials, Production configuration, local secrets and generated test artifacts are excluded. The implementation and tests match the code used for the latest 176/0 verification; the conditional-review preparation changes documentation only. Test evidence and the local PR-description draft remain ignored under `artifacts/`.

## Existing registration repair

The STNG contributor already calls ABP's `CreateOrUpdateApplicationAsync` with the configured Swagger values. [ABP 10.6.0's base contributor](https://github.com/abpframework/abp/blob/10.6.0/modules/openiddict/src/Volo.Abp.OpenIddict.Domain/Volo/Abp/OpenIddict/OpenIddictDataSeedContributorBase.cs) finds the existing client by ClientId and updates it through `IAbpApplicationManager.UpdateAsync`; it creates a client only when none exists. No new repair API, direct SQL write, replacement client or custom seeder is needed.

Regression coverage uses the real host, application manager and SQL Server LocalDB. It starts with the existing localhost registration, binds the actual Development workflow setting over the shipped DbMigrator defaults using the .NET environment provider, and reseeds twice. It verifies the same client ID, one row, exact public callback and ClientUri, and unchanged logical Mobile registration. HTTP checks exercise OpenIddict's real redirect validation and cache: the public callback reaches the login challenge while localhost/unregistered callbacks remain rejected. Separate cases cover first creation, explicit local settings and a representative separate Production root. No test connects to the public host or shared database.

The shared test fixture exposes its generated connection string internally so the new regression hosts can clean up only their own catalogs. Cleanup requires the LocalDB instance and exact STNG_Auth_Tests_ plus GUID naming rule, disposes the host, clears only that catalog's pool, and drops that owned catalog through a separate master connection. This avoids the process-wide pool clearing performed by [EF Core 10.0.9 EnsureDeleted](https://github.com/dotnet/efcore/blob/v10.0.9/src/EFCore.SqlServer/Storage/Internal/SqlServerDatabaseCreator.cs). Production cleanup behavior and the existing authentication fixture's normal disposal are unchanged.

### Reviewed seeding side effects

A local inventory built the actual `STNGDbMigratorModule` service provider with `AddDataMigrationEnvironment`, using a generated LocalDB-only configuration. It did **not** initialize modules, obtain a DbContext, connect to a database, run migrations or invoke seeding. Its registered `AbpDataSeedOptions.Contributors` list contains the following three types, in this order; evidence is retained in `artifacts/auth-conditional-review-20261103/seed-inventory.log`.

| Contributor or preceding operation | Existing behavior and operational consequence |
| --- | --- |
| Schema migration | `STNGDbMigrationService.MigrateAsync` calls every registered schema migrator before `IDataSeeder`. This PR adds no migration, but an artifact can still apply previously pending migrations. The whole operation must not be assumed to roll back atomically with IIS file deployment. |
| `Volo.Abp.PermissionManagement.PermissionDataSeedContributor` | Selects applicable role-provider permission definitions and seeds the admin role. The default permission seeder inserts missing grants; it can restore a deliberately removed admin-role grant. It does not delete grants in this path. |
| `Volo.Abp.Identity.IdentityDataSeedContributor` | Delegates to the Identity seeder with the existing bootstrap values supplied by `STNGDbMigrationService`. ABP returns without modifying an existing admin user; if that user is absent, it creates the administrator, creates the admin role if needed and assigns it. This is not a password-rotation operation. Stop before seeding if unexpected bootstrap creation would occur; do not expose or change credentials as part of this fix. |
| `STNG.OpenIddict.OpenIddictDataSeedContributor` | Creates the `STNG` scope only if missing. Always creates/updates `STNG_Mobile`; also creates/updates configured `STNG_App` and `STNG_Swagger` clients when their ClientId and RootUrl are non-empty. Existing IDs are retained. It is not a Swagger-only patch. |

The framework behavior above was reviewed against ABP 10.6.0's [permission contributor](https://github.com/abpframework/abp/blob/10.6.0/modules/permission-management/src/Volo.Abp.PermissionManagement.Domain/Volo/Abp/PermissionManagement/PermissionDataSeedContributor.cs), [permission seeder](https://github.com/abpframework/abp/blob/10.6.0/modules/permission-management/src/Volo.Abp.PermissionManagement.Domain/Volo/Abp/PermissionManagement/PermissionDataSeeder.cs), [Identity seeder](https://github.com/abpframework/abp/blob/10.6.0/modules/identity/src/Volo.Abp.Identity.Domain/Volo/Abp/Identity/IdentityDataSeeder.cs) and [data seeder](https://github.com/abpframework/abp/blob/10.6.0/framework/src/Volo.Abp.Data/Volo/Abp/Data/DataSeeder.cs). Multi-tenancy is disabled in this checkout, and the current migration service seeds host data without a tenant loop.

For each seeded OpenIddict client, a fresh descriptor defines application/client/consent types, display metadata, permissions, callbacks and logout callbacks. [OpenIddict Core 7.5.0](https://github.com/openiddict/openiddict-core/blob/7.5.0/src/OpenIddict.Core/Managers/OpenIddictApplicationManager.cs), the installed version, populates the existing client from that descriptor, including display-name maps, properties, requirements, settings, JSON web keys and client-secret state. [ABP's manager](https://github.com/abpframework/abp/blob/10.6.0/modules/openiddict/src/Volo.Abp.OpenIddict.Domain/Volo/Abp/OpenIddict/Applications/AbpApplicationManager.cs) also populates ClientUri, LogoUri and front-channel logout metadata. Fields/collections omitted from the seed descriptor can be cleared, and out-of-band customizations can be replaced. All current STNG seed descriptors are public clients with no secret; an unexpected stored secret or custom security requirement must be reviewed before a run, not silently normalized under this repair's approval.

Client updates can advance auditing/concurrency metadata even when logically unchanged. ABP's manager removes the old application from its cache during update; a separate DbMigrator process must not be assumed to invalidate the live IIS worker's cache. The normal approved IIS deployment uses `app_offline.htm` to stop/restart the application. A separately authorized seed-only operation must also verify that the live API observes the corrected registration; any needed cache refresh or worker recycle requires its own operational scope.

The regression tests demonstrate unchanged canonical Mobile metadata and repeated Swagger upsert without duplication. They do not prove preservation of arbitrary live customizations or the optional App client's live configuration. Before a shared run, compare all three clients' effective configuration and existing metadata with the expected seed descriptors, check the existing administrator and admin-role grants through approved read-only administration, and stop on unapproved differences. Keep protected backup material private; export neither credential/key values nor arbitrary properties that might contain secrets. No new permission grant, account, secret, key or token change is introduced by this PR's code.

## Safe operator procedure — separate authorization required

1. Review and approve the standalone change and build one immutable artifact, recording its source commit and CI run. The present local task does not publish it.
2. Before running DbMigrator against Development, obtain explicit approval for that database target and inspect the artifact's migrations against the target's migration history. This fix adds no schema changes. Do not use a Phase 2 artifact or apply its join migration as part of this repair.
3. Record the existing Swagger application ID and a protected snapshot/backup sufficient to assess or recover the seeded client descriptors, administrator/admin-role state and migration history. Review the side effects above for Mobile and optional App as well as Swagger. Exclude client secrets, tokens, credential material and connection strings from reports. If any new administrator, grant restoration, unexpected secret clearing or custom-client normalization would occur, stop for review before running the full seeder.
4. Use the reviewed Development workflow's DbMigrator configuration, including the RootUrl variable above and the existing securely supplied Development connection string. If using a separately approved manual run of the same artifact, set the same process-scoped configuration before invoking the existing DbMigrator entry point. Do not assume IIS worker variables are inherited by a runner or console process. Ensure no later configuration provider overrides the registration RootUrl; the checked-in secrets file contains no such override.
5. Run exactly one approved DbMigrator instance. Its existing migration service runs schema migration **and all registered data-seed contributors**; it is not a Swagger-only or seed-only command. Existing clients' audit/concurrency metadata may advance even when their logical registration is unchanged. Review this full scope before approval. Do not retry a failed run until its applied migrations and completed seed changes have been inspected.
6. The approved contributor/application manager updates the existing Swagger application. Do not edit the shared database directly, weaken redirect checks or add wildcard callbacks. No Mobile permission/certificate/credential change is part of this fix.
7. Verify the metadata and browser flow below. Keep the corrected public callback if a separate authentication issue remains; do not revert it to localhost as an automatic rollback. No schema rollback is required by this change.

## Post-deployment verification

This is a recommended **mandatory operator gate after a separately authorized deployment/seeding run**. It is not implemented as a new automatic shared-database job in this PR. The existing `.github/scripts/deploy-stng.ps1` gate checks only a successful health HTTP status; it does not inspect persisted client registration or complete interactive OAuth.

The operator can inspect the following non-secret client metadata through approved read-only administration. If using SQL, confirm the actual database/table identity first; the current model uses `OpenIddictApplications`:

```sql
SELECT Id, ClientId, ApplicationType, ClientType, RedirectUris, ClientUri, LastModificationTime
FROM OpenIddictApplications
WHERE ClientId = N'STNG_Swagger';
```

Require exactly one row with the existing application identity, public/web types, exactly `https://zezotaha.ddns.net:44391/swagger/oauth2-redirect.html` as its callback, and `https://zezotaha.ddns.net:44391/swagger` as its ClientUri. Inspect the Mobile registration separately to confirm its password/refresh grants and token endpoint permission remain unchanged. Do not select/export client secrets or tokens.

Then verify from an external browser, with normal TLS validation:

- Health returns 200/Healthy.
- Public discovery returns issuer `https://zezotaha.ddns.net:44391/` and usable public authorization/token/JWKS endpoints.
- Swagger's OIDC discovery URL uses the public origin.
- The real Swagger authorization-code flow accepts the exact callback and reaches login, then completes login/code exchange with an approved test identity. Correct metadata or a login challenge alone is not a completed OAuth test.
- In Microsoft Edge, start a fresh Swagger OAuth session, follow normal login/consent, return to the exact public callback and confirm Swagger becomes authorized and an approved protected API read succeeds. Confirm an unregistered callback remains rejected. Record only sanitized statuses/outcomes, not the callback query, authorization code, state or bearer token.
- Parent token issuance, authenticated identity/Family access and anonymous/forbidden responses work through the actual pipeline without inappropriate HTML redirects. Any dedicated Family mutation requires the approved rename-and-restore scope.

Record statuses, source/artifact identity, timestamps and sanitized correlation information. Do not capture passwords, OTPs, bearer tokens or OAuth codes/state in logs/screenshots. Repeat the registration check after the next separately authorized Development deployment to establish that it no longer reverts to localhost.

The registration repair gate passes only when the persisted ID/callback/ClientUri check **and** the interactive OAuth round trip and protected read pass. A database-only result or HTTP 302 does not close that gate. The separate Parent `/connect/token` HTTP 500 continues to block the broader Development authentication/Phase 2 smoke gate until independently diagnosed and verified. If it or another defect prevents the OAuth round trip, mark the affected operational gate Blocked/Failed; retain the valid public callback and report the actual failure.

## Safe rollback procedure — separate operational approval required

1. Record the failed stage, source/artifact SHA, migration/seeding progress, sanitized exception and current metadata before retrying or rolling back. Coordinate one operator/run so a subsequent automatic DbMigrator cannot overwrite the repair. This document does not authorize cancelling a live run or changing workflow controls.
2. If seeding has completed and a later IIS file deployment or smoke check fails, restore the approved known-good API artifact through the existing reviewed IIS backup/deployment procedure. Its existing rollback restores site files and the protected certificate from the established store; it does not undo database seeding. Preserve the persistent IIS environment configuration, public authority and existing signing credentials. This PR requires no certificate, secret or credential rotation.
3. Preserve the corrected public Swagger callback and ClientUri during an API artifact rollback. Reverting the workflow line or binaries does not revert the persisted registration. Do not rerun an older DbMigrator/workflow with its localhost defaults; any separately approved rerun must still supply the correct Development RootUrl. Keep that override in future Development deployment configuration even if application binaries are rolled back.
4. If client metadata was unexpectedly changed, stop for operator/security review. Use the existing application manager and a reviewed, target-specific recovery descriptor to restore only approved fields on the existing application ID. Retain the exact callback valid for the public origin. The pre-fix localhost snapshot is evidence, not a valid recovery target for public Swagger. Do not delete/recreate clients, use direct SQL repairs, relax redirect validation or restore a whole shared database to undo this setting. Unrelated admin/grant or custom-client side effects require their own reviewed recovery scope; do not reverse permissions or accounts speculatively.
5. This PR adds no schema changes, so it has no migration `Down` rollback. If the artifact applied older pending migrations, assess their compatibility and recovery separately before reverting binaries; do not run blanket `database update`/down-migrations as a Swagger repair rollback.
6. Verify the existing application ID and exact persisted public callback/ClientUri after recovery, then repeat public discovery, health and the interactive Edge OAuth/protected-read gate. Account for live worker caches through the approved restart/cache procedure. If recovery cannot pass, leave the affected operational gate open and report the blocker. Parent token HTTP 500 and SQL 995 remain separate investigations.

## Parent token HTTP 500 — not diagnosed from registration metadata

The observed POST `/connect/token` failure was around 2026-11-03 19:21:12 UTC /21:21:12 Egypt time. The operator reports a public/native Mobile client with password/refresh grants and token endpoint permission; this does not establish a registration defect.

The operator now reports SQL authentication evidence for the live Parent token HTTP 500: **`Login failed for user 'sa'`**. The SQL Server **18456 state** and the effective configuration cause remain to be investigated. This is separate from the unexplained LocalDB transport error 995. The message does not establish which configuration or credential is wrong, or a Swagger registration, grant or certificate defect.

The Parent controller resolves the user, checks Parent eligibility and delegates password/sign-in checks and token issuance to ABP/OpenIddict. Obtain the correlated sanitized application exception/inner exceptions/stack and the SQL Server-side 18456 state and timestamp/correlation evidence, together with the effective configuration source/target identity without secret values. The previously proposed export was pending from `C:\Deploy\STNG\Logs\Api\stng-api-20261103.log` on the Development server. The report of a login failure is evidence, not a completed configuration diagnosis. Do not change grants, signing certificates, credentials or authentication implementation speculatively as part of this PR.

The HealthChecks.UI collector error is a separate operator-reported issue and is not changed here. Phase 2, its migration, both preserved Family checkouts and private checkpoints are outside the fix's file scope.

## Test-failure investigation

The original full run was **174 passed /2 failed /0 skipped**. Untouched develop at the same base, in `F:/STNG/STNG-auth-registration-baseline`, was **171 passed /1 failed /0 skipped**. Its source remains unmodified. The four additional cases account for the difference in total tests (176 versus 172).

| Exact failing test | Failure location | Verified classification |
| --- | --- | --- |
| `STNG.AuthenticationFlowTests.Concurrent_registration_cannot_create_duplicate_identity_or_profile` | `AuthenticationFlowTests.cs:92`, the verification SELECT after the two registration responses | Reproduced individually on both the fix branch and untouched develop. This failure predates the Swagger change and does not require the new registration tests. |
| `STNG.AuthenticationFlowTests.SqlServer_rejects_duplicate_parent_and_orphan_profile` | `AuthenticationFlowTests.cs:104`, the initial identity lookup before the expected constraint violations | Failed in the original branch full run with the same transport exception. Passed individually on both branches. It has not been reproduced on develop; a common cause is plausible but not established. |

Both original exceptions are `Microsoft.Data.SqlClient.SqlException`, with the message:

```text
A transport-level error has occurred when receiving results from the server.
(provider: Named Pipes Provider, error: 0 - The I/O operation has been aborted
because of either a thread exit or an application request.)
```

The inner exception is `System.ComponentModel.Win32Exception` with the same I/O-aborted message. Temporary command instrumentation reproduced SQL error **995**, inner native error **995**. It recorded no parameters, credentials, token values or HTTP bodies. The relevant original stack paths are:

```text
Both:
  RelationalCommand.ExecuteReaderAsync
  SplitQueryingEnumerable<T>.AsyncEnumerator.InitializeReaderAsync / MoveNextAsync
Concurrent registration:
  EntityFrameworkQueryableExtensions.ToListAsync
  AuthenticationFlowTests.cs:92 -> AuthenticationHostFixture.InUnitAsync:89
Constraint test:
  ShapedQueryCompilingExpressionVisitor.SingleOrDefaultAsync
  EfCoreIdentityUserRepository.FindByNormalizedEmailAsync
  UserManager.FindByEmailAsync
  AuthenticationFlowTests.cs:104 -> AuthenticationHostFixture.InUnitAsync:89
```

The complete original messages and stacks are retained in the ignored local evidence file `artifacts/auth-failure-resolution-20261103/initial-failures.json`. These are verification-read transport failures, not failed uniqueness assertions, SQL deadlock error 1205 or command timeout -2. That distinction does not rule out load or timing as a contributing factor.

### Fixture, database lifecycle and concurrency evidence

- The only retained change to `AuthenticationHostFixture` is a read-only internal connection-string accessor. Its unit-of-work, transaction, HTTP, database creation, disposal and pooling behavior match develop.
- Each host uses its own GUID-named disposable LocalDB catalog. The new Swagger hosts dispose before guarded, catalog-scoped cleanup; their cleanup does not call process-wide `ClearAllPools`. No shared Development connection is used.
- On a reproduced failure, the verification unit had no HTTP context and its cancellation token was neither cancellable nor cancelled. A new logical `SqlConnection` reused the preceding HTTP operation's physical `ClientConnectionId`; the SELECT failed with 995 and the connection transitioned from Open to Closed. This identifies the failing transport boundary, not the reason native I/O was aborted.
- A diagnostic control with pooling disabled passed both tests. After restoring pooling, a confirmation plus three repeated concurrent-test runs passed, followed by all 16 authentication-flow tests and the full 176-test suite. Passing and failing reads both reused pooled physical connections. Therefore disabling pooling is **not proven to fix the cause** and is not retained.
- A minimal provider probe tested ten pooled and ten unpooled SELECT-only cases after an issuing thread exited; all passed. A second probe exercised an expected temporary-table duplicate-key exception and rollback; all twenty cases passed. Neither establishes a SqlClient, Windows or SQL Server defect.
- The original failing full run also used serialized test projects. Project serialization is not a demonstrated fix. The actual concurrent HTTP requests (`Task.WhenAll`), SQL transactions, uniqueness/FK checks and authorization pipeline remain intact. No test is skipped, weakened, retried or delayed to obtain a passing result.

The additional full-suite failure cannot presently be attributed conclusively to either the new tests or existing infrastructure. No confirmed application-code regression was found. Temporary SQL interceptors and diagnostic middleware were removed, and the pooling experiment was reverted. No speculative production change is included.

### Remaining diagnostic work

The lower-level trigger for error 995 remains unresolved. A green rerun is not proof that the intermittent fault has been repaired. For the next reproduction, collect a bounded, sanitized SqlClient/SNI event trace together with SQL session/disconnect, transaction and thread-lifecycle evidence, correlating physical connection IDs and timestamps. Do not enable SQL parameter or token/body logging. If this is specific to LocalDB/native named pipes, compare the unchanged tests against a disposable SQL Server instance over TCP with normal pooling. No package upgrade or production pooling change is justified by the current evidence. Microsoft's [pooling guidance](https://learn.microsoft.com/en-us/sql/connect/ado-net/sql-server-connection-pooling?view=sql-server-ver17) describes pool-scoped diagnostics and the distinction between logical and physical connections; it does not diagnose this particular error.

## Verification

Fresh verification on 2026-11-03, with normal fixture pooling and all temporary diagnostics removed:

| Check | Actual result |
| --- | --- |
| Reproduce Swagger defect before fix | Repair regression failed because persisted ClientUri remained localhost instead of the public value |
| Clean Release solution build | Passed: zero errors, one existing CS0162 warning at `STNGHttpApiHostModule.cs:343` |
| Swagger regression selection after clean build | **4 passed /0 failed /0 skipped**, including real HTTP redirect acceptance/denial |
| Concurrent-registration selection after clean build | **1 passed /0 failed /0 skipped** |
| Constraint-test selection after clean build | **1 passed /0 failed /0 skipped** |
| Full-suite control before clean build | **176 passed /0 failed /0 skipped**: Domain 39, Application 17, EF Core 40, HTTP 80 |
| Full suite after clean build | **176 passed /0 failed /0 skipped**: Domain 39, Application 17, EF Core 40, HTTP 80 |
| Untouched develop initial full comparison | **171 passed /1 failed /0 skipped**; concurrent registration also failed individually |
| EF CLI after clean build | No pending model changes; EF/migration source unchanged |
| Development and Production YAML | Previously parsed successfully using installed YamlDotNet; no dependency added |
| Production/runtime configuration | Unchanged; only the Development DbMigrator step receives the new setting |

The test-support `STNG.TestBase` project contains no tests; VSTest reports zero available tests there. The four suites above account for all 176 discovered tests. Local runtime versions are .NET SDK 10.0.403, EF Core 10.0.9, Microsoft.Data.SqlClient 6.1.1 and SQL Server LocalDB 17.0.4025.3.

**Verdict: Ready for a standalone, conditional PR review.** The exact four-file Swagger configuration change has passing local verification. Reviewers must explicitly accept the documented unexplained baseline SQL 995 risk and the still-unattributed additional historical full-suite failure; neither is claimed fixed by the subsequent green runs. Parent token HTTP 500 remains a separate unresolved Development blocker. This conditional review assessment does not approve merge, deployment, shared seeding, Phase 2 publication or production readiness.

## Remaining approval requirements

- **Publication authorized for review only:** commit the four reviewed files, push `fix/development-auth-registration` and open the standalone PR targeting `develop`. The publication preflight fetched `origin/develop` and confirmed the unchanged base SHA above and the exact four-file scope. Rerun applicable verification if the base or implementation changes. The only update to the verified implementation in publication preparation is documentation of the new operator-reported SQL login evidence and review-only authorization.
- **Review and CI:** reviewer acceptance of the narrowly scoped fix, seeding side effects, rollback plan and unresolved SQL risk, with required GitHub CI green on the published revision. The published PR checks are authoritative for CI status; the table above records local verification. A recurrence of 995 must retain its diagnostic evidence; do not silently retry it into a green claim. Successful CI does not authorize merge or deployment, and auto-merge must remain disabled.
- **Merge and Development operations:** separate authorization for merging and the resulting Development CI/CD, including its full DbMigrator schema/seed scope against the explicitly approved target. Merging into develop currently triggers that deployment path; PR publication alone does not.
- **Operational verification:** operator access/approval for protected preflight metadata review, any shared seed run, any necessary worker/cache refresh and interactive OAuth using an approved test identity. The persisted-registration and browser gate above must be recorded after the authorized run. Any Family mutation needs explicit test-resource scope. The Parent HTTP 500 remains open until its server exception and independent verification are available.

Evidence is ignored under `artifacts/auth-failure-resolution-20261103` in `F:/STNG/STNG-auth-registration-fix`, with `resolution-*.trx` results in `test/*/TestResults`. The earlier full runs are retained under `artifacts/auth-registration-fix` and the untouched baseline's `artifacts/auth-registration-baseline`; new focused baseline reruns are under its `artifacts/auth-failure-resolution-20261103`. Host tests use disposable LocalDB catalogs; provider probes only read or roll back session-local temporary tables. Four catalogs left by an earlier cleanup attempt were individually inventoried, verified to be owned by that run and removed; no shared target was used.

The fix branch is `fix/development-auth-registration`, based on `365aed9de7c8dfd13cd796646b943fe543b95917`, with only the Development workflow, regression file, internal fixture accessor and this document in scope. The staged diff is manually reviewed and scanned for credential/token/private-key patterns; no dedicated external secret scanner is installed. Its standalone commit and PR identify the published revision. Merge, deployment and shared database operations remain unauthorized and have not been performed for this fix. The original 46-file mixed Family checkout and private checkpoints remain untouched; Phase 2 remains clean at `1c0d7209d03d9147880ce8d88c380de960a35b9e`. Preservation hashes matched all 880 selected source/report/configuration files, including files previously checked.
