# Authentication rebuild report

## Git cleanup

- Final default branch: `main` (GitHub initially used `integration/auth-foundation`).
- Verified ABP initial commit: `7ce740f05b24919bd0510fe1ea87453e0804fb2e`.
- Verified emergency bundle: `F:\STNG\STNG-before-history-reset.bundle`. Created once, outside the repository, and never pushed.
- Seven old local branches deleted: `backup/pre-auth-foundation`, `chore/repo-foundation`, `feat/auth-email-verification`, `feat/auth-parent-login`, `feat/auth-parent-registration`, `integration/auth-foundation`, `test/authentication-flow`.
- One old remote branch deleted: `integration/auth-foundation`.
- One local tag deleted: `pre-auth-foundation`; no remote tags existed.
- The old remote default was successfully force-pushed with an explicit lease from `b1ddd341a28648366a0bca4c9d7fc80629fec0e6` to the ABP initial commit. `main` was published at that initial commit, made the GitHub default, and the obsolete integration branch deleted.
- `main` has not received the new implementation. No old development commits remain reachable from repository branch/tag refs. This does not assert physical deletion of objects retained by GitHub, caches or local reflogs.

## Skills read and applied

Global instructions: `C:\Users\zezot\.codex\AGENTS.md`.
No repository or parent-directory `AGENTS.md` existed.

The following installed skills were read from `C:\Users\zezot\.codex\skills\<name>\SKILL.md`:

- `git-workflow`: backup, explicit lease, branch sequence, scoped commits and integration checks.
- `abp-framework`: module wiring, Identity APIs, application services, units of work and OpenIddict extension points.
- `dotnet-ddd`: encapsulated Parent/OTP state and domain invariants.
- `efcore-persistence`: relationships, constraints, migrations and optimistic concurrency.
- `fluent-validation`: request rules and verified ABP validation interception.
- `automapper`: mapping guidance applied by preserving the generated Mapperly architecture and avoiding input-to-aggregate mapping; AutoMapper was not added.
- `dotnet-testing`: domain, SQLite integration and SQL Server HTTP tests.
- `code-quality-review`: focused review and corrections for model binding, authentication challenge responses, concurrency, email-bound OTPs and sensitive logging.

No dedicated OpenIddict/security skill was installed. The installed ABP skill and version-matched framework source were used.

## ABP and authentication

ABP 10.6.0; .NET 10, SDK 10.0.401 (the original solution has no global SDK pin); EF Core 10.0.9, SQL Server; OpenIddict 7.5.0 and FluentValidation 12.0.0. The auth server is integrated into `STNG.HttpApi.Host`.

ABP Identity owns credentials, email confirmation, security stamps, activity and lockout. OpenIddict owns access/identity/refresh tokens and persistence. Public native client `STNG_Mobile` has no secret, uses password and refresh grants, and requests `openid profile email roles offline_access STNG`.

Parent mobile password login requires a Parent profile, confirmed email, active user, valid password and no lockout. Refresh rechecks eligibility and the Identity security stamp. No custom JWT or refresh-token store exists.

## Domain and database

`Parent` contains Identity `UserId`, optional `Job`, creation audit metadata and concurrency metadata. A unique `UserId` index and Identity foreign key enforce the profile relationship. Speculative optional fields were not introduced.

`EmailVerificationOtp` contains purpose, keyed hash, expiry, attempt count/limit, sent/used/invalidated timestamps, audit and concurrency metadata. Defaults: six digits, ten minutes, five attempts, sixty-second resend cooldown. HMAC is bound to the user, challenge, purpose and email. A unique filtered index permits only one active challenge per user/purpose.

Migrations `AddParentAuthentication` and `AddEmailVerificationOtps` were generated, inspected and applied with DbMigrator to local SQL Server database `STNG_Auth_Rebuild`. HTTP tests also apply them to disposable SQL Server databases. No shared database was changed.

## Actual API routes

- `POST /api/auth/parent/register`
- `POST /api/auth/parent/verify-email`
- `POST /api/auth/parent/resend-verification-code`
- `POST /connect/token` (password and refresh requests)
- `GET /api/auth/me`

## New branches and commits

| Retained branch | Commit / purpose |
| --- | --- |
| `main` | `7ce740f` — original ABP baseline |
| `feat/auth-parent-domain` | `f16fe5b` — `feat(parent): add parent profile domain model` |
| `feat/auth-parent-register` | `457ff7e` — `feat(auth): implement parent registration` |
| `feat/auth-email-otp` | `1fbdf91` — `feat(auth): implement email otp verification` |
| `feat/auth-parent-login` | `5110548` — `feat(auth): implement parent login with OpenIddict` |
| `test/auth-flow` | `test(auth): cover parent authentication flow` — tests, regression fixes and documentation |
| `develop` | All five features integrated in sequence; same final commit as `test/auth-flow` |

Each branch was created from the latest `develop`, built, tested, reviewed, committed and fast-forward merged. Feature branches are retained and pushed. No release, tag or production deployment was created.

## Verification

Baseline: build succeeded; three tests passed.

Final test inventory: 45 tests, 42 added over baseline: five domain tests, eighteen SQLite integration tests and twenty-two SQL Server HTTP tests. Coverage includes registration transaction rollback, email delivery failure, validation, profile linkage/uniqueness, OTP expiry/attempts/reuse/resend, account eligibility, password lockout, token issuance, authenticated requests, refresh, account state changes, email changes, concurrent operations and request throttling.

Final result: **45 passed, 0 failed, 0 skipped**. The full solution restore and build succeeded, with zero errors and one pre-existing unreachable-code warning for the disabled multi-tenancy block. EF reports no pending model changes. The generated `Application.Tests` and `TestBase` assemblies contain no independently discoverable tests; their fixtures are exercised in the concrete integration projects. SQL Server tests require Windows LocalDB. Email delivery is exercised through a test sender; external SMTP delivery is not verified.

## Required deployment configuration

Supply the OTP hash secret, SMTP settings, production database and OpenIddict certificates through external configuration. Per-IP HTTP limits are per process; aggregate multi-instance limits belong at trusted ingress. Registration commits before email delivery; resend recovers from delivery failure or a crash between commit and delivery.

The preserved original commit contained a database credential. It was removed from current development configuration, but remains in the exact original commit requested for `main`; rotate it. The generated admin-password autofill script was removed from the new implementation.

See `parent-authentication.md` for runtime setup and request formats.

## Next phase — not implemented

Family → Create / Join Family → Child Identity User → QR Login → Child Devices → Device Pairing.
