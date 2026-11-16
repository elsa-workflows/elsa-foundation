# Implementation Plan: Finite JSON Lines ETL input

**Branch**: `agent/claude/2908-jsonl-input` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: [spec.md](spec.md), [research.md](research.md), source `main` at `3adffdfe1`.

## Summary

Add `jsonl` as a third v2 source kind. It reuses the CSV file path end to end: pinned file approval, immutable snapshot, byte index, file partitions and positions, owed batches, retained output, publication and fenced acceptance. The new parts are a byte-level line indexer at preparation, a per-line JSON reader at batch build that shares the HTTP JSON field extraction, a widened set of file-source checks keyed on the pinned source kind, a zero-record settlement fix that also repairs header-only CSV runs, and the Studio source-kind option. No persisted shape, migration or identity formula changes.

## Technical Context

**Language/Version**: C# on .NET 10 (SDK 10.0.402); TypeScript/React in the Studio ETL client.

**Primary Dependencies**: `System.Text.Json` (`JsonDocument`, `Utf8JsonWriter`), EF Core with SQLite for the ETL runtime store, Vitest and Playwright in Studio.

**Storage**: the existing ETL runtime and design stores (SQLite-qualified only) and the host snapshot root. No schema change.

**Testing**: xUnit in `Extensions/Etl/tests/Tests` (PR tier), including its child `Elsa.Etl.WorkerHost` processes; Vitest for the ETL Studio client; a Python driver against an actual Workbench host for the journey.

**Target Platform**: the Workbench host and runtime-only worker hosts.

**Project Type**: an extension domain (`Elsa.Etl`) with Core, Design, Runtime, Persistence, Workflows and Studio packages.

**Constraints**: preparation memory is independent of line length; every bound uses the existing artifact limits (`maxRecordBytes`, `maxSnapshotBytes`, `maxBatchRecords`, `maxBatchBytes`, `http.maxJsonDepth`, retained-input bytes); existing artifact bytes and hashes are untouched.

**Scale/Scope**: about 15 production files and 8 test files; one new public record in `Elsa.Etl.Core`.

## Constitution Check

Both constitutions and the review checklist were loaded.

| Gate | Result |
|---|---|
| §E2.2 / §E2.2.3 Design↔Runtime split, deployment shapes | Pass. Runtime changes take no Design reference; runtime-only execution is unchanged. |
| §E2.6 Executable-always-runs, artifact-only runtime | Pass. A JSON Lines run needs only the artifact, its pins and the retained snapshot. |
| §E6 Type naming R1–R8 | Pass with one recorded exception. New types: `EtlJsonlSource`, `EtlJsonlLineIndexer`, `EtlJsonRecordReader`, `EtlDuplicatePropertyException` (internal), and the Studio component `EtlJsonFieldsEditor` (renamed from `EtlHttpJsonSourceFieldsEditor` because it now edits both JSON sources); all within four components. **R4 exception:** `…Source` is reserved for "returns on request"; `EtlJsonlSource` is a member of the existing closed `EtlSource` declaration set beside `EtlCsvSource` and `EtlHttpJsonSource`, and renaming the set is out of scope. The wire discriminator `jsonl` matches the existing destination kind string. `EtlPreparedCsvInput` keeps its public name (research R3). |
| §2.21.1 Tests describing valid behaviour still pass | One existing test, `Empty_csv_preparation_can_settle_shared_terminal_success`, encodes the zero-record gap by settling by hand. It is rewritten to assert automatic settlement, with the reason recorded (FR-011). |
| §2.22.1 Extension-point catalog in the same unit | No extension point is added; the source kind is a closed polymorphic set. FR-011 adds an obligation to the existing optional `IEtlConnectorRuntimeStore.CommitConnectorPreparationAsync`, so `Extensions/Etl/src/Core/EXTENSION_POINTS.md` and the method documentation state it. `Extensions/Etl/studio/Elsa.Studio.Etl/EXTENSION_POINTS.md:16` describes the authoring surface as "HTTP or CSV sources" and is updated in the same unit (T027). |
| §2.23.1–§2.23.2 Registration and behaviour tests | Behaviour tests per FR; no new registration. |
| §2.23.3 Public by meaning | `EtlJsonlSource` is public, like its sibling source records; the indexer and record reader are internal. |
| §2.23.5 Infrastructure exceptions wrapped | `JsonException` and decoder failures become record errors; I/O failures keep the existing `etl.snapshot-*` diagnostics. |
| §4.1 / §E5 Public API delta | Additive: one record and its members, recorded in `Core/PublicAPI.Unshipped.txt`. |
| §E7 CI tiers | No new test project. |

