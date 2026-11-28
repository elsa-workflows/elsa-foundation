# Elsa.Activities.Bpmn.Composite

The `ActivitiesBpmn` feature composes the BPMN composite activities and their authored trigger bindings over the workflow contracts. Explicit host assembly selection can discover it through `typeof(ActivitiesBpmnFeature).Assembly`. The [Design feature](../Design/README.md) owns authored structure and publication pins. The [Interchange module](../Interchange/README.md) owns XML import and export.

Existing direct BPMN and embedded `RunBpmn` deployments use the [classic Workbench compatibility path](../../../../docs/contributing/workbench-compatibility-tests.md#classic-bpmn-direct-execution).
