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

Values enter scripts and bindings alike as plain, changeable JavaScript values through `IScriptValueBridge` (spec 1228);
a JavaScript input expression only reads, so any change the bridge reports fails its evaluation with `VF-BIND-001`, and
one the source shows is refused at publish. Each node that uses JavaScript is pinned to `JavaScriptValueAdapter.Version`
(`ExpressionValueAdapter`, registered by `JavaScriptFeature` and `JintFeature`); the runtime refuses a node with no pin
or another version (`VF-ADPT-001`).

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

## Overridable contracts

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
- **Host functions:** a host function that reads workflow variables is declared with
  `JavaScriptHostFunctionDeclaration(name, ReadsVariables: true)` (`Elsa.Expressions.JavaScript.Core`), registered in the
  service collection as a singleton; the analyzer then treats a call to it as an opaque read, so publish sets the
  fallback marker. The isolated engine has no way to install host functions yet, so a declaration only affects publish
  until one exists; such a function would read through the request's `IExpressionVariableReader`.

### IScriptValueBridge *(Jint — Elsa.Expressions.JavaScript.Jint)*

- **Kind:** replacement contract: exactly one implementation is active per host (spec 1228 FR-009, ADR 0046 V12).
- **Contract:** `Elsa.Expressions.JavaScript.Jint.Contracts.IScriptValueBridge` — `Begin(engine)` starts the values of
  one evaluation (`IScriptValueEvaluation`): `Enter(key, json)` puts a value in as a plain, changeable JavaScript value
  and records its lossy numbers; `Collect(key, current, assigned)` reports, after the run, unchanged, a member change
  (untouched members keep their stored text) or a whole assignment (a changed array is written whole); `Write(value,
  path)` converts a value leaving the evaluation, writing an unchanged entered value from its original JSON and
  reporting the lossy entered numbers the written JSON carries; `TryFindLoss(double)` serves host-function argument
  checks. Script activities and JavaScript input expressions use the same bridge (D-15).
- **Implementation:** `CopyComparePatchValueBridge` (internal), registered with `TryAddSingleton` by `JintFeature`.
  It compares JavaScript with JavaScript (ADR 0090 C2) and records its cost per evaluation on the
  `Elsa.Expressions.JavaScript` meter (`elsa.javascript.value_bridge.enter.duration`, `…collect.duration`, `…bytes`).
- **Override when:** a faster bridge (for example one built on proxies) behaves identically; replace the registration
  from a host feature (`services.Replace(...)`). One that behaves differently is a new JSON profile, so a new adapter
  version (`JavaScriptValueAdapter.Version`).
- **Contract text:** [`docs/reference/value-adapters.md`](../../../../../docs/reference/value-adapters.md).

### IJavaScriptPrecisionPolicy *(Core — Elsa.Expressions.JavaScript.Core)*

- **Kind:** replacement contract: exactly one policy is active per host, host-wide, with no per-workflow or per-node
  override (D-17, spec 1228 FR-012).
- **Contract:** `Elsa.Expressions.JavaScript.Core.Contracts.IJavaScriptPrecisionPolicy` — `Decide(context)` gets the
  target being written (`result` or `variables.<name>`) and each `JavaScriptPrecisionLoss` (path, the lossy entered
  number with its stored text, the written text, and the array written whole when there is one), and returns allow or
  refuse with one `JavaScriptPrecisionDiagnostic` per loss. A refusal faults the step with `VF-PREC-001`
  (`JavaScriptPrecisionException`); nothing of the step is written. Only writes that carry a loss reach the policy;
  computing with a lossy value and passing one to a host function warn (`VF-PREC-002`).
- **Implementation:** `RefusingPrecisionPolicy` (public, no options), registered with `TryAddSingleton` by `JintFeature`.
- **Override when:** a host wants other handling of a loss. Replace the registration from a host feature
  (`services.Replace(ServiceDescriptor.Singleton<IJavaScriptPrecisionPolicy, MyPolicy>())`); Elsa's default then does
  not run.

## Cross-references

- JS type declarations for the design surface: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Value-flow contract: [`../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`](../../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).
