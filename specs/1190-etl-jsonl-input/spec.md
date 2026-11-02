# Feature Specification: Finite JSON Lines ETL input

**Feature Branch**: `agent/claude/2908-jsonl-input`

**Created**: 2026-11-02

**Status**: Implemented — [PR #3657](https://github.com/elsa-workflows/elsa-foundation/pull/3657); [evidence](evidence/README.md)

**Input**: The JSON Lines half of the file-connector criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908) ("CSV and JSON Lines file ingestion/delivery have explicit positions, replay, parsing/validation and destination-idempotency behavior"), in [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). Scope follows the [25 October research checkpoint](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6393884289) and the [claim](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6578657843).

## Context

The v2 connector pipeline ([spec 996](../996-http-etl/spec.md)) reads a CSV file or a paginated HTTP JSON source and writes JSON Lines files or HTTP JSON. File input is already partitioned, positioned and recovered through a format-neutral path: an immutable snapshot of the pinned file, a byte index of its records, file partitions over record ordinals, owed batches identified by their ordinal range and canonical content, and fenced acceptance. This slice adds JSON Lines as a second file input format on that same path. It does not add CSV output, which keeps the file criterion open.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Run a pipeline over a JSON Lines file (Priority: P1)

An integration author has a host-approved source file that holds one JSON object per line. In Studio they choose the JSON Lines source kind and the approved source file, declare each field with a JSON Pointer, type and required flag, map, filter and route the records exactly as for the other sources, and publish. A workflow runs the published version; the run splits the file into independently leased partitions and writes the mapped records to the chosen JSON Lines or HTTP destination.

**Why this priority**: it is the slice's purpose: the file criterion requires JSON Lines ingestion, and every later JSON Lines guarantee depends on it.

**Independent Test**: publish a JSON Lines → JSON Lines pipeline through the authoring API, run it over a file whose records span two partitions, and compare the accepted main and error outputs with the expected mapped records.

**Acceptance Scenarios**:

1. **Given** a source file of valid object records and a two-partition limit, **When** the run executes, **Then** both partitions progress independently over disjoint, contiguous record-ordinal ranges and every mapped record is accepted on the main route once per effect identity; physical publication remains at least once.
2. **Given** the same pipeline with an HTTP destination, **When** the run executes, **Then** delivery follows the existing HTTP destination contract unchanged.
3. **Given** a lookup declared on the pipeline, **When** a JSON Lines record carries the source key, **Then** the existing CSV lookup join applies to it as it does to CSV and HTTP records.
4. **Given** an existing CSV or HTTP v2 artifact, or a v1 file artifact, **When** it is opened, pinned or run after this change, **Then** its bytes, content hash and behaviour are unchanged.

---

### User Story 2 — See bad lines as routed errors, not a failed run (Priority: P1)

A data owner's file contains some bad lines: broken JSON, an array instead of an object, a missing required field, a value of the wrong type, an over-long line. Each bad line is reported on the error route with a stable code and its record ordinal, while every good line still reaches the main route. Only a problem with the file as a whole (unavailable, changed since admission, too large) stops the run.

**Why this priority**: per-line failures are the normal case for line-oriented data; failing a whole run on one bad line would make the connector unusable.

**Independent Test**: run a file that mixes valid lines with each class of bad line, and check that the error output names each bad line's file ordinal and code, the main output holds the valid ones, and the run settles as a business error with diagnostic `etl.rows-rejected` and matching accepted and rejected counts. A file with no bad lines settles as success.

**Acceptance Scenarios**:

