# Research: operator actions on compensation recovery (M4)

Citations are to `main` at `e9a3d18aa` (20 October 2026). Paths are under `Essentials/src/` unless they say otherwise. Studio paths are under `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/`.

## R1 — The incident is the lock

- **Evidence:**
  - A scope waits for an operator only through `RequestChildFaultIntervention`: the child's incident stays `Blocking` with outcome `WaitForIntervention` from `StructuralIntervention` (`Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs:100-115`; applied in `Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs:870-913`, which records the requester as `runtime.interventionRequestedBy` at :891).
  - The scope uses it in two places: a handler out of attempts (`Activities/Compensation/Activities/CompensationScope.cs:574-586`) and an uncertain step without a handler (`CompensationScope.cs:449-458`, via `WaitForStep` at :893-913). The entry records the incident as `InterventionIncidentId`.
  - A committed outcome is write-once. The single exception is `WaitForIntervention` → `Resolved` by `OperatorRetry` from `OperatorRetryAlteration` on the same activity execution (`Workflows/Runtime/Core/Models/IncidentStateTransitionValidator.cs:14-32, 49-56`).
  - While the scope waits, nothing else advances that entry: a repeat fault is filtered by incident (`CompensationScope.cs:851-860`), a hand-over only adds entries (`CompensationScope.cs:305-348`), Confirm and Compensate are ignored outside `Running` (`CompensationScope.cs:352-363, 386-389`).
- **Decision:** every operator action targets the waiting incident, and is valid only while that incident is `Blocking` with `WaitForIntervention`. The first action to resolve it wins; any later one is stale and refused. No extra version check is needed on the scope.

## R2 — Seam choice: one generic decision alteration, not compensation kinds

- **Evidence:**
  - Modules can contribute alteration kinds: `AddWorkflowAlterationHandler<THandler>` accepts dotted, namespaced kinds (`Workflows/Runtime/Core/Extensions/WorkflowAlterationServiceCollectionExtensions.cs:15-47`; registry rules at `Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/WorkflowAlterationRegistry.cs:9-17, 60-74`).
  - But a handler reads the run through `WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection`, which lives in the implementation assembly (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/WorkflowAlterationActorCommandExecutor.cs:90`). The compensation module references only the Core projects (`Activities/Compensation/Elsa.Activities.Compensation.csproj`).
  - A compensation-owned handler would also have to write the scope's private state outside the scope's own evaluation. Private state is staged by a structural continuation against exactly one open attempt (`Workflows/Runtime/Core/Models/RuntimeStructuralStateProjector.cs:20-22`); an alteration has no such attempt.
  - It would still need the incident validator (R5) to accept a compensation-specific outcome, which puts compensation knowledge in `Workflows.Runtime.Core`.
  - The M2/M3 precedent is generic seams that the scope uses: operation keys, intervention, R-1 to R-4 (spec 251 OD6, spec 258 runtime-seams).
- **Decision (R-5):** one built-in alteration, `DecideIntervention/1`, plus a structural callback:
  1. the requester **offers** decisions when it asks for intervention (FR-401);
  2. the alteration checks the decision against the offer and the incident lock, resolves the incident and enqueues the decision, in one checkpoint (FR-402);
  3. the requester **applies** it in an ordinary structural evaluation (FR-403).

  The requester validates by what it offers, and applies by its own transition. The runtime only carries codes it does not interpret. This is one kind, under the owner's limit of two.
- **Alternatives considered:**
  - Two compensation kinds (`Compensation.ResolveEntry/1`, `Compensation.AbandonScope/1`) through the extension point. Rejected for the reasons above.
  - Resolving the incident through a new incident endpoint. Rejected by #1015 and by spec 251 (`specs/251-compensation-uncertainty/research.md:56,60`).
  - Delivering the decision as a parent notification. Rejected: notification codes are chosen by activity code, so a child could forge a decision (`Workflows/Runtime/Core/Models/RuntimeNotifyParentCommandPayload.cs:6-13` spoof-proofs the target, not the code).

## R3 — How the scope receives the decision durably

- **Evidence:**
  - An alteration job commits all staged changes with its terminal evidence in one checkpoint, or records one failed outcome and applies nothing (`Workflows/Runtime/Elsa.Workflows.Runtime/README.md:38-42`).
  - The alteration checkpoint can stage incident changes and post-commit intents that become durable in that same checkpoint (`Workflows/Runtime/Core/Contracts/Alterations/IWorkflowAlterationExecutionContracts.cs:91-98, 112-127`). `RetryFaultedActivity/1` uses both (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/Handlers/RetryFaultedActivityAlterationHandler.cs:112-127`).
  - Structural evaluations are scheduler work items with their own command kind; the list ends at `AlterWorkflow = 18` (`Workflows/Runtime/Core/Models/WorkflowExecutionCommand.cs:16-37`). The notify-parent handler shows the shape: reactivate the parent from its pinned snapshot, ack when the parent is no longer running (`Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowNotifyParentActivitySchedulerWorkHandler.cs:74-78`).
