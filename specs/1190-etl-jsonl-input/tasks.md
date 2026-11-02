# Tasks: Finite JSON Lines ETL input

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md).

`[P]` means the task can run in parallel with the other `[P]` tasks in its phase (different files, no dependency). Each task names its files and the requirement it serves.

## Phase 1 — Model and design validation (US1)

- [ ] T001 Add `EtlJsonlSource` and the `jsonl` discriminator; widen the `EtlPreparedCsvInput` summary; record the public API (`Extensions/Etl/src/Core/EtlPipelineV2.cs`, `PublicAPI.Unshipped.txt`). FR-001.
- [ ] T002 Null-entry check for JSON Lines fields (`Extensions/Etl/src/Core/EtlArtifactV2Json.cs`). FR-001.
- [ ] T003 [P] Compiler validation and string budget for the `jsonl` case (`Design/Elsa.Etl.Design/EtlArtifactCompiler.V2.cs`). FR-002.
- [ ] T004 [P] Executable reader field count and `ValidateConnectors` case (`Runtime/Elsa.Etl.Runtime/Hosting/EtlConnectorExecutableReader.cs`). FR-002.
- [ ] T005 [P] Transformer field mapping for JSON Lines sources (`Transformation/EtlRowTransformer.cs`). FR-007.
- [ ] T006 Tests: artifact round trip and unchanged existing hashes; compiler valid/invalid JSON Lines drafts; executable reader acceptance and refusals (`EtlArtifactV2JsonTests`, `EtlArtifactCompilerV2Tests`, `EtlConnectorExecutableReaderTests`). FR-001, FR-002, SC-004.

## Phase 2 — Bindings and persistence (US1, US4)

- [ ] T007 Resolve a JSON Lines source through Source file approvals pinned as `Jsonl` (`Bindings/HostConnectorBindings.cs`). FR-003.
- [ ] T008 Codec: `jsonl` source case without shards; role-based digest rule; file-source prepared validation with per-format index bound (`Persistence/…/Stores/EtlConnectorRecordCodec.cs`). FR-003, FR-008.
- [ ] T009 Zero-record preparation settles success in the commit transaction (`Persistence/…/Stores/EfEtlConnectorRuntimeStore.cs`). FR-011.
- [ ] T010 Runtime API reports `SourceInput` for any file source (`Runtime/Api/EtlRuntimeOperations.Connectors.cs`). FR-010.
- [ ] T011 Tests (`HostConnectorBindingsTests`, `EtlConnectorRuntimeStoreTests`, `EtlConnectorRuntimeApiHttpTests`), covering FR-003, FR-008, FR-010 and FR-011:
  - Binding resolution, with the catalog unchanged; the same alias pinned as `csv` by a CSV pipeline and as `jsonl` by a JSON Lines one.
  - The codec accepts JSON Lines prepared runs and rejects bad digests, shards and index bounds.
  - A zero-record commit settles CSV and JSON Lines with exactly one terminal row and one outbox row, also after a replayed commit. This replaces the manual-settlement test, with its reason recorded.
  - The run detail reports the `jsonl` kind, the source input and the record count.

## Phase 3 — Preparation and batch building (US1, US2, US3)

- [ ] T012 Add the `JsonlSnapshot` chunk format and store (`Output/IFileChunkPublisher.cs`, `Output/FileChunkPublisherFactory.cs`). R11.
- [ ] T013 Streaming line indexer (`Preparation/EtlJsonlLineIndexer.cs`). FR-004, FR-005.
- [ ] T014 Preparer accepts JSON Lines: kind and pin checks, the shard refusal, the field-count policy, the snapshot publisher, the zero-byte case, the `.jsonl` reopen, the index identity kind, the indexer, and `etl.source-too-many-records` through an injectable ceiling (`Preparation/EtlConnectorPreparer.cs`). FR-003, FR-004.
- [ ] T015 Extract the shared JSON record reader from the page reader without behaviour change. It takes the field list and a 32-bit ordinal, has no size check, and raises a neutral duplicate-name failure that each caller maps (`Json/EtlJsonRecordReader.cs`, `Http/EtlJsonPageReader.cs`). FR-006.
- [ ] T016 Batch builder for JSON Lines: file-source gates, sentinel check, kind-specific key extension and pin, framing re-verification, oversize classification, per-line parse with file ordinals, field names (`Execution/EtlConnectorInputBatchBuilder.cs`). FR-005, FR-006, FR-007.
- [ ] T017 Tests (`EtlJsonlLineIndexerTests`, `EtlJsonlInputPreparationTests`, `EtlJsonlInputBatchBuilderTests`, `EtlJsonPageReaderTests`), covering FR-004 to FR-007 and SC-002:
  - The indexer framing matrix and the ceiling refusal.
  - Preparer: unavailable file, digest mismatch, snapshot too large, empty and BOM-only files, shards refused.
  - Batch builder: every FR-006 code and edge case; more partitions than records; two partitions; rebuild identity; tampered snapshot.
  - Forged-hash index refusals for each R10 check.
  - The host file changed after preparation is not read.
  - The HTTP page reader tests stay green unchanged.

