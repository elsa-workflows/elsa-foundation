# Elsa.Activities.Bpmn.Interchange

The registered BPMN interchange module converts XML/BPMNDI through released `Bpmn.Interchange` **0.5.0**, then binds representable work to Foundation's existing `elsa.bpmn.structure` **1.0.0** authored envelope. Foundation retains `childNodeId`, `listenerNodeId`, exact catalog references, variables, default-flow markers and designer data. The runtime package carries the [host adapter](../Elsa.Activities.Bpmn/README.md); XML fidelity and host execution readiness are distinct results.

`IBpmnDocumentImporter` analyzes/imports and `IBpmnDocumentExporter` exports. `ActivitiesBpmnInterchangeFeature` registers both interfaces and `IBpmnHostWorkBinder`. The normal Workbench enables this module. APIs and Studio use the registered services; direct port-only conversions do not establish published or browser compatibility.

## Host bindings and readiness

| Neutral work | Foundation binding |
|---|---|
| Supported duration wait | Delay with an invariant TimeSpan literal; the original ISO duration descriptor remains available for export. |
| Named message/signal wait | Event with `CanStartWorkflow=false`; normal runtime correlation/local/test-scope policy still applies. |
| Named publication | PublishEvent, using the durable first-delivery recipient snapshot when broadcast is requested. |
| Call Activity | Waited DispatchWorkflow with original definition/source binding; newly compiled capable parents opt into ordered child control. |
| Embedded subprocess/event-subprocess body | Nested BpmnProcess with external starts disabled and exact declared body/listener references. |

Catalog-backed imports resolve actual visible active recommended profile-1.0.0 activity publications, including build metadata. Missing or ambiguous inventory is an explicit readiness failure. Ordinary publication validation remains authoritative. An importer retains representable library descriptors/data even when the current host cannot execute their bindings. Date/cycle waits are reported Unsupported; Unknown never qualifies readiness. Retained condition expressions are not silently evaluated by a host that does not support them.

The library supplies its selected XML/semantic profile: coded errors, named events, links, compensation associations, transactions, event subprocesses, loops and default flows are interpreted under its released contracts. The [parity cut ledger](../../../../../docs/reports/evidence/bpmn-adoption-0.5.0/cut-ledger.md) distinguishes supported neutral constructs, host policy and retained cuts. A retained XML element, valid graph or available library test does not imply that a Foundation work binding exists.

Default markers are derived from the source element's DefaultFlowId. Import/edit/export preserves Foundation's authored markers; it does not replace them with a library bindingRef envelope. Error classification is separate from ordinary host faults/incidents. Compensation handlers retain their associated ordinary bound task identities; called escalation is carried through real child-control sources rather than an authored marker alone.

## Direct binding metadata retention

The optional authored element property `elsa.direct.binding` round-trips as `elsa:directBinding` in the existing Elsa vendor namespace. Interchange retains the exact JSON object, including future schema versions, with a limit of16KiB UTF-8 and a maximum depth of32. Malformed values and conflicting attributes under both vendor namespace aliases are refused with element context. Other unrepresented extension content still reports degradation; documents without the property emit no new binding attribute.

This is metadata retention, not executable-profile validation or a direct publication path. A publisher must validate the versioned binding's full schema, supported inventory and immutable dependencies before accepting execution. Native `childNodeId` and listener bindings keep their existing meaning. The remaining direct Studio journey is tracked in [spec1184](../../../../../specs/1184-direct-bpmn-studio/spec.md).

## Verification

The [post-deletion host and Studio qualification](../../../../../specs/1006-bpmn-library-adoption/evidence/post-deletion-host-qualification.md) records normal UI import, default-flow editing, publication, a real published run, reload and downloaded XML. Actual ordinary compensation, called error/escalation, concurrent calls, legacy recovery and broadcast witnesses are linked from the [task ledger](../../../../../specs/1006-bpmn-library-adoption/tasks.md). Each receipt names its source and scope. Final resulting-build/main qualification remains a separate delivery gate.

Permanent frozen authoring descriptors and the original runtime/interchange discovery baseline remain in [the extraction baseline](../../../../../docs/reports/evidence/bpmn-extraction-baseline/README.md). Runtime graph checks use the released graph through the Foundation adapter; the retired embedded engine is not a validator fallback.
