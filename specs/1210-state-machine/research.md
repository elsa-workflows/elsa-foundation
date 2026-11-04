# StateMachine research and open architecture

**Status:** Read-only source observations and proposed decision path, 4 November 2026. No architecture is accepted by this record and no runtime experiment has run.

## Reference evidence

- [Elsa 3.8 Program #1458](https://github.com/elsa-workflows/elsa-foundation/issues/1458), [PRD #1456](https://github.com/elsa-workflows/elsa-foundation/issues/1456) and [historical semantics #1457](https://github.com/elsa-workflows/elsa-foundation/issues/1457) are completed Elsa 3 evidence, not Elsa 4 delivery.
- [Original request #5085](https://github.com/elsa-workflows/elsa-core/issues/5085), [runtime PR #8010](https://github.com/elsa-workflows/elsa-core/pull/8010) and [historical Studio PR #958](https://github.com/elsa-workflows/elsa-studio/pull/958) provide the context for ordinary trigger/action activities and the authoring experience.
- [Demonstration #1462](https://github.com/elsa-workflows/elsa-foundation/issues/1462#issuecomment-5473109632) records actual Elsa 3 Studio save/reload/publish and an ordered runtime event/wait journal. It does not prove Elsa 4 restart/race behavior.
- [Community discussion #38](https://github.com/elsa-workflows/elsa-core/discussions/38) records lifecycle actions, competing timers/manual transitions and available-action inspection. It motivates sample journeys without introducing approval concepts into Elsa.
- [WF4 state machines](https://learn.microsoft.com/en-us/dotnet/framework/windows-workflow-foundation/state-machine-workflows) and [SCXML](https://www.w3.org/TR/scxml/) are semantic references. Hierarchy, parallel behavior and action-without-exit have informed the interview; full reference conformance/interchange is uncommitted.

## Elsa 3 observations

The [Elsa 3 source snapshot](https://github.com/elsa-workflows/elsa-core/tree/28091a9d7b5f29fe9c5dbf2a9ef373d107fae755/core/src/modules/Elsa.Workflows.Core/Activities/StateMachine) was inspected read-only under its repository/Studio instructions. The inspected runtime and StateMachine unit-test paths were clean against that commit when verified.

StateMachine schedules ordinary activity triggers after entry. Its current trigger callback re-arms only the completed/rejected trigger. Competing waits are preserved until a winning transition cancels them. The source contains explicit triggerless handling and lifecycle callbacks; the historical semantics record documents shared-trigger identity rejection and the retained JSON boundary.

Those observations informed OD13–OD14 but do not impose an Elsa 3 schema or callback/tag implementation on Elsa 4. Shared identity, unknown-condition JSON handling and declaration-order continuation tags require their own Elsa 4 decisions if adopted.

## Current Elsa 4 source seams

The initial inspection used source inherited from commit 2e2203504a8dfd1cd303f3583c400acc32fb8a47. These are observations of available contracts, not proof that the complete capability is implementable without an extension.

| Surface | Observed capability | Limit of the evidence |
|---|---|---|
| [Flowchart](../../Essentials/src/Activities/Flowchart/Activities/Flowchart.cs) | Native structural activity with child completion/fault callbacks | Flowchart behavior is not StateMachine semantics |
| [Structural continuation](../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeStructuralContinuation.cs) | Typed/versioned private state staged for a runtime-owned checkpoint | Does not itself prove bounded external machine-event admission |
| [Runtime activity context](../../Essentials/src/Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs) | Child scheduling, live-child inspection and staged subtree cancellation | Full hierarchy/region selection, phases and recovery still need design |
| [Model hosting](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Extensions/ModelHostingServiceCollectionExtensions.cs) | Opt-in model dispatch through existing mailbox/scheduler/checkpoint authority | Existence of another hosting seam does not justify another authored definition domain |
| [Protocol driver](../../Essentials/src/Workflows/Runtime/Core/Contracts/IRuntimeExecutionProtocolDriver.cs) and [model state](../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeModelExecutionState.cs) | Model-owned input handling, exact material identity, committed model state and retained input receipts | No StateMachine driver/contract or ordinary-slot integration has been proved |
| [Event](../../Essentials/src/Activities/Primitives/Activities/Event.cs) and [Delay](../../Essentials/src/Activities/Scheduling/Activities/Delay.cs) | Existing named-event waits and exact-invocation durable timers | Machine-directed queued events are not interchangeable with an exact retired wait completion |
| [Saved recipients](../../Essentials/src/Workflows/Runtime/Core/Models/StimulusDeliverySnapshot.cs) | Resume delivery captures bookmark/activity/executable identity and validates live suspension | Retargeting a saved completion to a future visit is invalid |

Existing child-subtree cancellation and BPMN event-based gateway test sources exercise relevant generic scenarios. Their presence is not newly executed StateMachine evidence.

## Architecture decision still required

OD16 chooses normal workflow/activity authoring and publication ownership. It does not choose the internal interpreter/hosting mechanism.

Compare a native composite implementation, an internal model-hosting fit and a simpler sufficient alternative against the same acceptance matrix. Prefer the smallest fit that supports the accepted behavior. Reuse existing activity, scheduler, delivery, identity, correlation, publication and persistence authorities; identify genuinely StateMachine-owned semantic state separately.

Before freezing placement, propose and qualify a bounded witness:

1. Select a transition whose action durably waits.
2. Admit a new machine-addressed event under an explicit bound and distinguish admission from evaluation.
3. Restart an owned host/store and recover the selected phase plus retained input.
4. Finish selected work, evaluate the retained event under the declared order and retain its handled/unhandled outcome.
5. Deliver an obsolete timer/task completion and prove it cannot act on a new state visit.
6. Identify the exact existing contracts used and any reviewed shared extension required. Do not substitute a competing scheduler or independently acknowledged delivery authority to make the witness pass.

The witness has not been implemented or executed. Its technical plan and any architecture-affecting changes require the applicable agreement first.

## Additional integration constraints

### Automatic-cycle protection is not proved by drain limits

OD19 requires automatic progress before queued business-event evaluation and protection against an indefinitely running automatic cycle. The existing [drain options](../../Essentials/src/Workflows/Runtime/Core/Models/WorkflowDrainOrchestratorOptions.cs) and [drain orchestrator](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Scheduler/WorkflowDrainOrchestrator.cs) bound generic execution for their own purposes: the no-progress count is reset by newly dispatched work IDs, the total-cycle limit is optional, and each drain creates its own cycle budget.

Consequently, those source counters alone do not prove a semantic bound for a StateMachine automatic cycle that keeps producing work or spans durable continuations/restarts. The M0 plan must identify the protection boundary, lifetime, diagnostic and recovery contract, reuse suitable runtime controls and prove the accepted bound. This is an unproved integration requirement, not a reported defect in the generic drain contract.

OD32 extends the internal-progress ordering to completion reactions; OD33 requires detected excessive automatic/completion progress to stop, retain committed progress and expose an actionable incident requiring normal recovery without automatic repetition. These accepted outcomes do not establish the concrete detection limit/lifetime or a sufficient incident/recovery integration. The source-grounded technical design must specify and prove those details without creating competing runtime authorities.

### Existing owners remain authoritative

OD23 requires recovery at the failed activity boundary while preserving earlier completed phases. The [structural recovery opt-in](../../Essentials/src/Workflows/Runtime/Core/Contracts/IRuntimeStructuralActivity.cs), `IRuntimeAcceptsRescheduledFaultedChild`, applies only when a running parent still tracks a faulted child and has not absorbed its fault. The [operator retry handler](../../Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Alterations/Handlers/RetryFaultedActivityAlterationHandler.cs) retains scheduling provenance and operation keys for the successor and rejects unsupported source/incident states. These are candidate integration contracts, not evidence that StateMachine recovery already works. The witness must cover nested ordinary activity work, the selected phase, preserved completed phases and restart without weakening existing recovery eligibility or external-effect guarantees.

The existing [Native Compensation and Saga Recovery program](../../docs/program-goals/native-compensation.md) retains its semantics and work. Cancellation/rearming does not undo participant effects. Assess composition through its actual contracts rather than implementing another saga authority.

Initiative #3052 governs conversion and related core value/expression decisions. This StateMachine program is not its child or a board topic. Any proposed change to a contract it governs must go through that initiative's actual agreement process. Current implementation text or a bot review cannot approve an unagreed ADR.

## Readiness

The [specification](spec.md) is provisional with Q1–Q3 open. The [acceptance matrix](acceptance-matrix.md) is test design, not runtime acceptance. No technical plan, production implementation, new public API/schema, source import, SCXML interchange or release is delivered by this research note.
