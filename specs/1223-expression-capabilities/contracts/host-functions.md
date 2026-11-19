# Contract: Expression capability surface & functions

The capabilities and functions available to Elsa 4 JavaScript expressions under `binding-readonly-v1`, and their behavioural contract. Semantic contract for review; signatures are illustrative, not final code.

## C1 — One surface, two entry points

- A single engine configuration serves input-binding expressions **and** `RunJavaScript` scripts. Any capability (intrinsic or function) granted to one is granted to the other.
- Writes to engine-owned state are not part of this surface: `RunJavaScript` writes variables as ADR 0090 and spec 1231 define; input bindings never write.
- By default (no .NET interop strategy installed), no live .NET objects cross the surface (ADR 0046 V10).

## C2 — Native intrinsics

- `Date`, `Temporal`, `Intl`, `Math` (incl. `Math.random`) are available and behave as standard JavaScript.
- `new Date()`, `Date.now()` and `Temporal.Now.*` return the **host clock** time. The engine runs with `TimeZone = UTC` and `Culture = Invariant`. A converted Elsa 3 expression with a recorded source time zone evaluates with that zone as its local time zone (conversion contract D1a).
- `Temporal` and `Intl.DateTimeFormat` accept every IANA time zone ID the host knows; they are the built-in date helpers (FR-025).
- `Math.random` uses the engine's native RNG. **No reproducibility guarantee.**

**Contract test cues**: with a controlled `TimeProvider`, `new Date().toISOString()`, `Date.now()` and `Temporal.Now.instant()` reflect the provided instant; `Intl`/`Temporal` are defined; `Temporal.Instant.from('2026-01-15T08:15:00Z').toZonedDateTimeISO('Europe/Amsterdam').hour === 9` and the July equivalent gives `10`; two `Math.random()` calls are permitted to differ.

## C3 — Built-in functions (JSON in / JSON out)

| Function | Signature (illustrative) | Behaviour |
|---|---|---|
| `newGuid()` | `() => string` | New GUID, canonical `D` text |
| `newGuidString()` | `() => string` | New GUID, canonical `D` text |
| `getGuidString()` | `() => string` | New GUID, canonical `D` text |
| `newShortGuid()` | `() => string` | New GUID, Elsa 3 short form: standard Base64 of `ToByteArray()` with `/`, `+`, `=` removed |
| `getShortGuid()` | `() => string` | As `newShortGuid` |
| `parseGuid(s)` | `(string) => string` | `D` text of `Guid.Parse(s)`; invalid text fails the evaluation |
| `stringToBase64(s)` | `(string) => string` | UTF-8 bytes → standard Base64 |
| `stringFromBase64(s)` | `(string) => string` | standard Base64 → UTF-8 string; invalid Base64 fails the evaluation, invalid UTF-8 gives U+FFFD |

**Contract tests**: each returns a string; `newShortGuid` for a fixed GUID equals Elsa 3's algorithm output; `parseGuid('{6F9619FF-8B86-D011-B42D-00CF4FC964FF}')` returns `6f9619ff-8b86-d011-b42d-00cf4fc964ff`; `stringFromBase64(stringToBase64(x)) === x` for UTF-8 `x`; no `toJson` global exists.

## C4 — Refused functions

Calling any of these is refused (at conversion for imported workflows; see the conversion contract): `bytesToString`, `bytesFromString`, `bytesToBase64`, `bytesFromBase64`, `streamToBytes`, `streamToBase64` (binary data is not a JSON value), `getConfig`, `getConfiguration` (deferred).

## C5 — Registration (D-5, D-6, ADR 0083, ADR 0089)

- One registration per function: name, `ReadsVariables`, implementation. It extends spec 1222's `JavaScriptHostFunctionDeclaration`.
- A registered function is allowed in `binding-readonly-v1`.
- **Names**: the read roots (`variables`, `input`, `activities`, `workflow`, `args`), built-in names and registered namespace roots are reserved; host functions are namespace-prefixed (`acme.lookup`, installed as a read-only global object `acme`); a duplicate or reserved registration fails at startup naming the function. A script declaring `variables` gets the existing publish **error** `Scripts/ReservedName`; `args` follows spec 1222 and `complete` keeps spec 1208's error; declaring `input`, `activities`, `workflow`, a built-in name or a namespace root gets the publish **warning** `Scripts/ShadowedFunction`.
- **Effect**: none in this spec. Every function is effect-free under D-6 and calling it adds no commit boundary; the effectful declaration ships with its boundary (#4831).
- **Reads**: the C3 functions have `ReadsVariables = false`, so calling them does not make publish report an expression as possibly reading every variable.
- **Arguments**: script values map to the value model as a script result does (`Date` → ISO 8601 UTC text, `undefined` → `null`).
- **Precision**: a number argument that carries the value model's loss information logs a warning naming the function and the argument path; the call runs (ADR 0046 V9).

**Contract tests**: a duplicate name fails startup; an unprefixed host function fails startup; `const newGuid = 1` warns at publish (`Scripts/ShadowedFunction`); `const variables = 1` is a publish error (`Scripts/ReservedName`); calling `newGuid()` adds no fallback read marker; a lossy number argument logs the warning and the call returns.
