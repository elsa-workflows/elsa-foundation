# Contract: Clock and UniqueId parameters and the binding-pure-v2 Date

This is the author-facing contract. The documentation (FR-025) and the tests (SC-003) are written against it.

## Declaring the parameters

A full portable definition as the input value:

```json
{
  "language": "JavaScript",
  "source": "({ id: args.orderId, at: args.now })",
  "resultType": { "alias": "Object", "collectionKind": "Single" },
  "capabilityProfile": "binding-pure-v2",
  "parameters": {
    "now":     { "kind": "Clock" },
    "orderId": { "kind": "UniqueId" }
  },
  "options": {},
  "metadata": {}
}
```

- `args.now` is, for example, `"2026-10-20T08:30:00.123Z"`. Every `Clock` parameter in one evaluation has the same
  value.
- `args.orderId` is, for example, `"01929f1c-7e2a-7c3d-9b1e-4f2a6c8d0e11"`. Each `UniqueId` parameter has its own value.
- When the values are fixed: when the step's inputs commit. A retry sees the same values. Each `While` pass gets new
  values.
- UUID version 7 values are unique and time-ordered. They are not secrets; do not use them as unguessable tokens.

## `Date` under binding-pure-v2

| Category | Members |
|---|---|
| Allowed | `new Date(isoString)`, `new Date(epochMs)`, `new Date(y, m[, d, h, min, s, ms])`, `new Date(date)`, `Date.UTC`, `Date.parse`, every `get*` / `getUTC*` / `set*` / `setUTC*` method, `getTime`, `valueOf`, `getTimezoneOffset` (always 0), `toISOString`, `toJSON`, `toString`, `toDateString`, `toTimeString`, `toUTCString`, comparison, subtraction and `instanceof Date` |
| Removed | `new Date()`, `Date()`, `Date.now()`, `toLocaleString()`, `toLocaleDateString()`, `toLocaleTimeString()` |
| Constrained | time zone is UTC (local getters equal UTC getters); only ECMAScript date-time strings parse (`YYYY`, `YYYY-MM`, `YYYY-MM-DD`, optionally `THH:mm`, `:ss`, `.sss`, then `Z` or `±HH:mm`; expanded years `±YYYYYY`). Any other string gives an invalid date. A date-time without an offset is read as UTC. |

`Temporal`, `Intl` and `Math.random` remain undefined.

## Messages

Thrown as `TypeError` at runtime and reported by authoring validation at the authored location. The text is stable.

| Form | Message | Validation severity |
|---|---|---|
| `new Date()` | `new Date() with no argument reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write new Date(args.now).` | error |
| `Date()` | `Date() called as a function reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write new Date(args.now).toString().` | error |
| `Date.now()` | `Date.now() reads the clock, which binding-pure-v2 does not allow. Declare a Clock parameter and write Date.parse(args.now).` | error |
| `toLocaleString()` | `toLocaleString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString(), or format the parts with the UTC getters.` | warning |
| `toLocaleDateString()` | `toLocaleDateString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString().slice(0, 10), or format the parts with the UTC getters.` | warning |
| `toLocaleTimeString()` | `toLocaleTimeString() depends on a locale, which binding-pure-v2 does not provide. Use toISOString().slice(11, 23), or format the parts with the UTC getters.` | warning |
| `toISOString()` on an invalid date | `Invalid date: binding-pure-v2 parses only ISO 8601 date-time strings such as 2026-10-20T08:30:00.000Z.` (`RangeError`) | — |

The `toLocale*` forms are warnings in validation, because the receiver may not be a date. At runtime they always fail
on a date.

## Runtime failure for a missing instant

`VF-EXP-CLOCK-001: Parameter '<name>' on input '<input>' of executable node '<node>' binds '<Clock|UniqueId>', but this
evaluation has no evaluation instant.` This is defensive; no supported evaluation site reaches it.
