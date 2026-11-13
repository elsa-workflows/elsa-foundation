# Research: Expression capabilities (Elsa 4) — S-1

Phase 0 for [plan.md](plan.md). Format per decision: **Decision / Rationale / Alternatives**. Facts about Jint 4.9.1 and the converter were verified against the engine source and the converter source in this worktree.

---

## R-1 — Why the ambient-capability strip existed, and why S-1 reverses it

**Decision**: Reverse the strip. `IsolatedJintEngine` will re-admit `Date`, `Intl`, `Temporal` and native `Math.random` instead of overwriting them with `undefined`.

**Rationale**: The strip (`DisableAmbientCapabilities`) was introduced to enforce **replay-safe determinism** under the `binding-pure-v1` doctrine. That goal has two problems, both now moot:

1. **The goal is cancelled.** D-1 establishes that expressions need not be deterministic; D-2 makes them *safe* to be non-deterministic (a recorded step never re-runs, so a value is captured once and reused on retry/recovery — it is never re-evaluated "by accident"). The platform principle is guardrails, not the removal of standard language capabilities authors expect.
2. **The strip never actually bought determinism.** Even with `Date`/`random` removed, two evaluations of the same expression could differ: variable/`getVariable` reads change between evaluations, loop iterations produce new values. The real determinism attempt was value **recording/pinning** (the ADR 0088 line), which was built and **withdrawn**. The strip was enforcing a guarantee it could not, on its own, provide.

