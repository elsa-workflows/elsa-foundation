# Research: Structured Logs API Minimal API Migration

## Authoritative HTTP and SSE surface

**Decision**: Treat the three current option-driven FastEndpoints registrations and real-host observations as the migration baseline: recent JSON, source discovery, and resumable SSE.

**Rationale**: Older specs describe intent, but deployed clients observe configured routes, headers, framing, serialization, and errors from current code. The migration replaces the authoring framework, not the protocol.

**Alternatives rejected**: Hand-written expected behavior can miss inferred OpenAPI and global framework behavior. Adding fixed aliases for configured routes would expand the surface and create ownership ambiguity.

## CShells module mapping seam

**Decision**: Implement `IWebShellFeature` directly on `StructuredLogsFeature` and delegate `MapEndpoints` to `MapStructuredLogsApi(IEndpointRouteBuilder)`.

**Rationale**: CShells already supplies generation-scoped services, prefixes, ownership enrichment, publication, and removal. This is the explicit mapping seam accepted by ADR 0068 and used by the two completed migrations.

**Alternatives rejected**: Application-root mapping does not compose loaded features. A new mapper registry duplicates CShells. Keeping FastEndpoints discovery does not deliver the program outcome.

## Collectible-safe handler and metadata boundary

**Decision**: Use explicit `RequestDelegate` handlers and only framework/shared metadata in OpenAPI-visible endpoint metadata. Do not attach module-owned operation-transformer delegates or module-owned response types when stable Core contract types suffice.

**Rationale**: The Preferences canary proved typed handler signatures can populate process caches. Secrets proved actual OpenAPI generation retains its collectible API assembly. ASP.NET Core OpenAPI 10's keyed singleton document service maintains `_operationTransformerContextCache`; API Explorer copies endpoint metadata into the cached `ApiDescription`, making module `Type`, `MethodInfo`, and transformer delegates credible roots.

**Alternatives rejected**: Typed delegates are terser but violate proven lifecycle constraints. Merely disposing the host does not clear framework caches. Calling the retention framework-owned without inspecting a root would overstate the evidence.

## OpenAPI retention experiment

**Decision**: Generate the real document and inspect the document-service operation-context cache for module-owned references. If stable metadata still retains the context, capture a GC-root dump and keep dynamic documentation blocked or move documentation to a host-owned serialized-contract boundary.

**Rationale**: This isolates module metadata from framework lifetime. A collected Structured Logs context would validate the stable-metadata technique without pretending the Secrets transformer-heavy surface is fixed. A retained context needs root evidence before a durable shared mitigation is designed.

**Alternatives rejected**: Skipping OpenAPI makes the collectible test incomplete. Clearing a private framework field in production is unsupported. Moving a speculative adapter into shared code before multiple consumers prove it would violate the bounded-convention rule.

## Permission ownership

**Decision**: Contribute the existing stable `Diagnostics:StructuredLogs` permission from `Elsa.Diagnostics.StructuredLogs`, attach `Any(*, Diagnostics:StructuredLogs)` to all three routes, and declare no implication.

**Rationale**: This exactly preserves wildcard compatibility while moving policy execution to Foundation Identity's normalized-principal, catalog, implication, evaluator, and resource-handler path. The current module leaves the permission ownerless.

**Alternatives rejected**: Renaming the permission changes public grants. Direct claim checks preserve the split authorization implementation. A new broad diagnostics permission lattice expands access without a reviewed requirement.

## Query and serialization preservation

**Decision**: Reuse `StructuredLogFilterBinder` and `StructuredLogEntrySerializer`, passing raw query values and writing the serializer output explicitly.

**Rationale**: `minLevel` and `take` have deliberate validation, `take = 0` is valid, and current payloads serialize log levels as PascalCase enum names. Ambient Minimal API binding/serialization may differ for repeated values, culture, nulls, and enums.

**Alternatives rejected**: Framework binding without differential evidence can turn invalid input into an unfiltered query. Replacing the serializer during migration couples a wire-contract change to framework removal.

## SSE response and writer ownership

**Decision**: Keep a small module-local writer using ASP.NET Core `HttpResponse`, preserving the current response headers, per-frame flush, 15-second heartbeat, five-second pending-read cleanup bound, and cancellation behavior.

**Rationale**: The production helper lives in `Elsa.Api.FastEndpoints` and would keep the module dependent on the framework being retired. ADR 0068 permits a shared helper only after at least three consumers demonstrate the same gap; this is the first Minimal API streaming proof.

**Alternatives rejected**: Copying the generic FastEndpoints helper into a new shared package would pre-decide an abstraction. `Results.Stream` does not itself provide the existing heartbeat and pending-enumerator lifecycle contract.

## Durable tail and wake-hint semantics

**Decision**: Move the existing durable-tail logic without semantic redesign. Store pages remain the only payload source; the local feed only wakes polling. Validate the first page before starting SSE and preserve generic cursor rejection.

**Rationale**: This is the multi-writer-safe design established by the replay-cursor work. Forwarding live-feed items directly could reorder or duplicate committed entries and would expose drop frames not emitted today.

**Alternatives rejected**: Subscribe-and-forward is lower latency but not authoritative across processes. Exposing formatter-supported drop frames would be a new public event contract.

## Compatibility evidence for an infinite response

**Decision**: Capture bounded SSE observations consisting of response status/headers plus complete first frame or heartbeat and an explicit terminal state after test cancellation. Keep exact framing in the `Streaming` facet and use reviewed timing windows rather than wall-clock bytes.

**Rationale**: The shared compatibility library already models bounded streams and terminal state. Infinite responses need a deterministic observation boundary to remain byte-stable.

**Alternatives rejected**: Reading the response to EOF hangs. Direct formatter tests do not prove HTTP headers, flush/start order, cancellation, or middleware authorization.

## Coexistence and transition retirement

**Decision**: Keep one unrelated secured FastEndpoints route in the test host while production Structured Logs maps only Minimal APIs. Remove the module's three transition entries and all production FastEndpoints/SSE-helper dependencies after parity passes.

**Rationale**: The program remains deployable during staged migration, but the migrated module must have one explicit owner and authoring path.

**Alternatives rejected**: A Minimal-only test does not prove transition middleware/evaluator compatibility. Keeping unused endpoint classes preserves discovery and inventory debt.