- **Decision:** the alteration checkpoint holds the resolved incident and a post-commit intent for a new command kind, `DeliverInterventionDecision`. Its handler (in Activities.Runtime, modelled on the notify-parent handler) reactivates the requester and calls `IRuntimeActivityInterventionDecisionHandler.OnInterventionDecidedAsync`. The requester's evaluation commits its state, child schedules, subtree cancellations and continuation in one checkpoint, as any structural evaluation does. So:
  - the decision is durable as soon as the alteration commits (incident outcome plus queued work item);
  - a kill between the two commits replays the queued work item once (processed marker, as for notifications);
  - a requester cancelled in between acks the delivery: the incident shows the decision, the journal shows the entry unchanged (spec edge case).
- **To verify in PR 1:** the work-item id is derived from the alteration job id and incident id, so a replayed alteration checkpoint enqueues the same item.

## R4 — Offered decisions and what the alteration checks

- **Decision:**
  - `RequestChildFaultIntervention` gains an optional `decisions` list (an overload, so callers are unchanged). The runtime writes it to the `WaitForIntervention` outcome metadata as `runtime.interventionDecisions` (a JSON array) and adds `runtime.interventionRequestedBy` there too, because outcome metadata is readable without the sensitive grant while incident metadata is not (`Workflows/Runtime/Api/Models/WorkflowExecutionViews.cs:568-582, 594-604, 612-615`).
  - Offering decisions from a type that does not implement the callback faults the evaluation, like any seam misuse.
  - `DecideIntervention/1` preflight, in order: payload, run not terminal, incident exists in the run, incident waits (`Blocking`, `WaitForIntervention`, `StructuralIntervention`), decision offered, requester exists and is `Running`, requester has no claimed scheduler work, pinned executable has the requester node. Codes are in [contracts/operator-actions.md](contracts/operator-actions.md).
- **Compatibility:** a wait that started before M4 offers no decisions, so only Retry applies to it; when that retry fails again, the new wait offers decisions. No compiled capability flag is needed: the offer itself says the requester accepts decisions.
- **Captured concurrency:** the payload names an incident, not an activity, so the capture task's activity-id switch (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/WorkflowAlterationTargetCaptureTask.cs:185-191`) is unchanged. The incident lock (R1) is the stale check.

## R5 — Incident transition rule

- **Evidence:** `IncidentStateTransitionValidator.cs:49-56` names the retry pair exactly; `IncidentResolutionActionKinds.cs:4-16` and `IncidentResolutionSystemSources.cs:4-16` hold the constants.
- **Decision:** add `IncidentResolutionActionKinds.OperatorDecision` and `IncidentResolutionSystemSources.OperatorDecisionAlteration`, and allow `WaitForIntervention` → `Resolved` with that pair on the same activity execution. Nothing else changes: every other committed outcome stays write-once.

## R6 — Outcome vocabulary

