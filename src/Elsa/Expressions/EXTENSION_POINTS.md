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

### `IScopedVariableProvider` *(Core — `Elsa.Expressions.Core`)*
- **Kind:** Optional capability implemented by an `IExpressionExecutionContext` (not DI-registered).
- **Signature:** `bool TryGetScopedVariable(VariableReference reference, out IVariable? variable);`
- **Purpose:** Lets `VariableExpressionHandler` resolve a structured `VariableReference` (reference key + declaring scope identity) through the context's visible scope chain — workflow scope plus visible ancestor container scopes — honouring nearest-scope visibility and shadowing (ADR 0027). Contexts that do not implement it fall back to workflow-scope name lookup, preserving prior behaviour. The reusable `VariableScope` chain (`Elsa.Expressions.Core.Models`) provides the resolution primitives.

---

## Cross-references

- JavaScript expression extension points (pre/post processors, declaration contributors): [`Elsa.Expressions.JavaScript/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript/EXTENSION_POINTS.md).
- JS rendering declaration contributors: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Liquid extension points: [`Elsa.Expressions.Liquid/EXTENSION_POINTS.md`](../Elsa.Expressions.Liquid/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
