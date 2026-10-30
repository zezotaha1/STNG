# Family Core final hardening

Hardening checkpoint: 2026-10-27. Outcome: the confirmed unbounded read-loading issue is resolved and Family Core is ready for a scoped commit/PR review against the fetched develop base. No commit, push or PR was created at that checkpoint. This is not a production-readiness certification.

Revalidated on 2026-10-30 after receiving the same hardening brief again. A fresh fetch still resolves origin/develop and this worktree's HEAD/merge base to `d36f41b4647c3ebc1396853f95be54f18b078888`. The implemented read projections, SQL captures and actual plans remain present. The four saved TRX reports confirm the 2026-10-27 result of 165 passed, zero failed and zero skipped; no source/configuration changes were found after that run, so build/tests were not repeated. The EF pending-model check was run again and passed. Baseline file hashes, original HEAD/branch and the secrets hash still match; Family migration Up/Down remains byte-identical to the original. This revalidation changes documentation only; nothing was staged, committed, pushed or submitted as a PR.

## Final commit/PR verification — 2026-10-30

Following explicit authorization to commit, push and create a PR, remote refs were fetched again. The feature branch and fetched develop still shared HEAD/merge base `d36f41b`; there was no divergence or existing remote Family branch. Parent Registration and File Management were confirmed as ancestors of develop.

The complete staged scope was checked against `artifacts/family-hardening/review-files.txt`: exactly 37 implementation, test, migration, localization and documentation files. Secrets, local configuration, TestResults, SQL captures/plans and temporary artifacts were excluded. The EF migration designer and snapshot are required persistence metadata. No source behavior changed during finalization.

Fresh verification used SDK 10.0.403 and the CI Release configuration:

- `dotnet clean STNG.slnx --configuration Release --verbosity quiet`: passed.
- `dotnet restore STNG.slnx --verbosity minimal`: passed.
- `dotnet build STNG.slnx --configuration Release --no-restore --verbosity minimal`: passed, zero errors and one pre-existing CS0162 warning at `STNGHttpApiHostModule.cs:337`. The same code is present in develop and was not changed.
- `dotnet test STNG.slnx --configuration Release --no-build --no-restore --verbosity minimal --logger 'trx;LogFileName=family-finalize.trx'`: passed. Domain 39, Application 17, EF Core 40 and HTTP/SQL Server 69; total **165 passed, zero failed and zero skipped**. The TestBase support assembly contains no tests.
- `dotnet ef migrations has-pending-model-changes --project . --startup-project . --configuration Release --no-build`: passed, no pending model changes.
- The SQL script from `20261022155907_AddFileManagementCore` to `20261027193809_AddFamilyCore` was generated and reviewed: only the four Family tables and their keys, restrictive foreign keys and indexes, plus the migration history entry. No existing table alteration or drop was present. No historical migration was changed.

The host suite applied migrations only to its uniquely named disposable SQL Server LocalDB database and deleted it on disposal. Fresh evidence is in ignored `artifacts/family-hardening/finalize-*.log`, `finalize-family.sql` and `test/*/TestResults/family-finalize.trx`. The SQL/materialization tests regenerated their captures and actual plans. The production limitations below remain applicable.

## Git branch and preservation

The original checkout is `F:/STNG/STNG`, branch `feat/parent-registration-dob-mobile`, HEAD `481dbdbe79ac7f1c2544c77d457e792099c9744c`.

Remote references were fetched. `origin/develop` was `d36f41b4647c3ebc1396853f95be54f18b078888`, four commits ahead of that original HEAD. The original merge base was the original HEAD; there were no original-branch-only commits. Parent Registration commit `d918b77` was already integrated through PR #12 (`95742ba`). File Management commit `4f79c51` was integrated through PR #13 (`d36f41b`).

Family Core was safely isolated into a new native Git worktree at **`F:/STNG/STNG-family-core`**, branch **`feat/family-core`**, starting exactly at fetched `origin/develop`. Its merge base with that fetched integration ref is `d36f41b`; no Parent Registration or File Management commits need to be replayed in the Family PR.

The original checkout, all its uncommitted files and the personal secrets edit were retained in place. Secret contents were not copied into the new worktree. The new worktree was populated with the existing Family feature changes, reconciling only the shared registrations/localization/model with the already-integrated File Management code. There was no reset, stash, rebase or Git merge. The managed worktree tool rejected the chat's nested source path, so Git's native worktree command was used.

