# Research: Finite CSV ETL delivery

Source snapshot: `agent/claude/2908-jsonl-input` at `f1b85fa0c` (spec 1190, PR #3657). That branch is the base of this one.

## R1 — Declaration

**Decision.** `EtlCsvDestination([JsonRequired] string Binding)`, discriminator `"csv"` on `EtlDestination`, beside `EtlJsonlDestination` and `EtlHttpDestination`.

**Why.** It mirrors the JSON Lines file destination exactly; a CSV dialect is fixed (FR-005), so there is nothing else to declare.

## R2 — Build CSV rows from the transformer's JSON objects

**Decision.** The route body builder converts each accepted row's JSON object (already produced by `EtlRowTransformer`, properties in mapping order, values as JSON scalars) into one CSV row. String tokens are decoded and quoted per FR-005. Number tokens are copied as their raw JSON text, which is the exact invariant text the transformer wrote for `int64` and `decimal`. `true`/`false` stay as written. `null` becomes an empty field. The header comes from the pipeline's mapping names.

**Why.** No transformer change. Exactness is inherited from the transformer's JSON writer, which already writes `decimal` with its scale and never in exponent form. Re-deriving values would duplicate the formatting.

**Byte bound (spec review blocker).** The transformer bounds each accepted row's JSON object by the remaining output allowance and rejects an overflowing row as `etl.output-too-large`. The JSON object is always at least as large as its CSV row: it adds property names, writes null as `null`, and escapes non-ASCII as `\uXXXX`. Passing the CSV allowance to the transformer would therefore reject rows that fit as CSV, and it would never count the header. So, for a CSV route:
- the transformer is given a memory-only bound of eight times the batch output limit, saturating. No CSV row that fits can exceed it. JSON escaping adds at most 6 bytes per UTF-8 byte of a value. Each field adds at most its name length plus 4 bytes of name and structure, and the header already pays the name length plus 1 for that field. So the JSON object of any CSV row that fits after its header is at most 8 × (header + row) ≤ 8 × the limit. The per-row allocation is transient and can exceed the attempt's charge of one batch output limit (`EtlConnectorExecution.cs:196`); it is not retained;
- the CSV writer applies the real bound. It measures the row, adding the header if this is the route's first row, against the remaining allowance. If the row does not fit, it rejects that row to the error route as `etl.output-too-large` with the record's ordinal, exactly as the JSON Lines path does. When the first row does not fit, the header is not written either.

JSON Lines and HTTP routes keep today's transformer bound.

**Rejected.** A second transformer output channel with typed values: more surface for the same bytes.

## R3 — Header in every file

**Decision.** Each published file, one per batch and route, begins with its header row.

**Why.** Files are published independently and may be consumed one at a time. A header only in the "first" file would depend on publication order, which is not defined across partitions.

## R4 — Dialect

**Decision.**
- Comma delimiter.
- Double-quote quoting with doubled inner quotes.
- CRLF after every row.
- UTF-8 without a BOM.

A string is quoted when it is empty or holds a comma, a quote, CR, LF, or leading or trailing whitespace.

**Why.**
- This follows RFC 4180, which most consumers read.
- Quoting an empty string distinguishes it from null for readers that care. Elsa's own CSV source reads both as null, which the spec states.
- Quoting values with edge whitespace stops trimming readers from changing them.
- CRLF is the RFC terminator. Elsa's CSV reader accepts both LF and CRLF.

## R5 — Error route columns

**Decision.** `recordOrdinal,code,field`, matching the JSON Lines error object `{"recordOrdinal","code","field"?}` in the same order. A missing field name is written as an empty field.

## R6 — Persistence: effect kind column without a migration

**Finding.** `elsa_etl_connector_batches.MainEffectKind` and `ErrorEffectKind` store `(int)route.Kind`, under the CHECK constraint `IN (1, 2)` (HTTP = 1, JSONL = 2). `EtlConnectorKind.Csv` is 0. Storing it would violate the constraint, and widening a CHECK constraint on SQLite needs a full table rebuild.

**Prior writers.** Every writer since the column's migration (`20261021170000_ConnectorInspectionEffects`) has written 1 for HTTP and 2 for JSONL, and nothing else, through `EfEtlConnectorOutputStore`. The only readers are:
- the inspector, which tests the column for null;
- `ReadConnectorBatchOutput` in the output store, which compares it with the retained output plan. It is reached from the batch store, acceptance and the inspector.

**Decision.** Define the column as the delivery mechanism: 1 is an HTTP delivery and 2 is a file publication. The store maps JSON Lines and CSV route kinds to 2 through one function, used both when it writes the column and when its replay check compares the stored value with the retained plan (`EfEtlConnectorOutputStore.cs:123-124`). With a raw `(int)Kind` comparison, every CSV row would fail its replay read. Tests cover a CSV row on write and on replay read. The exact route kind stays in the retained output plan JSON (`"kind":"csv"`), which is what acceptance and inspection already read. Existing rows keep both their values and their meaning. `EtlConnectorRuntimeApiHttpTests` rewrites a row's effect columns exactly as the prior writer did (`(int)route.Kind`) and reads it back through inspection unchanged. `EtlConnectorCsvOutputTests` shows a CSV effect stored as 2, and a CSV route stored as 1 refused on read.

The migration backfill (`20261021170000`, http→1, jsonl→2) already matches the mechanism meaning, and no CSV row predates this change. Two existing inspector tests assert the column as `(int)EtlConnectorKind`. They pass only because JSON Lines is 2, so they are rewritten against the mechanism constants, with the reason recorded.

**Rejected.** A migration that rebuilds the batch table to allow 0: it carries risk to the foreign keys and indexes for no information the plan JSON does not already hold.

## R7 — File keys and publishers

**Decision.**
- `EtlRuntimeIdentity.ConnectorFileOutputKey` gains a kind-aware overload that returns `.csv` for CSV. The JSON Lines result is unchanged.
- A new `EtlFileChunkFormat.CsvOutput` publisher writes `.csv` output files, distinct from the CSV input-snapshot format, so input-side test gates do not fire on output.
- The retained body identity is `etl-connector-csv-route-body-v1:<effectId>` in a `.csv` body store.

**Why.** Existing JSON Lines keys and retained identities must not move (FR-009). A CSV effect's key differs from any JSON Lines key by extension, because its effect identity differs by route kind through the pinned binding.

## R7a — Gates keyed on the JSON Lines or HTTP destination kinds

Each of these must accept a CSV route:

| File and location | What it does |
|---|---|
| `EtlConnectorOutputProcessor.cs:113` | Dispatches a route to file publication |
| `EtlConnectorOutputProcessor.cs:254` | Expected key, which needs the kind-aware overload |
| `EtlConnectorOutputProcessor.cs:258` | Verifies the publication through `_jsonlStore`. **Trap:** this read finds no `.csv` file and reports `etl.effect-conflict`, so it must use the kind's store. |
| `EtlConnectorOutputProcessor.cs:387-405` | Kind, body store and retained identity |
| `EfEtlConnectorAcceptanceStore.cs:37-38` | Publication kind and expected key, which needs the kind-aware overload |
| `EfEtlConnectorAcceptanceStore.cs:228-229` | `ReadConnectorFilePublications` |
| `EfEtlConnectorAcceptanceStore.cs:241` | `Delivered` |
| `EfEtlConnectorOutputStore.cs:92-93` | Mechanism write |
| `EfEtlConnectorOutputStore.cs:123-124` | Replay comparison, which must use the same mapping |
| `EfEtlConnectorOutputStore.cs:167` | Kind whitelist |
| `EfEtlConnectorRuntimeInspector.cs:143` | Effect state |
| `EtlRuntimeOperations.Connectors.cs:73, 76` | API effect kinds and `published` |
| `EtlConnectorRecordCodec.cs:177` | Destination pin |
| `HostConnectorBindings.cs:69` | Destination resolution |
| `HostConnectorBindings.cs:84` | Output pins default to JSON Lines, so CSV needs an override |
| `EtlArtifactCompiler.V2.cs:156, 238` | Design-time validation |
| `EtlConnectorExecutableReader.cs:126` | Destination alias switch |
| `tests/WorkerHost/StagingPausePublisherFactory.cs:15, 24-28` | Pauses `OutputChunk` only for the JSON Lines format, so `CsvOutput` needs mapping too |

## R8 — Bindings and catalog

**Decision.** A CSV destination resolves through the role's output folder approval, pinned with `EtlConnectorKind.Csv` and a null digest. Spec 1190's role-based digest rule already requires a null digest for every output. The binding catalog keeps listing output approvals as `jsonl`, as spec 1190 kept source approvals as `csv`. Studio's CSV route picker filters those approvals and labels them "file".

## R9 — Mixed versions

**Decision.** No shim. A runtime that predates this change cannot read a `"csv"` destination. It would also refuse a CSV plan row: its replay check compares the stored effect kind with `(int)Csv`, and its output-shape whitelist rejects the kind. An older API or Studio host inspecting a CSV run would hit the same refusal. All workers sharing a store are upgraded before CSV destinations are published (stated in the spec).
