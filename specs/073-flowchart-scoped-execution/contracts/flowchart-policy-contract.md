# Contract: Flowchart Gateway Policy

## Purpose

Flowchart gateway policies are public extension points that decide routing, synchronization, loop, merge, and cancellation behavior for Flowchart graph nodes and connections.

## Policy Registration

A policy is registered with:

- `policyKind`: stable string identifier stored in Flowchart structure metadata.
- `displayName`: user-facing policy name.
- `supportedNodeRoles`: optional hints for authoring/validation.

Built-in policy kinds:

- `decision`
- `parallelFork`
- `parallelJoin`
- `inclusiveFork`
- `inclusiveJoin`
- `firstWins`
- `merge`
- `implicitActivationJoin`
- `directContinuation`

## Policy Input

Policies receive a read-only decision context containing:

- Flowchart graph topology.
- Current execution scopes.
- Current execution paths.
- Relevant arrivals.
- Active child summaries.
- Triggering event: start, child completed, child canceled, child faulted, or resume.
- Completed outcome names when applicable.
- Approved read-only helper services such as reachability evaluation.

Policies must not receive:

- Mutable runtime state.
- Raw persistence stores.
- Direct scheduler APIs.
- Design-time workflow documents.

## Policy Output

Policies return commands for the Flowchart engine to apply:

- create scope
- create execution path
- move execution path
- wait execution path
- complete execution path
- cancel execution path
- schedule node in scope
- record arrival
- consume arrival
- cancel scope or branch subtree
- complete scope
- write diagnostic event

## Invariants

- Policies decide; the Flowchart engine mutates state.
- Policies must be deterministic for the same context.
- Policies must not schedule missing nodes or reference missing scopes.
- Policies must not consume an arrival more than once.
- Policies must not cancel work outside their permitted scope boundary.

## Failure Behavior

When a policy returns invalid or conflicting commands:

1. The Flowchart engine rejects the decision.
2. The active execution path faults or the Flowchart records an incident according to runtime failure behavior.
3. A policy failure diagnostic is recorded with the policy kind, node, scope, and reason.

## Documentation Obligations

Public policy contracts must be documented in:

- `src/elsa/Activities/Flowchart/EXTENSION_POINTS.md`
- root `EXTENSION_POINTS.md`
- generated extension-point maps after implementation
