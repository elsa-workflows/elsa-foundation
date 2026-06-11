# Contract: Runtime Activity Input Resolution

`IRuntimeActivityInputMaterializer.MaterializeInputs(ExecutableNode node, RuntimeInputBindingResolutionContext context)` materializes executable-node input bindings for one activity invocation.

Supported materialized value sources in this slice:

- literal JSON values;
- active activity outputs resolved by `WorkflowExecutionId`, producer `ActivityExecutionId`, and output name;
- durable values resolved by runtime value ID.

Unsupported sources such as expression declarations and references remain explicit failures until their provider/evaluator slices are implemented.

Runtime input resolution does not read authored workflow documents or history/audit output snapshots.
