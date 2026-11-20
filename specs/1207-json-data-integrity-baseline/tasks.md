# Tasks: JSON data integrity baseline

**Input**: Design documents from `specs/1207-json-data-integrity-baseline/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/json-baseline.md, quickstart.md

**Start**: once this spec is approved and merged, on a new branch cut from the initiative branch. ADR 0091 and the
ADR 0046 amendment are already merged there. T001 is the gate.

**Tests**: framework §2.23 requires them for every logic-bearing change, and SC-001 to SC-006 require them too. The
application declares no TDD cadence, so within each story tests follow the implementation. One exception is written
first: T005, the guard's negative tests. The PR's bite-proof
shows each new test failing without the change.

**Build rule**: build and test only the projects a task touches (shared machine); CI runs the rest.

**Paths**: under `Essentials/src` unless another root is given.

## Phase 1: Setup

- [ ] T001 Confirm the spec is merged with Q9 and Q10 answered, and diff the initiative branch's ADR 0091, ADR 0046
  *Dates* and spec 1223 FR-034 against spec.md. Message the parent session before editing the serializer baseline
  files S-2 (spec 1216) also touches, and merge the initiative branch in first if S-8 (#4838) or S-1 (#4855) has
  landed
- [ ] T002 Confirm that the shared-baseline pattern passed the framework §2.24.3 gate with ADR 0091 and is listed in
  §2.24.2 (`.specify/memory/constitution-framework.md`). If not, stop and raise it
- [ ] T003 List every hash or fingerprint whose input is JSON (research R-1 and R-14, then a search for every
  `SHA256`, `IncrementalHash` and `XxHash` call fed by serialized JSON) as the T023 checklist, with the fixtures and
  goldens that pin each one

## Phase 2: Foundational (blocks every story)

- [ ] T004 Add `JsonBaseline` (read-only `Options`, `CreateCopy()`) and `RawJsonElementConverter` in
  `Serialization/Core/` (data-model.md). Unit tests: read-only, no key policy, nulls kept, exact reads, relaxed
  encoder, a `JsonElement` written byte-identical, an indented writer leaving a value's inner whitespace alone
  (FR-001, FR-003, FR-007, FR-009)
- [ ] T005 Add `JsonBaselineArchitectureTests` in `Essentials/tests/Architecture/Tests/` with a source scan of every
  source root (Essentials, Extensions, Studio C#; Q2) for:
  - `new JsonSerializerOptions`;
  - `JsonSerializerOptions.Web` and `.Default`;
  - `[JsonSourceGenerationOptions]` settings;
  - protected settings;
  - `JsonSerializer` calls without options or type info.

  It reads the register table in `docs/serialization.md` and `Baselines/json-optionless-calls.txt`. Write its
  negative tests first (FR-020, FR-002a): an unregistered deviation of each kind fails, and a register entry passes;
  a new option-less call not on the legacy list fails, removing a migrated entry passes, and a workflow-data path on
  the list fails
- [ ] T006 Add the reflection half of T005: every `MapEndpointGroup` context, every module's constructed contexts and
  the composed host HTTP options (`Essentials/apps/Elsa.Foundation.Host`, Workbench) checked against the baseline,
  and host resolvers checked for describing owned types only, with no overlap. Add the FR-011 check: no member of an
  API, store or message contract that carries data is a dictionary, `ExpandoObject`, `object` or POCO; data members
  are `JsonElement` or `JsonNode`
- [ ] T007 Add the "Baseline deviations" register to `docs/serialization.md`, with the ADR 0091 decision 7 rows and
  one row for `JsonHashInput`'s option set, the only register entry for hashes (Q11, FR-019, FR-021). Generate `Baselines/json-optionless-calls.txt` from
  today's option-less calls, excluding the value-path sites of research R-10 (Q3, FR-002a)
- [ ] T008 Add `JsonCanonicalForm` in `Serialization/Core/` (FR-021a): move the canonical writer out of
  `Workflows/Runtime/Core/Models/VariableValueDigest.cs` and add `Equals(JsonElement, JsonElement)`. Unit tests with
  ADR 0091's examples: `{"total":1.0,"name":"Ren\u00e9"}` equals `{"name":"René","total":1}`; `9007199254740993` is
  not `9007199254740992`; `{}` is not `{"x":null}`; arrays in order (FR-021a)
- [ ] T008a Make `VariableValueDigest` hash through `JsonHashInput` (after T023) instead of its own writer and
  `SHA256` call; regenerate the digests its tests and fixtures pin, and assert the digest agrees with
  `JsonCanonicalForm.Equals` on every T008 pair. Assigning one T008 value over its equal partner writes nothing: use
  S-7's no-op write test (spec 1231 FR-012) if it exists on the branch, else add one
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
  the default conversion contract. Add a case for a key that differs only in case: it now gives `json-unmatched-member`, where
  `ActivityValueJson.Options` filled the member (research R-10; FR-012)
- [ ] T014 [US3] Add `UnmatchedMemberScanner` in `Serialization/Core/` (Q1, research R-13), contract-aware, emitting
  the FR-005 codes (contract). Unit tests: top-level and nested keys, arrays of objects, annotated names, a scoped
  case-insensitive contract, explicit mappings, dictionaries, extension data (not reported), a custom converter
  (coverage unavailable, no false warnings), a strict contract (refused), a missing required member (fails)
- [ ] T015 [US3] Add `JsonConversionContract` (default and scoped) and `JsonConversionAttribute` in
  `Serialization/Core/`. The default is camelCase, exact, warn; the attribute on a type or input member selects
  naming, matching and unmatched-member handling (FR-004, FR-004a); cache per type and member; nothing is stamped into
  the executable. Add `StrictDateTimeConverters` (also used by the baseline): `Z` or an offset required, else
  `json-datetime-offset-missing`; a `DateTime` normalized to UTC, a `DateTimeOffset` keeps its offset; `DateOnly` and
  `TimeOnly` as FR-016 states (FR-016, FR-016c, Q8, Q10)
- [ ] T015a [US3] FR-016c: test that a start request with an offset-less `DateTime` input, and an own-contract
  `DateTimeOffset` field without an offset, get 400 with `json-datetime-offset-missing` and the path (US3 scenario
  12), and that instants Elsa returns are UTC
- [ ] T016 [US3] Move every value-path conversion of research R-10 to the target type's conversion contract, calling
  the scanner on reads (FR-012 to FR-014a, FR-025 codes from the contract):
  - `ActivityValueJson.Options` (used by `ActivityCompletionProjector.cs` L61, `ActivityInputHydrator.cs` L109 and
    `StepVariableSession.cs` L266, L331): replace it with the default conversion contract, and delete it;
  - `RuntimeActivityInputMaterializer.cs` L466 (`SelectedMemberFitOptions`, from `ActivityValueJsonProfile.CreateOptions()`);
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

  Replace `Convert.ChangeType` at `RuntimeInputBindingCompiler.cs` L1044 with a conversion-contract read
- [ ] T017 [US3] Add tests for US3 scenarios 1–10. Scenario 4 (numbers through ForEach) is a regression test for #3424.
  Scenario 5 (dates) runs under a non-UTC `TimeZoneInfo` at run time and in a publish-time literal, with and without
  an offset (the offset-less value refused with `json-datetime-offset-missing`), plus the UTC normalization and
  `DateOnly` cases. Scenario 6 covers FR-014a (`Boolean`, `Int32`, GUID case, `TimeSpan`). Scenario 2 pins the
  camelCase default (`customerName` fills `CustomerName`)
- [ ] T017a [US3] Studio time zone setting (FR-016b, scenario 11): add a `timeZone` appearance preference
  (`"browser"` default, `"UTC"`, or an IANA ID) to `Elsa.Studio.Hosting/Client/src/app/themes/appearance.ts` and a
  picker to `settings/AppearancePage.tsx`; add one shared formatter to
  `Elsa.Studio.Workflows/Client/src/workflowFormatting.tsx` that shows a date in that zone with its offset
- [ ] T017b [US3] Move every Studio date display (`toLocaleString()` and similar on a date, about 30 files) to the
  shared formatter, so every date shows its offset
- [ ] T017c [US3] Run dialog: `workflowRunInputs.ts` turns `datetime-local` text into ISO 8601 with the setting's
  offset for that date, and `WorkflowRunInputDialog.tsx` shows the zone. Vitest: a winter and a summer Amsterdam date
  get `+01:00` and `+02:00`, and the UTC setting gives `+00:00`. Verify in the composed host per Studio UX acceptance,
  with screenshots in the PR

## Phase 5: User Story 2 - Data comes back as sent (P1)

- [ ] T018 [US2] Derive `JsonPayloadSerializer` (`Serialization/SystemText/Services/JsonPayloadSerializer.cs`
  L103–137) from the baseline, keeping `DeterministicDictionaryConverter` and `SortObjectMembers` as register entries
- [ ] T019 [US2] Derive `EfDesignSupport.Json` / `ReadJson`
  (`Workflows/Design/Persistence/EntityFrameworkCore/Stores/EfDesignSupport.cs`) from the baseline for data reads and
  writes; `Fingerprint` and `EfWorkflowLibraryStore.Fingerprint` move to `JsonHashInput` in T023, and
  `LegacyFingerprint` is deleted (FR-021)
- [ ] T019a [US2] Add the FR-024a test: every stored-format fixture and seed JSON read through `IPayloadSerializer`
  in the repository uses the camelCase names it writes (exact reads lose nothing)
- [ ] T020 [US2] Derive `RuntimeArtifactJson`, `EfBookmarkStateStore.CreateJsonOptions` and the five other runtime
  store option sets (research R-9) from the baseline, registering `JsonStringEnumConverter` and
  `LosslessUtf16StringConverter` (FR-023)
- [ ] T021 [US2] Add the runtime store's first byte-level golden fixture. A stored data value with escapes, `1.0` and
  inner whitespace keeps its text through write and read on every provider. Add fixed serialized examples: new code
  reads a row in today's format with the same meaning, and today's reader options read a new row with the same
  meaning; if they fail, regenerate the migrations and golden fixtures in place (no upcaster, no family-version bump,
  no run-time check). Document the rule of FR-023 in `docs/serialization.md` next to the Distributed gate (Q7)
- [ ] T022 [US2] Add the round-trip tests of US2 scenarios 1, 2, 4 and 5, plus scenario 3 (suspend, restart, resume)
  on SQLite and PostgreSQL

## Phase 6: User Story 4 - Content hashes stay stable (P2)

- [ ] T023 [US4] Add `JsonHashInput` in `Serialization/Core/` (explicit options, SHA-256 over `JsonCanonicalForm`) and
  move every hash on the T003 checklist to it, including `WorkflowExecutableHasher` and its context,
  `ActivityContract`'s fingerprint, `DefaultActivityDefinitionHasher`, `ExecutableActivityTemplateBehaviorHasher`,
  `EfDesignSupport.Fingerprint`, `EfWorkflowLibraryStore.Fingerprint`, `GitCanonicalJson`, `EtlArtifactJson`,
  `ActivityForkMaterialFingerprint`, `AuthoringSchemaExporter.ComputeFingerprint` and `VariableValueDigest` (T008a);
  delete `EfDesignSupport.LegacyFingerprint` and the V1 verification path of `WorkflowExecutableHasher.MatchesHash`
  (FR-021, Q11)
- [ ] T023a [US4] Add a golden test per hash (fixed input → pinned value), covering every variant the canonical form
  absorbs (escaping, whitespace, member order, number notation) and every one it keeps apart (missing versus `null`,
  array order, a different number). Each golden's failure message states the first-stable freeze rule of FR-021. Add
  the guard rule: any hash call whose input is serialized JSON and does not go through `JsonHashInput` fails the
  architecture test (US4 scenario 2)
- [ ] T024 [US4] Move every check that compares serialized text to decide equality (research R-1) to
  `JsonCanonicalForm.Equals` (FR-021):
  - `EfWorkflowTriggerBindingStore.cs` L44, L68;
  - `EfDesignAtomicWrite.cs` L257;
  - `ActivitiesDesignDbContext.cs` L548;
  - `EfClusterMembershipStore.cs` L182;
  - `Extensions/Etl/src/Core/EtlArtifactJson.cs` L38–39 and its `GetRawText` comparisons;
  - `EfEtlRuntimeStore.cs`;
  - `EfRuleRunStore.Delivery.cs` L183;
  - `Elsa3ImportRecordCodec.cs` L199.
- [ ] T025 [US4] Git: the exporter (`GitWorkflowExporter.cs` L153) indents with a writer that writes `JsonElement`
  raw; `GitCanonicalJson.HashFile` is `JsonHashInput` over the parsed file. Add a round trip (write files, read them
  back, compare hashes) that also asserts the data text in the file (FR-022, Q5)
- [ ] T026 [US4] Regenerate in place every fixture and golden whose hash moved (for example
  `Essentials/tests/Workflows/Fixtures/legacy-set-v1.json`, `WorkflowExecutableCompilerGoldenTests`, the response-replay
  manifests), list them in the PR, and test that equal inputs with other escaping, whitespace, member order or number
  notation hash the same (SC-004, US4 scenario 3)
- [ ] T026a [US4] FR-022a: test that an unchanged definition exported before and after differs only by kept null
  members (US4 scenario 5), and regenerate the Git fixtures with them

## Phase 7: User Story 5 - Explicit nulls are kept (P2)

- [ ] T027 [US5] Remove `WhenWritingNull` from `JsonPayloadSerializer` (part of T018). Add tests: an explicit null
  member is written; an old row without the member reads the same. Coordinate with S-2's T-tasks on the
  `ValueEnvelope` representation (FR-018)

## Phase 8: User Story 6 - One output format, safe to serve (P2)

- [ ] T028 [US6] Add `X-Content-Type-Options: nosniff` to every Elsa API group, in one place in the NativeEndpoints
  composition. `HttpResponseInstructionDelivery` uses the author's content type; with none, text → `text/plain`,
  anything else → `application/json` (FR-010, Q6)
- [ ] T029 [US6] Add tests for US6 scenarios 1–3: the same changed value has identical text from Runtime, Design
  and Publishing; every data-carrying Elsa API response has both headers; a workflow response keeps `text/html` when
  authored, and defaults by body when not

## Phase 9: User Story 7 - Deviations are visible (P3)

- [ ] T030 [US7] Run the guard over the migrated tree. For every remaining construction, either derive it from the
  baseline or add a register row (the R-10 counts are the checklist). Option-less calls on Elsa's own contracts
  stay on the legacy list; no workflow-data path does
- [ ] T031 [US7] Add tests for US7 scenario 3: a resolver describing a type its module does not own, and two
  overlapping resolvers

## Phase 10: User Story 8 - Converted Elsa 3 literals keep binding (P3)

- [ ] T032 [US8] FR-015: rewrite case-only key differences in literal lowering in `Extensions/Elsa3/src/Mapping/`, refusing
  ambiguous ones with `ELS3-LITERAL-KEY-AMBIGUOUS` and reporting rewrites with `ELS3-LITERAL-KEY-REWRITTEN`. Align
  the codes with `Elsa3ActivityDiagnosticCodes.cs`
- [ ] T032a [US8] Date literals (FR-016a) in `Extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs`: for a
  literal or variable default bound to a `DateTime`, `DateTimeOffset` or `DateOnly` member, read
  `Elsa3ConversionOptions.DateInterpretation` (S-1) and refuse (`ELS3-LITERAL-DATE-OFFSET-MISSING`), write the source
  zone's offset for that date, or write `Z` with a warning (`ELS3-LITERAL-DATE-INTERPRETED`); add
  `ELS3-LITERAL-DATE-WALL-CLOCK` for offset-carrying or non-literal values on `DateTime` and `DateOnly` members;
  record the interpretation on the converted workflow with S-1's `DateInterpretationApplied` diagnostic
- [ ] T033 [US8] Add converter tests for US8 scenarios 1 to 3 (scenario 3 under all three interpretations, with a
  winter and a summer date), and re-run the #3052 JTest measurement (no definition may stop converting)

## Phase 11: Polish

- [ ] T034 [P] Update `docs/serialization.md`: the baseline and the data rule; the canonical form; the default
  conversion contract and `[JsonConversion]`; "camelCase, case-insensitive on read" → exact reads; the "HTTP boundary"
  exception without FastEndpoints (FR-024); a *Hash inputs* section with `JsonHashInput` and the first-stable freeze
  rule (FR-021)
- [ ] T034a Add one line to `AGENTS.md` that routes to that rule: until Elsa 4's first stable release, stored formats
  and hashes may change with regenerated fixtures; from it on, they are frozen and change only by an explicit
  decision (FR-021)
- [ ] T035 [P] Delete `Essentials/apps/Elsa.Workbench/Constants/JsonSerializerOptionsCache.cs` (unused, research R-11)
- [ ] T036 [P] Add *baseline* and *baseline deviation* to the glossary, and update `EXTENSION_POINTS.md` if the
  baseline is listed as a contract
- [ ] T037 Run quickstart.md by hand and record the result in the PR
- [ ] T038 Set the spec's **Status** to Implemented in the implementation PR

## Dependencies

- **T001 and T002 gate everything.** T004 to T008 block every story.
- **Stories**:
  - US1 needs only T004.
  - US3 needs T004 and T014.
  - US2 needs T004 and T008.
  - US4 needs T008, and must land in the same change as T018 and T025 (FR-022, FR-022a).
  - US5 is part of T018.
  - US6 is independent after T004.
  - US7 needs every earlier story.
  - US8 is independent; T032a needs S-1's `Elsa3ConversionOptions` on the initiative branch (#4855).
  - T017c needs T015 (the refusal it avoids) and T017a. T023 needs T008; T008a needs T023.
- **Within US3**: T013 runs with T016.
- **Parallel**: T009 with T014 and T032; T034 to T036 together.

## Implementation strategy

1. **MVP**: US1 (renames gone, host options owned by the host). It is ADR 0091's "first step" and stands alone.
2. Then US3 and US2 together, since they share the conversion contract, with US4 in the same change as T018.
3. Then US5, US6, US7 and US8.
