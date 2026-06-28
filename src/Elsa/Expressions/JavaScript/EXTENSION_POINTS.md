# Extension points — Expressions.JavaScript domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Expressions.JavaScript` — the composition root where `JavaScriptFeature` registers `PreProcessScript` and `PostProcessScript` handlers, the Jint evaluator, and the built-in pre-processors.

---

## Implementable contributor interfaces

### `IScriptPreProcessor` *(Core — `Elsa.Expressions.JavaScript.Core`)*
- **Kind:** Contributor (receives execution context and acts — push pattern).
- **Signature:** `ValueTask PreProcess(string script, IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IScriptPreProcessor, MyPreProcessor>()`.
- **Aggregated by:** the single `PreProcessScript : IEventHandler<OnEvaluatingScript>` (this feature), which injects `IEnumerable<IScriptPreProcessor>` and invokes each before the script executes.
- **Purpose:** inject variables, functions, type declarations, and engine configuration into the Jint context before execution.

**Known implementations (shipped):**
- `Elsa.Expressions.JavaScript` — `TypeRegistrationPreProcessor` *(intra-domain — default)*
- `Elsa.Expressions.JavaScript` — `ConfigurationAccessFunctionPreProcessor` *(intra-domain)*
- `Elsa.Expressions.JavaScript` — `CommonFunctionsPreProcessor` *(intra-domain)*
- `Elsa.Expressions.JavaScript` — `ArgumentFunctionsPreProcessor` *(intra-domain)*
- `Elsa.Expressions.JavaScript` — `ArgsObjectPreProcessor` *(intra-domain)*
- `Elsa.Expressions.JavaScript.Libraries` — `LibraryResourcePreProcessor` *(cross-domain — injects library resources)*
- `Elsa.Workflows.Runtime.JavaScript` — `VariableFunctionsPreProcessor` *(cross-domain — resolves get/set variable helpers through the visible scope chain via `IScopedVariableProvider` when the expression context exposes one, else workflow scope; ADR 0027)*
- `Elsa.Workflows.Runtime.JavaScript` — `WorkflowFunctionsPreProcessor` *(cross-domain)*
- `Elsa.Workflows.Runtime.JavaScript` — `WorkflowInputFunctionsPreProcessor` *(cross-domain)*
- `Elsa.Workflows.Runtime.JavaScript` — `ActivityOutputFunctionsPreProcessor` *(cross-domain)*
- `Elsa.Workflows.Runtime.JavaScript` — `WorkflowVariablesContextPreProcessor` *(cross-domain)*

### `IScriptPostProcessor` *(Core — `Elsa.Expressions.JavaScript.Core`)*
- **Kind:** Contributor (receives execution context after the script has run and acts — push pattern).
- **Signature:** `ValueTask PostProcess(IJavaScriptExecutionContext executionContext, IExpressionExecutionContext expressionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken);`
- **Register:** `services.AddScoped<IScriptPostProcessor, MyPostProcessor>()`.
- **Aggregated by:** the single `PostProcessScript : IEventHandler<OnScriptEvaluated>` (this feature).
- **Purpose:** copy outputs, extract results, or clean up after script execution.

**Known implementations (shipped):**
- `Elsa.Workflows.Runtime.JavaScript` — `CopyVariablesToWorkflowContext` *(cross-domain — copies JS variables back into workflow context)*

---

## Events

Both events are `IEvent` (framework §2.6.1), Sequential / contribution strategy.

`CatalogParityTests` scans `Elsa.Expressions.JavaScript.Core` for `IEvent` types and asserts bidirectional alignment with `### On…` headings here.

### OnEvaluatingScript
`(string Script, IJavaScriptExecutionContext ExecutionContext, IExpressionExecutionContext ExpressionContext, IExpressionEvaluatorOptions? Options)`

**Semantic.** A JavaScript expression is about to be evaluated. Pre-processors enrich the engine context. Sequential: the evaluator must see a fully-prepared engine.

**Contributor interface.** `IScriptPreProcessor` (above).

**Delivery strategy.** Sequential.

**Publication site.** `JavaScriptExpressionHandler` (`Elsa.Expressions.JavaScript`), before script execution.

**Expected handler.** Exactly one: `PreProcessScript` (this feature).

### OnScriptEvaluated
`(IJavaScriptExecutionContext ExecutionContext, IExpressionExecutionContext ExpressionContext, IExpressionEvaluatorOptions? Options)`

**Semantic.** A JavaScript expression has finished executing. Post-processors copy outputs or clean up. Sequential: the caller must receive a fully-extracted result.

**Contributor interface.** `IScriptPostProcessor` (above).

**Delivery strategy.** Sequential.

**Publication site.** `JavaScriptExpressionHandler` (`Elsa.Expressions.JavaScript`), after script execution.

**Expected handler.** Exactly one: `PostProcessScript` (this feature).

---

## Cross-references

- JS type declarations for the design surface: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.6.6 + §2.22.1.
