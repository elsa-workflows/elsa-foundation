# Extension points — Expressions.JavaScript domain

The JavaScript binding surface is intentionally closed. `JavaScriptFeature` contributes a
`PortableJavaScriptExpressionHandler` to the portable evaluator and an authoring descriptor. A
binding receives only its serialized source, options, capability profile, and immutable declared
parameters.

Under `binding-readonly-v1` (ADR 0083, spec 1223) every expression and script has the native `Date`, `Temporal`, `Intl`
and `Math` (including `Math.random`). The clock is the host's `TimeProvider`, and every evaluation runs in UTC with the
invariant culture. Every expression and script also has the registered .NET-implemented functions. Both surfaces get the
same capabilities. Values cross as JSON: no live .NET object reaches a script (ADR 0046 V10).

There are no binding-time script pre/post-processors, events, library injectors, configuration host
functions, workflow contexts, raw Jint engine factories, mutable JavaScript execution contexts,
or service-location seams. The one way to add a function is a `JavaScriptHostFunctionDeclaration` (below).

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

## Extension points

### JavaScriptHostFunctionDeclaration *(Core — Elsa.Expressions.JavaScript.Core)*

- **Kind:** contribution (fan-in): one singleton registration per function.
- **Contract:** `JavaScriptHostFunctionDeclaration(name, implementation, ReadsVariables)` (spec 1223 FR-011, FR-027,
  FR-029). The implementation is a `JavaScriptHostFunction`: JSON values in, a JSON value out, with the evaluation's
  `IExpressionVariableReader` and cancellation on the call. A script `Date` argument arrives as ISO 8601 UTC text, and
  `undefined` as `null`. A thrown exception fails the call with a JavaScript error.
- **Names:** a host function is namespaced (`acme.lookup`) and is installed as a member of a read-only global object
  `acme`. The built-ins (`JavaScriptBuiltInFunctions`: `newGuid`, `newGuidString`, `getGuidString`, `newShortGuid`,
  `getShortGuid`, `parseGuid`, `stringToBase64`, `stringFromBase64`) are the only unprefixed functions; the Jint
  feature registers them. Shell activation fails, naming the function, on a duplicate, an unprefixed host function, a
  malformed name or a reserved namespace root: a read root (`variables`, `input`, `activities`, `workflow`, `args`), a
  built-in name, `complete`, `getVariable` or a JavaScript global.
- **Effects:** none. Every function is effect-free under ADR 0045 §5 D-6, and a call adds no commit boundary. The
  effectful declaration ships with its boundary (#4831).
- **Reads:** `ReadsVariables: true` makes publish treat a call as an opaque read and set the fallback marker; the
  function reads through the call's `Variables`.
- **Publish:** a script that declares `variables` gets the error `Scripts/ReservedName`. A script that declares `input`,
  `activities`, `workflow`, a built-in name or a namespace root gets the warning `Scripts/ShadowedFunction`.

## Overridable contracts

### IJavaScriptDotNetUsageAnalyzer *(Core — Elsa.Expressions.JavaScript.Core)*

- **Kind:** replacement contract: one per host, part of the default JSON-only engine strategy (spec 1223 FR-030).
- **Contract:** `Analyze(source, scriptBody, context)` refuses, quoting the expression:
  - a reference to a catalogued .NET type (`new Uri(s)`, `Enumerable.Range(…)`);
  - an invocation of a PascalCase member on a value the script did not define (`items.Where(f)`);
  - with a `JavaScriptDotNetUsageContext` (the Elsa 3 converter), member use on a value that was a live .NET object.

  It allows JSON property reads of any casing, calls on JavaScript globals and registered namespaces, members and names
  the script defines, and computed access.
- **Implementation:** `JavaScriptDotNetUsageAnalyzer` (Acornima), registered with `TryAdd` by `JavaScriptFeature`.
- **Consumers:** `ScriptOutcomeValidator` (`Scripts/DotNetMemberUsage`), the expression tooling
  (`JavaScript/DotNetMemberUsage`) and the Elsa 3 converter (`VF-IMP-012`).
- **Override when:** the opt-in .NET interop strategy (#4103) replaces the default strategy and decides whether this
  analysis applies under it.

### IJavaScriptScriptOutcomeAnalyzer *(Core — Elsa.Expressions.JavaScript.Core)*

- **Contract:** `Elsa.Expressions.JavaScript.Core.Contracts.IJavaScriptScriptOutcomeAnalyzer` —
  `Analyze(script)` parses a script-activity program (never evaluates it) and returns a
  `JavaScriptScriptOutcomeAnalysis`, or `null` when the script does not parse:
  - `LiteralOutcomes` — string literals passed as the outcomes of `complete(_, 'X' | ['X', …])`
    calls anywhere, nested functions included;
  - `ReturnedStrings` — string literals returned at the top level;
  - `ReservedNameDeclarations` — zero-based ranges of every declaration of `complete`.
  Computed names and aliased `complete` calls are not reported; they are checked at run time.
- **Implementation:** `JavaScriptScriptOutcomeAnalyzer` (internal), registered as a singleton by
  `JavaScriptFeature`: it is stateless, holds no scoped dependency and only parses. It shares the reserved-name collector
  with the expression tooling provider.
- **Consumer:** `ScriptOutcomeValidator` in `Elsa.Workflows.Design.Validations` takes it as an
  optional dependency, so that project needs no JavaScript parser; with no analyzer registered,
  scripts are not checked.
- **Override when:** a host uses another JavaScript parser or dialect for script activities. Register one analyzer; a
  later registration replaces the default, as for `IJavaScriptScriptEvaluator`.

### IScriptVariableReadAnalyzer *(Core — Elsa.Expressions.Core)*

- **Kind:** replacement contract per language: exactly one analyzer per language, any lifetime (JavaScript registers
  a singleton, Liquid a scoped one, since it holds the scoped `FluidParser`). Two of different types for one language
  fail the shell at start through `DataSelectionAnalyzerCompositionCheck` (Design.Validations, which Publishing and the
  Design API depend on).
- **Contract:** `Elsa.Expressions.Core.Contracts.IScriptVariableReadAnalyzer` (spec 1222 FR-014) —
  `Analyze(source, scriptBody)` parses a script (never evaluates it) and returns a `ScriptVariableReadAnalysis`, or
  `null` when it does not parse: `LiteralNames` read as `variables.X`, `variables['X']` or `getVariable('X')`, and
  `HasComputedRead` when it also reads variables it does not name (`variables[expr]`, `getVariable(expr)`, `variables`
  or `getVariable` used as a value or declared by the script).
- **Implementation:** `JavaScriptVariableReadAnalyzer` (internal, Acornima), registered as a singleton by
  `JavaScriptFeature`. One analyzer per language; the Liquid feature registers its own.
- **Consumer:** `ScriptVariableReadResolver` in `Elsa.Workflows.Design.Validations`, used by the publish compiler to
  pin literal reads and set the fallback marker, by `ScriptVariableReadValidator` for the draft diagnostics and by the
  rename planner.
- **Override when:** a host uses another JavaScript parser or dialect. Replace the default registration (remove it and
  register its own): two analyzers of different types for one language fail the shell at start, so none wins silently.
- **Host functions:** a call to a function declared with `ReadsVariables: true` (by its name, or as
  `namespace.member`) is an opaque read, so publish sets the fallback marker. See `JavaScriptHostFunctionDeclaration`
  above.

## Cross-references

- JS type declarations for the design surface: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Value-flow contract: [`../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`](../../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).
