# Implementation Plan: Finite CSV ETL delivery

**Branch**: `agent/claude/2908-csv-delivery`, stacked on `agent/claude/2908-jsonl-input` (PR #3657). **Date**: 2026-11-02. **Spec**: [spec.md](spec.md).

**Input**: [spec.md](spec.md), [research.md](research.md), source `f1b85fa0c`.

## Summary

This plan adds `csv` as a third v2 destination kind. It reuses the file-route path end to end: retained body, effect identity, create-only publication, verification, recorded publication and fenced acceptance.

What is new:
- a CSV body writer that turns the transformer's JSON objects into RFC 4180 rows, with a header in every file;
- a fixed CSV error-row shape;
- a kind-aware output key and publisher;
- file-kind gates widened from JSON Lines to any file destination;
- the effect-kind column redefined as the delivery mechanism, without a migration;
- the Studio route option.

## Technical Context

**Language/Version**: C# on .NET 10 and TypeScript/React. **Storage**: the existing SQLite-qualified ETL runtime store; no schema change. **Testing**: xUnit in `Extensions/Etl/tests/Tests` (PR tier, child-process worker host), Vitest, Playwright, and the spec 1190 Workbench driver extended.

## Constitution Check

Both constitutions and the review checklist were loaded.

| Gate | Result |
|---|---|
| §E2.2/§E2.6 | Pass. The runtime gains no design reference, and runs need only artifact plus pins. |
| §E6 naming | `EtlCsvDestination`, `EtlCsvRouteWriter` (internal) and `EtlFileChunkFormat.CsvOutput`. All are within four components and need no exception. |
| §2.21.1 | `EtlConnectorRuntimeInspectorTests` asserts the effect-kind column as `(int)EtlConnectorKind`. Those assertions are rewritten against the mechanism constants (1 HTTP, 2 file), and the reason is recorded. Any test of the JSONL-only file-route gate is widened, with its reason. |
| §2.22.1 | `Core/EXTENSION_POINTS.md` already describes the connector store. The effect-kind redefinition is an internal persistence detail and is documented in the EF store and DbContext. The Studio and Design catalogs name the new destination. |
| §2.23.3 / §4.1 / §E5 | Additive public surface, a minor change by SemVer class: `EtlCsvDestination` and its members, plus a public `ConnectorFileOutputKey(string, EtlConnectorKind)` overload that throws for non-file kinds. Both are recorded in `Core/PublicAPI.Unshipped.txt`. Compared by hand with the shipped baseline: no member is removed or changed. Versions are computed. |
| §E7 | No new test project. |

No violations.

## Design

### Core

- `EtlPipelineV2.cs`: `[JsonDerivedType(typeof(EtlCsvDestination), "csv")]` and the `EtlCsvDestination(Binding)` record.
- `EtlRuntimeIdentity.cs`: `ConnectorFileOutputKey(string effectId, EtlConnectorKind kind)` returns the same hash with `.jsonl` or `.csv`. The one-argument form stays and keeps returning `.jsonl`.
- `PublicAPI.Unshipped.txt`.

### Design and runtime validation

- `EtlArtifactCompiler.V2.cs`, in `ValidateDestination` and the text budget: add a `csv` case.
- `EtlConnectorExecutableReader.cs`: the destination alias switch.

### Runtime

- `Bindings/HostConnectorBindings.cs`: `DestinationAsync` resolves `EtlCsvDestination` through the role's file approval with `Kind = Csv`.
- `Output/IFileChunkPublisher.cs` and `FileChunkPublisherFactory.cs`: `CsvOutput`, a `.csv` store.
- `Output/EtlCsvRouteWriter.cs` (new, internal): writes the header and record rows from the transformer's JSON objects (R2, FR-004 and FR-005), and error rows (FR-006) with every cell under the string rule. It measures each row, with the header when it is the route's first row, before appending it.
- `Execution/EtlConnectorOutputProcessor.cs` (a CSV row rejected for size carries a null field):
  - `RouteBodyBuilder` takes the destination kind. For a CSV main route, `BuildOutputAsync` calls the transformer with the memory-only bound (R2). If the measured CSV row, with the header if first, does not fit the remaining allowance, the row is rejected to the error route as `etl.output-too-large` with the record's ordinal. JSON Lines and HTTP keep the transformer's bound.
  - `EtlConnectorKindFor`, `BodyStore` and `RetainedBodyIdentity` gain CSV entries (`etl-connector-csv-route-body-v1:`, `.csv` store).
  - `RouteBodyBuilder` for an HTTP array reserves its closing `]` when it appends the first element, and `Finish` adds no unreserved byte. An HTTP main route's first row is transformed with one byte less, so an exact-limit first row is rejected rather than failing the batch. Both fix existing HTTP limit cases.
  - `DispatchAsync` sends JSON Lines and CSV to `PublishFileRouteAsync`. That method takes its publisher, its verification store (not the fixed `_jsonlStore`) and its key from the route kind.
- Every gate in research R7a is updated, including the `StagingPausePublisherFactory` test host mapping.

### Persistence

- `EtlConnectorRecordCodec.Destination`: a `csv` case pinned as `EtlConnectorKind.Csv` with no digest.
- `EfEtlConnectorOutputStore`: `MainEffectKind`/`ErrorEffectKind` store the delivery mechanism, 1 for HTTP and 2 for a file (JSON Lines or CSV). One mapping function serves both the write path and the replay comparison. `EfEtlConnectorOutputStore.cs:167` accepts the CSV route kind.
- `EfEtlConnectorAcceptanceStore`:
  - `RecordConnectorFilePublicationAsync` accepts JSON Lines or CSV and computes the expected key with the kind-aware overload.
  - `ReadConnectorFilePublications` and `Delivered` treat both file kinds alike.
- `EfEtlConnectorRuntimeInspector`: the effect state for a CSV route is `published` or `retained`, as for JSON Lines.
- `EtlRuntimeDbContext`: a comment on the effect-kind columns states the mechanism meaning. The constraint is unchanged.

### Runtime API

- `EtlRuntimeOperations.Connectors.cs` `Effect`: accept the CSV kind, with `published` allowed for either file kind.

### Studio

- `etlTypes.ts`: the `EtlCsvDestination` type, added to the destination union.
- `EtlConnectorPipelineEditor.tsx` route editor:
  - a "CSV file" option;
  - a binding picker for file kinds, filtered to `jsonl` approvals and labelled "Accepted file output" or "Rejected file output" through `subjectLabel`;
  - the stage text "Choose HTTP, JSONL or CSV independently";
  - the connector profile text in `authoringProfiles.tsx` names CSV.
- `EtlRunInspection.tsx`: `kindLabel` gives "CSV file" for output types, so a CSV source still reads "CSV".
- Doc comments that name only JSONL for file routes are widened: `Core/Runtime/EtlConnectorBatch.cs:30` and `:39`, `IEtlConnectorRuntimeInspector.cs:40`, `EtlRuntimeIdentity.cs:87` and `Runtime/Api/EXTENSION_POINTS.md:38`.

## Verification plan

1. **Unit** (`EtlCsvRouteWriterTests`):
   - every FR-005 quoting and formatting case, including empty, comma, quote, CR, LF, edge spaces, non-ASCII, the formula-like prefix, decimal scale, `long.MinValue`/`MaxValue`, booleans and null;
   - header quoting;
   - error rows;
   - (the byte-bound cases are in `EtlConnectorCsvOutputTests`, step 2);
   - a single-column null row writes an empty line;
   - parsing the output back through `CsvRecordReader` recovers the values.
2. **Store**:
   - The CSV effect is stored as mechanism 2.
   - Rows seeded with the prior writer's JSON Lines and HTTP values read back unchanged.
   - A CSV row is read back on replay.
   - Publication recording accepts the `.csv` key and refuses a `.jsonl` key for a CSV route.
   - A conflicting file at the key gives `etl.effect-conflict`.
   - The inspector state and the API effect view are correct.
3. **Design and identity**:
   - Compiler and reader accept and refuse correctly.
   - The artifact round-trips.
   - The golden hashes from spec 1190 still pass.
   - The JSON Lines output key is unchanged for the same effect id.
4. **Worker**:
   - CSV → CSV with a lookup and rejected rows; HTTP → CSV; JSON Lines → CSV.
   - Mixed CSV main with JSON Lines errors.
   - All published files are parsed and compared.
5. **Process recovery**: a kill at `BeforeAcceptance` with CSV routes. The replacement must hold a higher fence, publish no second file, and leave zero accounting.
6. **Studio**: Vitest for the route kind switch and picker; Playwright for authoring a CSV destination.
7. **Actual host**: a copy of the spec 1190 driver (left unchanged as that spec's evidence) runs a JSON Lines → CSV case (success and business error) that checks the published CSV files. A real browser observes the route type.
8. **Bite-proofs**:
   - quoting trigger removed;
   - header omitted;
   - CRLF changed to LF;
   - the effect-kind mapping reverted to raw kind, which hits the constraint;
   - the publication key extension.
9. **Whole suites**: the ETL test project with `--list-tests` comparison, the architecture guard, the maps check, Studio tests and the browser spec.
