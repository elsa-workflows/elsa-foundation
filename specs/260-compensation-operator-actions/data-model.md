# Data model: operator actions on compensation recovery (M4)

Extends [spec 258 data-model.md](../258-compensation-nesting/data-model.md). The authored structure and the scope inputs are unchanged. No new table: decisions live on incidents, alteration plans and the scope's private state.

## Runtime constants (new)

| Constant | Value | Where |
|---|---|---|
| `IncidentResolutionActionKinds.OperatorDecision` | `OperatorDecision` | `Workflows/Runtime/Core/Constants/IncidentResolutionActionKinds.cs` |
| `IncidentResolutionSystemSources.OperatorDecisionAlteration` | `OperatorDecisionAlteration` | `Workflows/Runtime/Core/Constants/IncidentResolutionSystemSources.cs` |
| `WorkflowExecutionCommandKind.DeliverInterventionDecision` | `19` | `Workflows/Runtime/Core/Models/WorkflowExecutionCommand.cs` |
| `DecideInterventionAlterationHandler.Kind` | `DecideIntervention`, schema version `1` | reserved built-in kind in `WorkflowAlterationRegistry` and `WorkflowAlterationServiceCollectionExtensions` |

## Runtime metadata keys (new)

| Key | On | Set by | Read by |
|---|---|---|---|
| `runtime.interventionDecisions` | `WaitForIntervention` outcome metadata | the runtime, from the requester's offer (FR-401); a JSON array of codes | `DecideIntervention/1` preflight, Studio |
| `runtime.interventionRequestedBy` | `WaitForIntervention` and `OperatorDecision` outcome metadata (it is already on incident metadata) | the runtime | preflight, Studio history |
| `runtime.interventionDecision` | `OperatorDecision` outcome metadata | `DecideIntervention/1` | Studio history |
| `runtime.alteration.jobId`, `.planId`, `.submittedBy`, `.reason` | `OperatorDecision` outcome metadata (as on `OperatorRetry`) | `DecideIntervention/1` | Studio history |
| `runtime.interventionDecisionProcessedWorkItemId` | the requester's activity metadata | the delivery handler | the delivery handler (repeat delivery) |

## Incident outcomes (additions)

| Situation | Status | Action kind | System source | Outcome metadata |
|---|---|---|---|---|
| Scope waits on a handler or uncertain step (M2, now with an offer) | `Blocking` | `WaitForIntervention` | `StructuralIntervention` | M2 keys, plus `runtime.interventionDecisions`, `runtime.interventionRequestedBy` |
| Operator resolved (either outcome) | `Resolved` | `OperatorDecision` | `OperatorDecisionAlteration` | alteration keys, `runtime.interventionDecision` = `compensation.resolve.effect-absent` or `…effect-accepted`, `runtime.interventionRequestedBy` |
| Operator abandoned | `Resolved` | `OperatorDecision` | `OperatorDecisionAlteration` | as above, `runtime.interventionDecision` = `compensation.abandon` |
| Scope abandoned (its own fault) | `Blocking`, then as its parent or the strategy decides | — | — | fault code `compensation.scope.abandoned`, category `outcome-unknown` |

On every operator outcome the prior kind and source move to incident metadata (`runtime.priorResolutionActionKind`, `runtime.priorResolutionSystemSource`), as `RetryFaultedActivity/1` does.

## Decision codes (compensation)

| Code | Offered when | Effect |
|---|---|---|
| `compensation.resolve.effect-absent` | the scope waits on a handler or an uncertain step | entry `ResolvedManually`, outcome `EffectAbsent` |
| `compensation.resolve.effect-accepted` | same | entry `ResolvedManually`, outcome `EffectAccepted` |
| `compensation.abandon` | same | phase `Abandoned`, scope faults |

## Private state — type `Elsa.CompensationScope.State`, version `4`

```json
{
  "phase": "Compensating",
  "nextStepIndex": 3,
  "nextOrdinal": 4,
  "cause": { "kind": "fault", "step": "Confirm order", "activityExecutionId": "ae-7", "incidentId": "inc-1" },
  "entries": [
    {
      "id": "e2",
      "step": "Charge card",
      "ordinal": 2,
      "status": "ResolvedManually",
      "forwardOutcome": "Completed",
      "operationKey": "ae-scope/step/1",
      "forwardActivityExecutionId": "ae-5",
      "handlerAttempts": [
        { "activityExecutionId": "ae-9", "incidentId": "inc-3", "result": "Faulted" },
        { "activityExecutionId": "ae-10", "incidentId": "inc-4", "result": "Faulted" },
        { "activityExecutionId": "ae-11", "incidentId": "inc-5", "result": "Faulted" }
      ],
      "interventionIncidentId": null,
      "resolution": { "outcome": "EffectAbsent", "incidentId": "inc-5" }
    }
  ],
  "confirmations": [],
  "receivedHandOvers": [],
  "handOversInProgress": [],
  "abandonment": null
}
```

