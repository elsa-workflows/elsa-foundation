# Implementation Plan: Wave 6 Workflows Design API Review Corrections

**Branch**: `codex/1372-wave6-workflows-design-minimal-apis` | **Date**: 2026-08-16 | **Spec**: [spec.md](spec.md)

## Summary

Close review round 1 findings for issue #1372 while preserving the existing two-commit baseline-first
history. Add a real FastEndpoints-era capture host and immutable evidence before any corrective code,
then correct source-generation, permission metadata, exact compatibility, authorization/coexistence,
semantic, collectibility, documentation, and E2E evidence.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Minimal APIs, CShells `IWebShellFeature`, Foundation Identity
authorization/catalog, existing Workflows Design stores/services/providers, retained FastEndpoints
support for an unrelated canary, xUnit/TestServer, `Elsa.Api.Compatibility.Testing`, OpenAPI.

**Storage**: Existing Workflows Design stores; E2E uses a fresh documented SQLite database.

**Testing**: Real before/after HTTP and consumed OpenAPI capture/comparison, exact approval/mutation
bites, authorization matrix, semantic unit tests, three-cycle collectible host, architecture/build,
maps, format/diff, and backend E2E.

**Constraints**: Exactly 27 design registrations removed; no production FastEndpoints dependency;
wildcard is evaluator-level only; no blanket or one-sided approvals; stable owner-local operation IDs
and tags; baseline commit precedes corrective implementation commits; no push/PR.

## Constitution Check

*GATE: Pass against the framework and Elsa constitutions; framework §2.24 and Elsa §E2.9 remain
draft/provisional and are recorded as context rather than treated as ratified architecture.*

- Layering: capture, transport comparison, and metadata remain in tests/API boundary; domain stores,
  handlers, and provider contracts remain unchanged.
- Framework honesty: standard ASP.NET Core routes, metadata, JSON, OpenAPI, and policies are used;
  FastEndpoints is retained only for historical capture/coexistence evidence.
- Golden rule: all previous semantic test objectives regain executable coverage; immutable before
  fixtures and two-sided comparer bites guard behavior.
- Security: endpoint declarations carry catalog actions only; Foundation Identity owns implication,
  wildcard, normalized claims, resource, and tenant evaluation.
- Collectibility: real mapped delegates, DI, auth, OpenAPI, source-generated serialization, provider/
  store adapters, disposal, and weak references are exercised through three cycles.
- Subtractive change: exactly the 27 Workflows Design adapters remain removed and unrelated owners stay.

## Design Decisions

1. Add a second immutable baseline capture commit after the already-frozen baseline, containing a
   real FastEndpoints-era host and expanded all-route cases. The correction implementation follows
   that commit so history remains auditable.
2. Use separate before and after host builders with the same request corpus and consumed projections.
   Capture fixture bytes and SHA-256 provenance; compare raw headers/content types without normalization.
3. Record only exact two-sided approvals when unavoidable. Approval consumption is required in both
   directions and a deliberate fixture mutation/unused-approval test must fail.
4. Use `RequirePermission(action)` on each route. The evaluator may accept wildcard/implied grants,
   but endpoint metadata must not list wildcard as an owned action.
5. Add `PreflightDraftPromotion` to the owner JSON context and exercise its successful handler path,
   malformed/missing/non-JSON body failures, and exact response metadata.
6. Restore semantic tests through public/real service and mapped HTTP seams rather than reintroducing
   FastEndpoints endpoint types or production dependencies.
7. Keep owner-local names/tags stable independent of host application name and compare all consumed
   OpenAPI fields for all 27 operations.

## Project Structure

```text
specs/163-wave6-workflows-design-review/{spec,plan,tasks,research,data-model,quickstart}.md
specs/163-wave6-workflows-design-review/contracts/
tests/Elsa/Workflows/Design/Api/Tests/Baselines/
tests/Elsa/Workflows/Design/Api/Tests/Support/
tests/Elsa/Workflows/Design/Api/Tests/WorkflowDesignApiBeforeBaselineTests.cs
tests/Elsa/Workflows/Design/Api/Tests/WorkflowsDesignApiContractTests.cs
tests/Elsa/Architecture/
docs/reports/workflows-design-api-migration-2026-08.md
docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md
```

## Rollback and Risks

The original baseline and migration commits remain independently revertible. The review correction
will use one or more local commits after the existing map refresh; no remote history is changed.
Risks are fixture fidelity, exact header differences, OpenAPI generator variation, hidden service
dependencies in semantic tests, E2E schema setup, and generation retention; each has an executable
gate or is explicitly reported.
