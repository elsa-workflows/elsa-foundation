# Elsa.Workflows.Design.JavaScript

Bridges the workflow design surface and the JavaScript expression engine. Contributes workflow-design-specific type and function declarations to the JavaScript type-declaration pipeline so that activities, outcomes, variables, and workflow inputs/outputs are fully typed in the script editor.

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IJavaScriptDeclarationContributor`** *(Core — `Elsa.Expressions.JavaScript.Rendering.Core`)* — multiple contributors push workflow-design declarations onto the context: outcome functions, workflow-level functions, workflow-input functions, workflow-variable declarations, and activity-output function declarations. Aggregated by `BuildDeclarationsDocument` in `Elsa.Expressions.JavaScript.Rendering`.
  - Known impls: `OutcomeFunctionDeclarationContributor`, `WorkflowFunctionDeclarationContributor`, `WorkflowInputFunctionDeclarationContributor`, `WorkflowVariablesDeclarationContributor`, and others.
  - Catalog: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md)
