# Implementation plan: operator actions on compensation recovery (M4)

**Spec**: [spec.md](spec.md) · **Issue**: [#3047](https://github.com/elsa-workflows/elsa-foundation/issues/3047) · **Also closes**: the Studio slice of [#2747](https://github.com/elsa-workflows/elsa-foundation/issues/2747)

## Technical context

.NET 10 runtime modules under `Essentials/`, React 19/Vite Studio under `Studio/`. No new table. M4 adds two generic runtime seams (R-5 intervention decisions, R-6 activity state view), one built-in alteration kind (`DecideIntervention/1`), one refusal on `RetryFaultedActivity/1`, a per-run permission signal, scope state v4, and the Studio actions. Evidence is in [research.md](research.md); state in [data-model.md](data-model.md); transitions and payloads in [contracts/](contracts/); proofs in [quickstart.md](quickstart.md).

## Placement

| Item | Location |
|---|---|
| R-5 offer: `RequestChildFaultIntervention` overload with decisions | `Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs`, `RuntimeChildFaultInterventionRequest`, `SimpleActivityExecutionContext`; applied in `WorkflowParentActivityCompletionSchedulerWorkHandler` (outcome metadata) |
| R-5 alteration: `DecideIntervention/1` | `Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/Handlers/DecideInterventionAlterationHandler.cs`; reserved in `WorkflowAlterationRegistry` and `WorkflowAlterationServiceCollectionExtensions`; registered with the built-ins |
| R-5 constants and rule | `IncidentResolutionActionKinds.OperatorDecision`, `IncidentResolutionSystemSources.OperatorDecisionAlteration`, `IncidentStateTransitionValidator`, `RuntimeMetadataKeys` |
| R-5 delivery | `WorkflowExecutionCommandKind.DeliverInterventionDecision`, `RuntimeInterventionDecisionCommandPayload` (Core); `IRuntimeActivityInterventionDecisionHandler`, `ActivityInterventionDecidedContext` (Core); `WorkflowInterventionDecisionSchedulerWorkHandler` (Activities.Runtime, modelled on `WorkflowNotifyParentActivitySchedulerWorkHandler`) |
| Retry refusal (FR-405) | `RetryFaultedActivityAlterationHandler` |
| Explicit-target capture (FR-406) | `WorkflowAlterationTargetCaptureTask` explicit branch |
| R-6 state view | `IActivityStateViewProjector`, `ActivityStateViewContext` (Core); endpoint `Workflows/Runtime/Api/Endpoints/ActivityExecutions/StateView/Endpoint.cs`; `ActivityStateViewService`; capability source next to `RuntimeAlterationCapabilitySource` |
| Permission signal | `IWorkflowRuntimeOperatorAccess` + HTTP implementation (Runtime.Api); `WorkflowInstanceDetailsView.Permissions` |
| Scope v4 and decisions | `Activities/Compensation/` (`CompensationScope` implements `IRuntimeActivityInterventionDecisionHandler`; `Internal/CompensationScopeState`, persister, upcaster v3→v4) |
| Journal projector | `Activities/Compensation/Internal/CompensationJournalProjector.cs`, registered by `ActivitiesCompensationFeature` |
| Studio | `api/alterations.ts` (new), `api/runtime.ts` (state view, permissions), `compensationRuntime.ts` (journal mapping, new states, history), `graph-authoring/CompensationScopeDesigner.tsx` (actions, banner, history), `OperatorActionDialog.tsx` (new), `workflow-editor/WorkflowInstances.tsx` (incident dialog actions, URL selection), `workflowTypes.ts` |
| Docs | `website/docs/essentials/compensation.md` (operator section), `docs/glossary/elsa.md`, runtime and compensation `EXTENSION_POINTS.md`, `Elsa.Workflows.Runtime/README.md` (built-in list) |

## Phases

- **A — Runtime seam and incident rule.** R-5 end to end (offer, alteration, delivery, validator), FR-405, FR-406. Proven with a test structural activity, not the scope.
- **B — Scope decisions, state v4, journal, permission signal.** The scope offers and applies decisions; v4 and upcaster; R-6 and the compensation projector; `permissions` on run details; backend journeys O1–O8, J1 in every mode; Workbench journey; negative controls.
- **C — Studio.** Alteration client, journal-backed view with fallback, new states, actions and dialog, polling and error mapping, history, URL selection, mocked browser suite, live journey.
- **D — Tests and docs.** User guide operator section, glossary, `EXTENSION_POINTS.md`, runtime README, spec status. Backend docs ship with PR 1; Studio docs and the status change with PR 2.

## Constitution and ADR checks

- **§E2.2:** runtime seams stay generic in Workflows.Runtime and Activities.Runtime; compensation knowledge stays in Activities.Compensation, which still references only Core projects; Studio talks to the API through capability links.
- **§E2.6.1:** v1–v3 scopes resume under v4 through the upcaster; waits that began before M4 still accept a retry.
- **§E6 naming:** `DecideIntervention` (2 components), `OperatorDecision` mirrors `OperatorRetry`, `OperatorDecisionAlteration` mirrors `OperatorRetryAlteration`, `IRuntimeActivityInterventionDecisionHandler` follows the `IRuntimeActivityChild…Handler` family, `IActivityStateViewProjector` (4 components).
- **ADR 0049:** `DecideIntervention/1` is schema-versioned; `RetryFaultedActivity/1` gains a refusal, not a new effect, so it keeps its version.
- **ADR 0080 D0:** `DecideIntervention/1` is the owner's one-off exception (OD12). Bulk retry, cancel-run and variable edits stay parked. FR-406 changes only explicit targets.
- **ADR 0080 D1 risk-based tests:** unit tests per refusal, per transition and for the upcaster; integration and restart journeys per mode; API tests for authorization and tenants; Vitest per state and action; the live journey.
- **ADR 0080 D3 internal by default:** new public surface: the overload, the callback interface and context, the projector interface and context, the operator-access interface, the constants and the alteration kind. The handler, service and projector classes are internal or sealed in their assemblies.
- **Extension points:** both `EXTENSION_POINTS.md` files list R-5 and R-6; maps regenerate locally (not committed).

## Delivery as PRs

1. **PR 1 — Phases A + B** (backend): R-5, FR-405, FR-406, scope v4 and decisions, R-6 and the projector, the permission signal, backend journeys and Workbench, negative controls, and the backend half of D: user-guide API section, glossary, `EXTENSION_POINTS.md`, runtime README.
2. **PR 2 — Phase C + the rest of D** (Studio): actions, dialog, polling, history, journal-backed view, URL selection, mocked and live browser suites, the user guide's Studio walkthrough, and spec 260 status `Implemented`. Closes #3047 and the #2747 Studio slice.

PR 2 starts once PR 1 is merged on `main`. The spec 258 FR-315 wording fix ("never starts a handler") ships with this design packet, not with either PR.

## Risks

| Risk | Mitigation |
|---|---|
| The delivery handler duplicates notify-parent plumbing | Extract the shared reactivation and commit into a helper first, with the notify suites unchanged (T405). |
| A decision lands on a scope that was cancelled in between | Acked unapplied; journal and Studio show it (edge case); covered by O5c and a delivery unit test. |
| FR-406 is a policy change | Decided by the owner as OD16; limited to explicit targets; reverting it is one method. |
| Studio fallback and journal disagree | Vitest runs both paths over the same fixtures and compares the shown states. |
