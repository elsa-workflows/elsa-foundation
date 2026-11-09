# Contract: The `variables` read surface and publish read-set

**Spec**: [../spec.md](../spec.md) · Provisional (initiative #3052). Amends spec 095 expression contract §1 and §6.

## Authoring surface

A script reads a workflow variable by a **direct name**:

- JavaScript: `variables.X`, `variables['X']` (string literal), `getVariable('X')` (string literal argument).
- Liquid: `{{ variables.X }}`.

`input.X` and `workflow.correlationId` / `workflow.instanceId` continue to read as today (via `args`). The
`activities.<name>.<output>` root is NOT provided by this contract (out of scope).

## Publish (name resolution and validation)

1. Parse the source with the language parser (no regex). Collect direct variable names.
2. Resolve each name to its nearest declaring scope: iteration scope → enclosing container → outer containers →
   workflow; innermost declaration shadows an outer one.
3. For each resolved name, add a pinned `Variable` parameter with `surface = variables`, recording
   `declaringScopeNodeId` + `variableKey`. The authored source is not rewritten.
4. Refuse (publish diagnostic) when:
   - a name resolves to no visible declaration;
   - the source uses a computed name (`variables[expr]`, `getVariable(expr)`, or passes `variables`/`getVariable` as a
     value);
   - a name resolves to a transient (non-durable) variable.
5. Warn when a name now resolves to a different declaration than in the previously published version.
6. A direct read of a protected (secret) variable is protection-checked here (the name is known).

## Run time (evaluation)

- The evaluator installs a frozen `variables` object built from pinned parameters whose `surface = variables`; one
  property per name, value = pinned (snapshot) value; unset declared variable → JSON `null` (never `undefined`).
- The `args` object is built from the remaining parameters, as a separate object.
- There is NO run-time variable name lookup, no frame-chain projection, and no `getVariable` / `get<Name>()` runtime
  function.
- Only the variables a script names are materialized (not all visible).
- Secret values are redacted from any log/diagnostic output; a withheld (no-permission) value is not provided.

## RunJavaScript body

The body reads through its variable **session** view (ADR 0092), which equals the pinned input snapshot until writes
exist (writes are ADR 0090 / S-7, out of scope here).

## spec 095 contract changes (recorded; flow to #3420 / spec 095)

- **§1**: the compiled binding's parameter gains a `surface` flag (`variables` | `args`, default `args`); the evaluation
  request exposes a `variables` root over the `surface = variables` parameters. (No reader port / run-time frame is
  introduced — reads stay pinned.)
- **§6**: the importer keeps `variables.X` (does not lower it to `args.X`); it still lowers `input`, activity results and
  instance metadata to `args`, and still fails rather than guess on computed names.
