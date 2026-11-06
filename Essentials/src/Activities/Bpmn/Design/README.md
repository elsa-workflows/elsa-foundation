# BPMN authoring

This package owns the authored structure, child/outcome projection, executable lowering and exact
published-model reference contribution described by the approved [package boundary](../../../../../specs/1179-specialist-agents/plan.md#d7--bpmn-runtimeauthoring-separation).
It depends on [neutral Core](../Core/README.md), Workflows Design Core, Validations Core and Publishing Core, with no
reference to the concrete BPMN runtime or interpreter. [Extension points](EXTENSION_POINTS.md) record
the owning contracts and registration lifetimes.

Enable `ActivitiesBpmnDesign` in an authoring/publishing host. `ActivitiesBpmnInterchange` specializes
this feature and includes these registrations when configured directly. Repeated composition retains
one handler per structure schema and one model-reference contribution. Publishing hosts supply their ordinary
material and source-reference stores. Runtime-only hosts enable `ActivitiesBpmn` and/or
`ActivitiesBpmnHosting` without this package.

## Cross-domain contributions

- Workflows Design [`IActivityStructureHandler`](../../../Workflows/Design/Api/EXTENSION_POINTS.md#iactivitystructurehandler-core--elsaworkflowsdesigncore):
  the singleton `BpmnStructureHandler` retains schema `1.0.0` graph projection/lowering. The stateless singleton
  `BpmnDocumentStructureHandler` projects schema `2.0.0` native children and scoped variables while preserving complete XML.
- Workflows Design `IDraftValidator`: the scoped `BpmnScopeViewSourceValidator` refuses internal derived
  scope structures in physical activity positions through the ordinary inline draft-validation gate.
- Workflows Publishing [`IExecutableCompilationSource`](../../../Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md#iexecutablecompilationsource):
  the scoped `BpmnEmbeddingPinSource` validates exact live tenant-matched model material and contributes its immutable metadata/dependency pin.

## Complete-document retention spike

Internal schema `2.0.0` stores `documentXml`, `processId`, native `activities`, identity-linked `bindings`
(`elementId`, optional `childNodeId` / `listenerNodeId`), scoped `variables` and optional `scopeHeaders`. XML is the only standard
BPMN semantics, extension and DI authority; parallel flat graph fields are rejected. Opaque payload and binding
metadata survive replacement. The original XML string is retained without parsing/serializing it back to text.

Each configured standard process/subprocess/transaction/ad-hoc scope has a compact header containing its XML
`scopeElementId`, stable native `nodeId`, exact `activityVersionId`, `inputs`, `outputs`, `variables` and optional
`executionPlacement`. Headers retain opaque metadata and carry no body structure. The selected process uses
the physical owner's configuration, so it has no duplicate header. Unconfigured standard XML remains intact.

Selected-process traversal derives pure `elsa.bpmn.scope-view@1.0.0` activity nodes for configured nested
standard scopes. Each view carries a transient canonical-owner snapshot and scope address. Replacement folds
only that scope's native records and headers into the physical owner, checks unchanged foreign content, and
composes sibling edits. Derived snapshots are never persisted. Native Sequence bodies retain their actual
slots; event-subprocess listeners remain siblings in the enclosing scope rather than becoming body children.
If standard and native bodies both carry intent, both records survive; this spike never selects one for execution.
An independently persisted BPMN body bound to a standard XML subprocess remains refused.

Independent processes retain their configurations but are not projected as lexical children of the selected
process. A pure process-root view can supply existing unsaved-state variable/input-option queries without
leaking selected-process variables. This does not qualify independent-process runtime execution or generic
whole-document clone/upgrade traversal. Those mutation entry points must use a complete BPMN-owned identity
inventory/transform or refuse before mutation; a selected-process walk is incomplete. In particular a root-ID
replacement is not detectable by a stateless handler without a caller's previous root identity.

Generic child replacement supports existing identities/property edits, reordering and order-preserving identity
remapping. Adding/removing ownership requires an explicit atomic document/binding edit.

The scoped physical-source validator visits actual activity structures: schema `2.0.0` contributes its physical
native forest directly, and other registered handlers contribute their native slots. Literal JSON and opaque
metadata are not scanned for reserved strings. A genuine derived view submitted as a physical root or native
child is rejected even if its snapshot is valid. The host must compose the ordinary inline validation handler;
this contribution does not authorize raw persistence paths that bypass the write gate. Draft query views are
transient inputs to query services, never create/update payloads.

This is an intermediate contract proof for [#3597](https://github.com/elsa-workflows/elsa-foundation/issues/3597),
not full-toolbox delivery or executable qualification. With this handler registered, schema `2.0.0` compilation
always refuses, including nested processes with `CanStartWorkflow=false`. Unknown-schema rejection in the current
generic compiler requires the catalog's owned structure facet; an older host without that declaration is not
qualified by this spike. Production needs complete-content capability admission before
typed lowering, coordinated with [#2736](https://github.com/elsa-workflows/elsa-foundation/issues/2736), and
qualified derived lowering for currently executable content. The interpreter and runtime schema remain unchanged.
The current public XML exporter and legacy authored reader require exact schema `1.0.0` before deserializing;
they refuse complete-source documents rather than returning an empty legacy process.

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
