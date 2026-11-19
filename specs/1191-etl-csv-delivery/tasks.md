# Tasks: Finite CSV ETL delivery

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md).

## Phase 1 — Model, identity and validation

- [x] T001 `EtlCsvDestination` with the `csv` discriminator, and the public API entries (`Core/EtlPipelineV2.cs`, `Core/PublicAPI.Unshipped.txt`). FR-001.
- [x] T002 Kind-aware `ConnectorFileOutputKey` overload; the JSON Lines result is unchanged (`Core/Runtime/EtlRuntimeIdentity.cs`). FR-008, FR-009.
- [x] T003 Compiler and executable-reader `csv` destination cases (`Design/Elsa.Etl.Design/EtlArtifactCompiler.V2.cs`, `Runtime/.../Hosting/EtlConnectorExecutableReader.cs`). FR-002.
- [x] T004 Tests:
  - artifact round trip;
  - compiler acceptance and refusals;
  - reader acceptance;
  - the JSON Lines output key unchanged for a fixed effect id, plus a distinct `.csv` key;
  - golden hashes unchanged.

  (`EtlArtifactCompilerV2Tests.Csv_destinations_compile_on_either_route_and_validate_their_alias` for compile, round trip and refusal, `EtlConnectorExecutableReaderTests.Reader_accepts_csv_destinations`, `EtlRuntimeIdentityTests.Connector_file_keys_keep_the_jsonl_key_and_add_a_csv_extension`, and the unchanged `EtlV2ArtifactIdentityTests` golden hashes; the kind-aware key overload throws for HTTP.) FR-001, FR-002, FR-009, SC-004.

## Phase 2 — CSV body writer

- [x] T005 `EtlCsvRouteWriter`: header, record rows from the transformer's JSON objects, error rows, quoting and formatting. Row size is bounded by the caller (T008) (`Runtime/.../Output/EtlCsvRouteWriter.cs`). FR-004 to FR-007.
- [x] T006 Writer tests:
  - the formatting matrix;
  - header quoting;
  - error rows;
  - read-back through `CsvRecordReader` with typed parsing.

  (`EtlCsvRouteWriterTests`.) SC-002.

## Phase 3 — Output path, bindings and persistence

- [x] T007 `CsvOutput` chunk format and store (`Output/IFileChunkPublisher.cs`, `Output/FileChunkPublisherFactory.cs`, the `EtlConnectorOutputProcessorTests` factory, and `tests/WorkerHost/StagingPausePublisherFactory.cs` so the pause gate maps the new format). R7.
- [x] T008 Output processor:
  - the route body builder by kind;
  - the CSV row bound and the memory-only transformer bound (research R2);
  - the kind-aware verification store (research R7a trap);
  - CSV body store and identity;
  - CSV dispatch to file publication, with the kind's publisher, verification store and key.

  (`Execution/EtlConnectorOutputProcessor.cs`.) FR-004, FR-008.
- [x] T008a Output processor tests (`EtlConnectorCsvOutputTests.cs`, a partial of `EtlRuntimeStoreTests`), including an HTTP main whose over-limit first row is rejected rather than failing the batch:
  - a CSV row at the limit and one byte over, header counted;
  - the header absent when the first row is rejected, and carried by the first row that fits;
  - a size rejection whose CSV error row cannot fit fails the batch;
  - a row whose JSON exceeds the allowance but whose CSV fits is accepted;
  - a mixed CSV main and HTTP errors at the limit (the `]` reservation);
  - a tampered retained CSV body refused before publication (`etl.output-read-conflict`);
  - a conflicting file at the key refused (`etl.effect-conflict`).

  FR-004 to FR-008.
- [x] T009 Binding resolution for a CSV destination (`Bindings/HostConnectorBindings.cs`). FR-003.
- [x] T010 Persistence:
  - the codec destination case;
  - one mechanism-mapping function for the output store's write and its replay comparison, plus the kind whitelist;
  - acceptance-store publication recording and delivered checks for both file kinds;
  - the inspector effect state;
  - a DbContext comment.

  (`EtlConnectorRecordCodec.cs`, `EfEtlConnectorOutputStore.cs`, `EfEtlConnectorAcceptanceStore.cs`, `EfEtlConnectorRuntimeInspector.cs`, `EtlRuntimeDbContext.cs`.) FR-008, FR-009, FR-010.
- [x] T011 Runtime API effect view accepts CSV (`Runtime/Api/EtlRuntimeOperations.Connectors.cs`). FR-010.
- [x] T012 Tests (`EtlConnectorCsvOutputTests`, `HostConnectorBindingsTests`, `EtlConnectorRuntimeApiHttpTests` (its `csvErrors` case refuses a `.jsonl` key for a CSV route; its JSONL case rewrites the effect columns as the prior writer did and reads them back), `EtlConnectorRuntimeInspectorTests`):
  - mechanism value 2 for a CSV effect, on write and on replay read;
  - a CSV route stored as 1 throws `InvalidDataException`;
  - prior-writer rows unchanged;
  - inspector tests rewritten to the mechanism constants;
  - publication key checks;
  - binding pin;
  - API effect view.

  FR-003, FR-008 to FR-010.

## Phase 4 — Worker journeys and recovery

- [x] T013 Worker journeys: CSV → CSV, HTTP → CSV, JSON Lines → CSV, and mixed routes, with every published file parsed. They use `MaxBatchRecords = 1`, so every file carries its header. They check that no main file exists for an all-rejected batch and no error file for an all-accepted one. One output approval is shared by a JSON Lines pipeline and a CSV pipeline (`EtlRuntimeJourneyTests.Csv.cs`). SC-001.
- [x] T014 Child-process recovery with CSV routes at `BeforeAcceptance` (higher fence, no second file, zero accounting), plus a run whose existing conflicting file makes it fault (`EtlRuntimeJourneyTests.Csv.cs`). SC-003.

## Phase 5 — Studio

- [x] T015 Destination type, route option, file picker label, stage text, profile text and the inspection output label (`etlTypes.ts`, `components/EtlConnectorPipelineEditor.tsx`, `authoringProfiles.tsx`, `EtlRunInspection.tsx`). FR-010.
- [x] T016 Vitest (`__tests__/connector-pipeline-editor.test.tsx`, and `__tests__/runInspection.test.tsx` for the "CSV file" output type and a published CSV effect) and Playwright (`Studio/tests/browser/etl-connector-authoring.spec.ts`). SC-005.

## Phase 6 — Real host, docs, review

- [x] T017 Copy the spec 1190 Workbench driver to `specs/1191-etl-csv-delivery/evidence/` and give it CSV routes, run it, and keep the summary (`specs/1191-etl-csv-delivery/evidence/`). SC-006.
- [x] T018 Real-browser observation of a CSV route and its inspection. SC-005.
- [x] T019 Bite-proofs, including reverting the publication verification store to `_jsonlStore`. SC-002.
- [x] T020 Docs: `Extensions/Etl/README.md`, `src/Core/README.md`, the Design and Studio `EXTENSION_POINTS.md` destination lists, `src/Runtime/Api/EXTENSION_POINTS.md:38`, and the JSONL-only doc comments named in the plan. FR-011.
- [x] T021 Whole suites (SC-004), the review loop with its findings recorded in the gate-evidence comment, and spec `**Status**: Implemented` in this PR. Marking the PR ready, requesting review and the #2908, #2892 and Project #62 updates follow outside this diff.
