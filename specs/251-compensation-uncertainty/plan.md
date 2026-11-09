# Implementation plan: uncertain effects and resumable compensation (M2)

**Spec**: [spec.md](spec.md) · **Issue**: [#3045](https://github.com/elsa-workflows/elsa-foundation/issues/3045) · **Prerequisite slice of**: [#2747](https://github.com/elsa-workflows/elsa-foundation/issues/2747)

## Technical context

.NET 10 runtime modules under `Essentials/`, React 19/Vite Studio under `Studio/`. No new tables. M2 adds
three generic runtime seams, one runtime alteration kind, and a version-2 compensation scope state. Everything
else reuses M1's contracts: structural evaluation, fault absorption, private state, causal result resolution.

Design decisions and their evidence are in [research.md](research.md). The state is in [data-model.md](data-model.md). The transitions and alteration contract are in [contracts/](contracts/). The proof journeys are in [quickstart.md](quickstart.md).

## Placement

| Item | Location | Notes |
|---|---|---|
| Operation keys | `Essentials/src/Activities/Runtime/Core/Models/ActivityExecutionContext.cs` (+ start/resume contexts, `StatefulActivity`), keys in `RuntimeMetadataKeys` | Resolved by the runtime when it builds the context: assigned key → first attempt id → invocation id (R1) |
| Fault code on child-fault callback | `ActivityChildFaultedContext` and its construction in `WorkflowParentActivityCompletionSchedulerWorkHandler` | Read from the committed child state's normalized fault (R2) |
| Intervention request | `IRuntimeActivityExecutionContext.RequestChildFaultIntervention`, applied next to absorption in `WorkflowParentActivityCompletionSchedulerWorkHandler`; source `IncidentResolutionSystemSources.StructuralIntervention` | Same validation rules and same checkpoint as absorption (R3) |
| Absorption metadata | `ResolveAbsorbedIncident` copies the request metadata into the outcome | Lets Studio tell an uncertain step from a plain fault (R3) |
| Retry alteration | `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/Handlers/RetryFaultedActivityAlterationHandler.cs`, kind `RetryFaultedActivity/1` | Built on `RescheduleActivity`'s successor and supersession logic; adds incident resolution and keeps the scheduling cause (R4) |
| Alteration incident writes | `WorkflowAlterationRuntimeCheckpointStateBuilder.AddIncident` and the alteration checkpoint committer | Alteration checkpoints cannot write incidents today |
| Scope v2 | `Essentials/src/Activities/Compensation/` (`CompensationScope`, `Internal/CompensationScopeState`, `CompensationScopeStatePersister`, new `CompensationScopeStateUpcaster`) | R5, R6 |
| Studio | `compensationRuntime.ts`, `workflowTypes.ts`, `CompensationScopeDesigner.tsx`, run-view incident panel | R7 |
| Test participant | `Essentials/tests/Activities/Compensation/Tests/Support/Participant.cs`, `Essentials/tests/Workbench/Fixtures/Reservations/` | Implements the FR-220 contract with lost responses, tombstones and fail-n-times |
| Docs | `website/docs/essentials/compensation.md`, glossary, spec 250 `contracts/semantics.md`, program charter | FR-219 |

## Constitution and ADR checks

- **§E2.2:** all changes stay inside Runtime and Activities. Nothing touches Design, except the Studio client.
- **§E2.6.1 (executable always runs):** a scope from M1 resumes under M2 through the state upcaster. No published artifact stops running.
- **§E6 naming:**
  - `RetryFaultedActivity` has 3 components;
  - `RequestChildFaultIntervention` mirrors the existing `RequestChildFaultAbsorption`;
  - `CompensationScopeStateUpcaster` has 4 components;
  - `StructuralIntervention` mirrors `StructuralFaultAbsorption`.
- **ADR 0080 D1 risk-based tests:**
  - unit tests for key resolution, the intervention seam, the upcaster and the scope transitions;
  - integration and restart tests for the journeys;
  - Studio Vitest for each state.
- **ADR 0080 D3 internal by default:** the new public surface is the two context properties, the seam method, the alteration kind constant and the system-source constant.
- **ADR 0049:** the alteration envelope is schema-versioned (`/1`).
- **ADR 0080 D0 park:** delivering #2747's core while Runtime Alterations is parked is the owner exception OD5 (20 October 2026). Bulk retry, cancel-run and variable edits stay parked.
- **Extension points:** `EXTENSION_POINTS.md` lists the new seam and the alteration kind; maps regenerate.

## Delivery as PRs

Merging goes straight onto `main`, not as a long stack (lesson from M1).

1. **Runtime seams + retry alteration** (#2747 core). Covers operation keys, the child-fault code, the intervention request, absorption metadata, `RetryFaultedActivity/1` and alteration incident writes, with unit and integration tests. Closes the #2747 runtime slice. The Studio button follows in M4.
2. **Scope v2 + Studio + journeys.** Covers the state upcaster and transitions; the uncertainty, retry and intervention tests across modes; the Workbench lost-response and resume journey; the Studio states and the `resolutionOutcome` fix; docs; and the spec 250 and charter amendments. Spec 251 is set to `Implemented` in this PR.

PR 2 starts once PR 1 is green, and merges after it.
