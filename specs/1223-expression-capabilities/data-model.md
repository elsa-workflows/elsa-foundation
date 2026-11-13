# Data model: Expression capabilities (Elsa 4) — S-1

Structures and classification rules behind [plan.md](plan.md). These are semantic models, not serialized schemas — no persistence changes.

## 1. Capability profile — `binding-readonly-v1`

The read-only set of capabilities granted to expressions on **both** surfaces (input bindings and `RunJavaScript` scripts), served by one engine.

| Element | Content |
|---|---|
| Native JS intrinsics (admitted) | `Date`, `Temporal`, `Intl`, `Math` (including `Math.random`), plus the always-present `String`/`Number`/`Array`/`Object`/`JSON` globals |
| Clock | Native `Date`/`Date.now()` read the host `TimeProvider` via a custom `ITimeSystem`; `TimeZone = UTC`, `Culture = Invariant` |
| Randomness | Native `Math.random` (uncontrolled RNG — accepted limitation) |
| Host functions | The JSON-exchanging .NET functions in §2 |
| Boundary contract | Every value in/out is a JSON value; no live .NET objects (A-1 proxies off) |
| Not granted | Engine-owned state mutation, live .NET objects/proxies, unregistered functions, host configuration, binary/stream data |

Supersedes the `binding-pure-v1` profile (which forbade time/random). The profile **constant** is introduced in engine code by S-1 (deferred to S-1 by PR #3390).

## 2. Host-function catalog

All registered as JSON-exchanging functions; all arguments and results are JSON values.

| Function | Args | Returns | Notes |
|---|---|---|---|
| `newGuid` | — | string | Canonical GUID string; `Guid.NewGuid()` (crypto-grade) |
| `newGuidString` | — | string | GUID string (Elsa 3 parity) |
| `newShortGuid` | — | string | **Must reproduce Elsa 3 short-GUID encoding exactly** (URL-safe Base64 of 16 bytes, 22 chars) |
| `getGuidString` | — | string | Elsa 3 parity |
| `getShortGuid` | — | string | Elsa 3 parity (short form) |
| `stringToBase64` | string | string | UTF-8 → standard Base64 |
| `stringFromBase64` | string | string | standard Base64 → UTF-8 |
| `now` | — | string | ISO-8601 UTC timestamp (clock-backed sugar / import target) |
| `toJson` | any JSON value | string | Canonical JSON via the shared read-only encoder (ADR 0091) |

**Inlined elsewhere (unchanged, spec 247):** `isNullOrEmpty`, `isNullOrWhiteSpace`.

**Not provided (refused on use):** `parseGuid` (live .NET `Guid`), `bytesToString`/`bytesFromString`/`bytesToBase64`/`bytesFromBase64`/`streamToBytes`/`streamToBase64` (binary/stream, not JSON), `getConfig`/`getConfiguration` (host configuration — deferred).

**Effect declaration (D-6; OQ-1)**: a function registered without a declaration is effect-free. A host may set one optional **effectful** flag when registering a function; only then are commits placed before and after an input evaluation that calls it. The flag never refuses, skips or deduplicates a call. All functions above are registered without it. No other metadata, no effect profiles.

## 3. Proxy-dependent-usage classification

Applied by the detector (extends `JavaScriptAnalyzer.VisitNode`) **only while `!ProxiesEnabled`**. Input: an Acornima AST node. Output: allow, or refuse with `VF-IMP-010`.

| AST shape | Example | Verdict |
|---|---|---|
| Method **invocation**, PascalCase member | `x.ToString("N")`, `a.Where(f)`, `s.ToUpper()` | **Refuse** |
| Method **invocation**, curated CLR method | `a.Contains(x)` (CLR; JS uses `includes`) | **Refuse** |
| Method **invocation**, camelCase/native member | `s.toUpperCase()`, `a.map(f)`, `o.toString()` | Allow |
| Property **read**, any casing | `payload.OrderId`, `x.Count` | Allow (JSON key) |
| **CLR type** identifier — static access | `DateTime.Now`, `Convert.ToBase64String(...)`, `Enumerable.Range(...)` | **Refuse** |
| **CLR type** identifier — construction | `new Guid(...)`, `new DateTime(...)` | **Refuse** |
| **JS global** identifier | `JSON.stringify(...)`, `Math.max(...)`, `new Date()` | Allow |
| **Dynamic/computed** member invocation | `o[name]()` | Allow (recorded gap) |
| Invocation of a member **defined in the same script** (object-literal key, class member, `x.M = ...`) | `helpers.Format(x)` with `const helpers = { Format: ... }` | Allow |

**CLR-type deny catalog** (identifiers; excludes every JS global): `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Convert`, `Encoding`, `Enumerable`, `Queryable`, `Regex`, `StringBuilder`, `CultureInfo`, `TimeZoneInfo`, `BitConverter`, `File`, `Directory`, `Path`, `Stream`, `Uri` (curated; extended as the corpus warrants).

**JS-global allow set** (never treated as CLR types): `String`, `Number`, `Boolean`, `Array`, `Object`, `Date`, `Math`, `JSON`, `Intl`, `Temporal`, `RegExp`, `Map`, `Set`, `Symbol`, `BigInt`, `Promise`, `Error`, `Function`.

**PascalCase rule**: a member name whose first letter is uppercase, when **invoked**, is treated as a .NET member. (Enrichment only: when the invocation target is a known converted function — e.g. `newGuid().ToString(...)` — the message names the Elsa 3 CLR return type via a small note-map.)

## 4. Diagnostic — `VF-IMP-010`

| Field | Content |
|---|---|
| Code | `VF-IMP-010` (added to `Elsa3ImportDiagnosticCodes`; distinct from `VF-IMP-009 LiveInstanceState`) |
| Message | Quotes the offending expression (from node `.Start`/`.End`) and states that boundary values are JSON and live .NET objects (proxies) are disabled |
| Guidance | How to rewrite to JSON/JS equivalents; live .NET objects are disabled in this configuration |
| Severity | Error (refusal) |
| Path/metadata | Reuses the existing activity JSON-path + metadata the lowerer already attaches |

No JS source line/column (OQ-2 follow-up); the quoted snippet is the locator and survives minified source.

## 5. Elsa 3 → Elsa 4 conversion disposition

Updates the conversion table in `Extensions/Elsa3/src/Mapping/README.md`.

| Elsa 3 construct | Current | S-1 disposition |
|---|---|---|
| Zone-independent `Date` usage (`Date.now()`, `getTime()`, `toISOString()`, epoch arithmetic, UTC methods, parse with offset) | Refused | **Faithful** (native `Date` restored, clock-backed) |
| Zone-dependent `Date` usage (local getters/setters, local formatting, local multi-argument constructor, offset-less literal parse) | Refused | **Faithful with a source time zone** (evaluated in that zone); refused without one |
| `Date` string parse of a non-literal | Refused | Faithful with a source time zone; behaviour warning without one |
| `Math.random()` | Refused | **Faithful** (native restored) |
| `newGuid`, `newGuidString`, `newShortGuid`, `getGuidString`, `getShortGuid` | Refused | **Faithful** (host functions → strings) |
| `stringToBase64`, `stringFromBase64` | Refused | **Faithful** (host functions) |
| `toJson` | Refused | **Faithful** (canonical encoder) |
| `isNullOrEmpty`, `isNullOrWhiteSpace` | Inlined | Inlined (unchanged) |
| `parseGuid` | Refused | Refused (live .NET `Guid`) |
| `bytes*` / `stream*` helpers | Refused | Refused (binary/stream) |
| `getConfig` / `getConfiguration` | Refused | Refused (deferred) |
| `Guid` / `DateTime` constructors, static access | Refused | Refused (CLR type) |
| CLR-member usage on a boundary value (proxy) | — (cryptic runtime failure) | **Refused at convert + publish** (`VF-IMP-010`) while proxies off |
