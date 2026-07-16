# Publication Contract

## Compile contribution

Publishing emits one named Sequential contribution event after activity-node compilation and before hashing. One Publishing-owned handler aggregates all `IExecutableCompilationSource` registrations in deterministic source-identity order. Sources receive immutable compile context, return node metadata/dependency claims, never mutate the tree, and may abort compilation with a domain diagnostic.

## Dispatch target resolution

For every DispatchWorkflow node, publication:

1. Reads one literal nonblank workflow definition ID.
2. Resolves it under the publication request's explicit tenant scope and rejects cross-tenant candidates.
3. Requires exactly one live Published source reference.
4. Loads and verifies the referenced executable.
5. Requires a versioned child input contract and trustworthy dependency graph; legacy targets must be recompiled/republished.
6. Contributes one exact child dependency/node binding.

Missing, stale, inaccessible, unpublished, ambiguous, or inconsistent targets abort compilation and prevent activation.

## Input validation

The child executable's `InputContract` is authoritative.

- Literal object maps are fully validated at parent compilation through the shared `TypeReference` and well-known type registry.
- Expression-backed maps receive structural compile checks and mandatory runtime validation before checkpoint staging.
- Supported literal defaults are materialized into the normalized child input bag; unknown aliases and unsupported defaults fail closed.
- A legacy child with no contract remains readable/executable but is not eligible as a new strict dispatch target.
- Diagnostics identify node/input/reason but never echo raw values.

## Canonical identity

Hasher input is the canonical node tree, versioned declared-input contract, and direct dependencies sorted by child artifact ID with sorted unique node bindings.

Source reference/publication/slot identity, timestamps, access context, layout, retirement, and runtime deny state never enter the behavioral hash. A reachable child behavior change propagates through its child hash into the parent hash. Equivalent orderings and diamond graphs remain stable.

## Graph validation

Before activation, Publishing loads and validates reachable child graphs using full artifact ID/hash pairs, rejects missing artifacts/hash mismatches/conflicting edges/malformed repeated identities, computes the candidate identity, and defensively rejects that full identity if found in its closure. The diagnostic path is deterministic. Repeated definition IDs across distinct artifacts remain legal.

## Atomicity and compatibility

Resolution-time liveness and tenant visibility select the immutable pin. A source replacement/unpublication after resolution never retargets or invalidates it. Artifact save and source activation extend existing publication/lease behavior to the exact dependency closure, so the selected artifact cannot be collected during activation. Failure before root creation exposes no live partial parent. Existing public overloads remain; old artifacts deserialize with `InputContract = null` and empty dependencies but cannot become new strict targets. Compiler goldens intentionally change because the new material is behavioral.

## Exclusions

No WorkflowDefinitionActivity behavior, Studio, broker transport, waited completion, lifecycle/cancellation, redrive, test-scope dispatch, or distributed placement.
