# Structured Logs Minimal API migration 2026-08

## Decision

**Proceed.** The complete Structured Logs HTTP surface can use explicit ASP.NET Core Minimal APIs without
changing its consumed REST, SSE, authorization, or OpenAPI contracts. This is the first program wave to
exercise a durable live stream as well as ordinary JSON operations, and it strengthens the recommendation
to keep migrating first-party REST APIs module by module.

The recommendation is scoped: keep explicit `RequestDelegate` mappings, Foundation permission policies,
module-local protocol helpers, and evidence-driven OpenAPI metadata. Do not introduce module-owned OpenAPI
operation-transformer delegates into dynamically unloadable endpoint assemblies without a separate
retention mitigation.

## Scope

Issue [#1349](https://github.com/elsa-workflows/elsa-foundation/issues/1349) migrated exactly these existing
operations:

| Operation | Runtime contract retained |
|---|---|
| `GET /_elsa/studio/diagnostics/structured-logs/recent` | Raw query binding, safe `400` text, explicit serializer, JSON status/content type, order and store clamp |
| `GET /_elsa/studio/diagnostics/structured-logs/sources` | Existing source collection, JSON shape and OpenAPI `LogSource[]` schema |
| `GET /_elsa/studio/diagnostics/structured-logs/stream` | SSE headers/framing, opaque replay, durable ordering, polling, 15-second heartbeat, cancellation and cleanup |

The three paths remain option-driven. OpenTelemetry, persistence design, UI work, route-collision policy,
and other endpoint modules remain outside this work unit.

## Compatibility evidence

The pre-migration FastEndpoints host produced an immutable evidence set before production registration was
changed:

- 17 HTTP/SSE cases covering default and custom paths, filtering, invalid and repeated values, `take = 0`,
  initial and resumed streams, malformed and unavailable cursors, heartbeat, cancellation, and exact SSE
  response headers;
- a real ASP.NET Core OpenAPI document projected to the three consumed operations;
- a three-route endpoint manifest with owner, authoring model, method, and permission-policy evidence;
- separate cursor evidence requiring each emitted identifier to be present, parseable, and bounded.

The replacement host reproduces the immutable HTTP/SSE and OpenAPI projections with zero approved
differences. The only intended manifest change is the authoring disposition from `FastEndpoints` to
`MinimalApi`; route owner, methods, configured paths, and the canonical any-permission policy are unchanged.

The bounded SSE reader was hardened during comparison: a single transport read can coalesce multiple
already-flushed frames, so evidence is now truncated at the requested complete-frame boundary rather than
accidentally observing extra frames from the same read.

## Streaming result

The endpoint retains the existing durable-tail algorithm:

1. bind and validate the filter;
2. parse `Last-Event-ID` or capture the durable tail boundary;
3. subscribe to the process-local feed as a wake hint only;
4. read and validate the first durable page before starting the response;
5. emit only durable-page entries in committed cursor order;
6. drain `HasMore` immediately and otherwise wait for a wake hint or the polling interval;
7. emit `: keep-alive\n\n` after 15 idle seconds;
8. cancel and dispose pending enumeration with a five-second writer bound and the existing bounded wake
   cleanup.

The migration review exposed and fixed a legacy cleanup gap: the in-memory wake feed registers eagerly,
so first-read and initial-flush failures now unconditionally cancel the subscription. Regression tests
prove both failure paths return the subscriber count to zero.

The formatter still supports a dropped-entry frame for its own unit-tested contract, but the public durable
tail does not forward process-local feed payloads or drop signals. This avoids an accidental protocol
expansion during the framework migration.

## Authorization and coexistence

All three routes use `RequireAnyPermission("*", "Diagnostics:StructuredLogs")` through Foundation Identity.
The module contributes only `Diagnostics:StructuredLogs`, owned by `Elsa.Diagnostics.StructuredLogs`, with
no permission implication. The administrative wildcard remains a grant rather than a catalog entry.

Real-host tests prove:

- anonymous callers and identities rejected by principal normalization receive `401`;
- authenticated missing, adjacent, ambiguous, and resource-denied principals receive `403`;
- exact permission and wildcard callers succeed;
- rejected stream calls do not start SSE or add a live-feed subscription;
- the three Minimal API routes and a secured test-only FastEndpoints route coexist and reach the same
  instrumented Foundation evaluator.

## OpenAPI and collectibility

The mapper uses explicit `RequestDelegate` handlers and stable `RequestDelegate.Invoke` description
metadata. It adds no module-owned operation transformer. Actual ASP.NET Core OpenAPI generation preserved
the legacy projection, and inspection of the keyed `OpenApiDocumentService` found zero entries in
`_operationTransformerContextCache` for these routes. That zero is expected and useful: the cache is
populated for endpoint operation transformers, which this mapper intentionally does not contribute.

Three repeated cycles materialized all routes, executed a recent query, started and cancelled SSE,
exercised the serializer, generated the real OpenAPI document, released owners, and collected the isolated
Structured Logs implementation `AssemblyLoadContext` using weak-reference-only evidence. A deliberate
combined lifecycle owner covering the exercised routes, service provider, completed SSE exchange, and
serializer remained alive while held and collected after release; this proves fixture sensitivity without
claiming that those components were isolated as four independent retaining roots.

This result differs from the Secrets wave, whose legacy OpenAPI compatibility required module-owned
operation transformers and exposed ASP.NET Core's operation-context cache as a retaining root. Structured
Logs demonstrates the preferred unload-safe shape: stable metadata and no collectible transformer delegate.
The fixture resolves shared Core contracts from the default context, matching the intended host-shared
contract boundary; it does not claim that arbitrary private dependency graphs will unload safely.

## Dependency retirement

The production package now depends on CShells ASP.NET Core abstractions, `Elsa.Api.AspNetCore`, and Foundation
Identity instead of CShells/FastEndpoints integration and `Elsa.Api.FastEndpoints`. The three endpoint
classes, legacy feature base, legacy permission holder, and shared FastEndpoints SSE helper usage were
removed. FastEndpoints remains test-only for coexistence evidence.

Exactly the three `#1349` transition-exception records were removed. Architecture and module tests guard
the route count, owner, Minimal API authoring disposition, security metadata, catalog ownership, production
dependency graph, and absence of legacy registrations.

## Evidence summary

| Area | Result |
|---|---|
| HTTP/SSE compatibility | Pass: immutable 17-case projection, zero differences |
| OpenAPI compatibility | Pass: three consumed operations, zero differences |
| Query/serialization | Pass: blank, mixed-case, repeated, zero, negative, culture-sensitive, clamp, empty and stable source cases |
| SSE lifecycle | Pass: initial/resume, durable order, remote polling, filter, heartbeat, cancellation, failure and bounded cleanup |
| Authorization/catalog | Pass: challenge/forbid/exact/wildcard/resource and unique owner evidence |
| FastEndpoints coexistence | Pass: both authoring models use one Foundation evaluator |
| Production dependency removal | Pass: no Structured Logs FastEndpoints registration, base, package/project reference, or shared SSE helper |
| Collectibility/OpenAPI | Pass: repeated exercised collection; zero operation-transformer cache contexts |

The final pull-request comment is the notification of record for exact build, focused, persistence,
compatibility, architecture, generated-map, and diff-review commands.

## Remaining risks and follow-up guidance

- Keep raw query and explicit serialization tests for each migrated module; framework defaults are not a
  substitute for a measured contract.
- Treat SSE as a lifecycle protocol. Future streaming migrations need real HTTP boundaries, remote commits,
  disconnects, pending reads, and cleanup evidence rather than handler-only tests.
- OpenAPI compatibility can reintroduce retention. Prefer stable metadata; if a module needs custom operation
  transformers, require cache inspection and a supported documentation lifetime before claiming unload safety.
- The current legacy OpenAPI shape for `recent` and `stream` advertises `204/401/403` rather than their richer
  runtime behavior. It is preserved here as a compatibility constraint, not endorsed as the ideal public
  description. Correcting it should be a separate reviewed contract change.
- Continue migration through bounded module waves. Do not convert all remaining APIs in one mechanical PR.
- Configured route collision adjudication remains a host-generation responsibility and is outside #1349.
  The module publishes its three option-driven templates; the central manifest-validation work must reject
  empty, duplicate, equivalent-template, and cross-owner collisions before a candidate generation activates.

## Recommendation for the program

Use this implementation as the streaming reference wave alongside Studio Preferences for simple operations
and Secrets for complex CRUD/security. Prioritize the next module whose API is self-contained and whose
OpenAPI contract can be expressed without collectible transformer delegates. Keep FastEndpoints only as a
temporary coexistence path until each module has equivalent evidence and its transition records can be
removed.
