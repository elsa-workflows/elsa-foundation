# Extension points — Expressions.JavaScript domain

The JavaScript binding surface is intentionally closed. `JavaScriptFeature` contributes a
`PortableJavaScriptExpressionHandler` to the portable evaluator and an authoring descriptor. A
binding receives only its serialized source, options, capability profile, and immutable declared
parameters.

There are no binding-time script pre/post-processors, events, library injectors, configuration host
functions, workflow contexts, raw Jint engine factories, mutable JavaScript execution contexts,
delegate-backed host functions, or service-location seams. Extending the binding language requires
a separately reviewed capability profile; a global registration cannot join `binding-pure-v1`.

Jint reuses immutable prepared syntax under weak source-string ownership, with separate preparation for value
bindings and script activities. A bounded, hash-only weak index also reuses script preparation across freshly
hydrated equal source strings; dead source/preparation is not kept alive by that index. Arguments, variables,
limits, cancellation and engine state remain per evaluation;
discarding executable source also releases its preparation. No engine or evaluated result is cached.

`IJavaScriptScriptEvaluator` is a separate typed activity-script contract used by `RunJavaScript`.
It accepts one explicit JSON argument document and returns a `JavaScriptScriptEvaluationResult`: the
script's JSON result and, on a separate channel, its outcome selection (`Unspecified`, `Named` with
the selected names in order, or `Empty`). It is not registered as an expression handler, cannot
evaluate canonical input bindings, and shares only the closed `IsolatedJintEngine` implementation
with portable expression evaluation.

- **Built-in `complete(result, outcomes)`** (spec 1208 FR-007): installed for script activities only.
  A script selects outcomes solely by *returning* `complete(result, outcomes)`, where `outcomes` is
  omitted, `undefined`, a non-blank string or an array of non-blank strings (`[]` is an explicitly
  empty selection). Any other returned value is the result alone, with an unspecified selection.
- **Invalid completion → `JavaScriptCompletionException`** (`VF-ACT-006`): a completion value nested
  in returned data, or outcomes of another shape. The activity faults with that code.
- **`complete` is reserved in every JavaScript surface** (`JavaScriptScriptBuiltIns.Complete`, ADR
  0045 §5): a script or expression that declares it (variable, function, class, parameter or catch
  binding, through any destructuring, default or rest pattern) is refused at design time with
  `JavaScript/ReservedName`.

## IJavaScriptScriptOutcomeAnalyzer (design-time contract)

- **Contract:** `Elsa.Expressions.JavaScript.Core.Contracts.IJavaScriptScriptOutcomeAnalyzer` —
  `Analyze(script)` parses a script-activity program (never evaluates it) and returns a
  `JavaScriptScriptOutcomeAnalysis`, or `null` when the script does not parse:
  - `LiteralOutcomes` — string literals passed as the outcomes of `complete(_, 'X' | ['X', …])`
    calls anywhere, nested functions included;
  - `ReturnedStrings` — string literals returned at the top level;
  - `ReservedNameDeclarations` — zero-based ranges of every declaration of `complete`.
  Computed names and aliased `complete` calls are not reported; they are checked at run time.
- **Implementation:** `JavaScriptScriptOutcomeAnalyzer` (internal), registered as a singleton by
  `JavaScriptFeature`. It shares the reserved-name collector with the expression tooling provider.
- **Consumer:** `ScriptOutcomeValidator` in `Elsa.Workflows.Design.Validations` takes it as an
  optional dependency, so that project needs no JavaScript parser; with no analyzer registered,
  scripts are not checked.
- **Cardinality:** a replacement contract — one analyzer per host.

## Cross-references

- JS type declarations for the design surface: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Value-flow contract: [`../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`](../../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).
