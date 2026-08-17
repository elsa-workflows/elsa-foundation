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

The public request/response/command contract surface is emitted by the stable
`Elsa.Workflows.Design.Api.Core` assembly; the implementation assembly forwards those types for
source compatibility. Every documented route appends `RequireStableOpenApi()` after its owner
metadata, and the Workbench plus collectible owner test host register the public API Explorer
refresh bridge.

The feature keeps virtual `ConfigureServices`/`MapEndpoints` seams, module-owned catalog
contributions, source-generated JSON for every mapped payload (including `PreflightDraftPromotion`),
and the shared normalized permission evaluator. Route metadata declares only the catalog action;
implied/manage and wildcard behavior is evaluator-owned.

## Immutable before evidence

The immutable before evidence is tied to the resolvable pre-migration source commit and the checked-in
capture runner inputs rather than to an intermediate runner commit (which a squash merge may discard):

```text
67ba4b3b9  pinned pre-migration FastEndpoints source
checked-in  tools/compatibility/ capture runner and projector inputs
immutable  78-case HTTP/OpenAPI/handler-trace fixtures
3157570ab  squash-merged Minimal API migration
```

The checked-in runner is copied from the checkout under test and identified as `checked-in-worktree`; its
seven runner/source dependency paths are individually hashed and bound by fingerprint
`6a6254f7c91c7bec695bfcdca305d81ce1809e826f4240e4324a8d5e7d6207b0`. The source commit
`67ba4b3b9bec3a6c2aac0d6d332099baf723e802` must resolve, be an ancestor of the current checkout, and
contain the expected FastEndpoints feature/endpoint surface. The capture was executed detached against that
pre-migration source and captured all
27 OpenAPI operations and 78 uniquely keyed HTTP observations: anonymous 401s for every route, authenticated
success and failure paths, one
authenticated route case for every route, exact binding/content-type failures, ProblemDetails/domain errors,
paging/filtering, headers, concurrency, preflight nonmutation, and permanent-delete status outcomes. The
handler trace is canonically sorted and two independent captures produce the same hash. Fixture hashes are:

| Fixture | SHA-256 |
| --- | --- |
| `workflows-design-http-fastendpoints.json` | `25175448d9f3003dc28f879aea0b6e897c35b498b4a9c47a7aec7d40f81867fb` |
| `workflows-design-openapi-fastendpoints.json` | `cab09ec395c74329bcad1a40346c5912c00fd54c076f588f89bd70c457298dc5` |
| `workflows-design-handler-trace-fastendpoints.json` | `02dfcb7bbc50d64ea785df897128b8bc39caeec2709444b8fc34d91a04f6133c` |

The receipt records the checked-in runner identity/fingerprint, source commit, counts, categories, and fixture hashes.
The after comparison consumes all 78 HTTP cases and 27 OpenAPI operations with no content-length
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
- Three collectible architecture cycles invoke the authentication/authorization middleware, mapped
  descriptor delegate, provider registration, source-generated serialization, endpoint publication,
  DI disposal, and weak-reference unload checks.

## E2E evidence

On commit `a76f34d12121ab5b3c6a025d224bddab92226b5d`, the Workbench was rebuilt with `--no-restore`,
the existing SQLite files were moved to a recoverable temporary directory, and the schema was applied to a
fresh `src/Apps/Elsa.Workbench/elsa-groundwork.db` using the documented reference composition command. The
server ran from that exact executable at `http://localhost:5095`.

```text
pwsh -NoProfile -File e2e-tests/workflow-version-override/Test-WorkflowVersionOverride.ps1 -BaseUrl http://localhost:5095
SUCCESS - workflow exact-version promotion: 5/5 write endpoints/cases passed

pwsh -NoProfile -File e2e-tests/get-endpoints/Test-DesignWorkflowGets.ps1 -BaseUrl http://localhost:5095
SUCCESS - workflow-design API: 8/8 GET endpoints/cases passed
```

These runs exercised real login/authentication, design persistence, preflight, exact promotion,
immutable version reads, list/get/version/draft/validation reads, and 404 handling.
The evidence-only commit recording this run follows it and changes documentation only; it does not alter
the Workbench executable or production source exercised above.

## Verification record

- Workflows Design API tests: `dotnet test tests/Elsa/Workflows/Design/Api/Tests/Elsa.Workflows.Design.Api.Tests.csproj --no-restore` passed 148/148; the immutable baseline suite passed 9/9, including source/runner ancestry and dependency-byte checks.
- Architecture: the owner collectibility suite passed 1/1 (seven owners across three real collectible cycles), the integrated transition suite passed 2/2 with the 85-entry ratchet, and the full Architecture suite passed 472/472.
- Full solution build: `dotnet build Elsa.Server.slnx --no-restore` passed with 0 errors (218 repository warnings only).
- Maps: `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` followed by `... -- check` passed; generated snapshots and `docs/maps/manifest.json` are included.
- Format/import verification: focused verification passes for all changed production, test, architecture, comparer, and OpenAPI projector files. The broad project verification still reports inherited charset/import diagnostics in untouched base files; these are recorded as an advisory baseline issue rather than normalized into this migration.
- E2E: the rebuilt Workbench at final executable commit `a76f34d12121ab5b3c6a025d224bddab92226b5d`, with a fresh SQLite DB, passed `Test-WorkflowVersionOverride.ps1` 5/5 and `Test-DesignWorkflowGets.ps1` 8/8 against `http://localhost:5095`; commands and results are recorded above.
- Final review: issue comments/open PRs were rechecked before commit; the final diff is checked for whitespace, exact 27-route removal, production FastEndpoints references, and map/spec consistency.

## Risks

The OpenAPI generator and SQLite Groundwork schema are host-owned surfaces; the recorded historical
receipt, generated-map check, rebuilt fresh-DB E2E, and full architecture/build gates are the required
guards against host composition drift. The framework constitution's §2.24 and Elsa constitution's
§E2.9 remain provisional; this work relies on the accepted ADR and does not ratify either section.
