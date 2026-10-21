# Contract: Studio operator interaction

Extends [spec 250 contracts/studio-interaction.md](../../250-native-compensation/contracts/studio-interaction.md) and the M2/M3 run-view states. Studio is the workflows module (`Studio/src/essentials/Elsa.Studio.Workflows/Client`).

## Data sources

| What | Source | Fallback |
|---|---|---|
| Journal of a live scope | `activity-execution-state-view` (view `compensation.journal/1`) | M1–M3 derivation in `compensationRuntime.ts`, marked "Derived from run data" |
| Completed scope | M1–M3 derivation | — |
| May act | run details `permissions.canManage` and the `workflow-alteration-plans` link | no actions |
| History | run incidents with outcome `OperatorRetry` or `OperatorDecision` | — |
| Captured values | activity-execution inspection of the selected occurrence | withheld, as today |

## States (text plus icon)

| State | Label | Icon (lucide) |
|---|---|---|
| Pending | Waiting to undo | Hourglass |
| Running, attempt 1 | Undoing | CircleDot |
| Running, attempt > 1 | Retrying · Attempt n of m | RotateCw |
| Forward uncertain | Uncertain | AlertTriangle |
| Compensated | Compensated (nth) | Undo2 |
| Confirmed | Confirmed | BadgeCheck |
| NeedsAction | Needs action · Attempt n of m | Hand |
| ResolvedAbsent | Resolved: effect absent | UserCheck |
| ResolvedKept | Resolved: effect kept | UserCheck |
| NotUndone | Not undone (recovery abandoned) | CircleSlash |
| Action submitted | Retry submitted / Resolve submitted / Abandon submitted | Loader |
| Applying | Applying… | Loader |
| Scope phase Abandoned | Abandoned | OctagonX |

Existing states keep their labels and icons; the attempt suffix keeps the M2 form with the recovery order, for example "Needs action (1st) · Attempt 3 of 3" (research R19). Each chip's accessible name reads "Recovery: {label}".

## Where the actions are

| Place | Shown when | Actions |
|---|---|---|
| Scope row, recovery cell | row state NeedsAction | **Retry**, **Resolve…** (next to "Show incident") |
| Scope row, forward cell | forward Uncertain and the step waits (no handler) | **Retry**, **Resolve…** |
| Scope banner | phase Needs action | **Abandon recovery…** |
| Incident dialog | incident `Blocking` with `WaitForIntervention` | **Retry** (any activity); one button per offered decision |

Every action is hidden unless `canManage` is true, the alteration links are advertised, and the scope is not cancelled. Resolve and Abandon are also hidden when the wait offers no decisions. Buttons have accessible names that include the step: "Retry the compensation of Reserve delivery", "Resolve Reserve delivery", "Abandon recovery of Order".

## Confirmation dialog (`OperatorActionDialog`)

One dialog for the three actions: title, a target block, a consequence block, a choice (Resolve only), a reason field, and Cancel / confirm. Focus starts on the first field; Escape cancels; the confirm button stays disabled until the form is valid.

**Retry**

```
Retry the compensation of "Reserve delivery"?

Target   Scope "Order" › step "Reserve delivery" › compensation "Cancel delivery"
         Attempt 4 (limit 3 reached) · incident inc-5 · run wf-42
Effect   Runs "Cancel delivery" again with the same operation key (ae-scope/step/1/compensate).
         If it succeeds, recovery continues with "Release inventory". If it fails, the scope waits again.

Reason (optional)  [                                   ]
Visible to everyone who can view this run. Do not include secrets.
                                                     [Cancel] [Retry]
```

**Resolve**

```
Resolve "Reserve delivery" without running it?

Target   Scope "Order" › step "Reserve delivery" · incident inc-5 · run wf-42
Outcome  ( ) Effect absent or undone — the reservation does not exist (never made, or undone outside Elsa).
         ( ) Effect present, keep it — the reservation exists and stays.
Effect   On a compensation: recovery continues with "Release inventory".
         On a step, "Effect absent": the completed steps are undone, newest first.
         On a step, "Effect present": the scope carries on with the next step.
         The entry is never undone afterwards.

Reason (required)  [                                   ]
                                                     [Cancel] [Resolve]
```

The effect lines shown are the ones that apply to the row; the dialog never shows the others.

**Abandon**

