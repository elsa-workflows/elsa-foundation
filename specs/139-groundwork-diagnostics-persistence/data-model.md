# Data Model: Durable Diagnostics Persistence

All identities below are scoped by an explicit `StorageScope`. Provider table/collection names are resolved by Groundwork naming and routing policy; Elsa does not embed provider-specific identifiers in its public contracts.

## Storage binding

- `StorageScope`: opaque host-selected isolation boundary
- `SourceId`: diagnostics source binding
- `StreamKind`: structured logs, traces, spans, metric points, or telemetry logs
- `StreamId`: stable logical stream identity derived from scope, source, and kind
- `SchemaVersion`: declared adapter schema version

Validation rejects missing scope/source, unsupported schema, and a cursor or operation identity bound to a different tuple.

## Immutable record streams

### Common record envelope

- Provider-issued durable sequence/position
- Operation identity and item identity for idempotency
- Commit timestamp
- Occurrence timestamp
- Stable tie-break fields required by the domain query
- Filterable metadata declared by the adapter
- Canonical payload

### Structured Log record

Adds logical display sequence, level, category, event, source, and correlation filter fields. The opaque Elsa replay cursor binds version, scope, source, stream, provider position, and record anchor; it exposes none of those values as a supported parsing contract.

### Trace summary and span records

Trace summary records support bounded latest/history queries. Span records support exact trace-detail lookup by trace identity, with stable span ordering.

### Metric point records

Metric points bind an instrument identity and support inclusive time ranges plus stable latest-per-logical-key selection where required by the existing contract.

### Telemetry log records

Telemetry logs support inclusive time, severity, trace, resource, and text-related filters exactly as declared by `OpenTelemetryLogFilter`.

## Mutable catalogs

### Resource catalog entry

- Scoped resource identity
- Resource attributes/payload
- `LastSeenUtc`
- Stable last-seen tie-breaker
- Revision/concurrency token

### Instrument catalog entry

- Scoped instrument identity
- Resource identity
- Name, unit, description, and instrument metadata
- `LastSeenUtc`
- Stable last-seen tie-breaker
- Revision/concurrency token

Catalog upsert is idempotent. Capacity enforcement keeps exactly the newest configured entries by `(LastSeenUtc, tie-breaker)` within one scope. Capacity zero removes every current entry without changing immutable history high-water metadata.

## Capture batch and operation ledger

A capture batch contains one bounded normalized set for one storage binding. Its operation identity is stable across retries and restart. Reusing the identity for different canonical input fails. A committed operation returns the original authoritative outcome after acknowledgement loss.

Retention operations use the same identity/fingerprint rule and record exact affected counts. Lifetime high-water and cursor sequence never rewind when records are trimmed.

## Lifecycle state

The Elsa drain moves through `Created -> Running -> Closing -> Draining -> Stopped/TimedOut`. Producers are nonblocking while running. Every accepted acknowledgement completes exactly once as committed or failed. Loss counters distinguish queue overflow, retry exhaustion, shutdown timeout, writes after closure, durable retention deletion, and subscriber delivery loss.

## Relationships

- One storage scope owns many source/stream bindings.
- One resource catalog entry can be referenced by traces, spans, metric instruments, metric points, and telemetry logs.
- One instrument catalog entry owns many metric point records.
- One trace summary owns many span records and may relate to telemetry log records.
- One replay position belongs to exactly one structured-log binding.
