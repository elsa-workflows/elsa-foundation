# Research: Activities Design API Minimal API Migration

## Decision 1: Capture the real FastEndpoints service before migration

**Decision**: Produce the method/path inventory, authenticated and anonymous HTTP corpus, and native OpenAPI
document from a real historical FastEndpoints owner before any endpoint deletion. Archive the exact runner and
its dependent source identities in a receipt, and make the capture script fail when its working-tree content
differs from the committed runner.

**Rationale**: The owner contains rich binder, cursor, provider-payload, lifecycle, and upgrade behavior that
cannot be reconstructed reliably from endpoint declarations. A baseline-first commit prevents the migrated host
from becoming its own oracle.

**Alternatives considered**: metadata-only snapshots (reject: no handler/binding behavior); hand-authored JSON
expectations (reject: self-fulfilling); copying the current runner while executing it (reject: not hermetic).

## Decision 2: Use a stable owner API Core for public wire contracts

**Decision**: Add `Elsa.Activities.Design.Api.Core`; compile API-visible request/response models there under their
existing namespaces, reference it from the implementation, and forward moved public types from the former API
assembly where binary compatibility requires it.

**Rationale**: Spec 165 proved native API Explorer/OpenAPI can retain collectible request/response `Type` metadata
even after provider disposal. Stable contract lifetime makes that retention safe while the implementation
generation remains collectible and preserves native OpenAPI schemas.

**Alternatives considered**: reflection/cache cleanup (reject: private/timing-dependent); `object` schemas or
omitted OpenAPI (reject: contract loss); putting API projections in Activities Design Core (reject: API read
models belong to the API sub-domain).

## Decision 3: Keep one thin owner mapper and existing domain handlers

**Decision**: Add one explicit mapper over standard route groups and have the feature expose it through
`IWebShellFeature`. Bind and translate transport values at the API boundary, then call the existing handlers,
services, stores, and provider contracts.

**Rationale**: This creates a consistent first-party REST authoring style without rebuilding FastEndpoints as a
custom abstraction and preserves the already-tested domain behavior.

**Alternatives considered**: one class per Minimal endpoint (reject: retains class boilerplate without benefit);
shared Elsa endpoint DSL (reject: recreates framework coupling); rewrite domain behavior inline (reject: golden
rule and branch-coverage risk).

## Decision 4: Keep wildcard and implication out of route ownership

**Decision**: Route metadata declares exactly the module catalog action. Foundation Identity owns authentication
normalization, permission implication, wildcard compatibility, tenant/resource handlers, cancellation, and
replaceable evaluation. Provider-authoring and provider-payload decisions remain distinct inner resource checks.

**Rationale**: Endpoint inventories stay meaningful and Minimal/FastEndpoints coexistence uses one authorization
authority. A route-level wildcard requirement would make an evaluator compatibility grant appear module-owned.

**Alternatives considered**: `RequireAnyPermission(wildcard, action)` (reject: wrong ownership); path middleware
(reject: hides requirements); direct claim matching in the handler (reject: bypasses normalized policy semantics).

## Decision 5: Compare consumed, exact HTTP and OpenAPI evidence

**Decision**: Reuse the shared compatibility-testing seam. Every approval identifies a precise route/case/facet,
real before value, real after value, reason, and disposition. The validator rejects duplicate, unused, no-op,
one-sided, unknown-property, wrong-value, and stale-document approvals; mutation tests assert exact diagnostic
keys/messages.

**Rationale**: A generated document or populated approval file is not evidence unless every claimed facet is
consumed against the real artifacts. Exact comparison also catches operation ID/tag/security/schema regressions.

**Alternatives considered**: normalization of volatile-looking headers (reject unless a specifically proven
field is approved); compare only route/method/status (reject: misses consumed client contracts).

## Decision 6: Combine real OpenAPI and request execution in every unload cycle

**Decision**: Each of at least three collectible generations maps routes, authenticates and authorizes callers,
invokes representative catalog/authoring/availability/dependency/upgrade delegates, binds and source-generates
JSON, generates native OpenAPI, disposes scopes/provider, removes endpoints, unloads the context, and asserts all
owner weak references die within the established bounded GC loop.

**Rationale**: Separate shallow tests can each pass while their combination leaks. Real OpenAPI generation is the
known high-risk lifetime path and may not be omitted or replaced with metadata inspection.

**Alternatives considered**: process-memory observations (reject: no root evidence); synthetic delegates only
(reject: misses owner captures); delayed eviction, cache clearing, or unbounded GC (reject: non-production hacks).

## Decision 7: Extend live E2E to the upgrade path

**Decision**: Retain the existing Activities GET and reusable-authoring scripts and add a focused upgrade-plan
script that authors and publishes successive reusable versions, creates/reads/applies/refreshes a dependent
upgrade plan, and verifies exact version pinning plus the staged handoff draft against fresh SQLite.

**Rationale**: Upgrade routes coordinate multiple stores and revisions and are the least adequately covered by
simple GET/write smoke tests. They are also the area most likely to expose binding or result translation drift.

**Alternatives considered**: rely only on in-process unit tests (reject: misses host composition and persistence);
run every reusable suite as the only gate (reject: costly without directly covering upgrade route semantics).

## Decision 8: Preserve semantic test objectives before deleting endpoints

**Decision**: Inventory every owner test whose subject/objective is an endpoint, binder, handler, provider/store
interaction, cancellation path, or authorization contract. Rewire setup to the mapper or stable handler seam; do
not delete or weaken cases. Record architect approval if a subject genuinely ceased to exist.

**Rationale**: The constitution's golden rule treats refactor-driven test deletion as a correctness failure.
Activities Design already has deep reusable-authoring and upgrade tests that are more valuable than mechanical
route coverage.

**Alternatives considered**: replace suites with one compatibility snapshot (reject: snapshots cannot prove
provider/store non-invocation or internal outcomes); retain endpoint classes test-only (reject: masks production
dependency retirement).

## Decision 9: Preserve the effective wire serializer contract explicitly

**Decision**: Configure the owner source-generated JSON context for case-insensitive input, camel-case property and
dictionary keys, and camel-case string enums. Include every request, response, page, cursor, diff, lifecycle,
provider-payload, problem, fork, and upgrade type referenced by accepts/produces metadata, and assert the effective
resolver chain reaches the stable owner context before any reflection fallback.

**Rationale**: These options are currently supplied globally by the FastEndpoints configurator. Removing the
framework without restating them would silently reject formerly valid input casing or change dictionary and enum
wire shapes even if canonical happy-path fixtures remained green.

**Alternatives considered**: host-default JSON options (reject: not equivalent); reflection fallback (reject:
collectible lifetime leak and incomplete metadata); route-specific ad-hoc serialization (reject: repetition and
contract drift).

## Decision 10: Translate results by semantic family, not one generic helper

**Decision**: Retain the authoring-specific and shared mediator error contracts separately. Preserve all seven
`201 Created` locations, the discard `204`, ordinary `200` responses, typed authoring diagnostics, 5xx sanitization,
legacy `EntityNotFoundException`/`ArgumentException` mappings, logging, and same-instance cancellation propagation.

**Rationale**: A single generic Minimal API execution helper would erase established differences that clients
consume and that existing semantic tests already pin.

**Alternatives considered**: normalize all errors to a new common format (reject: public contract redesign is out
of scope); approve broad response changes (reject: not an unavoidable framework delta).