```
Abandon recovery of scope "Order"?

Target   Scope "Order" (occurrence ae-scope) · waiting on incident inc-5 · run wf-42
Effect   No further compensation runs. These stay not undone:
           • Reserve delivery (undo failed)
           • Reserve inventory (waiting to undo)
         The scope fails with compensation.scope.abandoned. Inside "Fulfilment", that scope decides next;
         otherwise the run's incident strategy does (by default the run fails).

Reason (required)  [                                   ]
                                                     [Cancel] [Abandon recovery]   (danger tone)
```

## Submission and polling

1. Mint `Idempotency-Key = studio-{uuid}` when the dialog opens. Keep it for any resend of that submission.
2. `POST` the plan. Store `{ planId, kind, incidentId, key }` in session storage under the run id (try/catch).
3. Poll `workflow-alteration-plan` at 1 s, backing off to 5 s, for up to 2 minutes, until `Completed`, `CompletedWithFailures`, `Failed` or `Cancelled`.
4. Read the job (`workflow-alteration-plan-jobs-page`, then `workflow-alteration-job`). `Succeeded`: show "Applying…" and refresh the run and journal until the incident is resolved and the row changed, then clear the stored plan. `Failed`: show the outcome message below, refresh, clear.
5. On reload, a stored plan resumes at step 3. It is never submitted again.
6. A `Replayed` disposition is treated like `Accepted`.

## Error mapping

| Source | Message |
|---|---|
| HTTP 401 | "Your session has expired. Sign in again to act on this run." |
| HTTP 403 | "You do not have permission to change this run." Studio then refreshes the run details and hides actions if `canManage` is now false. |
| HTTP 404 on the run | "This run is unavailable or you do not have access to it." |
| HTTP 404 or another failure reading the journal | No message: the view is derived from run data and says "the scope's journal is not available" |
| HTTP 409 `AlterationIdempotencyConflict` | "This action was already submitted with different details. Refresh and try again." |
| HTTP 429 | "The runtime is busy. Try again in {Retry-After} seconds." |
| `NoBlockingIncident`, `InterventionNotPending`, `SourceActivityNotFaulted` | "Someone else already acted on this. The run has been refreshed." |
| `IncidentNotAwaitingIntervention` | "The runtime has not finished deciding what to do with this failure. Try again shortly." |
| `SourceWorkClaimed`, `RequesterWorkClaimed` | "The scope is busy. Try again in a moment." |
| `SourceParentNotRunning`, `RequesterNotRunning` | "The scope is no longer running, so it cannot be changed." |
| `DecisionNotOffered` | "This action is not available for this failure." |
| `WorkflowTerminal` | "The run has ended; it cannot be changed." |
| `TargetNotFound` | "This run is unavailable for operator actions." |
| `InvalidDecideInterventionPayload`, `InvalidRetryFaultedActivityPayload` | "Check the reason: 1 to 500 characters, no control characters." |
| Polling timeout | "The action is still queued. Studio will keep checking while this page is open." |
| Not shown 60 s after the job succeeded | "The runtime accepted the action, but the run does not show it yet. Refresh to check again." |
| Anything else | "The action could not be applied ({code})." |

A refused action changes nothing; the message says so where it helps ("Nothing was changed").

## History

The scope's **Operator actions** list, newest first, one row per incident resolved by an operator:

```
Resolved — effect absent · bob · 20 Oct 11:05 · "Refunded manually, ticket FIN-221" · Show incident
Retry · alice · 20 Oct 10:42 · "Delivery service restored" · Show incident
```

- Kind and outcome come from the action kind and `runtime.interventionDecision`; actor from `runtime.alteration.submittedBy`; time from the outcome's `appliedAt`; reason from `runtime.alteration.reason` ("No reason given" when absent).
- A retry belongs to the scope when its incident's activity is a step or handler occurrence of the scope, or a descendant of one; a decision when `runtime.interventionRequestedBy` is the scope.
- A decision whose entry the journal still shows waiting, on a cancelled scope, reads "Decision not applied: the scope was cancelled first".

## Navigation and reload

- The run view keeps the selected occurrence in the URL (`?selected={activityExecutionId}`), replacing component-only state (`WorkflowInstances.tsx:719`). A reload or a deep link opens the same scope and occurrence. The older `?activityExecutionId=` deep link is still read.
- After an action, focus returns to the row's action button, or to the row's state chip if the button is gone.

## Accessibility

- The dialog is a labelled modal; the outcome choice is a radio group with a legend; the reason field has a visible label and an error message linked by `aria-describedby`.
- A live region announces "Retry submitted", "Applying…", the result and any refusal.
- Axe passes at 390 and 1200 px, light and dark, for: a waiting scope with actions, each dialog, a resolved row, an abandoned scope, the history list, and the read-only view.
