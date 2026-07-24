# Feature Specification: Durable Diagnostics Persistence

**Feature Branch**: `138-groundwork-diagnostics-persistence`

**Created**: 2026-07-13

**Status**: Draft

**Input**: Replace the Structured Logs and OpenTelemetry EF Core persistence implementations with one Groundwork-backed implementation family while keeping diagnostics core contracts independent of any persistence technology.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resume durable diagnostics after interruption (Priority: P1)

An operator reconnecting to a running or restarted Elsa host needs Structured Logs and OpenTelemetry history to remain available in a stable committed order, so an investigation does not lose or duplicate evidence when writers, readers, or hosts restart.

**Why this priority**: Durable, trustworthy history is the reason to select a persistent diagnostics store. Incorrect replay or restart behavior makes the feature actively misleading.

**Independent Test**: Append diagnostic records from multiple writers, restart the persistence session, reconnect from an earlier cursor, and verify that every committed record is returned exactly once in stable order while trimmed or foreign cursors fail without leaking binding details.

**Acceptance Scenarios**:

1. **Given** multiple writers commit Structured Log entries with equal display sequences and timestamps, **When** a reader replays from a committed cursor, **Then** every later committed entry appears exactly once in durable commit order.
2. **Given** a host restarts after committing Structured Logs and OpenTelemetry records, **When** an operator queries history, **Then** committed records, lifetime high-water values, catalog state, and valid replay positions remain available.
3. **Given** a replay position is malformed, trimmed, or belongs to another source, scope, or stream, **When** it is used, **Then** replay is rejected with one non-disclosing unavailable outcome.
4. **Given** storage is unavailable or its schema is invalid, **When** a diagnostic read or write is attempted, **Then** the operational failure remains visible and is not reported as an empty store or unavailable cursor.

---

### User Story 2 - Query exact diagnostic history at scale (Priority: P1)

An operator investigating a workflow needs bounded filters, deterministic ordering, trace detail, metrics, logs, exact counts, and retention to behave identically regardless of the host's supported database choice.

**Why this priority**: A durable store is useful only when it can answer the diagnostics product's actual queries correctly without loading broad histories into application memory.

**Independent Test**: Load deterministic Structured Logs and OpenTelemetry datasets, run every supported filter and boundary case against each supported provider, and compare results, order, counts, retention, and execution bounds against the shared behavior contract.

**Acceptance Scenarios**:

1. **Given** Structured Logs across levels, categories, sources, and equal timestamps, **When** recent or replay history is requested, **Then** filters, limits, cursor advancement, and oldest-to-newest response order are exact.
2. **Given** resources, repeated trace summaries, spans, instruments, metric points, and log records, **When** resource, trace, trace-detail, metric, or log queries are executed, **Then** all declared filters, inclusive ranges, latest-per-key behavior, tie-breaks, and result limits are exact.
3. **Given** a retention limit including zero, **When** retention runs, **Then** exactly the newest allowed records remain within the caller's scope while lifetime cursor and logical high-water metadata do not rewind.
4. **Given** identical identifiers in two storage scopes, **When** either scope appends, queries, counts, or trims records, **Then** the other scope's existence and data cannot be observed or affected.
5. **Given** resource and instrument catalogs grow beyond their configured capacities, **When** catalog retention runs, **Then** growth remains bounded by deterministic least-recently-seen retention without requiring broad client-side scans.

---

### User Story 3 - Preserve capture under load and shutdown (Priority: P2)

An application owner needs diagnostics capture to remain nonblocking, bounded, observable, and drainable during bursts and shutdown, so persistence latency cannot destabilize workflow execution or silently discard evidence.

**Why this priority**: Provider correctness alone does not protect the host. The Elsa-owned capture and drain policy must continue to enforce explicit overload and shutdown behavior.

**Independent Test**: Saturate the capture queue, inject transient and persistent storage failures, cancel writes, and perform graceful and timed-out shutdowns while verifying bounded memory, retry behavior, committed acknowledgements, loss accounting, and host responsiveness.

**Acceptance Scenarios**:

1. **Given** producers outpace durable persistence, **When** the configured queue capacity is exceeded, **Then** capture remains nonblocking, the documented items are shed, and loss is counted by reason.
2. **Given** a transient provider failure, **When** a batch is retried, **Then** it commits at most once and the caller receives the original durable outcome after acknowledgement loss.
3. **Given** retry exhaustion, **When** a batch cannot be persisted, **Then** the batch is accounted as lost, the drain loop remains alive, and a later batch can still succeed.
4. **Given** graceful host termination, **When** shutdown begins, **Then** new writes stop, queued items drain within the configured window, final retention is attempted, and the outcome is observable before storage services are disposed.
5. **Given** the shutdown deadline expires, **When** provider work remains incomplete, **Then** shutdown stays bounded and every accepted caller acknowledgement is completed with either a committed result or an explicit failure.

