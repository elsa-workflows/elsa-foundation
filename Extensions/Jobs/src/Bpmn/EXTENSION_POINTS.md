# Jobs BPMN integration extension points

This module defines no replacement port or event. It contributes `JobsBpmnAssociationSource` to
Jobs.Workflows.Runtime's `IJobWorkflowAssociationSource` aggregation and `JobsBpmnResultValidator`
to [BPMN's public native-work validation fan-in](../../../../Essentials/src/Activities/Bpmn/Core/EXTENSION_POINTS.md).
`JobsBpmnResultDeliveryContributor` contributes to BPMN's public native-result delivery fan-in.
`JobsBpmnFeature.ConfigureServices` registers all three as scoped enumerable contributions with `TryAddEnumerable`.

The association source consumes the scoped `IBpmnNativeWorkConsumer` replacement reader. The result
validator consumes `IJobNativeDeliveryInspector` and the exact retained `IJobQueries.FindContinuationAsync`
read. Overrides preserve full owner/result/address identity and defer unavailable evidence; they never
interpret helper completion or transport status as semantic root consumption.

The delivery contributor uses public `IJobNativeResultSettlement` after accepted semantic input or positive
root terminal proof. A selected Jobs result rejected by a live root remains owed. Native settlement owns
exact Jobs ACK and positive pre-admission/cancellation phase classification, including when no selected
result binding exists; missing or retired-only Jobs state cannot release the existing BPMN result source.
