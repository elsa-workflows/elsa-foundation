# Extension points — Expressions domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Expressions` — the composition root where `ExpressionsFeature` builds `ExpressionDescriptorRegistry` by aggregating all `IExpressionDescriptorProvider` implementations at startup.

---

## Implementable contributor interfaces

### `IExpressionHandler` *(Core — `Elsa.Expressions.Core`)*
- **Kind:** Contributor (handles evaluation for a specific expression type).
- **Signature:** `ValueTask<object?> EvaluateAsync(IExpression expression, Type returnType, IExpressionExecutionContext context, IExpressionEvaluatorOptions options);`
- **Register:** `services.AddScoped<IExpressionHandler, MyHandler>()`. One registered handler per expression kind; `ExpressionEvaluator` resolves the matching handler by expression type at call time (not a fan-in aggregator — handlers are independent per expression kind).

**Known implementations (shipped):**
- `Elsa.Expressions` — `VariableExpressionHandler` *(intra-domain — handles variable-reference expressions)*
- `Elsa.Expressions.JavaScript` — `JavaScriptExpressionHandler` *(cross-domain — evaluates JS expressions via Jint)*
- `Elsa.Expressions.Liquid` — `LiquidExpressionHandler` *(cross-domain — evaluates Liquid templates)*

### `IExpressionDescriptorProvider` *(Core — `Elsa.Expressions.Core`)*
- **Kind:** Source (returns a set of expression descriptors — pull pattern).
- **Signature:** `IEnumerable<IExpressionDescriptor> GetDescriptors();`
- **Register:** `services.AddScoped<IExpressionDescriptorProvider, MyProvider>()`.
- **Consumed by:** `ExpressionDescriptorRegistry` (this feature) — aggregates all providers in its constructor (once, at DI build time). Not event-driven; the registry is a startup snapshot.

**Known implementations (shipped):**
- `Elsa.Expressions` — `DefaultExpressionDescriptorProvider` *(intra-domain — registers built-in expression types)*
- `Elsa.Expressions.JavaScript` — `JavaScriptExpressionDescriptorProvider` *(cross-domain — registers JS expression descriptor)*
- `Elsa.Expressions.Liquid` — `LiquidExpressionDescriptorProvider` *(cross-domain — registers Liquid expression descriptor)*

---

## Cross-references

- JavaScript expression extension points (pre/post processors, declaration contributors): [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript/EXTENSION_POINTS.md).
- JS rendering declaration contributors: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Liquid extension points: [`Elsa.Expressions.Liquid/EXTENSION_POINTS.md`](../Elsa.Expressions.Liquid/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