---

### User Story 4 - Operate one provider model without EF migrations (Priority: P2)

A host developer or DevOps engineer needs diagnostics persistence to use the same provider-neutral deployment model as the rest of Groundwork, so selecting SQLite, SQL Server, PostgreSQL, or MongoDB does not require maintaining feature-specific EF migration sets.

**Why this priority**: Eliminating duplicated migration and provider implementations is the program-level maintenance outcome, but it follows behavioral parity and operational proof.

**Independent Test**: Configure each supported provider, validate and apply the declared schema before startup, run the same diagnostics behavior suite, and verify that the diagnostics composition contains no EF Core implementation, registration, package, or migration dependency.

**Acceptance Scenarios**:

1. **Given** any supported provider, **When** deployment validation runs before the host serves traffic, **Then** all required streams, catalogs, indexes, scopes, and capabilities are validated or applied deterministically.
2. **Given** validation detects missing or drifted storage, **When** the host starts, **Then** readiness fails with actionable non-payload diagnostics instead of falling back to an empty or in-memory durable store.
3. **Given** parity and performance gates pass, **When** the diagnostics persistence feature is composed, **Then** it registers only the Groundwork concrete adapters while diagnostics core contracts remain provider-neutral.
4. **Given** the completed repository, **When** diagnostics projects and dependency graphs are inspected, **Then** no diagnostics EF Core projects, registrations, packages, contexts, entities, configurations, or migrations remain.

### Edge Cases

- An append commits but its acknowledgement is lost before reaching the Elsa drain loop.
- A trim commits but its acknowledgement is lost, then the same operation is retried after restart.
- Records share occurrence timestamps, logical sequences, trace identifiers, or resource identifiers.
- A filtered replay page contains no matching entries even though its scanned cursor advances.
- Retention removes the replay anchor while lifetime high-water state remains valid.
- Cancellation occurs before provider work, during provider work, and after commit but before acknowledgement.
- One storage scope is noisy enough to reach retention limits while another remains nearly empty.
- Provider instrumentation emits logs or traces while persisting the same diagnostics signal.
- A malformed payload, oversized batch, invalid range, or unsupported query reaches the adapter boundary.
- Startup is concurrent in multiple host processes against an empty database.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST preserve the existing provider-neutral Structured Logs and OpenTelemetry store contracts; core diagnostics modules MUST NOT depend on a concrete persistence implementation.
- **FR-002**: Structured Log append MUST complete only after durable acknowledgement and MUST return an authoritative opaque replay cursor bound to source, storage scope, and stream.
- **FR-003**: Structured Log history MUST preserve lifetime logical high-water, bounded recent queries, durable tail capture, bounded read-after pages, filtered cursor advancement, and exact count-based retention across restart.
- **FR-004**: OpenTelemetry persistence MUST support normalized batch writes and exact resource, trace, trace-detail, metric, and log queries, including declared case policies, inclusive ranges, deterministic ordering, and latest-per-logical-key selection.
- **FR-005**: Mutable resource and instrument catalogs MUST use bounded keyed storage with deterministic capacity enforcement; high-volume immutable signal history MUST use append-oriented record streams.
- **FR-006**: Every append, query, inspection, catalog operation, and trim MUST bind an explicit storage scope at the persistence boundary; cross-scope access MUST require a distinct privileged path and MUST NOT be the default.
- **FR-007**: All scale-bearing predicates, ordering, latest-per-key selection, exact counts, and retention selection MUST execute within a declared bounded provider operation; broad client-side evaluation is prohibited.
- **FR-008**: Retried append and trim operations MUST be idempotent across acknowledgement loss and restart, and reuse of an operation identity for a different request MUST fail without mutation.
- **FR-009**: The Elsa-owned capture layer MUST provide bounded queues, nonblocking producers, documented batch limits, cancellation-aware retry with bounded backoff, overload shedding, graceful drain, and a bounded shutdown fallback.
- **FR-010**: Loss and lifecycle observability MUST distinguish queue overflow, retry exhaustion, shutdown timeout, writes after closure, durable retention deletion, and subscriber delivery loss.
- **FR-011**: Diagnostics persistence instrumentation MUST be non-recursive and MUST NOT include payloads, secrets, tenant identifiers, trace identifiers, or record identifiers in low-cardinality labels.
- **FR-012**: Missing schema, schema drift, capability mismatch, and operational storage failures MUST fail validation or readiness visibly; they MUST NOT be converted into empty results or an in-memory persistence fallback.
- **FR-013**: SQLite, SQL Server, PostgreSQL, and MongoDB MUST pass one shared highest-seam behavior suite covering correctness, scope isolation, restart, concurrency, failure, acknowledgement loss, retention, and bounded execution evidence.
- **FR-014**: The system MUST expose the documented OpenTelemetry logs query surface so every query promised by the diagnostics contract is reachable through the product surface.
- **FR-015**: Provider validation and schema application MUST be executable before application startup using the shared deployment tooling.
- **FR-016**: EF Core diagnostics implementations, provider registrations, contexts, entities, configurations, packages, and migrations MUST be removed only after shared parity and performance gates pass.
- **FR-017**: Diagnostics core projects and public contracts MUST remain free of Groundwork references after the concrete Groundwork implementation becomes the sole first-party durable provider.

