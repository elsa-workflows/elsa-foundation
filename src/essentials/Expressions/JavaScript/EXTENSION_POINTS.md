# Extension points — Expressions.JavaScript domain

The JavaScript binding surface is intentionally closed. `JavaScriptFeature` contributes a
`PortableJavaScriptExpressionHandler` to the portable evaluator and an authoring descriptor. A
binding receives only its serialized source, options, capability profile, and immutable declared
parameters.

There are no binding-time script pre/post-processors, events, library injectors, configuration host
functions, workflow contexts, raw Jint engine factories, mutable JavaScript execution contexts,
delegate-backed host functions, or service-location seams. Extending the binding language requires
a separately reviewed capability profile; a global registration cannot join `binding-pure-v1`.

`IJavaScriptScriptEvaluator` is a separate typed activity-script contract used by `RunJavaScript`.
It accepts one explicit JSON argument document and returns JSON. It is not registered as an
expression handler, cannot evaluate canonical input bindings, and shares only the closed
`IsolatedJintEngine` implementation with portable expression evaluation.

## Cross-references

- JS type declarations for the design surface: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Value-flow contract: [`../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`](../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).