- **Evidence:** spec 250 names the entry status `ResolvedManually`, "an out-of-band resolution, with reason", not eligible (`specs/250-native-compensation/contracts/semantics.md:28`). Spec 251's participant contract separates "effect present" from "effect absent" (FR-220).
- **Decision:** one status, `ResolvedManually`, with a resolution outcome:

  | Outcome | Decision code | Studio label | Meaning |
  |---|---|---|---|
  | `EffectAbsent` | `compensation.resolve.effect-absent` | Effect absent or undone | The effect does not exist: it never happened, or someone undid it outside Elsa. |
  | `EffectAccepted` | `compensation.resolve.effect-accepted` | Effect present, keep it | The effect exists and the business keeps it. |

  Abandon is `compensation.abandon`. Studio shows the entries as "Resolved: effect absent" and "Resolved: effect kept".
- **Alternatives considered:** two statuses (`ResolvedAbsent`, `ResolvedKept`). Rejected: spec 250 fixed one status, and one field keeps the "eligible" rule simple.

## R7 — What resolve does on a handler and on a forward step

- **Handler (entry `Failed`):** both outcomes settle the entry. Recovery continues with the next eligible entry, or finishes as in spec 251. A scope whose entries end compensated or resolved completes `Compensated` (spec 250 semantics: "every registered entry is `Compensated` or explicitly resolved"), or faults with `faultAfterCompensation`.
- **Uncertain forward step without a handler (entry `Uncertain`, phase `AwaitingIntervention` from `Running`):**
  - `EffectAbsent` means the step failed with no effect. That is what spec 251 already does when an operator retry of such a step faults definitely: recover the earlier steps (`specs/251-compensation-uncertainty/contracts/semantics.md`, "retried uncertain step without a handler"). The cause stays `uncertain`.
  - `EffectAccepted` means the step's effect happened. That is the spec 251 FR-213 path when a retried step completes: continue forward (`CompensationScope.cs:612-629, 631-654`). The step produced no result, so a later binding to it faults as a missing value (spec 250 edge case).
- **Descendant faults:** when the waiting incident came through a Sequence or loop step (spec 258 R-3), the step's subtree is still suspended. Resolve cancels the via child's subtree in the same evaluation (allowed with `Defer` or `Complete`, `Activities/Runtime/Elsa.Activities.Runtime/Services/StructuralParentEvaluationSupport.cs:157`).

## R8 — Abandon and its fault

- **Evidence:**
  - Spec 250 fixed `Abandoned`: "the scope faults with the cause; unresolved entries stay visible as unresolved" (`specs/250-native-compensation/contracts/semantics.md:16`).
  - A structural fault keeps the scope's private state: the fault path applies the staged state and only adds the fault (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:549-563`). Completion clears it (`StructuralParentEvaluationSupport.cs:412-432`, line 429).
  - An enclosing scope treats a step fault with category `outcome-unknown` as an uncertain step: undone by its handler, or a wait (`CompensationScope.cs:449-465`); spec 258 R14 uses this for `uncertain-unhandled`.
- **Decision:** phase `Abandoned`; no further handler; entry statuses unchanged; fault `compensation.scope.abandoned`, category `outcome-unknown`, message naming the scope and how many entries were not undone.
  - At the top, the fault is a blocking incident and the run's incident strategy decides; under the default strategy the run faults. It never completes.
  - Inside another scope, the outer scope sees an uncertain step: its handler runs, or it waits and offers the same decisions, so the operator decides again at that level. Nothing is reported as undone.
  - Abandon faults at once and cancels nothing, because a fault cannot ride a subtree cancellation (spec 258 R13). A suspended descendant subtree is cancelled when an enclosing scope absorbs the fault, or ends with the run.
- **Alternatives considered:**
  - Category none (a plain fault). Rejected: the outer scope would treat the inner scope as failed with no effect and finish `Compensated` while effects remain.
  - Abandon cascading outward. Rejected: the outer scope's operator may prefer to resolve; late compensation reaches only the enclosing scope.

## R9 — A retry must not start a handler under a cancelled scope (finding)

