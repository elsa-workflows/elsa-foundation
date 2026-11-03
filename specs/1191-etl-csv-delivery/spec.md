# Feature Specification: Finite CSV ETL delivery

**Feature Branch**: `agent/claude/2908-csv-delivery` (stacked on `agent/claude/2908-jsonl-input`)

**Created**: 2026-11-02

**Status**: Implemented — [PR #3668](https://github.com/elsa-workflows/elsa-foundation/pull/3668); [evidence](evidence/README.md)

**Input**: The CSV delivery half of the file-connector criterion of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908) ("CSV and JSON Lines file ingestion/delivery have explicit positions, replay, parsing/validation and destination-idempotency behavior"), in [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). [Claim](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6580829972). Together with [spec 1190](../1190-etl-jsonl-input/spec.md) (JSON Lines input) it completes the criterion's four directions: CSV and JSON Lines, each for ingestion and delivery.

## Context

The v2 connector pipeline writes its main and error routes to JSON Lines files or HTTP JSON receivers. A file route body is built once per owed batch, retained under its effect identity before any dispatch, published create-only into the host-approved output folder under a key derived from that identity, verified, recorded, and then accepted under the batch's fence. This slice adds CSV as a second file destination format on the same path.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Deliver records as CSV files (Priority: P1)

An integration author picks **CSV file** as the destination of the accepted-records route, the rejected-records route, or both, and selects a host-approved output folder. Each batch publishes one self-contained CSV file: a header row of the mapped field names, then one row per accepted record, with every value written exactly.

**Why this priority**: it is the slice's purpose and the remaining direction of the file criterion.

**Independent Test**: run a CSV → CSV and an HTTP → CSV pipeline through the real worker and store, then read every published file and compare its header and rows with the expected mapped records.

**Acceptance Scenarios**:

1. **Given** a pipeline whose main route is CSV, **When** the run completes, **Then** each published main file starts with the header row of the mapped field names in mapping order, and the files together hold every accepted record once per effect identity.
2. **Given** values with commas, double quotes, CR or LF, leading or trailing spaces, non-ASCII text, decimals with trailing zeros, negative and very large integers, booleans and nulls, **When** they are written, **Then** each is written so that an RFC 4180 reader recovers the value: strings verbatim, quoted when they need it with inner quotes doubled; numbers as the canonical invariant text of the typed value (a decimal keeps its scale, so `1.50` stays `1.50`; `+5` and `007` from a CSV source were already typed as `5` and `7`); booleans as `true` or `false`; null as an empty unquoted field.
3. **Given** the CSV output of a run, **When** it is read back through a CSV source with the matching schema, **Then** every non-null value round-trips exactly. An empty string is written as `""` but, like an empty field, is read back as null by the CSV source.
4. **Given** a rejected record and a CSV error route, **When** the batch is published, **Then** the error file has the header `recordOrdinal,code,field` and one row per rejected record, carrying no source text.
5. **Given** an existing pipeline with JSON Lines or HTTP routes, **When** it runs after this change, **Then** its artifact bytes, hashes, effect identities and outputs are unchanged, except the exact-limit HTTP bracket cases in Edge Cases.

---

### User Story 2 — Recover CSV delivery after worker loss (Priority: P1)

An operator loses a worker after it published a CSV file and before the batch was accepted. The replacement republishes the same effect to the same key with the same bytes, which the create-only publication treats as already present, and accepts it once.

**Why this priority**: destination idempotency is part of the criterion; a new format must keep it.

**Independent Test**: in real worker processes over one runtime store, kill the worker after physical CSV publication and before acceptance, start a replacement, and verify the published file set, effect identities and accounting.

**Acceptance Scenarios**:

1. **Given** a published but unaccepted CSV effect, **When** a replacement claims the partition after the lease expires, **Then** it accepts the same effect identity under a higher fence and no second file appears.
2. **Given** a different file already exists at the effect's key, **When** publication runs, **Then** it is refused with `etl.effect-conflict`, the existing file is left untouched, the batch is not accepted, and the run settles with that diagnostic through the existing shared settlement path (which classifies it as a business error, as it does for JSON Lines).
3. **Given** a retained CSV route body that no longer matches its hash, **When** dispatch runs, **Then** it is refused before anything is published.