Shared File Management registrations, permissions, configuration, storage infrastructure and historical migrations remain from develop. Parent Registration contracts, entities, validators and endpoints are unchanged from develop; the existing Family cancellation-token overload in ParentAccessManager remains part of the Family changes.

## Root cause and implementation

`AccessibleFamilyAsync` used the default details query, which includes all Family.Members and Family.OwnershipTransfers. Family details, member pages and transfer pages invoked it before their own read queries. Response pagination therefore did not bound the initial entity load, especially as terminal transfers accumulated.

`FamilyAppService.cs` now has a small no-tracking query helper and a shared scalar family/current-membership projection. It uses ABP's existing DisableTracking scope, which adds AsNoTracking to the returned query and restores the repository setting immediately. No EF Core dependency was added to Application and no CQRS framework or new production abstraction was introduced.

- Details projects only its existing DTO fields and current membership role.
- My Families filters active membership/status and projects DTOs; count and page stay in SQL.
- Member listing first authorizes through the lightweight view, then repeats family, status and active-caller membership filters before counting and paging. Retained member visibility is unchanged.
- Transfer listing similarly authorizes through the lightweight view, then joins the current membership and applies Owner-or-sender-or-recipient visibility inside both count and page SQL. It does not trust a previously captured Owner boolean for data visibility. FamilyConcurrencyStamp comes from the queried family row.
- Sorting remains JoinedAt/Id for members, RequestedAt descending/Id for transfers and CreationTime descending/Id for My Families. Defaults remain 20 and maximum page size 100.
- Unknown and inaccessible families still produce FamilyNotFound/404. Active members querying an Archiving family still receive FamilyNotActive/409. Parent eligibility, including the earlier no-tracking identity fix, remains unchanged.

Mutation paths continue to load the tracked aggregate and invoke its domain behavior. Their ownership rules, root Version/ConcurrencyStamp protocol, idempotency and rollback implementation were copied unchanged. Public DTOs and HTTP routes were not changed.

## SQL and materialization evidence

Tests use the existing disposable SQL Server LocalDB host fixture. A test-only singleton interceptor records Family SQL commands and Family/FamilyMember/OwnershipTransfer/FamilyMembershipHistory entity materialization across actual HTTP requests. Observations are bounded to one request and the host collection disables parallel tests. It does not log account/token SQL or modify query execution.

The focused data sets include **131 memberships** (including retained Removed memberships) and **1,025 retained transfers** spanning an ownership handover. The non-owner visibility test includes both transfers sent while previously Owner and transfers received afterward as Member. Timestamps deliberately tie; expected ID ordering uses SQL Server's SqlGuid ordering rather than assuming .NET Guid ordering matches it.

| Read | Family data commands per request | Actual plan output rows | Family-domain entities materialized |
| --- | ---: | ---: | ---: |
| Family details | 1 | 1 | 0 |
| My Families, page size 1 | 2: count + page | 1 | 0 |
| Members, page size 13 | 3: authorize + count + page | 13 | 0 |
| Owner transfers, page size 11 | 3: authorize + count + page | 11 | 0 |
| Member transfers, page size 9 | 3: authorize + count + page | 9 | 0 |

Parent eligibility/authentication commands are separate and are not included in these Family data command counts. Unauthorized/inactive-family probes execute one lightweight Family query and materialize no Family-domain entities.

Captured member and transfer queries contain family/current-membership predicates before ORDER BY and OFFSET/FETCH. Count SQL uses COUNT_BIG without ordering or unused Name projection. Details and My Families never query the transfer/history tables. Representative transfer page shape:

```sql
SELECT t.Id, t.FromParentId, t.ToParentId, t.Status, t.RequestedAt,
       t.ExpiresAt, t.AcceptedAt, t.CancelledAt, f.ConcurrencyStamp
FROM AppFamilies AS f
JOIN AppFamilyMembers AS m ON f.Id = m.FamilyId
JOIN AppOwnershipTransfers AS t ON f.Id = t.FamilyId
WHERE f.Id = @familyId AND f.Status = 0
  AND m.ParentId = @parentId AND m.Status = 0
  AND (m.Role = 0 OR t.FromParentId = @parentId OR t.ToParentId = @parentId)
ORDER BY t.RequestedAt DESC, t.Id
OFFSET @skip ROWS FETCH NEXT @take ROWS ONLY;
```

