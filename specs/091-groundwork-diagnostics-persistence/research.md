# Research: Durable Diagnostics Persistence

## Decision 1: Keep diagnostics core provider-neutral

**Decision**: Preserve `IStructuredLogStore` and `IOpenTelemetryStore` as Elsa-owned contracts. Groundwork packages appear only in concrete persistence projects.

**Rationale**: The host must be able to replace infrastructure without changing the diagnostics domain. This also keeps a later third-party EF implementation possible without carrying EF in the foundation repository.

**Alternatives rejected**: Exposing Groundwork sessions or records from core contracts would make infrastructure vocabulary part of the domain API.

## Decision 2: Use record streams for immutable history and documents for catalogs

**Decision**: Store Structured Logs, trace summaries, spans, metric points, and telemetry log records in append-oriented diagnostic-record streams. Store resources and metric instruments as bounded keyed documents with explicit last-seen metadata.

**Rationale**: Immutable signal history requires stable append order, replay positions, exact trimming, and idempotent append. Catalogs require keyed replacement and deterministic capacity enforcement rather than append replay.

**Alternatives rejected**: Modeling every signal as an ordinary document loses the specialized replay/trim semantics. Modeling catalogs as streams makes current-state lookup and capacity enforcement unnecessarily indirect.

## Decision 3: Extract one Elsa-owned drain lifecycle component

**Decision**: Move the reusable bounded-channel, retry, acknowledgement, overload, and shutdown mechanism out of the EF namespace into `Elsa.Diagnostics.Persistence`. Adapters compose it rather than inherit provider-specific policy.

**Rationale**: Those semantics belong to diagnostics capture, not EF or Groundwork. One implementation prevents policy drift between Structured Logs and OpenTelemetry.

**Alternatives rejected**: Copying the existing EF base into each adapter repeats subtle lifecycle code. Moving the policy into Groundwork would invert domain ownership.

The shared lifecycle project therefore has no Groundwork package reference. Structured Logs and OpenTelemetry each own their Groundwork schema declaration inside their concrete adapter project and contribute it to host composition independently.

## Decision 4: Let host composition supply Groundwork access

**Decision**: Groundwork adapters consume a provider-neutral factory/lease abstraction selected by the host. They do not expose four provider-specific Elsa store implementations.

**Rationale**: Elsa owns the adapter behavior once; Groundwork owns relational/document-provider realization. This is the intended separation between store contracts and concrete implementation.

**Alternatives rejected**: Four Elsa adapters would recreate the provider matrix that this program is eliminating. A global static session would obscure scope and disposal.

## Decision 5: Separate cursor invalidity from operational failure

**Decision**: Translate only malformed, trimmed, or wrong-binding replay positions to `StructuredLogReplayCursorUnavailableException`. Provider, schema, serialization, and cancellation failures remain visible as their actual domain-facing failure.

**Rationale**: Returning cursor-unavailable for storage outages makes an investigation misleading and defeats readiness signals.

**Alternatives rejected**: Catch-all cursor translation and empty-result fallback both conceal operational faults.

## Decision 6: Bound filtered replay without client evaluation

**Decision**: Compile filters, ordering, page limits, and cursor advancement into declared Groundwork operations. A page may advance over scanned nonmatches, but application code may not fetch an unbounded history to filter it.

**Rationale**: Empty filtered pages can be legitimate while the durable cursor advances. Provider-side bounds protect memory and give identical semantics at scale.

**Alternatives rejected**: Repeated unrestricted reads followed by LINQ filtering are not scale-safe and yield provider-dependent work.

## Decision 7: Use deterministic least-recently-seen catalog retention

**Decision**: Resource and instrument entries carry last-seen time plus a stable tie-breaker. Capacity enforcement retains the newest configured entries and deletes the exact remainder within the caller's storage scope.

**Rationale**: This gives bounded catalogs, deterministic tests, and restart stability without generic reduce support.

**Alternatives rejected**: In-memory LRU state is lost on restart; time-only ordering is ambiguous when timestamps collide.

## Decision 8: Add the missing OpenTelemetry logs query endpoint

**Decision**: Expose the existing `QueryLogsAsync` product capability alongside resources, traces, trace detail, and metrics, using the same authorization and validation conventions.

**Rationale**: The core/provider contract already promises log queries, but the HTTP query surface is incomplete.

**Alternatives rejected**: Deferring the endpoint would leave conformance success invisible to users and retain an acknowledged product gap.

## Decision 9: Require provider execution evidence

**Decision**: The four-provider suite must capture the selected physical operation/index evidence for scale-bearing reads and retention, in addition to comparing results.

**Rationale**: Result equality does not prove the absence of broad scans or client-side evaluation.

**Alternatives rejected**: Unit-only query-translation tests do not prove real provider plans.

## Decision 10: Remove EF only after dual-oracle certification

**Decision**: Retain the EF implementations as temporary behavior/performance oracles during implementation. Remove them in the final phase after shared conformance, four-provider readiness, and ratified performance gates pass.

**Rationale**: The end state is zero EF in diagnostics, but deleting the oracle early would make subtle parity regressions harder to detect.

**Alternatives rejected**: Immediate deletion optimizes repository appearance before behavioral replacement is proven.
