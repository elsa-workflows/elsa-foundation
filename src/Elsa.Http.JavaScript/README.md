# Elsa.Http.JavaScript

Bridges the HTTP domain and the JavaScript expression engine. Contributes HTTP-specific type declarations to the JavaScript type-declaration pipeline so that HTTP-related types are available in the script editor.

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IJavaScriptDeclarationContributor`** *(Core — `Elsa.Expressions.JavaScript.Rendering.Core`)* — `HttpTypeDeclarationContributor` pushes HTTP-domain type declarations (request/response types, etc.) onto the declarations context. Aggregated by `BuildDeclarationsDocument` in `Elsa.Expressions.JavaScript.Rendering`.
  - Catalog: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md)
