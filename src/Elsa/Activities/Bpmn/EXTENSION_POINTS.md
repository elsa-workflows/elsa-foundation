# Elsa.Activities.Bpmn Extension Points

## Scoped execution seam

`BpmnExecutionEngine` is the activity-owned scoped execution seam. It owns BPMN runtime state
mutation, child scheduling metadata, token propagation, join accounting, diagnostics, and deferred
composite completion. Its durable snapshot is staged as one typed, versioned structural
private-state document; it does not patch the activity metadata bag.

The token model is intentionally not an extension point directly: custom element semantics cross the
public behavior contract below, and `BpmnExecutionEngine` remains the authority that validates and
applies behavior commands.

## Implementable contributor interfaces

### `IBpmnElementBehavior`

- **Kind:** Contributor (element behavior provider)
- **Contract:** `Elsa.Activities.Bpmn.Contracts.IBpmnElementBehavior`
- **Behavior contract:** behaviors receive `IBpmnBehaviorContext` and return `BpmnBehaviorDecision`
  commands for `BpmnExecutionEngine` to validate and apply.
- **Registration:** Register one or more implementations with DI as `IBpmnElementBehavior`.
- **Aggregation:** `IBpmnBehaviorRegistry` resolves all registered behavior implementations by
  stable element family (`BpmnElementFamilies`).
- **Selection:** `BpmnElementFamilies.Resolve` maps a `BpmnElement` (element type + event
  definitions) to its behavior family.
- **Decision boundary:** Behaviors receive `IBpmnBehaviorContext`, which exposes read-only
  element/flow/state/trigger information. Behaviors return `BpmnBehaviorDecision` commands;
  `BpmnExecutionEngine` validates and applies those commands.

Known implementations:

- `NoneStartEventBehavior` *(intra-domain — default)*
- `NoneEndEventBehavior` *(intra-domain — default)*
- `TerminateEndEventBehavior` *(intra-domain — default)*
- `TaskBehavior` *(intra-domain — default)*
- `SubProcessBehavior` *(intra-domain — default)*
- `ExclusiveGatewayBehavior` *(intra-domain — default)*
- `ParallelGatewayBehavior` *(intra-domain — default)*
- `InclusiveGatewayBehavior` *(intra-domain — default)*

## Activity-owned structure contracts

This module also exposes these activity-owned contracts:

- `Bpmn.Activities` child slot
- `elsa.bpmn.structure` structure payload with schema version `1.0.0`
- `BpmnStructure.Elements` containing `BpmnElement[]`
- `BpmnStructure.SequenceFlows` containing `BpmnSequenceFlow[]`
- `BpmnAuthoredStructure.Pools` / `BpmnAuthoredStructure.Lanes` (authored/designer-side only)
- `BpmnAuthoredStructure.Diagram` opaque BPMN-DI-shaped layout document (authored-side only,
  stripped at compile time)
- `BpmnAuthoredStructure.Variables` optional container-scoped variable declarations (ADR 0027)

## Consumed runtime contracts

`BpmnProcess` implements the engine-only structural execution protocol
(`Elsa.Workflows.Runtime.Core.Contracts`):

- `IRuntimeStructuralActivity` — builds and validates the BPMN graph, emits start-event tokens,
  propagates them, and returns a `RuntimeStructuralContinuation`.
- `IRuntimeActivityChildCompletionHandler` — invoked when a bound child completes; routes through
  `BpmnExecutionEngine.OnChildCompletedAsync` to select outbound flows and continue propagation.
- `IRuntimeActivityChildFaultHandler` — invoked when a child faults; the returned decision faults
  the process deterministically (`bpmn.child.faulted`) instead of hanging a join. Error boundary
  events replace this rule in the events tier.

## Cross-domain contributions

- `BpmnStructureHandler` implements `IActivityStructureHandler` (`Elsa.Workflows.Design.Core`) with
  `SupportsScopedVariables = true` and `ProjectScopedVariables` — a `BpmnProcess` is a container
  scope that can own container-scoped variables visible to its descendant activities, using the same
  generic scope semantics as `Sequence` and `Flowchart` (ADR 0027).
