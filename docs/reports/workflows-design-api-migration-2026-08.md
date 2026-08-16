# Workflows Design API migration and review corrections (2026-08)

Issue: [#1372](https://github.com/elsa-workflows/elsa-foundation/issues/1372)
Spec Kit work unit: [`specs/163-wave6-workflows-design-review`](../../specs/163-wave6-workflows-design-review/)

## Architecture reconciliation

This work implements [ADR 0068](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md):
the owner exposes an explicit public `IWebShellFeature` mapping seam, ordinary ASP.NET Core
`RequestDelegate` routes, owner-local names/tags, and Foundation Identity permission metadata. The
27 Workflows Design registrations are mapped by the owner and no production Workflows Design API
source retains a FastEndpoints dependency. FastEndpoints remains only in the immutable historical
capture tool, compatibility fixtures, and the retained test canary.

The feature keeps virtual `ConfigureServices`/`MapEndpoints` seams, module-owned catalog
contributions, source-generated JSON for every mapped payload (including `PreflightDraftPromotion`),
and the shared normalized permission evaluator. Route metadata declares only the catalog action;
implied/manage and wildcard behavior is evaluator-owned.

## Immutable before evidence

The baseline-first history is:

```text
9d10c2392  freeze FastEndpoints before evidence
1ab6ec3a7  add historical FastEndpoints capture runner
2779f1e07  record expanded FastEndpoints evidence
eee4007a5  consume OpenAPI identity metadata
e2ddf3560  bind capture to rebased FE source
ba498773b  capture against W5 main baseline
e53de6de8  Minimal API migration
f862437e4  generated maps
```

The runner was executed detached from the pre-migration runner commit `e2ddf35608e3b1ccf6a6423d4fa275faccd9ddba`
against the W5 main FastEndpoints source `ee6b9cf23f01e169fd6ce056f3c402db479d4e50`. It captured all
27 OpenAPI operations and 39 HTTP observations: anonymous 401s for every route, authenticated success,
exact binding/content-type failures, ProblemDetails/domain errors, paging/filtering, headers, concurrency,
preflight nonmutation, and permanent-delete status outcomes. Fixture hashes are:

| Fixture | SHA-256 |
| --- | --- |
| `workflows-design-http-fastendpoints.json` | `f9e19ea8b6119f8664cce18b7b23b4d229aa82ad33ee4fcc04bb02fca0103a33` |
| `workflows-design-openapi-fastendpoints.json` | `cab09ec395c74329bcad1a40346c5912c00fd54c076f588f89bd70c457298dc5` |

The receipt records the full runner commit, source commit, counts, categories, and both hashes.
The after comparison consumes all 39 HTTP cases and 27 OpenAPI operations with no content-length
normalization. Exact bidirectional approval, unused-approval, reverse-approval, and fixture mutation
tests are bite-proof. W5's transition baseline was 112 registrations; this branch removes exactly the
27 Workflows Design registrations, leaving the integrated 85-entry ratchet (Activity Design 38,
Publishing 23, Runtime 24).

## Semantic and lifecycle coverage

- Mapped expression tooling tests execute the real request delegates for empty catalogs, a 501-symbol
  provider, provider failure, authorization projection, preserved revisions, and `no-store` headers.
- Preflight tests exercise validation contributions, semantic identity conflicts, latest/version
  lookups, exact synthetic draft route precedence, and prove the draft/store remain unmodified.
- The expanded FE and after HTTP corpus covers promotion 404/409/500, permanent-delete 404/501/500,
  paging/filtering, malformed/non-JSON binding, and authenticated nonmutation behavior.
- Three collectible architecture cycles invoke the mapped descriptor delegate, authenticated
  metadata, provider registration, source-generated serialization, endpoint publication, DI disposal,
  and weak-reference unload checks.

## E2E evidence

On a rebuilt Workbench with the prior SQLite files moved aside to a temporary directory, the schema was
applied to a fresh `src/Apps/Elsa.Workbench/elsa-groundwork.db` using the documented reference
composition command. The server ran from the rebuilt branch at `http://localhost:5095`.

```text
pwsh -NoProfile -File e2e-tests/workflow-version-override/Test-WorkflowVersionOverride.ps1 -BaseUrl http://localhost:5095
SUCCESS - workflow exact-version promotion: 5/5 write endpoints/cases passed

pwsh -NoProfile -File e2e-tests/get-endpoints/Test-DesignWorkflowGets.ps1 -BaseUrl http://localhost:5095
SUCCESS - workflow-design API: 8/8 GET endpoints/cases passed
```

These runs exercised real login/authentication, design persistence, preflight, exact promotion,
immutable version reads, list/get/version/draft/validation reads, and 404 handling.

## Verification record

- Workflows Design API tests: `dotnet test tests/Elsa/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj --no-restore` passed 110/110; the immutable baseline suite passed 8/8.
- Architecture: the focused EndpointSecurity/collectibility suite passed 8/8 (three real collectible cycles), the integrated transition suite passed 2/2 with the 85-entry ratchet, and the full Architecture suite passed 441/441.
- Full solution build: `dotnet restore Elsa.Server.slnx --ignore-failed-sources` followed by `dotnet build Elsa.Server.slnx --no-restore` passed with 0 errors (repository warnings only).
- Maps: `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` followed by `... -- check` passed; generated snapshots and `docs/maps/manifest.json` are included.
- Format/import verification: focused verification passes for all changed production, test, architecture, comparer, and OpenAPI projector files. The broad project verification still reports inherited charset/import diagnostics in untouched base files; these are recorded as an advisory baseline issue rather than normalized into this migration.
- E2E: the rebuilt Workbench/fresh-DB run passed `Test-WorkflowVersionOverride.ps1` 5/5 and `Test-DesignWorkflowGets.ps1` 8/8 against `http://localhost:5095`; commands and results are recorded above.
- Final review: issue comments/open PRs were rechecked before commit; the final diff is checked for whitespace, exact 27-route removal, production FastEndpoints references, and map/spec consistency.

## Risks

The OpenAPI generator and SQLite Groundwork schema are host-owned surfaces; the recorded historical
receipt, generated-map check, rebuilt fresh-DB E2E, and full architecture/build gates are the required
guards against host composition drift. The framework constitution's §2.24 and Elsa constitution's
§E2.9 remain provisional; this work relies on the accepted ADR and does not ratify either section.
