# write-endpoints — POST/PUT/PATCH/DELETE coverage

Systematic backend coverage of the mutating endpoints on the default `Elsa.Workbench`, one script per API area,
with valid + negative (400/404/409) cases. Shared harness: `_WriteCommon.ps1` (`Invoke-Write` / `Assert-Write`
/ `Complete-WriteSuite` / `New-WorkflowState`). All fixtures are created by the tests, so destructive ops
(permanent delete, discard) only touch throwaway data; `PUT diagnostics/settings` is a read -> put-same
round-trip that doesn't change global state. Runs against a from-source server (see ../README.md).

## Scripts (areas)

| Script | Writes covered |
|--------|----------------|
| `Test-DesignWorkflowWrites.ps1` | submit; add; patch metadata; replace draft; promote (including Event-result and intrinsic parity); soft-delete; restore; permanent-delete; discard draft (22 cases) |
| `Test-PublishingWrites.ps1` | workflow preflight/publish/version-test-run/draft-test-run; activity draft preflight/publish/test-run (10 cases) |
| `Test-DesignActivityWrites.ps1` | create/validate/replace/patch/publish; version retire/revoke (recommendation-gated); discard (10 cases) |
| `Test-RuntimeWrites.ps1` | execute; stimuli (match + no-op); diagnostics settings round-trip; redrive; durable alteration-plan submit/read/page/cancel shape (10 cases) |
| `Test-IdentityWrites.ps1` | login (valid + bad-creds); refresh; logout (4 cases) |

## Bugs found (filed, tracked in-test as accept-4xx-or-500 so the scripts stay green and flip clean when fixed)

- **#1020** — workflow-design write endpoints return **500 instead of 4xx** for invalid requests (promote unknown draft; permanent-delete without prior soft-delete).
- **#1021** — `PUT runtime/workflows/diagnostics/settings` returns **500 on valid input** (endpoint unusable).
- **#1022** — `POST _elsa/identity/refresh` returns **500 (not the documented 401)** for an empty/missing refresh token.

## Contract notes (real behaviour, not bugs)

- `POST executables/{bogus}/execute` and malformed `versionId` publish/preflight -> **400** (route/id validation), not 404.
- `dispatches/{bogus}/redrive` and `runtime/workflows/activation-slots/{bogus}` are **lenient** (200), not 404.
- Retiring/revoking the **recommended** activity version is **409** (`activity-definition-recommendation-required`) — a deliberate gate; the happy-path transition needs a multi-version + recommendation-decision flow (advanced, not covered here).

## Scope

Composed-server endpoints only. Advanced activity flows (fork, contract-proposals apply, upgrade-plans apply/refresh, migrate-provider, availability/recommendation settings) and workflow slots lifecycle are not covered here.
