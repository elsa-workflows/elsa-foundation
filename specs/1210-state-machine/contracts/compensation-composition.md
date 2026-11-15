# StateMachine and native compensation composition

**Status:** proposed M0/OD47 qualification topology, 15 November 2026. This is source-grounded design,
not an implemented example, executed witness or product agreement. [OD47](../acceptance-matrix.md) requires
real Studio authoring and a durable run/restart before delivery. General root/nested StateMachine support
and all other accepted requirements remain unchanged.

The [native compensation program](../../../docs/program-goals/native-compensation.md) retains its authority.
Use its existing [scope contract](../../250-native-compensation/spec.md),
[uncertainty rules](../../251-compensation-uncertainty/spec.md),
[nesting/ancestor boundaries](../../258-compensation-nesting/spec.md),
[operator decisions](../../260-compensation-operator-actions/spec.md) and
[qualification](../../261-compensation-qualification/spec.md). This adds no Saga type, compensation engine,
workflow-wide undo registry or StateMachine-owned recovery journal. Example business labels belong in the sample.

## Candidate authored topology

Keep one compensation scope active while the machine waits, revises and reaches its business disposition.
The baseline uses an inline nested machine and the normal scope inputs `faultAfterCompensation=false` and
`confirmOnComplete=false`; these are explicit example settings, not new defaults.

```text
CompensationScope
  Step Reserve:   ReserveResource                 handler: ReleaseResource
  Step Lifecycle: Flowchart
                    StateMachine -- Approved --> Confirm -- finish Lifecycle
                                 -- Withdrawn/Expired -----> finish Lifecycle
  Step Settle:    Compensate                      direct step child
```

`ReserveResource` and `ReleaseResource` represent real sample participants, with the normal forward operation
key and handler target key. Reserve is a direct step. Lifecycle performs control, waits and ordinary local-value
work; its baseline contains no further external business effect. Its machine-level final states explicitly map
to declared normal activity outcomes (OD45); matching state labels alone do not create those outcomes.
Capture the business disposition through normal values/results so it remains distinct from the scope outcome.

The Approved branch runs ordinary `Confirm`, then finishes Lifecycle. Other terminal business branches finish
Lifecycle without confirmation. Both controls target the nearest enclosing scope in this baseline, with no
intervening compensation or reusable-activity boundary. Settle is `Compensate` itself in the scope's direct
forward slot, with no Sequence, Flowchart or other wrapper around it.

| Business path | Required effect/journal observation | Scope outcome |
|---|---|---|
| Approved | Reservation stays present; Reserve is confirmed; no release handler runs. | `Compensated` (no handler runs) |
| Withdrawn or expired | Unconfirmed reservation is released using its exact forward operation key. | `Compensated` |

`Compensated` reports completion of the explicitly requested recovery. The chart's business disposition and
the journal's confirmed/compensated entries explain what happened; this scope outcome alone cannot indicate
that every path undid an effect. Do not add an empty handler to suppress an uncertain-step fault.

## Why the direct step and active lifetime matter

The claims below were checked at source basis `21ceeea309fedfed1b3074506758cd0503fe00aa`.
Source links follow the current branch; refresh that comparison if the linked implementation changes.
At that basis,
[CompensationScope](../../../Essentials/src/Activities/Compensation/Activities/CompensationScope.cs)
handles a request whose notifying child is the direct forward child as an already-completing `Compensate`:
there is no stopped wrapper to journal as uncertain. A request from inside a running composite instead stops
that step and records it uncertain; a missing handler can end with `compensation.scope.uncertain-unhandled`.
The existing [nesting journeys](../../../Essentials/tests/Activities/Compensation/Tests/CompensationNestingJourneyTests.cs)
distinguish these cases. Their source is a reference here, not a fresh execution of this example.

Studio authoring, normal publication and execution must preserve Settle's exact direct-child shape and the
normal ancestor-notification/completion ordering. An automatically introduced wrapper changes the contract.
Qualify that fact in the real published graph; do not change compensation semantics to accommodate a renderer.

The outer scope remains active throughout Lifecycle, retaining its normal private journal across waits and
restarts. A completed inner scope without an enclosing recipient does not retain future workflow-wide undo
eligibility. After scope completion, inspection uses only the configured normal retained evidence; completion
does not promise indefinite private-state or forensic retention.