No violations; one recorded naming exception (R4). Recorded debt: the shared `EtlJsonRecordReader`, created in this slice, is placed on purpose beside `EtlJsonPointer` in the `Http` namespace that already holds the JSON helpers, and the JSON Lines snapshot is opened through the pre-existing `EtlConnectorCsvSupport`; moving the JSON helpers to a format-neutral namespace is a later cleanup. Complexity Tracking is empty.

## Design

### Core (`Extensions/Etl/src/Core`)

- `EtlPipelineV2.cs`: add `[JsonDerivedType(typeof(EtlJsonlSource), "jsonl")]` and `public sealed record EtlJsonlSource([JsonRequired] string Binding, [JsonRequired] IReadOnlyList<EtlJsonField> Fields) : EtlSource`. Widen the `EtlPreparedCsvInput` summary to "CSV or JSON Lines file input".
- `EtlArtifactV2Json.cs:101`: the null-entry check covers `EtlJsonlSource.Fields`.
- `PublicAPI.Unshipped.txt`: the new record's members.

### Design (`Extensions/Etl/src/Design`)

- `EtlArtifactCompiler.V2.cs`:
  - `ValidateConnectorSource`: add a `jsonl` case, with the alias plus per-field pointer validation shared with the HTTP case through a small local helper.
  - `CheckConnectorStructureAndStrings`: count the fields and text.

### Runtime (`Extensions/Etl/src/Runtime/Elsa.Etl.Runtime`)

- `Bindings/HostConnectorBindings.cs`: resolve `EtlJsonlSource` through the file approvals in the Source role, pinned as `EtlConnectorKind.Jsonl`. The catalog's `FileKind` is unchanged (research R2).
- `Hosting/EtlConnectorExecutableReader.cs`:
  - Add `EtlJsonlSource` to the field-count switch (`:71`).
  - In `ValidateConnectors` (`:98`), require the alias and valid field pointers.
- `Output/IFileChunkPublisher.cs` and `FileChunkPublisherFactory.cs`: add `JsonlSnapshot`, a `.jsonl` store (R11).
- `Preparation/EtlConnectorPreparer.cs`, gate by gate:
  - `TryReadAdmission` `:254`/`:263`: accept the `Jsonl` source kind and build its source pin.
  - `:286-287`: a JSON Lines source refuses shards, like CSV.
  - `ValidatePolicy` `:318`: JSON Lines field count in 1..`MaxFields`.
  - `PrepareAsync`: publish the snapshot with the `JsonlSnapshot` publisher.
  - `PublishSourceIndexAsync`: for JSON Lines, skip the zero-byte `etl.source-header-missing` refusal (`:177`), reopen the snapshot with `.jsonl` (`:192`), use the identity kind `connector-jsonl-source-index` (CSV keeps `connector-csv-source-index`), and write entries with `EtlJsonlLineIndexer`.
  - Refuse more than `int.MaxValue` records with `etl.source-too-many-records`; the indexer takes an injectable record ceiling so the refusal is unit-testable.
  - The digest check is the same for both kinds.