1. **Given** a line that is not valid JSON, has a duplicate property name, nests deeper than the pipeline's JSON depth limit, or contains invalid UTF-8, **When** its batch is built, **Then** the record is routed to errors as `etl.jsonl.record-invalid`.
2. **Given** a line that is valid JSON but not an object, **When** its batch is built, **Then** the record is routed to errors as `etl.jsonl.record-not-object`.
3. **Given** a line whose content (its bytes without the LF or CRLF terminator) is longer than the pipeline's maximum record size, **When** its batch is built, **Then** the record is routed to errors as `etl.record-too-large` without its content being read into memory; a line of exactly the maximum size is parsed.
4. **Given** a missing or null required field, or a value of the wrong JSON type for its declared scalar type, **When** its batch is built, **Then** the record is routed to errors as `etl.required-value-missing` or `etl.invalid-value`, naming the field, exactly as for HTTP JSON records.
5. **Given** a source file that is unavailable, does not match its pinned digest, or exceeds the snapshot limit, **When** the run is prepared, **Then** preparation fails with the existing file diagnostics and no partition becomes runnable.

---

### User Story 3 — Recover a JSON Lines run after worker loss (Priority: P1)

An operator restarts or loses an ETL worker while a JSON Lines partition has an owed batch whose output was already published but not yet accepted. A replacement worker continues from the retained snapshot and index, rebuilds the identical batch, republishes the same effect, and accepts it under a newer lease. The stale worker cannot advance the position.

**Why this priority**: the parent feature's central promise is recoverable, resumable partitions; a new input format must keep it.

**Independent Test**: run a two-partition JSON Lines → JSON Lines pipeline in real worker processes over one SQLite runtime store, kill the worker at each file-path boundary (after reservation, and after physical main and error publication but before acceptance), start a replacement process on the same stores, and verify the final accepted output, effect identities and accounting. Workbench has no deterministic gate at these boundaries, so the actual-host journey (US5) proves publication, running and inspection rather than the kill points.

**Acceptance Scenarios**:

1. **Given** an owed batch with a published main effect and no acceptance, **When** a replacement claims the partition after the lease expires, **Then** it rebuilds a batch with the same identity, input bytes and canonical hash, reuses the same effect identity and file, and accepts it once.
2. **Given** the stale owner tries to accept or advance after replacement, **When** it calls the store, **Then** the transition is refused and accounting is unchanged.
3. **Given** the retained snapshot or index bytes are altered after preparation, **When** a worker opens the partition, **Then** it refuses with `etl.snapshot-integrity-failed` or a reconstruction mismatch instead of producing different records.
4. **Given** the source file on the host changes after admission, **When** a replacement worker continues, **Then** it reads the retained snapshot, not the changed file.
5. **Given** a JSON Lines run with owed work, **When** it is cancelled, **Then** no new batch is reserved or accepted afterwards and the run settles as cancelled once, as for CSV.

---

### User Story 4 — Inspect a JSON Lines run (Priority: P2)

An operator opens a JSON Lines run in Studio or the runtime API and sees the source kind, the safe source identity (alias, revision and digest, never a host path), the total record count and each partition's ordinal range and next ordinal, as for CSV runs.

**Why this priority**: inspection reuses the existing file views; it matters but adds little new risk.

**Independent Test**: fetch the run detail and partition list for a JSON Lines run through the runtime API and check the source kind, record count and ranges.

**Acceptance Scenarios**:

1. **Given** a prepared JSON Lines run, **When** its detail is fetched, **Then** the source kind is `jsonl`, the source input identity and record count are present, and no host path appears.
2. **Given** a running JSON Lines run, **When** its partitions are listed, **Then** each shows its file ordinal range and next ordinal.

### User Story 5 — Run it from a workflow on a real host (Priority: P2)

An integration author publishes a JSON Lines pipeline through the Workbench's design API, runs it from a native workflow's RunEtl activity, and the workflow continues according to the run's outcome: success for a clean file, business error for a file with rejected lines.

**Why this priority**: it proves the slice in the shipped host and native boundary rather than only in test hosts.

**Independent Test**: on an actual Workbench host with file approvals configured, publish a JSON Lines → JSON Lines pipeline, start it from a native workflow, wait for the workflow, and compare the outputs and the run inspection with the expected values; repeat with a file containing rejected lines and observe the business-error outcome.

