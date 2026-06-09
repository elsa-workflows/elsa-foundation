# Extension points — Expressions.Liquid domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Expressions.Liquid` — a provider/feature project (no `.Core`). One event applies; no contributor interfaces or overridable contracts.

---

## Events

### `RenderingLiquidTemplate`
`(TemplateContext TemplateContext, IExpressionExecutionContext Context)`

**Semantic.** A Liquid template is about to be rendered. Handlers can configure the `TemplateContext` — register custom filters, tags, or value converters — before the template executes.

**Delivery strategy.** Sequential — the template context must be fully configured before rendering begins.

**Publication site.** `LiquidExpressionHandler` (`Elsa.Expressions.Liquid`), before template execution.

**Expected handler.** `ConfigureLiquidEngine` (this feature) — configures the Fluid/Liquid engine defaults.

> **Naming note.** This event uses the present-participle `RenderingXxx` pattern rather than the `OnXxx` convention used elsewhere in the repo. It is an existing seam and is intentionally not renamed here.

---

## Cross-references

- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
