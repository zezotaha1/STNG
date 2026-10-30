# Family Core architecture and security review

Review date: 2026-10-27. Scope: the actual uncommitted Family Core implementation in `F:/STNG/STNG`, on `feat/parent-registration-dob-mobile`, based on HEAD `481dbdb`. This review does not certify production readiness.

This document retains the historical 139-test review checkpoint. Subsequent hardening isolated the work on `feat/family-core` at `F:/STNG/STNG-family-core`, based on fetched `origin/develop` (`d36f41b`), which includes File Management. It resolved the Medium read-loading finding and verified 165 tests. See `docs/family-core-hardening.md` for the current result.

## Result and findings

At this review checkpoint, one High issue was reproduced and fixed. No Critical issue was identified in the inspected Family Core API paths. One Medium performance issue was left unchanged under the original Critical/High fix scope and was subsequently resolved in the final hardening task. Recommendations and verification limits from this checkpoint are listed separately below.

### High — fixed: tracked identity state could defeat eligibility revalidation

Location: `src/STNG.Domain/Families/FamilyManager.cs`, `EnsureEligibleAsync`, lines 37–45.

Trigger: an identity user is already tracked in the current EF context. A second transaction commits a change that deactivates that user, revokes email verification or locks the account. A subsequent ownership acceptance calls `EnsureEligibleAsync` again, but a tracking query returns the existing object with its old eligibility values.

Impact: ownership acceptance can pass the required revalidation for an ineligible previous owner or recipient. Family.Version does not prevent this because identity-account changes do not modify the Family root. This is a security check against stale entity state, not a missing Controller attribute.

Evidence: six deterministic SQL Server tests failed before the fix: each of the three account changes was tested independently for the previous owner and recipient. In each case, `OwnershipTransferManager.AcceptAsync` returned without the expected `ParentNotEligible` exception after the independent account change had committed. The failing tests discard the outer transaction rather than persisting the unauthorized result.

Fix: scope ABP's existing `_users.DisableTracking()` around the eligibility read. The check now reads current persisted identity values without replacing or mutating other tracked objects. Soft-delete and tenant filters remain enabled. No new abstraction, package, schema change or API was added.

Verification: all six cases pass after the fix; persisted ownership and transfer status remain unchanged after rejection. Existing parent authentication tests also pass. The fresh read does not promise serialization against account changes that occur after that read.

### Medium — subsequently resolved: pagination followed an unbounded aggregate load

Final hardening replaced the read-side aggregate load with no-tracking authorized projections and verified SQL pagination plus zero Family/Member/Transfer entity materialization on large retained histories. The description below records the original finding.

Locations: `src/STNG.Application/Families/FamilyAppService.cs`, `AccessibleFamilyAsync` at line 188 and its callers `GetMembersAsync` / `GetOwnershipTransfersAsync`; `src/STNG.EntityFrameworkCore/EntityFrameworkCore/STNGEntityFrameworkCoreModule.cs`, lines 44–46.

Trigger: a family accumulates many memberships or retained ownership transfers. Every accessible-family load includes both complete collections. Paginated list methods perform that load before their separate count and paginated projection queries; even a one-item page therefore loads the full aggregate first. Family details also loads ownership history that is absent from its response.

Impact: request database work, tracking and memory grow with all retained records rather than page size. An authorized owner can grow transfer history by repeated initiation/cancellation, amplifying the cost of later reads for that family. No cross-family disclosure was found in this path.

Recommendation: authorize read-only views using a minimal server-side family/status/current-membership projection, then query only the requested DTO page. Retain complete aggregate loading where a mutation actually needs its invariants. Do not impose an arbitrary business membership limit to hide the query problem. This issue was confirmed from query composition; no production-scale load benchmark was performed.

## Architecture and domain assessment

