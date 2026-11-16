# Implementation tasks

Spec and revised plan reviewed independently for correctness, standards and acceptance. No remaining planning findings. Check tasks only when implementation/evidence is complete; hosted/merge receipts belong on #3518 and its PR.

## Typed structure inputs

- [x] T01 Add causal tests in existing typed-input/contract/hydrator suites for structure-owned input contracts, ordered materialization, skipped later errors, unchanged legacy fingerprints and CLR hydration exclusions.
- [x] T02 Add Design/Core input projection model and default hooks to `Contracts/IActivityStructureHandler.cs`, `Contracts/IActivityStructureService.cs` and `Services/DefaultActivityStructureService.cs`.
- [x] T03 Extend `Activities/Runtime/Core/Models/ActivityContract.cs` with optional structure-input ownership and fingerprint omission for false. Update tracked public API declarations.
- [x] T04 Compile projected definitions/states with normal binding compilation in `Workflows/Publishing/.../ExecutableNodeCompiler.cs`, honoring consumed ordinary inputs and marking structure-owned contracts. Keep all bindings in the existing map.
- [x] T05 Add keyed runtime structure-input materialization policy/consumer contracts in `Workflows/Runtime/Core/Contracts/`; reject missing/duplicate ownership. Integrate ordered pre-activation materialization into `RuntimeActivityInputMaterializer.cs`, using one normal resolution snapshot and preserving durable policies.
- [x] T06 Extend `Activities/Runtime/.../ActivityInputHydrator.cs` to hydrate ordinary properties normally and deliver structure-owned inputs from the existing resolved activation snapshot. Keep external/withheld behavior and fresh-instance rules.

## Switch execution and compatibility

- [x] T07 Add compiler/runtime regressions in `Activities/ControlFlow/Tests/Switch/` for true/false, first true, skipped later errors, evaluated failures, empty branch, Default and Break.
- [x] T08 Add v2 stable IDs/condition arguments and compiled input keys to Switch models. Register v2 and retained v1 structure handlers, current source version and collection condition/ID metadata. Reject predicates in v1 and malformed/duplicate v2 IDs.
- [x] T09 Implement the Switch-owned materialization policy and pinned-input consumer. Retain v1 Value matching, defer v2 legacy Value until reached and omit it when unused. Do not silently default missing pinned values.
- [x] T10 Add worker-loss/redelivery/retry proof to existing runtime journey tests: change source variables after the Running checkpoint and verify the original pinned case and no repeated condition evaluation.
- [x] T11 Add publication/hash/serialization and reusable placement tests proving condition changes alter the artifact, old artifacts retain identity, and variable/result references remap. Exercise the relevant legacy and condition REST journeys on a fresh source host; record that the existing PowerShell wrapper could not run because pwsh is unavailable.

## Studio and tooling

- [x] T12 Extend optional collection condition/ID metadata from `ActivityChildSlotAttribute.cs` through catalog facets and Studio types. Retain old collection metadata behavior.
- [x] T13 Extend persisted Workflow expression context and `ExpressionDraftSemanticValidator.cs` to project structure inputs, Boolean targets and active arguments; exclude consumed unused Value. Test authorization/unavailable context and target typing.
- [x] T14 Add collection model tests and helpers for predicate initialization, clear-to-unbound, stable case IDs, condition updates, moving and explicit v1->v2 upgrade. Preserve unknown members and descendants; refuse unsupported direct-Secret legacy upgrades.
- [x] T15 Render standard Boolean `ActivityPropertiesPanel` controls in `CollectionSlotEditor.tsx` through shared Inspector props; preserve selected syntax, standard data tooling, read-only controls and current host capability reporting. Add explicit upgrade/Use condition/priority controls.
- [x] T16 Use stable v2 slot IDs in `workflowAdapter.ts`, keep indices separately and reconcile current breadcrumb labels. Preserve v1 and Parallel identities. Cover owner-inspector edits while a branch is open.
- [x] T17 Add rendered/browser cases for both hosts and Sequence/Flowchart/BPMN: expression syntax, save/reload, label/branch edits, priority, clear predicate, pending stale edits, open-branch reorder/delete/undo, source-owned/read-only and narrow keyboard behavior.

## Qualification and delivery

- [x] T18 Update canonical extension-point catalogs and Switch README for the new projection/materialization/hydration contracts and v1/v2 behavior. Record the pre-existing Activity Graph server-language-service limitation without claiming new support.
- [x] T19 Run focused and complete affected frontend tests, lint/typecheck/style/build/bundle checks; affected backend tests, architecture and generated maps checks. Record failures/skips/retries separately.
- [x] T20 Run fresh real Workbench/Studio proofs: author conditions, autosave/reload, publish, execute first-match/default/error; publish/place/run a condition-bearing Activity Definition graph with variable and prior-result references. Clean owned processes.
- [x] T21 Recheck main, integrate if necessary, requalify and obtain independent reviews of the complete diff plus issue/PR comments. Record causal red/green evidence.
- [ ] T22 Open/attach the PR with acceptance mapping, request Code Review, verify exact-head CI/Studio checks, merge through authorized review and qualify resulting main. Close #3518 with the merge/source-preservation receipt.

T22 includes gates that occur after the final branch commit. Exact-head hosted checks, merge and resulting-main receipts are recorded on PR #3539 and issue #3518.
