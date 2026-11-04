# Tasks: JSON data integrity baseline

**Input**: Design documents from `specs/1207-json-data-integrity-baseline/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/json-baseline.md, quickstart.md

**Provisional**: do not start any task before this spec is approved and #3052's specs are defined. That means its
ADR PRs are agreed (see the spec's *Approval*). T001 is the gate.

**Tests**: framework §2.23 requires them for every logic-bearing change, and SC-001 to SC-006 require them too. The
application declares no TDD cadence, so within each story tests follow the implementation. One exception is written
first: T005, the guard's negative tests. The PR's bite-proof
shows each new test failing without the change.

**Build rule**: build and test only the projects a task touches (shared machine); CI runs the rest.

**Paths**: under `Essentials/src` unless another root is given.

## Phase 1: Setup

- [ ] T001 Confirm that #3408 and #3422 *Dates* are agreed (Q8). Diff their agreed text against
  spec.md, and amend spec, plan and these tasks for any change before starting
- [ ] T002 Confirm that the shared-baseline pattern passed the framework §2.24.3 gate with ADR 0091 and is listed in
  §2.24.2 (`.specify/memory/constitution-framework.md`). If not, stop and raise it
- [ ] T003 Record the current hashes as a baseline for SC-004 and US4 scenario 3: every ADR 0038 artifact hash in the
  workflow fixtures (`Essentials/tests/Workflows/Fixtures/legacy-set-v1.json`, `WorkflowExecutableCompilerGoldenTests`)
  and every `ActivityContract.SchemaFingerprint` in them

## Phase 2: Foundational (blocks every story)

- [ ] T004 Add `JsonBaseline` (read-only `Options`, `CreateCopy()`, `DataBoundary`) and `RawJsonElementConverter` in
  `Serialization/Core/` (data-model.md). Unit tests: read-only, no key policy, nulls kept, exact reads, relaxed
  encoder, a `JsonElement` written byte-identical, an indented writer leaving a value's inner whitespace alone
- [ ] T005 Add `JsonBaselineArchitectureTests` in `Essentials/tests/Architecture/Tests/` with a source scan of every
  source root (Essentials, Extensions, Studio C#; Q2) for:
  - `new JsonSerializerOptions`;
  - `JsonSerializerOptions.Web` and `.Default`;
  - `[JsonSourceGenerationOptions]` settings;
  - protected settings;
  - `JsonSerializer` calls without options or type info.

  It reads the register table in `docs/serialization.md` and `Baselines/json-optionless-calls.txt`. Write its
  negative tests first: an unregistered deviation of each kind fails, and a register entry passes
- [ ] T006 Add the reflection half of T005: every `MapEndpointGroup` context, every module's constructed contexts and
  the composed host HTTP options (`Essentials/apps/Elsa.Foundation.Host`, Workbench) checked against the baseline,
  and host resolvers checked for describing owned types only, with no overlap
- [ ] T007 Add the "Baseline deviations" register to `docs/serialization.md`, with the ADR 0091 decision 7 rows and
  every hash preimage from research R-1 (Q4). Generate `Baselines/json-optionless-calls.txt` from today's option-less
  calls, excluding the value-path sites of research R-10 (Q3)
- [ ] T008 Add `CanonicalJson` (canonical preimage and equality: sorted members, fixed encoder, raw number text) in
  `Serialization/Core/`, with unit tests that equal values with different escaping and whitespace are equal and hash
  equal

## Phase 3: User Story 1 - Names keep their casing (P1) 🎯 MVP

- [ ] T009 [P] [US1] Remove `DictionaryKeyPolicy` from:
  - `Workflows/Runtime/Api/WorkflowsRuntimeJsonContext.cs`;
  - `Workflows/Publishing/Api/WorkflowsPublishingJsonContext.cs`;
  - `Activities/Design/Api/ActivitiesDesignJsonContext.cs`;
  - `Connections/Api/{ConnectionsJsonContext,ConnectionsApi}.cs`;
  - `Secrets/Api/{SecretsJsonContext,SecretsApi}.cs`;
  - `Workflows/Design/Api/Services/AuthoringSchemaExporter.cs`.

  Construct each from `JsonBaseline.CreateCopy()` (FR-002, FR-006)
- [ ] T010 [US1] Replace the `Configure` calls at `Workflows/Publishing/Api/WorkflowsPublishingApiFeature.cs` L91 and
  `Activities/Design/Api/ActivitiesDesignApiFeature.cs` L146 with resolver-only insertion, as
  `WorkflowsRuntimeApiFeature.cs` L77–81 does (FR-017)
- [ ] T011 [US1] Invert the camelCase contract tests and rename them `…dictionary_keys_are_verbatim`:
  - `Essentials/tests/Activities/Design/Tests/Api/ActivitiesDesignSerializationContractTests.cs` L277–302;
  - `Essentials/tests/Workflows/Publishing/Api/Tests/PublishingSerializationContractTests.cs` L236–255.

  Drop the policy-pinning assertions (research R-4) and update `CompatibilityJson.cs` and
  `AddDefinitionDeserializationTests.cs`
- [ ] T012 [US1] Add Runtime API tests for US1 scenarios 1, 2 and 4: an output `OrderTotal` and a parameter `Amount`
  keep their keys; a host composing Publishing and Activities Design writes the Studio preference response unchanged
  (research R-6)

## Phase 4: User Story 3 - A .NET-typed value survives (P1)

- [ ] T013 [US3] Keep #3801's `ActivityResultRoundTripTests` and `ActivityValueJsonTests` green through the move to
  the data-boundary copy. Add a case for a key that differs only in case: it now gives `json-unmatched-member`, where
  `ActivityValueJson.Options` filled the member (research R-10)
- [ ] T014 [US3] Add `UnmatchedMemberScanner` in `Serialization/Core/` (Q1, research R-13) emitting
  `json-unmatched-member` (contract). Unit tests: top-level and nested keys, arrays of objects, annotated names
- [ ] T015 [US3] Add the UTC `DateTime` converter to `JsonBaseline.DataBoundary` (FR-016, ADR 0046 *Dates*, Q8)
- [ ] T016 [US3] Move every value-path conversion of research R-10 to `JsonBaseline.DataBoundary`, calling the
  scanner on reads:
  - `ActivityValueJson.Options` (used by `ActivityCompletionProjector.cs` L61 and `ActivityInputHydrator.cs` L109):
    replace it with the data-boundary copy, and delete it;
  - `RuntimeActivityInputMaterializer.cs` L316, L679, L702;
  - `RuntimeInputBindingCompiler.cs` L719, L788–806, L1019, L1042;
  - `WorkflowExecutableInputValidator.cs` L119 (since #3731 the admission check of every start);
  - `WorkflowExecutionStartDispatch.cs` L713;
  - `StatefulActivity.cs` L87, L89, L214, L399, L415;
  - `BackgroundActivityCompletionCodec.cs`;
  - `RuntimeDurableValueStorageDrivers.cs`;
  - `GraphActivity.cs`;
  - `WorkflowBuilder.cs` L105, L334;
  - `DescriptorPayloadSerializer.cs`;
  - `ObjectConverter.cs`;
  - `RuntimeWorkflowStateSeed.cs`, `ActivitySecretInputResolver.cs`, `ParentResumeExecutor.cs`.

  Replace `Convert.ChangeType` at `RuntimeInputBindingCompiler.cs` L1044 with a data-boundary read
- [ ] T017 [US3] Add tests for US3 scenarios 1–6. Scenario 4 (numbers through ForEach) is a regression test for #3424.
  Scenario 5 (dates) runs under a non-UTC `TimeZoneInfo` at run time and in a publish-time literal

## Phase 5: User Story 2 - Data comes back as sent (P1)

- [ ] T018 [US2] Derive `JsonPayloadSerializer` (`Serialization/SystemText/Services/JsonPayloadSerializer.cs`
  L103–137) from the baseline, keeping `DeterministicDictionaryConverter` and `SortObjectMembers` as register entries
- [ ] T019 [US2] Derive `EfDesignSupport.Json` / `ReadJson`
  (`Workflows/Design/Persistence/EntityFrameworkCore/Stores/EfDesignSupport.cs` L198–199) from the baseline, keeping
  `FingerprintJson` as a registered preimage
- [ ] T020 [US2] Derive `RuntimeArtifactJson`, `EfBookmarkStateStore.CreateJsonOptions` and the five other runtime
  store option sets (research R-9) from the baseline, registering `JsonStringEnumConverter` and
  `LosslessUtf16StringConverter` (FR-023)
- [ ] T021 [US2] Add the runtime store's first byte-level golden fixture. A stored data value with escapes, `1.0` and
  inner whitespace keeps its text through write and read on every provider. Document it in `docs/serialization.md`
  next to the Distributed gate, together with the 4.0-on rule of FR-023 (Q7)
- [ ] T022 [US2] Add the round-trip tests of US2 scenarios 1, 2, 4 and 5, plus scenario 3 (suspend, restart, resume)
  on SQLite and PostgreSQL

## Phase 6: User Story 4 - Content hashes stay stable (P2)

- [ ] T023 [US4] Make every R-1 hash preimage a register entry with its frozen options, and add a guard assertion
  that none derives from the baseline (FR-021, Q4)
- [ ] T024 [US4] Move the direct text hashes and `Ordinal` comparisons of research R-1 to `CanonicalJson`, or
  register them with a reason:
  - `EfWorkflowTriggerBindingStore.cs` L44, L68;
  - `EfDesignAtomicWrite.cs` L257;
  - `ActivitiesDesignDbContext.cs` L548;
  - `EfClusterMembershipStore.cs` L182;
  - `Extensions/Etl/src/Core/EtlArtifactJson.cs` L38–39 and its `GetRawText` comparisons;
  - `EfEtlRuntimeStore.cs`;
  - `EfRuleRunStore.Delivery.cs` L183;
  - `Elsa3ImportRecordCodec.cs` L199;
  - `ActivityForkMaterialFingerprint.cs`;
  - `AuthoringSchemaExporter.ComputeFingerprint`.
- [ ] T025 [US4] Git: the exporter (`GitWorkflowExporter.cs` L153) indents with the baseline writer, and
  `GitCanonicalJson.HashFile` uses `CanonicalJson`. Replace the invariant in `GitCanonicalJsonTests.cs` L14–24 with a
  round trip (write files, read them back, compare hashes) that also asserts the data text in the file (FR-022, Q5)
- [ ] T026 [US4] Compare every hash recorded in T003 after the change (SC-004, US4 scenario 3)

## Phase 7: User Story 5 - Explicit nulls are kept (P2)

- [ ] T027 [US5] Remove `WhenWritingNull` from `JsonPayloadSerializer` (part of T018). Add tests: an explicit null
  member is written; an old row without the member reads the same. Coordinate with S-2's T-tasks on the
  `ValueEnvelope` representation (FR-018)

## Phase 8: User Story 6 - One output format, safe to serve (P2)

- [ ] T028 [US6] Add `X-Content-Type-Options: nosniff` to every Elsa API group, in one place in the NativeEndpoints
  composition. Register `HttpResponseInstructionDelivery` as author-owned (FR-010, Q6)
- [ ] T029 [US6] Add tests for US6 scenarios 1 and 2: the same changed value has identical text from Runtime, Design
  and Publishing; every data-carrying Elsa API response has both headers

## Phase 9: User Story 7 - Deviations are visible (P3)

- [ ] T030 [US7] Run the guard over the migrated tree. For every remaining construction, either derive it from the
  baseline or add a register row (the R-10 counts are the checklist). Option-less calls not on the value path stay
  in the ratchet
- [ ] T031 [US7] Add tests for US7 scenario 3: a resolver describing a type its module does not own, and two
  overlapping resolvers

## Phase 10: User Story 8 - Converted Elsa 3 literals keep binding (P3)

- [ ] T032 [US8] Rewrite case-only key differences in literal lowering in `Extensions/Elsa3/src/Mapping/`, refusing
  ambiguous ones with `ELS3-LITERAL-KEY-AMBIGUOUS` and reporting rewrites with `ELS3-LITERAL-KEY-REWRITTEN`. Align
  the codes with `Elsa3ActivityDiagnosticCodes.cs`
- [ ] T033 [US8] Add converter tests for US8 scenarios 1 and 2, and re-run the #3052 JTest measurement (no definition
  may stop converting)

## Phase 11: Polish

- [ ] T034 [P] Update `docs/serialization.md`: the baseline and the data rule; "Why it matters" (no longer
  case-insensitive); the "HTTP boundary" exception without FastEndpoints (FR-024)
- [ ] T035 [P] Delete `Essentials/apps/Elsa.Workbench/Constants/JsonSerializerOptionsCache.cs` (unused, research R-11)
- [ ] T036 [P] Add *baseline* and *baseline deviation* to the glossary, and update `EXTENSION_POINTS.md` if the
  baseline is listed as a contract
- [ ] T037 Run quickstart.md by hand and record the result in the PR

## Dependencies

- **T001 and T002 gate everything.** T004 to T008 block every story.
- **Stories**:
  - US1 needs only T004.
  - US3 needs T004 and T014.
  - US2 needs T004 and T008.
  - US4 needs T008, and must land in the same change as T018 and T025 (FR-022).
  - US5 is part of T018.
  - US6 is independent after T004.
  - US7 needs every earlier story.
  - US8 is independent.
- **Within US3**: T013 runs with T016.
- **Parallel**: T009 with T014 and T032; T034 to T036 together.

## Implementation strategy

1. **MVP**: US1 (renames gone, host options owned by the host). It is ADR 0091's "first step" and stands alone.
2. Then US3 and US2 together, since they share the data-boundary copy, with US4 in the same change as T018.
3. Then US5, US6, US7 and US8.
