# Research: Reusable Activity Boundary Outcomes

## Decision 1: Add manifest schema 2

**Decision**: Keep schema 1 unchanged and add schema 2 with an `outcomeMappings` array.

**Rationale**: Schema 1 explicitly guarantees exactly one emitted `done` outcome. Relaxing that rule would silently change persisted schema-1 semantics. A new schema makes the capability opt-in and reversible.

**Alternatives considered**: Relaxing schema 1 was rejected for compatibility. Inferring mappings by matching names was rejected because references are the stable design identity. Explicit return nodes were rejected as a larger control-flow feature deferred by spec 092.

Schema-1 drafts can migrate to schema 2 by adding an empty mapping collection. The migrated draft remains intentionally unpublished until the author supplies total mappings; no runtime behavior is guessed.

## Decision 2: Map stable references and compile runtime names

**Decision**: Each authored mapping contains `sourceOutcomeReferenceKey` and `boundaryOutcomeReferenceKey`. The source is implicitly the graph's resolved direct entry occurrence. Compilation resolves both references to their contract outcome names and emits `sourceOutcomeName`/`boundaryOutcomeName` in the runtime descriptor.

**Rationale**: This follows the design/runtime split. Reference keys survive authoring changes; runtime names are exactly what child completion and parent routing use.

**Alternatives considered**: A source node id was rejected because only the direct entry completes the boundary in this work unit. Design references in the runtime descriptor were rejected because runtime must not resolve design contracts.

## Decision 3: Require total, source-unambiguous mappings

**Decision**: For schema 2, every emitted public boundary outcome has at least one mapping; every source reference is emitted by the resolved entry dependency and remains unique. Distinct source outcomes may converge on the same public boundary outcome.

**Rationale**: Total mappings make declared boundary results truthful, while source uniqueness ensures a child result selects at most one parent result. Target convergence intentionally collapses several internal results into one stable public result. Dependency-aware validation proves reachability against the exact published dependency contract.

**Alternatives considered**: Implicit `done` fallback was rejected because it hides contract errors. Requiring unique targets was rejected because convergence is deterministic and does not imply aggregation within a single execution.

## Decision 4: Preserve the selected outcome through checkpointing

**Decision**: `GraphActivity` stores the mapped boundary outcome on its activity instance after child completion and uses that same value in `PrepareCompletionCheckpointAsync`.

**Rationale**: The runtime uses the same structural activity instance for the child-completion callback and checkpoint and verifies outcome equality. This is transient execution state, not durable workflow data.

**Alternatives considered**: Persisting or recomputing the child outcome was rejected because it would expand runtime-core persistence without need.

## Decision 5: Reuse the generic outcomes design facet

**Decision**: Publication adds an `elsa.outcomes` schema-1 design facet whose `ports` carry emitted public contract outcome reference keys and names. Source-owned publication contracts and the authoring catalog use the same facet, falling back to the port name for older CLR facets that predate explicit reference keys.

**Rationale**: CLR-scanned activities already use this portable facet and Studio renders it generically. A shared name fallback keeps existing CLR outcomes such as `True` and `False` authorable while keeping dependency validation and Studio choices aligned.

**Alternatives considered**: Studio inspection of graph manifests was rejected because it duplicates provider logic.

## Decision 6: Add a schema-2 Studio contribution

**Decision**: Studio registers a separate exact schema-2 graph contribution, preserves `outcomeMappings` during graph edits, receives the current draft contract, and offers mapping controls limited to the direct entry activity's catalog outcomes and emitted public outcomes.

**Rationale**: The exact schema-1 contribution cannot edit unknown schemas and its normalizer would discard the field. Passing the draft contract is the smallest safe SDK expansion.

**Alternatives considered**: Server-only delivery was rejected because Studio users could not author mappings. Free-text public keys and a raw JSON editor were rejected as avoidably unsafe or too broad.
