# Contract: Runtime Workflow Execution Context

`WorkflowExecutionContext` is an in-memory runtime execution helper over:

- `WorkflowExecutionState`;
- explicit runtime workflow inputs;
- explicit runtime variables;
- active activity outputs keyed by activity execution ID or runtime activity name.

It must not load authored workflow documents, use Elsa 3 instance models, or persist history/audit payloads. Missing values fail deterministically.
