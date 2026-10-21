# Feature Specification: First Finite CSV-to-JSONL ETL Journey

**Feature Branch**: `agent/codex/3109-finite-etl`

**Created**: 2026-10-20

**Status**: Approved — implementation authorized under owner-delegated self-review; #3109 acceptance remains open

**Input**: User description: Deliver the first finite CSV-to-JSONL ETL journey in #3109: Studio authoring and publishing, native workflow invocation, bounded partitioned work, durable recovery, and run inspection.

## Context and Scope

This is the first implementation slice of the broader [#2908 distributed ETL feature](https://github.com/elsa-workflows/elsa-foundation/issues/2908), under [#3109](https://github.com/elsa-workflows/elsa-foundation/issues/3109). It delivers one finite journey: an author publishes a supported CSV pipeline with a pinned local CSV lookup, a native workflow invokes one run, and that run writes JSON Lines output and reports its result through normal workflow composition. Studio supports authoring and inspection.

The scope is finite file processing on the already-qualified built-in host. Runs process bounded batches in independently leased partitions; the qualification journey proves at least two nonempty partitions progressing independently, as specified by FR-007 and SC-002. Records and batches do not become workflow instances. The published runnable artifact is sufficient for runtime execution with its configured runtime features. Runtime execution does not read mutable drafts, authoring history, or Studio layout.

This slice does not complete #2908. Continuous processing, HTTP, PostgreSQL and SQL Server, RabbitMQ, BPMN invocation, general graph editing, and optional Orleans remain outside this work. No new actor provider or universal public ETL API is introduced. Correctness and recovery are the acceptance focus; throughput targets and benchmarks are not.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Author and publish a finite file pipeline (Priority: P1)

An ETL author selects approved file bindings and configures a finite CSV-to-JSONL pipeline in Studio. The author can validate and publish a version that describes the source schema, local lookup, supported transformations, and accepted/error outputs. Host paths are supplied through configured bindings rather than authored text.

**Why this priority**: Without a publishable, bounded definition, the end-to-end journey has no stable contract for invocation or recovery.

**Independent Test**: Configure a source, lookup, main-output, and error-output binding; author and publish the supported pipeline; inspect the published version and verify that arbitrary host paths cannot be entered through the authored definition.

**Acceptance Scenarios**:

1. **Given** the host exposes named bindings for an approved CSV source, local CSV lookup, and JSONL destinations, **When** an author configures and publishes a pipeline, **Then** the published version refers to those bindings and contains the schema and supported transformation choices.
2. **Given** an author tries to supply a raw host path or a binding outside the available set, **When** the pipeline is saved or published, **Then** Studio rejects it and does not produce a runnable version.
3. **Given** a published version is invoked and its draft is later edited, **When** the run executes or resumes, **Then** it uses the published version and its run-pinned file identities without loading the changed draft or layout.

### User Story 2 — Run the pipeline from a native workflow (Priority: P1)

A native workflow invokes one ETL run. The run partitions the finite input, applies the configured validation, exact-key lookup, mapping, filtering, and routing, and writes accepted records as JSONL. The enclosing workflow receives one explicit success, business-error, fault, or cancellation outcome.

**Why this priority**: This is the first user-visible production journey and proves that ETL is a composed workflow capability rather than a separate batch application.

**Independent Test**: Invoke a published pipeline from a native workflow over a fixture with valid, filtered, and rejected records; verify the main and error JSONL outputs, the returned outcome, and the enclosing Sequence or Flowchart behavior.

**Acceptance Scenarios**:

1. **Given** a valid published pipeline and a finite source with at least two partitions, **When** a native workflow invokes it once, **Then** one run owns the partitions, workers can lease and progress them independently, and no workflow instance is created per record or per batch.
2. **Given** valid records, filtered records, and row-level validation or lookup failures, **When** the run completes its input, **Then** accepted records appear in the main JSONL output, rejected records appear in the error JSONL output, filtered records are counted without being emitted, and the run returns success only when there are no rejected records; otherwise it returns a business error.
3. **Given** a run returns success, **When** it is invoked in a Sequence or Flowchart, **Then** the containing workflow follows its normal completion behavior exactly once.
4. **Given** a runtime/storage failure, an output conflict, or an explicit cancellation, **When** the invocation settles, **Then** the parent receives the corresponding fault or cancellation outcome distinctly from success and business error, and it does not continue twice.

### User Story 3 — Recover partition work safely (Priority: P1)

An operator can recover a run after a worker interruption without skipping accepted work, losing its cursor, accepting a stale result, or silently processing changed source data. A second worker can take over an abandoned partition and continue from durable state.

**Why this priority**: Partitioned execution is only useful if its durable effects and progress agree after failures and restarts.

**Independent Test**: With real durable persistence and two workers, interrupt a worker before an effect, after an effect but before cursor advancement, and around result/cancellation settlement; restart or let the other worker recover, then inspect outputs, cursors, and the single parent outcome.

**Acceptance Scenarios**:

1. **Given** a batch has been durably admitted, **When** its worker stops before or after writing the output but before advancing the cursor, **Then** the work remains owed and can be retried; a matching replay is recognized as the same effect and the cursor advances only after the destination accepts it.
2. **Given** an effect identity already has accepted output, **When** a replay presents different output content for that identity, **Then** the destination refuses the conflict and the run faults without advancing past that batch.
3. **Given** one worker's partition lease has expired or been replaced, **When** its late result arrives, **Then** it cannot make output authoritative, advance the cursor, or settle the run; the current owner can recover the partition. A physical file operation already in progress may finish; that does not make its result accepted.
4. **Given** a run is cancelled while work or completion is racing, **When** the durable terminal outcome is settled, **Then** only one terminal result wins, no new work is admitted after cancellation wins, previously admitted work remains durably accounted for, and the parent accepts at most one continuation. Transport retries may repeat delivery of the same result identity.
5. **Given** the retained source or lookup snapshot no longer matches the identity pinned to the run, **When** the run is resumed, **Then** it refuses to continue under the old cursor and leaves the existing progress visible for diagnosis. Editing the original configured file does not replace a retained immutable run snapshot.

### User Story 4 — Inspect definitions and runs in Studio (Priority: P2)

An author or operator can see which published version was used, whether a run succeeded or needs attention, and how its partitions progressed. The view provides enough durable state and diagnostics to distinguish a data rejection, a safe refusal, a fault, cancellation, and recoverable outstanding work.

**Why this priority**: Studio is the supported authoring and operational surface for this journey; recovery must be understandable without reading storage directly.

**Independent Test**: Publish and invoke a run, interrupt one worker, and inspect the run in Studio before and after recovery; verify the definition version, pinned source and lookup identities, partition progress, terminal outcome, and latest actionable diagnostic.

**Acceptance Scenarios**:

1. **Given** a published definition and a run, **When** an authorized user opens them in Studio, **Then** Studio shows the definition version, source/output binding names, configured finite topology, run outcome, partition states and cursors, and the latest recovery-relevant diagnostic.
2. **Given** a worker interruption or refusal, **When** the run is inspected before recovery, **Then** Studio distinguishes completed, failed, cancelled, and outstanding/recoverable work and does not present lease expiry as proof that a worker's memory has been reclaimed.
3. **Given** a new draft is published while an older version has active runs, **When** either version is inspected, **Then** each run remains associated with its own published version and pinned input/lookup identities.

### Edge Cases

- Missing, duplicate, or unexpected CSV headers, invalid quoting/encoding and malformed logical-record framing produce a visible business error during bounded preparation/indexing, before partition dispatch or JSONL effects. Well-framed row conversion/lookup failures become error records during processing. This first slice does not dispatch a valid prefix before framing validation completes.
- A duplicate key in the pinned lookup invalidates the lookup before processing. A missing lookup key rejects that input record to the error output.
- A record or batch that exceeds a configured byte ceiling is not dispatched. An individually oversized record is reported as a business error with its source ordinal and is not copied verbatim into diagnostics.
- If either record-count or byte admission is full, the coordinator does not dispatch additional work or advance past unadmitted input.
- A matching replay is idempotent by deterministic batch/effect identity and content. Different content for the same identity is refused.
- A changed published version, source identity, or lookup identity cannot resume an old run's cursor.
- A late worker result cannot make output authoritative or mutate cursor or terminal run state after its lease/cursor ownership has been replaced. Physical file I/O and a database lease cannot be atomically revoked together; unaccepted effects remain distinguishable from accepted output. Lease timeout is not treated as proof of process or memory reclamation.
- Cancellation and normal completion racing cannot cause two terminal outcomes or two parent continuations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Studio MUST let an author configure the supported finite file pipeline: one CSV source, one pinned local CSV lookup, a main JSONL destination, and an error JSONL destination.
- **FR-002**: Authored definitions MUST select host-configured named bindings. They MUST NOT accept arbitrary host paths, and save/publish MUST reject bindings outside the user's configured set.
- **FR-003**: A published version MUST contain the complete supported pipeline definition required for execution, including schema, operation order, routing, and binding identifiers; runtime execution MUST use that published runnable version and configured runtime features without loading mutable authoring data.
- **FR-004**: The supported CSV dialect, schema, type conversions, lookup-key rules, output format, and row error behavior MUST follow the defaults in **Assumptions**.
- **FR-005**: The supported deterministic transform chain MUST validate source fields, perform the pinned lookup by exact key, map/rename selected fields, apply the supported filters, and route accepted versus rejected records to the configured outcomes in the operation order defined in **Assumptions**. It MUST NOT execute arbitrary authored code or fetch mutable/network lookup data.
- **FR-006**: The runtime MUST retain the published version and immutable source and lookup snapshots identified by content for each run. Resume MUST refuse to substitute changed content under the prior cursor. Editing an original configured file MUST NOT change an existing run's retained snapshot.
- **FR-007**: One native workflow invocation MUST create or resume one ETL run. When its requested partition count and finite record count are both at least two, the run MUST own at least two independently leased, nonempty partitions. Empty, one-record and explicitly single-partition inputs MUST NOT acquire artificial empty ranges. The SC-002 qualification run MUST demonstrate at least two nonempty partitions and second-worker recovery. It MUST NOT create workflow instances per input record or per batch.
- **FR-008**: Distinct workers MUST be able to lease and progress different partitions independently. Lease and cursor ownership MUST prevent acceptance of a replaced or stale worker's output, cursor change, or terminal result. This does not promise revocation of physical file I/O already in progress.
- **FR-009**: Work MUST be bounded by finite per-batch record and encoded-byte limits and finite run-level outstanding record and byte budgets. A batch MUST pass both record and byte admission before dispatch; the durable source cursor MUST NOT advance past any unadmitted record.
- **FR-010**: Once admitted, work MUST remain durably owed until its destination effect is accepted and its cursor is advanced. The partition cursor MUST NOT advance before destination acceptance.
- **FR-011**: Each output effect MUST have a deterministic identity and content. Replaying the same identity and content MUST be treated as the same accepted effect; different content for the same identity MUST be refused.
- **FR-012**: Interruption at read, admission, effect, checkpoint, result, restart, or cancellation boundaries MUST NOT lose admitted owed work or allow a stale writer to advance it. Physical output effects are at least once unless the selected destination independently demonstrates a stronger guarantee.
- **FR-013**: The run MUST return distinct success, business error, fault, and cancellation outcomes through native workflow invocation. It MUST settle one durable terminal outcome and admit at most one parent continuation, including under retry and cancellation races. Delivery itself may be retried with a stable identity.
- **FR-014**: Successful completion MUST allow normal enclosing Sequence continuation and Flowchart completion. Business errors, faults, and cancellation MUST retain their distinct outcome for the containing workflow's normal semantics.
- **FR-015**: Studio MUST support creation, validation, publication, and inspection of the supported definition and MUST expose each run's published version, pinned file identities, partition state/progress, terminal outcome, and recovery-relevant diagnostics.

### Design Constraints

- **DC-001**: The ETL capability owns its definition, published-version, run, partition, and cursor model. Supporting types remain internal by default. Any cross-feature contract is limited to a demonstrated consumer and has an explicit compatibility/version policy reviewed before implementation.
- **DC-002**: Planning identifies the production persistence and worker mechanisms and the public compatibility boundary before implementation. It reuses existing production mechanisms whose actual contracts fit; it does not freeze a universal ETL API or add an actor-provider prerequisite.

### Key Entities *(include if feature involves data)*

- **ETL Definition**: The finite source, schema, lookup, transformation, routing, and named destination bindings authored as one supported pipeline.
- **Published ETL Version**: The immutable runnable version selected by a run; editing a draft does not alter it.
- **ETL Run**: One native invocation's pinned version and file identities, lifecycle outcome, counts, and recovery state.
- **Partition**: An independently leased portion of a finite source with durable progress and a cursor.
- **Batch Effect**: The deterministic identity and content of a bounded unit of output whose acceptance governs cursor advancement and replay.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a valid finite fixture, every logical input record is accounted for as emitted, filtered, or rejected, and the main and error JSONL contents match the published mapping and routing choices.
- **SC-002**: A run demonstrates at least two partitions progressing under independent leases, including recovery by a second worker; elapsed time alone is not evidence of this outcome.
- **SC-003**: At each tested effect-before-checkpoint interruption, the cursor never passes an unaccepted effect, accepted owed work is not lost, identical replay converges, and conflicting replay is refused.
- **SC-004**: Each run produces one distinct terminal outcome, and its enclosing native workflow receives at most one continuation or terminal signal across restart and cancellation races.
- **SC-005**: For any inspected run, Studio presents the published version, pinned source and lookup identities, each partition's durable state/progress, the final outcome, and an actionable latest diagnostic.

## Assumptions

- CSV uses UTF-8 with an optional byte-order mark, comma delimiters, a header in the first logical record, double-quote quoting with doubled quotes as escapes, and LF or CRLF line endings. Quoted fields may contain line breaks; record ordinals count logical records, not physical lines.
- Headers are case-sensitive, unique, and must contain exactly the declared schema fields; their order may vary. The first supported types are string, signed 64-bit integer, invariant decimal using a dot and no grouping separator, and boolean written as true or false without regard to case. String values are preserved; empty optional values map to null; empty required values and failed conversions reject the record.
- The pinned lookup is a local CSV using the same dialect. Its configured exact-match key is unique; duplicate keys invalidate the lookup before work is dispatched, and a missing key rejects that row.
- The operation order is CSV parsing and schema validation, exact-key lookup, field selection/renaming, filtering, then routing. Mapping selects or renames source and lookup fields without computed expressions. Filters are deterministic equality or inequality comparisons against configured literals, using exact string comparison and typed numeric/boolean comparison; all configured filters must match. A nonmatching record is counted as filtered.
- Each pinned finite input is divided into stable, disjoint, exhaustive ranges of logical-record ordinals. Every parsed record belongs to exactly one partition; a partition cursor identifies the next record ordinal in its range and retains that meaning on recovery. The acceptance fixture has at least two nonempty ranges.
- Row validation or lookup errors are written to the error JSONL as compact UTF-8 JSON objects containing the source logical-record ordinal and stable error code, with a field name when applicable. Raw source values are not copied into diagnostics by default. If any row is rejected, the finite run finishes processing classifiable records and returns business error.
- Main output is compact UTF-8 JSON Lines with LF separators and deterministic property order from the configured mapping. Values retain their validated JSON types. Cross-partition line order is unspecified; record values, routing, and batch identities are deterministic for the pinned run.
- Record and byte ceilings are finite positive configuration. Run-level budgets cover admitted but unsettled records and their deterministic serialized batch bytes; dispatch requires both batch ceilings and remaining run-level capacity. Byte admission is measured from the deterministic serialized batch handed to a worker; parsing, lookup, and output expansion also have finite bounds. These are logical payload limits, not a measurement of managed-heap size or process resident memory. An input that cannot fit its configured byte ceiling is not dispatched and is reported as a business error; no claim is made that lease expiration reclaims memory.
- A batch effect identity remains stable across worker attempts for the same published version, pinned source and lookup identities, partition, and cursor range. Lease or worker identity does not change the effect identity.
- A safe refusal to start or resume because a source or lookup identity changed is a business error. An infrastructure failure or conflicting effect is a fault. A terminal state is immutable once durably settled; if cancellation and completion race, the first durably accepted terminal transition wins.
- At-least-once physical effects are the baseline. Matching deterministic output replay is safe; exactly-once behavior is not claimed without separate evidence from the destination.

## Scope Boundaries

This slice uses the already-qualified built-in host. Optional Orleans is owner-deferred; Proto.Actor and other new actor providers are not prerequisites. Planning must identify the feature-owned model, partition/cursor semantics, persistence and worker integration, and any public compatibility boundary before implementation. It must reuse existing production mechanisms where their actual contracts fit; this specification does not freeze a general-purpose ETL API.

The parent feature remains open after this slice. Continuous ingestion, HTTP, PostgreSQL, SQL Server, RabbitMQ, BPMN invocation, full graph editing, shuffle, windows, stream joins, and general analytics remain future work. The slice does not claim throughput/SLA evidence, production actor-provider support, or fourth-model qualification.
