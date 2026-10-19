# Research: native compensation scopes

Source baseline: `main` at `553f35e3` (19 October 2026). Paths are under `Essentials/src/` unless stated.

## R1 — A scope is a structural activity with a private-state journal

**Decision**: `CompensationScope : StructuralActivity` implementing `IRuntimeStructuralActivity`,
`IRuntimeActivityChildCompletionHandler` and `IRuntimeActivityChildFaultHandler`
(`Workflows/Runtime/Core/Contracts/IRuntimeStructuralActivity.cs`). Its journal is a versioned private-state blob.

**Why**: Completion callbacks are routed by `ParentActivityExecutionId` to a fresh parent instance
(`Activities/Runtime/Elsa.Activities.Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs:185,263-303`).
Staged state is applied in the same checkpoint (`:454`, `RuntimeStructuralStateProjector`). BPMN compensation
already keeps its log this way (`Activities/Bpmn/Elsa.Activities.Bpmn/Models/BpmnCompensable.cs`,
`Internal/BpmnStatePersister.cs:104-168`), so there is a working precedent for a durable, never-pruned journal
without a new table.

**Rejected**: a compensation table (a second representation of state already in the checkpoint, plus its own
retention); reading the journal from `RuntimeHistoryEvent` (observability only, "not used to resume").

## R2 — Steps are a Switch-style collection with two slots per item

**Decision**: authored `steps[]` with `activity` and `compensation`. Two `[ActivityChildSlot]` declarations share
`CollectionProperty = "steps"` and differ in `ChildProperty`.

**Why**: Switch does this for cases (`Activities/ControlFlow/Switch/Activities/Switch.cs:31-40`,
`Internal/SwitchStructureHandler.cs`). The structure handler owns `ProjectChildren`, `ReplaceChildren`,
`CompileExecutableStructure` and `RemapExecutableStructure`, so two slots per item need no runtime change. Studio
already derives repeatable slots from `collectionProperty`/`childProperty`
(`Studio/…/Client/src/workflowAdapter.ts:503-521`).

## R3 — Fault-triggered recovery uses fault absorption

**Decision**: on a direct step fault, call `RequestChildFaultAbsorption(incidentId, …)` and return `Defer` with the
first handler scheduled.

**Why**: Absorption resolves the incident and reclaims the subtree atomically
(`IRuntimeActivityExecutionContext.cs`, `WorkflowParentActivityCompletionSchedulerWorkHandler.cs:696`, spec 115).
An absorbed fault never reaches `IncidentStrategyResolutionDrainObserver`, so recovery starts before the incident
strategy can terminalize the run (PRD FR6).

**Constraint found**: a parent that does not implement the fault handler halts, and the incident stays blocking
(`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:209-222`). Sequence does not implement it; Flowchart and
Parallel re-fault. A fault deep inside a Sequence step therefore never reaches the scope. M1 supports leaf and
Flowchart steps; R5 records the M3 fix.

## R4 — Explicit trigger needs ancestor targeting by activity type (M3)

`RequestAncestorNotification` resolves the nearest ancestor by `Descriptor.ConsumerKey`
(`Activities/Runtime/Elsa.Activities.Runtime/Services/ParentNotificationEvaluation.cs:195-217`). All CLR activities
share `elsa.clr-activity` (`Activities/Runtime/Core/Models/RuntimeActivityDescriptor.cs:62-65`), so a `Compensate`
leaf cannot target "the nearest compensation scope" today. M3 adds an ancestor selector by activity type to that
seam. The extension is owned by this program and is additive: existing consumer-key callers are unchanged.

## R5 — Sequence fault propagation (M3)

The options are (a) Sequence re-faults like Flowchart, or (b) the runtime delivers a fault to the nearest
fault-handling ancestor. Option (a) changes incident counts for every Sequence fault. Option (b) changes a
runtime rule. Either needs runtime-owner review. Until then, Studio's publish validation warns when a step is a
Sequence whose descendants can fault, so the limitation is visible.

## R6 — Pre-effect durability is mode-independent