## Phase 4 — Worker journeys and recovery (US2, US3)

- [ ] T018 Test fixtures: JSON Lines source content in `RuntimeFiles`, a `jsonl` arm in `EtlRuntimeFixtures.ConnectorAdmission`, and a JSON Lines pipeline helper (`EtlRuntimeJourneyTests.cs`, `EtlRuntimeFixtures.cs`).
- [ ] T019 Worker journeys: JSON Lines → JSON Lines and → HTTP with lookup and rejected lines (business error, counts); empty JSON Lines and header-only CSV settle as success; cancellation with owed work (`EtlConnectorWorkerJourneyTests.cs` or a new `EtlRuntimeJourneyTests.Jsonl.cs`). SC-001, SC-003, SC-004.
- [ ] T020 Child-process recovery at `AfterReservation` and `BeforeAcceptance` on two partitions (`EtlRuntimeJourneyTests.JsonlProcessRecovery.cs`; `WorkerHost` unchanged). SC-003.
  - Start the replacement after the predecessor's partition lease expires.
  - Assert a higher fence, identical batch and effect identity and file bytes, and zero outstanding accounting.

## Phase 5 — Studio (US1, US4)

- [ ] T021 [P] Types, source-kind option, binding filter, fields editor reuse, labels (`etlTypes.ts`, `EtlConnectorSourceEditor.tsx`, `EtlHttpJsonSourceFieldsEditor.tsx`, the pipeline editor and inspection text). FR-009, FR-010.
- [ ] T022 [P] Tests, written first against T021's intended behaviour. SC-005.
  - Vitest for the kind switch, the binding filter and label (`__tests__/connector-pipeline-editor.test.tsx`), and the inspection text (`__tests__/runInspection.test.tsx`).
  - Extend `Studio/tests/browser/etl-connector-authoring.spec.ts` and its approvals in `Studio/tests/browser/etlConnectorsFixture.tsx` with a JSON Lines source.

## Phase 6 — Real host, review and evidence

- [ ] T023 Actual Workbench driver: build, free port, fixture approvals, publish via the design API, native workflow runs to success and to business error, output and inspection checks, own-process cleanup (`specs/1190-etl-jsonl-input/evidence/jsonl_workbench_journey.py`). SC-006.
- [ ] T024 Real-browser observation of JSON Lines authoring and run inspection against the running host; screenshots and limits in `evidence/`. SC-005.
- [ ] T025 Bite-proofs for framing, line-error routing and zero-record settlement, recorded in `evidence/`. SC-002, SC-004.
- [ ] T026 Whole ETL test project with `--list-tests` count comparison; architecture guard; `Elsa.Maps.Generator -- check`; Studio ETL client tests. SC-004.
- [ ] T027 Docs: `Extensions/Etl/README.md`, `src/Core/README.md` and `studio/Elsa.Studio.Etl/EXTENSION_POINTS.md` source-kind lists. FR-001, §2.22.1.
- [ ] T028 Review loop to convergence (correctness, standards, spec lenses with adversarial verification); gate-evidence PR comment; set spec `**Status**: Implemented` in the PR; mark ready; request review; source-bound status on #2908, #2892 and Project #62.

## Dependencies

- Phase 1 comes first: everything compiles against `EtlJsonlSource`.
- T008 precedes T009.
- T012 to T015 precede T016.
- Phases 2 and 3 precede Phase 4.
- Phase 5 depends only on T001's wire shape.
- Phase 6 follows everything else.