Keep scope and ancestor controls within the same reusable-activity boundary. Any variant using a reusable
machine call must separately qualify its exact version, return/outcome mapping and control placement outside
that call. Confirm, Compensate and journal handover do not acquire cross-boundary authority from this proposal.

## Runtime and fault-owner gates

The actual [Compensate](../../../Essentials/src/Activities/Compensation/Activities/Compensate.cs) and
[Confirm](../../../Essentials/src/Activities/Compensation/Activities/Confirm.cs) use normal ancestor notifications.
The private kernel Native prototype currently has no compensation implementation or equivalent ancestor port;
at the stated basis its [StateMachine final path](../../../prototypes/kernel-model/models/Statecharts.Native/StateMachine.cs)
completed with default `Done`. The later [private mapping profile](../../../prototypes/kernel-model/models/Statecharts.Run/README.md#private-final-outcome-transport)
proves named single-outcome transport through ordinary Native completion, not the product declared-outcome
references/publication contract of OD45 or this composition. B7b alone supplies neither. Reuse the authoritative mechanisms on the selected runtime and qualify the bridge before adoption;
copying a compensation journal/engine into the spike would not prove that integration.

Phase-fault ownership remains an explicit integration gate. Trace the chosen normal runtime's in-chart
intervention boundary and the enclosing scope's descendant-fault absorption. One failure cannot simultaneously
mean pending chart retry and completed parent compensation. If a parent legitimately absorbs the fault and
cancels the chart, inspection and receipts must show that disposition. Preserve both owners' existing semantics;
obtain their applicable review before choosing the final profile or claiming this sample qualified.

| Adverse path | Required distinction |
|---|---|
| Machine phase fault | Observe the actual normal owner: retained selected-phase recovery versus parent absorption/cancellation. Do not infer it from this topology. |
| Reserve outcome unknown | Use existing uncertainty/recovery rules and stable operation keys; lack of a response is not proof the resource is absent. |
| Release fails or waits for intervention | Preserve the compensation journal and normal retry/operator boundary; recovery cannot restart the completed business lifecycle. |
| Workflow cancellation | Cancellation initiates no compensation handler. Effects or handlers committed before the cancellation remain historical facts. |
| Scope already completed | Do not invent later undo eligibility or a global journal from the machine's saved values. |

## Qualification before adoption

All following journeys and controls are planned, not executed results:

1. Author, save/reload and publish the graph through Studio. Inspect declared normal outcomes, direct Reserve
   and Settle children, handler affiliation and the exact executed version. Keep pending editor work under the
   [normal authoring contract](studio.md#authoring-identity-and-pending-work).
2. Hold Review across real waits, revisions and visit-owned deadlines. Restart the actual process/provider while
   the outer scope remains active; prove the same reservation, forward key and journal survive without replaying
   completed work. Ordinary triggers/source-wave and invocation-admission guarantees retain their separate gates.
3. Prove approval leaves the resource present with zero release calls. Prove withdrawal/deadline makes it absent
   using the exact forward key. Inspect real participant state and normal journal evidence; handler-order logs
   alone are insufficient. Physical retries may repeat calls under the existing idempotency contract.
4. Exercise unknown outcomes, handler failure/retry, operator intervention and workflow cancellation at declared
   commit cuts. Qualify phase-fault ownership separately and record failed, skipped and unrun cases explicitly.
5. Inspect chart configuration, pending/completed phases, business receipts and the separate scope journal/recovery
   owner through composed Studio against retained evidence. Prove same-boundary reuse constraints and refusal of
   unsupported cross-boundary requests. Passing source fixtures or a mockup cannot replace these journeys.

Meaningful negative controls must detect a wrapped Settle, missing Confirm, premature loss of the active journal,
wrong target operation key, completion ahead of the ancestor request, mislabelled parent absorption and forbidden
reusable-boundary traversal. Require named semantic/ledger assertions, not generic exceptions or successful logs.

Q1/Q2/Q3/M0, source-owner review, actual independent Astra High architecture review, delegated product approval
and real StateMachine/Studio/provider qualification remain open. This proposal closes none of those gates.
