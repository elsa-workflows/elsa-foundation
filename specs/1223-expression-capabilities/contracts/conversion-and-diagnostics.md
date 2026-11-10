# Contract: Elsa 3 conversion & proxy diagnostics

How the Elsa 3 → Elsa 4 converter treats expression constructs, and the proxy-dependent-usage detector. Semantic contract for review.

## D1 — Faithful conversions (must not refuse)

The converter MUST convert these without refusal and preserve behaviour:

- `new Date()`, `Date.now()`, `Date` → native `Date` (clock-backed, UTC/invariant).
- `Math.random()` → native `Math.random`.
- `newGuid()`, `newGuidString()`, `newShortGuid()`, `getGuidString()`, `getShortGuid()` → the host GUID functions (string results; short GUID byte-for-byte).
- `stringToBase64()`, `stringFromBase64()` → the host Base64 functions.
- `toJson()` → the canonical `toJson`.

These flip the current refusals: the rewriter's nondeterministic-call path (`Math.random`/`Date.now`) and the `new Date()`/globals refusal are removed for these constructs, and the GUID/Base64/`toJson` names move out of the refused-host-function set into resolved host calls.

## D2 — Refused conversions (must refuse with guidance)

- `Guid` / `DateTime` constructors and static access (CLR types) → refuse.
- `parseGuid` (live .NET `Guid`), byte/stream helpers, `getConfig`/`getConfiguration` → refuse.

Each refusal carries a code, a message, and guidance, as the existing pipeline already does.

## D3 — Proxy-dependent-usage detector

**When active**: only while live-.NET-object proxies are disabled (`!ProxiesEnabled`, A-1 default). When a future setting enables proxies, the detector is **not registered**, and the constructs below are valid again.

**Where**: at **conversion** (Elsa 3 import) and at **publish** (hand-authored Elsa 4 scripts) — one shared analysis pass.

**Rules** (input: Acornima AST; see [data-model.md §3](../data-model.md) for the full table):

1. Refuse a **method invocation** whose member name is PascalCase (or a curated CLR method). → `VF-IMP-010`
2. Refuse a reference to a **CLR type** identifier (static access or `new`), where the identifier is in the CLR-type catalog and is not a JS global. → `VF-IMP-010`
3. **Allow** property reads of any casing (JSON keys).
4. **Allow** (do not classify) dynamic/computed member invocation — recorded gap.

**Diagnostic `VF-IMP-010`**: message quotes the offending expression (from node offsets); guidance explains boundary values are JSON and proxies are disabled, and that proxies are a separate future capability. No source line/column (OQ-2 follow-up).

**Contract tests**:

| Case | Expected |
|---|---|
| `getVariable('x').ToString()` (after lowering, a boundary value) | Refuse `VF-IMP-010`, message quotes `…ToString()` |
| `someValue.Where(f)` / `.ToArray()` / `.Contains(y)` | Refuse `VF-IMP-010` |
| `DateTime.Now`, `new Guid(s)`, `Convert.ToBase64String(b)` | Refuse `VF-IMP-010` |
| `payload.OrderId` (PascalCase property read) | Allow |
| `s.toUpperCase()`, `a.map(f)`, `JSON.stringify(o)`, `new Date()` | Allow |
| `o[name]()` (computed) | Allow (gap) |
| Same proxy-using script with proxies **enabled** | Not refused by this detector |

## D4 — Faithfulness note

`newShortGuid` conversion is faithful only if the Elsa 4 host function reproduces the Elsa 3 short-GUID encoding exactly; this is verified during implementation against Elsa 3's `ShortGuid`.
