# Research: Finite JSON Lines ETL input

Source snapshot: `main` at `3adffdfe1`. Each decision names what it reuses and why the alternative was rejected.

## R1 — Declaration reuses the HTTP JSON field shape

**Decision.** `EtlJsonlSource(Binding, Fields)` with `Fields: IReadOnlyList<EtlJsonField>`, discriminator `"jsonl"` on `EtlSource`.

**Why.** `EtlJsonField` (name, JSON Pointer, scalar type, required) already describes how a typed field is selected from one JSON object, and the transformer, canonical batch writer and Studio field editor already consume it. A JSON Lines record is that same object without a page around it, so it needs no records pointer and no pagination.

**Rejected.** A CSV-style column schema matched by property name: it cannot reach nested values and would need a second extraction path.

## R2 — Same Source file approvals; the pin records the format

**Decision.** `HostConnectorBindings` resolves a JSON Lines source through the existing file approvals in the `Source` role and pins it as `EtlConnectorKind.Jsonl` with its SHA-256. The persisted binding check requires a digest for every file *input* role (source and lookup, whatever the format) and none for outputs, rather than keying the digest rule on the CSV kind.

**Why.** Host file approvals are files, not formats; the pipeline declares the format. `EtlConnectorKind.Jsonl` already exists and serializes as `"jsonl"`, so no enum member, numeric value or persisted string is added, and batch and effect identities, which hash `(int)Kind`, keep their values for CSV and HTTP. A JSON Lines run's identities differ from a CSV run over the same file because the pinned kind differs, which is correct.

**Catalog.** `ListBindings` keeps reporting Source and Lookup file approvals as kind `csv`. Changing that string would break current Studio and API clients; Studio's JSON Lines picker filters Source approvals of kind `csv`, labelled as a source file. A format-neutral `file` kind would be cleaner and is left for a later API revision.

## R3 — Prepared input record, position and batch range are reused

**Decision.** `EtlConnectorPreparedRun.File` (`EtlPreparedCsvInput`: snapshot reference, index reference, record count) holds the JSON Lines snapshot and index too; positions are `EtlConnectorFilePosition` and batch ranges `EtlConnectorFileBatchRange`.

**Why.** None of these carry format: the record type is a snapshot, an index and a count. Reusing them means no new persisted JSON shape, no migration and no change to batch identity (`etl-connector-file-v2` hashes the two references and the count). The C# type name `EtlPreparedCsvInput` is a public name, not a persisted identifier; it is kept to avoid a public API break and its documentation is widened. Renaming it is a pre-GA cleanup candidate, not part of this slice.

**Rejected.** A new `EtlPreparedJsonlInput` case: it would add a persisted shape, a codec branch and a compatibility decision for no behavioural gain.

## R4 — Snapshot key extension

**Decision.** The JSON Lines snapshot is published through a new `.jsonl` input-snapshot chunk publisher (distinct from the output publisher, so output-side test gates do not fire during preparation), so its key ends in `.jsonl`; CSV snapshots keep `.csv`. Every check of the source snapshot key takes the extension from the pinned source kind.

**Why.** The key extension is the only on-disk hint of the content; reusing `.csv` for JSON would mislead an operator reading the object root. Keys are content-identity hashes of distinct identity documents, so a source snapshot cannot collide with an output chunk.

## R5 — Framing and the index

**Decision.** Indexing scans bytes once: skip a BOM (`EF BB BF`) at offset 0; split on LF; a record's span runs from its first byte to the end of its LF (or to EOF for an unterminated last line); a line whose bytes are all space, tab or CR is skipped. Index entry 0 is a sentinel `(0, 0, bomLength)`; entry *i* is `(i, start, end)`. Spans increase strictly and never overlap, but skipped blank lines leave gaps, so the batch builder checks `start ≥ previous end` for JSON Lines where it checks `start = previous end` for CSV, and re-verifies the framing itself (R10). Opening a JSON Lines partition checks the sentinel is `(0, 0, 0)` or `(0, 0, 3)` with a BOM present, instead of parsing a header.

