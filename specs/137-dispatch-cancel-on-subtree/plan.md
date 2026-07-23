# Implementation Plan: Cancel Waited Dispatches on Subtree Teardown

**Branch**: `codex/998-seam-a-dispatch-cancellation` | **Date**: 2026-07-23 | **Spec**: [spec.md](spec.md)

**Input**: GitHub issue #998 and the approved feature specification in this work unit.

## Summary

Extend the existing DispatchWorkflow checkpoint cancellation enricher so it derives child-cancellation work from either the existing whole-parent `Cancelled` transition or an exact locally-cancelled parent activity execution in the same checkpoint. Reuse the shipped dispatch query, eligibility policy, deterministic request/intent construction, replay conflict checks, provider resolution, outbox recovery, and child Cancel delivery without adding a BPMN-specific hook, public contract, or schema.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`, nullable enabled, implicit usings enabled)

**Primary Dependencies**: Elsa Activities Runtime/Core, Workflows Runtime/Core, DispatchWorkflow Runtime

**Storage**: Existing runtime checkpoint state changes, workflow-dispatch store/query capability, and post-commit outbox

**Testing**: xUnit DispatchWorkflow unit tests plus Activities Runtime, Workflows Runtime, BPMN, and architecture regressions

**Target Platform**: Cross-platform .NET server hosts

**Project Type**: Multi-project runtime library feature

**Performance Goals**: One bounded parent-dispatch scan per relevant checkpoint; no extra child-state query or polling path; exact owner matching

**Constraints**: Preserve deterministic fingerprints, full paging, non-advancing-cursor rejection, provider-atomic directive resolution, existing retry semantics, public API, persistence schema, and unrelated dispatch behavior

**Scale/Scope**: All waited dispatches owned by one workflow execution, with zero or more exact locally-cancelled activity owners in one checkpoint

## Constitution Check

*GATE: Passed before research; re-checked after design.*

- **Runtime/Design boundary**: PASS — the change remains inside runtime and DispatchWorkflow implementation projects and reads no design state.
- **Artifact-only execution**: PASS — no child definition resolution or authored source data is introduced.
- **Checkpoint/post-commit discipline**: PASS — child-cancel responsibility is still committed through the existing checkpoint enricher before delivery.
- **Single-writer/actor boundary**: PASS — the provider resolves dispatch coordination state; child execution changes only through the existing actor Cancel command.
- **Sanctioned pattern catalog**: PASS — this extends the existing checkpoint-enricher contribution and post-commit delivery pattern; no new structural pattern is introduced.
- **Provider neutrality**: PASS — no provider contract or provider-specific implementation changes.
- **Replay/idempotency**: PASS — exact owner selection feeds unchanged deterministic request/intent factories and equivalence checks.
- **Compatibility/SemVer**: PASS — internal bug fix only; no public API, persisted schema, package dependency, or wire identifier changes.
- **Naming**: PASS — no new Elsa-owned type is required.
- **Documentation/catalog discipline**: PASS — no extension point changes; the work-unit contract documents the broadened trigger semantics.
- **Test discipline**: PASS — branch-covering unit regressions cover local-only, unrelated, combined, ineligible, terminal, replay, and paging paths; existing tests remain intact.
- **Constitution status**: Both constitutions remain draft pending ratification. This work treats their current checkpoint, modularity, compatibility, and test rules as active repository gates and requires no provisional-rule exception.

Post-design re-check: PASS. No constitution exception or complexity waiver is required.

## Architecture and Flow

1. Read whole-parent cancellation from the workflow-execution change exactly as today.
2. Derive an ordinal set of activity-execution IDs from upserted state changes whose terminal status is `Cancelled`.
3. If neither signal exists, return the checkpoint unchanged without querying dispatch records.
4. Enumerate every dispatch page for the checkpoint workflow execution in the existing stable `(CreatedAt, DispatchId)` order.
5. Select a record when whole-parent cancellation is active or its exact parent activity execution appears in the local-cancellation set, then apply the unchanged wait-mode, propagation-policy, active/marked, and committed-outbox eligibility rules.
6. Reuse the existing canonical cancellation request, post-commit intent, equivalence, conflict, and deduplication logic so combined whole-parent/local cancellation still emits one logical responsibility.
7. Leave provider resolution and child Cancel delivery unchanged; they already converge across admission, terminal, retry, and replay races.

## Project Structure

### Documentation

```text
specs/137-dispatch-cancel-on-subtree/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/dispatch-cancel-on-subtree.md
├── checklists/requirements.md
└── tasks.md
```

### Source and Tests

```text
src/Elsa/Activities/DispatchWorkflow/Runtime/Services/
└── WorkflowDispatchCancellationEnricher.cs

tests/Elsa/Activities/DispatchWorkflow/Tests/
└── WorkflowDispatchCancellationTests.cs
```

**Structure Decision**: Extend the existing DispatchWorkflow implementation and its direct unit-test project. Add no runtime-core contract, provider implementation, BPMN code, persistence schema, or new project.

## Ordered Delivery

1. Lock local-cancellation selection, exact isolation, combined-trigger deduplication, eligibility, terminal, replay, and late-page behavior with focused tests.
2. Extend `WorkflowDispatchCancellationEnricher` with a small local-cancellation selection helper or inline set derivation, preserving the existing paging and work-construction loop.
3. Run focused tests, related runtime/BPMN regressions, and architecture checks.
4. Run up to five bounded self-review/fix iterations, commit the work unit, push the organization branch, open a draft PR that closes #998, and update the issue with validation evidence.

## Complexity Tracking

No constitution violation requires justification.
