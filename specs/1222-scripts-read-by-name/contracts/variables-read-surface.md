# Contract: The `variables` read surface, publish read-set and run-time fallback

**Spec**: [../spec.md](../spec.md) · Provisional (initiative #3052). Amends spec 095 expression contract §1 and §6.
Implements decision D-7 (minimal ADR 0089 amendment).

## Authoring surface

A script reads a workflow variable by name:

- JavaScript literal: `variables.X`, `variables['X']` (string literal), `getVariable('X')` (string literal argument).
- JavaScript computed: `variables[expr]`, `getVariable(expr)` (non-literal).
- Liquid: `{{ variables.X }}`; computed access where Liquid allows it.
- Host functions that read variables not named in source (opaque reads).

`input.X` and `workflow.correlationId` / `workflow.instanceId` continue to read as today (via `args`). The
`activities.<name>.<output>` root is NOT provided by this contract (out of scope).

## Publish (name resolution and validation)

1. Parse the source with the language parser (no regex). Collect literal variable names; detect computed access.
2. Resolve each literal name to its nearest declaring scope: iteration scope → enclosing container → outer containers →
   workflow; innermost declaration shadows an outer one.
3. For each resolved literal name, add a pinned `Variable` parameter with `surface = variables`, recording
   `declaringScopeNodeId` + `variableKey`. The authored source is not rewritten.
4. For an expression with computed or opaque reads, add the fallback marker with the conservative read report (the
   variables visible at that point). Publish succeeds.
5. Refuse (publish diagnostic) when:
   - a literal name resolves to no visible declaration (spec 1216 FR-027);
   - a literal name resolves to a transient (non-durable) variable (retained rule).
6. Warn when a literal name now resolves to a different declaration than in the previously published version; warn
   when a computed read's candidates include a protected or a transient variable.
7. A literal read of a protected (secret) variable is protection-checked here (the name is known).

## Run time (evaluation)

- The evaluator installs a frozen `variables` object. A pinned name returns its pinned (snapshot) value; an unset
  declared variable reads JSON `null` (never `undefined`).
- On an expression with the fallback marker, the candidate set is made readable before evaluation. A computed or opaque
  read then resolves the name nearest-scope-first over the candidates, is protection-checked for the variable actually
  read, and is recorded (name, declaring scope, present/absent).
- A computed name that matches no visible variable reads `undefined` in JavaScript and nil in Liquid, records a warning,
  and is recorded as absent (spec 1216 FR-025/FR-026).
- A withheld (no-permission) value is not provided; the step faults with the protection diagnostic.
- A computed name that resolves to a transient variable faults the step with the transient-read diagnostic (OQ-3).
- The recorded reads are returned with the evaluation result and stored in the step's input snapshot.
- An expression without the marker loads only the variables it names; there is no frame-chain projection for it.
- `getVariable` remains as a runtime function (literal argument → pinned value; otherwise → fallback). Generated
  `get<Name>()` accessors are not provided.
- The `args` object is built from the remaining parameters, as a separate object.
- Secret values are redacted from any log, diagnostic or recorded-read output.

## Input snapshot vs RunJavaScript body session

- Input-binding expressions read the step's input snapshot (pinned values plus recorded reads) and never see pending
  writes.
- The `RunJavaScript` body reads — literal and computed — through its variable **session** view (ADR 0092), which shows
  the body's own pending writes. Its recorded reads still enter its input snapshot. Writes are ADR 0090 / S-7, out of
  scope here.

## spec 095 contract changes (recorded; flow to #3420 / spec 095)

- **§1**: the compiled binding's parameter gains a `surface` flag (`variables` | `args`, default `args`); the compiled
  binding gains an optional fallback marker carrying the conservative read report; the evaluation request exposes a
  `variables` root over the `surface = variables` parameters plus the fallback lookup; the evaluation result returns the
  recorded reads.
- **§6**: the importer keeps `variables.X` (does not lower it to `args.X`) and keeps computed `getVariable(expr)` /
  `variables[expr]` (served by the fallback) instead of failing on them; it still lowers `input`, activity results and
  instance metadata to `args`.
