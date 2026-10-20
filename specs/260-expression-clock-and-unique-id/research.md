# Research: Clock and UniqueId parameters and binding-pure-v2

Measured on `947cb78e1`. Decisions here refine [ADR 0088](../../docs/adr/0088-expressions-read-time-and-unique-values-through-recorded-parameters.md);
they do not change it.

## R-1 A Date without a clock in Jint 4.9.1

Probed in a throwaway program against the `Jint` 4.9.1 package the repository pins.

**Facts.**

- `Options.TimeSystem` takes an `ITimeSystem` with four members: `GetUtcNow()`, `DefaultTimeZone`,
  `TryParse(string, out long)` and `GetUtcOffset(long)`.
- Jint parses every date string through `ITimeSystem.TryParse`, including ISO strings. The default
  `DefaultTimeSystem` falls back to .NET parsing, so `new Date('10/20/2026')`, `Date.parse('20 October 2026')` and
  `new Date('Tue Oct 20 2026')` all parse today. That breaks FR-016 and depends on culture.
- With a time system whose `TryParse` accepts only the ECMAScript date-time string format and otherwise delegates to
  `new DefaultTimeSystem(utc, CultureInfo.InvariantCulture).TryParse`:
  - date-only, year-month, year, date-time with and without seconds and milliseconds, `Z`, numeric offsets and
    expanded years parse as ECMAScript specifies;
  - `2026-02-30`, `T25:00`, four fraction digits and every non-ISO form give `NaN`;
  - date-time strings without an offset are read as UTC, because the default time zone is UTC.
- With the default time zone UTC, `getHours()` equals `getUTCHours()`, and `toString()` is
  `Thu Jan 01 1970 00:00:00 GMT+0000 (Coordinated Universal Time)`. The trailing name comes from the time zone's
  display name. `TimeZoneInfo.Utc`'s display name can be localized on some hosts, so the plan uses a custom
  zone created with fixed English names.
- A JavaScript prelude can replace the global `Date` with a function that throws on a call without `new` and on a
  construction with no arguments, and otherwise forwards with `Reflect.construct(NativeDate, args, new.target)`.
  Pointing its `prototype` at the native prototype and setting `prototype.constructor` keeps `instanceof Date`,
  `new Date(x).constructor === Date`, subclassing and `JSON.stringify` working. `Date.now` and the `toLocale*`
  methods are replaced with functions that throw. `toISOString` is wrapped so an invalid date throws the FR-017
  message instead of Jint's generic `RangeError: Invalid time value`.

**Decision.**

- `binding-pure-v2` engines set `TimeSystem` to an internal `ClocklessTimeSystem`:
  - `GetUtcNow()` throws, as a safety net under the prelude;
  - `DefaultTimeZone` is a fixed custom UTC zone;
  - `TryParse` matches `^(?:[+-]\d{6}|\d{4})(?:-\d{2}(?:-\d{2})?)?(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d{3})?)?(?:Z|[+-]\d{2}:\d{2})?)?$`
    before delegating;
  - `GetUtcOffset` returns zero.
- They also set `Culture` to the invariant culture, and run a frozen prelude that installs the clockless `Date`.
- `binding-pure-v1` engines are unchanged: `Date` stays undefined.

**Residual difference.** ECMAScript expects `Date.parse(d.toString())` to round-trip; under v2 it gives `NaN`
because only ISO strings parse. This is accepted and documented, since `toISOString()` is the format to exchange.

**Alternatives rejected.**

- Only an `ITimeSystem` whose `GetUtcNow()` throws: `new Date()`, `Date()` and `Date.now()` would all fail with one
  generic host message, which does not meet FR-017.
- A `Date` subclass (`class extends Date`): calling it without `new` gives the engine's generic class error.
- Writing an ISO parser: the regex plus Jint's own parser gives ECMAScript semantics with no new parsing code.

## R-2 Where the materialization instant reaches parameter resolution

**Facts.**

- `RuntimeActivityInputMaterializer.MaterializeSnapshotAsync(node, invocationId, context, materializedAt, ...)` is the
  only producer of an `ActivityInputSnapshot`. Its `materializedAt` becomes `ActivityInputSnapshot.MaterializedAt`.
