# Runtime Contract

## Start authority and order

Starts use either existing **LiveReference** authority or internal **RetainedDependency** authority. The latter contains exact parent artifact ID/hash and dispatch node ID; Runtime loads the parent and permits only the child ID/hash bound to that node. This dependency provenance is copied into child start/state inspection. It does not require historical child source provenance to remain live and cannot be supplied by public endpoints.

Before actor lookup, enqueue, or state materialization, Runtime:

1. Loads the child executable.
2. Validates start authority.
3. Validates its input contract when present.
4. Validates non-negative depth and the configured maximum.
5. Evaluates `IWorkflowExecutableStartPolicy`.
6. Enqueues only after every gate allows.

Rejection is domain-scoped and machine-classifiable and creates no execution state.

## Input isolation

Only contract-approved values and materialized supported defaults enter workflow `Inputs`. A declared name such as `tenant` remains an ordinary input and never writes variables, stimulus/trigger identity, execution/idempotency identity, lineage, tenant, partition, authority, run kind, or nesting depth. Those remain typed Runtime-owned fields. Rejected raw values are not retained.

## Retained child payload

A committed child start carries child identity, parent artifact ID/hash, dispatch node ID, deterministic child/start identity, validated/defaulted inputs, typed lineage/authority/partition/run kind, and stable nesting depth. `ChildStartExecutor` reconstructs retained-dependency authority and never falls back to the broad legacy reference-less path. Redelivery reuses the same identity, authority, and depth.

## Start policy

`IWorkflowExecutableStartPolicy` is one explicit replacement contract. The default allows; a host replacement may deny using immutable start context and must return a stable reason code plus safe message. Evaluation occurs before materialization. Multiple registrations are a startup conflict, not an ordered/last-wins chain. Decisions do not alter artifacts or in-flight executions.

## Depth

- Root/legacy: 0.
- First child: 1.
- Default maximum allowed child: 32.
- Depths 1–32 succeed; attempted 33 fails before checkpoint staging.
- Child executor/dispatcher recheck the stored value against corrupt payloads.
- No delivery/replay layer increments depth.

## Retention

Every live source or retained execution root protects its full dependency closure. Root creation leases sorted distinct closure IDs. Partial acquisition releases held leases and writes no root. GC recomputes closure under final deletion guards. Missing/cyclic/inconsistent graphs and store failures retain rather than delete. The final removed root makes the now-unreachable closure eligible subject to grace/concurrency.

## Compatibility and observability

Missing depth defaults to 0, missing dependencies to empty, and missing input contract to legacy permissive direct-execution behavior. Such a legacy artifact is rejected only when selected as a new strict dispatch target. Existing overloads delegate to additive defaults. Safe reason categories cover invalid target/input/authority/policy/depth/graph/lease without logging raw inputs.

## Exclusions

No waiting parent, terminal observation, cancellation propagation, redrive, test-scope dispatch, broker selection, or distributed owning-node placement.
