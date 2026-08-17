# Unload-safe OpenAPI boundary

- **Issue:** [#1392](https://github.com/elsa-workflows/elsa-foundation/issues/1392)
- **Program:** [First-party REST API Consolidation #1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342)
- **Branch:** `codex/1392-unload-safe-openapi-boundary`
- **Worktree:** `/Users/sipke/.codex/worktrees/elsa-foundation-1392-openapi-boundary`
- **Base:** `67ba4b3b9bec3a6c2aac0d6d332099baf723e802`
- **Date:** 2026-08-16
- **Runtime:** .NET SDK 10.0.300; .NET runtime/reference pack 10.0.8; `Microsoft.AspNetCore.OpenApi` 10.0.10; `Microsoft.OpenApi` 2.7.5
- **Competing work check:** Issue comments and open pull requests rechecked on 2026-08-16; the existing worktree claim is the only claim and no open PR references #1392.

## Status

Delivered. [PR #1394](https://github.com/elsa-workflows/elsa-foundation/pull/1394) merged as `efe280446cafc998cbeb305bf235527eafd30b19`. The selected model is that first-party API-visible contracts use a stable `*.Api.Core` lifetime while replaceable mappers and handlers remain collectible. A final endpoint-build convention rejects API Explorer-facing artifacts from a collectible context before publication.

The pull-request CI and the required post-merge `main` gates are green:

- [PR CI](https://github.com/elsa-workflows/elsa-foundation/actions/runs/31976838502)
- [post-merge CI](https://github.com/elsa-workflows/elsa-foundation/actions/runs/31977619684)
- [post-merge HTTP workflow performance](https://github.com/elsa-workflows/elsa-foundation/actions/runs/31977619644)
- [post-merge Maps](https://github.com/elsa-workflows/elsa-foundation/actions/runs/31977619266)

Issue #1392 and Project 45 are Done. Waves #1372 and #1375 are reopened, unblocked, and In Progress against the merged boundary.

## Confirmed cause

The built-in ASP.NET Core document path retains API-description operation contexts and schema identifiers for the document service-provider lifetime. Those graphs include endpoint metadata and request/response `Type` identities. A collectible implementation remains unloadable only when everything API Explorer retains belongs to a stable host/shared contract lifetime.

Existing evidence:

- mapping without API description/OpenAPI collects;
- source-generated request/response serialization without OpenAPI collects;
- enumerating API descriptions or generating the real document retains endpoints whose request/response metadata names module-generation types;
- substituting stable request/response contract types releases the implementation generation;
- Structured Logs is the positive production control because its description method and documented contract types are framework/shared-Core owned.

The installed `Microsoft.AspNetCore.OpenApi` implementation exposes no public generation eviction seam for the internal operation-context or schema-ID caches. Private cache clearing, reflection mutation, timed eviction, and production forced garbage collection are explicitly rejected.

A second, independent framework finding came from the successful-replacement test: Endpoint API Explorer reads `EndpointDataSource.Endpoints`, but its description-group cache is invalidated by `IActionDescriptorChangeProvider`, not by the endpoint source change token. `AddDynamicEndpointApiExplorerRefresh()` bridges those two standard seams. With it, 64 document reads racing an endpoint-source replacement each observed exactly one complete old or new generation; without it, the first description collection remained indefinitely.

## Decision hypothesis

Use the existing three-layer contract model rather than introduce an Elsa OpenAPI publication framework:

1. Public request/response types for a replaceable first-party API live in an owner-scoped stable `*.Api.Core` assembly (or an existing stable Core when the model genuinely belongs there).
2. Mappers, binders, handlers, provider adapters, and source-generated runtime serialization remain in the replaceable API implementation.
3. A shared final endpoint convention validates that API Explorer-facing metadata contains no collectible type, metadata object, member/method, delegate/transformer, or serializer artifact.
4. Native ASP.NET Core API Explorer/OpenAPI remains the documentation authority.
5. Existing public namespaces and JSON contracts remain stable; moved public CLR types use type forwarding where binary compatibility requires it.
6. A dynamic host registers `AddDynamicEndpointApiExplorerRefresh()` once at its root composition so document descriptions track atomic endpoint-source generations.

A serialized, host-owned OpenAPI snapshot remains the more general option for independently authored third-party plugins that cannot share a stable wire-contract lifetime. It is deferred because it would add a new document source, snapshot schema, merge/validation engine, generation store, and host endpoint. Adopting it broadly would require a separate ADR and draft framework §2.24 review.

## Evidence matrix

| Gate | Command / evidence | Result |
|---|---|---|
| Framework-only unsafe control | `OpenApiLifetimeCollectibilityTests.Collectible_contract_metadata_is_retained_by_real_openapi_generation` | PASS: provider/delegate released; ALC, assembly, and contract type retained |
| Stable-contract combined lifecycle, three cycles | `OpenApiLifetimeCollectibilityTests.Stable_contract_metadata_releases_the_collectible_implementation` | PASS 3/3 with mapped delegate, source-generated request/response JSON, specific nested schemas, real document, disposal, unload |
| Atomic successful replacement | `Accepted_replacement_documents_one_complete_generation_before_and_after_the_swap` repeated 5 times | PASS: 64 concurrent reads per run exactly matched the complete serialized old or new OpenAPI document, with no missing/mixed operation set |
| Candidate rejection preserves previous generation | `Rejected_candidate_never_replaces_the_previous_callable_documented_generation` | PASS: candidate absent; prior route remains documented and callable |
| Structured Logs combined lifecycle | `StructuredLogsApiCollectibilityTests` | PASS 3/3 in one provider per generation: query, SSE start/cancel, exact permission authorization, generated JSON, both serialization/document orders, public API-description inspection, native OpenAPI, disposal, ALC/assembly/feature/context collection |
| Structured Logs HTTP/OpenAPI compatibility | Full `Elsa.Diagnostics.StructuredLogs.Tests` project | PASS 110/110, including immutable FastEndpoints-before HTTP/OpenAPI equality and schema-weakening mutation bite |
| Focused boundary tests | `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --filter 'FullyQualifiedName~OpenApiLifetimeCollectibilityTests|FullyQualifiedName~OpenApiLifetimeBoundaryTests'` | PASS 29/29 |
| Full Architecture suite | `dotnet restore Elsa.Server.slnx`; `dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore` | PASS 471/471 (the pre-restore attempt correctly failed only the incomplete-restore guard) |
| Full solution build | `dotnet build Elsa.Server.slnx --no-restore --nologo` | PASS, 0 errors; 200 existing aggregate warnings |
| Backend REST E2E | Rebuilt/fresh SQLite Workbench; `e2e-tests/diagnostics/Test-OpenTelemetryApiMigration.ps1`; live login + Structured Logs sources query | PASS: diagnostics smoke; Structured Logs login 200 and sources 200 JSON array |
| Generated maps | `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`; `-- check` | PASS; changed generated snapshots committed explicitly |
| Formatting and diff check | Changed-file `dotnet format ... --verify-no-changes --include <15 affected C# files>`; full-solution formatter; `git diff --check` | PASS for every changed C# file and diff check. The full-solution formatter reached the repository's pre-existing analyzer/style baseline and exited 2 with violations only in unrelated files. |
| Independent five-axis review | Two read-only rounds over correctness/security, tests/evidence, architecture/API compatibility, code quality, and operational unloadability, followed by an independent CI-correction review | PASS: 0 Critical, 0 Required. Review findings drove fail-closed enumerable/private-state/signature/serializer inspection, one-provider lifecycle evidence, full-document replacement comparison, production API Explorer invalidation, and a root-anchored evidence-project exclusion with near-match ratchet coverage. |

The repository had an existing open `main is red: Integration (nightly)` issue (#1323). Its recorded failures concern Groundwork provider evidence/lane provisioning and are unrelated to this work; the affected full build, Architecture suite, live diagnostics smoke, and Structured Logs route all passed in this worktree.

## Upstream reproduction

The checked-in reproduction at `docs/reports/repros/openapi-collectible-contract-retention/` uses only ASP.NET Core routing/API Explorer/OpenAPI and one collectible contract assembly. It was run with .NET SDK 10.0.300, runtime 10.0.8, `Microsoft.AspNetCore.OpenApi` 10.0.10, and `Microsoft.OpenApi` 2.7.5 on:

- macOS arm64 host;
- Linux amd64 in `mcr.microsoft.com/dotnet/sdk:10.0.300`.

Both produced the same bounded result:

```text
Stable metadata:      CollectionResult { Collected = True, LoadContextAlive = False, AssemblyAlive = False, ContractTypeAlive = False, DelegateAlive = False, ProviderAlive = False }
Collectible metadata: CollectionResult { Collected = False, LoadContextAlive = True, AssemblyAlive = True, ContractTypeAlive = True, DelegateAlive = False, ProviderAlive = False }
```

The framework retention is filed as [dotnet/aspnetcore#68564](https://github.com/dotnet/aspnetcore/issues/68564), with the durable branch reproduction and both platform receipts linked from the issue. Elsa's production boundary does not wait on the upstream outcome.

## Program handoff

- W6 Workflows Design received the stable `Elsa.Workflows.Design.Api.Core`, type-forwarding, final-convention, host invalidation, resolver-completeness, and combined lifecycle obligations in [#1372](https://github.com/elsa-workflows/elsa-foundation/issues/1372#issuecomment-5309897405).
- W9 Workflows Runtime received the corresponding stable `Elsa.Workflows.Runtime.Api.Core`, activity-inspection/SSE, accepts/produces, resolver-chain, and combined lifecycle obligations in [#1375](https://github.com/elsa-workflows/elsa-foundation/issues/1375#issuecomment-5309898868).
- The green gate and decision were recorded on [#1392](https://github.com/elsa-workflows/elsa-foundation/issues/1392#issuecomment-5309901671) and the [parent program](https://github.com/elsa-workflows/elsa-foundation/issues/1342#issuecomment-5309902480). The delivered state and post-merge evidence are recorded on [#1392](https://github.com/elsa-workflows/elsa-foundation/issues/1392#issuecomment-5310162546). Project 45 and the issue label are synchronized to Done; W6/W9 are resumed.

## Independent review disposition

The final review found no remaining correctness, security, compatibility, code-quality, or unloadability blocker. Two advisory observations remain:

- the Structured Logs canary's direct public API-description inspection is intentionally shallower than the focused validator graph suite; combined three-cycle collection and the exhaustive validator tests cover the deeper escape shapes;
- ADR 0069 is proposed because framework constitution section 2.24 is explicitly draft/unratified; the first-party boundary is implemented as a scoped program prerequisite, not presented as ratified general plugin doctrine.

The reviewer classified upstream filing, issue handoffs, PR publication, merge, and post-merge verification as required delivery sequencing rather than implementation defects. T031-T033 and T038-T039 are complete now that the merge and required `main` gates are verified.

## Remaining risks

- A stable contract split is sufficient only when *all* API Explorer-facing metadata is stable; module-owned operation transformers or custom metadata remain unsafe and must fail closed.
- Shared contract changes carry the documented restart/version boundary even when the implementation remains hot-replaceable.
- Existing migration waves whose collectibility tests did not combine real OpenAPI and serialization need owner-local remediation before the program closes.
- Third-party plugins without stable shared contracts remain outside this first-party boundary.
- A dynamic host that omits `AddDynamicEndpointApiExplorerRefresh()` will serve a stale first description generation even though routing updates correctly; W6/W9 handoffs therefore treat host registration as mandatory.
