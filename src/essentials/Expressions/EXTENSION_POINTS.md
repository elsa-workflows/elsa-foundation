# Extension points — Expressions domain

`ExpressionsFeature` builds the authoring descriptor registry and the portable evaluator. Expression
descriptors are metadata only; they do not carry runtime handler factories.

## Implementable contributor interfaces

### `IPortableExpressionHandler` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Language evaluator for one explicit capability profile.
- **Signature:** `ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request)`.
- **Register:** `services.AddScoped<IPortableExpressionHandler, MyPortableHandler>()`.
- **Boundary:** The request contains portable source/options and immutable declared parameters. It
  exposes no workflow context, variable frame, service provider, delegate, or mutation callback.

Shipped implementations are `PortableJavaScriptExpressionHandler` and
`PortableLiquidExpressionHandler`, both restricted to `binding-pure-v1`.

### `IExpressionDescriptorProvider` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Authoring metadata source.
- **Signature:** `IEnumerable<IExpressionDescriptor> GetDescriptors()`.
- **Consumed by:** `ExpressionDescriptorRegistry`, which creates a startup snapshot.

### `IExpressionToolingProvider` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Language-specific, metadata-only authoring assistance provider.
- **Registration:** one provider per exact expression descriptor type.
- **Boundary:** receives a revisioned, Design-filtered metadata snapshot; it must not evaluate
  source, access runtime values, or retain source. Duplicate types fail deterministic resolver
  construction. The optional tooling capability is absent when no language provider is composed.
- **Declared catalog:** `DeclaredCatalog` may expose an immutable `ExpressionToolingCatalog`
  containing Core symbols and an opaque profile revision; its default is `null` for legacy
  providers. The scoped Design context service adds only the selected provider's declared symbols,
  applies the existing symbol filters to those profile candidates, and then searches or pages the
  combined catalog. Profiles are limited to 500 unique nonempty symbol IDs and the combined
  context to 5,500 symbols. Providers must use the symbols in the filtered context and must not
  reinsert profile entries that were omitted by policy. Context and symbol-catalog revisions
  incorporate the profile revision independently; a provider without a catalog leaves both source
  revisions unchanged.

### `IVariableTypeDescriptorProvider` *(Core — `Elsa.Expressions.Core`)*

- **Kind:** Authoring/schema type source.
- **Signature:** `IEnumerable<TypeDescriptor> GetDescriptors()`.
- **Consumed by:** `VariableTypeDescriptorCatalog` and alias-registry seeding.

## Cross-references

- JavaScript binding boundary: [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript/EXTENSION_POINTS.md).
- Liquid binding boundary: [`Elsa.Expressions.Liquid/EXTENSION_POINTS.md`](../Elsa.Expressions.Liquid/EXTENSION_POINTS.md).
- JS rendering declaration contributors: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
