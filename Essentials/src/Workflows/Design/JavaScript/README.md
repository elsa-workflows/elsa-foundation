# Elsa.Workflows.Design.JavaScript

Bridges the workflow design surface and the JavaScript expression engine. Canonical input-binding expressions use the closed `binding-readonly-v1` capability profile: the immutable `args` parameter map and the variables they read by name, the native `Date`, `Intl`, `Temporal` and `Math`, and the built-in and host-registered .NET functions (spec 1223). This contributor declares `args`; the JavaScript expression tooling completes the variables and functions (see [the JavaScript extension points](../../../Expressions/JavaScript/Elsa.Expressions.JavaScript/EXTENSION_POINTS.md)).

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IJavaScriptDeclarationContributor`** *(Core — `Elsa.Expressions.JavaScript.Rendering.Core`)* — `BindingPureArgsDeclarationContributor` contributes the single read-only `args` global. Ambient workflow functions, configuration helpers, variable mutation, and output lookup are intentionally absent because they do not exist in the runtime capability profile. Aggregated by `BuildDeclarationsDocument` in `Elsa.Expressions.JavaScript.Rendering`.
  - Known impl: `BindingPureArgsDeclarationContributor`.
  - Catalog: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../../../Expressions/JavaScript/Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md)
