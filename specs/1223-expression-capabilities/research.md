# Research: Expression capabilities (Elsa 4) — S-1

Phase 0 for [plan.md](plan.md). Format per decision: **Decision / Rationale / Alternatives**. Facts about Jint 4.9.1 and the converter were verified against the engine source and the converter source in this worktree.

---

## R-1 — Why the ambient-capability strip existed, and why S-1 reverses it

**Decision**: Reverse the strip. `IsolatedJintEngine` will re-admit `Date`, `Intl`, `Temporal` and native `Math.random` instead of overwriting them with `undefined`.

**Rationale**: The strip (`DisableAmbientCapabilities`) was introduced to enforce **replay-safe determinism** under the `binding-pure-v1` doctrine. That goal has two problems, both now moot:

1. **The goal is cancelled.** D-1 establishes that expressions need not be deterministic; D-2 makes them *safe* to be non-deterministic (a recorded step never re-runs, so a value is captured once and reused on retry/recovery — it is never re-evaluated "by accident"). The platform principle is guardrails, not the removal of standard language capabilities authors expect.
2. **The strip never actually bought determinism.** Even with `Date`/`random` removed, two evaluations of the same expression could differ: variable/`getVariable` reads change between evaluations, loop iterations produce new values. The real determinism attempt was value **recording/pinning** (the ADR 0088 line), which was built and **withdrawn**. The strip was enforcing a guarantee it could not, on its own, provide.

So the strip was a *defensible mechanism for the doctrine of its time*, not a mistake — but it is aimed at a target the initiative has abandoned, and S-1 removes it. (Recorded as context for future readers.)