`CoalescingRuntimeCheckpointPersistencePolicy.DecideAsync`
(`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Coalescing/CoalescingRuntimeCheckpointPersistencePolicy.cs:32-66`)
flushes `ActivityAttemptClaimed` immediately for every activity that is not `ReplaySafe`, in every mode.
`SideEffectProfile.External` is the default (`Activities/Runtime/Core/Models/ActivityContract.cs:457-470`). So the
pre-effect boundary that issue #3043 asked to prove for Balanced already exists in all modes; what needs proving
is the scope's behaviour across replays (quickstart), not a new boundary. This is why FR-013 claims all three
modes, conditional on their restart tests.

## R7 — Cancellation runs no activity code

`ActivitySubtreeCancellationPlanner` and `WorkflowCancellationPlanner` only terminalize state and clean up
(`Workflows/Runtime/Elsa.Workflows.Runtime/Services/ActivityExecutions/`). Running handlers on cancellation would
need a runtime hook that does not exist. M1 documents "cancellation does not compensate". The M3 decision goes to
the owner with two options: an opt-in "compensate on cancel" scope setting backed by a new hook, or explicit
`Compensate` before cancel.

## R8 — Identity and the operation key

- Invocation id = `ActivityExecutionId` (`Workflows/Runtime/Core/Models/ActivityExecutionState.cs:54`). It is
  stable across crash re-attempts, which re-claim the same execution as a `Retry` attempt
  (`Activities/Runtime/Elsa.Activities.Runtime/Services/ActivityAttemptActivationClaimer.cs`). It is exposed as
  `ActivityExecutionContext.InvocationId` and already documented as the external idempotency key
  (`docs/reference/background-activity-execution.md:41`).
- The operator boundary retry (`RetryActivityBoundaryCommand`) mints a new execution id and keeps lineage in
  `ActivityExecutionAttemptLineage.FirstAttemptActivityExecutionId`. The M2 logical operation key derives from
  that lineage. M1 has no boundary retries of compensated steps, so the invocation id is sufficient.
- Handler binding: the causal resolver walks the scheduling and parent chain with no newest-by-node fallback
  (`Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/CausalActivityResultResolver.cs:54-119`). Scheduling
  a handler with the forward execution id as its scheduling id makes "the forward step's output" mean that exact
  occurrence, including inside loops (FR-010).

## R9 — Relationship to #2892

No #2892 contract is needed. Everything above predates #2892's offload work (spec 241). Spec 241's
`BackgroundActivityWork*` ledger, coordinator and worker are not used. An offloaded forward step or handler
(`ExecutionPlacement = Background`) still works, because offload preserves invocation identity and completion
semantics. That combination is tested in M3, not assumed.

## R10 — Studio fit

- Canvas modes: `WorkflowEditor.tsx:1298-1406` switches between `SequenceDesigner` and React Flow. A
  `compensation` mode beside the sequence mode follows the same pattern (`workflowAdapter.ts:640-656`).
- `workflowDesigner.nodeRenderers` is declared but unused (`Studio/src/apps/Elsa.Studio.Web/Client/src/sdk/index.ts:1850-1852`),
  so no plugin seam exists for custom node rendering. The scope designer is built into the workflows module,
  which owns all built-in designers.
- Run overlays show only the latest execution per node (`workflowAdapter.ts:1113`). Handler nodes differ from
  forward nodes, so forward and recovery states do not hide each other. Loops (M3) need the per-occurrence list,
  which the projection provides.
- Inspection precedent for a per-composite panel: `ReusableBoundaryInspector.tsx`.

## R11 — BPMN

See [ADR 0086](../../docs/adr/0086-native-and-bpmn-compensation-stay-separate.md). Nothing in BPMN changes.

## R12 — Structural activities have no output channel

`RuntimeStructuralContinuation` carries an outcome, a fault or a cancellation, plus private state; it has no
outputs (`Workflows/Runtime/Core/Models/RuntimeStructuralContinuation.cs`). The graph activity captures outputs
through its own completion-checkpoint preparation path, which is specific to graph boundaries. M1 therefore
exposes the recovery cause through the inspection projection and the `Compensated` outcome. Making the cause
an expression-readable output is an M3 item that needs a structural-output seam, reviewed by the runtime owner.
