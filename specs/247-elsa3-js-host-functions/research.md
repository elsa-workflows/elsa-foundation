# Research: spec 247

Sources:
- **Elsa 3** source at tag `3.5.0` (`d72c4974d`), Jint 4.3.0, Humanizer 2.14.1. Paths are relative to
  `src/modules/` in elsa-core. The source was read, not run.
- **Elsa 4** code in this repository.
- **Corpus** measurements come from the throwaway probes described in the spec's "Measured baseline".

## R-1 Top-level `return`

**Elsa 3.** `Elsa.JavaScript/Services/JintJavaScriptEvaluator.cs` L129-139 prepares every script with
`ParsingOptions = new() { AllowReturnOutsideFunction = true }` and runs it as a Script, not wrapped in a function.
- `Evaluate` returns the completion value: a top-level `return x` gives `x`, and a bare `return;` gives undefined, which
  becomes `null`. Without a `return`, the result is the completion value of the last value-producing statement.
- RunJavaScript (`Activities/RunJavaScript/RunJavaScript.cs` L49-84) uses the same evaluator and stores `Result` only
  when the value is not null.
- A new engine is created per evaluation, so top-level `var`/`let` in the global scope are not observable across
  evaluations.

**Decision.** Parse with `AllowReturnOutsideFunction`. If the body has a top-level `return` (one not inside a nested
function):
- In Expression mode, wrap the body in `(function () {\n…\n})()`. If the last top-level statement is an expression,
  prefix it with `return` (Elsa 3's completion value). If it is a `return`, keep it. Otherwise refuse with `VF-IMP-006`.
- In Script mode, Elsa 4 already runs the body as a function. The same last-statement rule applies.
- A bare top-level `return;` becomes `return null;`. Elsa 3 turned undefined into `null`, while the Elsa 4 evaluator
  refuses an undefined result (found by the US1 equivalence tests).
- In Script mode, a body that ends with another statement converts with `CompletionValueLost`, as spec 236 does for
  scripts. It is refused only when something reads the script's result.

**Why faithful.** Every path ends in a `return` whose value Elsa 3 would return:
- If the last statement is a `return`, any earlier path falls through to it.
- If the last statement is an expression, Elsa 3 returns its completion value, which is exactly what the added `return`
  returns.

A trailing `if`, `for` or `switch` can have a completion value that `return` does not reproduce, so it stays refused, as
spec 236 already refuses it.

**Alternatives rejected.**
- Rewrite every `return` into a completion expression. This is impossible in general.
- Track completion values through control flow. Complex, and the corpus has no such case.

## R-2 Which accessors exist (standalone vs reusable)

**Elsa 3** (`Handlers/ConfigureEngineWithVariablesAndInputOutputAccessors.cs`; the order is "important", per its
L29-32 comment):

1. **Variables in scope** (L35-49): `get{Pascalize(name)}` and `set{Pascalize(name)}`.
   - Only names where `IsValidVariableName` is true, meaning non-empty and every character is a letter or a digit.
   - The getter returns the raw CLR value.
2. **Workflow inputs** (L51-70): `get{name}`, using the name **verbatim** (not Pascal-cased).
   - Getter only.
   - Not registered when the workflow runs as an activity inside another workflow (`IsContainedWithinCompositeActivity`,
     L55-56).
3. **Activity outputs** (L72-79): `get{Pascalize(output)}From{Pascalize(activityName)}`, registered last.

A later `SetValue` overwrites an earlier one, and JavaScript names are case-sensitive.

**Composite (reusable) runs.** `WorkflowDefinitionActivity.DeclareInputAsVariables` (`WorkflowDefinitionActivity.cs`
L141-156) declares each input as a variable whose name equals the input name. Child scripts therefore see inputs only
through `get{Pascalize(inputName)}` variable accessors (and `getVariable`/`getInput`).

**Decision.** Resolve `get{N}()` according to the conversion mode:

| Order | Standalone (`IsReusable = false`) | Reusable (`IsReusable = true`) |
|---|---|---|
| 1 | Output accessor match → refuse (it shadows everything) | Same |
| 2 | Input with name exactly `N` → workflow-request binding | Input or variable with `Pascalize(name) == N`; if both match → `VF-IMP-002` (two declarations of one name) |
| 3 | Variable with `Pascalize(name) == N` → variable binding | — |
| 4 | No match → "unknown host function" (`VF-IMP-005`) | Same |

In reusable mode, an input match resolves to the same binding `getInput("{name}")` resolves to today.

**Corpus.** Every input name in the corpus is already PascalCase (for example `CustomInput`, `InputData`,
`ShouldFault`), so both modes give the same result there.

## R-3 Names, Pascal-casing, collisions with built-ins

- Humanizer 2.14.1 `Pascalize` is `Regex.Replace(input, "(?:^|_| +)(.)", m => m.Groups[1].Value.ToUpper())`. For names
  that are only letters and digits, it upper-cases the first character.
- `ToUpper()` uses the current culture, so the Turkish `i` → `İ` is an edge case.
  - **Decision:** use the invariant culture. If the culture-sensitive result would differ (only for `i`/`ı` with a
    Turkish culture), the two cases are documented as a residual risk rather than emulated.
- The duplication (a few lines) beats a Humanizer dependency (§2.17). This goes in internal `Elsa3JavaScriptNames`.
- **Built-in order.** Built-ins (`ConfigureEngineWithCommonFunctions`) and accessors are both registered by
  `EvaluatingJavaScript` handlers. Their relative order depends on assembly metadata order and cannot be read from the
  source. It is likely, but not proven, that the accessor wins.
  - **Decision:** a generated accessor whose name equals a built-in (for example a variable `CorrelationId`) is refused
    with `VF-IMP-002` and guidance to rename it. No guessing.
- Generated accessors and built-ins override RunJavaScript's `setOutcome`/`setOutcomes`. This is irrelevant here,
  because both are refused.

## R-4 Instance metadata binding (Elsa 4)

**Today.** Elsa 4 has four binding kinds (`Literal`, `WorkflowRequest`, `Variable`, `ActivityResult`), defined in
`Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs` L106-111. No expression can read the correlation ID or
the instance ID.

**Rules that apply.**
- ADR 0045 §5: "time, randomness and other nondeterministic data enter an expression as explicit pinned parameters".
- The spec 095 contract (`expression-and-import-contract.md` L104-116) forbids identity that is "not declared as a
  parameter". A declared binding is therefore the sanctioned route.
- ADR 0083: no new capability profile; the expression stays binding-pure.

**Where the change goes.**

| Place | Change |
|---|---|
| `ExpressionDefinition.cs` | A new record and `JsonDerivedType`. The fingerprint and equality are generic |
| `RuntimePortableExpressionEvaluator.MaterializeParametersAsync` (L223-230) | Add a case. Its default arm throws, so the case is required |
| `DetermineDependencyPolicy` (L173-183) | Add an explicit case: no policy, not sensitive |
| Not changed | `WorkflowExecutableHasher` (generic), `RuntimeInputBindingCompiler` (pass-through), `ValueFlowValidator` (ignores non-ActivityResult kinds) |
| Tests | `ExpressionParameterBindingWireTests` pins the kinds and a fingerprint; the theory gains a row. The four-kind fingerprint fixture stays as it is, and a separate fixture covers the new kind |

**Decision.** `WorkflowInstanceExpressionParameterBinding(member)`, with members `correlationId` and `instanceId`.
- One kind with a member rather than two kinds, because the source (the instance) is the same and the set is closed.
- `instanceName` and definition identity are left out until something needs them.

**Alternatives rejected.**
- **Literal binding at conversion.** The correlation ID is a per-instance value, so a literal is wrong.
- **A Variable synthesized by a SetVariable at start.** It hides metadata in user state, and a later
  `SetCorrelationId` would not update it.
- **Ambient `getCorrelationId()` in the sandbox.** This violates binding-pure-v1 and ADR 0045 §5.

## R-5 Where the correlation ID comes from at runtime

- **Authoritative value:** `WorkflowExecutionState.CorrelationId`. It is set at start from the dispatch
  (`WorkflowStartSchedulerWorkHandler.cs:335`), and `SetCorrelationId` updates it
  (`WorkflowIntrinsicExecutor.cs:240-267`).
- **Expression channel (already present, so far unused):**
  - The durable value `identity:correlationId` is written by `SetCorrelationId` and read by
    `RuntimeIdentityStateProjection` (`RuntimeInputBindingStateProjection.cs:251-281`).
  - The projection set already exposes `CorrelationId`. Nothing reads it yet; ADR 0030's `getCorrelationId()` idea is
    what it was built for.
- **Gap:** `RuntimeWorkflowStateSeed.BuildSeedChanges` (L63-98) does not seed the start-time correlation ID, so the
  projection is null until a `SetCorrelationId` runs.

**Decision.**
- Seed `identity:correlationId` at start when the dispatch carries one.
- Resolve the binding from the identity projection over committed durable values, the same values the context's
  variable envelopes come from.
- The instance ID is `RuntimeInputBindingResolutionContext.WorkflowExecutionId`, because in Elsa 4 the instance is the
  execution.

**Why committed state.** Input snapshots can be re-materialized: on replay of a coalesced segment (replay-safety audit)
and per `While` pass (#977). Reading committed values keeps re-materialization stable, whereas reading live
`WorkflowExecutionState` would not be.

**Semantics vs Elsa 3.** Elsa 3 reads and writes the live context (`WorkflowExecutionContext.CorrelationId`, which is
`null` when unset), so a change is visible to later activities. Elsa 4 does the same at activity granularity.
- Within a single Elsa 3 script, `setCorrelationId` followed by `getCorrelationId` would see the new value, but
  `setCorrelationId` is refused (mutation), so this case cannot arise.
- **Residual difference:** Elsa 3 parallel branches could see a sibling's change mid-activity. Elsa 4 shows the latest
  committed value when an input is materialized. This is acceptable: Elsa 3's order there was not deterministic either.

**Compatibility.** An older runtime cannot deserialize the new discriminator (§E2.6.1). Elsa 4 is pre-GA (ADR 0080), so
this is recorded in the ADR 0045 amendment rather than mitigated.

## R-6 Helpers

| Helper | Elsa 3 (`Handlers/ConfigureEngineWithCommonFunctions.cs`) | Decision |
|---|---|---|
| `isNullOrEmpty(string)` | `string.IsNullOrEmpty` (L50) | Inline: `v == null \|\| v === ""` |
| `isNullOrWhiteSpace(string)` | `string.IsNullOrWhiteSpace` (L49), using .NET `char.IsWhiteSpace`: U+0009–000D, U+0020, U+0085, U+00A0, U+1680, U+2000–200A, U+2028, U+2029, U+202F, U+205F, U+3000 | Inline a regex over exactly that set. JavaScript `\s` differs (it adds U+FEFF and omits U+0085) |
| `toJson(object)` | System.Text.Json with `JavaScriptEncoder.Create(UnicodeRanges.All)` and `JsonStringEnumConverter`; no naming policy, not indented (L73-86). HTML-sensitive characters escape as `<` etc., and CLR properties stay PascalCase | Refuse. `JSON.stringify` differs on those characters and on CLR-shaped values. Guidance: `JSON.stringify`, and `JSON.parse(JSON.stringify(x))` for the corpus's copy idiom |
| `parseGuid(string)` | `Guid.Parse`, which returns a CLR `Guid` with .NET members and accepts the N, D, B, P and X formats | Refuse. Only a complex emulation would be faithful |
| Bytes and streams | Return `byte[]` (seen in JavaScript as an ArrayBuffer) or take a `Stream` | Refuse. Elsa 4 values are JSON |
| `stringToBase64` / `stringFromBase64` | UTF-8 Base64 | Refuse (no corpus use). Faithful in principle, but Elsa 4's sandbox has no `btoa`/`TextEncoder`, so a hand-written UTF-8 encoder would be a shim |

**Non-string arguments.** Jint converts a JavaScript argument for a `string` parameter. The exact behaviour for numbers
and objects is not verified. The inline forms treat non-strings by JavaScript equality (`0 === ""` is false), which
matches .NET for any value that is not a string. This is recorded as a residual risk; every corpus use passes a string
or `null`.

Prelude functions (`__elsa3IsNullOrEmpty`, `__elsa3IsNullOrWhiteSpace`) are emitted once per expression, so each
argument is evaluated exactly once.

## R-7 Refusal messages

- Today, every unlisted name falls through to "unknown or unapproved host function". The rewriter instead gets a
  static refusal table: name → (code, message, guidance). The rows are in
  [contracts/javascript-conversion.md](contracts/javascript-conversion.md).
- The codes stay the same (FR-023); only the messages and guidance become specific.
- Elsa 3 has no `getConfiguration`, only `getConfig` (opt-in, `JintJavaScriptEvaluator.cs` L103-107). The corpus's
  `getConfiguration` stays "unknown".

## R-8 Pending-graph cleanup (FR-024, FR-025)

| Item | Public? | Reachable? | Used elsewhere? |
|---|---|---|---|
| `Elsa3ActivityDiagnosticCodes.GraphConversionPending` | Yes | Only through a custom `IElsa3ActivityMap` entry with `GraphLevel` | No |
| `Elsa3ActivityTarget.GraphLevel` (record) | Yes (nested public record) | Same | One resolver test |
| Resolver `GraphLevel` case (`Elsa3ActivityResolver.cs:115-119`) | Internal | Same | One resolver test row |
| `IsDeferred` (`Elsa3DefinitionConverter.cs:143-154`) | Private | Always false for built-ins | No |
| `Elsa3NodeConversionContext.Deferred` and its protected constructor parameter | Yes; listed in `EXTENSION_POINTS.md:57` | Always empty for built-ins | Tests only; spec 235 (PR #2708) does not use it |

**Defect found.** Spec 239 removed the normalizer's "descend into a pending node" special case, so a `GraphLevel`
node's children are no longer prepared. The documented promise ("its children are still converted node by node", in
`IElsa3NodeConverter.cs:19-23`, `EXTENSION_POINTS.md:52` and the resolver guidance) is now false. Calling
`ConvertNodeAsync` on such a child would throw `ArgumentException` (inferred from the code, not run).

**Decision (D-6, approved 2026-10-19).** Remove the path:
- `GraphLevel`, the resolver case, `IsDeferred`, and `Deferred` together with its constructor parameter.
- Keep the constant, marked retired.

Rationale:
- The path is reachable only through a public extension that is already broken.
- ADR 0080 asks for pre-GA cleanup.
- No consumer exists (spec 235 does not use it).

`Elsa3GraphConstructKind` and `Elsa3MemberRuleKind.GraphLevel` are live (spec 239) and are not part of this cleanup.

## R-9 Expected corpus outcome

These rules were applied to the measured issues: exact accessor names, correlation and instance ID lowered, the two
helpers inlined, and statement-ending bodies ending in `return` accepted.

| Outcome | Result |
|---|---|
| Parse refusals | 0 of 217 (baseline 200) |
| Definitions whose JavaScript fully converts | **7 of 33** (baseline 0). Spec SC-002 is corrected from 8 to 7 because `javascript-json-methods` uses `toJson`, which stays refused (R-6) |

Remaining refusals, all deliberate:

| Construct | Definitions |
|---|---|
| `newGuid` | 17 |
| `Date` | 9 |
| `toJson` | 3 |
| `newGuidString` | 2 |
| byte/stream helpers | 4 uses |
| `parseGuid` | 1 |
| unknown names (`getItem`, `getConfiguration`, `base64Encode`) | 1 each |

The 7 definitions convert end to end only once spec 235 or follow-up issues fix their activity-level refusals.