**Alternatives considered**: Keep the strip and add bespoke .NET date/random helpers instead (rejected — reintroduces the exact capability behind a worse, non-standard surface, and still relies on `.NET` member shapes authors don't expect); keep the strip but relax case-by-case (rejected — leaves the determinism-era restriction as the default, against D-1).

---

## R-2 — Restoring native `Date` wired to the host clock

**Decision**: Re-admit native `Date`/`Temporal`/`Intl`. Drive native `Date` and `Date.now()` from the host `System.TimeProvider` via a custom Jint `ITimeSystem`; set engine `Options.TimeZone = UTC` and `Options.Culture = Invariant`.

**Rationale**: Jint 4.9.1 supports a custom `ITimeSystem` through `options.TimeSystem`; `DateConstructor` uses `_timeSystem.GetUtcNow()` for both `new Date()` and `Date.now()`. The default `DefaultTimeSystem.GetUtcNow()` returns `DateTimeOffset.UtcNow` (wall clock), **not** a `TimeProvider`; to make the engine clock-injectable (and test-controllable at the host level) we wrap the host `TimeProvider` in a small `ITimeSystem`. `TimeZone=UTC` + `Culture=Invariant` make time values and formatting deterministic in representation (ADR 0045 §5 / ADR 0046's UTC-invariant direction), independent of server locale. `Options.Interop.DateTimeKind` already defaults to `Utc`.

**Alternatives considered**: Leave `Date` reading wall-clock `DateTimeOffset.UtcNow` directly (rejected — not injectable, tests can't control it, and ignores the host clock abstraction); add a `.NET` `now()` as the *only* time source and keep `Date` stripped (rejected per R-1). The 10 Nov draft also added a `now()` helper as sugar; it is dropped (R-12, OQ-6). `Temporal.Now` reads the same `ITimeSystem`.

Date/time **semantics** (arithmetic, parsing edge cases) are owned by **ADR 0046** *Dates*; S-1 wires the source and the UTC/invariant frame, not new date math. Converted Elsa 3 scripts with a source time zone are the one exception to the UTC frame; see R-10.

**Date helpers (FR-025)**: ADR 0045 §5 D-1 and ADR 0046 leave "the built-in date helpers" for converting between an instant and an IANA zone to this spec. Jint 4.9.1 ships `Temporal` (`Instant.prototype.toZonedDateTimeISO`, `ZonedDateTime`) and `Intl.DateTimeFormat` with `timeZone`, so the native facilities are the helpers and no date host function is added. Implementation verifies that they resolve IANA IDs through the host's time zone data (ICU on Windows) with daylight saving; a gap there becomes a finding, not a shim.

---

## R-3 — `Math.random` RNG is not injectable (accepted limitation)

**Decision**: Restore native `Math.random`; accept that its RNG is uncontrolled.

**Rationale**: In Jint 4.9.1 `MathInstance` lazily news up `new Random()` with no injection hook, so there is no supported way to seed or replace the RNG. Under D-1 this is fine: randomness is genuinely allowed to be non-deterministic. The only practical consequence is that **tests must not depend on a fixed random sequence**. Authors who rely on `Math.random` are responsible for its behaviour, as in any JS runtime.

**Alternatives considered**: Replace `Math.random` with a `.NET`-backed seedable RNG host function (rejected — non-standard surface, and D-1 removes the motivation; would also silently diverge from native JS semantics). GUID randomness is separate and **is** cryptographic (`Guid.NewGuid()`).

---

## R-4 — The function surface (supersedes spec 247 R-6 for these functions)

**Decision**: Provide, as JSON-exchanging .NET built-ins on the shared surface: `newGuid`, `newGuidString`, `getGuidString`, `newShortGuid`, `getShortGuid`, `parseGuid` (GUID strings); `stringToBase64`, `stringFromBase64` (UTF-8 / standard Base64 strings); `toJson` (Elsa 3's text, R-11). Keep `isNullOrEmpty`/`isNullOrWhiteSpace` inlined as spec 247 specifies. **Refuse**: byte/stream helpers (binary, not JSON) and `getConfig`/`getConfiguration` (host configuration — deferred).

**Elsa 3 source** (verified in Elsa.JavaScript 3.5.3, `Handlers/ConfigureEngineWithCommonFunctions`):

| Function | Elsa 3 implementation |
|---|---|
| `newGuid` | `Func<Guid>`: `Guid.NewGuid()` — a .NET `Guid` handed to the script as a wrapped object |
| `newGuidString`, `getGuidString` | `Func<string>`: `Guid.NewGuid().ToString()` — both create a new GUID; neither takes an argument |
| `newShortGuid`, `getShortGuid` | `Func<string>`: `Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "")` — standard Base64 with three characters removed, not URL-safe Base64, so the length varies (at most 22) |
| `parseGuid` | `Func<string, Guid>`: `Guid.Parse(value)` — a wrapped .NET `Guid` |
| `stringToBase64` / `stringFromBase64` | UTF-8 and `Convert.ToBase64String` / `FromBase64String` |
| `toJson` | `JsonSerializer.Serialize(value, options)` with `JavaScriptEncoder.Create(UnicodeRanges.All)` and `JsonStringEnumConverter`, not indented |

**Rationale**: These are the Elsa 3 built-ins with a faithful JSON-only Elsa 4 form. ADR 0045 §5 D-1 says Elsa 3 date and GUID functions convert directly and their names alone never cause a refusal, so `parseGuid` and `newGuid` convert: both returned a .NET `Guid`, whose text is the canonical `D` form, so returning that text is faithful wherever the script uses the GUID as text (concatenation, `toString()`, comparison with a string, a returned or stored value). Using a .NET member of the `Guid` (`newGuid().toString('N')`, `toByteArray()`) is not faithful and is refused by R-7. Spec 247 R-6 refused the GUID family, the Base64 pair and `toJson` under the determinism rule and without a host-function mechanism; this spec supersedes those rows. The Base64 pair needed a hand-written UTF-8 encoder in a sandbox without `btoa`/`TextEncoder`; as a .NET built-in it is the same code Elsa 3 ran.

**Corpus cost of the refusals** (JTest, 36 definitions, counted as calls `name(` in the definition JSON): `getConfig` 0; `getConfiguration` 1 use in 1 definition (`javascript-script-handler-functions`; Elsa 3 has only `getConfig`, so it already failed there). `parseGuid` (1 use in `javascript-elsa-functions`) now converts.

**Alternatives considered**: Provide `getConfig` now (rejected — host `IConfiguration` surface and secret exposure are beyond S-1; deferred). Keep `parseGuid` refused (rejected — contradicts ADR 0045 §5 D-1; its member use is caught by R-7 instead). Return GUIDs as objects that emulate `Guid` members (rejected — a shim; values are JSON).

---

## R-5 — Function effect declaration (D-6)

**Decision**: A .NET-implemented function registered without a declaration is **effect-free**. Registration takes one optional flag that declares a function **effectful**; only then does the engine commit before and after an input evaluation that calls it. The flag never refuses, skips or deduplicates a call. The built-ins of R-4 are registered without it. No effect profiles. (D-6, decided and written into ADR 0045 §5.)

**Rationale**: Most functions called from expressions have no side effects, so the default is optimized for them. The boundary narrows the window for a repeated effect and does not close it, so the designer needs idempotency either way, and external business operations belong in activities. Accepted cost: an effectful function registered without the flag gets no boundary. S-1 adds and records the flag; placing the commits belongs to the D-2 execution-contract implementation. Until that lands, the flag is recorded and has no run-time effect.

**Background eligibility**: input bindings run on the coordinator and `RunJavaScript` is ineligible for background runs (ADR 0093 C9, spec 1230), so the flag does not interact with eligibility. A host function that reads variables (`ReadsVariables`) is a computed read and falls under C9 like any other.

**Alternatives considered**: No metadata at all, every registered function possibly effectful (not chosen in D-6 — two commits for every step whose inputs call any function); a richer effect classification or function profiles (rejected — no consumer).

---

## R-6 — `binding-readonly-v1` delivered by re-admitting native intrinsics

**Decision**: Deliver `binding-readonly-v1` by re-admitting the native JS intrinsics plus the registered JSON functions, and rename the profile constant (R-13).

**Rationale**: ADR 0083 defines the profile, records that native intrinsics are admitted and that the Jint 4.9.1 RNG is not injectable (R-3), and leaves three things to this spec: the constant rename with the `Inline` justification, the registration that marks a function as allowed, and the editor guidance for an `undefined` result. The first two are here (R-12, R-13); the editor guidance belongs to the null and undefined spec (S-2).

**Alternatives considered**: Deliver the profile purely as host functions with intrinsics kept stripped (rejected per R-1/R-2).

---

## R-7 — .NET-usage analysis

**Decision**: One static analysis in the Essentials JavaScript analysis services (beside spec 1222's `JavaScriptVariableReadAnalyzer`, which already uses Acornima), called by the Elsa 3 converter with the Elsa 3 types and by publish validation for every script:

- *Conversion only*: refuse a member read or invocation on a **known .NET-object value** — the result of `newGuid()`/`parseGuid()`, or a read of a variable, input or activity output whose Elsa 3 type Elsa 3's engine handed over as a wrapped object — used directly or through a `const`/`let` alias in the same script. `toString()` without arguments on a GUID is allowed (same text).
- Refuse a reference to a **.NET type** identifier (static access or `new`) from a curated catalog that excludes JS globals. At conversion, the globals the converter already refuses (`ElsaThreeGlobals`: `DateTime`, `Guid`, `Random`, …) keep `VF-IMP-005`.
- Refuse an invocation of a **PascalCase member** on a value the script did not define, unless the receiver is a JS global (`Intl.DateTimeFormat(...)` is valid JS).
- **Allow** property reads of other values in any casing (JSON keys), computed member access (gap), values reached indirectly (gap), and invocations of members the script defines itself.

Conversion emits **`VF-IMP-012`** (spec 1222 took `010` and `011`); publish emits the script validation error `Scripts/DotNetMemberUsage`. The message quotes the offending expression (from the Acornima node's `.Start`/`.End`).

**Rationale**: A corpus scan of all 224 JavaScript expressions in the 36 JTest definitions found that a PascalCase-invocation rule refuses none of them, while the corpus does use .NET members — in camelCase, which Elsa 3's engine resolved (`formFile.openReadStream()`, `formFile.name` on an `IFormFile[]`). The signal is therefore *which value* was a .NET object, not the member's casing. The converter knows that for the values it can trace: the Elsa 3 built-ins' return types (R-4) and the declared Elsa 3 types of variables, inputs and outputs (the Elsa 3 type map). Following direct use and plain aliases covers the corpus's pattern without a data-flow engine. The PascalCase and type rules stay as a cheap net that also serves publish, where no Elsa 3 types exist and no Elsa 4 value is a .NET object by default. Property reads of other values stay allowed because PascalCase JSON keys are common.

**Corpus check**: the one corpus case (`http-endpoints`) is already refused by other routes (the `FormFile[]` variable and the lost files output); T018 recounts the corpus after implementation.

**Default strategy only**: ADR 0046 V10 makes .NET interop an opt-in engine strategy provided by a feature (#4103), and spec 1228 ships the default only. The analysis is part of the default strategy; S-1 adds no switch. The opt-in decides whether the analysis applies under it, and it brings its own publish warning (ADR 0083, V12).

**Why not in the converter's private walker**: `JavaScriptAnalyzer` is a private class of the Elsa 3 extension; publish validation in Essentials cannot depend on it. Spec 1222 already put publish-side JavaScript analysis in Essentials.

**Alternatives considered**: PascalCase invocation only (the 10 Nov design — rejected by the corpus scan); full taint/data-flow analysis (rejected — complexity the signal does not need); refuse every member use on any value read from a variable (rejected — refuses faithful JSON access); refuse all dynamic dispatch (rejected — over-refuses).

---

## R-8 — Converter integration points (existing infrastructure)

**Decision**: Extend the existing pieces rather than add new components.

**Findings (verified in source at `b6a67a880`)**:
- The converter parses with **Acornima** in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs`; `JavaScriptAnalyzer.VisitNode` walks the AST with JS scoping and has an Expression/Script mode. Positions are `int` offsets (`.Start`/`.End`).
- Name authority is `Internal/Elsa3JavaScriptNames.cs` (`BuiltIns`); per-function tables live in the rewriter (`InlineHelpers`, `RefusedHostFunctions`, `ElsaThreeGlobals`, …). `IsNondeterministicCall` refuses `Math.random`, `Date.now` and `crypto.randomUUID`; `ElsaThreeGlobals` includes `Date`, `Guid`, `DateTime` and `Random`. The guidance texts `TimeGuidance` and `UniqueValueGuidance` still say there is no clock or randomness and need rewording.
- Diagnostic codes live in `Elsa3ImportDiagnosticCodes` at the end of the rewriter (`VF-IMP-001..011`; `005 ForbiddenCapability`, `009 LiveInstanceState`, `010 ResolutionDiffers`, `011 ShadowedCandidate`). No diagnostic carries a JS source line/column.
- The profile constant is `ExpressionCapabilityProfiles.BindingPureV1` in Expressions.Core (`ExpressionDefinition.cs`), checked by the evaluators (portable, Jint, Liquid, runtime), emitted by the converter, serialized as `capabilityProfile` and hashed by `WorkflowExecutableHasher`.
- `JintPortableJavaScriptEvaluator` keeps its own `StrippedAmbientCapabilities` list and rethrows "unavailable in the deterministic binding sandbox"; both go with the strip. It rejects every evaluator option today.
- Spec 1222 added `JavaScriptHostFunctionDeclaration(Name, ReadsVariables)` in Expressions.JavaScript.Core; its doc notes the isolated engine cannot install host functions yet. `variables` and `getVariable` are installed by `IsolatedJintEngine.SetReadOnlyVariables` as non-writable globals.
- Publish-side script validation (`ScriptOutcomeValidator`) emits `Scripts/*` validation errors, including `Scripts/ReservedName` for `complete`.

**Rationale / alternative**: Building parallel components was rejected — the existing pieces already hold scope handling, the declaration record and the diagnostic pipelines.

---

## R-11 — `toJson` reproduces Elsa 3's text

**Decision**: `toJson(value)` serializes the JSON value with System.Text.Json using Elsa 3's options (`JavaScriptEncoder.Create(UnicodeRanges.All)`, not indented, no naming policy). It does not use the engine's `JSON.stringify` and does not depend on the JSON-integrity ADR (A-5).

**Rationale**: Faithful-or-refuse (D-13). Elsa 3's encoder writes HTML-sensitive characters as `\u003C` and similar escapes; `JSON.stringify` writes them as-is, so a converted script that sends or compares the text would change. Non-JSON arguments follow Elsa 3's mapping: Jint handed a `Date` to the .NET function as a UTC `DateTime` (serialized with trimmed fractional seconds, unlike `toISOString()`), `undefined` as `null`, and `NaN` made the serializer throw; Elsa 4's built-in does the same. For a JSON value the remaining Elsa 3 differences (PascalCase .NET properties, enums as strings) cannot occur, because no .NET object reaches the function. `toJson(newGuid())` gives the quoted GUID text in both versions. A known .NET-object value of another type passed to `toJson` would have serialized with its .NET property names in Elsa 3, so the converter refuses it (FR-016). The text is a string the script asked for, not stored data, so it is outside a "one baseline for stored data" rule; if the JSON-integrity ADR is accepted with every serializer deriving from one baseline, `toJson` is named there as an exception (OQ-5).

**Alternatives considered**: `toJson` = the engine's captured `JSON.stringify` (simpler, same path as result materialization; rejected — different text from Elsa 3 for HTML-sensitive characters, which would need a conversion warning on every `toJson`); keep `toJson` refused (rejected — 3 corpus uses, and the faithful form is a few lines).

---

## R-12 — Registration, names and precision

**Decision**: Extend spec 1222's `JavaScriptHostFunctionDeclaration` with `Effectful` and an implementation delegate, and install registered functions in `IsolatedJintEngine` the way `getVariable` is installed. A registration marks the function as allowed in `binding-readonly-v1` (ADR 0083). Names follow ADR 0045 §5 D-5: the read roots (`variables`, `input`, `activities`, `workflow`, `args`), built-in names and namespace roots are reserved; host functions carry a namespace prefix and are installed as members of a read-only namespace object; a duplicate or reserved registration fails at startup. A script that declares a read root gets the existing error `Scripts/ReservedName` (it breaks pinning); one that declares a built-in or namespace root gets the warning `Scripts/ShadowedFunction`. The .NET-usage rules never apply to script-declared names or registered functions. A number argument carrying spec 1228's loss information logs a warning naming the function and the argument path, and the call runs (ADR 0046 V9; spec 1228 FR-015 hands this to S-1 and supplies the loss information).

**`now()` dropped**: the 10 Nov draft added `now()` as sugar. Elsa 3 had no `now`, so it serves no conversion; native `Date` and `Temporal.Now` cover it; and as a reserved name it would warn on the common `const now = new Date()`.

**Severity**: ADR 0045 asks for "a publish diagnostic" without a severity. Declaring a read root breaks pinning, so it keeps the existing error; shadowing a built-in breaks nothing (the script's own declaration wins), so it is a warning with its own code rather than a second severity on one code.

**Alternatives considered**: A second registration API for S-1 (rejected — spec 1222's record is already the registration surface); fail publish on a reserved-name declaration (rejected — breaks scripts that work).

---

## R-13 — Profile constant rename, no alias

**Decision**: Rename `BindingPureV1` to `BindingReadonlyV1` (value `binding-readonly-v1`) everywhere: definitions, converter, evaluators (portable, Jint, Liquid, runtime), `ExpressionEvaluationCapabilities` (time and randomness allowed), and the executable hash. Regenerate stored fixtures and golden hashes in place. Remove the `Inline` comment that grounds its replay safety in `binding-pure-v1`; its basis is the D-2 contract.

**Rationale**: ADR 0083 says there is no compatibility path for `binding-pure-v1`, and compatibility is owed only from Elsa 4's first stable release. One constant serves every evaluator; keeping a second name for Liquid would leave the cancelled determinism doctrine alive under its old label. Liquid's filters are unchanged by S-1; unblocking its date filters under D-1 is a follow-up (OQ-7).

**Alternatives considered**: Keep the string and change only what it grants (rejected — the name would say the opposite of the rule); keep a reader for the old value (rejected — a pre-release shim).

---

## R-10 — Time zones in converted scripts

**Decision**: Classify `Date` usage in a converted script as zone-independent or zone-dependent (FR-021, FR-022). Zone-independent usage converts faithfully. Zone-dependent usage converts only when the conversion has a source time zone (ADR 0046 *Dates*: an IANA ID set on the converter feature and recorded on the converted workflow and in the published conversion contract; ADR 0046's assume-UTC policy is the source time zone `UTC`); a converted expression with a source time zone evaluates with that zone as the Jint engine's local time zone (`Options.TimeZone`), the clock instant unchanged. Without a source time zone, zone-dependent usage is refused with a quoted diagnostic. A non-literal string parse is refused without a source time zone (FR-024). Date-typed Elsa 3 values, which Elsa 3 handed to scripts as a `Date`, are rewritten to a `Date` (FR-032). Locale-dependent calls without an explicit locale are refused (FR-033), because Elsa 3 used the server culture (Jint's default `CultureInfo.CurrentCulture`, never set by Elsa 3) and Elsa 4 the invariant culture.

**Rationale**: Elsa 3 evaluated scripts in the server's time zone. `Date.now()`, `getTime()` and `toISOString()` give the same result on every server; `getHours()`, `toLocaleString()`, the local multi-argument constructor and an offset-less parse (`new Date('2026-10-28T09:15:00')`) do not. Running every converted script in UTC would silently change those on any workflow from a non-UTC server, which faithful-or-refuse forbids and which ADR 0046 removes for literals. The source time zone is the same option ADR 0046 defines for literals, so one setting covers a converted workflow. Jint takes the time zone per engine, so a converted expression can carry it as an evaluator option. `JintPortableJavaScriptEvaluator` rejects every evaluator option today and options enter the executable hash, so S-1 adds this one option to the evaluator's schema; the hash change is fine before the first stable release.

**Non-literal parse**: the converter cannot see whether `new Date(input)` receives text with an offset. ADR 0046's behaviour warning covers non-literal expressions bound to date *members*; for scripts it says converting needs the same source time zone, and offset-less text gives a different instant, so R-10 refuses without a source time zone (OQ-3; the 10 Nov draft warned). Corpus: 3 definitions parse non-literal strings (`javascript-date-methods`, `performance-minimal-baseline`, `composition-execute-workflow-root`); none uses a local-time getter, local formatting, the multi-argument constructor or an offset-less literal.

**Alternatives considered**: Run every script in UTC and mark all `Date` usage faithful (rejected — silent behaviour change for non-UTC sources); refuse all `Date` usage without a source time zone (rejected — refuses the zone-independent majority for no reason); convert non-literal parses with a behaviour warning (the 10 Nov choice; rejected — the ADR's warning is for date members, not scripts); add a source-culture option (not proposed — ADR 0046 defines none, so it needs an amendment; OQ-9); refuse every use of a date-typed value instead of rewriting it (rejected — refuses common faithful scripts; OQ-8).

---

## R-9 — Execution-contract basis (context, not re-specified)

**Decision**: Rely on D-2 and ADR 0092 decisions 8 and 9 as the safety basis; do not re-specify them.

**Rationale**: Non-deterministic expressions are safe because a value recorded in an invocation's input snapshot is reused on retry and recovery. A re-execution or reschedule is a new invocation with fresh inputs; a background refresh of a stale snapshot (ADR 0092 decision 9) and each pass of a loop that re-materializes its inputs evaluate again; validation probes never execute effectful bindings. Idempotency across external effects is the designer's concern, with engine-supplied stable invocation ids. The `Inline` activity's old "pure replay" justification leaned on `binding-pure-v1`; S-1 removes it (R-13), and its basis is this contract.
