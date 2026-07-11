# Research: Activity Input Editor Options

## Decision: Preserve the existing UI-specification bag

`InputDefinition` already carries opaque `JsonElement? UISpecifications`, the workflow-management descriptor response already projects it verbatim, and Studio already recognizes `uiSpecifications.options`. Static and provider metadata will therefore extend this JSON rather than add parallel descriptor fields.

**Rationale**: Additive wire compatibility and ADR 0035 compliance.

**Alternatives considered**: New top-level descriptor properties would duplicate the UI metadata contract; CLR-typed option bags would restore open-object serialization.

## Decision: Separate metadata identity from provider execution

`ActivityInputAttribute.OptionsProvider` stores a stable case-sensitive string key. `IActivityInputOptionsProvider` is a design-side keyed contribution that receives the current typed workflow state, selected activity node, and input definition.

**Rationale**: CLR scanning remains reflection-only, catalog rows do not persist implementation type names, and runtime activity projects do not depend on workflow-design packages.

**Alternatives considered**: Persisting `typeof(provider)` couples catalog data to loadable assemblies; client-only providers cannot safely access server-side module/configuration data.

## Decision: Use one canonical option shape

Static, inferred enum, and provider responses use ordered `{ label, value }` items. Values are JSON scalars; enums serialize by name. String shorthand expands to identical label/value strings.

**Rationale**: Studio already reads this shape and can preserve string, boolean, and number identity.

**Alternatives considered**: Parallel label/value arrays are error-prone; serialized JSON attribute strings are hard to validate and author.

## Decision: Cardinality supplies the default editor

Scalar option inputs default to dropdown; collection option inputs default to checklist. Explicit `checklist` claims the collection. Explicit `dropdown` on a collection opts out of the collection editor so the existing repeater renders one dropdown per row.

**Rationale**: The input type expresses single versus multiple values while the hint selects presentation. This preserves the existing collection repeater as an extensibility fallback.

**Alternatives considered**: A separate selection-mode flag duplicates type cardinality; always using checklist prevents authors from intentionally using row-based collection editing.

## Decision: Dynamic refresh is dependency-driven

Studio fetches on editor open and 150 ms after a declared dependency changes, cancels superseded requests, and does not cache responses. Missing/failing providers disable the editor and expose retry; stale values are shown and preserved.

**Rationale**: Avoid needless requests and silent workflow mutation while supporting dependent input lists.

**Alternatives considered**: Refreshing on every activity edit is noisy; open-only refresh becomes stale; free-text fallback defeats the allowable-set constraint.

## Decision: Fail ambiguous declarations during reconciliation

Static shorthand, repeatable typed options, and providers are mutually exclusive. Blank fields, duplicate values, incompatible types, dependencies without a provider, and dependencies that do not name sibling inputs are errors identifying the activity input.

**Rationale**: Invalid catalog metadata should never reach Studio, and no consumer should invent precedence rules.

**Alternatives considered**: Last-one-wins behavior is order-dependent and makes public metadata hard to reason about.
