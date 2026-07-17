# Extension points — Expressions.Liquid domain

The Liquid binding surface is intentionally closed. `LiquidExpressionsFeature` contributes a
`PortableLiquidExpressionHandler` and authoring descriptor. The handler creates a fresh model-less
Fluid context containing only immutable declared parameter roots.

There is no rendering event, ambient workflow context, configuration access, service-backed filter
registration, or mutable template-manager seam for canonical bindings. Time-dependent filters and
undeclared roots are rejected by the `binding-pure-v1` capability profile.

## Cross-references

- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Value-flow contract: [`../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`](../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).