- It is reached only through `RuntimeActivityInputSnapshotMaterializer.MaterializeAsync`. The callers are the start
  handler (`WorkflowStartActivitySchedulerWorkHandler.cs:367`) and the `While` re-materialization
  (`WorkflowParentActivityCompletionSchedulerWorkHandler.cs:191`, with `TimeProvider.GetUtcNow()` at :192).
- Parameters are resolved in `RuntimePortableExpressionEvaluator.MaterializeParametersAsync` from a
  `RuntimeInputBindingResolutionContext`, which today carries no instant.
- The snapshot stores each input's evaluated value, not the parameter values. The `Clock` and `UniqueId` values are
  therefore pinned through the inputs evaluated from them, which is what ADR 0088 D2 needs: a retry reads the
  committed inputs.
- `WorkflowIntrinsicExecutor.MaterializeValueAsync` (`:586-600`) builds a second context for intrinsic inputs
  (`Set` values, `Control` outcomes and other required intrinsic inputs). Their results are fixed by the
  intrinsic's completion commit (`CompleteIntrinsicAsync`). The executor already takes a `TimeProvider`.

**Decision.**

- `RuntimeInputBindingResolutionContext` gains an optional `EvaluatedAt` (`DateTimeOffset?`).
- `MaterializeSnapshotAsync` resolves with a context whose `EvaluatedAt` is `materializedAt`. If the caller's context
  already has an instant that differs from `materializedAt`, it throws, so the two can never disagree (FR-010).
- The intrinsic executor reads `timeProvider.GetUtcNow()` once per `MaterializeValueAsync` and sets it as
  `EvaluatedAt`. The intrinsic's completion commit fixes the result; a retry before that commit evaluates again,
  which ADR 0088 D2 allows.
- A `Clock` parameter resolved from a context without `EvaluatedAt` fails with a stable message naming the
  evaluation site (FR-012).

## R-3 Evaluation sites

| Site | Path | Instant | Fixed by |
|---|---|---|---|
| Activity inputs (every CLR activity, including `If`, `Switch`, `Inline` and the first entry of `While`/`Do`) | `RuntimeActivityInputMaterializer.EvaluateExpressionAsync` → shared evaluator | `startedAt` (`WorkflowStartActivitySchedulerWorkHandler.cs:288`) | The `ActivityStarted` commit of `InputSnapshot` (:350) |
| `While` / `Do` pass | `StructuralParentEvaluationSupport.ConstructActivityAsync` with the re-materializer | `GetUtcNow()` at `WorkflowParentActivityCompletionSchedulerWorkHandler.cs:192` | The pass-decision commit with `ParentCompletionProcessedWorkItemId`; redelivery guard at :128-135 |
| Notification and bookmark-resume evaluation | `ConstructActivityAsync` without a re-materializer | none needed | Reads the pinned snapshot |
| Engine intrinsics (`Set`, `Merge`, `Reduce`, `Return`, `Control`, `SetCorrelationId`, `SetInstanceName`, `SetOutput`, `Finish`) | `WorkflowIntrinsicExecutor.MaterializeValueAsync` (:586-635) | none today; added by R-2 | The intrinsic completion commit |
| `GraphActivity` input and output mappings | `GraphActivity.NewEvaluator` (:285-362) calls `IPortableExpressionEvaluator` directly with literal parameters only, stamped v1 | not applicable | Entry and completion checkpoints |
| `RunJavaScript`, `JavaScriptExecutionApi` | `IJavaScriptScriptEvaluator`, not portable definitions | not applicable | Out of scope |

Flowchart in Essentials has no connection conditions. No trigger or start evaluates a portable expression.

**Decision.** The first two sites, plus intrinsics, are the full set that resolves declared parameters; all three get
an instant (R-2). `GraphActivity` stays as it is, since an authored `Clock` cannot reach it. No publish-time
rejection is needed, so FR-012 is a runtime guarantee plus a defensive failure.

## R-4 Profile plumbing