| Area | Assessment |
| --- | --- |
| Layer ownership | Domain behavior is in Family and the managers; Application orchestrates and maps DTOs; Controllers delegate. Domain has no EF Core, HTTP or DTO dependency. No additional framework was introduced. |
| Encapsulation | Family state has private setters and its collections use read-only wrappers. Child state transitions are internal to the Domain assembly. Current Application code does not directly set child roles or statuses. Internal access remains a trusted assembly boundary rather than a language-level restriction to Family alone. |
| Owner invariant | Construction creates the owner before exposing the aggregate. Only accepted ownership transfer assigns a subsequent Owner. Owner leave/removal/direct demotion are rejected. Transfer demotes the previous owner to Admin and preserves one active owner in the committed transaction. |
| Membership | The Family/Parent key is permanent and unique. Reactivation reuses that membership, resets its role to Member and retains separate transition history. Roles are scoped to each family membership. |
| Ownership lifecycle | Current owner initiates; an active eligible recipient accepts; both parties are revalidated. Pending uniqueness, expiry, decline, cancellation and accepted-request replay are enforced. Recipient removal/leave cancels pending transfers so rejoining cannot revive them. |
| Cross-aggregate rules | Parent/identity eligibility is coordinated by domain services. The aggregate handles family-local membership and role invariants. The approved-member manager is not exposed as an unrestricted add-member API. |
| Auditing and history | Audited roots/children are retained without soft deletion; history is a separate creation-audited append-only record. Framework audit fields are not presented as complete entity-change history. |
| Boundaries and scope | Family is not an ABP tenant. Invitations, joins, children, devices and ownership recovery remain outside the implementation and were not added by this review. |

No additional confirmed DDD violation was found in the current Application-to-Domain call paths. The generic repositories and infrastructure remain trusted code; the aggregate's rules are not database-wide protection against privileged direct writes.

## Authorization and API assessment

- The HTTP Controller requires the existing ParentBearer policy backed by local OpenIddict validation for the STNG audience. The Application Service additionally requires authentication and resolves the actor using CurrentUser; DTO target IDs do not select the acting parent.
- Parent eligibility requires a host identity, active account, confirmed email, non-deleted profile and no current lockout. The corrected read addresses the tracked-identity defect above. Normal family operations do not disable data filters.
- Accessible-family queries require the current parent's active membership. Unknown and inaccessible families return the same FamilyNotFound response; family-local role requirements are also enforced by aggregate operations. No client-accessible IDOR bypass was found in the inspected routes.
- Owner/Admin/Member restrictions are enforced in Domain, including Admin restrictions against other Admins and the Owner. Fresh HTTP requests reflect role revocation without requiring a new access token. A SQL race test verifies a demotion and a mutation authorized by the stale Admin role cannot both commit.
- DTOs expose no entities, identity security state, invitation secrets or other account/profile details. Membership listing deliberately includes retained Left/Removed memberships to current active members; former members themselves receive 404. Parent IDs, roles and lifecycle timestamps are visible within that authorized view.
- FluentValidation runs through the actual HTTP/Application pipeline. Required identifiers/stamps, trimmed name bounds and pagination limits are checked. Domain name validation independently protects non-HTTP callers. Pagination has deterministic ID tie-breakers, subject to the Medium loading issue.
- Normal mutations require the current Family concurrency stamp; successful writes return the updated stamp. Creation keys are unique per creating parent. Initiation replay validates the original sender/recipient; accepted-transfer replay by its recipient does not repeat ownership changes or history.
- Business errors have explicit HTTP mappings and Arabic/English messages. Validation failures use ABP's standard 400 response; missing bearer authentication returns 401. No new exception detail exposure was introduced by the fix.

## Concurrency and rollback assessment

Family.Version increments for child changes as well as root changes and is configured as an EF concurrency token alongside ABP's ConcurrencyStamp. The conditional no-op root update checks the original version/stamp before changing children and holds the SQL transaction's write lock. SQL tests with independent units of work verify one winner for competing initiation, acceptance, membership creation, membership changes and Admin-demotion/privileged-mutation races.

The preliminary owner demotion avoids transient violation of SQL Server's filtered unique owner index. Terminal pending transfers are similarly retired before replacement inserts. These direct updates intentionally leave EF's tracked original state intact for normal persistence and root concurrency checks. They share the ambient transaction and, when supported, the outer savepoint with the remaining EF writes. They are not independent autocommit operations in the tested API path.

The review added a SQL failure after the preliminary updates: a temporary trigger in the disposable test database makes SQL Server reject EF's OUTPUT clause with error 334. The test catches the save error and queries the database before disposing the outer unit of work. It confirms the original owner, Pending transfer and original history count are already restored, proving savepoint recovery rather than merely eventual outer rollback. The failed context is discarded. The temporary trigger is dropped and is not part of any migration.

Concurrency/unique conflicts map to FamilyConcurrencyConflict. Other errors propagate after cleanup. Deadlock-victim handling recognizes SQL Server's automatic transaction rollback; this review did not force a deadlock or exercise connection-loss/commit-acknowledgement failures. No blind retry against changed intent was introduced.

