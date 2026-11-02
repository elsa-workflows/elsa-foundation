# Tasks: Finite CSV ETL delivery

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md).

## Phase 1 — Model, identity and validation

- [ ] T001 `EtlCsvDestination` with the `csv` discriminator, and the public API entries (`Core/EtlPipelineV2.cs`, `Core/PublicAPI.Unshipped.txt`). FR-001.
- [ ] T002 Kind-aware `ConnectorFileOutputKey` overload; the JSON Lines result is unchanged (`Core/Runtime/EtlRuntimeIdentity.cs`). FR-008, FR-009.
- [ ] T003 Compiler and executable-reader `csv` destination cases (`Design/Elsa.Etl.Design/EtlArtifactCompiler.V2.cs`, `Runtime/.../Hosting/EtlConnectorExecutableReader.cs`). FR-002.
- [ ] T004 Tests:
  - artifact round trip;
  - compiler acceptance and refusals;
  - reader acceptance;
  - the JSON Lines output key unchanged for a fixed effect id, plus a distinct `.csv` key;
  - golden hashes unchanged.

  (`EtlArtifactV2JsonTests`, `EtlArtifactCompilerV2Tests`, `EtlConnectorExecutableReaderTests`, `EtlRuntimeIdentityTests`.) FR-001, FR-002, FR-009.

## Phase 2 — CSV body writer

- [ ] T005 `EtlCsvRouteWriter`: header, record rows from the transformer's JSON objects, error rows, quoting and formatting, and a bound on every write (`Runtime/.../Output/EtlCsvRouteWriter.cs`). FR-004 to FR-007.
- [ ] T006 Writer tests:
  - the formatting matrix;
  - header quoting;
  - error rows;
  - the byte bound at the limit and one over;
  - read-back through `CsvRecordReader` with typed parsing.

  (`EtlCsvRouteWriterTests`.) SC-002.

## Phase 3 — Output path, bindings and persistence

- [ ] T007 `CsvOutput` chunk format and store (`Output/IFileChunkPublisher.cs`, `Output/FileChunkPublisherFactory.cs`, `EtlConnectorOutputProcessorTests` factory, `tests/WorkerHost/StagingPausePublisherFactory.cs`). R7.
- [ ] T008 Output processor:
  - the route body builder by kind;
  - the CSV row bound and the memory-only transformer bound (research R2);
  - the kind-aware verification store (research R7a trap);
  - CSV body store and identity;
  - CSV dispatch to file publication, with the kind's publisher, verification store and key.

  (`Execution/EtlConnectorOutputProcessor.cs`.) FR-004, FR-008.
- [ ] T009 Binding resolution for a CSV destination (`Bindings/HostConnectorBindings.cs`). FR-003.
- [ ] T010 Persistence:
  - the codec destination case;
  - one mechanism-mapping function for the output store's write and its replay comparison, plus the kind whitelist;
  - acceptance-store publication recording and delivered checks for both file kinds;
  - the inspector effect state;
  - a DbContext comment.

  (`EtlConnectorRecordCodec.cs`, `EfEtlConnectorOutputStore.cs`, `EfEtlConnectorAcceptanceStore.cs`, `EfEtlConnectorRuntimeInspector.cs`, `EtlRuntimeDbContext.cs`.) FR-008, FR-009, FR-010.
- [ ] T011 Runtime API effect view accepts CSV (`Runtime/Api/EtlRuntimeOperations.Connectors.cs`). FR-010.
- [ ] T012 Tests (`EtlConnectorOutputStoreTests`, `EtlConnectorAcceptanceStoreTests`, `HostConnectorBindingsTests`, `EtlConnectorRuntimeApiHttpTests`):
  - mechanism value 2 for a CSV effect;
  - prior-writer rows unchanged;
  - publication key checks;
  - binding pin;
  - API effect view.

  FR-003, FR-008 to FR-010.

## Phase 4 — Worker journeys and recovery

- [ ] T013 Worker journeys: CSV → CSV, HTTP → CSV, JSON Lines → CSV, and mixed routes, with every published file parsed (`EtlRuntimeJourneyTests.Csv.cs`). SC-001.
- [ ] T014 Child-process recovery with CSV routes at `BeforeAcceptance`: higher fence, no second file, zero accounting (`EtlRuntimeJourneyTests.Csv.cs`). SC-003.

## Phase 5 — Studio

- [ ] T015 Destination type, route option, file picker label, stage text, profile text and the inspection output label (`etlTypes.ts`, `components/EtlConnectorPipelineEditor.tsx`, `authoringProfiles.tsx`, `EtlRunInspection.tsx`). FR-010.
- [ ] T016 Vitest (`__tests__/connector-pipeline-editor.test.tsx`) and Playwright (`Studio/tests/browser/etl-connector-authoring.spec.ts`). SC-005.

## Phase 6 — Real host, docs, review

- [ ] T017 Extend the spec 1190 Workbench driver with CSV routes, run it, and keep the summary (`specs/1191-etl-csv-delivery/evidence/`). SC-006.
- [ ] T018 Real-browser observation of a CSV route and its inspection. SC-005.
- [ ] T019 Bite-proofs. SC-002, SC-004.
- [ ] T020 Docs: `Extensions/Etl/README.md`, `src/Core/README.md` and the Design and Studio `EXTENSION_POINTS.md` destination lists. FR-011.
- [ ] T021 Whole suites, then the review loop to convergence or its cap. Then the gate-evidence comment and spec `**Status**: Implemented` in this PR.