---

### User Story 3 — Author and inspect CSV delivery (Priority: P2)

In Studio the author chooses CSV as a route's destination kind and picks an output folder approval. In run inspection the operator sees the route type "CSV file", the published route counts and partition progress.

**Independent Test**: a component test and a browser test cover the destination choice; the runtime API and a real browser show a CSV run's inspection.

**Acceptance Scenarios**:

1. **Given** the connector editor, **When** the author picks CSV for a route, **Then** the binding picker lists the host's output folder approvals for that role, and the saved draft holds `{"kind":"csv","binding":…}`.
2. **Given** a CSV run, **When** it is inspected, **Then** the main or error output type reads "CSV file" and published effects count as published.

---

### User Story 4 — Run it from a workflow on a real host (Priority: P2)

On an actual Workbench, a native workflow runs a published JSON Lines → CSV pipeline (source from spec 1190) to success, and a file with rejected lines to business error, with CSV files on both routes.

**Independent Test**: a copy of the spec 1190 journey driver with CSV routes publishes the pipeline through the design API, runs it from a native Flowchart and checks the published CSV files.

### Edge Cases

- A batch with no accepted records publishes no main file; a batch with no rejected records publishes no error file.
- A mapped string field whose value is only spaces, starts with `=`, `+`, `-` or `@`, or holds a lone CR: written verbatim with RFC 4180 quoting only. Spreadsheet formula escaping is not applied, because it would change the data.
- A mapped field name containing a comma, quote or line break: the header cell is quoted like any value. The same holds for an error row's field name.
- A mapping with a single output field whose value is null writes an empty line. Elsa's CSV reader reads it as one null field, but many general-purpose readers skip blank lines; this is a stated limitation of single-column CSV.
- A row that would fit as CSV but whose intermediate JSON object is larger: it is accepted, because the CSV bytes are what is bounded (FR-007). The transformer's memory-only bound of eight times the batch output limit can never reject such a row (research R2).
- Output exactly at, and one byte over, the batch output byte limit, counting the header row: at the limit the row is written; one byte over, that row is rejected to the error route as `etl.output-too-large`, as for JSON Lines. If the header plus the first row do not fit, that row is rejected and the header is not written; the next row that fits carries it. A CSV row rejected for size carries no field name. If the rejection's own error row cannot fit either, the batch fails with `etl.output-too-large`, as it does for JSON Lines.
- An HTTP route in the same pipeline: the closing `]` of its JSON array is reserved when its first element is appended, so a file route (CSV or JSON Lines) that fills the allowance exactly cannot leave the array unclosable; previously that case failed the batch.
- An HTTP main route's first row whose JSON exactly fills the allowance: the row is bounded with its reserved bracket, so it is rejected to the error route as `etl.output-too-large` instead of failing the batch. This changes existing HTTP-main pipelines only at that exact limit.
- Many batches: each file repeats the header, so every file can be read on its own.
- Main route CSV and error route JSON Lines, or the reverse, in one pipeline.
- The same output folder approval used by a JSON Lines route in one pipeline and a CSV route in another: files differ by extension and effect identity, so they never collide.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001** The v2 artifact accepts a destination `{"kind":"csv","binding":<alias>}` for the main route, the error route or both. Existing destination kinds, their bytes and their hashes are unchanged.
- **FR-002** Design-time validation and runtime executable reading accept the CSV destination under the same alias rules as JSON Lines destinations.
- **FR-003** A CSV destination binds to a host-approved output folder approval for its role (MainOutput or ErrorOutput), exactly as a JSON Lines destination does, and is pinned as the CSV kind with no digest.
- **FR-004** A main-route CSV body is UTF-8 without a byte order mark. Every row, header included, ends with CRLF. The header row holds the mapped output field names in mapping order. Each record row holds its values in the same order.
- **FR-005** Value formatting: a string is written as-is, and is enclosed in double quotes when it is empty or contains a comma, a double quote, CR or LF, or begins or ends with a character for which `char.IsWhiteSpace` is true, with each inner double quote doubled. A 64-bit integer and a decimal are written as the canonical invariant-culture text of the typed value (a decimal keeps its scale; never an exponent). A boolean is `true` or `false`. A null is an empty unquoted field. Header cells follow the string rule.
- **FR-006** An error-route CSV body has the header `recordOrdinal,code,field` and one row per rejected record: the ordinal, the stable code, and the field name or an empty field, each cell following the FR-005 string rule. It carries no source text.
- **FR-007** The CSV route body counts against the existing batch output byte limit and the run's retained output budget, header rows included. For a CSV route the bound that decides a row is the CSV bytes it adds (with the header when it is the route's first row), not the size of the transformer's intermediate JSON. A row that does not fit is rejected to the error route as `etl.output-too-large`, as a JSON Lines row is.
- **FR-008** Retention, effect identity, create-only publication to a key derived from the effect identity (with a `.csv` extension), post-publication verification, recording and fenced acceptance follow the existing file route path. A conflicting existing file is refused. Republishing the same effect is idempotent.
- **FR-009** Existing persisted rows keep their meaning: the per-batch effect-kind column records the delivery mechanism (HTTP delivery or file publication), so a CSV file effect takes the existing file-publication value and no migration is needed. Existing JSON Lines and HTTP effect keys, identities and stored values are unchanged.
- **FR-010** Studio offers "CSV file" as a destination kind for each route in the connector editor, lists output folder approvals for it under a "file" label (not "JSONL output"), and names CSV in the pipeline stage text and the connector authoring profile text. Run inspection reports the CSV output type as "CSV file" (a CSV source still reads "CSV") and counts published CSV effects as published.
- **FR-011** The ETL README, the Core README and the Design and Studio extension-point catalogs list the CSV destination.

