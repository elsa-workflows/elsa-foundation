# Extension points — Expressions.Liquid domain

`LiquidExpressionsFeature` contributes a `PortableLiquidExpressionHandler`, an authoring descriptor,
and a singleton tooling provider. The handler creates a fresh model-less Fluid context containing
only immutable declared parameter roots. The module's immutable `LiquidExpressionProfile` is the
shared source for runtime parser configuration and the provider's `DeclaredCatalog`; the catalog is
Core metadata and contains no Fluid object, service, callback, or runtime value.

The default profile is pinned to the Fluid 2.31.0 built-in registries. Runtime scopes receive fresh
parsers, evaluations receive fresh `TemplateOptions`, and validation creates a fresh parser from the
same profile. Time-dependent filters (`date`, `format_date`, and `time_zone`) are denied. `include`
and `render` are removed from the executable parser surface, and `macro`/`from` remain absent because
functions are disabled. The template options keep a null file provider, fixed Unix-epoch time, UTC,
invariant culture, and throwing undefined-name behavior. Direct tooling callers must pass an
authorized context containing the provider's declared catalog; the provider never appends profile
symbols after policy filtering.

Hosts can register a trusted profile before configuring the feature:

```csharp
services.AddSingleton(LiquidExpressionProfile.Create(
    revision: "host-managed-opaque-revision",
    symbols: completeTagAndFilterCatalog,
    configureParser: parser => parser.RegisterEmptyTag("tenant_label", RenderTenantLabel),
    configureTemplateOptions: options => options.Filters.AddFilter("tenant_label", TenantLabelFilter)));
```

The supplied catalog must describe the complete effective profile, including any standard tags and
filters retained by the host. The parser and options recipes are applied to fresh objects; they must
be stateless and must register exactly the syntax described by the catalog. The opaque revision must
change when that metadata or its runtime configuration changes. Reserved time filters and file-backed
tags cannot be re-enabled in a binding-pure profile.

Replacing only the scoped `FluidParser` remains supported for legacy runtime hosts. Its custom
registrations continue to execute, but the default tooling profile does not infer metadata from that
unknown parser. To expose custom syntax to authoring, register a trusted `LiquidExpressionProfile`
whose parser/options recipes and catalog describe the same runtime configuration. Do not add custom
symbols directly in the provider or after context policy filtering.

## Cross-references

- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Value-flow contract: [`../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`](../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).