- `Preparation/EtlJsonlLineIndexer.cs` (new, internal): a streaming byte scanner implementing the R5 framing. It writes `(ordinal, start, end)` entries through `EtlConnectorCsvSupport.WriteIndexEntry`, starting with the sentinel.
- `Http/EtlJsonRecordReader.cs` (new, internal, beside `EtlJsonPointer`; moved from `Http/EtlJsonPageReader.cs`):
  - `ReadFields(JsonElement record, IReadOnlyList<EtlJsonField>, IReadOnlyList<EtlJsonPointer>, int recordOrdinal)` covers pointer resolution, required/null handling and scalar typing, and returns a typed row or a field error. It has no size check: HTTP keeps its `rawBytes > MaxRecordBytes` check in the page reader, and JSON Lines classifies oversize from the index before parsing.
  - `TryReadExactDecimal`.
  - `ValidateDocument` throws one neutral internal exception for duplicate names. HTTP maps it to `etl.http.duplicate-property` and its existing `InvalidOperationException` decoder failures to `etl.http.page-invalid`; JSON Lines maps both to `etl.jsonl.record-invalid`.
  - HTTP keeps its page-local `int` ordinal and its `long` absolute index. JSON Lines passes the file ordinal (≤ `int.MaxValue`, guaranteed by preparation) for both.
  - The existing `EtlJsonPageReaderTests` pass unchanged.
- `Execution/EtlConnectorInputBatchBuilder.cs`:
  - Generalize `_csvSource` gates to "file source", where a CSV or JSON Lines source has `prepared.File`.
  - `OpenAsync`: CSV still compiles its header from entry 0; JSON Lines verifies the sentinel.
  - `ValidatePrepared`: the snapshot key extension depends on the source kind.
  - `PreparedPin`: accepts the `Jsonl` kind for the source role only.
  - `BuildFileRangeAsync`: dispatches per format. JSON Lines classifies oversize from the index; streams the span (and any gap before it) through a bounded buffer for the R10 checks, so an oversize line is never held in memory; buffers a span of at most `maxRecordBytes` plus a CRLF terminator (and never beyond `Array.MaxLength`); parses with `EtlJsonRecordReader` at `MaxDepth = http.maxJsonDepth`; and uses the file ordinal.
  - `SourceFieldNames`: covers JSON Lines fields.
- `Transformation/EtlRowTransformer.cs:346`: map `EtlJsonlSource.Fields` like HTTP fields.
- `Execution/EtlConnectorExecution.cs:193`: unchanged (the file path uses batch limits).

### Persistence (`Extensions/Etl/src/Persistence/EntityFrameworkCore/Stores`)

- `EtlConnectorRecordCodec.cs`:
  - `ValidateBindings`: add a `jsonl` source case with no shards.
  - `Binding()`: takes a `requiresDigest` flag. Inputs (source and lookup) require a SHA-256; outputs require none. This replaces the CSV-kind rule.
  - `ValidatePrepared`: applies to any file source. The index bound is `≤ Source × 24` for CSV and `≤ (Source + 1) × 24` for JSON Lines.
- `EfEtlConnectorRuntimeStore.cs` `CommitConnectorPreparationAsync`: when there are no initial positions, call the acceptance store's existing `CompleteConnectorRunIfReadyAsync` after the final `Running` save (otherwise that save would overwrite the success) and before the `expiry <= NowTicks()` check (so a lost lease rolls back the terminal and outbox rows too). The early `Replay` return keeps a replayed commit neutral. Runs already stuck in `Running` from before this change are not repaired; that is out of scope and recorded in the evidence.

### Runtime API (`Extensions/Etl/src/Runtime/Api/EtlRuntimeOperations.Connectors.cs:34`)

- `SourceInput` is reported for any file source kind (CSV or JSON Lines).

### Studio (`Extensions/Etl/studio/Elsa.Studio.Etl/Client/src`)

- `etlTypes.ts`: `EtlJsonlSource { kind: "jsonl"; binding; fields }`, added to `EtlSourceV2`.
- `components/EtlConnectorSourceEditor.tsx`:
  - A "JSON Lines file" option.
  - The binding picker filters Source approvals of kind `csv`, labelled "Source file binding".
  - The kind switch carries fields across, as the existing CSV↔HTTP switch does.
- `EtlJsonFieldsEditor.tsx` (renamed from `EtlHttpJsonSourceFieldsEditor.tsx`, since it now edits both JSON sources): accepts a JSON Lines source, rendering only the fields table (no records pointer or pagination).
- `PipelineBindingSelect.tsx`: an optional `subjectLabel` prop overrides the role and kind wording, so the JSON Lines picker reads "Source file" rather than "Source CSV".
- Pipeline editor summary labels and run inspection empty-state text name JSON Lines where they name only CSV/HTTP.

## Verification plan

