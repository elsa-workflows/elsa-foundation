# Elsa.Workflows.Design.JavaScript

Bridges the workflow design surface and the JavaScript expression engine. Canonical input-binding expressions use the closed `binding-pure-v1` capability profile, so the editor exposes only the immutable `args` parameter map available to the isolated runtime evaluator.

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IJavaScriptDeclarationContributor`** *(Core — `Elsa.Expressions.JavaScript.Rendering.Core`)* — `BindingPureArgsDeclarationContributor` contributes the single read-only `args` global. Ambient workflow functions, configuration helpers, variable mutation, and output lookup are intentionally absent because they do not exist in the runtime capability profile. Aggregated by `BuildDeclarationsDocument` in `Elsa.Expressions.JavaScript.Rendering`.
  - Known impl: `BindingPureArgsDeclarationContributor`.
  - Catalog: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md)