### Key Entities

- **Diagnostic Storage Binding**: The explicit tenant, storage scope, stream, and source identity governing one adapter instance.
- **Structured Log Record**: An immutable committed log entry with display metadata, filter fields, durable provider cursor, and an opaque Elsa replay cursor.
- **Telemetry Record Stream**: One append-ordered stream for trace summaries, spans, metric points, or telemetry log records.
- **Resource Catalog Entry**: Mutable keyed metadata for a telemetry-producing resource, including last-seen state used for bounded retention.
- **Instrument Catalog Entry**: Mutable keyed metadata for a metric instrument, including last-seen state used for bounded retention.
- **Capture Batch**: The bounded unit accepted by Elsa's drain policy and committed idempotently to one durable stream.
- **Replay Position**: A versioned opaque value binding a reader to its source, scope, stream, provider position, and record anchor.
- **Retention Operation**: An idempotent request to keep exactly the newest configured records or catalog entries within one scope.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A multi-writer restart and replay suite returns 100% of committed records exactly once and in stable order, with zero cross-scope or cross-source disclosure.
- **SC-002**: All four supported providers pass the same Structured Logs and OpenTelemetry behavior suite with identical results, ordering, exact counts, and retention outcomes.
- **SC-003**: Every scale-bearing query and retention scenario has provider execution evidence showing a declared bounded plan and no broad application-memory evaluation.
- **SC-004**: Under sustained producer overload, capture memory remains within configured queue bounds, producer calls do not wait for database I/O, and every loss is attributed to one documented reason.
- **SC-005**: Graceful shutdown either commits every accepted item or reports its explicit loss within the configured shutdown window; no caller acknowledgement remains incomplete.
- **SC-006**: Issue #646 records a passing diagnostics performance verdict under the spec-094 handoff, or a program-owner-ratified replacement gate with retained evidence, before the EF oracle is removed.
- **SC-007**: Startup and deployment validation reject 100% of tested missing-schema, drift, wrong-scope, and capability-mismatch cases before the host reports ready.
- **SC-008**: The completed diagnostics persistence and composition surface contains zero EF Core projects, registrations, packages, contexts, entities, configurations, or migrations, while core diagnostics assemblies contain zero Groundwork references.
- **SC-009**: One persisted diagnostic operation produces no recursive diagnostic append in the non-recursion conformance test.

## Assumptions

- Existing Structured Logs and OpenTelemetry domain contracts are the behavioral authority and remain stable unless a contradiction is proven by their highest-seam tests.
- The committed Groundwork diagnostic-record and ordinary-document primitives provide the provider execution foundation; this work owns Elsa adapters and composition rather than a second storage abstraction.
- The current Groundwork Structured Logs adapter is a useful starting point but is not considered complete until it passes the full failure, scope, provider, lifecycle, and operational requirements in this specification.
- Structured Log live fan-out, OpenTelemetry live fan-out, ingestion parsing, redaction, and authorization remain diagnostics-domain responsibilities outside durable storage.
- Resource and instrument catalog retention is capacity-based and least-recently-seen; cascading record deletion and generic aggregation are out of scope.
- Production EF data migration is out of scope because the selected Groundwork and Elsa persistence software is greenfield and unreleased.
- A dedicated third-party EF implementation repository may be created later, but it is outside this work unit.
