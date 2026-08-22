# Research: Publishing API Minimal API Migration

## Decision 1 — Freeze evidence before production migration

**Decision**: Capture all 23 FastEndpoints registrations through a checked-in, clean-content-guarded runner against the pinned pre-migration source, run it twice, commit the immutable HTTP, projected/raw OpenAPI, approval, and receipt artifacts before deleting endpoints, and make the migrated comparer consume only those frozen artifacts.

**Rationale**: Current Publishing tests prove many semantics but directly instantiate FastEndpoints classes and are not an immutable two-host compatibility oracle. Baseline-first history prevents the migrated implementation from defining its own expected output.

**Alternatives considered**: Hand-written snapshots and post-migration capture were rejected as self-fulfilling; anonymous-only route inventories were rejected because they cannot detect binding, response, or handler drift.

## Decision 2 — Stable owner contract lifetime

**Decision**: Add `Elsa.Workflows.Publishing.Api.Core` for API-visible requests, responses, and wire enums currently owned by the replaceable API implementation. Reuse `Elsa.Workflows.Publishing.Core` types only where they already represent genuine engine/domain contracts. Preserve namespaces and public signatures and forward moved types from the old assembly.

**Rationale**: ADR 0069 proves native OpenAPI retains collectible implementation types. Publishing has no API Core today and exposes a rich graph of public transport records.

**Alternatives considered**: Leaving DTOs in the owner fails unloadability; moving transport projections into engine Core confuses API and engine ownership; serialized custom OpenAPI contributions would create an unratified framework.

## Decision 3 — Explicit mapper, unchanged engine

**Decision**: Replace `FastEndpointsFeatureBase` with `IWebShellFeature`, preserve feature identity/dependencies/services/contributors, and delegate standard ASP.NET mapping to `WorkflowsPublishingApi.MapWorkflowsPublishingApi`. Leave the endpoint-free Publishing engine and API-owned activity publication/test-run services unchanged.

**Rationale**: The engine/API split already provides the correct orchestration boundary. The migration adapts transport only.

**Alternatives considered**: Moving activity publication/test-run services into the engine was rejected because they intentionally consume `IActivityPublishingAuthorizationContext`; a new endpoint base/DSL was rejected by ADR 0068.

## Decision 4 — Exact route and binding preservation

**Decision**: Preserve all 23 templates, including the negative-lookahead `versionId` constraint excluding the reserved `drafts` literal, and implement per-route binders that make route values authoritative over body values exactly where the before contract does.

**Rationale**: Endpoint routing order can vary and the current constraint removes a real draft/version ambiguity. Activity preflight, publish, and test-run routes explicitly overwrite body IDs today.

**Alternatives considered**: Relying on route precedence or generic model binding was rejected because it can select the wrong endpoint or reverse identifier authority.

## Decision 5 — Foundation Identity remains the policy authority

**Decision**: Attach exactly `workflow-publishing.read` or `workflow-publishing.manage` through `RequirePermission`. Preserve wildcard, normalization, and configured implication behavior in the evaluator; do not invent `manage -> read`. Preserve the inner activity-publication tenant/resource context and deny before collaborators execute.

**Rationale**: Route metadata must describe owner action, not compatibility grants. The current contributor owns both keys and Runtime also consumes the read key.

**Alternatives considered**: Encoding wildcard in metadata repeats the transitional FE design; path middleware or endpoint-local claim checks split policy ownership.

## Decision 6 — Source-generated, contract-exact JSON

**Decision**: Add an owner source-generated context covering every request/response/error root and nested graph used by the 23 routes, register it first in effective HTTP JSON options, and serialize through generated metadata while preserving case-insensitive input, camel-case properties/dictionary keys, camel-case string enums, nullability, route-field omission, and opaque `JsonElement` payloads.

**Rationale**: This preserves effective FastEndpoints JSON while preventing reflection metadata from retaining the replaceable implementation.

**Alternatives considered**: Default reflection and separate ad-hoc ProblemDetails options were rejected because they weaken collectibility and can drift across error paths.

## Decision 7 — Exact differential approvals

**Decision**: Compare full HTTP and consumed OpenAPI projections. Approvals are endpoint/case/facet/value exact, two-sided, uniquely consumed, and mutation-tested; unknown, duplicate, unused, stale, no-op, broad, or false-valued entries fail.

**Rationale**: Earlier waves found that unconditional approval consumption can hide real response/schema regressions.

**Alternatives considered**: Route-level blanket approvals and normalized document equality were rejected because they suppress unchanged shared facets and stale entries.

## Decision 8 — Combined real lifecycle proof

**Decision**: Each of three collectible cycles maps all routes, authenticates and authorizes, invokes representative catalog/preflight/policy/publication/slot/test-run delegates, serializes with the owner context, generates native OpenAPI in alternating order, removes endpoints, disposes providers/stores/background resources, unloads, and verifies weak references.

**Rationale**: Mapper-only or OpenAPI-only tests do not prove the combined owner graph releases. Publishing's test-run stores and retained scopes are owner-specific risks.

**Alternatives considered**: Sleeps, private cache mutation, isolated serializer/OpenAPI tests, process-memory observation, or skipping document generation were rejected.

## Decision 9 — Preserve semantic suites and extend live E2E

**Decision**: Retain the existing publication compiler/activation/preflight/projection/slot/policy/activity/test-run suites. Run all affected Publishing and reusable-activity scripts and add one lifecycle journey for snapshot review/publish, policy CAS, slot unpublish/restore, receipts/replay, runtime preflight, activity test-run lookup/cancel, and route/body precedence.

**Rationale**: These behaviors span persistence, Runtime, Design, activities, authorization, and background resources and cannot be established by in-process contract tests alone.

## Decision 10 — Defer shared retirement

**Decision**: Remove exactly the Publishing production FastEndpoints reference and 23 registrations. Retain historical oracle/coexistence dependencies and shared FastEndpoints infrastructure until #1376.

**Rationale**: Wave 8 is one revertible owner migration; final shared subtraction needs a separate repository-wide zero-consumer audit.

**Alternatives considered**: Combining owner migration and shared retirement was rejected because it broadens rollback and review surface after the compatibility baseline has already changed.
