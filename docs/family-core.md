# Family Core — Phase 1

Implements Family Core, membership and ownership from **STNG Family Management Domain Specification v1.0** on ABP 10.6.0, .NET 10 and EF Core 10.0.9. No package upgrades are required.

## Scope and lifecycle

`Family` is a host-scoped aggregate, not an ABP tenant. It owns `FamilyMember` and `OwnershipTransfer`. An eligible parent can own or join multiple families; each active family has exactly one active owner. Family names are trimmed, have a maximum of 120 characters, and are not unique.

| Type | Classification | Audit base | Retention |
| --- | --- | --- | --- |
| Family | Aggregate root | AuditedAggregateRoot | Archive workflow, no soft deletion |
| FamilyMember | Mutable child | AuditedEntity | Left/Removed memberships retained and reactivated in place as Member |
| OwnershipTransfer | Mutable child | AuditedEntity | Terminal decisions retained |
| FamilyMembershipHistory | Append-only record | CreationAuditedEntity | Each membership/role transition retained separately |
| FamilyName | Immutable value object | None | Ordinal value equality, stored as nvarchar(120) |

Mutation queries include members and ownership transfers to enforce aggregate invariants. Read-only family details and lists use no-tracking scalar/DTO projections without loading those collections. Membership history is persisted with the aggregate's changes but is not loaded by default. Audit fields are framework-managed; the history records additionally identify the acting parent and the membership event. Local domain events describe membership changes and completed transfers. They are not an externally delivered notification/outbox contract.

Invitations, join credentials/requests, children, devices and ownership recovery remain outside this phase. `FamilyManager.AddApprovedMemberAsync` provides the domain entry point for future approval workflows; there is no unrestricted add-member API.

`BeginArchiving` blocks operational changes and cancels pending transfers. No archive-completion API is exposed: completing archive requires the future device-safety coordinator. `RecoveryRequired` is reserved for the approved recovery workflow.

`ImageFileAssetId` and the domain image operation are present. File Management is integrated in the branch's `origin/develop` base (`d36f41b`); its existing infrastructure is retained. Image mutation is not exposed by this API. A Family resource access policy, appropriate asset ownership/tenant/status checks and FileReference coordination are still required before exposing it; do not accept arbitrary asset IDs or create another storage subsystem.

## API

All routes require the existing **ParentBearer** policy. The application resolves the acting parent from the authenticated identity and rechecks its active, verified, unlocked account and non-deleted parent profile. Client-supplied parent identities identify targets only. Each family request checks current membership and role; an inaccessible family returns the same 404 as an unknown family.

Identity eligibility is read without tracking so an identity object loaded earlier in the unit of work cannot hide a subsequently committed account deactivation, email-verification revocation or lockout. This is a fresh eligibility check, not transaction-wide serialization of later account changes.

| Method | Route under `/api/families` | Access |
| --- | --- | --- |
| POST | `/` | Eligible parent; create with owner |
| GET | `/mine` | Current parent's active families |
| GET | `/{id}` | Active member |
| PUT | `/{id}` | Owner/Admin; rename |
| GET | `/{id}/members` | Active member; includes retained memberships |
| POST | `/{id}/leave` | Member/Admin |
| POST | `/{id}/members/{parentId}/remove` | Owner, or Admin removing Member |
| POST | `/{id}/members/{parentId}/promote` | Owner; Member to Admin |
| POST | `/{id}/members/{parentId}/demote` | Owner; Admin to Member |
| GET | `/{id}/ownership-transfers` | Owner sees all; other members see transfers involving themselves |
| POST | `/{id}/ownership-transfers` | Owner; active eligible recipient |
| POST | `/{id}/ownership-transfers/{transferId}/accept` | Named recipient; both parties revalidated |
| POST | `/{id}/ownership-transfers/{transferId}/decline` | Named recipient |
| POST | `/{id}/ownership-transfers/{transferId}/cancel` | Current initiating owner |

List endpoints accept `skipCount` and `maxResultCount` (default 20, maximum 100) and return ABP `PagedResultDto`. Ordering has an ID tie-breaker. Contracts contain DTOs, never domain entities. Structural mapping follows the existing application style; AutoMapper is not installed or introduced.

Read authorization projects a single family and the caller's active membership, preserving 404 for inaccessible families and the existing inactive-family errors. Member and transfer count/page queries repeat family/status/membership filters in SQL. Transfer visibility uses the current membership role in the query: Owner sees all, other members see transfers they sent or received. Filtering and ordering precede SQL Server OFFSET/FETCH. Count queries project only COUNT_BIG; no family collections are materialized. Counts and ordering still require server-side work over the relevant rows; this is not a constant-I/O guarantee.

Create:

```json
{ "requestId": "92a1347c-4d3b-43b0-b999-e81134ea8b29", "name": "My Family" }
```

Reuse the creation `requestId` for retries. Its uniqueness is scoped to the creating parent. A new request ID permits creating another family with the same name. A concurrent conflicting creation returns 409; retrying the original request returns the existing family when the caller still has access.

