# Research: Contributed Runtime Intent Handlers

## Decision 1: Use a typed handler contribution behind the existing dispatcher contract

**Decision**: Add a public intent-handler contract for the async handling operation and typed contribution metadata carrying the stable intent kind plus handler type. Modules register the `(kind, handler type)` pair through one service-collection extension. The existing `IRuntimePostCommitIntentDispatcher` remains the outbox processor’s replacement seam, with its default implementation becoming a composite over the contributed handlers.

**Rationale**: This preserves the outbox processor and provider-facing contracts while allowing multiple modules to extend delivery without replacing one another. Scoped handler resolution matches the existing scoped outbox processor and permits handlers to use scoped runtime services.

**Alternatives considered**:

- Add a switch statement to the current scheduler dispatcher: rejected because each DispatchWorkflow slice would deepen a central type and couple Runtime to activity modules.
- Register multiple dispatchers: rejected because `IRuntimePostCommitIntentDispatcher` is a replacement contract, not fan-in, and enumerable dispatchers would make ownership and conflicts ambiguous.
- Key DI registrations directly by string: rejected because the repository’s public composition patterns use typed contribution contracts and deterministic validation rather than container-specific keyed-service behavior.

## Decision 2: Validate a deterministic handler map at composite construction

**Decision**: Normalize contributions by `(ordinal intent kind, handler type)`, collapse identical entries, sort identities ordinally, and fail if more than one distinct handler type claims a kind. Unsupported kinds throw an actionable delivery exception from the composite.

**Rationale**: Composition order cannot affect the winner, repeated module activation remains idempotent, and conflicts are visible before any handler for the conflicting map is selected. Throwing from dispatch preserves the existing outbox processor’s safe failure path and lets the existing retry policy select the persisted failed state.

**Alternatives considered**:

- First or last registration wins: rejected because module load order would silently change correctness.
- Invoke all handlers for a kind: rejected because the issue requires exactly one owner per named intent kind, not fan-out observation.
- Silently acknowledge unknown kinds: rejected because it loses durable work.

## Decision 3: Move scheduler delivery into the same contribution mechanism without changing its body

**Decision**: Adapt the current scheduler dispatcher implementation into the built-in handler for `RuntimePostCommitIntentKinds.EnqueueSchedulerWork`, retaining its payload deserialization, validation, workflow identity check, and queue enqueue logic unchanged.

**Rationale**: A single mechanism must serve built-ins and modules. Keeping the validated body intact minimizes regression risk and protects persisted identities.

**Alternatives considered**:

- Give scheduler work a privileged fast path in the composite: rejected because it creates the dual registration mechanism prohibited by #675.

## Decision 4: Let the global resumption service process all deliverable intent kinds

**Decision**: Remove its explicit `EnqueueSchedulerWork` query filter. The outbox store already accepts a null kind filter and returns deliverable work in its established order; the composite dispatcher routes each item by kind.

**Rationale**: Child-start and parent-resume work must be delivered by the global pump outside actor mailboxes. Filtering by scheduler kind would strand valid contributed work.

**Alternatives considered**:

- Add one processor call per known kind: rejected because the resumption service would need to discover the registry and batch limits/order would become kind-dependent.
- Process contributed work inside workflow drains: rejected because cross-execution parent/child dependencies can deadlock actor mailboxes and contradict #674.

## Decision 5: Prove the seam through a committed marker intent

**Decision**: Add an in-memory integration guardrail that commits a marker intent through the real checkpoint commit store, verifies it becomes outbox work, runs a real resumption sweep, and asserts one marker invocation and delivered status.

**Rationale**: Constructor or DI descriptor tests alone cannot prove the global pump stopped filtering contributed kinds.

**Alternatives considered**:

- Invoke the composite dispatcher directly: retained as a focused unit test but insufficient as the integration guardrail.
- Require Groundwork for #675: rejected because provider durability is unchanged and Groundwork dispatch-specific crash coverage belongs to #678.
