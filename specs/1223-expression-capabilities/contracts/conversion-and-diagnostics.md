# Contract: Elsa 3 conversion & .NET-usage diagnostics

How the Elsa 3 → Elsa 4 converter treats expression constructs, and the .NET-usage analysis shared by conversion and publish. Semantic contract for review.

## D1 — Faithful conversions (must not refuse)

The converter MUST convert these without refusal and preserve behaviour:

- Zone-independent `Date` usage → native `Date` (clock-backed, UTC/invariant); see D1a.
- `Math.random()` → native `Math.random`.
- `newGuid()`, `newGuidString()`, `newShortGuid()`, `getGuidString()`, `getShortGuid()`, `parseGuid(s)` → the built-in GUID functions (string results, Elsa 3's text).
- `stringToBase64()`, `stringFromBase64()` → the built-in Base64 functions.
- `toJson(x)` → `JSON.stringify(x)`, with a behaviour warning that quotes the expression and names the text differences (escaping, `Date` precision, `undefined`, `NaN`). Elsa 4 has no `toJson`.

These flip the current refusals: the rewriter's nondeterministic-call path is removed for `Math.random`/`Date.now` (it keeps `crypto.randomUUID`), the `Date` global leaves the refused globals, and the GUID/Base64/`parseGuid` names move out of `RefusedHostFunctions` and `toJson` gets the rewrite. Guidance texts that say "no clock" or "no random" are reworded.

## D1a — `Date` and the date interpretation

The date interpretation is the Elsa 3 converter setting of FR-034 (ADR 0046 *Dates*), recorded on the converted workflow and in the published conversion contract: `Refuse` (default), `AssumeUtc`, or `SourceTimeZone` with an IANA ID. S-1 applies it to scripts; S-9 (spec 1207) applies the same setting at the JSON to .NET boundary.

| Usage | `Refuse` (default) | `AssumeUtc` | `SourceTimeZone` |
|---|---|---|---|
| `Date.now()`, `new Date()` as an instant, `getTime()`/`valueOf()`, epoch arithmetic, `new Date(number)`, `Date.UTC(...)`, `toISOString()`/`toJSON()`, `getUTC*`/`setUTC*`, parse of a literal with `Z` or an offset; parse of a date-only literal (`new Date('2026-10-28')`, UTC in ECMAScript); a date-typed Elsa 3 value rewritten to a `Date` (FR-032) used as an instant | Convert | Convert | Convert |
| `getHours()`, `getDate()`, `getDay()`, `getMonth()`, `getFullYear()`, `getTimezoneOffset()`, local setters; `toString()`, `toDateString()`, `toTimeString()` on a receiver known to be a `Date`; `new Date(y, m, d, ...)`; parse of a date-time literal without an offset (`new Date('2026-10-28T09:15:00')`, `Date.parse('2026-10-28T09:15:00')`); parse of a non-literal string (`new Date(input)`, `Date.parse(input)`) | **Refuse**: quotes the expression; guidance: set `SourceTimeZone` to the Elsa 3 server's IANA zone (for example `Europe/Amsterdam`), or set `AssumeUtc` to accept UTC, or rewrite with UTC methods or an explicit offset | Convert, evaluate in UTC, **behaviour warning** quoting the expression (differs from Elsa 3 if its server was not on UTC) | Convert; evaluate with the zone as local time zone; no warning |
| `toLocaleString()`, `toLocaleDateString()`, `toLocaleTimeString()`, `localeCompare`, `Intl` constructors **without** an explicit locale (FR-033) | **Refuse** (guidance: pass an explicit locale) | **Refuse** (same) | **Refuse** (same) |

Date-only method names (`getHours` ...) are recognised by name; the generic names (`toString`, `valueOf`) only on a receiver known to be a `Date` (`new Date(...)`, a `Date`-returning call, a rewritten date value, or a `const`/`let` alias). A `Date` reached indirectly is not classified (gap).

**Date-typed Elsa 3 values (FR-032)**: each direct read of a variable, input or activity output whose Elsa 3 type was `DateTime`/`DateTimeOffset` is rewritten to a `Date` of the same instant, `null` kept (`v == null ? v : new Date(v)`), because Elsa 3 handed such values to scripts as a `Date` and Elsa 4 hands over ISO text.

Under `SourceTimeZone` the zone travels with the converted expression as part of its published form (an evaluator option today rejected by `JintPortableJavaScriptEvaluator`; S-1 adds it, and it enters the executable hash).

**Contract tests**: under `Refuse`, `new Date('2026-10-28T09:15:00').getHours()` and `new Date(input)` are refused and the message names `SourceTimeZone` and `AssumeUtc`; under `AssumeUtc` both convert with a behaviour warning and `getHours()` returns `9`; `new Date('2026-10-28')` converts under every value; `variables.due.getTime()` with `due: DateTime` converts and equals Elsa 3; `n.toLocaleString()` refused, `n.toLocaleString('nl-NL')` converts; under `SourceTimeZone` = `Europe/Amsterdam` it converts and returns `9`, and `new Date(Date.UTC(2026, 9, 28, 8, 15)).getHours()` returns `9` (CET); in July the summer offset applies; `Date.now()` and `new Date().toISOString()` convert under every value.

## D2 — Refused conversions (must refuse with guidance)

- `Guid` / `DateTime` / `Random` globals (constructors and static access) → refused as today (`VF-IMP-005`).
- Byte/stream helpers, `getConfig`/`getConfiguration`, `crypto.randomUUID` → refused as today.

## D3 — .NET-usage analysis

**Where**: one analysis in the Essentials JavaScript analysis services, called by the converter (Elsa 3 import, with the Elsa 3 types) and by publish validation (any script). It belongs to the default JSON-only engine strategy; this spec adds no switch. Whether it applies under the opt-in .NET interop strategy is decided by that feature (#4103).

**Rules** (see [data-model.md §3](../data-model.md)):

1. *Conversion only*: refuse any member read or invocation on a **known .NET-object value** — the result of `newGuid()`/`parseGuid()`, or a read of a variable, input or activity output whose Elsa 3 type was a wrapped .NET type — used directly or through a `const`/`let` alias; except `toString()` with no arguments on a GUID; also refuse passing such a value, other than a GUID, to `toJson`. → `VF-IMP-012`
2. Refuse a reference to a **.NET type** identifier (static access or `new`) in the catalog and not a JS global; at conversion, names already refused under `VF-IMP-005` keep that code.
3. Refuse an invocation of a **PascalCase member** on a value the script did not define, unless the receiver is a JS global.
   Rules 2 and 3 never apply to a name the script declares itself (`const Path = …`) or to a registered function or namespace.
4. **Allow** property reads of any casing on other values (JSON keys). On a known value whose Elsa 3 type was a list or dictionary, allow property reads and native `Array`/`Object` member calls (`length`, `map`, `filter`, …), which behaved the same in Elsa 3.
5. **Allow** (do not classify) dynamic/computed member access — recorded gap; values reached indirectly (function arguments, computed reads, object fields) are not followed — recorded gap.
6. **Allow** an invocation of a member defined in the same script (object-literal key, class member, or `x.M = ...`).

**Diagnostics**: conversion `VF-IMP-012`; publish `Scripts/DotNetMemberUsage`. The message quotes the offending expression (from node offsets); guidance explains that values are JSON and live .NET objects are not available, and how to rewrite. No source line/column (OQ-2 follow-up).

**Contract tests**:

| Case | Expected |
|---|---|
| `newGuid().toString('N')` (converted) | Refuse `VF-IMP-012`, message quotes it |
| `const g = parseGuid(s); g.toByteArray()` (converted) | Refuse `VF-IMP-012` |
| `variables.upload.openReadStream()` with `upload: FormFile` in Elsa 3 (converted) | Refuse `VF-IMP-012` |
| `'id-' + newGuid()`, `newGuid().toString()`, `toJson(newGuid())` → `JSON.stringify(newGuid())` (converted) | Allow (the `toJson` rewrite warns) |
| `toJson(variables.upload)` with `upload: FormFile` in Elsa 3 (converted) | Refuse `VF-IMP-012` |
| `someValue.Where(f)` / `.ToArray()` | Refuse (conversion `VF-IMP-012`, publish `Scripts/DotNetMemberUsage`) |
| `TimeSpan.FromHours(1)`, `new Uri(s)` | Refuse |
| `DateTime.Now`, `new Guid(s)` (converted) | Refuse `VF-IMP-005` (unchanged) |
| `payload.OrderId`, `variables.Order.Status` | Allow |
| `s.toUpperCase()`, `a.map(f)`, `JSON.stringify(o)`, `new Date()`, `Intl.DateTimeFormat('en-GB')` | Allow |
| `o[name]()` (computed) | Allow (gap) |
| `variables.items.map(f)`, `variables.items.length` with `items: List<string>` in Elsa 3 (converted) | Allow |
| `variables.items.Add(x)` with `items: List<string>` in Elsa 3 (converted) | Refuse `VF-IMP-012` |
| `const Path = { join: (a, b) => a + b }; Path.join(x, y)`; a registered `acme.Lookup(x)` | Allow |
| `const helpers = { Format: x => x }; helpers.Format(v)` | Allow (defined in the script) |

## D4 — Faithfulness note

GUID and Base64 conversions are faithful because the built-ins reproduce Elsa 3's algorithms (verified against Elsa 3.5.3's `ConfigureEngineWithCommonFunctions` during implementation, with fixed inputs). The `toJson` rewrite is a documented exception with a warning (D-13).
