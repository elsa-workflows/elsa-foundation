# Tasks: Finite JSON Lines ETL input

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md).

`[P]` means the task can run in parallel with the other `[P]` tasks in its phase (different files, no dependency). Each task names its files and the requirement it serves.

## Phase 1 — Model and design validation (US1)

- [x] T001 Add `EtlJsonlSource` and the `jsonl` discriminator; widen the `EtlPreparedCsvInput` summary; record the public API (`Extensions/Etl/src/Core/EtlPipelineV2.cs`, `PublicAPI.Unshipped.txt`). FR-001.
- [x] T002 Null-entry check for JSON Lines fields (`Extensions/Etl/src/Core/EtlArtifactV2Json.cs`). FR-001.
- [x] T003 [P] Compiler validation and string budget for the `jsonl` case (`Design/Elsa.Etl.Design/EtlArtifactCompiler.V2.cs`). FR-002.
- [x] T004 [P] Executable reader field count and `ValidateConnectors` case (`Runtime/Elsa.Etl.Runtime/Hosting/EtlConnectorExecutableReader.cs`). FR-002.
- [x] T005 [P] Transformer field mapping for JSON Lines sources (`Transformation/EtlRowTransformer.cs`). FR-007.
- [x] T006 Tests: artifact round trip and golden bytes and hashes for existing CSV and HTTP JSON artifacts (`EtlV2ArtifactIdentityTests`); compiler valid/invalid JSON Lines drafts; executable reader acceptance and refusals (`EtlArtifactV2JsonTests`, `EtlArtifactCompilerV2Tests`, `EtlConnectorExecutableReaderTests`). FR-001, FR-002, SC-004.

## Phase 2 — Bindings and persistence (US1, US4)

- [x] T007 Resolve a JSON Lines source through Source file approvals pinned as `Jsonl` (`Bindings/HostConnectorBindings.cs`). FR-003.
- [x] T008 Codec: `jsonl` source case without shards; role-based digest rule; file-source prepared validation with per-format index bound (`Persistence/…/Stores/EtlConnectorRecordCodec.cs`). FR-003, FR-008.
- [x] T009 Zero-record preparation settles success in the commit transaction (`Persistence/…/Stores/EfEtlConnectorRuntimeStore.cs`). FR-011.
- [x] T010 Runtime API reports `SourceInput` for any file source (`Runtime/Api/EtlRuntimeOperations.Connectors.cs`). FR-010.
- [x] T011 Tests (`HostConnectorBindingsTests`, `EtlConnectorRuntimeStoreTests`, `EtlConnectorRuntimeApiHttpTests`), covering FR-003, FR-008, FR-010 and FR-011:
  - Binding resolution, with the catalog unchanged; the same alias pinned as `csv` by a CSV pipeline and as `jsonl` by a JSON Lines one.
  - The codec accepts JSON Lines prepared runs and rejects bad digests, shards and index bounds.
  - A zero-record commit settles CSV and JSON Lines with exactly one terminal row and one outbox row, also after a replayed commit. This replaces the manual-settlement test, with its reason recorded.
  - The run detail reports the `jsonl` kind, the source input and the record count.

## Phase 3 — Preparation and batch building (US1, US2, US3)

- [x] T012 Add the `JsonlSnapshot` chunk format and store (`Output/IFileChunkPublisher.cs`, `Output/FileChunkPublisherFactory.cs`). R11.
- [x] T013 Streaming line indexer (`Preparation/EtlJsonlLineIndexer.cs`). FR-004, FR-005.
- [x] T014 Preparer accepts JSON Lines: kind and pin checks, the shard refusal, the field-count policy, the snapshot publisher, the zero-byte case, the `.jsonl` reopen, the index identity kind, the indexer, and `etl.source-too-many-records` through an injectable ceiling (`Preparation/EtlConnectorPreparer.cs`). FR-003, FR-004.
- [x] T015 Extract the shared JSON record reader from the page reader without behaviour change. It takes the field list and a 32-bit ordinal, has no size check, and raises a neutral duplicate-name failure that each caller maps (`Http/EtlJsonRecordReader.cs`, `Http/EtlJsonPageReader.cs`). FR-006.
- [x] T016 Batch builder for JSON Lines: file-source gates, sentinel check, kind-specific key extension and pin, framing re-verification, oversize classification, per-line parse with file ordinals, field names (`Execution/EtlConnectorInputBatchBuilder.cs`). FR-005, FR-006, FR-007.
- [x] T017 Tests (`EtlJsonlInputTests`, with its `Line_indexer_*`, `Preparation_*`, `Batch_*` and `Opening_*` cases, plus `EtlJsonPageReaderTests` unchanged), covering FR-004 to FR-007 and SC-002:
  - The indexer framing matrix and the ceiling refusal.
  - Preparer: unavailable file, digest mismatch, snapshot too large, empty and BOM-only files, shards refused.
  - Batch builder: every FR-006 code and edge case (exact and max+1 content, int64 and decimal bounds, object and array values, nested duplicates, CR-only files); two partitions; rebuild identity; tampered snapshot. More partitions than records is asserted at the store (`Jsonl_preparation_creates_file_ranges_and_bounds_its_sentinel_index`); the record ceiling at and over its bound in `Preparation_*`.
  - Forged-hash index refusals for each R10 check.
  - The host file changed after preparation is not read.
  - The HTTP page reader tests stay green unchanged.