Every mutation after creation requires `concurrencyStamp` from the latest family response. Transfer initiation additionally requires `requestId` (used as the transfer ID) and `toParentId` (the parent-profile ID). Transfer responses include `familyConcurrencyStamp`. Initiation retries with the same transfer identity and recipient return the existing transfer. Acceptance retries by the same recipient after successful acceptance are harmless, including retries with the original stamp. Rejected stale writes require refreshing the family; they are not automatically retried against changed user intent.

Acceptance changes the previous owner to Admin and the recipient to Owner in the same transaction. Leaving/removing a pending recipient cancels that transfer; rejoining does not revive it. Expired pending transfers are retired when a replacement is initiated. Expiration is exclusive: acceptance at `ExpiresAt` is rejected.

The configurable technical default is 48 hours:

```json
{ "Family": { "OwnershipTransfer": { "Lifetime": "2.00:00:00" } } }
```

## Persistence and concurrency

Migration: `20261027193809_AddFamilyCore`. It adds four tables and references existing parents. It does not alter existing parent/authentication data. Foreign keys use restrictive deletion so deleting a related row cannot silently remove retained family state.

The `(FamilyId, ParentId)` alternate key prevents duplicate memberships. Filtered unique indexes enforce at most one active owner and one pending transfer. A unique `(CreatedByParentId, CreationRequestId)` index protects creation retries. Transfer party foreign keys reference memberships in the same family.

Every aggregate mutation increments `Family.Version`, an EF concurrency token, including child-only changes. ABP's root concurrency stamp remains enabled. Persistence obtains a transaction-scoped root write lock using a conditional no-op version update before writing children. It demotes the previous owner before promoting the recipient, and retires terminal pending requests before inserting replacements, to satisfy SQL Server's immediate filtered-index checks. These steps and normal EF persistence share one transaction/savepoint; failure rolls them back together. A losing writer receives `STNG.Family:1005`.

Direct tracked child mutations without a changed root, physical deletion of family records, and modifications of membership history are rejected by persistence. As with any aggregate, privileged raw SQL can bypass domain behavior. The unique owner index enforces a maximum of one owner; the aggregate creation and transfer protocols enforce the minimum of one.

These guards run in the tracked asynchronous save path. Direct/bulk EF operations and synchronous persistence must not be used as substitutes for aggregate operations. If savepoints are unavailable, a failed write requires rollback of the ambient transaction. Discard a failed context rather than completing or retrying its stale tracked changes.

The ordered writes explicitly account for [EF Core ExecuteUpdate transaction and tracking behavior](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete). Production writes use ABP transactional units of work; SQL Server concurrency tests use separate scopes and transactions.

## Errors

Business errors use the standard ABP remote error envelope with localized Arabic/English messages. Explicit HTTP mappings are registered in the HTTP API module:

| Code (`STNG.Family:` prefix) | Meaning | HTTP |
| --- | --- | --- |
| 1001 | Family not found/inaccessible | 404 |
| 1002 | Family not active | 409 |
| 1003 | Invalid domain family name | 400 |
| 1004 | Family archived | 409 |
| 1005 | Family concurrency conflict | 409 |
| 2001 | Membership already exists | 409 |
| 2002 | Membership not found | 404 |
| 2003 | Membership inactive | 403 |
| 2004 | Insufficient family role | 403 |
| 2005 | Owner cannot leave | 409 |
| 2006 | Owner cannot be removed | 409 |
| 2007 | Invalid role transition | 409 |
| 2008 | Parent not eligible | 403 |
| 3001 | Transfer already pending | 409 |
| 3002 | Invalid ownership recipient | 409 |
| 3003 | Transfer expired | 409 |
| 3004 | Transfer not pending | 409 |
| 3005 | Transfer conflicts with current state | 409 |

Malformed requests and FluentValidation failures use ABP's normal 400 validation response with field errors. Missing bearer authentication returns 401. Error codes, rather than translated messages, should drive client behavior.

## Verification

Domain tests cover creation, name boundaries/equality, independent roles, membership retention/reactivation, role restrictions, owner protection, transfer acceptance/idempotency/expiry/cancellation and the archive boundary. Host tests exercise the real HTTP/authentication/application pipeline on a disposable SQL Server LocalDB database. SQL tests apply the actual migrations and cover database constraints, rollback, competing initiation/acceptance/membership writes, retained history, concurrency tokens and model/snapshot consistency. The existing EF Core regression suite uses SQLite; SQL-specific behavior is verified by the host suite.

```powershell
dotnet build STNG.slnx --no-restore
dotnet test test/STNG.Domain.Tests --no-restore
dotnet test test/STNG.Application.Tests --no-restore
dotnet test test/STNG.EntityFrameworkCore.Tests --no-restore
dotnet test test/STNG.HttpApi.Host.Tests --no-restore
```

The host fixture requires `(localdb)\MSSQLLocalDB`, creates a uniquely named test database, applies migrations, and deletes that database on disposal. No migration is applied to a shared environment by this implementation workflow. Production readiness still requires the deferred disabled-owner recovery policy and the File Management integration above.

The historical architecture/security review is recorded in `docs/family-core-review.md`. The remaining read-loading finding was resolved during final hardening; current branch, SQL evidence, integration and verification details are in `docs/family-core-hardening.md`.
