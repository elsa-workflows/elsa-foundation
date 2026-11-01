# BPMN authoring

This package owns the authored structure, child/outcome projection, executable lowering and exact
published-model reference contribution described by the approved [package boundary](../../../../../specs/1179-specialist-agents/plan.md#d7--bpmn-runtimeauthoring-separation).
It depends on [neutral Core](../Core/README.md), Workflows Design Core and Publishing Core, with no
reference to the concrete BPMN runtime or interpreter. [Extension points](EXTENSION_POINTS.md) record
the owning contracts and registration lifetimes.

Enable `ActivitiesBpmnDesign` in an authoring/publishing host. `ActivitiesBpmnInterchange` specializes
this feature and includes these registrations when configured directly. Repeated composition retains
one structure handler and one model-reference contribution. Publishing hosts supply their ordinary
material and source-reference stores. Runtime-only hosts enable `ActivitiesBpmn` and/or
`ActivitiesBpmnHosting` without this package.

## Cross-domain contributions

- Workflows Design [`IActivityStructureHandler`](../../../Workflows/Design/Api/EXTENSION_POINTS.md#iactivitystructurehandler-core--elsaworkflowsdesigncore):
  the singleton `BpmnStructureHandler` projects authored children/outcomes and scoped variables, then lowers/remaps the executable graph.
- Workflows Publishing [`IExecutableCompilationSource`](../../../Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md#iexecutablecompilationsource):
  the scoped `BpmnEmbeddingPinSource` validates exact live tenant-matched model material and contributes its immutable metadata/dependency pin.

## Pre-GA source/package migration

`Elsa.Activities.Bpmn.Models.BpmnAuthoredStructure` retains its namespace and JSON shape, but moves
from the runtime assembly to `Elsa.Activities.Bpmn.Design`. Authoring consumers must reference this
package and rebuild. There is deliberately no Runtime-to-Design type forwarder.

Neutral graph models move to Core under their original namespaces; the runtime assembly forwards
those types. Existing activity aliases, `elsa.bpmn.structure` schema `1.0.0`, the `Bpmn.Activities`
slot, compiled graph JSON and opaque embedding-document encoding are preserved. The runtime's
`BpmnProcess` and `RunBpmn` identities remain unchanged. The shared private embedding codec belongs
to Core and is visible only to the BPMN sibling implementations/tests; third-party consumers use the
public activity/model contracts.

This package separation is part of M4. Editorial/native/BPMN execution and independent packaged
consumer qualification remain separate acceptance tasks in [spec1179](../../../../../specs/1179-specialist-agents/tasks.md).