## Phase 4 — Worker journeys and recovery (US2, US3)

- [x] T018 Test fixtures: JSON Lines source content in `RuntimeFiles`, a `jsonl` arm in `EtlRuntimeFixtures.ConnectorAdmission`, and a JSON Lines pipeline helper (`EtlRuntimeJourneyTests.cs`, `EtlRuntimeFixtures.cs`).
- [x] T019 Worker journeys: JSON Lines → JSON Lines and → HTTP with lookup and rejected lines (business error, counts); empty JSON Lines and header-only CSV settle as success; cancellation with owed work (`EtlConnectorWorkerJourneyTests.cs` or a new `EtlRuntimeJourneyTests.Jsonl.cs`). SC-001, SC-003, SC-004.
- [x] T020 Child-process recovery at `AfterReservation` and `BeforeAcceptance` on two partitions (`EtlRuntimeJourneyTests.Jsonl.cs`; `WorkerHost` changed only to map the new snapshot format). SC-003.
  - Start the replacement after the predecessor's partition lease expires.
  - Assert a higher fence, identical batch and effect identity and file bytes, and zero outstanding accounting.

## Phase 5 — Studio (US1, US4)

- [x] T021 [P] Types, source-kind option, binding filter, fields editor reuse, labels (`etlTypes.ts`, `EtlConnectorSourceEditor.tsx`, `EtlJsonFieldsEditor.tsx` (renamed from `EtlHttpJsonSourceFieldsEditor.tsx`), `PipelineBindingSelect.tsx`, the pipeline editor and budgets labels). FR-009, FR-010.
- [x] T022 [P] Tests, written first against T021's intended behaviour. SC-005.
  - Vitest for the kind switch, the binding filter and label (`__tests__/connector-pipeline-editor.test.tsx`). Run inspection needed no change: its `JSON Lines` kind label already existed.
  - Extend `Studio/tests/browser/etl-connector-authoring.spec.ts` with a JSON Lines source; its existing `csv` Source approval serves as the source file.

## Phase 6 — Real host, review and evidence

- [x] T023 Actual Workbench driver: build, free port, fixture approvals, publish via the design API, native workflow runs to success and to business error, output and inspection checks, own-process cleanup (`specs/1190-etl-jsonl-input/evidence/jsonl_workbench_journey.py`). SC-006.
- [x] T024 Real-browser observation of JSON Lines authoring and run inspection against the running host; screenshots and limits in `evidence/`. SC-005.
- [x] T025 Bite-proofs for framing, line-error routing and zero-record settlement, recorded in `evidence/`. SC-002, SC-004.
- [x] T026 Whole ETL test project with `--list-tests` count comparison; architecture guard; `Elsa.Maps.Generator -- check`; Studio ETL client tests. SC-004.
- [x] T027 Docs: `Extensions/Etl/README.md`, `src/Core/README.md` and `studio/Elsa.Studio.Etl/EXTENSION_POINTS.md` source-kind lists. FR-001, §2.22.1.
- [x] T028 Review loop (correctness, standards, spec lenses with adversarial verification): rounds 1–3 stopped at their cap unconverged; round 4 re-reviewed the round-3 fixes, found no code findings and four documentation inaccuracies, fixed through #3668 because #3657 was in auto-merge; gate-evidence PR comment; set spec `**Status**: Implemented` in this PR. Marking the PR ready, requesting review and the #2908/#2892/Project #62 status updates follow outside this diff.

## Dependencies

- Phase 1 comes first: everything compiles against `EtlJsonlSource`.
- T008 precedes T009.
- T012 to T015 precede T016.
- Phases 2 and 3 precede Phase 4.
- Phase 5 depends only on T001's wire shape.
- Phase 6 follows everything else.