**Acceptance Scenarios**:

1. **Given** a clean two-partition file, **When** the workflow runs, **Then** RunEtl reports success, the main output holds every mapped record and the run inspection shows two partitions at their end ordinals.
2. **Given** a file with rejected lines, **When** the workflow runs, **Then** RunEtl reports a business error and the error output names each rejected ordinal.

### Edge Cases

- The file starts with a UTF-8 byte order mark: it is excluded from the first record and from every record's content.
- Lines end with LF or CRLF, in any mix; the last line has no terminator: each is a record.
- Blank or whitespace-only lines (space, tab, CR) anywhere, including at the end: they are not records and take no ordinal.
- A file that is empty or holds only blank lines: zero records and no partitions; the run settles as success when preparation commits (FR-011).
- CR characters inside a non-blank line are JSON whitespace and handled by the parser; a file using CR-only line ends is one long line and becomes one invalid record (`etl.jsonl.record-invalid`) or `etl.record-too-large`.
- A record whose content is exactly the maximum record size (parsed), or one byte over (`etl.record-too-large`).
- More than 2,147,483,647 records in one file: preparation fails with `etl.source-too-many-records`, because record ordinals in routed errors are 32-bit.
- More partitions requested than records: the partition count is the record count.
- A batch whose records are all filtered or all errors: accepted with zero main effects, under the existing rule.
- A record whose declared field resolves to an object or array: `etl.invalid-value`.
- Numbers that do not fit `int64`, or decimals with too many significant digits: `etl.invalid-value`, as for HTTP JSON.
- The same alias approved for the source role is used by a CSV pipeline and a JSON Lines pipeline: each run pins it with its own declared format.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** The v2 artifact accepts a source declaration `{"kind":"jsonl","binding":<alias>,"fields":[…]}` whose fields have the existing HTTP JSON field shape (name, JSON Pointer, scalar type, required). Existing source kinds, their JSON bytes and content hashes are unchanged.
- **FR-002** Design-time validation applies to a JSON Lines source the same rules it applies to HTTP JSON fields (unique names, valid pointers, field count within limits, a declared binding alias), and the mapping, filter and lookup rules that already apply to every source.
- **FR-003** A JSON Lines source binds to a host-approved file approval in the Source role, scoped to tenant and persistence partition, exactly as a CSV source does. Admission pins the revision, handle and SHA-256 digest; the pinned binding records the JSON Lines connector kind. Authors cannot name a path.
- **FR-004** Preparation copies the pinned file into an immutable, digest-verified run snapshot and publishes a byte index of its records before any partition is runnable, under the existing snapshot and retained-input limits. Indexing reads bytes, never whole records, so memory use does not depend on line length.
- **FR-005** Framing: the input is UTF-8; a byte order mark at offset 0 is skipped; a record's span is the bytes of one line up to and including its LF, and its content is the span without the LF or CRLF terminator; the final line may lack a terminator; lines made only of spaces, tabs and CRs are skipped. Record ordinals start at 1 and increase by one per record in file order. A record's raw input size, which is part of its canonical batch content and so of its batch identity, is its full span length.
- **FR-006** Each record is parsed independently as one JSON value: strict UTF-8, no comments, no trailing commas, unique property names at every level, nesting depth within the pipeline's declared JSON depth limit. Content longer than the maximum record size is not parsed. Line-level failures are routed record errors (`etl.jsonl.record-invalid`, `etl.jsonl.record-not-object`, `etl.record-too-large`), and field failures use the HTTP JSON field codes. Typed rows and error records carry the record's file ordinal and the code, never source text.
- **FR-007** File partitions, positions, batch ranges, batch identity, canonical batch content, rebuild verification, admission quotas, output retention, publication, acceptance and terminal settlement reuse the existing file mechanisms; checks that today accept only a CSV source accept any file source, keyed on the pinned source kind. When building a batch, the worker re-verifies the framing of every record it reads against the snapshot: no LF before the span's end, a terminating LF unless the span ends the file, and only whitespace between consecutive spans. A rebuilt batch with different records, bytes or hash is refused.
- **FR-008** No persisted shape changes: the prepared file record (snapshot, index, record count), the file position and the file batch range are reused; existing runtime databases need no migration, and existing CSV and HTTP runs keep their batch and effect identities.
- **FR-009** Studio offers "JSON Lines file" as a source kind, lists the Source file approvals for it, and edits its fields with the existing JSON field editor. Changing kinds keeps the binding and carries field names and types across, as it does between CSV and HTTP JSON.
- **FR-010** Run inspection reports the `jsonl` source kind, the safe source input identity and the record count, and lists file partition progress, as it does for CSV.
- **FR-011** When a file run's preparation yields zero records, the preparation commit also settles the run as success with its terminal result and owed native delivery, in the same transaction. This fixes the existing v2 CSV behaviour, where a header-only file left the run running with no partition able to finish it.

