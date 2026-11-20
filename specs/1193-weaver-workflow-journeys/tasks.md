# Tasks: Weaver workflow authoring and inspection journeys

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

Each slice is one PR that says "Refs #3442", and slice G's PR closes the issue. A slice starts only after the previous slice is merged and its resulting main is green. `[P]` marks tasks in a slice that touch different files and can run in parallel.

## Slice A: Read-path hardening (story 5)

- [x] T001 Add the Design fault renderer, which maps `WorkflowDefinitionAuthorizationException` to 503 with `workflow.authorization.unavailable`, and add the optional `errorCode` to `WorkflowDesignError` (FR-001, D1).
- [x] T002 Record built-in read evidence in `WorkflowDefinitionDetailsReader`, and accept it only in `HttpWorkflowDefinitionAuthorizer` for the same request, definition and tenant (FR-002).
- [x] T003 Map the exception to the unavailable outcome in `WorkflowResourceAuthority`. Keep the owner-unavailable code through `AgentToolInvoker` and context admission, with every other denial unchanged (FR-001, D1).
- [x] T004 [P] Tests: unavailable versus denied, no cause in the body, read counts, replacement reader cannot produce evidence, evidence for one definition does not satisfy another, manage still checked, tool outcome. Re-run the existing boundary tests unchanged.
- [x] T005 Update the Design Api catalog for the failure semantics (FR-034).
- [x] T006 Probe case `workflow-authorization-unavailable` on both adapters, and a normal-host Playwright journey for unavailable versus denied (FR-036, SC-006).

## Slice B: Truth seams (story 4)

- [x] T007 Remove the seven legacy contracts, implementations, registrations and models (D3, FR-006), and make `DefaultWorkflowAgentContextProvider` `internal sealed`.
- [x] T008 Add `IActivityAuthoringCatalog` to `Elsa.Activities.Design.Core`, implemented in the Api over `IActivityAuthoringCatalogReader` with a single-registration guard and a catalog entry. Consume it from an internal catalog reader with the Read gate. A missing contract is reported as unavailable (FR-003, D4, R1).
- [x] T009 Add the `workflow.catalog` and `workflow.permissions` attachments in `CollectAsync`, add them to `ContextKinds`, and re-admit them each turn. Gate tool availability on the permissions (FR-004, FR-031).
- [x] T010 [P] Composition test: no placeholder type in the composed host or the packaged consumer (SC-003).
- [x] T011 [P] Update the Agent Workflows catalog's "Legacy domain seams" section, with the SemVer notes (FR-033, FR-034).
- [x] T012 Probe case `workflow-truth-seams`, and a normal-host journey for a read-only actor (FR-036).

## Slice C: Draft authoring (stories 2, 3)

- [x] T013 Server tool `workflow_apply_operation_batch` v1: strict schemas, catalog, declared-input, credential and ambiguity checks, per-operation risk, and `RequiresApproval` in every mode. Refuse stale, published and historical targets (FR-007, FR-012, FR-013, FR-015, FR-017).
- [x] T014 Studio applier: strict schemas and no default property name (FR-007).
- [x] T015 SHA-256 content revision, and compare-and-replace on definition, revision, binding and selection (FR-008, R2).
- [x] T016 History flush, one history entry per batch, and a review-card Undo that uses designer history (FR-009, R3).
- [x] T017 Autosave hold keyed to the history position, held again on Redo (FR-010).
- [x] T018 Scope frame, selection, test-run result and published-artifact handling, and a receipt before the binding rotates. Outcomes distinguish proposed, approved, applied locally, persisted, refused and uncertain, and Weaver never saves (FR-008, FR-011).
- [x] T019 Studio risk recheck that fails closed (FR-012).
- [x] T020 `workflows.batch-result` renderer (Weaver Workflows).
- [x] T021 [P] Server and Vitest tests: the armed capture timer, a hand edit inside the capture delay, autosave across Undo and Redo, the 50-entry history cap, and the NIST vectors for the hash (SC-002, SC-004).
- [x] T022 Probe case `workflow-batch` and its negative controls, and a Playwright journey.

## Slice D: Explain and troubleshoot (story 1)

