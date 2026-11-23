# Extension points — Expressions.JavaScript.Rendering domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Expressions.JavaScript.Rendering` — the composition root where `JavaScriptRenderingFeature` registers `BuildDeclarationsDocument` and the built-in `CommonDeclarationContributor`.

---

## Implementable contributor interfaces

### `IJavaScriptDeclarationContributor` *(Core — `Elsa.Expressions.JavaScript.Rendering.Core`)*
- **Kind:** Contributor (receives a contribution context and acts — push pattern).
- **Signature:** `ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IJavaScriptDeclarationContributor, MyContributor>()`.
- **Aggregated by:** the single `BuildDeclarationsDocument : IEventHandler<DeclarationsDocumentGenerating>` (this feature), which injects `IEnumerable<IJavaScriptDeclarationContributor>` and invokes each to build the TypeScript declaration document surfaced in the design editor.
- **Purpose:** contribute TypeScript type declarations, function signatures, variable names, etc. to the editor's IntelliSense document.
- **Check the output:** the document factory and renderer are internal, so a contributor's rendered declarations are read through the rendering endpoint, `GET javascript/documents/render`, which `JavaScriptRenderingEndpointsFeature` maps.

**Known implementations (shipped):**
- `Elsa.Expressions.JavaScript.Rendering` — `CommonDeclarationContributor` *(intra-domain — default, registers common built-in declarations)*
- `Elsa.Http.JavaScript` — `HttpTypeDeclarationContributor` *(cross-domain — adds HTTP-related types)*
- `Elsa.Workflows.Design.JavaScript` — `BindingPureArgsDeclarationContributor` *(cross-domain — exposes only immutable `args` for canonical binding expressions)*

---

## Events

None public. `DeclarationsDocumentGenerating` is internal to this feature: the document factory publishes it inline on each request for the declaration document, and its single handler, `BuildDeclarationsDocument`, has every `IJavaScriptDeclarationContributor` add to the event's context before the document is returned. Contribute through `IJavaScriptDeclarationContributor`, not by handling the event.

---

## Cross-references

- JS script pre/post processors (runtime execution): [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../../Elsa.Expressions.JavaScript/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../Elsa.Expressions/EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1.
