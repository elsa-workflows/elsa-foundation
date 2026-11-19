# Data model: Expression capabilities (Elsa 4) — S-1

Structures and classification rules behind [plan.md](plan.md). These are semantic models, not serialized schemas. The one stored change is the profile constant (§1), which changes expression definitions and executable hashes; fixtures are regenerated (no compatibility before the first stable release).

## 1. Capability profile — `binding-readonly-v1`

ADR 0083 defines the profile and spec 095's expression contract holds its full text. S-1 delivers it:

| Element | Content |
|---|---|
| Native JS intrinsics (admitted) | `Date`, `Temporal`, `Intl`, `Math` (including `Math.random`), plus the always-present `String`/`Number`/`Array`/`Object`/`JSON` globals |
| Clock | Native `Date`/`Date.now()`/`Temporal.Now` read the host `TimeProvider` via a custom Jint `ITimeSystem`; `TimeZone = UTC` (or the converted expression's source time zone), `Culture = Invariant` |
| Randomness | Native `Math.random` (uncontrolled RNG — accepted limitation) |
| Functions | The built-in functions in §2 and host functions registered under §2's rules |
| Boundary contract | Default strategy: every value in and out is a JSON value; no live .NET objects (ADR 0046 V10) |
| Writes | Unchanged: `RunJavaScript` writes variables (ADR 0090); input bindings do not |

The constant `ExpressionCapabilityProfiles.BindingPureV1` (Expressions.Core) becomes `BindingReadonlyV1` with the value `binding-readonly-v1`, and `ExpressionEvaluationCapabilities` admits time and randomness. No alias for the old value.

## 2. Functions

### 2.1 Registration

One registration per function, extending spec 1222's `JavaScriptHostFunctionDeclaration`:

| Field | Meaning |
|---|---|
| `Name` | Global name. Built-ins: unprefixed and reserved. Host functions: namespace-prefixed (`acme.lookup`). Duplicate or reserved name → startup failure naming the function. |
| `ReadsVariables` | Whether the function reads workflow variables (ADR 0089 R3). All built-ins: `false`. |
| `Effectful` | D-6 declaration; default `false`. All built-ins: `false`. The only effect metadata. |
| Implementation | The .NET delegate the engine installs; JSON values in and out. |

A registered function is allowed in `binding-readonly-v1` (ADR 0083). Functions are installed as non-writable globals, as `getVariable` is today.

**Reserved names**: the read roots `variables`, `args`, and the built-in names below. A script declaring one gets the publish warning `Scripts/ReservedName` (which already exists for `complete`).

**Precision (ADR 0046 V9)**: a number argument JavaScript cannot hold exactly logs a warning naming the function and the argument path; the call runs.

### 2.2 Built-in catalog

| Function | Args | Returns | Elsa 3 behaviour reproduced |
|---|---|---|---|
| `newGuid` | — | string | Elsa 3 returned a .NET `Guid`; its text (`D`, 36 chars) is returned |
| `newGuidString` | — | string | `Guid.NewGuid().ToString()` |
| `getGuidString` | — | string | Same as `newGuidString` (a new GUID, not a getter) |
| `newShortGuid` | — | string | `Regex.Replace(Convert.ToBase64String(Guid.NewGuid().ToByteArray()), "[/+=]", "")` — at most 22 chars |
| `getShortGuid` | — | string | Same as `newShortGuid` |
| `parseGuid` | string | string | Elsa 3 returned a .NET `Guid` from `Guid.Parse`; its `D` text is returned; invalid text fails the evaluation |
| `stringToBase64` | string | string | UTF-8 → standard Base64 |
| `stringFromBase64` | string | string | standard Base64 → UTF-8 |
| `toJson` | JSON value | string | System.Text.Json, `JavaScriptEncoder.Create(UnicodeRanges.All)`, not indented, no naming policy |

**Inlined elsewhere (unchanged, spec 247):** `isNullOrEmpty`, `isNullOrWhiteSpace`.

**Not provided (refused on use):** `bytesToString`/`bytesFromString`/`bytesToBase64`/`bytesFromBase64`/`streamToBytes`/`streamToBase64` (binary/stream, not JSON), `getConfig`/`getConfiguration` (host configuration — deferred).

**Dropped**: the 10 Nov `now()` helper (OQ-6). Date conversion between zones uses native `Temporal`/`Intl` (FR-025).

## 3. .NET-usage classification

One analysis in the Essentials JavaScript analysis services (beside spec 1222's `JavaScriptVariableReadAnalyzer`), called by the converter and by publish validation. It belongs to the default JSON-only engine strategy (FR-030). Input: an Acornima AST, plus — at conversion — the Elsa 3 types of the script's variables, inputs and activity outputs.

### 3.1 Known .NET-object values (conversion only, FR-016)

| Source | Example |
|---|---|
| Result of `newGuid()` / `parseGuid(...)` | `newGuid().toString('N')` |
| Read of a variable whose Elsa 3 type was a wrapped .NET type | `variables.upload.openReadStream()` (`upload: FormFile`) |
| Read of an input or activity output whose Elsa 3 type was a wrapped .NET type | an `HttpEndpoint` files output |
| A `const`/`let` alias of any of the above in the same script | `const g = newGuid(); g.toByteArray()` |

**Wrapped .NET types**: every Elsa 3 type that Elsa 3's Jint handed to scripts as an object wrapper rather than converting it — not `string`, the number types, `bool`, `DateTime`/`DateTimeOffset` (converted to JS `Date`), `object`/`ExpandoObject`/JSON objects and arrays (converted to JS values). Examples: `Guid`, `TimeSpan`, `Uri`, `FormFile`, `Stream`, `List<T>`, `Dictionary<,>`. The catalog is data in the converter, beside the Elsa 3 type map.

On a known .NET-object value: any member read or invocation → **refuse** (`VF-IMP-012`), except `toString()` with no arguments on a GUID (same text). Use as a whole value (concatenation, comparison, return, argument) → allow, except as the argument of `toJson` when the value is not a GUID → **refuse** (Elsa 3 serialized its .NET properties).

### 3.2 Rules on every script (conversion and publish, FR-017, FR-019)

| AST shape | Example | Verdict |
|---|---|---|
| **.NET type** identifier — static access or construction | `TimeSpan.FromHours(1)`, `new Uri(s)`, `Enumerable.Range(0, 3)` | **Refuse** |
| Invocation of a PascalCase member on a value the script did not define | `a.Where(f)`, `s.ToUpper()` | **Refuse** |
| Invocation whose receiver is a JS global | `Intl.DateTimeFormat('en-GB')`, `Math.max(1, 2)` | Allow |
| Invocation of a member defined in the same script (object-literal key, class member, `x.M = ...`) | `helpers.Format(x)` | Allow |
| Invocation, camelCase/native member (not §3.1) | `s.toUpperCase()`, `a.map(f)` | Allow |
| Property **read**, any casing (not §3.1) | `payload.OrderId`, `variables.Order.Status` | Allow (JSON key) |
| **Dynamic/computed** member invocation | `o[name]()` | Allow (recorded gap) |

At conversion, .NET globals already in the converter's `ElsaThreeGlobals` refusal (`DateTime`, `Guid`, `Random`, …) keep `VF-IMP-005`; the type rule covers the rest.

**.NET type catalog** (identifiers; excludes every JS global): `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, `Convert`, `Encoding`, `Enumerable`, `Queryable`, `Regex`, `StringBuilder`, `CultureInfo`, `TimeZoneInfo`, `BitConverter`, `File`, `Directory`, `Path`, `Stream`, `Uri` (curated; extended as the corpus warrants).

**JS-global allow set**: `String`, `Number`, `Boolean`, `Array`, `Object`, `Date`, `Math`, `JSON`, `Intl`, `Temporal`, `RegExp`, `Map`, `Set`, `WeakMap`, `WeakSet`, `Symbol`, `BigInt`, `Promise`, `Error`, `Function`, `Proxy`, `Reflect`.

## 4. Diagnostics

| Surface | Code | Severity | Message / guidance |
|---|---|---|---|
| Conversion | `VF-IMP-012` (next free import code; `010`/`011` belong to spec 1222) | Error | Quotes the offending expression (node `.Start`/`.End`); says values are JSON and live .NET objects are not available; how to rewrite with JS or a built-in |
| Publish | `Scripts/DotNetMemberUsage` | Error | Same message and guidance |
| Publish | `Scripts/ReservedName` (extended) | Warning | Names the reserved name the script declares |
| Startup | — | Failure | Duplicate or reserved host-function name |
| Run time | — | Warning (log) | Lossy number argument to a host function (V9) |

No JS source line/column (OQ-2 follow-up); the quoted snippet is the locator.

## 5. Elsa 3 → Elsa 4 conversion disposition

Updates the conversion table in `Extensions/Elsa3/src/Mapping/README.md`.

| Elsa 3 construct | Current | S-1 disposition |
|---|---|---|
| Zone-independent `Date` usage (`Date.now()`, `getTime()`, `toISOString()`, epoch arithmetic, UTC methods, parse with offset) | Refused | **Faithful** (native `Date`, clock-backed) |
| Zone-dependent `Date` usage (local getters/setters, local formatting, local multi-argument constructor, offset-less literal parse) | Refused | **Faithful with a source time zone** (evaluated in that zone); refused without one |
| `Date` string parse of a non-literal | Refused | Faithful with a source time zone; behaviour warning without one |
| `Math.random()` | Refused | **Faithful** |
| `crypto.randomUUID()` | Refused | Refused (not in Elsa 3) |
| `newGuid`, `newGuidString`, `newShortGuid`, `getGuidString`, `getShortGuid`, `parseGuid` | Refused | **Faithful** (built-ins → strings) |
| Member use on a `newGuid()`/`parseGuid()` result, other than `toString()` | — | Refused (`VF-IMP-012`) |
| `stringToBase64`, `stringFromBase64` | Refused | **Faithful** |
| `toJson` | Refused | **Faithful** (Elsa 3 encoder) |
| `isNullOrEmpty`, `isNullOrWhiteSpace` | Inlined | Inlined (unchanged) |
| `bytes*` / `stream*` helpers | Refused | Refused (binary/stream) |
| `getConfig` / `getConfiguration` | Refused | Refused (deferred) |
| `Guid` / `DateTime` / `Random` globals | Refused (`VF-IMP-005`) | Unchanged |
| Other .NET types; member use on a wrapped .NET-typed variable, input or output | — (run-time failure) | **Refused** (`VF-IMP-012`) |