- [x] T023 Property, expression and credential context in the active-draft snapshot, bounded, with resource content marked as untrusted (FR-016, FR-017, FR-031).
- [x] T024 Graph read by scope path, and graph activities by definition and version (FR-014).
- [x] T025 Expression interpretation, semantic diagnostics and edits unconditionally unavailable (routing to an Expression Module deferred to #3711). Explanation guidance names the revision it describes and uses only the designer's diagnostics (FR-016, FR-018).
- [x] T026 Remove the `workflows.proposal` renderer, the explain-executable prompt and `getExecutableDetail`. Add the registered-reference behaviour test (FR-019, SC-007).
- [x] T027 Glossary entries in `Studio/CONTEXT.md` (FR-035).
- [x] T028 Probe case `workflow-explain` with a canary search, and a Playwright journey (SC-005).

## Slice E: Library, create dialog and templates (stories 6, 7)

- [x] T029 Operation-key overloads on `IWorkflowLibraryStore`, the EF column with its unique index scoped by tenant scope key (and subject for views), migrations for four engines, and the API change (FR-021, FR-033).
- [x] T029a Extract `IWorkflowLibraryCommands` into `Elsa.Workflows.Design.Core`, implemented in the Api from the inline endpoint authority logic. The endpoints keep the built-in decisions; a replacement decides only Weaver's writes (D4).
- [x] T030 `workflows.library` context and the list-state client tool (FR-020).
- [x] T031 Library write tools for tags, favorites, folders and views, with per-target outcomes (FR-021, FR-025).
- [x] T032 Unavailable when organization is disabled (FR-022). Refuse deletes.
- [x] T033 `workflows.create-dialog` context and the prefill tool (FR-023).
- [x] T034 `workflows.template-list` and `workflows.template-editor` contexts, and template batches through the template adapter (FR-024).
- [x] T035 [P] Tests and EF provider-contract tests. Update the catalogs (FR-034).
- [x] T036 Probe cases `workflow-library` and `workflow-templates`, and Playwright journeys (SC-008).

## Slice F: Versions and Executables (story 8)

- [x] T037 Add `IWorkflowVersionReader` to `Elsa.Workflows.Design.Core`, implemented in the Api. Add the `workflows.version` context and the version tool with a two-version comparison, refusing edits (FR-015, FR-026, D4, R6).
- [x] T038 Add `IWorkflowExecutableInspection` to `Elsa.Workflows.Runtime.Core`, implemented in the Api. Create the new package `Elsa.Agent.Workflows.Runtime` with its feature, the `workflows.executable` context and `workflow_read_executable`, and its own test project `Elsa.Agent.Workflows.Runtime.Tests` (FR-027, FR-028, D2, D4).
- [x] T039 Restore the explain-executable prompt against the registered tool (FR-019).
- [x] T040 Composition tests for the Design-only, Runtime-only and combined shapes (SC-009). Add the package to the conformance consumers.
- [x] T041 Probe cases `workflow-versions` and `workflow-executables`, and normal-host journeys for the version history and the Executable Inspector (FR-036).

## Slice G: One exact Run (story 9)

- [x] T042 Add `IWorkflowRunInspection` to `Elsa.Workflows.Runtime.Core`, implemented in the Api. Add the `workflows.run` context and `workflow_read_run`, bound to the instance and the activity execution (FR-029, D4).
- [x] T043 Redaction and unavailable-by-reason. Refuse run controls.
- [x] T044 Studio run-page context provider.
- [x] T045 Probe case `workflow-run` with a canary search, and a normal-host journey on the run page (SC-005, FR-036). Implemented (`run_journeys.py`, the `workflow-run` browser journey and the `QualificationRunCapture` seam); native evidence: `{copilot,anthropic}-workflow-run` passed in the complete witness on `8e5657d39`. Pruned and unsupported-backend evidence is unit-tested only (see the plan's Slice G deviation).
- [x] T046 Full package conformance witness and the composed Studio suite on the final source. Record the acceptance evidence in spec.md, set its status to Implemented, and close #3442 (SC-010).

## Cross-cutting, in every slice

- [x] T047 Every new replacement contract has a single-registration guard with a clear diagnostic. Every new implementation is `internal sealed` (FR-032).
- [x] T048 Catalogs are updated in the same PR as their code (FR-034), including `Extensions/Agent/src/Elsa.Agent/EXTENSION_POINTS.md`, which lists `DefaultWorkflowAgentContextProvider`.
- [x] T049 Evidence is recorded by category, with failures, skips and quarantines as they occurred (FR-037).

## Traceability

| Requirement | Tasks |
| --- | --- |
| FR-001, FR-002 | T001–T006 |
| FR-003–FR-006 | T007–T012 |
| FR-007–FR-013, FR-015 | T013–T022, T037 |
| FR-014, FR-016–FR-019 | T023–T028, T039 |
| FR-020–FR-025 | T029–T036 |
| FR-026–FR-029 | T037–T045 |
| FR-030 | packages per plan D2 and D4, in every slice |
| FR-031 | T009, T023 |
| FR-032–FR-034 | T005, T011, T035, T047, T048 |
| FR-035 | T027 |
| FR-036, FR-037 | T006, T012, T022, T028, T036, T041, T045, T046, T049 |
| SC-001 | each slice's probe and journey tasks |
| SC-002 | T016, T017, T021 |
| SC-003 | T010 |
| SC-004 | T013, T021, T022, T032 |
| SC-005 | T028, T045 |
| SC-006 | T004, T006 |
| SC-007 | T026 |
| SC-008 | T029, T031, T036 |
| SC-009 | T040 |
| SC-010 | T046 |