**Why.** The sentinel keeps the index layout, ordinal base and record count formula (`index bytes / 24 − 1`) identical to CSV, so partition plans, the store's index-size check and recovery code apply unchanged. The scan never buffers a line, so memory is independent of line length. An empty file gives one sentinel entry and zero records, and a one-byte unterminated file gives two entries, so the store's index bound becomes `(sourceBytes + 1) × 24` for JSON Lines; for CSV it stays `sourceBytes × 24`. A count above `int.MaxValue` fails preparation with `etl.source-too-many-records`, because routed error ordinals are 32-bit, as CSV's already are.

**Rejected.** Treating blank lines as error records: common JSON Lines writers end files with a blank line, and gaps cost nothing because the index is digest-verified.

## R6 — Record parsing and errors

**Decision.** At batch build each record's content (span minus its terminator) is parsed with `JsonDocument` (no comments, no trailing commas, `MaxDepth = http.maxJsonDepth`), every string and property name is decoded so invalid UTF-8 anywhere fails, and duplicate property names at any level fail. Content (span minus its LF or CRLF terminator) longer than `limits.maxRecordBytes` is classified `etl.record-too-large` from the index alone. A record's `rawInputBytes`, which enters the canonical batch hash and so the batch identity, is its full span, matching CSV's `end − start`. Typed rows and errors carry the file ordinal, where the HTTP reader carries a page-local one. The top-level value must be an object. Field extraction and scalar typing reuse the HTTP JSON record reader, moved to a shared internal helper so both sources share one implementation.

**Why.** It matches the HTTP JSON page reader's rules, so a record means the same thing from either source, and line-level problems become record errors because a framed line is still a well-delimited record. The HTTP reader's exact-decimal parsing and pointer resolution are reused, not copied.

**Rejected.** Failing preparation on the first bad line: one bad line would discard a whole file; CSV fails preparation only for framing errors that make record boundaries unknowable, which cannot happen with LF framing.

## R7 — Depth limit

**Decision.** Use the artifact's existing `http.maxJsonDepth` (default 32).

**Why.** Adding a JSON Lines depth limit to `EtlConnectorLimits` would change the artifact model and need a hash-preserving default; the existing knob already bounds JSON depth in v2 artifacts. Documented in the spec's assumptions.

## R8 — Mixed-version workers

**Decision.** No compatibility shim. A runtime built before this change fails to read a `"jsonl"` source (`IgnoreUnrecognizedTypeDiscriminators = false`) and diagnoses the artifact as invalid. The spec requires all workers sharing a store to be upgraded before JSON Lines pipelines are published.

**Why.** The artifact format is closed by design; forward compatibility was never offered for new connector kinds (the HTTP slice added `httpJson` under the same rule).

## R9 — Zero-record runs settle at preparation

**Decision.** `CommitConnectorPreparationAsync` settles the run as success, with its terminal result and owed native delivery, in the same transaction when the prepared input yields zero partitions (only a file source can).

**Why.** Found while scoping: a header-only v2 CSV run commits zero partitions and stays `Running`, because completion is checked only on batch acceptance and HTTP page advance, and no worker settles success. The v1 store already settles zero-record runs at preparation (`EfEtlRuntimeStore`). An empty JSON Lines file would hit the same gap. The existing store test that settled the empty CSV run by hand is changed to assert the automatic settlement; the reason is recorded in the test.

## R10 — Framing is re-verified at batch build

**Decision.** Reading a record, the batch builder checks that its span holds no LF before its last byte, ends in LF unless it ends the file, and that the bytes between the previous span's end and its start are only space, tab, CR or LF.

**Why.** CSV batch builds re-parse each span and compare offsets, so a wrong index cannot silently shift records. The index is digest-verified, but the same independent check keeps JSON Lines at the CSV standard for little cost.

## R11 — Snapshot publisher format

**Decision.** Add `EtlFileChunkFormat.JsonlSnapshot`, a `.jsonl` store distinct from the output `Jsonl` format.

**Why.** Test hosts gate output writes by chunk format; sharing the output format would fire output gates during preparation and blur the two roles.