### Key Entities

- **JSON Lines source declaration**: the binding alias and declared fields of a v2 pipeline whose input is a JSON Lines file.
- **Prepared file input**: the existing prepared record of snapshot reference, index reference and record count, now holding either a CSV or a JSON Lines source.
- **Record index entry**: ordinal, start offset and end offset of one record in the snapshot; entry 0 is a sentinel covering only the byte order mark, if any.
- **Record error**: the existing routed error carrying ordinal, optional field and stable code.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** A JSON Lines → JSON Lines run over two partitions, and a JSON Lines → HTTP run, accept exactly the expected records on both routes, proved by tests through the real store and worker path.
- **SC-002** Every framing and parsing rule in FR-005 and FR-006, and every edge case listed above, has a test that fails when the rule is broken (a bite-proof is recorded for the framing and the line-error routing).
- **SC-003** In real worker processes over one runtime store, a worker killed after reservation, and one killed after physical publication and before acceptance, are each replaced; the replacement runs under a higher fence, accepts the same batch and effect identities once, the final output equals the uninterrupted output, and accounting returns to zero. Physical publication is reported as at least once. Cancellation of a JSON Lines run with owed work settles once.
- **SC-004** Existing CSV, HTTP and v1 artifacts keep their canonical bytes and hashes, the existing ETL test project passes in full, and a zero-record CSV and a zero-record JSON Lines run each settle as success through the worker with no manual settlement.
- **SC-005** Studio authoring of a JSON Lines source is covered by a component test and observed in a real browser against a running host.
- **SC-006** On an actual Workbench host, a native workflow runs a published JSON Lines pipeline to success and, with rejected lines, to business error, and the run inspection shows the source kind, record count and partition ranges.

## Assumptions and boundaries

- This slice adds file input only. CSV output, continuous runs, SQL and RabbitMQ connectors, BPMN invocation and the other #2908 criteria stay open; nothing here checks the file criterion off.
- Kill-point recovery is proved in the test project's real worker processes, not by killing Workbench, which has no gate at those boundaries. A frozen-writer upgrade journey is not needed because no persisted shape changes; existing migration and upgrade tests keep covering the stores.
- Lookups stay CSV files.
- All workers that share a runtime store run this version before a JSON Lines pipeline is published to them. A worker that predates it cannot read a JSON Lines artifact, and how it fails is not specified.
- Physical effects remain at least once; destination idempotency is the existing effect-identity and file-publication contract.
- ETL runtime and design persistence remain qualified on SQLite only; this slice does not change that.
- The JSON depth limit is the artifact's existing `http.maxJsonDepth`, the one JSON depth limit a v2 artifact declares; no new limit is added to the artifact.
- The design and runtime catalogs keep listing Source file approvals under the `csv` kind, for compatibility with existing clients; a JSON Lines source selects from those same approvals.