So the strip was a *defensible mechanism for the doctrine of its time*, not a mistake — but it is aimed at a target the initiative has abandoned, and S-1 removes it. (Recorded here at Joey's request as context for future readers of the ADR/spec.)

**Alternatives considered**: Keep the strip and add bespoke .NET date/random helpers instead (rejected — reintroduces the exact capability behind a worse, non-standard surface, and still relies on `.NET` member shapes authors don't expect); keep the strip but relax case-by-case (rejected — leaves the determinism-era restriction as the default, against D-1).

---

## R-2 — Restoring native `Date` wired to the host clock

**Decision**: Re-admit native `Date`/`Temporal`/`Intl`. Drive native `Date` and `Date.now()` from the host `System.TimeProvider` via a custom Jint `ITimeSystem`; set engine `Options.TimeZone = UTC` and `Options.Culture = Invariant`.

**Rationale**: Jint 4.9.1 supports a custom `ITimeSystem` through `options.TimeSystem`; `DateConstructor` uses `_timeSystem.GetUtcNow()` for both `new Date()` and `Date.now()`. The default `DefaultTimeSystem.GetUtcNow()` returns `DateTimeOffset.UtcNow` (wall clock), **not** a `TimeProvider`; to make the engine clock-injectable (and test-controllable at the host level) we wrap the host `TimeProvider` in a small `ITimeSystem`. `TimeZone=UTC` + `Culture=Invariant` make time values and formatting deterministic in representation (ADR 0045 §5 / ADR 0046's UTC-invariant direction), independent of server locale. `Options.Interop.DateTimeKind` already defaults to `Utc`.

**Alternatives considered**: Leave `Date` reading wall-clock `DateTimeOffset.UtcNow` directly (rejected — not injectable, tests can't control it, and ignores the host clock abstraction); add a `.NET` `now()` as the *only* time source and keep `Date` stripped (rejected per R-1). We still provide a small `now()` host function returning an ISO-8601 UTC string as a convenient import target and sugar, alongside native `Date`.

Date/time **semantics** (arithmetic, parsing edge cases) are deferred to **ADR 0046** (#3422); S-1 wires the source and the UTC/invariant frame, not new date math. Converted Elsa 3 scripts with a source time zone are the one exception to the UTC frame; see R-10.

---

## R-3 — `Math.random` RNG is not injectable (accepted limitation)

**Decision**: Restore native `Math.random`; accept that its RNG is uncontrolled.

**Rationale**: In Jint 4.9.1 `MathInstance` lazily news up `new Random()` with no injection hook, so there is no supported way to seed or replace the RNG. Under D-1 this is fine: randomness is genuinely allowed to be non-deterministic. The only practical consequence is that **tests must not depend on a fixed random sequence**. Authors who rely on `Math.random` are responsible for its behaviour, as in any JS runtime.

**Alternatives considered**: Replace `Math.random` with a `.NET`-backed seedable RNG host function (rejected — non-standard surface, and D-1 removes the motivation; would also silently diverge from native JS semantics). GUID randomness is separate and **is** cryptographic (`Guid.NewGuid()`).

---

## R-4 — The host-function surface (supersedes spec 247 R-6 for these functions)

**Decision**: Provide, as JSON-exchanging .NET host functions on the shared read-only surface:
`newGuid`, `newGuidString`, `newShortGuid`, `getGuidString`, `getShortGuid` (GUID strings); `stringToBase64`, `stringFromBase64` (UTF-8 / standard Base64 strings); `now` (ISO-8601 UTC string); `toJson` (canonical JSON via ADR 0091). Keep `isNullOrEmpty`/`isNullOrWhiteSpace` inlined as spec 247 already specifies. **Refuse**: `parseGuid` (returns a live .NET `Guid`), byte/stream helpers (binary, not JSON), `getConfig`/`getConfiguration` (host configuration — deferred to a later spec).

**Rationale**: These are the Elsa 3 built-ins with a faithful, JSON-only Elsa 4 form. Spec 247's research (R-6) previously **refused** `toJson`, the GUID family, and the Base64 pair; those refusals were correct *under the determinism/CLR-value constraints of the time*. With proxies off and values JSON-only, and with the ADR 0091 canonical encoder available, `toJson` is now faithful (no CLR PascalCase casing / HTML-escaping divergence because there are no CLR values to serialize — the encoder defines the shape), and the GUID/Base64 functions return plain strings. This spec therefore **supersedes spec 247 R-6** for exactly these functions; `parseGuid`, bytes/streams stay refused there and here.

`newShortGuid` must reproduce Elsa 3's exact short-GUID encoding (URL-safe Base64 of the 16 GUID bytes, trimmed to 22 chars) — a faithfulness requirement verified during implementation against Elsa 3's `ShortGuid`.

**Corpus cost of the refusals** (JTest, 36 definitions, counted as calls `name(` in the definition JSON, the same throwaway method as spec 247's measured baseline): `getConfig` 0; `getConfiguration` 1 use in 1 definition (`javascript-script-handler-functions`, `getConfiguration(key)[name]`; Elsa 3 has only `getConfig`, so it was unknown there too); `parseGuid` 1 use in 1 definition (`javascript-elsa-functions`, `parseGuid(text)`). Deferring costs at most 2 of 36 definitions.

**Alternatives considered**: Provide `getConfig` now (rejected — pulls in host `IConfiguration` surface and secret-exposure questions beyond S-1's scope; deferred). Keep `toJson` refused (rejected — the original reasons no longer hold once values are JSON-only and the canonical encoder exists). Rename `toJson` to `toCanonicalJson` (considered for self-description; **decided to keep `toJson`** for faithful import and author familiarity).

---

## R-5 — Function effect declaration (D-6)

**Decision (provisional, direction agreed by two approvers)**: A .NET-implemented function registered without a declaration is **effect-free**. The registration API takes one optional flag that declares a function **effectful**; only then does the engine commit before and after an input evaluation that calls it. The flag never refuses, skips or deduplicates a call. The built-ins of R-4 are registered without it. No effect profiles.

**Rationale**: Most functions called from expressions have no side effects, so the default is optimized for them. Treating every registered function as possibly effectful would make every step whose inputs call any function, `newGuid()` included, pay two commits with no way out. The boundary narrows the window for a repeated effect and does not close it, so the designer needs idempotency either way, and external business operations belong in activities. The flag has a consumer: it is what turns the commit-around-effects guarantee on for a function. Accepted cost: an effectful function registered without the flag gets no boundary, and after a crash its effect can repeat anywhere in the step. S-1 adds and records the flag; placing the commits belongs to the execution-contract details amendment (ADR 0045 §3, ADR 0032, ADR 0020).

**Open**: OQ-1. One approver holds the no-metadata position; it goes to the approvers' call if held. ADR 0045 §5's text is on #3384.

**Alternatives considered**: No metadata at all, every registered function possibly effectful (not chosen — two commits for every step whose inputs call any function); an optional durability *hint* that changes only where state is recorded (superseded by the effectful flag, which has a defined consumer); a richer effect classification or function profiles (rejected — no consumer); the earlier `functions.*` object idea (withdrawn by its proposer as adding no value).

---

## R-6 — `binding-readonly-v1` delivered by re-admitting native intrinsics — findings for #3390

**Decision**: Deliver the `binding-readonly-v1` capability profile by re-admitting the native JS intrinsics plus the registered JSON host functions, and introduce the profile constant in code here.

**Rationale**: PR #3390 frames the profile's grants host-side ("time comes from the host's `TimeProvider`"; ".NET functions registered for this profile exchange only JSON") and explicitly **defers to S-1** both "renaming the profile constant in code" and "the registration API for profile-allowed functions". S-1 is where those land.

**Findings to flow back to #3390** (confirmed with Joey):

1. **Native intrinsics are admitted, not just host functions.** #3390's text does not state that native `Date`/`Math`/`Intl`/`Temporal` are re-enabled; S-1 does exactly that (they are the primary way authors handle dates and randomness). #3390's grant description should acknowledge the native-intrinsic surface, not only host-registered `.NET` functions.
2. **The Jint 4.9.1 RNG is not injectable** (R-3). Any #3390 language implying a controllable time/random source should distinguish time (injectable via `ITimeSystem`) from randomness (not injectable in this engine version).

**Alternatives considered**: Deliver the profile purely as host functions with intrinsics kept stripped (rejected per R-1/R-2).

---

## R-7 — Proxy-dependent-usage detector

**Decision**: A static, syntactic detector extending the existing `JavaScriptAnalyzer.VisitNode` walker. Rule:

- **Refuse** a method **invocation** `x.M(...)` when `M` is PascalCase (no native JS prototype method is PascalCase), or in a small curated CLR-method set.
- **Refuse** a reference to a **CLR type** identifier (static access `T.Member`, or construction `new T(...)`) where `T` is in a curated CLR-type catalog that **excludes** JS globals (`String`, `Number`, `Array`, `Object`, `Date`, `Math`, `JSON`, …).
- **Allow** property **reads** `x.P` (may be a legitimate JSON key, including PascalCase keys from .NET-serialized data).
- **Allow** (do not classify) dynamic/computed member access `x[expr](...)` — a recorded gap.
- **Allow** an invocation of a member the script defines itself: a property key of an object literal, a class method or field, or a member assignment `x.M = ...` anywhere in the same script (e.g. `helpers.Format(x)` with `helpers` declared in the script). The walker collects these names in one pass before classifying invocations. A script that defines `M` and also invokes a .NET `M` on a boundary value is not caught — a recorded gap.

Emit a new diagnostic code **`VF-IMP-010`**; the message **quotes the offending expression** (reconstructed from the Acornima node's `.Start`/`.End` offsets against the source the rewriter already holds) and explains the JSON-only / proxies-off contract. Register the detector **only while proxies are disabled** (`!ProxiesEnabled`, A-1 default). Run it at **publish** as well as **convert** (shared pass).

**Rationale**: "Proxy-dependent usage" is a *shape*, not a function list. JSON values carry only native JS prototype methods (camelCase) — so a PascalCase **invocation** can never be a valid native call and is an unambiguous proxy signal with near-zero false positives. .NET static types are not JS globals, so a reference to one is equally unambiguous. Property **reads** are the one case that overlaps with legitimate JSON (PascalCase keys are common in .NET-serialized data), so reads are allowed — the key false-positive guard. A full taint/dataflow engine would be more precise about *which value* is a proxy but is far heavier and can miss proxies from unknown sources; the syntactic net catches them regardless and degrades safely (over-refuses the genuinely weird, never silently passes a proxy call). Gating on `!ProxiesEnabled` keeps the migration unblocked now while preserving the seam: if a setting enables proxies, CLR-member usage becomes valid again and the detector is simply not registered. The same-script exemption removes the one false positive the PascalCase rule has on hand-authored scripts that would otherwise have no override.

**Alternatives considered**: Taint analysis (rejected — complexity the signal doesn't need; no existing dataflow infra); flag PascalCase property reads too (rejected — refuses legitimate JSON keys); refuse all dynamic dispatch (rejected — over-refuses faithful scripts); thread real line/column into the diagnostic (deferred — see R-8; quoting the snippet gives most of the value and quoting survives minified source better than a line number).

---

## R-8 — Converter integration points (existing infrastructure)

**Decision**: Extend the existing pieces rather than add new components.

**Findings (verified in source)**:
- The converter parses with **Acornima 1.6.1** and already runs a real recursive AST visitor — `JavaScriptAnalyzer.VisitNode` in `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` — with correct JS scoping (`IsDeclared` separates a host global from a shadowing local). Node types available: `CallExpression`, `MemberExpression` (`.Object`/`.Property`/`.Computed`), `NewExpression`, `Identifier`, etc. Positions are `int` offsets (`.Start`/`.End`), used today for `SourceEdit`s.
- Name authority is `Internal/Elsa3JavaScriptNames.cs` (`BuiltIns` `FrozenSet`); per-function behaviour tables live in dictionaries in the rewriter (`InlineHelpers`, `InstanceMembers`, `RefusedHostFunctions`, `PureGlobalCalls`, `ElsaThreeGlobals`).
- The rewriter already has an `IsNondeterministicCall` path refusing `Math.random`/`Date.now`/`crypto.randomUUID`, and a globals/`NewExpression` path refusing `new Date()`/`Guid`. **S-1 flips these**: remove the nondeterministic refusal, admit `new Date()`/`Date.now()`/`Date` and `Math.random`, keep `Guid`/`DateTime` constructors and `parseGuid`/bytes/streams/`getConfig` refused, and move the GUID/Base64/`toJson` names out of `RefusedHostFunctions` into resolved host calls.
- Diagnostics are `Elsa3ExpressionRewriteIssue(Code, Message, Guidance)` with codes in `Elsa3ImportDiagnosticCodes` (`VF-IMP-001..009`; `VF-IMP-005 ForbiddenCapability`, `VF-IMP-009 LiveInstanceState`). **No diagnostic carries a JS source line/column today** — hence R-7's "quote the snippet" and OQ-2's follow-up. `VF-IMP-010` is added for proxy-dependent usage (distinct from `LiveInstanceState`, which is about live workflow-instance state).
- There is **no structured return-type catalog** for built-ins (only prose in `RefusedHostFunctions` messages). The detector needs none (R-7 is value-agnostic); a tiny "returned a CLR object in Elsa 3" note-map is used only to enrich messages for the direct cases.

**Rationale / alternative**: Building a parallel analysis component was rejected — the existing walker already has scope handling and the diagnostic pipeline; extension is lower-risk and keeps one source of truth for JS analysis.

---

## R-10 — Time zones in converted scripts

**Decision**: Classify `Date` usage in a converted script as zone-independent or zone-dependent (FR-021, FR-022). Zone-independent usage converts faithfully. Zone-dependent usage converts only when the conversion has a source time zone (ADR 0046 *Dates*: an IANA ID set on the converter feature and recorded on the converted workflow and in the published conversion contract); a converted expression with a source time zone evaluates with that zone as the Jint engine's local time zone (`Options.TimeZone`), the clock instant unchanged. Without a source time zone, zone-dependent usage is refused with a quoted diagnostic. A non-literal string parse converts with a behaviour warning (FR-024).

**Rationale**: Elsa 3 evaluated scripts in the server's time zone. `Date.now()`, `getTime()` and `toISOString()` give the same result on every server; `getHours()`, `toLocaleString()`, the local multi-argument constructor and an offset-less parse (`new Date('2026-10-28T09:15:00')`) do not. Running every converted script in UTC would silently change those on any workflow from a non-UTC server, which faithful-or-refuse forbids and which ADR 0046 removes for literals. The source time zone is the same option ADR 0046 defines for literals, so one setting covers a converted workflow. Jint takes the time zone per engine, so a converted expression can carry it as an evaluator option.

**Non-literal parse**: the converter cannot see whether `new Date(input)` receives text with an offset. ADR 0046 gives a non-literal date expression a behaviour warning, and R-10 follows it rather than refusing (OQ-3). Corpus: 3 definitions parse non-literal strings (`javascript-date-methods`, `performance-minimal-baseline`, `composition-execute-workflow-root`); none uses a local-time getter, local formatting, the multi-argument constructor or an offset-less literal.

**Alternatives considered**: Run every script in UTC and mark all `Date` usage faithful (rejected — silent behaviour change for non-UTC sources); refuse all `Date` usage without a source time zone (rejected — refuses the zone-independent majority for no reason); refuse non-literal parses without a source time zone (the stricter reading; open as OQ-3).

---

## R-9 — Execution-contract basis (context, not re-specified)

**Decision**: Rely on D-2 as the safety basis; do not re-specify it.

**Rationale**: Non-deterministic expressions are safe because a recorded step never re-runs: its value is captured once and reused on retry/recovery; a genuine re-run is a new execution with new values, and idempotency across external effects is the designer's concern with engine-supplied stable invocation ids. This is ADR 0045 §5 / D-2 territory. The Inline activity's old "pure replay" justification (which leaned on `binding-pure-v1`) is withdrawn; its re-justification depends on the D-2 details amendment and `binding-readonly-v1`, **not** on S-1 re-specifying it.
