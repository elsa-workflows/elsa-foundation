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

Values enter scripts and bindings alike as plain, changeable JavaScript values through `IScriptValueBridge` (spec 1228);
a JavaScript input expression only reads, so any change the bridge reports fails its evaluation with `VF-BIND-001`, and
one the source shows is refused at publish.

`IJavaScriptScriptEvaluator` is a separate typed activity-script contract used by `RunJavaScript`.
It accepts one explicit JSON argument document and returns a `JavaScriptScriptEvaluationResult`: the
script's JSON result and, on a separate channel, its outcome selection (`Unspecified`, `Named` with
the selected names in order, or `Empty`). It is not registered as an expression handler, cannot
evaluate canonical input bindings, and shares only the closed `IsolatedJintEngine` implementation
with portable expression evaluation.

- **Variables** (spec 1222 FR-033, spec 1228 FR-006, FR-007): the request's `Variables`
  (`IScriptVariableSession`, `Elsa.Expressions.JavaScript.Core`) gives the body the `variables` root,
  `getVariable` and `setVariable(name, value)` (a whole assignment, spec 1231 FR-005; only a body with a session gets
  it, never an input expression), read through the host's session (each read recorded; the committed value plus the step's own
  writes). After the run, each change the bridge reports goes through the precision policy and then back through
  `Write` as a whole value (a member change patched into the stored value); every change is checked before any is
  written. A change made without assigning the variable is still written and raises `VF-WRITE-001` (ADR 0090 C2).
  When `HasReadProtectedValue` is set, the evaluator reports warnings in their protected form. A variable
  `IsReadOnly` reports (a loop's item or index) enters deep-frozen, and assigning it throws, so a change fails where
  the script makes it (spec 1231 FR-006). The implementation
  is `StepScriptVariables` in `Elsa.Activities.Scripting`, over the step's `IStepVariableSession`; without a session
  the body has no `variables` root.

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
- **Registration:** `services.AddSingleton(new JavaScriptHostFunctionDeclaration("acme.lookup", call => ...))`.
- **Aggregation:** `JavaScriptHostFunctions` (Jint, internal) collects every declaration, checks the naming rule at shell
  activation (`JavaScriptHostFunctionCheck`) and installs the functions into each evaluation. The analyzers, the
  expression tooling and the Elsa 3 converter (`Elsa3ExpressionRewriter`, which matches an Elsa 3 global to the
  function with that member name, #5664) read the same registrations.
- **Known implementations:** the eight built-ins, registered by `JintFeature` (intra-domain, `JavaScriptBuiltIns`).
- **Publish:** a script that declares `variables` gets the error `Scripts/ReservedName`. A script that declares `input`,
  `activities`, `workflow`, a built-in name or a namespace root gets the warning `Scripts/ShadowedFunction`, and an
  input expression the warning `JavaScript/ShadowedFunction`.

## Overridable contracts

### IJavaScriptDotNetUsageAnalyzer *(Core — Elsa.Expressions.JavaScript.Core)*

- **Kind:** replacement contract (framework §2.6.2): one per host, part of the default JSON-only engine strategy (spec
  1223 FR-030). A second registration fails shell activation (`JavaScriptDotNetUsageAnalyzerCheck`).
- **Contract:** `Analyze(source, scriptBody, context)` refuses, quoting the expression:
  - a reference to a catalogued .NET type (`new Uri(s)`, `Enumerable.Range(…)`);
  - an invocation of a PascalCase member on a value the script did not define (`items.Where(f)`);
  - with a `JavaScriptDotNetUsageContext` (the Elsa 3 converter), member use on a value that was a live .NET object.

  It allows JSON property reads of any casing, calls on JavaScript globals and registered namespaces, members and names
  the script defines, and computed access.
- **Implementation:** `JavaScriptDotNetUsageAnalyzer` (internal, Acornima), registered with `TryAdd` by `JavaScriptFeature`.
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
  - `ReservedNameDeclarations` — zero-based ranges of every declaration of `complete`;
  - `HasReturnStatement` — whether the outer script body contains a return statement. Returns inside nested
    functions do not count. A consumed script-activity result requires one at publication; input expressions and
    statement completion forms do not. Replacement analyzers must report this field for valid bound bodies.
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
  or `getVariable` used as a value or declared by the script). `Writes` lists each variable, or member of one, that
  the script visibly assigns, updates or deletes (spec 1228 FR-006a), including through a destructuring pattern, a
  `for…in`/`for…of` head, a parenthesized value (`(variables.o || {}).x = 1`) and, in a script body,
  `setVariable('X', v)`; each `ScriptVariableWrite` carries its source offset (`Start`). `Escapes` lists the literal
  names whose value the script lets escape (a binding, an argument, a container, a closure's return, a method call on
  it), and `HasComputedWrite` says it writes variables it does not name (spec 1231 FR-019, FR-020a); `MayWriteNames`
  joins `Writes` and `Escapes`. An analyzer that does not report writes leaves only the run-time `VF-BIND-001` check
  for input expressions, and a script body's may-write set empty.
- **Implementation:** `JavaScriptVariableReadAnalyzer` (internal, Acornima), registered as a singleton by
  `JavaScriptFeature`. One analyzer per language; the Liquid feature registers its own.
- **Consumer:** `ScriptVariableReadResolver` in `Elsa.Workflows.Design.Validations`, used by the publish compiler to
  pin literal reads and set the fallback marker and to refuse an input expression that writes (`VF-BIND-001`), by
  `ScriptVariableReadValidator` for the draft diagnostics and by the rename planner. For a script body (spec 1231):
  publication's `VF-WRITE-002`–`004` refusals and pinned may-write set (`StepVariableWriteMetadata`),
  `StepVariableWriteValidation` (the concurrent-write and overlap warnings), and `ScriptBodyWriteAnalyzer` (Studio's
  may-write display).
- **Override when:** a host uses another JavaScript parser or dialect. Replace the default registration (remove it and
  register its own): two analyzers of different types for one language fail the shell at start, so none wins silently.
- **Host functions:** a call to a function declared with `ReadsVariables: true` (by its name, or as
  `namespace.member`) is an opaque read, so publish sets the fallback marker. See `JavaScriptHostFunctionDeclaration`
  above.

### IScriptValueBridge *(Jint — Elsa.Expressions.JavaScript.Jint)*

- **Kind:** replacement contract: exactly one implementation is active per host (spec 1228 FR-009, ADR 0046 V12).
- **Contract:** `Elsa.Expressions.JavaScript.Jint.Contracts.IScriptValueBridge` — `Begin(engine)` (policies off) or `Begin(engine, undefinedAsNull)` starts the values of
  one evaluation (`IScriptValueEvaluation`): `Enter(key, json)` puts a value in as a plain, changeable JavaScript value
  and records its lossy numbers; `Collect(key, current, assigned)` reports, after the run, unchanged, a member change
  (untouched members keep their stored text) or a whole assignment (a changed array is written whole);
  `Preview(key, current, assigned)` gives what `Collect` would report during the run, remembering nothing, for host
  functions reading a variable (spec 1222 FR-034); `Write(value,
  path)` converts a value leaving the evaluation, writing an unchanged entered value from its original JSON and
  reporting the lossy entered numbers the written JSON carries; `TryFindLoss(double)` serves host-function argument
  checks. `Warnings` exposes boundary diagnostics for omitted undefined properties, null array positions and
  non-finite numbers, with value-free paths; callers apply the evaluation's protection before retaining them.
  Script activities and JavaScript input expressions use the same bridge (D-15).
- **Implementation:** `CopyComparePatchValueBridge` (internal), registered with `TryAddSingleton` by `JintFeature`.
  It compares JavaScript with JavaScript (ADR 0090 C2) and records its cost per evaluation on the
  `Elsa.Expressions.JavaScript` meter (`elsa.javascript.value_bridge.enter.duration`, `…collect.duration`, `…bytes`).
- **Consumer:** `JintJavaScriptScriptEvaluator` (script activities) and `JintPortableJavaScriptEvaluator` (input
  expressions), through `IsolatedJintEngine` and `JavaScriptValueWrites`.
- **Override when:** a faster bridge (for example one built on proxies) behaves identically; replace the registration
  from a host feature (`services.Replace(...)`). One that behaves differently is a new JSON profile, so a new adapter
  version of the contract in `docs/reference/value-adapters.md` (FR-028).
- **Contract text:** [`docs/reference/value-adapters.md`](../../../../../docs/reference/value-adapters.md).

### IJavaScriptPrecisionPolicy *(Core — Elsa.Expressions.JavaScript.Core)*

- **Kind:** replacement contract: exactly one policy is active per host, host-wide, with no per-workflow or per-node
  override (D-17, spec 1228 FR-012).
- **Contract:** `Elsa.Expressions.JavaScript.Core.Contracts.IJavaScriptPrecisionPolicy` — `Decide(context)` gets the
  target being written (`result` or `variables.<name>`) and each `JavaScriptPrecisionLoss` (path, the lossy entered
  number with its stored text, the written text, and the array written whole when there is one), and returns allow or
  refuse with one `JavaScriptPrecisionDiagnostic` per loss. A refusal faults the step with `VF-PREC-001`
  (`JavaScriptPrecisionException`); nothing of the step is written. Only writes that carry a loss reach the policy;
  computing with a lossy value warns (`VF-PREC-002`), and so does passing one to a host function (spec 1223 FR-028): the warning names the function and the argument path, and the call runs.
- **Implementation:** `RefusingPrecisionPolicy` in `Elsa.Expressions.JavaScript.Jint` (public, no options), registered
  with `TryAddSingleton` by `JintFeature`.
- **Consumer:** `JavaScriptValueWrites.Apply`, called by `JintJavaScriptScriptEvaluator` and
  `JintPortableJavaScriptEvaluator` for each write that carries a loss.
- **Override when:** a host wants other handling of a loss. Replace the registration from a host feature
  (`services.Replace(ServiceDescriptor.Singleton<IJavaScriptPrecisionPolicy, MyPolicy>())`); Elsa's default then does
  not run.

## Cross-references

- JS type declarations for the design surface: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md).
- Base expression descriptor registration: [`Elsa.Expressions/EXTENSION_POINTS.md`](../../Elsa.Expressions/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../../../../EXTENSION_POINTS.md).
- Value-flow contract: [`../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md`](../../../../../specs/095-value-flow-redesign/contracts/expression-and-import-contract.md).

### Script workflow state (spec 1234)

- **Kind:** body-only capability contract `IScriptWorkflowState` in Expressions.JavaScript.Core, supplied as the optional `JavaScriptScriptEvaluationRequest.WorkflowState` member.
- **Usage:** the Jint evaluator installs a fixed, non-extensible `workflowState` host object. Getters return JSON copies/string/null; setters return undefined. Ordinary input evaluators never receive it, and arguments cannot grant it.
- **Refusals:** adapters latch host refusals and recheck after evaluation and serialization. Output writes use the existing value bridge/precision policy. `requireExactIdentity` is the generic runtime `VF-STATE-005` guard; migration names and literal diagnostics stay in Elsa3.Mapping.
- **Authoring:** the language parser reports independent `ScriptWorkflowStateEffects`; consumers pin known targets or conservative computed/opaque access separately from variable writes.
