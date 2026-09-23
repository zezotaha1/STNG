# Parent authentication

This implementation starts from ABP initial commit `7ce740f05b24919bd0510fe1ea87453e0804fb2e`.
`main` stays at that commit. New work is integrated into `develop`; each retained feature branch was created from the updated development line.

## Runtime

- .NET 10, ABP 10.6.0, EF Core 10.0.9, SQL Server.
- The HTTP API host contains the auth server and uses ABP Identity and OpenIddict.
- The generated application uses Mapperly. The authentication DTOs use explicit structural assignments; AutoMapper was not introduced.
- FluentValidation uses ABP's 10.6.0 validation integration. Identity remains authoritative for password rules.
- Multi-tenancy is disabled. Parent profiles and their email uniqueness constraint are host-scoped; enabling multi-tenancy requires a separate design and migration.

## Local configuration

The committed connection string targets `(localdb)\MSSQLLocalDB`, database `STNG_Auth_Rebuild`.
Override `ConnectionStrings__Default` for another authorized database. Existing ignored local configuration may override committed configuration; environment variables take precedence in the migrator.

Set `EmailOtp__HashKey` to a base64 encoding of at least 32 cryptographically random bytes through a secret store or environment variable. Use the same key on every instance. Key rotation invalidates outstanding OTPs. No key is committed.

Configure ABP email settings (`Settings:Abp.Mailing.Smtp.Host`, `Port`, `UserName`, `Password`, `EnableSsl`, `UseDefaultCredentials`, `DefaultFromAddress`, and `DefaultFromDisplayName`) through secret configuration as appropriate for the mail provider. Development no longer substitutes a sender that logs email content. Tests use an in-memory recording sender; real SMTP delivery is not exercised by tests.

OTP defaults can be overridden under `EmailOtp`: `LifetimeMinutes` = 10, `MaxAttempts` = 5, `ResendCooldownSeconds` = 60. Codes always contain six numeric digits.

Production requires an issuer, HTTPS, and OpenIddict encryption/signing certificates supplied outside source control. Retain and protect the ASP.NET Core Data Protection key ring. The original baseline contained a database password: rotate that credential. The exact preserved original commit still contains it; replacing current configuration cannot remove it from history.

The host refuses a non-Development configuration that disables HTTPS. Configure forwarded headers only for trusted proxies. The generated development script that embedded an admin password has been removed from the new implementation.

Run from the repository root:

```powershell
dotnet restore STNG.slnx
dotnet build STNG.slnx --no-restore
Push-Location src/STNG.DbMigrator
dotnet run
Pop-Location
Push-Location src/STNG.HttpApi.Host
dotnet run
Pop-Location
```

The migrator seeds the public native client `STNG_Mobile`, with password and refresh-token grants and no client secret. Password grants are accepted only for this client. Browser clients retain their authorization-code flow.

## API contract

| Method | Route | Request / result |
| --- | --- | --- |
| POST | `/api/auth/parent/register` | JSON `email`, `password`, `fullName`, optional `job`; success 204 |
| POST | `/api/auth/parent/verify-email` | JSON `email`, `otp`; 200 with `{ "verified": true }` or `{ "verified": false }` |
| POST | `/api/auth/parent/resend-verification-code` | JSON `email`; 204 for sent, cooldown, unknown, or already confirmed accounts |
| POST | `/connect/token` | Standard OpenIddict form-encoded token request |
| GET | `/api/auth/me` | Bearer authentication; Identity id, Parent id, email, full name, optional job, confirmation state |

Password token request fields:

```text
grant_type=password
client_id=STNG_Mobile
username=<parent email>
password=<parent password>
scope=openid profile email roles offline_access STNG
```

Refresh request fields:

```text
grant_type=refresh_token
client_id=STNG_Mobile
refresh_token=<OpenIddict refresh token>
```

Store and replace refresh tokens securely on the device. No custom JWT signing, password hashing, or refresh-token table is used. A mobile login requires an active, unlocked, confirmed Identity user with a Parent profile. Refresh also rechecks profile eligibility and the Identity security stamp. Two-factor requirements are never bypassed; a separate mobile two-factor flow is outside this phase.

The request limiter applies to `/api/auth` and `/connect/token`, default 20 requests/minute per source IP, configurable with `Authentication:RequestsPerMinute`. It is per process; deployments with multiple instances should enforce an aggregate limit at their trusted ingress. The database-backed OTP attempt limit and resend cooldown apply across instances.

## Persistence and delivery

`AppParents` has a required foreign key to Identity and a unique `UserId`. Profile data is encapsulated; email, passwords and security fields stay in Identity.

`AppEmailVerificationOtps` stores only a keyed SHA-256 HMAC, user and purpose, send/expiry timestamps, attempts, usage and invalidation state, and ABP optimistic concurrency metadata. The HMAC is bound to the challenge, user, purpose and current email address. A filtered unique index permits only one outstanding challenge per user/purpose. Codes are generated using cryptographic randomness and compared in constant time.

Registration commits Identity, Parent and OTP in one transaction. Email is sent after commit. A delivery failure or crash after commit leaves a valid unconfirmed account; resend is the recovery path. There is no background email outbox containing raw OTPs.

Verification commits failed attempts as well as successful consumption. Successful verification confirms email through Identity's token/confirmation APIs in the same transaction. Resend invalidates the previous challenge and replaces it atomically before sending email.

Concurrent modifications are rejected through ABP optimistic concurrency and SQL constraints, returning 409. The losing request does not bypass the attempt limit or create a second active OTP. Retry after reading the latest account state; do not blindly replay successful registration or verification.

Migrations:

1. `20260922155152_AddParentAuthentication` creates the fresh ABP schema and Parent profile table.
2. `20260922155724_AddEmailVerificationOtps` adds OTP storage and host email uniqueness.

These migrations are for a fresh database. They are not a reset script for a database containing the discarded implementation.

## Tests

```powershell
dotnet test STNG.slnx --no-restore
```

The existing EF tests use SQLite; domain tests use deterministic timestamps. HTTP tests use SQL Server LocalDB and create a randomly named `STNG_Auth_Tests_*` database, apply real migrations, seed OpenIddict, and delete that database on completion. They use the real host, Identity password verification, OpenIddict token issuance, bearer authentication and refresh. LocalDB is required for this test project. Email is captured only in test memory.

The generated `Application.Tests` and `TestBase` projects contain abstract fixtures/support code and do not independently discover tests. Their concrete integration tests run in the EF test project.

## Framework references

The implementation uses the [ABP 10.6 token controller](https://github.com/abpframework/abp/blob/10.6.0/modules/openiddict/src/Volo.Abp.OpenIddict.AspNetCore/Volo/Abp/OpenIddict/Controllers/TokenController.Password.cs) extension points. OpenIddict owns [token storage](https://documentation.openiddict.com/configuration/token-storage) and [client permissions](https://documentation.openiddict.com/configuration/application-permissions).

## Next phase

Family creation/joining, Child Identity users, QR login, Child devices and device pairing remain unimplemented.