Focused checks run while implementing. CI runs whole suites (`Build & test`, `Full tier`).

1. **Unit**, all in `Extensions/Etl/tests/Tests`:
   - `EtlJsonlInputTests.Line_indexer_*`: BOM, LF/CRLF mix, unterminated last line, blank and whitespace-only lines, empty file, entry bounds, sentinel.
   - JSON Lines batch-builder coverage of every FR-006 code: exact and over-max content, depth, top-level and nested duplicate names, invalid UTF-8, non-object, wrong types, object and array values, int64 and decimal bounds, CR-only files.
   - `EtlArtifactV2JsonTests`: round trip; `EtlV2ArtifactIdentityTests`: golden bytes and hashes for existing CSV and HTTP JSON v2 artifacts.
   - Compiler V2, executable reader, `HostConnectorBindings` and codec tests for the new kind and the input-digest rule.
2. **Preparer and batch builder**:
   - Preparer: unavailable file, digest mismatch, snapshot too large, empty and BOM-only files, shards refused, too-many-records through the injectable ceiling.
   - Batch builder: two partitions; rebuild identity; a tampered snapshot gives an integrity failure. For the R10 refusals, a forged index is written with its real hash and referenced from the prepared record, so the framing check (not the digest) is what refuses it.
   - The host file is changed after preparation, and the worker still reads the snapshot.
3. **Store**: the zero-record commit settles success for CSV and JSON Lines, and replay of the commit stays neutral.
4. **Worker** (`EtlRuntimeJourneyTests`):
   - JSON Lines → JSON Lines and JSON Lines → HTTP, with lookup and rejected lines, settling as a business error.
   - An empty JSON Lines file and a header-only CSV settle as success with no manual call.
   - Cancellation with owed work.
5. **Worker process recovery** (`EtlRuntimeJourneyTests.Jsonl.cs`), using the child `WorkerHost`:
   - Gates at `AfterReservation` and `BeforeAcceptance` on a two-partition file. The child host receives file bindings through `EtlWorkerConfiguration` and shares the snapshot root; its staging-pause publisher only maps the new snapshot format.
   - Crash, then start the replacement only after the predecessor's partition lease has expired, so owed-batch-first claiming gives it the owed partition. A gate checkpoints once per process, and partition 1 is otherwise free immediately.
   - Assert the higher fence, the same batch descriptor, effect identity and file bytes, no lost records and zero outstanding accounting.
6. **Studio**: a Vitest case for the JSON Lines kind switch and binding filter, and the `etl-connector-authoring` browser spec extended with a JSON Lines source.
7. **Actual host** (`specs/1190-etl-jsonl-input/evidence/jsonl_workbench_journey.py`, Python 3.9-compatible; `e2e-tests/` is PowerShell and pwsh is not installed here, and spec 1183 keeps its evidence drivers the same way):
   - Build Workbench; start it on a free loopback port with fixture file approvals.
   - Publish a JSON Lines pipeline through the design API.
   - Run it from a native workflow (clean file to success; rejected lines to business error).
   - Check outputs and runtime inspection.
   - Stop only the processes it started.
   - Evidence goes to `evidence/`.
8. **Real browser**: open the published pipeline in Studio against the running host and capture the JSON Lines source editor and run inspection.
9. **Bite-proofs**:
   - Revert the R5 blank-line skip and see the indexer tests fail.
   - Revert the record-error routing and see the batch-builder tests fail.
   - Revert FR-011 and see the zero-record worker tests time out or fail.
   - Remove each R10 check and see its forged-index test fail.
   - Restore each and see green.

The ETL test project runs whole once before the PR is marked ready, with executed counts compared to `--list-tests`. The Architecture guard and `Elsa.Maps.Generator -- check` run locally.

## Project Structure

```text
specs/1190-etl-jsonl-input/
├── spec.md, research.md, plan.md, data-model.md, quickstart.md, tasks.md
└── evidence/                # real-host and browser receipts
Extensions/Etl/src/{Core,Design,Runtime,Persistence}/…   # files above
Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/…
Extensions/Etl/tests/Tests/…
Studio/tests/browser/etl-connector-authoring.spec.ts
```

## Complexity Tracking

None.
