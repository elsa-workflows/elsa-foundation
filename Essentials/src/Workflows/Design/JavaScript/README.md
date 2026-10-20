# Elsa.Workflows.Design.JavaScript

Bridges the workflow design surface and the JavaScript expression engine. Canonical input-binding expressions use a closed binding-pure capability profile, so the editor exposes only the immutable `args` parameter map available to the isolated runtime evaluator. JavaScript is published under `binding-pure-v2` (ADR 0088): `binding-pure-v1` plus a `Date` without a clock.

## Time, dates and unique IDs (`binding-pure-v2`)

Declare what an expression needs as parameters: `{"kind":"Clock"}` gives the evaluation instant as an ISO 8601 UTC string (`args.now`), and `{"kind":"UniqueId"}` gives a new lowercase UUID version 7 (`args.orderId`). A retry sees the same values; each `While` or `Do` pass gets new ones. UUID version 7 values are unique and time-ordered, not secret.

| `Date` | |
|---|---|
| Allowed | `new Date(isoString)`, `new Date(epochMs)`, `new Date(y, m, ...)`, `Date.UTC`, `Date.parse`, the getters and setters, `getTime`, `valueOf`, `toISOString`, `toJSON`, `toString`, comparison and arithmetic |
| Constrained | UTC on every host (local getters equal UTC getters); only ECMAScript ISO 8601 date-time strings parse, other strings give an invalid date |

| Removed form | Write instead |
|---|---|
| `new Date()` | declare a `Clock` parameter, `new Date(args.now)` |
| `Date()` | `new Date(args.now).toString()` |
| `Date.now()` | `Date.parse(args.now)` |
| `toLocaleString()` | `toISOString()`, or the UTC getters |
| `toLocaleDateString()` | `toISOString().slice(0, 10)` |
| `toLocaleTimeString()` | `toISOString().slice(11, 23)` |

Each removed form fails with exactly that guidance at runtime, and authoring validation reports it before publish. `Temporal`, `Intl` and `Math.random` stay unavailable. The full contract is in [spec 260](../../../../../specs/260-expression-clock-and-unique-id/contracts/clock-uniqueid-and-binding-pure-v2.md).

## Cross-domain contributions

This feature implements contributor interfaces from other domains:

- **`IJavaScriptDeclarationContributor`** *(Core — `Elsa.Expressions.JavaScript.Rendering.Core`)* — `BindingPureArgsDeclarationContributor` contributes the single read-only `args` global. Ambient workflow functions, configuration helpers, variable mutation, and output lookup are intentionally absent because they do not exist in the runtime capability profile. Aggregated by `BuildDeclarationsDocument` in `Elsa.Expressions.JavaScript.Rendering`.
  - Known impl: `BindingPureArgsDeclarationContributor`.
  - Catalog: [`Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md`](../../../Expressions/JavaScript/Rendering/Elsa.Expressions.JavaScript.Rendering/EXTENSION_POINTS.md)
