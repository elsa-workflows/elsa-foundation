# Elsa.Workflows.Runtime.JavaScript

Bridges the workflows runtime and the JavaScript expression engine. Injects runtime-context functions (workflow variables, activity outputs, workflow inputs) into the script execution context, and copies variable mutations back after execution.

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IScriptPreProcessor`** *(Core — `Elsa.Expressions.JavaScript.Core`)* — multiple pre-processors inject workflow-runtime context into the JavaScript environment before each script runs (variable functions, workflow functions, activity-output accessors, workflow-input accessors, variables-context setup). Aggregated by `PreProcessScript` in `Elsa.Expressions.JavaScript`.
  - Known impls: `WorkflowVariablesContextPreProcessor` and others.
  - Catalog: [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../../../Expressions/JavaScript/EXTENSION_POINTS.md)

- **`IScriptPostProcessor`** *(Core — `Elsa.Expressions.JavaScript.Core`)* — `CopyVariablesToWorkflowContext` propagates any variable mutations made inside the script back into the workflow execution context. Aggregated by `PostProcessScript` in `Elsa.Expressions.JavaScript`.
  - Catalog: [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../../../Expressions/JavaScript/EXTENSION_POINTS.md)
