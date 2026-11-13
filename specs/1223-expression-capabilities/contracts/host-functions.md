# Contract: Expression capability surface & host functions

The capabilities and functions available to Elsa 4 JavaScript expressions under `binding-readonly-v1`, and their behavioural contract. Semantic contract for review; signatures are illustrative, not final code.

## C1 — One surface, two entry points

- A single engine configuration serves input-binding expressions **and** `RunJavaScript` scripts. Any capability granted to one is granted to the other.
- The surface is **read-only**: no engine-owned state mutation and no unregistered functions. While live-.NET-object proxies are disabled, no live .NET objects cross it.

## C2 — Native intrinsics

- `Date`, `Temporal`, `Intl`, `Math` (incl. `Math.random`) are available and behave as standard JavaScript.
- `new Date()` and `Date.now()` return the **host clock** time. The engine is configured `TimeZone = UTC`, `Culture = Invariant`; `Date` values therefore reflect UTC and format culture-independently. A converted Elsa 3 expression with a recorded source time zone evaluates with that zone as its local time zone (conversion contract D1a).
- `Math.random` uses the engine's native RNG. **No reproducibility guarantee** — callers and tests must not assume a fixed sequence.

**Contract test cues**: with a controlled host `TimeProvider`, `new Date().toISOString()` and `Date.now()` reflect the provided instant in UTC; `Intl`/`Temporal` are defined (not `undefined`); two `Math.random()` calls are permitted to differ.

## C3 — Host functions (JSON in / JSON out)

Every argument and every result is a JSON value. No overload accepts or returns a live .NET object.

| Function | Signature (illustrative) | Behaviour |
|---|---|---|
| `newGuid()` | `() => string` | New canonical GUID string; crypto-grade uniqueness |
| `newGuidString()` | `() => string` | GUID string (Elsa 3 parity) |
| `newShortGuid()` | `() => string` | Short GUID — **byte-for-byte the Elsa 3 encoding** |
| `getGuidString()` | `() => string` | Elsa 3 parity |
| `getShortGuid()` | `() => string` | Elsa 3 parity (short) |
| `stringToBase64(s)` | `(string) => string` | UTF-8 bytes → standard Base64 |
| `stringFromBase64(s)` | `(string) => string` | standard Base64 → UTF-8 string |
| `now()` | `() => string` | Current host time as ISO-8601 UTC |
| `toJson(v)` | `(json) => string` | Canonical JSON of `v` via the shared read-only encoder (ADR 0091) |

**Contract tests**: each returns a JSON string; `stringFromBase64(stringToBase64(x)) === x` for UTF-8 `x`; `newShortGuid()` output matches the Elsa 3 short-GUID algorithm for a known GUID; `toJson` output equals the canonical encoder's output for representative values (objects, arrays, strings needing no HTML-escaping divergence); `now()` parses as a UTC ISO-8601 instant equal to the controlled clock.

## C4 — Refused functions

Calling any of these is refused (at conversion for imported workflows; see the conversion contract): `parseGuid`, `bytesToString`, `bytesFromString`, `bytesToBase64`, `bytesFromBase64`, `streamToBytes`, `streamToBase64`, `stringToBase64`/`stringFromBase64` are **provided** (not refused), `getConfig`, `getConfiguration`.

> Note: of the Base64 family, only the **string** pair (`stringToBase64`/`stringFromBase64`) is provided; the **bytes**/stream Base64 helpers are refused (binary data is not a JSON value).

## C5 — Effect declaration (D-6, provisional)

- A function registered without a declaration is **effect-free**: no commits are placed around an input evaluation because it calls one.
- Registration takes one optional **effectful** flag. Only a function with it gets a commit before and after an input evaluation that calls it.
- The flag never refuses, skips or deduplicates a call. The C3 functions are registered without it. No other metadata, no effect profiles.

**Contract tests**: a function registered with the flag reports it on the registration; one registered without reports effect-free; a call runs exactly as often with or without the flag. **Open (OQ-1):** direction agreed by two approvers; the no-metadata position goes to the call if held.
