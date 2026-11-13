# Contract: Elsa 3 conversion & proxy diagnostics

How the Elsa 3 → Elsa 4 converter treats expression constructs, and the proxy-dependent-usage detector. Semantic contract for review.

## D1 — Faithful conversions (must not refuse)

The converter MUST convert these without refusal and preserve behaviour:

- Zone-independent `Date` usage → native `Date` (clock-backed, UTC/invariant); see D1a.
- `Math.random()` → native `Math.random`.
- `newGuid()`, `newGuidString()`, `newShortGuid()`, `getGuidString()`, `getShortGuid()` → the host GUID functions (string results; short GUID byte-for-byte).
- `stringToBase64()`, `stringFromBase64()` → the host Base64 functions.
- `toJson()` → the canonical `toJson`.

These flip the current refusals: the rewriter's nondeterministic-call path (`Math.random`/`Date.now`) and the `new Date()`/globals refusal are removed for these constructs, and the GUID/Base64/`toJson` names move out of the refused-host-function set into resolved host calls.

## D1a — `Date` and the source time zone

The source time zone is the Elsa 3 converter option of ADR 0046 *Dates*, recorded on the converted workflow and in the published conversion contract.

| Usage | No source time zone | Source time zone set |
|---|---|---|
| `Date.now()`, `new Date()` as an instant, `getTime()`/`valueOf()`, epoch arithmetic, `new Date(number)`, `Date.UTC(...)`, `toISOString()`/`toJSON()`, `getUTC*`/`setUTC*`, parse of a literal with `Z` or an offset | Convert | Convert |
| `getHours()`, `getDate()`, `getDay()`, `getMonth()`, `getFullYear()`, `getTimezoneOffset()`, local setters; `toString()`, `toDateString()`, `toTimeString()`, `toLocale*String()`; `new Date(y, m, d, ...)`; parse of a literal without an offset (`new Date('2026-10-28T09:15:00')`, `Date.parse(...)`) | **Refuse** (quotes the expression; guidance: set the source time zone, or use UTC methods or an explicit offset) | Convert; the expression evaluates with the source zone as its local time zone |
| Parse of a non-literal string (`new Date(input)`) | Convert with a behaviour warning (quotes the expression) | Convert |

**Contract tests**: `new Date('2026-10-28T09:15:00').getHours()` refused without a zone; with `Europe/Amsterdam` it converts and returns `9`, and `new Date(Date.UTC(2026, 9, 28, 8, 15)).getHours()` returns `9` (CET) and in July the summer offset applies; `Date.now()` and `new Date().toISOString()` convert in both cases.

## D2 — Refused conversions (must refuse with guidance)

- `Guid` / `DateTime` constructors and static access (CLR types) → refuse.
- `parseGuid` (live .NET `Guid`), byte/stream helpers, `getConfig`/`getConfiguration` → refuse.

Each refusal carries a code, a message, and guidance, as the existing pipeline already does.

## D3 — Proxy-dependent-usage detector

**When active**: only while live-.NET-object proxies are disabled (`!ProxiesEnabled`, A-1 default). If a setting enables proxies, the detector is **not registered**, and the constructs below are valid again.

**Where**: at **conversion** (Elsa 3 import) and at **publish** (hand-authored Elsa 4 scripts) — one shared analysis pass.

**Rules** (input: Acornima AST; see [data-model.md §3](../data-model.md) for the full table):

1. Refuse a **method invocation** whose member name is PascalCase (or a curated CLR method). → `VF-IMP-010`
2. Refuse a reference to a **CLR type** identifier (static access or `new`), where the identifier is in the CLR-type catalog and is not a JS global. → `VF-IMP-010`
3. **Allow** property reads of any casing (JSON keys).
4. **Allow** (do not classify) dynamic/computed member invocation — recorded gap.
5. **Allow** an invocation of a member defined in the same script (object-literal key, class member, or `x.M = ...`).

**Diagnostic `VF-IMP-010`**: message quotes the offending expression (from node offsets); guidance explains boundary values are JSON while proxies are disabled, and how to rewrite to JSON/JS equivalents. No source line/column (OQ-2 follow-up).

**Contract tests**:

| Case | Expected |
|---|---|
| `getVariable('x').ToString()` (after lowering, a boundary value) | Refuse `VF-IMP-010`, message quotes `…ToString()` |
| `someValue.Where(f)` / `.ToArray()` / `.Contains(y)` | Refuse `VF-IMP-010` |
| `DateTime.Now`, `new Guid(s)`, `Convert.ToBase64String(b)` | Refuse `VF-IMP-010` |
| `payload.OrderId` (PascalCase property read) | Allow |
| `s.toUpperCase()`, `a.map(f)`, `JSON.stringify(o)`, `new Date()` | Allow |
| `o[name]()` (computed) | Allow (gap) |
| `const helpers = { Format: x => x }; helpers.Format(v)` | Allow (defined in the script) |
| Same proxy-using script with proxies **enabled** | Not refused by this detector |

## D4 — Faithfulness note

`newShortGuid` conversion is faithful only if the Elsa 4 host function reproduces the Elsa 3 short-GUID encoding exactly; this is verified during implementation against Elsa 3's `ShortGuid`.
