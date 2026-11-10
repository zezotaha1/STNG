# Family Join Requests — isolated browser evidence

Stage A's mandatory gate passed before Phase 2. The separate hosting PR retains the original Stage A report. This document contains only the isolated Phase 2 and review evidence extracted from the preserved mixed report.

## Phase 2 follow-through — isolated environment

After the mandatory gate passed, Phase 2 was implemented locally; see [Family Join Requests](family-join-requests.md). On **2026-10-30**, the final clean Release run finished at **18:09:54 Cairo (16:09:54 UTC)** with **217 passed / 0 failed / 0 skipped** .NET tests: Domain 55, Application 19, EF Core 40, HTTP/SQL/browser 103. Build passed with zero errors and the existing CS0162 host warning. EF reported no pending model changes, and the generated migration script adds only the two join tables and their constraints/indexes.

The new real-browser test used visible Microsoft Edge/Playwright against loopback **HTTPS Kestrel**, strict TLS validation, an isolated disposable SQL Server database and captured test email delivery. Three new Parents were registered and verified through Swagger with the actual generated verification codes. All credentials and OTPs stayed in memory/process input. No shared Development database records or configuration were used to construct membership fixtures.

That browser run recorded **135 Passed / 0 Failed / 0 Blocked assertions** (one passing .NET browser test). It exercised manual credential issuance and metadata-only replay; Pending submission without access; approval/retry and Member access; Member administration denial; rejection/reapplication; opaque QR submission/cancellation; Owner promotion, Admin restrictions, removal and Member-only reactivation; ownership transfer/acceptance, exactly one active Owner and previous Owner as Admin; subsequent demotion and leaving; stale/expired acceptance rejection; and cross-family isolation. The previously deferred extended membership/ownership coverage is therefore completed **locally** through the legitimate joining workflow. Complete interactive Swagger authorization-code login remains outside the recorded browser coverage; the existing public Parent grant was used.

Shared Development totals above remain **10 Passed / 1 Failed / 3 Blocked groups** because Phase 2 has not been deployed there. In particular, shared API-to-HTML redirects still exist; passing local middleware tests do not resolve that deployed finding. The mandatory Stage A gate is **ready to close**, with that Medium follow-up retained.

Fresh local evidence: ignored `artifacts/family-join/final-{clean,build,tests}.log`, `browser-results.json`, `test-summary.json`, `migration-consistency.log`, `migration.sql`, and `test/*/TestResults/family-join-final.trx`. Browser output contains assertion names, expected HTTP outcomes and pass/fail only. The earlier expanded-browser failure was an outdated isolation expectation for a Parent that had just legitimately joined; it was corrected before this final run. The credential lookup/stamp regression and SQL rollback checks also pass. No files are staged, committed, pushed, merged or deployed.

Latest local PR-readiness rerun (2026-10-30 **19:42:45 Cairo**): **219 tests /137 isolated Edge assertions passed**, zero failures/skips. The review added disposable migration upgrade/rollback/replay and audit-secret tests, and strengthened browser completion/error-code checks. See [PR-readiness report](family-join-pr-readiness.md). Shared Development results above remain unchanged; no deployment occurred.


## Independent feature-branch rerun — 2026-11-01

Visible Edge/Playwright over isolated HTTPS Kestrel passed **137 assertions, 0 failed**. Parent registration/verification, join review, role and ownership flow, expiry, and isolation checks completed. The feature-only solution passed **212 tests, 0 failed, 0 skipped**; hosting/API error tests are in PR 1. Only disposable local test databases and captured test email were used.
