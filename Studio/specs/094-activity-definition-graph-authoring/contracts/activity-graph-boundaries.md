# Contract: Activity Graph Boundaries

## Public outcomes

All activity definition providers may expose any number of outcomes. New outcomes default to `isEmitted: true`. Non-emitted outcomes remain valid historical/compatibility members and do not require an implementation.

## Outcome mapping

For Activity Graph schema 2:

```text
graph source outcome ──0..1──> emitted public outcome
emitted public outcome <──1..*── graph source outcomes
```

Validation:

- reject duplicate source outcome mappings
- allow duplicate public target outcome mappings
- reject missing/unknown/non-emitted targets
- require at least one source mapping for each emitted public outcome
- retain explicit stable reference keys; do not infer by display name

Runtime:

- the graph emits one source outcome for an execution path
- resolve the mapping for that source
- publish the mapped public boundary outcome
- convergent sources may therefore publish the same public outcome

## Output mapping

Each public output has at most one expression:

- required output: exactly one valid expression
- optional output: zero or one valid expression

Expression scope contains public inputs, graph variables, and available graph/node outputs. Public inputs are referenced by stable public reference key and are not duplicated as graph variables.

## Legacy schema

Schema 1 has no explicit `outcomeMappings` collection. It remains readable/editable in its own contract. Operations needing explicit public outcome mappings require a reviewed schema 1→2 migration and cannot trigger that migration implicitly.