Actual plans were captured by re-running the captured SELECTs with their test parameters using SET STATISTICS XML. They contain runtime row counters, rather than only estimated plans. The plans use existing primary keys and the membership alternate key; the transfer plans also use the filtered active-owner index. SQL Server selected a clustered transfer scan for the tested distribution, where the selected family represents much of the retained history. This is not evidence of constant database I/O: exact counts and ordering still examine relevant server rows. Existing family-prefix foreign-key indexes remain available. No additional index was justified from these plans, and none was added.

Evidence is in ignored `artifacts/family-hardening/queries/`: parameterized `.sql` captures for all four read types, and actual `.sqlplan` files for details, My Families, members and Owner/Member transfer pages. The tests explicitly assert SQL pagination, count shape, scoped parameters, deterministic contents and absence of full entity materialization. Functional pagination alone is not the verification basis.

## Regression coverage and results

Five new SQL/HTTP tests cover member pagination, transfer visibility/count/pagination, deterministic tied ordering, retained records, details/My Families projection, outsider/former-member 404 behavior and inactive-family error preservation. Existing tests continue to cover parent account revocation, eligibility freshness, membership reactivation, stale stamps, idempotency, ownership/membership concurrency, concurrent Admin revocation and failure rollback.

| Check | Result |
| --- | --- |
| dotnet restore STNG.slnx | Passed; existing package versions retained |
| Solution build, --no-restore | Passed; final incremental run has 0 errors and 0 warnings |
| Domain tests | 39 passed |
| Application tests | 17 passed |
| EF Core tests | 40 passed |
| HTTP/SQL Server host tests | 69 passed |
| Total | **165 passed; 0 failed; 0 skipped** |
| EF migration consistency | No pending model changes |

The increase from 139 is the five new read tests plus 21 existing File Management tests now included from develop. The earlier six-case eligibility reproduction remains protected by the complete host regression run. An existing CS0162 unreachable-code warning in the develop host module appeared on the initial compile; that unrelated code was not modified.

Final reports: `test/*/TestResults/family-hardening-final.trx`; focused query report: `test/STNG.HttpApi.Host.Tests/TestResults/family-hardening-query.trx`. Build, restore and test logs are in ignored `artifacts/family-hardening/`.

## Migration and File Management integration

Family migration **`20261027193809_AddFamilyCore`** retains its original ID and identical Up/Down schema operations. Its designer and the current snapshot were regenerated against the develop model containing File Management, so they describe the combined model correctly. No additional migration was created for pagination and no committed historical migration was modified.

The actual File Management → Family migration chain was applied only to the fixture's uniquely named LocalDB database. The combined model passes both the EF pending-model check and existing File Management tests. No production/shared database was changed.

Generic File Management is present, but **family image integration is still deferred**. Before exposing image mutation, use the existing FileAuthorizationService/IFileAccessPolicy infrastructure to enforce appropriate asset ownership, host/tenant and Ready status, Family resource access and FileReference coordination. No duplicate FileAsset/storage system or image endpoint was added.

## Files and readiness

Hardening-specific changes:

- `src/STNG.Application/Families/FamilyAppService.cs`: bounded read projections; mutation behavior retained.
- `test/STNG.HttpApi.Host.Tests/AuthenticationHostFixture.cs`: test-only interceptor registration.
- `test/STNG.HttpApi.Host.Tests/Families/FamilyReadProbe.cs`: SQL/materialization observation.
- `test/STNG.HttpApi.Host.Tests/Families/FamilyReadPerformanceTests.cs`: focused performance/authorization tests and actual plan capture.
- `docs/family-core.md`, `docs/family-core-review.md`, `docs/family-core-hardening.md`: current behavior, historical finding resolution and evidence.
- The existing Family registration/localization changes and uncommitted Family migration were reconciled with develop's File Management base. The original Family domain/contracts/concurrency/security implementation was otherwise preserved.

At the hardening checkpoint, the isolated worktree was ready for review, selective staging and a Family Core commit/PR; nothing had been staged, committed, pushed or submitted as a PR. The subsequent authorized finalization is documented above. The original worktree remains intentionally dirty and preserved; use the isolated worktree as the Family result.

Production limitations remain: disabled-owner recovery policy, Family image authorization/reference integration, and operational verification of no-savepoint/MARS modes, failover and sustained load. Exact count/page reads are not a single snapshot across concurrent changes, while each data query enforces family/membership/role predicates. This task did not implement Invitations, Join Requests, Children or Devices.
