# Extension points — Expressions.JavaScript.Rendering domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Expressions.JavaScript.Rendering` — the composition root where `JavaScriptRenderingFeature` registers `BuildDeclarationsDocument` and the built-in `CommonDeclarationContributor`.

---

## Implementable contributor interfaces

### `IJavaScriptDeclarationContributor` *(Core — `Elsa.Expressions.JavaScript.Rendering.Core`)*
- **Kind:** Contributor (receives a contribution context and acts — push pattern).
- **Signature:** `ValueTask Contribute(IJavaScriptDeclarationsContributionContext context, CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IJavaScriptDeclarationContributor, MyContributor>()`.
- **Aggregated by:** the single `BuildDeclarationsDocument : IEventHandler<OnDeclarationsDocumentGenerating>` (this feature), which injects `IEnumerable<IJavaScriptDeclarationContributor>` and invokes each to build the TypeScript declaration document surfaced in the design editor.
- **Purpose:** contribute TypeScript type declarations, function signatures, variable names, etc. to the editor's IntelliSense document.

**Known implementations (shipped):**
- `Elsa.Expressions.JavaScript.Rendering` — `CommonDeclarationContributor` *(intra-domain — default, registers common built-in declarations)*
- `Elsa.Http.JavaScript` — `HttpTypeDeclarationContributor` *(cross-domain — adds HTTP-related types)*
- `Elsa.Workflows.Design.JavaScript` — `BindingPureArgsDeclarationContributor` *(cross-domain — exposes only immutable `args` for canonical binding expressions)*

---

## Events

`CatalogParityTests` scans `Elsa.Expressions.JavaScript.Rendering.Core` for `IEvent` types and asserts bidirectional alignment with `### On…` headings here.

### OnDeclarationsDocumentGenerating
`(IJavaScriptDeclarationsContributionContext Context)`

**Semantic.** The TypeScript declaration document for the JavaScript editor is being built. Contributors add their type declarations to `Context`. Sequential: the document must be complete before it is returned to the editor.

**Contributor interface.** `IJavaScriptDeclarationContributor` (above).

**Delivery strategy.** Sequential.

**Publication site.** The declarations endpoint handler (`Elsa.Expressions.JavaScript.Rendering`), on each request for the declaration document.

**Expected handler.** Exactly one: `BuildDeclarationsDocument` (this feature).

---

## Cross-references

- JS script pre/post processors (runtime execution): [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1.
