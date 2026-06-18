# Research: Flowchart Scoped Execution

## Decision: Flowchart owns scoped execution state for v1

**Rationale**: The scoped execution tree is specific to Flowchart graph semantics in this feature. Keeping it on the Flowchart parent activity execution avoids introducing a new runtime primitive before other composites prove they need the same abstraction.

**Alternatives considered**:

- New global runtime scope primitive: rejected for v1 because it would broaden the runtime seam beyond Flowchart.
- Store all state on child executions: rejected because joins and races need parent-owned correlation and cancellation boundaries.

## Decision: Use generic execution path/scope metadata names

**Rationale**: Although Flowchart owns the scoped tree initially, child scheduling metadata should use `executionPathId` and `executionScopeId` because other composites such as Sequence, Parallel, and StateMachine may later use the same correlation concept.

**Alternatives considered**:

- `flowExecutionId` / `flowScopeId`: rejected because the names bind a reusable concept to Flowchart.
- Keep all IDs internal: rejected because tests, diagnostics, tracing, and future parallel scheduling need correlation.

## Decision: Public gateway policies from v1

**Rationale**: Custom routing and synchronization are likely first-class extension needs. A public policy seam prevents users from replacing the Flowchart engine to add custom graph behavior.

**Alternatives considered**:

- Internal policies first: rejected because the spec explicitly requires public extension points from v1.
- Service replacement contract for the whole engine: rejected because it is too coarse and risks fragmenting core Flowchart behavior.

## Decision: Policies are deterministic and command-returning

**Rationale**: Policies should decide behavior without mutating runtime state directly. The Flowchart engine applies commands, owns persistence, records diagnostics, and issues scheduling/cancellation through the runtime seam.

**Alternatives considered**:

- Policy mutates state directly: rejected because it weakens idempotency and makes tests harder.
- Policy receives scheduler/store APIs: rejected because it bypasses engine invariants.

## Decision: Runtime-aware dead-path detection

**Rationale**: Graph topology can identify structural reachability, but only runtime state can determine whether an execution path can still arrive in the current scope and loop iteration. Inclusive and implicit joins need both.

**Alternatives considered**:

- Graph-only reachability: rejected because active bookmarks/canceled paths matter.
- Runtime-only active-work inspection: rejected because structural paths and loop boundaries still matter.

## Decision: First Wins is interrupting by default

**Rationale**: The user-facing `First Wins` policy should be predictable: one branch wins and sibling competitors in the race scope are canceled. A future non-interrupting race should be a separate policy.

**Alternatives considered**:

- Configurable cancellation on the same policy: rejected because it makes the policy name ambiguous.
- Let losers finish by default: rejected because "First Wins" implies interrupting race behavior.

## Decision: True parallel execution is out of scope

**Rationale**: Flowchart must model safe parallelism and be order-independent, but actual concurrent worker execution depends on broader runtime scheduler, locking, idempotency, and conflict-retry guarantees.

**Alternatives considered**:

- Flowchart-owned threading/concurrency: rejected because it crosses into scheduler responsibility.
- Defer all parallel semantics: rejected because branch/join semantics are required even without true concurrent workers.