- **Evidence:**
  - Subtree cancellation suppresses only incidents without an outcome (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/ActivityExecutions/ActivitySubtreeCancellationPlanner.cs:47-54`), so a waiting incident under a cancelled scope stays `Blocking` with `WaitForIntervention`.
  - `RetryFaultedActivity/1` checks the run, the source, its incidents and claims, but not the source's parent (`RetryFaultedActivityAlterationHandler.cs:58-97`). A retry there would schedule the handler under a cancelled scope; the scope never sees the outcome (`WorkflowNotifyParentActivitySchedulerWorkHandler.cs:74-78`), but the handler's external effect happens.
- **Decision (FR-405):** the retry preflight refuses a source whose parent is not `Running` with `SourceParentNotRunning`. This tightens a precondition; the effect and payload are unchanged, so the kind stays `/1`. Studio hides Retry on a cancelled scope.

## R10 — Which runs an operator can alter (finding)

- **Evidence:**
  - The REST alteration context seals every plan to system identity and root initiator `runtime-api` (`Workflows/Runtime/Api/Services/Alterations/HttpContextWorkflowAlterationRequestContext.cs:15-22`).
  - Capture accepts an explicit target only when the run's authority snapshot equals that scope (`WorkflowAlterationTargetCaptureTask.cs:102-115, 158-167`); otherwise the target fails `TargetNotFound`.
  - A run's authority is created from whoever started it (`Workflows/Runtime/Core/Models/WorkflowExecutionStartDispatch.cs:173`). Only the REST start and stimulus services use `runtime-api` (`Workflows/Runtime/Api/Handlers/WorkflowExecutionStartService.cs:14`, `StimulusDispatchService.cs:15`). HTTP endpoints use `http-endpoint` (`Activities/Http/Middleware/HttpEndpointMiddleware.cs:86`), Studio test runs `workflow-designer-test-run` (`Workflows/Publishing/Api/Handlers/StartWorkflowTestRunRequestHandler.cs:82`), timers `runtime.timer` (`Workflows/Runtime/Core/Constants/DurableTimerConstants.cs:17`), recurring triggers `runtime.recurring-trigger` (`Workflows/Runtime/Scheduling/RecurringTriggerPumpTask.cs:70`), Publish Event `runtime.publish-event` (`Activities/Primitives/Activities/PublishStimulusConstants.cs:10`), and child workflows a derived authority (`Activities/DispatchWorkflow/Runtime/Activities/DispatchWorkflow.cs:118`).
  - So today an operator can retry only runs started through the runtime REST API. The spec 251 Workbench journeys pass because they start runs that way.
  - Run deletion, with the same `workflows/runtime:manage` permission, checks tenant and evaluates the permission with the run as resource, not the authority (`Workflows/Runtime/Api/Services/WorkflowInstanceDeletionService.cs:84-92`).
- **Decision (FR-406), narrowest fix:** for explicit execution-id targets, capture matches the tenant only, like deletion. The run's authority is still captured for the concurrency check (`WorkflowAlterationTargetCaptureTask.cs:147-155`), so a run whose authority changes after capture still fails. Query targets keep the authority filter; bulk alteration stays parked. The plan's sealed scope and canonical hash do not change.
- **Open question for the owner:** this widens which runs an operator with manage permission can alter by id, to match run deletion. It is applied by default in PR 1; say so if it should wait.

## R11 — Journal endpoint: shape and source of truth

- **Evidence:**
  - The journal lives only in the scope's private state, read through `IActivityExecutionStateStore` and decoded by `CompensationScopeStatePersister` (`Activities/Compensation/Internal/CompensationScopeStatePersister.cs:280-307`).
  - It is kept while the scope runs, waits, is cancelled (spec 258 R1) or faulted (R8), and cleared when it completes (`StructuralParentEvaluationSupport.cs:429`).
  - The runtime API already serves per-activity inspection under `workflows/runtime:read` (`Workflows/Runtime/Api/Endpoints/ActivityExecutions/Get/Endpoint.cs:12`), with tenant and grant checks from `IActivityInspectionContextAsync` (`Workflows/Runtime/Api/Services/HttpContextActivityExecutionInspectionAuthorizationContext.cs:62-95`).
  - The scope's state format is internal to the compensation module (`Activities/Compensation/Internal/CompensationScopeState.cs:65`).
- **Decision (R-6):** a generic **activity state view**: route `GET runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view`, capability link `activity-execution-state-view`, advertised when at least one projector is registered. A module registers an `IActivityStateViewProjector` for its private-state type alias. The runtime loads the state, checks the run and grants, gives the projector the state, the pinned node, the run's incidents and the sensitive-values flag, and wraps the result. The compensation module registers one for `Elsa.CompensationScope.State` producing view kind `compensation.journal`, version 1. Workflows.Runtime keeps no compensation term, and the compensation module needs no API project.
- **Values:** the journal carries identities, keys and statuses, never a captured value. Each entry links its forward and handler occurrences; their captured inputs and outputs come from the existing activity-execution inspection, which applies `CanInspectSensitiveValuesAsync`. The one free text from a fault, the cause message, is withheld unless that check passes, as in `IncidentStateView` (`WorkflowExecutionViews.cs:578`).
- **Alternatives considered:**
  - A compensation API project with a `runtime/compensation/...` route. Rejected: a new feature, manifest and capability source for one read, and the module would need the runtime API's authorization context.
  - Keeping the journal after completion. Rejected in spec 258 R1.
  - Studio inference only (M3). Kept as the fallback, but it cannot show operation keys, attempt incidents or remaining handlers reliably (spec 258 R22).

## R12 — Permission signal

- **Evidence:**
  - The diagnostics page reads `settings.permissions.canManage` (`RuntimeDiagnosticsSettingsPage.tsx:37`), but the backend never sets it from the caller: `RuntimeDiagnosticsPermissions.CanManage` defaults to `true` (`Workflows/Runtime/Core/Models/RuntimeDiagnosticsSettings.cs:61-68`) and the service resolves without permissions (`Workflows/Runtime/Api/Handlers/RuntimeDiagnosticsSettingsService.cs:17, 33`). The pattern's shape exists; a per-user value does not.
  - Permission decisions go through `IPermissionAuthorizationService` with a `PermissionEvaluationContext` (principal, permission, tenant, resource) (`HttpContextActivityExecutionInspectionAuthorizationContext.cs:86-95`). `IActivityInspectionContextAsync` is a replacement contract (`Workflows/Runtime/Api/Contracts/IActivityInspectionContextAsync.cs:14-28`), so adding a member to it would break replacements.
- **Decision (FR-416):** the run details response (`GET runtime/workflows/instances/{id}`, `WorkflowInstanceDetailsView`, `WorkflowExecutionViews.cs:96`) gains `permissions: { canManage, canInspectValues }`. `canManage` comes from a new small request-scoped service, `IWorkflowRuntimeOperatorAccess.CanManageAsync(run)`, which evaluates `workflows/runtime:manage` with the caller's tenant and the run as resource and fails closed for an untrusted principal. One signal per run serves the scope row, the banner and the incident dialog.
- **Follow-up, not in M4:** the diagnostics page's `canManage` is always `true`. It should use the same service; filed as a separate task, not changed here.

## R13 — Where the actions sit in Studio

- **Evidence:** the run view's recovery cell already links a waiting row to its incident (`graph-authoring/CompensationScopeDesigner.tsx:402-435`). The incident list and dialog live in `workflow-editor/WorkflowInstances.tsx` (`WorkflowIncidentList` at :2923, `IncidentDetailsDialog` at :3160). The run view confirms destructive actions with `getDialogs().confirm` (`WorkflowInstances.tsx:312`); the dialog API also has `prompt`, a single text field.
- **Decision:**
  - **Scope row** (state Needs action): Retry and Resolve buttons next to "Show incident".
  - **Scope banner** (phase Needs action): "Abandon recovery".
  - **Incident dialog**, for any incident waiting for intervention: Retry (the #2747 button, for any activity), and the offered decisions with labels a module contributes (compensation contributes its three). An incident whose decisions have no label shows the code.
  - A new `OperatorActionDialog` component holds target, consequence, outcome choice and reason. `confirm` and `prompt` cannot hold a choice plus a required reason.

## R14 — Submitting and polling in Studio

- **Evidence:** submission returns 202 with `submissionDisposition` `Accepted` or `Replayed` and plan links (`Workflows/Runtime/Api/Models/Alterations/WorkflowAlterationViews.cs:6-15, 136-142`); the `Idempotency-Key` header is required (`Workflows/Runtime/Api/Endpoints/AlterationPlans/Submit/Endpoint.cs:30-34`); plan and job statuses are in `Workflows/Runtime/Core/Models/Alterations/WorkflowAlterationModels.cs:201-227`; a job's outcomes carry a safe code and message (`WorkflowAlterationViews.cs:91-121`). Studio has no alteration client; the capability links exist when the feature is composed (`Workflows/Runtime/Api/Capabilities/RuntimeApiCapabilities.cs:64-86`).
- **Decision:**
  - A new client `api/alterations.ts`: submit, get plan, page jobs, get job, through capability links.
  - The idempotency key is `studio-{uuid}`, minted when the dialog opens and reused for any resend of that submission.
  - Poll the plan at 1 s, backing off to 5 s, for up to 2 minutes; then read the job and its outcome. After success, poll the run (journal or details) until the incident is resolved and the entry changed, showing "Applying…", since the decision reaches the scope in a later work item.
  - The pending plan id is kept per run in session storage, guarded by try/catch; a reload resumes polling and never resubmits (FR-423).
  - Errors map to messages in [contracts/studio-interaction.md](contracts/studio-interaction.md).

## R15 — Audit fields and history

- **Evidence:** the retry outcome carries `runtime.alteration.jobId`, `planId`, `submittedBy` and `reason` (`RetryFaultedActivityAlterationHandler.cs:203-249`); outcome metadata is not behind the sensitive grant (`WorkflowExecutionViews.cs:594-604`); the job view carries the submitter (`WorkflowAlterationViews.cs:91-107`).
- **Decision:** `OperatorDecision` outcomes carry the same four keys plus `runtime.interventionDecision` and `runtime.interventionRequestedBy`; the outcome's `appliedAt` is the time. The prior `WaitForIntervention` kind and source move to incident metadata, as for a retry. Studio builds the history from incidents (OD14): a retry is associated with the scope by its activity (a handler or step execution of the scope, or a descendant of one), a decision by `runtime.interventionRequestedBy`. The scope's private state keeps only what its transitions need (outcome and incident id), so incidents stay the single audit record.

## R16 — Scope state version 4

- **Evidence:** the persister reads versions 1 to 3 and writes 3 (`CompensationScopeStatePersister.cs:280-305`); statuses and phases are persisted by name (`CompensationScopeState.cs:5-42`).
- **Decision:** version 4 adds phase `Abandoned`, entry status `ResolvedManually`, entry field `resolution`, and state field `abandonment`. The v3 → v4 upcast adds nothing (the new fields are optional), but the version still moves, so a version-3 reader never meets a status it cannot name (the same reason as spec 251 R5). See [data-model.md](data-model.md).

## R17 — Reasons are visible to readers

- **Decision:** the reason is outcome metadata, readable by anyone who can inspect the run's structure, as for a retry today. The dialog says so ("Visible to everyone who can view this run; do not include secrets"). The reason is trimmed, 1 to 500 characters, without control characters (as `RetryFaultedActivityAlterationHandler.cs:251-273`).

## R18 — Implementation notes from PR 1

Recorded while building Phases A and B; each one corrects or sharpens the packet against the code.

- **The decision evaluation may notify an ancestor.** A resolve `EffectAccepted` on the last step completes the scope `Done`, and a completing scope hands its entries over with an ancestor notification (`CompensationScope.HandOver`, spec 258 R-2). The delivery evaluation therefore accepts ancestor notifications with a `Complete` continuation, exactly as a child-completion evaluation does (`Activities/Runtime/Elsa.Activities.Runtime/Services/StructuralCallbackEvaluation.cs`). The contract's list of what the callback may do now names it.
- **One evaluation helper for notifications and decisions.** `StructuralCallbackEvaluation` holds the reactivation and commit both `WorkflowNotifyParentActivitySchedulerWorkHandler` and `WorkflowInterventionDecisionSchedulerWorkHandler` use (T405). It refuses a child-fault intervention staged in either evaluation, where a notification evaluation used to drop it silently; no consumer staged one.
- **"Parent not running" for FR-405** is the parent's committed status other than `Running`, the same gate the parent-completion and notification handlers use before they consume a child's outcome: a parent in any other status never receives the successor's outcome. A source without a parent (a root activity) is not refused.

