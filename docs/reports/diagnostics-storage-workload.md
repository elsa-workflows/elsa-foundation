# Diagnostics Storage Workload

Status: decision input for [Zero-EF Persistence](../program-goals/zero-ef-persistence.md) and [Diagnostics Observability Readiness](../program-goals/diagnostics-observability-readiness.md).

Date: 2026-07-12.

Tracking: [Elsa issue #632](https://github.com/elsa-workflows/elsa-foundation/issues/632), [Groundwork diagnostic record-store #30](https://github.com/valence-works/Groundwork/issues/30), [Elsa PRD #629](https://github.com/elsa-workflows/elsa-foundation/issues/629), and [Groundwork PRD #25](https://github.com/valence-works/Groundwork/issues/25).

## Outcome

Elsa Structured Logs and OpenTelemetry do need a specialized Groundwork persistence primitive. Ordinary document CRUD is useful for the small mutable OpenTelemetry resource and instrument catalogs, but it is not the right abstraction for the high-volume immutable records. The missing primitive is an idempotent atomic single-stream batch append, bounded server-side query, exact inspection/count, and deterministic retention trim over tenant-scoped, time-ordered record streams.

The provisional name in this report is `IDiagnosticRecordStore`. The name is not ratified public Groundwork vocabulary; the vocabulary/API review tracked by [Groundwork #28](https://github.com/valence-works/Groundwork/issues/28) owns that decision.

Groundwork should own durable record semantics and provider execution. Elsa should continue to own capture policy: bounded channels, batch sizing, retry/backoff, overload shedding, drop accounting, graceful drain, shutdown timeouts, and non-recursive failure reporting. Moving those policies into the provider contract would make a general persistence primitive responsible for application-specific availability choices.

The existing Elsa core contracts remain Groundwork-free. `IStructuredLogStore` and `IOpenTelemetryStore` stay in their domain core modules; only the concrete Elsa persistence projects adapt those contracts to Groundwork.

No current Elsa caller requires numeric rollups, grouping, generic reduce, or map/reduce. Exact counts are required. Metric endpoints return raw points and instruments. This workload therefore does not justify adding reduce to Groundwork.

## Evidence Scope

The inventory covers:

- the public `IStructuredLogStore` and `IOpenTelemetryStore` contracts;
- their in-memory and EF Core implementations;
- the Structured Logs recent and resume endpoints;
- the OpenTelemetry provider facade and query endpoints;
- ingestion, source registry, startup/drain/termination hooks, retention, resilience, and provider-backed tests;
- specs 073 and 074; and
- the residual diagnostics debt in [issue #420](https://github.com/elsa-workflows/elsa-foundation/issues/420).

Live SSE fan-out is not a persistence concern. Its per-subscriber queues and in-band drop signals stay in the diagnostics domain.

## Current Contract Inventory

### Structured Logs

| Operation | Caller-visible semantics |
|---|---|
| `Append(entry)` | Synchronous, non-blocking enqueue of an already-sequenced entry. Durable visibility is eventual. The durable EF queue drops its oldest queued entry on overload. |
| `GetHighWaterMarkAsync()` | Highest retained logical `Sequence`, or zero when empty. `StructuredLogSink` blocks the first synchronous log emission on this async read and uses it to seed process sequencing after restart. |
| `GetRecentAsync(filter)` | Filter by minimum level, category, and source; clamp `MaxCount` to `MaxRecentQuerySize`; select the newest window; return it oldest-to-newest. Category and source equality are ordinal and case-sensitive. |
| `GetAfterAsync(sequence, filter)` | Return records whose logical sequence is greater than the supplied value, oldest-first. The EF implementation caps this at `MaxRecentQuerySize`; the in-memory implementation currently returns every buffered match. This is the durable replay behind SSE `Last-Event-ID`. |

The sink assigns one monotonically increasing process sequence and sends the same stamped entry to durable history and the live feed. The stream endpoint subscribes before it reads durable replay, then suppresses live entries at or below the last replayed sequence. That closes the query/subscribe gap without making the persistent store responsible for live delivery.

The EF implementation currently orders durable history by generated row `Id`, not timestamp, and uses logical `Sequence` only as a replay predicate and high-water mark. The generated row identity is therefore the actual durable tie-breaker even though it is absent from the public model.

The replay-size divergence is not portable behavior. The Groundwork-backed contract adopts a bounded first page capped by `MaxRecentQuerySize`, with continuation supplied by the durable cursor work in [#635](https://github.com/elsa-workflows/elsa-foundation/issues/635). The in-memory oracle must be aligned before it becomes a shared conformance fixture.

The current `GetHighWaterMarkAsync` contract reports the highest retained sequence and returns zero after all records are trimmed. The Groundwork target changes this through #635 to the lifetime maximum committed logical sequence, returning zero only when the stream has never committed a value. EF/in-memory oracle fixtures must adopt that meaning before parity comparison; otherwise `KeepNewest = 0` permits sequence reuse after restart.

`Sequence` is assigned in process and is not safe as a global multi-writer cursor. Two host processes sharing one storage scope can read the same high-water mark and emit overlapping values; the database index is intentionally non-unique. Groundwork's durable cursor fixes physical order, but Elsa cannot expose it through the current numeric `Last-Event-ID` contract without an adapter contract change. Until that change, a Structured Logs storage scope/source must have one active sequence writer.

### OpenTelemetry

| Operation | Caller-visible semantics |
|---|---|
| `WriteAsync(batch)` | Redacted resources, trace summaries, spans, instruments, metric points, and log records are accepted as one normalized batch. The EF implementation non-blockingly enqueues the batch, marks resources seen immediately, and persists eventually. |
| `QueryResourcesAsync(filter)` | Filter resources by case-insensitive service equality, status, and case-insensitive substring search over service name or id. Return newest `LastSeen` first and clamp `Take`. |
| `QueryTracesAsync(filter)` | Filter by resource/service, case-insensitive trace-id substring, workflow-id substring membership, status, inclusive start-time range, and case-insensitive trace-id/name search. Collapse repeated trace ids case-insensitively to the latest summary. Return the newest bounded window in oldest-to-newest order. |
| `GetTraceAsync(traceId)` | Match trace id case-insensitively and select the latest summary. Return spans ordered by start time and span id, resources ordered by service and id, and trace logs ordered by timestamp and record id. |
| `QueryMetricsAsync(filter)` | Resolve service to resource ids, filter by resource, inclusive time range, and case-insensitive instrument-name substring. Apply the instrument predicate before `Take`. Return the newest bounded point window oldest-to-newest plus the referenced instruments. Instrument identity is case-insensitive. |
| `QueryLogsAsync(filter)` | Resolve service to resource ids, then filter by resource, case-insensitive trace/span/severity substring, inclusive time range, and case-insensitive body substring. Return the newest bounded window oldest-to-newest. |
| `GetDiagnosticsAsync()` | Return configured capacities, exact current row counts, and process-local drop counters. |

The current HTTP surface calls the store through `IOpenTelemetryProvider`. Resources, traces, trace detail, metrics, and storage diagnostics have endpoints. `QueryLogsAsync` is implemented and tested but there is no logs query endpoint in the current source tree even though spec 074 describes one. That is diagnostics API drift, not an upstream Groundwork capability.

### Query Shape Summary

The combined scale-bearing predicate set is deliberately smaller than LINQ:

- equality and `IN` over scalar fields;
- lower/upper inclusive range over integers and timestamps;
- case-sensitive and case-insensitive equality as declared by the field;
- case-insensitive substring matching;
- membership and substring matching over declared multi-value fields;
- conjunctions with the few explicit disjunctions needed for text search;
- deterministic ordering by durable append cursor alone, or by one declared logical field plus the durable cursor tie-breaker;
- latest-per-logical-key selection for repeated trace summaries;
- a bounded newest window, with future keyset continuation; and
- exact count and a declared logical high-water mark.

Current Elsa list contracts offer only a bounded newest-window `Take`; they have no offset, cursor, or continuation response. Keyset continuation is the minimal paging extension for a durable time-ordered store. Traversal has snapshot semantics: the first page captures the stream's committed high-water cursor, and every continuation carries that snapshot boundary plus the last order value/cursor. Later pages include only records at or below that high-water. Concurrent appends—including backdated occurrence timestamps—appear only in a new traversal, so they cannot create gaps or duplicates inside the existing snapshot. Elsa adapters may initially request only the first window, while the Groundwork primitive preserves this stable continuation contract for the eventual durable-history UI.

The EF implementation performs several of these filters, de-duplication steps, and joins after loading broad table sets. Those are implementation shortcomings, not portable semantics. A Groundwork implementation must execute every declared scale-bearing predicate, ordering, latest-per-key operation, and count server-side or fail capability validation.

## Buffer, Drain, Retry, Drop, and Shutdown Inventory

Both durable EF stores inherit `ChannelDrainingStoreBase<T>`. Its behavior is application policy that the Groundwork adapter should preserve outside the provider primitive.

| Concern | Current behavior |
|---|---|
| Queue | Bounded multi-writer/single-reader channel with `DropOldest`; enqueue never waits for database I/O. |
| Structured Logs batch | Up to 200 entries. Queue capacity is `max(BufferCapacity, 200) * 4` (8,000 by default). |
| OpenTelemetry batch | Up to 64 normalized OTLP batches. Queue capacity is `max(SubscriberChannelCapacity, 64) * 4` (4,000 batches by default). |
| Retry | Initial attempt plus eight retries. Exponential delay begins at 50 ms and caps at 5 seconds. Cancellation is never converted into a retry. |
| Retry exhaustion | Drop the failed batch, emit an error, keep the drain loop alive. OpenTelemetry increments per-signal process counters; Structured Logs only logs the loss. |
| Queue overflow | Drop the oldest queued item. A warning is rate-limited to one per 30 seconds. OpenTelemetry counts every shed batch's signals; Structured Logs has no queryable shed count. |
| Prune cadence | Structured Logs every 5,000 accounted entries; OpenTelemetry every 500 inserted records. Failed prune retries with the same schedule; exhaustion leaves the counter armed for a later retry. |
| Graceful shutdown | Stop accepting writes, drain every queued item through bounded retries, then perform a final best-effort trim. Shell termination runs while storage services are still available. |
| Fallback disposal | Async disposal waits up to `ShutdownDrainTimeout` (10 seconds by default), then cancels and accepts loss. Synchronous disposal cancels immediately. |
| Crash/restart | Only committed batches survive. In-memory queued records and process-local drop counters do not. Structured Logs seeds its next logical sequence from durable history. OpenTelemetry reconstructs durable query state, while its in-memory source registry and drop counters restart from zero and repopulate on new writes. |

The Groundwork append operation should be idempotent by a versioned append-operation id. Without idempotency, a timeout or connection break after commit but before acknowledgement can cause Elsa's retry loop to append a duplicate batch. Each stream definition declares an idempotency retention window longer than the consumer's maximum retry window. The provider durably retains the canonical request fingerprint and outcome within that window and returns the original outcome only when both operation id and fingerprint match. Reusing an id for a different scope, stream, record order, ids, payloads, or declared field values is a conflict and writes nothing. An operation id carries its issuance time plus unique nonce; once it falls outside the durable window, a replay is rejected as expired rather than treated as a new append.

The durable append of one request to one stream should be atomic. The current EF implementation happens to commit all signal types in one transaction, but neither `IOpenTelemetryStore` nor the in-memory implementation promises cross-stream atomicity. A Groundwork adapter may therefore fan one normalized OTLP batch into atomic trace, span, metric-point, and log-record appends. Cross-stream or catalog-plus-record atomicity belongs to an explicit Groundwork unit of work if a future Elsa invariant proves it necessary; it is not part of this smallest primitive.

## Retention and Restart Semantics

Current durable retention is count-based, not time-based:

- Structured Logs keeps the newest 100,000 rows by durable generated id.
- OpenTelemetry independently keeps the newest configured number of traces, spans, metric points, and log records by generated id.
- OpenTelemetry resources keep the newest configured count by `LastSeen`.
- Metric instruments are upserted but never pruned, so the current instrument catalog is unbounded.
- Signal caps are independent. A span or log may outlive its trace summary, and a retained trace may have had older detail trimmed. No current caller promises cascade retention.

The portable v1 Groundwork primitive should preserve exact count-based trim per storage scope and record stream. `KeepNewest = N` means that after a successful trim, the scope/stream contains at most `N` records selected by durable append order. Trim must be isolated to the caller's storage scope and idempotent. Time-to-live, size-budget retention, cascading correlation retention, and instrument-orphan collection are useful later policies but are not required by current Elsa contracts.

Each scope/stream also has durable metadata independent of retained records: the next/committed cursor high-water, the maximum committed logical high-water value, and append/trim idempotency-window cutoffs, request fingerprints, and outcomes. Record trim never rewinds or deletes these lifetime high-water values. `KeepNewest = 0` removes records but preserves stream metadata, so restart cannot reuse a cursor or Structured Logs logical sequence. Idempotency outcomes expire only under their declared window rules; durable cutoffs remain so expired operation ids are rejected instead of replayed as new.

Restart conformance must prove:

1. committed records, stream cursors, logical high-water values, and catalog updates survive a new store/session instance;
2. an incomplete single-stream atomic batch is wholly present or wholly absent;
3. under the declared single-writer scope, replay after a structured-log high-water seed never reuses a committed sequence;
4. trim remains correct after restart and does not depend on process counters; and
5. process-local queue loss is surfaced by Elsa before shutdown completes or through a shutdown-timeout/drop signal when it cannot be drained.

## Tenant and Storage-Scope Behavior

Neither public diagnostics model contains a tenant id. The current services are singletons within a shell service provider, so tenant/shell selection is an execution concern rather than a field extracted from diagnostic payloads.

The Groundwork adapter must bind every append, query, inspect, and trim operation to an explicit storage scope supplied by the host/session. Providers must include that scope in physical keys and indexes. Two scopes may store identical record ids, trace ids, sequences, timestamps, and resource ids without collision or leakage. A privileged cross-scope diagnostics view, if ever required, needs an explicit tenant-agnostic session and a separate Elsa authorization decision; it is not the default query mode.

Retention is scope-local. A noisy tenant must not evict another tenant's records, and exact counts/high-water marks are computed within scope. Tenant/scope identifiers should not be emitted as high-cardinality metric labels or exposed in generic error text.

Storage-scope design must also decide whether a Structured Logs source instance is part of the scope. If multiple source instances share one tenant store, Elsa must expose Groundwork's durable cursor (or a source-plus-sequence cursor) before claiming lossless cross-process SSE resume.

## Smallest Specialized Groundwork Contract

The following is a semantic shape, not a ratified API signature.

```csharp
public interface IDiagnosticRecordStore
{
    ValueTask<AppendResult> AppendAsync(
        DiagnosticRecordBatch batch,
        CancellationToken cancellationToken = default);

    ValueTask<DiagnosticRecordPage> QueryAsync(
        DiagnosticRecordQuery query,
        CancellationToken cancellationToken = default);

    ValueTask<DiagnosticStreamInspection> InspectAsync(
        DiagnosticStreamInspectionRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<DiagnosticTrimResult> TrimAsync(
        DiagnosticTrimRequest request,
        CancellationToken cancellationToken = default);
}
```

The minimum semantics are:

- `DiagnosticRecordBatch` names one declared stream and has an idempotency/operation id plus its records. The acquired store/session context supplies the storage scope; scope is not trusted from record payload. One call is atomic within that scope and stream. A canonical fingerprint over scope, stream, ordered record identities, canonical payloads, and declared field values is bound to the durable operation outcome; same-id/different-fingerprint replay fails.
- A record is immutable and contains a logical record id, occurrence timestamp, canonical JSON payload, optional logical sequence/key, and values for fields declared by its stream definition.
- Groundwork assigns a monotonically ordered durable cursor within storage scope and stream. It is the stable total-order tie-breaker and keyset cursor. Its serialized value is stable but opaque to consumers; a provider may implement it with a 64-bit ordinal, a provider counter, or another conformance-equivalent representation. Cursor allocation state survives record trim.
- `DiagnosticRecordQuery` names one declared stream, carries the bounded predicate tree above, orders either by durable cursor alone or by a declared field plus durable cursor, has a validated positive limit, and may carry an exclusive keyset continuation. It never exposes `IQueryable` or provider expressions.
- `DiagnosticRecordPage` contains records in contract order, an optional next continuation, and an exact predicate count only when requested. Its continuation binds the traversal's committed high-water snapshot and last order value/cursor. Providers must not substitute an approximate count where exact was requested.
- `InspectAsync` returns exact retained counts, the maximum retained cursor when any record remains, the lifetime committed cursor high-water, and an optional lifetime maximum value for the stream's one declared logical high-water field. Lifetime values come from trim-independent stream metadata. This is sufficient for Structured Logs sequencing and OpenTelemetry storage diagnostics without introducing arbitrary aggregates.
- `DiagnosticTrimRequest` carries a versioned operation id and canonical fingerprint over scope, stream, and trim boundary. `TrimAsync` implements exact `KeepNewest` retention in v1 and reports examined/deleted counts and completion. Its outcome ledger is trim-independent: a retry after commit/acknowledgement loss returns the original counts when the fingerprint matches, while same-id/different-request reuse fails. Expiration follows a declared trim-operation idempotency window long enough for the operator retry policy.
- Every operation is storage-scope aware, cancellation-aware, and observable, and propagates storage/schema failure. A missing or invalid schema is not an empty result.

The stream manifest must declare:

- logical stream identity and physical table/collection policy;
- canonical payload shape/version;
- scalar or multi-value indexed fields, portable types, null behavior, and comparison/case policy;
- occurrence-time and optional logical-high-water fields;
- supported predicates, latest-per-key behavior, and deterministic orderings;
- count and retention capabilities; and
- append/trim idempotency windows and request fingerprints, atomic-batch, tenant-scope, trim-surviving stream metadata, and restart requirements.

Groundwork's `PhysicalTableDefinition` and query planner should materialize these declarations. The high-volume Structured Logs, trace-summary, span, metric-point, and OTLP-log streams are strong physical-entity-table candidates. Multi-value trace resource/workflow indexes may require linked index structures even when the main record uses an entity table. Resources and instruments are mutable keyed catalogs and should use ordinary dedicated or physicalized documents rather than forcing update semantics into the record store.

The specialized primitive deliberately does not include:

- live subscriptions or SSE;
- bounded in-process queues;
- retry schedules or drop policy;
- redaction and parsing;
- arbitrary updates/deletes;
- general joins or `IQueryable`;
- numeric/grouped aggregates; or
- map/reduce.

## Portable Semantics Versus Provider Optimizations

| Portable, conformance-gated behavior | Provider-native optimization allowed behind it |
|---|---|
| Atomic idempotent append batch | SQL multi-row insert, PostgreSQL `COPY`, SQL Server bulk copy, MongoDB bulk write/transaction where capability-compatible |
| Scope/stream monotonic durable cursor | Identity/sequence table, provider counter, partition-local allocation |
| Declared scalar and multi-value predicates | Physical columns, linked index tables, MongoDB indexes/arrays |
| Case policy fixed by field definition | Normalized shadow value, collation, expression index |
| Inclusive time/integer ranges | B-tree/compound index, partition pruning |
| Case-insensitive substring semantics | Provider full-text/trigram index when equivalent; otherwise server-side bounded scan |
| Latest-per-key and deterministic tie-break | Window function, grouped top-one plan, aggregation pipeline |
| Keyset continuation by logical order plus durable cursor | Tuple comparison, provider-specific continuation encoding |
| Exact count when requested | Provider exact count/query plan; approximate metadata counts are not equivalent |
| Exact scope-local `KeepNewest` trim | Set-based delete, partition rotation when exact semantics remain true, Mongo bulk delete |

Provider optimizations may change physical plans, not results, order, isolation, exactness, or failure behavior. Native TTL indexes, capped collections, approximate counts, and text search are optional only when they preserve the declared portable contract; otherwise they are additional provider-specific capabilities and cannot silently replace it.

## Highest-Seam Conformance Plan

Testing should be layered but acceptance belongs at the highest seam that proves application behavior.

### 1. Groundwork primitive contract suite

Run the same `IDiagnosticRecordStore` suite against real SQLite, SQL Server, PostgreSQL, and MongoDB providers:

- empty, single-record, batch-boundary, concurrent-stream isolation, and single-stream atomic append;
- append-operation replay proving stable outcomes, plus same-id/different-batch rejection;
- cancellation before execution and during provider I/O;
- scalar/multi-value equality, membership, ranges, substring, conjunction/disjunction;
- case-sensitive and case-insensitive declared fields;
- occurrence-time ties and deterministic durable-cursor order;
- cursor-only append-order traversal and field-plus-cursor ordering;
- forward/backward newest-window snapshot continuation with no gaps/duplicates under concurrent or backdated appends;
- latest-per-key selection;
- exact filtered/unfiltered counts and logical high-water;
- `KeepNewest` at `0`, `1`, `N-1`, `N`, and `N+1` records while lifetime cursor/logical high-water metadata survives;
- append/trim idempotency replay inside declared windows, request-fingerprint mismatch rejection, and expired-operation rejection outside the windows, including after record trim;
- two scopes with identical keys proving load/query/count/trim isolation;
- new store process/session proving restart durability;
- injected failure proving whole-batch commit/rollback and retry-safe acknowledgement; and
- executable-plan evidence proving no unbounded client evaluation.

### 2. Elsa adapter contract suite

Run the same existing `IStructuredLogStore` and `IOpenTelemetryStore` behavior fixtures against each real Groundwork provider through Elsa's adapter, not against a mocked Groundwork interface. This suite proves:

- exact filter/case/order/clamp semantics;
- Structured Logs lifetime committed high-water and bounded `GetAfterAsync` replay;
- latest trace summary and ordered trace detail;
- resource/instrument catalog correlation;
- exact storage diagnostics;
- independent signal retention;
- queue overflow, retry exhaustion, drain survival, final trim, and graceful shutdown; and
- correct mapping between shell/tenant execution scope and Groundwork scope.

The adapter suite should add tests missing from the current EF oracle: category/source case policy, all OpenTelemetry filter combinations, inclusive range boundaries, equal-timestamp tie-breaks, error propagation, storage-scope isolation, crash/restart, uncertain-commit idempotency, and bounded provider execution.

### 3. Physical/provider assertions

Each provider test run should inspect the declared query/materialization plan. Result equality alone is insufficient. Tests must prove that required fields and compound orderings are materialized, queries are server-side, trim is set-based or otherwise bounded, and tenant/scope appears in keys and scale-bearing indexes.

## Performance Workload

Performance validation should compare the EF Core SQLite oracle with a Groundwork physical-entity implementation first, then run the Groundwork provider matrix. The capture queue and durable provider must be measured separately so a fast non-blocking enqueue cannot hide a drain that continuously sheds data.

### Dataset scales

| Dimension | Values |
|---|---|
| Retained records per stream | 10,000; 100,000; 1,000,000 |
| Payload size | 512 B; 4 KiB; 32 KiB canonical JSON |
| Append batch size | 1; 32; 64; 200; 1,000 records |
| Normalized OTLP fan-out | 1; 10; 1,000 records per accepted OTLP batch, with 1 and 64 OTLP batches per drain append |
| Concurrent writers | 1; 4; 16; 64 |
| Query result limit | 50; 200; 1,000 |
| Predicate selectivity | approximately 0.01%; 1%; 10%; match-all |
| Time range | latest minute; latest hour; full retained window |
| Retention crossing | cap + 1; cap + 1%; cap + 10% |

Use deterministic Structured Log and OpenTelemetry generators with fixed seeds. Include realistic structured properties, span events/links, multi-resource/workflow trace indexes, metric attributes, and log bodies. Publish payload bytes, index bytes, write amplification, allocations, connection/pool wait, provider CPU, and query plans alongside latency and throughput.

### Measured operations

1. Hot-path enqueue latency and shed rate at increasing producer rates.
2. Durable single and batch append throughput, p50/p95/p99 acknowledgement latency, and allocation rate.
3. Structured Logs recent and resume queries, including cold restart high-water lookup.
4. Trace list, latest trace detail, metric, log, resource-catalog, exact-count, and inspection queries.
5. Keyset page traversal under concurrent append.
6. Retention trim latency, lock impact, and ingest/query interference.
7. Graceful drain time and retry-exhaustion behavior.
8. Restart recovery and first-query latency.

Correctness, no unexplained loss/duplication, atomicity, isolation, and server-side execution are prerequisites. Diagnostics durable operations use the provisional ordinary-store gate from the Zero-EF decision: Groundwork p95 no worse than 1.25x EF Core and throughput at least 80%, with Groundwork p99 no worse than 2x EF Core. The non-blocking capture path additionally must not regress host throughput or block a producer on database I/O. A sustained run is unacceptable if its apparent throughput is achieved by a higher shed/drop rate.

## Failure Observability

Groundwork should emit operational evidence for append/query/inspect/trim duration, batch/row counts, idempotency replays, selected physical plan, pool/session wait, trim deletions, cancellations, and failures. Elsa should emit queue depth/high-water, batches drained, retry attempts, queue-overflow drops, retry-exhausted drops, writes-after-stop, shutdown timeout, and final drain outcome.

Durable-adapter loss counters must distinguish at least `queue_overflow`, `retry_exhausted`, `shutdown_timeout`, and `writer_closed`. OpenTelemetry's per-signal counts remain useful; Structured Logs needs equivalent process-local durable-queue counters rather than only a warning. Groundwork trim results/inspection must distinguish durable retained-record eviction from failed persistence and subscriber delivery loss. In-memory ring-buffer eviction accounting remains owned by #420.

Diagnostics persistence cannot rely solely on logs or telemetry that it captures itself. Instrumentation must have a non-recursive path, using `Meter`, health/readiness state, and explicitly suppressed `Activity`/logger capture around the persistence implementation. Groundwork provider logging that is fed back into Structured Logs, or Groundwork tracing exported into the OpenTelemetry store it describes, can create an amplification loop. Conformance should prove that one append does not recursively generate another diagnostic append.

Do not put raw payloads, secrets, tenant ids, trace ids, or record ids into low-cardinality metric labels. Error logs should include bounded operation/stream/provider context and exception details without diagnostic payload content.

## Current Gaps and Follow-up Slices

### Upstream Groundwork capability slices

Delivery is tracked by [Groundwork #30](https://github.com/valence-works/Groundwork/issues/30) under the Groundwork PRD.

1. Ratify the record-store name and its relationship to `PhysicalTableDefinition`, storage units, sessions, and operational primitives in Groundwork #28.
2. Add the record stream definition plus atomic idempotent batch append and per-stream monotonic cursor.
3. Add the bounded diagnostic query plan: ranges, multi-value membership, deterministic tie-break/keyset continuation, latest-per-key, exact count, and high-water inspection.
4. Add exact scope-local `KeepNewest` trim and restart/atomicity semantics.
5. Implement SQLite, SQL Server, PostgreSQL, and MongoDB providers with shared conformance and physical-plan assertions.
6. Add non-recursive Groundwork operational instrumentation.

These can be delivered incrementally, but the Elsa diagnostic adapters should not freeze until the decision PR and vocabulary/session prerequisites are accepted.

### Elsa implementation slices

1. Move/extract the channel-drain policy from the EF-only project into the Groundwork diagnostics implementation layer without coupling core contracts to Groundwork.
2. Implement Structured Logs and OpenTelemetry adapters over the record store plus ordinary Groundwork documents for catalogs.
3. Replace broad client-evaluated EF queries with declared Groundwork server-side plans.
4. Add storage-scope binding, conformance fixtures, restart/idempotency tests, and non-recursive failure instrumentation.
5. Decide the missing OpenTelemetry logs endpoint and the unbounded instrument-catalog policy in Diagnostics Observability Readiness.
6. Replace the first-log synchronous async high-water block with an explicit initialization/readiness path, or prove the bounded startup behavior.
7. Replace the process-local numeric Structured Logs replay cursor with a durable/source-qualified cursor through [Elsa #635](https://github.com/elsa-workflows/elsa-foundation/issues/635); a single-active-writer rule is only a temporary constraint, not the scalable target.
8. Stop treating arbitrary Structured Logs read failures as an empty store. Schema absence belongs to Groundwork plan/validation readiness; operational read failures should be visible.

### Boundary with issue #420

This report does not absorb issue #420's SSE-writer deduplication, in-memory ring-buffer reuse/drop counting, culture-invariant request parsing, serializer null policy, option clamping, or enum validation. The current EF resource/instrument full-table scans are relevant evidence, but the durable fix is keyed Groundwork catalog lookup rather than carrying the EF implementation forward. Issue #420's original claim that persistence failures were silent is stale in current source: the shared drain base now logs retries, exhausted batches, pruning failures, and overload shedding. This report owns durable queue/retry/shutdown loss categories, durable trim counts, and recursion-safe telemetry; #420 retains in-memory retention-eviction accounting.

## Decision

Resolve `diagnostic-storage` with the specialized record-store shape above. Counts are required; generic reduce is not. Capture buffering stays in Elsa. Mutable catalogs stay in ordinary Groundwork documents. Every durable operation is explicit-scope, idempotent where retried, bounded, server-side, restart-safe, and conformance-tested across all four mandatory providers.
