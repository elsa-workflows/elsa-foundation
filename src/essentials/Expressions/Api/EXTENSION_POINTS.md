# Extension points — Expressions API

`ExpressionsApiFeature` projects the registries owned by the Expressions domain through the canonical routes documented in [README.md](README.md). The API layer has no separate contributor system and no `Elsa.Workbench` dependency.

## Descriptor Sources

- `IExpressionDescriptorProvider` contributes expression-language descriptors. `IExpressionDescriptorRegistry` aggregates them for `GET /expressions/descriptors`.
- `IVariableTypeDescriptorProvider` contributes stable type aliases and editor metadata. `IVariableTypeDescriptorCatalog` aggregates them for `GET /expressions/variable-types`.

Providers are additive Sources. Duplicate stable identifiers must be treated according to the owning registry/catalog rules; API handlers only project the resolved snapshot. Replace registry/catalog implementations only when changing the single-owner aggregation strategy.

Evaluation handlers and language-specific seams are outside the API boundary. Their full contracts and shipped implementations are documented in the [Expressions domain extension catalog](../EXTENSION_POINTS.md).

Canonical ownership is defined in the [domain-owned API spec](../../../../specs/092-domain-owned-apis/spec.md); terminology is defined in the [Elsa glossary](../../../../docs/glossary/elsa.md).
