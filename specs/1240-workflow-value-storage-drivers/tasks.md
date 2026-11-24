# Tasks: Storage drivers for workflow variables, inputs and outputs (spec 1240)

Implementation starts after Joey approves the spec and decisions D-1 to D-3.

## Phase 1: Foundation

- [ ] T001 Add the shared test helper `RecordingExternalStorageDriver` under `essentials/tests` (Testing project).
- [ ] T002 `RuntimeVariableDeclaration.StorageDriverKey`, `WorkflowDeclaredInput.StorageDriverKey`, and the output
      declaration with its key (Runtime.Core / Publishing models).
- [ ] T003 `RuntimeVariableValueCodec` (encode/decode, `runtime.storageDriverKey`, `elsa.json` short-cut, refuse
      `Custom`) with unit tests.

## Phase 2: US1 + US3, variables, requirements and the publish check

- [ ] T004 `CompileVariableDeclaration`: driver key instead of storage profile, remove VF-ACT-005; remove the
      `IsTransient` mapping. Tests for root, container and body-container variables.
- [ ] T005 `StorageDriverRequirements` include variable, input and output drivers; `workflow.runtime.storage-driver-missing`
      publish check. Tests, including a placement-gate test.
- [ ] T006 Initial defaults encoded through the codec (`RuntimeVariableDeclarationProjector`).
- [ ] T007 `StepVariableCommit` encodes driver-stored intents; lift the session's external refusal for driver-stored
      variables; lost-update test.
- [ ] T008 Output captures into variables encode with the target variable's driver.
- [ ] T009 Reads decode: script fallback, member read, input binding variable reads, step session open (D-3 A).
- [ ] T010 Script assignments and intrinsic variable writes go through the codec.

## Phase 3: US2, inputs and outputs

- [ ] T011 Seeded start inputs encoded with their driver plus metadata, after validation. Tests.
- [ ] T012 Workflow-level `SetOutput` encodes with the output's driver. Tests.
- [ ] T013 Decoding output projection for instance details and child-to-parent completion. Tests.

## Phase 4: US4, driver-declared types

- [ ] T014 `StorableTypeAliases` on the driver contract; `Stores` on `IActivityContractStorageDriverProvider`; both
      capability catalogs use the one rule. Tests (no list, explicit list, non-durable type).
- [ ] T015 Update `EXTENSION_POINTS.md` and the generated extension-point map for the driver contract change.
- [ ] T016 Studio: storage column for outputs in `WorkflowPropertiesView.tsx`, with a vitest test.

## Phase 5: US5, converter (after PR #5696 merges into the initiative branch)

- [ ] T017 Merge the initiative branch in. Map Elsa 3 drivers to the type name in `Elsa3VariableMapper` and
      `Elsa3ArgumentDefinitionToInputOutput`.
- [ ] T018 Remove the three `*-STORAGE-DRIVER-UNMAPPED` codes and their tests. Update the migration skill inventory.
- [ ] T019 Import end to end: the spec example against a host that registers `Acme.BlobStorageDriver`, and against one
      that does not (publish refusal).

## Phase 6: Finish

- [ ] T020 Regenerate the affected fixtures and hashes. Run the gate checker and the affected test projects.
- [ ] T021 Set the spec status to Implemented in this PR. Post the evidence and mark the PR ready.