## Database and compatibility assessment

Migration `20261027193809_AddFamilyCore`, its designer and the snapshot were inspected. They were not edited during this review.

- The four new tables have the expected non-null identifiers, required bounded Unicode name, audit fields and root concurrency properties.
- The `(FamilyId, ParentId)` alternate key prevents duplicate memberships, including inactive memberships. Transfer party foreign keys reference that same family/member key, preventing cross-family transfer-party references at the database level.
- Filtered unique indexes use the explicitly stored enum values: Owner/Active are 0/0; Pending transfer is 0. They enforce at most one active owner and one pending transfer per family. At least one owner is enforced by aggregate behavior and transaction boundaries, not by that unique index alone.
- Creation request uniqueness is scoped to CreatedByParentId. Family names remain non-unique. Foreign keys use restrictive deletion, retaining family/history state and avoiding new cascade paths.
- Existing Parent columns/mappings and authentication schema were not changed by the Family migration. Soft deletion remains compatible; physical parent/identity purges can now be blocked by retained family references and require a deliberate retention policy.
- File Management is absent from this checkout and was inspected read-only in the separate worktree. Its FileAssets/FileReferences table names do not collide with the Family tables. Family.ImageFileAssetId has no current FK or asset access validation, and image mutation is not exposed by these DTOs/APIs. Combined model/migration and file-resource authorization compatibility remain unverified until those branches are integrated. Do not expose arbitrary image IDs before then.

`dotnet ef migrations has-pending-model-changes --project . --startup-project . --no-build` reported no model changes. The SQL Server test also verifies snapshot consistency and applies the actual migration chain to its disposable database. No shared or production database was migrated.

## Recommendations and remaining verification limits

These are not additional confirmed Critical/High findings:

1. The Medium read-loading issue has since been resolved; see the hardening report for bounded materialization evidence and the remaining SQL count/sort cost limits.
2. Low-priority API contract recommendation: decide whether reuse of a creation idempotency key with a different payload should be rejected. Current behavior returns the existing accessible family and does not bind the key to a payload fingerprint; that behavior is not treated as a security fix here.
3. Eligibility is checked against current persisted state at the check point. Account revocation after that point is not serialized with the Family commit. Define stronger in-flight revocation semantics before adding cross-aggregate locks or isolation changes.
4. Tracked async save guards are not SQL permissions or database append-only constraints. Synchronous/direct/bulk persistence and explicitly disabled data filters remain privileged infrastructure concerns, not supported alternatives to the current aggregate/API path.
5. The tested SQL configuration supports savepoints and uses the fixture's default isolation. MARS/no-savepoint mode, snapshot-isolation variants, failover and sustained production load were not verified. Without a savepoint, a failed write must abort the ambient unit of work; swallowing an error and completing its context is unsafe.
6. Disabled-owner recovery remains an explicitly deferred production policy. No archive completion/device-safety guarantee or File Management integration was implemented or certified.

## Verification and Git state

The prior reports contained 127 passed tests: Domain 34, Application 17, EF Core 24 and HTTP host 52. Their result files were inspected rather than relying on the earlier message.

Twelve tests were added: six stale identity cases, two retained membership reactivation cases, and one each for post-demotion authorization, a mid-save SQL failure, concurrent membership creation and concurrent Admin-demotion/privileged mutation.

| Final check | Result |
| --- | --- |
| Solution build, --no-restore | Passed; 0 errors, 0 warnings in this incremental build |
| Domain tests | 34 passed |
| Application tests | 17 passed |
| EF Core tests | 24 passed |
| HTTP/SQL Server host tests | 64 passed |
| Total | 139 passed; 0 failed, 0 skipped |
| EF model vs migration | No pending changes |

Final test reports are `test/*/TestResults/family-review-final.trx`. The failing six-case reproduction and passing targeted reports are also retained under the host test's ignored TestResults directory. Build/log evidence and the pre-review file snapshot are under ignored `artifacts/family-review/`.

The review changes one production file (`FamilyManager.cs`), extends `FamilySqlPersistenceTests.cs`, adds `FamilyReviewRegressionTests.cs`, and updates/adds the two review documents. All previously uncommitted implementation files remain uncommitted. Branch and HEAD are unchanged. The existing secrets-file change was preserved and its hash matches the review-start value. No commit, push, merge, dependency upgrade or shared database deployment was performed.