### Key Entities

- **CSV destination declaration**: the binding alias of a route whose output is CSV files.
- **CSV route body**: the retained bytes of one batch's route, header plus rows, under its effect identity.
- **File effect**: one create-only publication at a key derived from the effect identity; `.jsonl` or `.csv` by route kind.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001** CSV → CSV, HTTP → CSV and JSON Lines → CSV runs through the real worker and store publish exactly the expected header and rows on both routes.
- **SC-002** Every formatting rule in FR-004 to FR-006 and each listed edge case has a test that fails when the rule is broken, and the quoting rule and header rule have recorded bite-proofs.
- **SC-003** In real worker processes, a worker killed after CSV publication and before acceptance is replaced; the replacement accepts the same effect identity once under a higher fence, no second file appears, and accounting returns to zero.
- **SC-004** Existing CSV, HTTP, JSON Lines and v1 artifacts keep their canonical hashes, existing persisted batch rows are unchanged (outputs change only in the exact-limit HTTP bracket cases in Edge Cases), and the whole ETL test project passes.
- **SC-005** Studio authoring of a CSV destination is covered by a component test and a browser test, and observed in a real browser against a running host.
- **SC-006** On an actual Workbench, a native workflow runs a JSON Lines → CSV pipeline to success and to business error, and the published CSV files hold the expected header and rows.

## Assumptions and boundaries

- This completes the file criterion's code paths together with spec 1190; the criterion is checked on #2908 only when both are merged and verified.
- CSV output is for data interchange, not spreadsheets; values are not altered to prevent formula evaluation.
- Delimiter, quote character, line terminator and encoding are fixed (comma, double quote, CRLF, UTF-8). Configurable dialects are out of scope.
- All workers, and every API or Studio host inspecting runs, that share a runtime store run this version before a CSV destination is published to them; an older host cannot read a CSV artifact or a CSV output plan.
- Physical publication remains at least once; idempotency is the create-only key and verified bytes.
- ETL stores remain SQLite-qualified only.
