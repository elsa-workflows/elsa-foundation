# BPMN authoring

This package owns the authored structure, child/outcome projection, executable lowering and exact
published-model reference contribution described by the approved [package boundary](../../../../../specs/1179-agents/plan.md#d7--bpmn-runtimeauthoring-separation).
It depends on [neutral Core](../Core/README.md), Workflows Design Core, Validations Core and Publishing Core, with no
reference to the concrete BPMN runtime or interpreter. [Extension points](EXTENSION_POINTS.md) record
the owning contracts and registration lifetimes.

Enable `ActivitiesBpmnDesign` in an authoring/publishing host. `ActivitiesBpmnInterchange` specializes
this feature and includes these registrations when configured directly. Repeated composition retains
one handler per structure kind/schema, two draft validators and one model-reference contribution. Publishing hosts supply their ordinary
material and source-reference stores. Runtime-only hosts enable `ActivitiesBpmn` and/or
`ActivitiesBpmnHosting` without this package.

## Cross-domain contributions

- Workflows Design [`IActivityStructureHandler`](../../../Workflows/Design/Api/EXTENSION_POINTS.md#iactivitystructurehandler-core--elsaworkflowsdesigncore):
  the singleton `BpmnStructureHandler` retains schema 1 projection/lowering. The singleton
  `BpmnDocumentStructureHandler` and `BpmnDocumentScopeViewHandler` provide the retention/query contract below.
- Workflows Design Validations `IDraftValidator`: scoped `BpmnScopeViewSourceValidator` rejects physical
  derived views; `BpmnDocumentAdmissionValidator` reports the interim execution-closed diagnostic.
- Workflows Publishing [`IExecutableCompilationSource`](../../../Workflows/Publishing/Elsa.Workflows.Publishing/EXTENSION_POINTS.md#iexecutablecompilationsource):
  the scoped `BpmnEmbeddingPinSource` validates exact live tenant-matched model material and contributes its immutable metadata/dependency pin.

## Complete-document retention

Internal authored schema `elsa.bpmn.structure@2.0.0` retains the original `documentXml` string,
selected `processId`, disjoint native `activities`, identity-linked `bindings`, scoped `variables`
and optional compact `scopeHeaders`. XML alone owns standard BPMN semantics, extensions and DI.
Competing flat graph/diagram fields, ambiguous identities, duplicate records and incomplete native
ownership refuse before projection or replacement. Unsupported standard XML and opaque native/binding
metadata remain retained. Reading a document never serializes its XML back into another string.

Configured standard scopes retain only their native invocation headers: XML scope ID, native node ID,
exact activity version, arguments, variables and placement. Nested traversal derives pure
`elsa.bpmn.scope-view@1.0.0` nodes from the physical owner and scope address. Child replacement folds
only owned native configuration and headers into that owner, refuses foreign snapshot changes and
composes sibling edits. Native Sequence bodies keep their own child slots. Independent standard
processes remain outside the selected process's lexical tree and have separate query views.

The separate [ownership projection](../../../Workflows/Design/Api/EXTENSION_POINTS.md#iactivitystructurehandler-core--elsaworkflowsdesigncore)
exposes all physical native roots and compact-header node IDs for presentation retention, including
independent processes. EF metadata pruning uses that projection without exposing those processes to
selected-process lexical queries. Removed physical ownership still prunes its presentation records.

Malformed JSON and XML at these parser boundaries throw the existing BPMN Core
`BpmnDocumentSourceException` with the original `JsonException` / `XmlException` as `InnerException`
and the owning node/process or view/scope identifiers. This changes neither schema-1 handling nor
explicit structural rejections; unsupported but well-formed XML remains retained.

The structure listing exposes retention, independently of execution support. Complete-source compilation
requires the explicit server-resolved `ActivityStructureCompilationContext`; the original one-argument
source/scope entrypoints remain closed. Exact native process/Decision runtime types, consumer/descriptor
schemas and declared target structures are captured before reusable placement changes boundary labels.
The owning handler admits the complete raw document before lowering to runtime schema 1. Unsupported
semantics remain authored and return content-derived `Bpmn/ExecutionCapability` diagnostics.

The bounded profile covers ordinary events/tasks, configured standard scopes, native bodies, gateways
and outcome/default routing. Loops, listeners, collaboration, independent-process execution and unknown
semantic extensions remain refused. This is not full-toolbox execution qualification. Child-result facts
are proved only for an admitted single unconditional ordinary-task chain of inline native leaves in its
actual source scope, using sequence flows rather than array order. Raw event/loop/unknown semantics,
non-leaf work and unqualified bindings remain Unknown; outer scopes must provide their own occurrence
proof. Schema 1's separately proven analyzer remains unchanged.

The ordinary inline validation gate still rejects physically authored derived views and preserves unsupported
drafts. Workflow and Graph compilers keep shared data-selection admission, member/Input lowering and exact
immutable dependencies. The canonical normalizer converts variable frames only inside a translated source
subtree; legacy-only overlays and siblings keep their prior representation. Placement remaps typed input
and canonical variable-initial bindings, preserving opaque data and leaving authored variable arrays alone.
The post-compilation contribution verifies the complete admitted scope payload through the same normalizer.
Raw persistence paths that bypass the owning validation gate are not authorized by this contribution.

This is the backend retention/lowering path of [spec 1224](../../../../../specs/1224-optional-bpmn-js-editor/delivery-slices.md).
Broader semantic execution admission and actual editor activation belong to later slices. Public XML is not the internal editor switch transport.
Whole-document clone/upgrade and identity transforms must inventory every configured process or refuse
before mutation: selected-process traversal is incomplete, and a stateless handler cannot infer a previous
root identity. Ownership addition/removal requires an explicit atomic document edit.

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
consumer qualification remain separate acceptance tasks in [spec1179](../../../../../specs/1179-agents/tasks.md).