**Facts.**

- `ExpressionEvaluationCapabilities.Resolve` (`:47`) grants the pure capability set only to v1. Every other profile
  gets all-false, so `IsBindingPure` is false.
- Exact-v1 checks: `PortableExpressionEvaluator.cs:31-37` (the dispatcher), `JintPortableJavaScriptEvaluator.cs:36-38`
  and its message at :73, `PortableLiquidExpressionHandler.cs:30-32`, `RuntimePortableExpressionEvaluator.cs:36`.
- Plain-source inputs compile in `RuntimeInputBindingCompiler.CompileExpressionInput` (:849-861) without a profile,
  and `RuntimeExpressionBinding` defaults a blank profile to v1 (`RuntimeInputBinding.cs:254-256`). A full portable
  definition passes its declared profile through (:890-897).
- `CapabilityProfile` is always serialized, so stored executables carry their stamp explicitly. It is part of the
  executable hash (`WorkflowExecutableHasher.cs:156`).
- `IsolatedJintEngine.Create` is shared with the script evaluator (`JintJavaScriptScriptEvaluator.cs:17`).

**Decision.**

- `ExpressionCapabilityProfiles.BindingPureV2 = "binding-pure-v2"`, with `IsBindingPureProfile(string)` true for v1 and v2.
- `Resolve` gives v2 the same pure grant as v1, so v2 is binding-pure with no nondeterminism.
- The dispatcher and the runtime evaluator accept both pure profiles. The Jint evaluator accepts both and passes the
  profile to `IsolatedJintEngine.Create`. The Liquid handler keeps requiring v1, so a Liquid expression stamped v2 is
  rejected (FR-022).
- `CompileExpressionInput` stamps v2 when the expression type is `JavaScript` (ordinal, ignoring case); every other
  language keeps the default. The deserialization default stays v1.
- `IsolatedJintEngine.Create` gains a `ClocklessDate` flag, off by default, so the script evaluator and v1 are
  unchanged.
- In the Jint evaluator's "stripped capability" diagnostic, `Date` counts as stripped only under v1. Under v2, the
  prelude's own `TypeError` messages surface as they are.

## R-5 Authoring validation and editor typings

**Facts.** `JavaScriptExpressionToolingProvider` (:67-79, :176-200) parses with `JavaScriptBindingSyntax` and never
evaluates. `BindingPureArgsDeclarationContributor` declares only `args`. Neither sees a profile.

**Decision.**

- An internal `ClocklessDateRules` in `Elsa.Expressions.JavaScript/Services` holds the six removed forms, their
  messages and the prelude source. The Jint project compiles it as a linked file, exactly as it already links
  `JavaScriptBindingSyntax.cs`. The runtime and the validator therefore share one source of text without a new
  project reference.
- The validator walks the parsed Acornima tree and reports each removed form it finds, at its location:
  - `new Date` with no arguments;
  - a call whose callee is the identifier `Date`;
  - a call of `Date.now`;
  - a call of a member named `toLocaleString`, `toLocaleDateString` or `toLocaleTimeString`.
  The last one is reported as a warning, because the receiver may not be a date. The others are errors.
- The typings contributor declares a `Date` with the allowed members only (FR-020).

## R-6 Authoring surface and UniqueId generation

**Facts.** Outside the converter, parameters are authored only as a full portable definition in the JSON input value.
There is no code-first builder or Studio control. The target framework is `net10.0`.

**Decision.**

- No new authoring API in this spec; the JSON wire form is the surface, and the Studio control is out of scope.
- `UniqueId` uses `Guid.CreateVersion7(evaluatedAt)`, formatted `"D"`, which .NET writes in lowercase. Taking the
  timestamp from the same instant keeps IDs ordered with `Clock`.
- The new records follow the `{Source}ExpressionParameterBinding` family: `ClockExpressionParameterBinding` and
  `UniqueIdExpressionParameterBinding`, with discriminators `"Clock"` and `"UniqueId"`.
- Both get explicit arms in `DetermineDependencyPolicy` (no policy) and `MaterializeParametersAsync`.