| Field | Change from v3 |
|---|---|
| `phase` | adds `Abandoned`. |
| entry `status` | adds `ResolvedManually`. |
| entry `resolution` | new, optional: `{ outcome: EffectAbsent \| EffectAccepted, incidentId }`. Set with `ResolvedManually`, never changed after. |
| entry `interventionIncidentId` | cleared when the entry is resolved; the incident id moves to `resolution.incidentId`. |
| `abandonment` | new, optional: `{ incidentId, entryId, fromPhase }`. Set once with phase `Abandoned`; `fromPhase` is `Running` (a waiting step) or `Compensating` (a waiting handler). |

Who, why and when are not in private state: the incident outcome is the audit record (research R15).

## Upcast v3 → v4

Every v3 field is kept and nothing is added: `resolution` and `abandonment` are optional and absent. No v3 status changes meaning. The persister reads versions 1 to 4, upgrading 1 → 2 → 3 → 4 in a chain, and writes 4. Version 4 is never downgraded.

## Entry status transitions (additions)

| From | Event | To |
|---|---|---|
| `Failed` (handler waiting) | resolve decision | `ResolvedManually` with the outcome |
| `Uncertain` (step without handler, waiting) | resolve decision | `ResolvedManually` with the outcome |
| any | abandon decision | unchanged |

| Phase from | Event | Phase to |
|---|---|---|
| `AwaitingIntervention` (was `Compensating`) | resolve | `Compensating`, then `Compensated` or faulted as in spec 251 |
| `AwaitingIntervention` (was `Running`) | resolve `EffectAbsent` | `Compensating` |
| `AwaitingIntervention` (was `Running`) | resolve `EffectAccepted` | `Running`, then `Completed` as in spec 258 |
| `AwaitingIntervention` | abandon | `Abandoned` (scope faults) |

## Eligibility (unchanged rule, new members)

- Eligible for recovery: `Registered`, and `Uncertain` with a handler (spec 251). `ResolvedManually` never is.
- Confirmable and handed over: `Registered`, `NoHandler`, `Uncertain` (spec 258). `ResolvedManually` never is.

## Journal view (`compensation.journal`, version 1)

The projection the state view endpoint returns; see [contracts/journal-and-permissions.md](contracts/journal-and-permissions.md). It is computed from the private state, the pinned scope node (to resolve own handlers and the remaining order) and the run's incidents (offered decisions, waiting status). It holds no captured value.

## Studio view model additions

As shipped in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/compensationRuntime.ts` and `workflowTypes.ts`:

- `CompensationRecoveryState` adds `Retrying` (a handler running again after a failed attempt), `ResolvedAbsent` and `ResolvedKept` (an entry an operator settled without running it, as effect absent or effect accepted) and `NotUndone` (an entry an abandoned recovery left as it was). A row carries no separate resolution field; the outcome is its recovery state.
- `CompensationScopeRunPhase` adds `Abandoned`.
- `CompensationRowRuntime` gains `entryId?` and `operationKey?` (when the row comes from the journal) and `actions?: CompensationRowActions`, present while the row waits for an operator.
- `CompensationRowActions`: `{ incidentId, sourceActivityExecutionId?, decisions: string[], target: "handler" | "step" }`. `sourceActivityExecutionId` is the faulted activity a retry runs again; `target` says whether the wait is on a handler out of attempts or on a forward step with no handler. Studio shows the actions only when the run says the caller may manage it.
- `CompensationRecoveryCause` gains `messageWithheld?`, set when the journal withheld the failure message (FR-415).
- `CompensationScopeRuntime` gains `source?: "journal" | "derived"`, `journalUnavailable?: "StateCleared" | "Unavailable"` (why a derived view has no journal), `scopeLabel?` (for action labels), `enclosingScope?: CompensationScopeReference` (the scope that decides next if this one faults), `remaining?: Array<{ step, handlerNodeId? }>` (the steps still to undo, in recovery order) and `history?: CompensationOperatorHistoryEntry[]`. It has no abandonment field: an abandoned recovery is the `Abandoned` phase, and the journal's own `abandonment` stays in the `CompensationJournal` read model.
- `CompensationOperatorHistoryEntry`: `{ incidentId, kind: "Retry" | "ResolvedAbsent" | "ResolvedKept" | "Abandoned" | "Decision", decision?, actor?, appliedAt?, reason?, step?, notApplied? }`, newest first, read from the incidents the actions resolved (OD14). `Retry` is an operator retry; `Decision` is any decision code other than the three compensation ones, carried in `decision`; `notApplied` marks a decision the scope never applied because it was cancelled first.
- `WorkflowInstanceDetails` gains `permissions?: WorkflowInstancePermissions | null` (`{ canManage: boolean; canInspectValues: boolean }`); absent means "cannot manage".
